using System;
using System.Linq;
using System.Reflection;
namespace UniversalIVAEditor
{
    public static class HullCamBinding
    {
        sealed class Target
        {
            readonly Part owner; readonly ConfigNode binding; Vessel vessel; int revision=-1; PartModule module;
            public Target(Part owner,ConfigNode binding) { this.owner=owner; this.binding=binding; }
            public PartModule Get()
            {
                int currentRevision=RuntimeCameras.Revision(owner);
                if(module!=null && vessel==owner.vessel && revision==currentRevision && module.part.vessel==owner.vessel) return module;
                string name=binding.GetValue("source")=="local"?"UIVA_"+binding.GetValue("slot"):binding.GetValue("camera");
                var parts=binding.GetValue("source")=="local"?new[]{owner}:owner.vessel?.parts.ToArray()??new[]{owner};
                var modules=parts.SelectMany(p=>p.Modules.Cast<PartModule>()).Where(m=>m.GetType().FullName=="HullcamVDS.MuMechModuleHullCameraZoom" || m.GetType().FullName=="HullcamVDS.MuMechModuleHullCamera").Where(m=>(m.GetType().GetField("cameraName")?.GetValue(m) as string)==name).ToArray();
                if(modules.Length!=1) throw new FormatException("HullCam switching source missing or ambiguous: "+name);
                module=modules[0]; vessel=owner.vessel; revision=currentRevision; return module;
            }
        }
        public static string Slot(string provider)
        {
            if(provider=="UIVAHullCam" || provider=="HullCamVDS" || provider=="HullcamVDS" || provider=="MuMechModuleHullCameraZoom" || provider=="MuMechModuleHullCamera") return "";
            return provider.StartsWith("UIVAHullCam_",StringComparison.Ordinal) && provider.Length>"UIVAHullCam_".Length?provider.Substring("UIVAHullCam_".Length):null;
        }
        public static bool TryBind(Part part,string request,Type delegateType,out Delegate result,out string error)
        {
            result=null; error=null;
            var split=(request??"").Split(':'); if(split.Length!=2) return false;
            var slot=Slot(split[0]); if(slot==null) return false;
            try {
                var bindings=(part.GetComponent<RuntimeState>()?.Snapshot?.GetNode("INTEGRATION")?.GetNodes("CAMERA") ?? new ConfigNode[0]).Where(c=>c.GetValue("provider")=="hullcam" && c.GetValue("enabled")!="false" && (slot=="" || c.GetValue("slot")==slot)).ToArray();
                if(bindings.Length!=1) throw new FormatException("Choose one HullCam switching binding, or use UIVAHullCam_<slot> for an explicit slot");
                var binding=bindings[0]; var target=new Target(part,binding); var module=target.Get(); var type=module.GetType(); var active=type.GetField("camActive"); var enabled=type.GetField("camEnabled");
                if(active==null || enabled==null) throw new NotSupportedException("HullCam state API unavailable");
                string operation=split[1];
                if(operation=="CameraActive" || operation=="CameraEnabled") {
                    var field=operation=="CameraActive"?active:enabled;
                    Func<bool> state=()=> { try { return (bool)field.GetValue(target.Get()); } catch(Exception e) { IntegrationState.Report(part,"HullCam "+binding.GetValue("slot"),"Camera source missing",e.GetBaseException().Message); return false; } };
                    if(delegateType==typeof(Func<bool>)) result=state;
                    else if(delegateType==typeof(Func<double>)) result=new Func<double>(()=>state()?1:0);
                    else throw new NotSupportedException("HullCam state requires Func<bool> or Func<double>");
                } else {
                    if(delegateType!=typeof(Action<bool>)) throw new NotSupportedException("HullCam action requires Action<bool>");
                    string methodName;
                    switch(operation) {
                        case "SetActive": case "ActivateCamera": case "ActivateCameraAction": methodName="ActivateCameraAction"; break;
                        case "DeactivateCamera": case "DeactivateCameraAction": methodName="DeactivateCameraAction"; break;
                        case "NextCamera": case "NextCameraAction": methodName="NextCameraAction"; break;
                        case "PreviousCamera": case "PreviousCameraAction": methodName="PreviousCameraAction"; break;
                        case "SetEnabled": case "EnableCamera": methodName="EnableCamera"; break;
                        default: throw new NotSupportedException("Unsupported HullCam action: "+operation);
                    }
                    var action=type.GetMethod(methodName,BindingFlags.Instance|BindingFlags.Public); var deactivate=type.GetMethod("DeactivateCameraAction");
                    if(action==null || operation=="SetActive" && deactivate==null) throw new NotSupportedException("HullCam action API unavailable");
                    result=new Action<bool>(value=> {
                        if(!value && operation!="SetActive" && operation!="SetEnabled") return;
                        try {
                            var current=target.Get();
                            if(operation=="SetActive" && (bool)active.GetValue(current)==value || operation=="SetEnabled" && (bool)enabled.GetValue(current)==value) return;
                            var method=operation=="SetActive" && !value?deactivate:action;
                            method.Invoke(current,method.GetParameters().Length==0?null:new object[]{new KSPActionParam(KSPActionGroup.None,KSPActionType.Activate)});
                        } catch(Exception e) { IntegrationState.Report(part,"HullCam "+binding.GetValue("slot")+" "+operation,"Action failed",e.GetBaseException().Message); }
                    });
                }
            } catch(Exception e) { error=e.GetBaseException().Message; }
            return true;
        }
    }
}

