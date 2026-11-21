using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace UniversalIVAEditor
{
    public static class PropModuleConfiguration
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static readonly ConditionalWeakTable<InternalModule,object> restored=new ConditionalWeakTable<InternalModule,object>();
        public static void NativeLoaded(InternalModule __instance) { if(IntegrationState.Owns(__instance.internalModel?.part)) restored.GetValue(__instance,_=>new object()); }
        public static ConfigNode Merge(ConfigNode asset,ConfigNode placement)
        {
            var result=Configuration.Copy(asset); if(placement!=null) foreach(var node in placement.GetNodes("MODULE")) result.AddNode(Configuration.Copy(node)); return result;
        }
        public static ConfigNode Resolve(ConfigNode definition,int moduleId,string className)
        {
            var modules=definition.GetNodes("MODULE");
            if(moduleId<0 || moduleId>=modules.Length) throw new FormatException("Missing module configuration: "+className+" ["+moduleId+"]");
            var source=modules[moduleId]; var name=source.GetValue("name");
            if(name!=className && name!=className.Split('.').Last()) throw new FormatException("Module configuration identity changed: "+name+" / "+className);
            return Configuration.Copy(source);
        }
        public static string Policy(string fullName,bool hasConfigHolder)
        {
            if(fullName=="FreeIva.HatchConfig") return "loading-only";
            if(fullName=="JSI.JSILabel") return "label";
            if(fullName=="JSI.JSICallbackAnimator") return "callback";
            return fullName.StartsWith("JSI.",StringComparison.Ordinal) && hasConfigHolder?"config-holder":"preserve";
        }
        public static void Restore(InternalProp prop,ConfigNode definition,InternalProp original=null)
        {
            if(original==null) original=PartLoader.Instance.internalProps.FirstOrDefault(p=>p.propName==prop.propName);
            foreach(var module in prop.internalModules.ToArray()) {
                if(RPMTextBinding.Failed(module)) { prop.internalModules.Remove(module); continue; }
                if(restored.TryGetValue(module,out var ignored)) continue;
                var type=module.GetType(); var holderField=type.GetField("moduleConfig",Flags); var policy=Policy(type.FullName,holderField!=null);
                if(policy=="loading-only" || policy=="preserve") { Log.Trace("Preserved loaded module: "+type.FullName); continue; }
                try {
                    var node=Resolve(definition,module.moduleID,type.Name);
                    var source=original?.GetComponents<InternalModule>().FirstOrDefault(m=>m.GetType()==type && m.moduleID==module.moduleID);
                    if(policy=="label") RPMTextBinding.RestoreLabel(module,source,node);
                    else if(policy=="config-holder") {
                        var holder=Activator.CreateInstance(holderField.FieldType,true); var field=holderField.FieldType.GetField("Node",Flags);
                        if(field==null) throw new NotSupportedException("RPM configuration holder changed");
                        field.SetValue(holder,node); holderField.SetValue(module,holder);
                    }
                    else RestoreCallbacks(module,source,node);
                    restored.Add(module,new object());
                } catch(Exception e) {
                    IntegrationState.Report(prop.internalModel?.part,"Prop "+prop.propID+" "+prop.propName+" "+type.Name,"Initialization failed",e.GetBaseException().Message);
                    RPMTextBinding.Disable(module);
                }
            }
        }
        static void RestoreCallbacks(InternalModule module,InternalModule original,ConfigNode config)
        {
            if(original==null) throw new InvalidOperationException("RPM callback template unavailable");
            var field=module.GetType().GetField("variableSets",Flags);
            var sources=(IList)field.GetValue(original); var definitions=config.GetNodes("VARIABLESET");
            if(sources.Count!=definitions.Length) throw new FormatException("RPM callback template/config count differs");
            var list=(IList)Activator.CreateInstance(field.FieldType); var callbackType=field.FieldType.GetGenericArguments()[0];
            var sourceComponents=original.GetComponents(callbackType); var components=module.GetComponents(callbackType);
            for(int i=0;i<sources.Count;i++) {
                int index=Array.IndexOf(sourceComponents,sources[i]);
                if(index<0 || index>=components.Length) throw new FormatException("RPM callback hierarchy differs");
                callbackType.GetMethod("Load",Flags).Invoke(components[index],new object[]{Configuration.Copy(definitions[i]),module.internalProp});
                list.Add(components[index]);
            }
            field.SetValue(module,list);
        }
    }
}

