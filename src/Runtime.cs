using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UniversalIVAEditor
{
    public static class PresetStore
    {
        static Catalog cached;
        public static Catalog Library => cached ?? (cached=Catalog.Read());
        public static void Invalidate() { cached=null; RevivaIntegration.Invalidate(); }
        public static ConfigNode Resolve(Part part)
        {
            if(RevivaIntegration.Installed) return RevivaIntegration.Resolve(part);
            return Default(part);
        }
        public static ConfigNode Default(Part part)
        {
            var c=Library;
            if(c.Disabled.Contains(part.partInfo.name) || !c.Defaults.TryGetValue(part.partInfo.name,out var id)) return null;
            return c.Snapshot(id,part.partInfo.name,part.CrewCapacity);
        }
        static string FileFor(string name) => Path.Combine(Catalog.UserDirectory,"Active",Uri.EscapeDataString(name)+".cfg");
        public static void Save(Part part,ConfigNode data)
        {
            data=MASBootstrap.ForSave(data,Library,part);
            Library.Validate(data,part.partInfo.name,part.CrewCapacity);
            if(data.GetValue("version")!="2") throw new FormatException("Unsupported preset format; version 2 required");
            LayoutEngine.Preflight(data,Library);
            var session=new EditSession(data); session.Data.RemoveValues("ownsIVA"); session.ForkIds(); var root=Catalog.Document(session.Data);
            var mapping=root.AddNode("UIVA_DEFAULT"); mapping.AddValue("version",2); mapping.AddValue("id","active."+Guid.NewGuid().ToString("N"));
            mapping.AddValue("part",part.partInfo.name); mapping.AddValue("preset",session.Data.GetValue("id")); mapping.AddValue("managed","true");
            AtomicWrite(FileFor(part.partInfo.name),Configuration.FileText(root)); Invalidate(); RevivaIntegration.Refresh(); Log.Trace("Saved part-wide preset "+part.partInfo.name);
        }
        public static void Disable(string partName)
        {
            var root=new ConfigNode(); var n=root.AddNode("UIVA_DISABLED"); n.AddValue("part",partName);
            AtomicWrite(FileFor(partName),Configuration.FileText(root)); Invalidate(); RevivaIntegration.Refresh();
        }
        public static void AtomicWrite(string path,string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)); var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temp,text,System.Text.Encoding.UTF8);
                var parsed=ConfigNode.Load(temp); if(parsed==null || !parsed.HasData) throw new FormatException("Written cfg could not be parsed");
                if(File.Exists(path)) File.Replace(temp,path,path+"."+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")+".bak");
                else File.Move(temp,path);
                PruneBackups(path);
            }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
        public static void PruneBackups(string path)
        {
            string directory=Path.GetDirectoryName(Path.GetFullPath(path)),prefix=Path.GetFileName(path)+".";
            var backups=Directory.GetFiles(directory,"*.bak").Where(file=> {
                string name=Path.GetFileName(file);
                if(!name.StartsWith(prefix,StringComparison.Ordinal) || !name.EndsWith(".bak",StringComparison.Ordinal)) return false;
                string stamp=name.Substring(prefix.Length,name.Length-prefix.Length-4);
                return stamp.Length==17 && stamp.All(char.IsDigit);
            }).OrderByDescending(file=>Path.GetFileName(file),StringComparer.Ordinal).Skip(3);
            foreach(var backup in backups) try { File.Delete(backup); } catch(IOException e) { Log.Info("Backup cleanup skipped: "+e.Message); } catch(UnauthorizedAccessException e) { Log.Info("Backup cleanup skipped: "+e.Message); }
        }
    }
    public sealed class PartTarget
    {
        public readonly Part part;
        public PartTarget(Part p) { part=p; }
        public ConfigNode Snapshot => PresetStore.Resolve(part) ?? PresetStore.Default(part);
        public void Commit(ConfigNode data)
        {
            if(part.Modules.Contains("ProbeControlRoomPart")) throw new FormatException("ProbeControlRoom is unsupported");
            PresetStore.Save(part,data);
        }
        public void RestoreOriginal()=>PresetStore.Disable(part.partInfo.name);
    }
    public sealed class RuntimeState : MonoBehaviour
    {
        public ConfigNode Snapshot; public ConfigNode Original; public bool Failed,Initialized,Spawned; public string Selection;
    }
    [KSPAddon(KSPAddon.Startup.Instantly,true)]
    public sealed class Bootstrap : MonoBehaviour
    {
        public void Awake()
        {
            DontDestroyOnLoad(gameObject); var h=new HarmonyLib.Harmony("local.universalivaeditor");
            Patch(h,typeof(Part),"CreateInternalModel",nameof(Prepare),null);

            Patch(h,typeof(Part),"AddInternalPart",nameof(BeforeCreate),nameof(AfterCreate));
            Patch(h,typeof(InternalModel),"Initialize",null,nameof(Initialized));
            Patch(h,typeof(Part),"SpawnIVA",null,nameof(Spawned));
            h.Patch(HarmonyLib.AccessTools.Method(typeof(Part),"SpawnIVA"),finalizer:new HarmonyLib.HarmonyMethod(typeof(Bootstrap),nameof(SpawnFailed)));
            h.Patch(HarmonyLib.AccessTools.Method(typeof(InternalCamera),"SetTransform",new[]{typeof(Transform),typeof(bool)}),prefix:new HarmonyLib.HarmonyMethod(typeof(Bootstrap),nameof(SeatView)));
            Log.Trace("RPM integration hooks deferred until MainMenu assembly loading completes");
            Log.Info("0.5.3 public release part preset editor loaded");
        }
        static void Patch(HarmonyLib.Harmony h,Type type,string method,string before,string after)
        {
            h.Patch(HarmonyLib.AccessTools.Method(type,method),before==null?null:new HarmonyLib.HarmonyMethod(typeof(Bootstrap),before),after==null?null:new HarmonyLib.HarmonyMethod(typeof(Bootstrap),after));
        }
        static void Prepare(Part __instance)
        {
            var state=__instance.GetComponent<RuntimeState>() ?? __instance.gameObject.AddComponent<RuntimeState>();
            if(state.Original==null) state.Original=__instance.partInfo.internalConfig==null ? new ConfigNode("INTERNAL") : Configuration.Copy(__instance.partInfo.internalConfig);
            var selection=RevivaIntegration.Selection(__instance);
            if(state.Selection!=selection) { state.Failed=false; state.Initialized=false; state.Spawned=false; state.Selection=selection; }
            if(state.Failed) return;
            try
            {
                bool wasActive=state.Snapshot!=null; state.Snapshot=PresetStore.Resolve(__instance); if(state.Snapshot==null)
                {
                    if(wasActive) { RuntimeEVA.Reset(__instance); RuntimeCameras.Reset(__instance); MASBootstrap.Reset(__instance); if(!RevivaIntegration.Installed) { __instance.partInfo=new AvailablePart(__instance.partInfo); __instance.partInfo.internalConfig=Configuration.Copy(state.Original); } } return;
                }
                if(state.Snapshot.GetValue("version")!="2") throw new FormatException("Unsupported preset format; version 2 required");
                if(__instance.Modules.Contains("ProbeControlRoomPart")) throw new FormatException("ProbeControlRoom is unsupported");
                LayoutEngine.Preflight(state.Snapshot,PresetStore.Library);
                var reference=new ConfigNode("INTERNAL"); reference.AddValue("name",state.Snapshot.GetNode("UIVA_LAYOUT").GetValue("internal"));
                __instance.partInfo=new AvailablePart(__instance.partInfo); __instance.partInfo.internalConfig=reference;
                Log.Trace("Creating part="+__instance.partInfo.name+" IVA="+reference.GetValue("name"));
            }
            catch(Exception e)
            {
                state.Failed=true; state.Snapshot=null; __instance.partInfo=new AvailablePart(__instance.partInfo); __instance.partInfo.internalConfig=Configuration.Copy(state.Original);
                Log.Error("Preset rejected before internal creation; original configuration restored",e);
            }
        }
        static void BeforeCreate(Part __instance,ref ConfigNode __0)
        {
            Prepare(__instance);
            var state=__instance.GetComponent<RuntimeState>();
            if(state?.Failed==true) { __0=Configuration.Copy(state.Original); return; }
            if(state?.Snapshot==null) return;
            __0=new ConfigNode("INTERNAL"); __0.AddValue("name",state.Snapshot.GetNode("UIVA_LAYOUT").GetValue("internal"));
        }
        static void AfterCreate(Part __instance,ref InternalModel __result)
        {
            var state=__instance.GetComponent<RuntimeState>(); if(state?.Snapshot==null || state.Failed || __result==null) return;
            try
            {
                var source=PartLoader.GetInternalPart(state.Snapshot.GetNode("UIVA_LAYOUT").GetValue("internal"));
                __result.part=__instance;
                bool rpmRequired=__result.GetComponentsInChildren<InternalModule>(true).Any(m=>m.GetType().Namespace=="JSI") || state.Snapshot.GetNode("UIVA_LAYOUT").GetNodes("PROP_ADD").Any(n=>PresetStore.Library.Props.TryGetValue(n.GetValue("name"),out var cfg) && cfg.GetNodes("MODULE").Any(m=>(m.GetValue("name")??"").StartsWith("JSI",StringComparison.Ordinal) || m.GetValue("name")=="RasterPropMonitor"));
                if(rpmRequired) RPMProviderBootstrap.Prepare(__instance);
                LayoutEngine.RebindInstance(__result,source,PresetStore.Library);
                var layoutGroup=LayoutEngine.Apply(__result,state.Snapshot,PresetStore.Library);
                MASBootstrap.Prepare(__instance,__result,state.Snapshot,PresetStore.Library);
                RuntimeEVA.Apply(__instance,__result,layoutGroup,state.Snapshot);
                RuntimeCameras.Apply(__instance,state.Snapshot);
                (__result.GetComponent<IntegrationAudit>() ?? __result.gameObject.AddComponent<IntegrationAudit>()).Model=__result;
                Log.Trace("Layout prepared; waiting for initialization and crew spawn: "+__instance.partInfo.name);
            }
            catch(Exception e)
            {
                __result=Recover(__instance,state,__result,"layout application",e,true);

            }
        }
        static InternalModel Recover(Part part,RuntimeState state,InternalModel failed,string stage,Exception error,bool initializeBeforeDisposal)
        {
            state.Failed=true; state.Initialized=false; state.Spawned=false;
            RuntimeCameras.Reset(part); RuntimeEVA.Reset(part); MASBootstrap.Reset(part);
            Log.Error("IVA failed at "+stage+"; recovering original for "+part.partInfo.name,error);
                                                                                                                            
            if(failed!=null && initializeBeforeDisposal && part.vessel!=null)
                try { failed.Initialize(part); } catch(Exception e) { Log.Error("Recovery lifecycle initialization",e); }
            part.partInfo=new AvailablePart(part.partInfo); part.partInfo.internalConfig=Configuration.Copy(state.Original);
            part.internalModel=null;
            if(failed!=null) { failed.gameObject.SetActive(false); Destroy(failed.gameObject); }
            InternalModel restored=null;
            try { if(state.Original.HasValue("name")) restored=part.AddInternalPart(Configuration.Copy(state.Original)); }
            catch(Exception e) { Log.Error("Original IVA recovery failed",e); }
            Log.Trace("Original IVA recovery result="+(restored==null?"none":restored.internalName)); return restored;
        }
        static Exception SpawnFailed(Part __instance,Exception __exception)
        {
            if(__exception==null) return null;
            var state=__instance.GetComponent<RuntimeState>();
            if(state?.Snapshot==null || state.Failed) return __exception;
            var restored=Recover(__instance,state,__instance.internalModel,"initialization / crew spawn",__exception,false);
            if(restored!=null)
            {
                try { __instance.SpawnIVA(); } catch(Exception e) { Log.Error("Original IVA crew recovery failed",e); return e; }
            }
            return null;
        }
        static void Initialized(InternalModel __instance)
        {
            __instance.GetComponent<DeletedPropVisibility>()?.Retire();
            RuntimeEVA.Initialized(__instance);
            var state=__instance.part?.GetComponent<RuntimeState>(); if(state?.Snapshot==null || state.Failed) return;
            foreach(var warning in PropIntegration.Warnings(state.Snapshot.GetNode("UIVA_LAYOUT"),PresetStore.Library,__instance.part)) Log.Info("Integration: "+warning);
            PropIntegration.FlightDiagnostics(state.Snapshot.GetNode("UIVA_LAYOUT"),PresetStore.Library);
            state.Initialized=true; Log.Trace("Initialized IVA="+__instance.internalName+" props="+__instance.props.Count+" seats="+__instance.seats.Count);
        }
        static void Spawned(Part __instance)
        {
            var state=__instance.GetComponent<RuntimeState>(); if(state?.Snapshot==null || state.Failed) return;
            if(__instance.internalModel!=null) RuntimeEVA.Initialized(__instance.internalModel);
            int crew=__instance.protoModuleCrew.Count;
            int occupied=__instance.internalModel?.seats.Count(s=>s.kerbalRef!=null) ?? 0;
            state.Spawned=__instance.internalModel!=null && state.Initialized && occupied>=crew;
            if(!state.Spawned) throw new InvalidOperationException("IVA creation did not complete crew assignment: "+occupied+"/"+crew);
            Log.Trace("Crew spawn complete part="+__instance.partInfo.name+" success="+state.Spawned+" occupied="+(__instance.internalModel?.seats.Count(s=>s.kerbalRef!=null) ?? 0));
        }
        static void SeatView(ref Transform __0)
        {
            if(__0==null) return; var kerbal=__0.GetComponentInParent<Kerbal>(); if(kerbal==null || kerbal.eyeTransform!=__0) return;
            var part=kerbal.InPart; var state=part?.GetComponent<RuntimeState>(); if(state?.Snapshot==null || state.Failed || part.internalModel==null) return;
            int slot=part.internalModel.seats.FindIndex(s=>s.kerbalRef==kerbal);
            var seat=state.Snapshot.GetNode("UIVA_LAYOUT").GetNodes("SEAT").FirstOrDefault(s=>s.GetValue("slot")==slot.ToString()); if(seat==null) return;
            var anchor=__0.Find("UIVA_ViewOrientation"); if(anchor==null) { anchor=new GameObject("UIVA_ViewOrientation").transform; anchor.SetParent(__0,false); }
            anchor.localRotation=Configuration.Rotation(seat,"viewRotation"); __0=anchor;
        }
    }
}




















