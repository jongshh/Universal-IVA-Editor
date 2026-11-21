using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UniversalIVAEditor
{
    public sealed class MASRuntime : MonoBehaviour
    {
        Part owner;
        InternalModel model,retiringModel;
        PartModule computer,retiringComputer;
        ConfigNode profile;
        string profileHash,computerWarning;
        bool terminal;
        int retireFrame;
        InternalModule invokingProp;
        readonly HashSet<InternalModule> pending=new HashSet<InternalModule>();
        readonly HashSet<InternalModel> retiredModels=new HashSet<InternalModel>();
        public bool Manages(InternalModule module) => module!=null && (module.internalModel==model || retiredModels.Contains(module.internalModel));
        public void Wake() { if(!terminal) enabled=true; }
        public void Warning(string key)
        {
            if(invokingProp!=null) MASBootstrap.Run(invokingProp).Warning=true; else computerWarning=key;
        }
        public void Configure(Part part,InternalModel nextModel,ConfigNode nextProfile)
        {
            owner=part;
            string hash=Configuration.Signature(nextProfile.GetNode("MODULE"));
            var native=MASBootstrap.Find(part).LastOrDefault(m=>!MASBootstrap.Owned(m));
            if(computer!=null && MASBootstrap.Owned(computer) && (profileHash!=hash || native!=null)) Retire();
            if(model!=null && model!=nextModel) retiredModels.Add(model);
            model=nextModel; profile=Configuration.Copy(nextProfile); profileHash=hash; terminal=false; computerWarning=null;
            pending.Clear();
            if(native!=null && retiringComputer!=native) computer=native;
            if(part.FindModuleImplementing<ModuleCommand>()==null) { Fail("ModuleCommand is required by MAS; no command module was added"); return; }
            var deleted=model.GetComponent<DeletedPropVisibility>();
            foreach(var module in model.GetComponentsInChildren<InternalModule>(true))
                if(MASBootstrap.Api.PropStarts.ContainsKey(module.GetType()) && !(deleted?.Contains(module.transform)??false)) Queue(module);
            Log.Trace("MAS automatic profile part="+part.partInfo.name+" source="+(profile.GetValue("source")??"snapshot")+" donor="+(profile.GetValue("sourcePart")??"common defaults"));
            IntegrationState.Report(part,"MAS profile","Automatically configured",native!=null?"Existing computer retained":"Computer configuration prepared without additional setup");
            enabled=true;
        }
        public void Queue(InternalModule module)
        {
            var run=MASBootstrap.Run(module);
            if(terminal) { run.Failed=true; return; }
            if(run.Attempted || run.Failed) return;
            pending.Add(module); enabled=true;
        }
        void Retire()
        {
            if(computer==null || !MASBootstrap.Owned(computer)) return;
            retiringComputer=computer; retiringModel=model; retireFrame=Time.frameCount+1;
            computer=null;
        }
        public void Stop()
        {
            if(model!=null) retiredModels.Add(model);
            Retire(); model=null; profile=null; pending.Clear(); terminal=false; enabled=true;
        }
        void Fail(string reason)
        {
            terminal=true; pending.Clear(); enabled=false;
            IntegrationState.Report(owner,"MAS","Initialization unavailable",reason);
            if(computer!=null && MASBootstrap.Owned(computer) && !MASBootstrap.Api.Ready(computer)) { var failed=computer; computer=null; owner.RemoveModule(failed); }
        }
        void Update()
        {
            try { Tick(); }
            catch(Exception e)
            {
                Fail(e.GetBaseException().Message); Log.Error("MAS runtime initialization",e);
            }
        }
        void Tick()
        {
            if(owner==null || !HighLogic.LoadedSceneIsFlight) { enabled=false; return; }
            retiredModels.RemoveWhere(m=>m==null);
            if(retiringComputer!=null)
            {
                if(Time.frameCount<=retireFrame || retiringModel!=null) return;
                var retired=retiringComputer; retiringComputer=null; owner.RemoveModule(retired); retiringModel=null;
            }
            if(profile==null || model==null || !IntegrationState.Owns(owner)) { enabled=false; return; }
            if(terminal) { enabled=false; return; }
            if(owner.vessel==null || !owner.vessel.loaded || !owner.started || owner.persistentId==0 || owner.GetComponent<RuntimeState>()?.Initialized!=true || !(bool)MASBootstrap.Api.PersistentsLoaded.GetValue(null,null)) return;
            if(computer==null)
            {
                var existing=MASBootstrap.Find(owner);
                if(existing.Length>1) { Fail("Multiple native MAS computers found; no additional computer was created"); return; }
                computer=existing.LastOrDefault();
                if(computer==null)
                {
                    var config=MASConfiguration.Sanitize(profile.GetNode("MODULE")); config.AddValue("flightComputerId",MASConfiguration.ComputerId(owner.persistentId));
                    computer=owner.AddModule(config,true);
                    if(computer==null) throw new InvalidOperationException("Cannot create MASFlightComputer");
                    MASBootstrap.RegisterOwned(computer,config);
                }
            }
            var state=MASBootstrap.Run(computer);
            if(!MASBootstrap.Api.Ready(computer))
            {
                if(state.Attempted || state.Failed) { Fail("MAS computer did not finish initialization; see KSP.log"); return; }
                if(!state.Begin(true)) { Fail("MAS computer initialization is already in progress"); return; }
                try { MASBootstrap.Api.ComputerStart.Invoke(computer,null); }
                finally { state.Complete(!state.Failed && MASBootstrap.Api.Ready(computer)); }
                if(!MASBootstrap.Api.Ready(computer)) { state.Failed=true; Fail("MAS computer Lua/proxy initialization failed"); return; }
            }
            IntegrationState.Report(owner,"MAS computer",computerWarning==null?"Connected / actual operation unverified":"Connected with configuration warnings",(MASBootstrap.Owned(computer)?"Generated":"Native reused")+" ID="+MASBootstrap.Api.ComputerId.GetValue(computer));
            foreach(var module in pending.ToArray())
            {
                pending.Remove(module);
                if(module==null || module.internalModel!=model) continue;
                var run=MASBootstrap.Run(module);
                if(run.Attempted || run.Failed) continue;
                if(!run.Begin(true)) continue;
                run.Computer=computer; invokingProp=module;
                try { MASBootstrap.Api.PropStarts[module.GetType()].Invoke(module,null); }
                catch(Exception e) { run.Failed=true; IntegrationState.Report(owner,MASBootstrap.Key(module),"Prop initialization failed",e.GetBaseException().Message); }
                finally { run.Complete(!run.Failed); invokingProp=null; }
                if(run.Failed && MASBootstrap.Api.PropDestroys.TryGetValue(module.GetType(),out var cleanup))
                    try { cleanup.Invoke(module,null); } catch(Exception e) { Log.Trace("MAS failed prop cleanup: "+e.GetBaseException().Message); }
            }
            AuditSources(); enabled=false;
        }
        void AuditSources()
        {
            if(owner.vessel==null) return;
            var modules=owner.vessel.parts.SelectMany(p=>p.Modules.Cast<PartModule>()).ToArray();
            var definitions=PresetStore.Library;
            var pages=new HashSet<string>(StringComparer.Ordinal);
            foreach(var prop in model.props) if(prop!=null && definitions.Props.TryGetValue(prop.propName??"",out var definition))
                foreach(var monitor in definition.GetNodes("MODULE").Where(n=>n.GetValue("name")=="MASMonitor")) foreach(var name in monitor.GetValues("page")) pages.Add(name);
            bool camera=GameDatabase.Instance.GetConfigs("MAS_PAGE").Any(p=>pages.Contains(p.config.GetValue("name")) && p.config.GetNodes("CAMERA").Length>0);
            if(camera && !modules.Any(m=>m.GetType().FullName=="AvionicsSystems.MASCamera")) IntegrationState.Report(owner,"MAS camera pages","Camera source missing","MASCamera is required for these video pages; RPM/HullCam outputs are not automatically converted");
            var config=profile.GetNode("MODULE");
            foreach(var group in config.GetNodes("MAS_ACTION_GROUP")) IntegrationState.Report(owner,"MAS action group "+group.GetValue("id"),"Configuration retained / operation unverified","Source-specific targets must exist on this vessel");
        }
        void OnDestroy()
        {
            pending.Clear();
            if(owner==null) return;
            if(computer!=null && MASBootstrap.Owned(computer)) owner.RemoveModule(computer);
            if(retiringComputer!=null && MASBootstrap.Owned(retiringComputer)) owner.RemoveModule(retiringComputer);
        }
    }
}
