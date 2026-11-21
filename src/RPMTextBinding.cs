using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace UniversalIVAEditor
{
    public static class RPMTextBinding
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly ConditionalWeakTable<InternalModule,object> restored=new ConditionalWeakTable<InternalModule,object>();
        static readonly ConditionalWeakTable<InternalModule,object> nativeLoaded=new ConditionalWeakTable<InternalModule,object>();
        static readonly ConditionalWeakTable<InternalModule,object> failed=new ConditionalWeakTable<InternalModule,object>();
        public static bool Failed(InternalModule module) => failed.TryGetValue(module,out var ignored);
        sealed class TextApi
        {
            public FieldInfo Renderer,Filter; public PropertyInfo[] Properties;
            public TextApi(Type type) { Renderer=type.GetField("meshRenderer_",Flags); Filter=type.GetField("meshFilter_",Flags); Properties=type.GetProperties(Flags).Where(p=>p.DeclaringType==type && p.CanRead && p.CanWrite && p.GetIndexParameters().Length==0).ToArray(); }
        }
        static readonly Dictionary<Type,TextApi> apis=new Dictionary<Type,TextApi>();
        public static bool IsLabel(Type type) => type.FullName=="JSI.JSILabel";
        public static void Install(HarmonyLib.Harmony harmony,Assembly rpm)
        {
            var label=rpm.GetType("JSI.JSILabel"); var text=rpm.GetType("JSI.JSITextMesh");
            if(label==null || text==null) return;
            harmony.Patch(HarmonyLib.AccessTools.Method(label,"Start"),prefix:new HarmonyLib.HarmonyMethod(typeof(RPMTextBinding),nameof(BeforeStart)));
            harmony.Patch(HarmonyLib.AccessTools.Method(text,"Awake"),postfix:new HarmonyLib.HarmonyMethod(typeof(RPMTextBinding),nameof(TextAwake)));
            harmony.Patch(HarmonyLib.AccessTools.Method(label,"OnLoad"),postfix:new HarmonyLib.HarmonyMethod(typeof(RPMTextBinding),nameof(AfterLoad)));
            foreach(var type in rpm.GetTypes().Where(t=>typeof(InternalModule).IsAssignableFrom(t) && t.GetMethod("OnLoad",Flags)?.DeclaringType==t))
                harmony.Patch(type.GetMethod("OnLoad",Flags),postfix:new HarmonyLib.HarmonyMethod(typeof(PropModuleConfiguration),nameof(PropModuleConfiguration.NativeLoaded)));
        }
        static void TextAwake(MonoBehaviour __instance)
        {
            var prop=__instance.GetComponentInParent<InternalProp>();
            if(IntegrationState.Owns(prop?.internalModel?.part)) try { Repair(__instance,null); } catch(Exception e) { IntegrationState.Report(prop.part,"Prop "+prop.propID+" text","Initialization failed",e.GetBaseException().Message); __instance.enabled=false; }
        }
        static void AfterLoad(InternalModule __instance)
        {
            if(!IntegrationState.Owns(__instance.internalModel?.part)) return;
            var text=__instance.GetType().GetField("textObj",Flags)?.GetValue(__instance) as MonoBehaviour;
            try { if(text==null) throw new InvalidOperationException("New RPM label has no text object"); Repair(text,null); nativeLoaded.GetValue(__instance,_=>new object()); }
            catch(Exception e) { IntegrationState.Report(__instance.part,"Prop "+__instance.internalProp.propID+" label","Initialization failed",e.GetBaseException().Message); Disable(__instance); }
        }
        static TextApi Api(Type type) { if(!apis.TryGetValue(type,out var api)) apis[type]=api=new TextApi(type); return api; }
        public static void Repair(MonoBehaviour text,MonoBehaviour original)
        {
            var api=Api(text.GetType());
            if(api.Renderer==null || api.Filter==null) throw new NotSupportedException("Unsupported RPM text geometry API");
            var renderer=text.GetComponent<MeshRenderer>() ?? text.gameObject.AddComponent<MeshRenderer>();
            var filter=text.GetComponent<MeshFilter>() ?? text.gameObject.AddComponent<MeshFilter>();
            api.Renderer.SetValue(text,renderer); api.Filter.SetValue(text,filter);
            if(original!=null) {
                foreach(var property in api.Properties) property.SetValue(text,property.GetValue(original,null),null);
                var sourceRenderer=original.GetComponent<MeshRenderer>();
                if(sourceRenderer!=null && sourceRenderer.sharedMaterial!=null) {
                    var resources=text.GetComponent<RPMTextResources>() ?? text.gameObject.AddComponent<RPMTextResources>();
                    resources.Own(new Material(sourceRenderer.sharedMaterial)); renderer.sharedMaterial=resources.Material;
                }
            }
            if(renderer.sharedMaterial==null) {
                var util=text.GetType().Assembly.GetType("JSI.JUtil");
                var shader=util?.GetMethod("LoadInternalShader",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.Invoke(null,new object[]{"RPM/JSILabel"}) as Shader;
                if(shader==null) throw new InvalidOperationException("RPM text shader unavailable");
                var resources=text.GetComponent<RPMTextResources>() ?? text.gameObject.AddComponent<RPMTextResources>(); resources.Own(new Material(shader)); renderer.sharedMaterial=resources.Material;
            }
        }
        public static void RestoreLabel(InternalModule label,InternalModule original,ConfigNode node)
        {
            if(restored.TryGetValue(label,out var ignored)) return;
            if(nativeLoaded.TryGetValue(label,out ignored)) { restored.Add(label,new object()); return; }
            if(original==null) throw new InvalidOperationException("RPM label template unavailable");
            var type=label.GetType(); var textField=type.GetField("textObj",Flags);
            var originalText=textField?.GetValue(original) as MonoBehaviour;
            if(originalText==null) throw new InvalidOperationException("RPM loaded label has no text template");
            var path=LayoutEngine.Path(original.internalProp.transform,originalText.transform);
            var target=LayoutEngine.At(label.internalProp.transform,path);
            var text=target.GetComponent(originalText.GetType()) as MonoBehaviour;
            if(text==null) throw new InvalidOperationException("RPM cloned label geometry unavailable");
            Repair(text,originalText);
                                                                                   
            var configField=type.GetField("moduleConfig",Flags); var config=Activator.CreateInstance(configField.FieldType,true);
            configField.FieldType.GetField("Node").SetValue(config,Configuration.Copy(node)); configField.SetValue(label,config);
            var batchField=type.GetField("batchInfo",Flags); var batch=Activator.CreateInstance(batchField.FieldType,true);
            batchField.FieldType.GetMethod("OnLoad",Flags).Invoke(batch,new object[]{Configuration.Copy(node)}); batchField.SetValue(label,batch);
            textField.SetValue(label,text); restored.Add(label,new object());
        }
        static bool BeforeStart(InternalModule __instance)
        {
            if(!IntegrationState.Owns(__instance.internalModel?.part)) return true;
            if(__instance.internalModel.GetComponent<DeletedPropVisibility>()?.Contains(__instance.internalProp.transform)==true) { Disable(__instance); return false; }
            var part=__instance.part; string key="Prop "+__instance.internalProp.propID+" "+__instance.internalProp.propName+" JSILabel";
            try {
                var text=__instance.GetType().GetField("textObj",Flags)?.GetValue(__instance) as MonoBehaviour;
                if(text==null || !text.transform.IsChildOf(__instance.internalProp.transform)) throw new InvalidOperationException("Label text is missing or belongs to shared prefab");
                Repair(text,null);
                var computer=part.Modules.Cast<PartModule>().FirstOrDefault(m=>m.GetType().FullName=="JSI.RasterPropMonitorComputer"); RPMProviderBootstrap.Ensure(computer);
                IntegrationState.Report(part,key,"Connected / flight unverified","Text geometry ready before callback registration"); return true;
            } catch(Exception e) {
                IntegrationState.Report(part,key,"Initialization failed",e.GetBaseException().Message); Disable(__instance); return false;
            }
        }
        public static void Disable(InternalModule module)
        {
            failed.GetValue(module,_=>new object());
                                                                                    
            try { module.GetType().GetMethod("OnDestroy",Flags)?.Invoke(module,null); } catch(Exception e) { Log.Trace("Label cleanup: "+e.GetBaseException().Message); }
            module.GetType().GetField("del",Flags)?.SetValue(module,null);
            module.GetType().GetField("rpmComp",Flags)?.SetValue(module,null);
            var sets=module.GetType().GetField("variableSets",Flags)?.GetValue(module) as System.Collections.IList; sets?.Clear();
            module.internalProp?.internalModules.Remove(module);
            module.enabled=false;
        }
    }
    public sealed class RPMTextResources : MonoBehaviour
    {
        public Material Material { get; private set; }
        public void Own(Material material) { if(Material!=null) Destroy(Material); Material=material; }
        void OnDestroy() { if(Material!=null) Destroy(Material); }
    }
}

