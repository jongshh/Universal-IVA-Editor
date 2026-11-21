using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed class RuntimeCameras : MonoBehaviour
    {
        readonly List<GameObject> aliases=new List<GameObject>(); Part owner; bool dirty,camerasChanged,retiring; Vessel configuredVessel;
        int revision;
        static RuntimeCameras Current(Part part) => part.GetComponents<RuntimeCameras>().FirstOrDefault(s=>!s.retiring);
        public static int Revision(Part part) => Current(part)?.revision ?? 0;
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        sealed class HullReady { public Vessel Vessel; }
        static readonly ConditionalWeakTable<PartModule,HullReady> started=new ConditionalWeakTable<PartModule,HullReady>();
        static Type hullType; static bool hullPatched;
        public static void InstallHull(HarmonyLib.Harmony harmony)
        {
            try { InstallHullCore(harmony); } catch(Exception e) { Log.Error("HullCam integration API unsupported; camera adapter disabled",e); }
        }
        static void InstallHullCore(HarmonyLib.Harmony harmony)
        {
            if(hullPatched) return;
            hullType=AssemblyLoader.loadedAssemblies.Select(a=>a.assembly.GetType("HullcamVDS.MuMechModuleHullCameraZoom")).FirstOrDefault(t=>t!=null);
            if(hullType==null) return;
            foreach(var name in new[]{"cameraName","cameraPosition","cameraForward","cameraUp","cameraTransformName"}) if(hullType.GetField(name,Flags)==null) throw new NotSupportedException("Missing HullCam field: "+name);
            var start=HarmonyLib.AccessTools.Method(hullType,"OnStart");
            start=start.DeclaringType.GetMethod(start.Name,Flags|BindingFlags.DeclaredOnly,null,new[]{typeof(PartModule.StartState)},null);
            harmony.Patch(start,prefix:new HarmonyLib.HarmonyMethod(typeof(RuntimeCameras),nameof(HullStart)));
            harmony.Patch(HarmonyLib.AccessTools.Method(typeof(ProtoPartSnapshot),"Save"),postfix:new HarmonyLib.HarmonyMethod(typeof(RuntimeCameras),nameof(StripOwnedModules)));
            foreach(var constructor in typeof(ProtoPartSnapshot).GetConstructors().Where(c=>c.GetParameters().Length>0 && c.GetParameters()[0].ParameterType==typeof(Part)))
                harmony.Patch(constructor,postfix:new HarmonyLib.HarmonyMethod(typeof(RuntimeCameras),nameof(StripSnapshotOwned)));
            hullPatched=true;
        }
        static bool HullStart(PartModule __instance)
        {
            bool owned=(Field(__instance,"cameraName") as string??"").StartsWith("UIVA_",StringComparison.Ordinal);
            if(started.TryGetValue(__instance,out var ignored)) return !owned;
            started.Add(__instance,new HullReady{Vessel=__instance.vessel}); return true;
        }
        static object Field(object instance,string name) => instance.GetType().GetField(name,Flags)?.GetValue(instance);
        static bool Hull(PartModule module) => module.GetType().FullName=="HullcamVDS.MuMechModuleHullCameraZoom" || module.GetType().FullName=="HullcamVDS.MuMechModuleHullCamera";
        static bool Owned(PartModule module) => module!=null && Hull(module) && (Field(module,"cameraName") as string??"").StartsWith("UIVA_",StringComparison.Ordinal);
        static void StripSnapshotOwned(ProtoPartSnapshot __instance) { __instance.modules?.RemoveAll(m=>Owned(m.moduleRef)); }
        static void StripOwnedModules(ProtoPartSnapshot __instance,ConfigNode __0)
        {
            var owned=__instance.modules.Select(m=>Owned(m.moduleRef)).ToArray(); if(!owned.Any(value=>value)) return;
            var nodes=__0.GetNodes("MODULE");
            foreach(int i in IntegrationPersistence.TransientIndices(nodes.Select(n=>n.GetValue("name")).ToArray(),__instance.modules.Select(m=>m.moduleName).ToArray(),owned)) __0.RemoveNode(nodes[i]);
        }
        public static string[] Choices(Part selected,string source)
        {
            var parts=HighLogic.LoadedSceneIsEditor?EditorLogic.SortedShipList.ToArray():selected.vessel?.parts.ToArray()??new[]{selected};
            if(source=="existing-hullcam") return parts.SelectMany(p=>p.Modules.Cast<PartModule>()).Where(Hull).Select(m=>Field(m,"cameraName") as string).Where(n=>!string.IsNullOrEmpty(n)).Distinct().ToArray();
            return parts.SelectMany(p=>p.Modules.Cast<PartModule>()).Where(m=>m.GetType().FullName=="JSI.JSIExternalCameraSelector").Select(m=>(Field(m,"cameraIDPrefix") as string)+Field(m,"current")).Distinct().ToArray();
        }
        public static void Apply(Part part,ConfigNode snapshot)
        {
            if(HighLogic.LoadedSceneIsEditor) return;
            var configs=(snapshot.GetNode("INTEGRATION")?.GetNodes("CAMERA") ?? new ConfigNode[0]).Where(c=>c.GetValue("enabled")!="false").ToArray();
            var state=Current(part); if(configs.Length==0 && state==null) return;
            if(state==null) state=part.gameObject.AddComponent<RuntimeCameras>(); state.owner=part;
            state.revision++;
            state.aliases.RemoveAll(g=>g==null);
            state.camerasChanged=state.configuredVessel!=part.vessel; state.configuredVessel=part.vessel;
            foreach(var config in configs) {
                var slot=config.GetValue("slot");
                var modulesBefore=new HashSet<PartModule>(part.Modules.Cast<PartModule>());
                try { state.Connect(config); }
                catch(Exception e) {
                    foreach(var alias in state.aliases.Where(g=>g!=null && (g.name==slot || g.name=="UIVA_Anchor_"+slot)).ToArray()) { state.aliases.Remove(alias); DestroyImmediate(alias); state.camerasChanged=true; }
                    foreach(var module in part.Modules.Cast<PartModule>().Where(Owned).Where(m=>!modulesBefore.Contains(m)).ToArray()) part.RemoveModule(module);
                    IntegrationState.Report(part,"Camera "+slot,e is NotSupportedException?"Not installed / incompatible API":"Camera source missing / initialization failed",e.GetBaseException().Message);
                }
            }
            foreach(var alias in state.aliases.Where(g=>g!=null && !configs.Any(c=>g.name==(c.GetValue("provider")=="rpm"?c.GetValue("slot"):"UIVA_Anchor_"+c.GetValue("slot")))).ToArray()) { state.aliases.Remove(alias); DestroyImmediate(alias); state.camerasChanged=true; }
            if(state.camerasChanged) state.RefreshViews();
            foreach(var module in part.Modules.Cast<PartModule>().Where(Hull).Where(m=>(Field(m,"cameraName") as string??"").StartsWith("UIVA_",StringComparison.Ordinal)).ToArray()) {
                string slot=((string)Field(module,"cameraName")).Substring(5);
                if(!configs.Any(c=>c.GetValue("slot")==slot && c.GetValue("provider")=="hullcam" && c.GetValue("source")=="local")) part.RemoveModule(module);
            }
        }
        void RefreshViews()
        {
            if(owner.internalModel==null) return;
            foreach(var module in owner.internalModel.GetComponentsInChildren<InternalModule>(true).Where(m=>m.GetType().FullName=="JSI.JSISteerableCamera")) {
                try {
                    var field=module.GetType().GetField("cameraObject",Flags); var camera=field?.GetValue(module);
                    if(camera==null) continue;
                    camera.GetType().GetMethod("CleanupCameraObjects",Flags)?.Invoke(camera,null); field.SetValue(module,null);
                } catch(Exception e) { IntegrationState.Report(owner,"Camera refresh","Initialization failed",e.GetBaseException().Message); }
            }
        }
        public static void Reset(Part part)
        {
            var state=Current(part); if(state!=null) { state.retiring=true; state.enabled=false; state.dirty=false; state.ClearAliases(); Destroy(state); }
            foreach(var module in part.Modules.Cast<PartModule>().Where(Owned).ToArray()) part.RemoveModule(module);
        }
        void Connect(ConfigNode config)
        {
            string slot=config.GetValue("slot"),source=config.GetValue("source"),provider=config.GetValue("provider");
            if(provider=="rpm" && !RPMProviderBootstrap.Available) throw new NotSupportedException("Compatible RPM camera provider is not installed");
            var pose=CameraPlacement.Resolve(owner,config,t=>aliases.Any(g=>g!=null && g.transform==t));
            if(pose.ReadOnly) { IntegrationState.Report(owner,"Camera "+slot,"Connected / flight unverified",pose.ReusedRPM?"Existing RPM source reused":"Existing HullCam switching provider retained"); return; }
            var anchor=pose.Anchor; var position=pose.Position; var rotation=pose.Rotation;
            string anchorName=provider=="rpm"?slot:"UIVA_Anchor_"+slot;
            var go=aliases.FirstOrDefault(g=>g!=null && g.name==anchorName);
            if(go==null) { go=new GameObject(anchorName); aliases.Add(go); camerasChanged=true; }
            if(anchor==go.transform || anchor.IsChildOf(go.transform)) throw new FormatException("Camera binding creates a transform cycle");
            if(go.transform.parent!=anchor) camerasChanged=true;
            go.transform.SetParent(anchor,false); go.transform.localPosition=position; go.transform.localRotation=rotation;
            if(provider=="hullcam") {
                InstallHull(new HarmonyLib.Harmony("local.universalivaeditor"));
                if(hullType==null || !hullPatched) throw new NotSupportedException("Compatible HullCamVDS is not installed");
                string name="UIVA_"+slot;
                var existing=owner.Modules.Cast<PartModule>().Where(Hull).Where(m=>(Field(m,"cameraName") as string)==name).ToArray();
                if(existing.Length>1) throw new FormatException("Duplicate owned HullCam provider");
                var node=new ConfigNode("MODULE"); node.AddValue("name","MuMechModuleHullCameraZoom"); node.AddValue("cameraName",name); node.AddValue("cameraTransformName",anchorName);
                node.AddValue("cameraPosition","0,0,0"); node.AddValue("cameraForward","0,0,1"); node.AddValue("cameraUp","0,1,0"); node.AddValue("cameraFoVMax",60); node.AddValue("cameraFoVMin",10);
                var module=existing.SingleOrDefault() ?? owner.AddModule(node);
                if(module==null) throw new InvalidOperationException("HullCam provider creation failed");
                if(!started.TryGetValue(module,out var ignored)) module.OnStart(PartModule.StartState.Flying);
                if(started.TryGetValue(module,out var lifecycle) && lifecycle.Vessel!=module.vessel) {
                    var cleanup=(Callback)Delegate.CreateDelegate(typeof(Callback),module,"CleanUp");
                    if(lifecycle.Vessel!=null) lifecycle.Vessel.OnJustAboutToBeDestroyed-=cleanup;
                    if(module.vessel!=null) { module.vessel.OnJustAboutToBeDestroyed-=cleanup; module.vessel.OnJustAboutToBeDestroyed+=cleanup; }
                    lifecycle.Vessel=module.vessel;
                }
                var cameras=hullType.GetField("sCameras",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy)?.GetValue(null) as System.Collections.IList;
                if(cameras==null) throw new NotSupportedException("HullCam camera registry unavailable");
                if(!cameras.Contains(module)) cameras.Add(module);
            }
            IntegrationState.Report(owner,"Camera "+slot,"Connected / flight unverified",provider+" source="+source);
        }
        void ClearAliases() { foreach(var alias in aliases) if(alias!=null) DestroyImmediate(alias); aliases.Clear(); }
        void Awake() { GameEvents.onVesselWasModified.Add(VesselModified); }
        void Start() { dirty=true; }
        void VesselModified(Vessel vessel) { if(!retiring && owner!=null && owner.vessel==vessel) { dirty=true; revision++; } }
        void LateUpdate() { if(!dirty) return; dirty=false; var snapshot=owner?.GetComponent<RuntimeState>()?.Snapshot; if(snapshot!=null) Apply(owner,snapshot); }
        void OnDestroy() { GameEvents.onVesselWasModified.Remove(VesselModified); ClearAliases(); }
    }
}




