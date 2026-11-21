using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace UniversalIVAEditor
{
                                                                                     
    public sealed class EVAEntryBinding : MonoBehaviour
    {
        public RuntimeEVA State; public ConfigNode ExitConfig; public InternalModule Native;
        public Transform External; public bool Awakened,Boarding;
        static FieldInfo airlock,airlockName,attachNode;
        static PropertyInfo canEVA; static bool installed;
        public static bool Available => installed;
        public static void Install(Harmony harmony)
        {
            if(installed) return;
            var type=AssemblyLoader.loadedAssemblies.Select(a=>a.assembly.GetType("FreeIva.FreeIvaHatch")).FirstOrDefault(t=>t!=null);                          
            if(type==null) return;
            try {
                airlock=AccessTools.Field(type,"airlockTransform"); airlockName=AccessTools.Field(type,"airlockName"); attachNode=AccessTools.Field(type,"attachNodeId"); canEVA=AccessTools.Property(type,"CanEVA");
                var awake=AccessTools.Method(type,"OnAwake"); var open=AccessTools.Method(type,"SetDesiredOpen");
                if(airlock==null || airlock.FieldType!=typeof(Transform) || airlockName==null || attachNode==null || canEVA?.SetMethod==null || awake==null || open==null) throw new NotSupportedException("FreeIVA entry binding API changed");
                harmony.Patch(awake,prefix:new HarmonyMethod(typeof(EVAEntryBinding),nameof(BeforeAwake)),postfix:new HarmonyMethod(typeof(EVAEntryBinding),nameof(AfterAwake)));
                harmony.Patch(open,prefix:new HarmonyMethod(typeof(EVAEntryBinding),nameof(BeforeOpen)));
                installed=true; Log.Info("FreeIVA owned-entry lifecycle hooks installed: "+type.Assembly.GetName().Version);
            } catch(Exception e) { Log.Error("FreeIVA entry lifecycle adapter unavailable; using seated EVA entry fallback",e); }
        }
        static EVAEntryBinding Owned(InternalModule module)
        {
            var binding=module.GetComponent<EVAEntryBinding>(); return binding!=null && binding.Native==module?binding:null;
        }
        static bool BeforeAwake(InternalModule __instance)
        {
            var binding=Owned(__instance); if(binding==null) return true;
            binding.Restore(); return !binding.Awakened;
        }
        static void AfterAwake(InternalModule __instance)
        {
            var binding=Owned(__instance); if(binding==null) return;
            binding.Awakened=true; binding.Restore();
        }
        static void BeforeOpen(InternalModule __instance)
        {
            var binding=Owned(__instance); if(binding==null) return;
            try { binding.Restore(); }
            catch(Exception e) { Log.Error("Owned EVA entry reconnection failed",e); }
        }
        public void Restore()
        {
            if(!installed || Native==null || State==null) return;
            if(External==null) External=State.EnsureExit(ExitConfig,Boarding);
            if(External==null) return;
            External.gameObject.SetActive(true);
                                                                                       
            attachNode.SetValue(Native,string.Empty); airlockName.SetValue(Native,External.name);
            var current=airlock.GetValue(Native) as Transform;
            if(current!=External) {
                airlock.SetValue(Native,External);
                State.TraceBinding(ExitConfig.GetValue("id"),"Airlock reference restored");
            }
            canEVA.SetValue(Native,true,null);
        }
    }
}
