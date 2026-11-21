using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace UniversalIVAEditor
{
    [KSPAddon(KSPAddon.Startup.MainMenu,true)]
    public sealed class IntegrationInstaller : MonoBehaviour
    {
        public void Awake() { var harmony=new HarmonyLib.Harmony("local.universalivaeditor"); RPMProviderBootstrap.Install(harmony); MASBootstrap.Install(); EVAEntryBinding.Install(harmony); RevivaIntegration.Install(harmony); Destroy(gameObject); }
    }
    public static class RPMProviderBootstrap
    {
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        static Type computerType,baseType; static FieldInfo installed,registered; static Type[] providers;
        static bool patched,registeredProviders;
        public static bool Available => patched;
        static readonly ConditionalWeakTable<object,object> ownedLists=new ConditionalWeakTable<object,object>();
        sealed class Ready { public Vessel Vessel; }
        static readonly ConditionalWeakTable<PartModule,Ready> ready=new ConditionalWeakTable<PartModule,Ready>();
        public static void Install(HarmonyLib.Harmony harmony)
        {
            try { InstallCore(harmony); } catch(Exception e) { Log.Error("RPM integration API unsupported; adapter preparation unavailable",e); }
        }
        static void InstallCore(HarmonyLib.Harmony harmony)
        {
            if(patched) return;
            computerType=AssemblyLoader.loadedAssemblies.Select(a=>a.assembly.GetType("JSI.RasterPropMonitorComputer")).FirstOrDefault(t=>t!=null);
            if(computerType==null) return;
            baseType=computerType.Assembly.GetType("JSI.IJSIModule"); installed=computerType.GetField("installedModules",Flags); registered=baseType?.GetField("x_registeredTypes",Flags);
            if(installed==null || registered==null) { Log.Info("RPM integration: unsupported API; provider adapter disabled"); return; }
            providers=computerType.Assembly.GetTypes().Where(t=>t!=baseType && baseType.IsAssignableFrom(t) && !t.IsAbstract).ToArray();
            harmony.Patch(HarmonyLib.AccessTools.Method(computerType,"Start"),prefix:Hook(nameof(BeforeComputerStart)));
            harmony.Patch(HarmonyLib.AccessTools.Method(baseType,"CreateJSIModules"),prefix:Hook(nameof(CreateProviders)));
            foreach(var method in computerType.GetMethods(Flags).Where(m=>m.Name=="GetMethod" || m.Name=="GetInternalMethod")) harmony.Patch(method,prefix:Hook(nameof(BeforeLookup)),postfix:Hook(nameof(LookupResult)));
            foreach(var method in computerType.GetMethods(Flags).Where(m=>m.Name=="onVesselChange" || m.Name=="onVesselWasModified")) harmony.Patch(method,postfix:Hook(nameof(VesselChanged)));
            harmony.Patch(HarmonyLib.AccessTools.Method(computerType,"FindFromProp"),postfix:Hook(nameof(FoundComputer)));
            RPMTextBinding.Install(harmony,computerType.Assembly);
            RuntimeCameras.InstallHull(harmony);
            patched=true; Log.Info("RPM integration hooks installed: "+computerType.Assembly.GetName().Version);
        }
        static HarmonyLib.HarmonyMethod Hook(string name) => new HarmonyLib.HarmonyMethod(typeof(RPMProviderBootstrap),name);
        static void RegisterProviders()
        {
            if(registeredProviders) return;
            foreach(var type in providers) try { RuntimeHelpers.RunClassConstructor(type.TypeHandle); } catch(Exception e) { Log.Error("RPM adapter registration: "+type.FullName,e); }
            registeredProviders=true;
        }
        public static void BeforeComputerStart(PartModule __instance)
        {
            if(HighLogic.LoadedSceneIsEditor) return;
            RegisterProviders();
            if(IntegrationState.Owns(__instance.part)) Mark(__instance);
        }
        static void Mark(PartModule computer) { var list=installed.GetValue(computer); if(list!=null) ownedLists.GetValue(list,_=>new object()); }
        static bool CreateProviders(object __0,Vessel __1)
        {
            if(!ownedLists.TryGetValue(__0,out var ignored)) return true;
            Fill((IList)__0,__1,null); return false;
        }
        static void Fill(IList list,Vessel vessel,Part part)
        {
            if(vessel==null) return;
            foreach(Type type in (IEnumerable)registered.GetValue(null)) {
                try {
                    var instance=ProviderRegistry.AddMissing(list,type,()=>Activator.CreateInstance(type,new object[]{vessel}));
                    baseType.GetField("vessel").SetValue(instance,vessel);
                    if(part!=null) IntegrationState.Report(part,type.Name,"Connected / flight unverified","RPM provider instance available");
                } catch(Exception e) { IntegrationState.Report(part,type.Name,"Initialization failed",e.GetBaseException().Message); }
            }
        }
        public static void Ensure(PartModule computer)
        {
            try { EnsureCore(computer); } catch(Exception e) { IntegrationState.Report(computer?.part,"RPM provider list","Initialization failed",e.GetBaseException().Message); }
        }
        static void EnsureCore(PartModule computer)
        {
            if(!patched || computer==null || !IntegrationState.Owns(computer.part) || HighLogic.LoadedSceneIsEditor) return;
            var state=ready.GetValue(computer,_=>new Ready()); if(state.Vessel==computer.vessel && state.Vessel!=null) return;
            RegisterProviders(); Mark(computer); Fill((IList)installed.GetValue(computer),computer.vessel,computer.part);
            state.Vessel=computer.vessel;
        }
        static bool BeforeLookup(PartModule __instance,string __0,object[] __args,ref Delegate __result)
        {
            Ensure(__instance);
            if(!IntegrationState.Owns(__instance.part)) return true;
            var requested=__args.OfType<Type>().FirstOrDefault() ?? typeof(Func<double>);
            if(!HullCamBinding.TryBind(__instance.part,__0,requested,out var result,out var error)) return true;
            __result=result;
            IntegrationState.Report(__instance.part,"Method "+__0,result==null?"Method binding failed":"Connected / flight unverified",error??"HullCam native action/state delegate bound");
            return false;
        }
        static void VesselChanged(PartModule __instance) => Ensure(__instance);
        static void LookupResult(PartModule __instance,string __0,object __result)
        {
            if(!IntegrationState.Owns(__instance.part)) return;
            int separator=__0?.IndexOf(':')??-1; if(separator>0 && HullCamBinding.Slot(__0.Substring(0,separator))!=null) return;
            IntegrationState.Report(__instance.part,"Method "+__0,__result==null?"Method binding failed":"Connected / flight unverified",__result==null?"RPM could not resolve requested delegate":"RPM resolved delegate without invoking action");
        }
        static void FoundComputer(PartModule __result) => Ensure(__result);
        public static void Prepare(Part part)
        {
            if(HighLogic.LoadedSceneIsEditor) return;
            if(!patched) Install(new HarmonyLib.Harmony("local.universalivaeditor"));
            if(computerType==null || !patched) { IntegrationState.Report(part,"RPM","Not installed / unsupported","RPM computer or compatible API missing"); return; }
            var computer=part.Modules.Cast<PartModule>().FirstOrDefault(m=>computerType.IsInstanceOfType(m));
            if(computer==null) { var config=new ConfigNode("MODULE"); config.AddValue("name","RasterPropMonitorComputer"); computer=part.AddModule(config); }
            Ensure(computer);
        }
    }
}

