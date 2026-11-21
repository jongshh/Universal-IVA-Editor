using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed class CameraPlacement
    {
        public Transform Anchor;
        public Vector3 Position;
        public Quaternion Rotation;
        public bool ReadOnly,ReusedRPM;
        public static bool Simple(ConfigNode node) => node.GetValue("provider")=="rpm" && node.GetValue("source")=="local" && string.IsNullOrEmpty(node.GetValue("anchor"));
        public static bool Hull(PartModule module) => module!=null && (module.GetType().FullName=="HullcamVDS.MuMechModuleHullCameraZoom" || module.GetType().FullName=="HullcamVDS.MuMechModuleHullCamera");
        static object Field(object module,string name) => module.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(module);
        public static CameraPlacement Resolve(Part owner,ConfigNode config,Func<Transform,bool> exclude=null)
        {
            var parts=owner.vessel?.parts.ToArray() ?? (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch?.ship!=null ? EditorLogic.fetch.ship.parts.ToArray() : new[]{owner});
            var pose=new CameraPlacement{Position=Configuration.Vector(config,"position",Vector3.zero),Rotation=Configuration.Rotation(config)};
            string source=config.GetValue("source");
            if(source=="local") {
                var name=config.GetValue("anchor"); pose.Anchor=string.IsNullOrEmpty(name)?owner.transform.Find("model"):owner.FindModelTransform(name);
                if(pose.Anchor==null) throw new FormatException("Local model anchor not found: "+name);
            } else if(source=="existing-rpm") {
                var found=parts.SelectMany(p=>p.FindModelTransforms(config.GetValue("camera"))).Where(t=>t!=null && !(exclude?.Invoke(t)??false)).Distinct().ToArray();
                if(found.Length!=1) throw new FormatException("RPM camera source missing or ambiguous: "+config.GetValue("camera"));
                pose.Anchor=found[0];
            } else if(source=="existing-hullcam") {
                var found=parts.SelectMany(p=>p.Modules.Cast<PartModule>()).Where(Hull).Where(m=>(Field(m,"cameraName") as string)==config.GetValue("camera")).ToArray();
                if(found.Length!=1) throw new FormatException("HullCam source missing or ambiguous: "+config.GetValue("camera"));
                var module=found[0]; var name=Field(module,"cameraTransformName") as string;
                pose.Anchor=string.IsNullOrEmpty(name)?module.part.transform:module.part.FindModelTransform(name);
                if(pose.Anchor==null) throw new FormatException("HullCam transform unavailable");
                pose.Position+=(Vector3)Field(module,"cameraPosition");
                pose.Rotation=Quaternion.LookRotation((Vector3)Field(module,"cameraForward"),(Vector3)Field(module,"cameraUp"))*pose.Rotation;
                pose.ReadOnly=config.GetValue("provider")=="hullcam";
                if(string.IsNullOrEmpty(name)) {
                    var model=module.part.transform.Find("model") ?? throw new FormatException("HullCam part model missing");
                    pose.Position=model.InverseTransformPoint(pose.Anchor.TransformPoint(pose.Position));
                    pose.Rotation=Quaternion.Inverse(model.rotation)*pose.Anchor.rotation*pose.Rotation; pose.Anchor=model;
                }
            } else throw new FormatException("Unknown camera source: "+source);
            if(config.GetValue("provider")=="rpm") {
                var occupied=parts.SelectMany(p=>p.FindModelTransforms(config.GetValue("slot"))).Where(t=>t!=null && !(exclude?.Invoke(t)??false)).Distinct().ToArray();
                if(occupied.Length!=0) {
                    if(source=="existing-rpm" && config.GetValue("camera")==config.GetValue("slot") && occupied.Length==1) {
                                                                                                 
                        pose.Position=Vector3.zero; pose.Rotation=Quaternion.identity; pose.ReadOnly=true; pose.ReusedRPM=true;
                    } else throw new FormatException("Camera slot already occupied: "+config.GetValue("slot"));
                }
            }
            return pose;
        }
        public static Matrix4x4 PreviewAnchor(Matrix4x4 previewPart,Matrix4x4 ownerInverse,Matrix4x4 anchor) => previewPart*ownerInverse*anchor;
        public static string[] SlotChoices(string[] required,string[] occupied)
        {
            var choices=required.Distinct().ToList(); int next=1;
            while(occupied.Contains("ExtCam"+next) || choices.Contains("ExtCam"+next)) next++;
            choices.Add("ExtCam"+next); return choices.ToArray();
        }
        public static float PresetPanelOverhead(float windowHeight) => windowHeight<650?100:155;
        public static float FileListHeight(float windowHeight,bool debug) => Mathf.Clamp(windowHeight-225-PresetPanelOverhead(windowHeight)-165-(debug?95:0),40,195);
        public static string RestoreSelection(string key,string[] available,System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string,string>> renames)
        {
            if(available.Contains(key)) return key;
            foreach(var rename in renames.Reverse()) {
                string candidate=key==rename.Key?rename.Value:key==rename.Value?rename.Key:null;
                if(candidate!=null && available.Contains(candidate)) return candidate;
            }
            return null;
        }
    }
}


