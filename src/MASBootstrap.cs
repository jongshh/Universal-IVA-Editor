using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace UniversalIVAEditor
{
    public static class MASBootstrap
    {
        internal sealed class ComputerRun : MASStartGate
        {
            public bool Owned,Destroyed;
            public ConfigNode Config;
        }
        internal sealed class PropRun : MASStartGate
        {
            public bool Warning,Cleaned;
            public PartModule Computer;
        }
        static readonly ConditionalWeakTable<PartModule,ComputerRun> computers=new ConditionalWeakTable<PartModule,ComputerRun>();
        static readonly ConditionalWeakTable<InternalModule,PropRun> props=new ConditionalWeakTable<InternalModule,PropRun>();
        static List<MASConfiguration.Candidate> profiles;
        internal static MASApi Api;
        public static bool Available { get; private set; }
        public static string Status { get; private set; }="MAS not installed";
        [ThreadStatic] static Part activePart;
        [ThreadStatic] static string activeKey;
        [ThreadStatic] static PartModule destroyComputer;
        [ThreadStatic] static Part destroyPart;
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        public static void ClearProfiles() { profiles=null; }
        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(MASBootstrap),name);
        public static void Install()
        {
            var assembly=AssemblyLoader.loadedAssemblies.Select(a=>a.assembly).FirstOrDefault(a=>a.GetType("AvionicsSystems.MASFlightComputer")!=null);
            if(assembly==null) return;
            var harmony=new Harmony("local.universalivaeditor.mas");
            try
            {
                Api=MASApi.Bind(assembly);
                var complain=MASApi.Declared(assembly.GetType("AvionicsSystems.Utility",true),"ComplainLoudly");
                harmony.Patch(Api.PartConfig,prefix:Hook(nameof(PartConfiguration)));
                harmony.Patch(Api.Instance,prefix:Hook(nameof(FindComputer)));
                harmony.Patch(Api.ComputerStart,prefix:Hook(nameof(ComputerStarting)),finalizer:Hook(nameof(ComputerFinished)));
                harmony.Patch(Api.ComputerDestroy,prefix:Hook(nameof(ComputerDestroying)),finalizer:Hook(nameof(ComputerDestroyed)));
                foreach(var method in Api.PropStarts.Values) harmony.Patch(method,prefix:Hook(nameof(PropStarting)),finalizer:Hook(nameof(PropFinished)));
                foreach(var method in Api.PropDestroys.Values) harmony.Patch(method,prefix:Hook(nameof(PropDestroying)),finalizer:Hook(nameof(PropDestroyed)));
                harmony.Patch(complain,prefix:Hook(nameof(Complaint)));
                harmony.Patch(MASApi.Declared(typeof(ProtoPartSnapshot),"Save"),postfix:Hook(nameof(StripSaved)));
                foreach(var constructor in typeof(ProtoPartSnapshot).GetConstructors().Where(c=>c.GetParameters().Length>0 && c.GetParameters()[0].ParameterType==typeof(Part))) harmony.Patch(constructor,postfix:Hook(nameof(StripSnapshot)));
                Available=true; Status="MAS adapter connected: "+assembly.GetName().Version+" / flight operation unverified"; Log.Info(Status);
            }
            catch(Exception e) { Available=false; Status="MAS adapter unavailable: "+e.GetBaseException().Message; Log.Error(Status,e); }
        }
        static IEnumerable<string> IvaReferences(ConfigNode node)
        {
            if(node.name=="INTERNAL" && node.HasValue("name")) yield return node.GetValue("name");
            if(node.name=="DATA" && node.HasValue("internalName")) yield return node.GetValue("internalName");
            foreach(ConfigNode child in node.nodes) foreach(var name in IvaReferences(child)) yield return name;
        }
        static IEnumerable<MASConfiguration.Candidate> Profiles()
        {
            if(profiles!=null) return profiles;
            profiles=new List<MASConfiguration.Candidate>();
            if(PartLoader.LoadedPartsList!=null) foreach(var part in PartLoader.LoadedPartsList)
            {
                var config=part.partConfig; if(config==null) continue;
                var module=config.GetNodes("MODULE").LastOrDefault(n=>n.GetValue("name")=="MASFlightComputer");
                if(module!=null) profiles.Add(new MASConfiguration.Candidate {Part=part.name,Internals=IvaReferences(config).Distinct(StringComparer.Ordinal).ToArray(),Module=MASConfiguration.Sanitize(module)});
            }
            return profiles;
        }
        internal static ConfigNode Profile(ConfigNode snapshot,Part part)
        {
            var existing=part?.partInfo?.partConfig?.GetNodes("MODULE").LastOrDefault(n=>n.GetValue("name")=="MASFlightComputer");
            return MASConfiguration.Select(snapshot,existing,part?.partInfo?.name,Profiles());
        }
        public static ConfigNode ForSave(ConfigNode snapshot,Catalog catalog,Part target=null)
        {
            var copy=Configuration.Copy(snapshot);
            if(!MASConfiguration.Needs(copy,catalog)) return copy;
            if(target==null) target=PartLoader.LoadedPartsList?.FirstOrDefault(p=>p.name==copy.GetValue("part"))?.partPrefab;
            var profile=Profile(copy,target); Log.Trace("MAS automatic profile part="+copy.GetValue("part")+" source="+profile.GetValue("source")+" donor="+profile.GetValue("sourcePart"));
            return MASConfiguration.Attach(copy,profile);
        }
        public static void Prepare(Part part,InternalModel model,ConfigNode snapshot,Catalog catalog)
        {
            if(!HighLogic.LoadedSceneIsFlight) return;
            try
            {
                var runtime=part.GetComponent<MASRuntime>();
                bool needed=MASConfiguration.Needs(snapshot,catalog);
                if(!needed) { runtime?.Stop(); return; }
                if(!Available) { IntegrationState.Report(part,"MAS","Not installed / incompatible API",Status); return; }
                if(runtime==null) runtime=part.gameObject.AddComponent<MASRuntime>();
                runtime.Configure(part,model,Profile(snapshot,part));
            }
            catch(Exception e) { IntegrationState.Report(part,"MAS","Initialization failed",e.GetBaseException().Message); Log.Error("MAS preparation failed",e); }
        }
        public static void Reset(Part part) { if(part!=null) part.GetComponent<MASRuntime>()?.Stop(); }
        internal static bool Owned(PartModule computer) => !ReferenceEquals(computer,null) && computers.TryGetValue(computer,out var run) && run.Owned;
        internal static ComputerRun Run(PartModule computer) => computers.GetValue(computer,_=>new ComputerRun());
        internal static PropRun Run(InternalModule prop) => props.GetValue(prop,_=>new PropRun());
        internal static PartModule[] Find(Part part) => part.Modules.Cast<PartModule>().Where(m=>Api.Computer.IsInstanceOfType(m)).ToArray();
        internal static void RegisterOwned(PartModule computer,ConfigNode config) { var run=Run(computer); run.Owned=true; run.Config=Configuration.Copy(config); }
        static bool PartConfiguration(Part __0,string __1,int __2,ref ConfigNode __result)
        {
            if(__1!="MASFlightComputer" || __2!=0 || __0==null) return true;
            var owned=__0.Modules.Cast<PartModule>().LastOrDefault(Owned);
            if(owned==null) return true;
            __result=Configuration.Copy(Run(owned).Config); return false;
        }
        static bool FindComputer(Part __0,ref object __result)
        {
            if(destroyPart==null || destroyPart!=__0) return true;
            __result=destroyComputer!=null?destroyComputer:null; return false;
        }
        internal sealed class Scope { public Part Part,DestroyPart; public string Key; public PartModule Computer; public UnityEngine.Object[] Resources; }
        static Scope Enter(Part part,string key)
        {
            var scope=new Scope{Part=activePart,Key=activeKey,Computer=destroyComputer,DestroyPart=destroyPart}; activePart=part; activeKey=key; return scope;
        }
        static void Leave(Scope scope) { if(scope!=null) { activePart=scope.Part; activeKey=scope.Key; destroyComputer=scope.Computer; destroyPart=scope.DestroyPart; } }
        static bool ComputerStarting(PartModule __instance,out Scope __state)
        {
            __state=null;
            if(!Owned(__instance) && !IntegrationState.Owns(__instance.part)) return true;
            var run=Run(__instance);
            if(run.Invoking) { __state=Enter(__instance.part,"MAS computer"); return true; }
            if(run.Attempted || run.Failed || Api.Ready(__instance)) return false;
            var runtime=__instance.part.GetComponent<MASRuntime>();
            if(runtime==null) return true;
            runtime.Wake(); return false;
        }
        static Exception ComputerFinished(PartModule __instance,Scope __state,Exception __exception)
        {
            if(__state==null) return __exception;
            var run=Run(__instance); run.Complete(__exception==null);
            run.Failed|=__exception!=null || !Api.Ready(__instance);
            if(run.Failed) IntegrationState.Report(__instance.part,"MAS computer","Initialization failed",__exception?.GetBaseException().Message ?? "Native computer did not complete Lua/proxy initialization");
            Leave(__state); return null;
        }
        static bool PropStarting(InternalModule __instance,out Scope __state)
        {
            __state=null; var part=__instance.internalProp?.part;
            var runtime=part==null?null:part.GetComponent<MASRuntime>();
            if(!IntegrationState.Owns(part) && (runtime==null || !runtime.Manages(__instance))) return true;
            if(HighLogic.LoadedSceneIsEditor) return false;
            var run=Run(__instance);
            if(run.Invoking) { __state=Enter(part,Key(__instance)); return true; }
            if(run.Attempted || run.Failed || part.internalModel?.GetComponent<DeletedPropVisibility>()?.Contains(__instance.transform)==true) return false;
            if(!Available || runtime==null) { run.Failed=true; IntegrationState.Report(part,Key(__instance),"Initialization unavailable",Status); return false; }
            runtime.Queue(__instance); return false;
        }
        internal static string Key(InternalModule module) => "MAS Prop "+module.internalProp?.propID+" "+module.internalProp?.propName+" "+module.GetType().Name;
        static Exception PropFinished(InternalModule __instance,Scope __state,Exception __exception)
        {
            if(__state==null) return __exception;
            var run=Run(__instance); run.Complete(__exception==null);
            var flag=__instance.GetType().GetField("initialized",Flags);
            bool ready=flag==null || !(flag.GetValue(__instance) is bool value) || value;
            run.Failed|=__exception!=null || !ready || run.Warning;
            IntegrationState.Report(__instance.internalProp?.part,Key(__instance),run.Failed?"Prop initialization failed":"Connected / actual operation unverified",__exception?.GetBaseException().Message ?? (run.Failed?"Check MAS configuration warnings in KSP.log":"Native MAS initialization completed"));
            Leave(__state); return null;
        }
        static bool Complaint(string __0)
        {
            if(activePart==null) return true;
            IntegrationState.Report(activePart,activeKey??"MAS","Initialization warning",__0);
            activePart.GetComponent<MASRuntime>()?.Warning(activeKey); return false;
        }
        static bool PropDestroying(InternalModule __instance,out Scope __state)
        {
            __state=null;
            if(!props.TryGetValue(__instance,out var run)) return true;
            if(run.Cleaned) return false;
            __state=Enter(__instance.internalProp?.part,Key(__instance)); destroyComputer=run.Computer; destroyPart=__instance.internalProp?.part;
            __state.Resources=new[]{"screenSpace","screen"}.Select(n=>__instance.GetType().GetField(n,Flags)?.GetValue(__instance) as UnityEngine.Object).Where(o=>o!=null).ToArray();
            var initialized=__instance.GetType().GetField("initialized",Flags);
            if(run.Failed && initialized?.FieldType==typeof(bool) && run.Computer!=null && __instance.GetType().GetField("variableRegistrar",Flags)?.GetValue(__instance)!=null) initialized.SetValue(__instance,true);
            return true;
        }
        static Exception PropDestroyed(InternalModule __instance,Scope __state,Exception __exception)
        {
            if(__state==null) return __exception;
            try
            {
                var run=Run(__instance); run.Cleaned=true;
                if(__exception!=null) ReleaseField(__instance,"variableRegistrar");
                var initialized=__instance.GetType().GetField("initialized",Flags);
                if(initialized?.FieldType==typeof(bool)) initialized.SetValue(__instance,false);
                var currentState=__instance.GetType().GetField("currentState",Flags);
                if(currentState?.FieldType==typeof(bool)) currentState.SetValue(__instance,false);
                foreach(var resource in __state.Resources ?? new UnityEngine.Object[0]) DestroyResource(resource);
                DestroyField(__instance,"screenSpace"); DestroyField(__instance,"screen");
                if(__exception!=null) IntegrationState.Report(__instance.internalProp?.part,Key(__instance),"Cleanup warning",__exception.GetBaseException().Message);
            }
            finally { Leave(__state); }
            return null;
        }
        static void ComputerDestroying(PartModule __instance)
        {
            if(!Owned(__instance)) return;
            var run=Run(__instance); run.Destroyed=true;
            if(run.Attempted && !(bool)Api.Initialized.GetValue(__instance,null)) Api.Initialized.GetSetMethod(true).Invoke(__instance,new object[]{true});
        }
        static Exception ComputerDestroyed(PartModule __instance,Exception __exception)
        {
            if(!Owned(__instance)) return __exception;
            DestroyField(__instance,"audioObject"); DestroyField(__instance,"morseAudioObject");
            if(__exception!=null) Log.Error("MAS owned computer cleanup",__exception);
            return null;
        }
        static void ReleaseField(object instance,string name)
        {
            try { var value=instance.GetType().GetField(name,Flags)?.GetValue(instance); value?.GetType().GetMethod("ReleaseResources",Flags,null,Type.EmptyTypes,null)?.Invoke(value,null); } catch(Exception e) { Log.Trace("MAS cleanup: "+e.GetBaseException().Message); }
        }
        static void DestroyField(object instance,string name)
        {
            var field=instance.GetType().GetField(name,Flags); var value=field?.GetValue(instance) as UnityEngine.Object;
            if(value==null) return;
            DestroyResource(value); field.SetValue(instance,null);
        }
        static void DestroyResource(UnityEngine.Object value)
        {
            if(value==null) return;
            if(value is RenderTexture texture) texture.Release();
            UnityEngine.Object.Destroy(value);
        }
        static void StripSnapshot(ProtoPartSnapshot __instance) { __instance.modules?.RemoveAll(m=>Owned(m.moduleRef)); }
        static void StripSaved(ProtoPartSnapshot __instance,ConfigNode __0)
        {
            if(__instance.modules==null) return;
            var nodes=__0.GetNodes("MODULE"); var owned=__instance.modules.Select(m=>Owned(m.moduleRef)).ToArray();
            foreach(int index in IntegrationPersistence.TransientIndices(nodes.Select(n=>n.GetValue("name")).ToArray(),__instance.modules.Select(m=>m.moduleName).ToArray(),owned)) __0.RemoveNode(nodes[index]);
        }
    }
}
