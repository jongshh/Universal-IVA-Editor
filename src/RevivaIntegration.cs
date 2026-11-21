using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UniversalIVAEditor
{
    public static class RevivaIntegration
    {
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        static Type revivaType,switchType,subtypeType,modifierType;
        static FieldInfo needUpdate,updateConfig;
        static PropertyInfo structuralTank;
        static FieldInfo internalName,subtypes,moduleID,subtypeName,subtypeTitle,currentIndex;
        static MethodInfo setupSubtype,setupGUI,updateWindow;
        static readonly Dictionary<string,List<RevivaChoice>> choices=new Dictionary<string,List<RevivaChoice>>(StringComparer.Ordinal);
        static readonly HashSet<string> reported=new HashSet<string>(StringComparer.Ordinal);
        static bool ready,refreshing,adding;
        public static bool Installed { get; private set; }
        public static bool Available => ready;
        public static string Status { get; private set; }="Reviva not installed";
        public static string Diagnostics => Status+"\n"+string.Join("\n",reported.OrderBy(x=>x,StringComparer.Ordinal).Take(20));
        static Type Find(string name) => AssemblyLoader.loadedAssemblies.Select(a=>a.assembly.GetType(name)).FirstOrDefault(t=>t!=null);
        static MethodInfo ImplementedMethod(Type type,string name)
        {
            for(var declaring=type;declaring!=null;declaring=declaring.BaseType)
            {
                var method=declaring.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly);
                if(method==null) continue;
                if(method.IsAbstract || method.GetMethodBody()==null) throw new MissingMethodException("No implemented method: "+declaring.FullName+"."+name);
                return method;
            }
            throw new MissingMethodException(type.FullName,name);
        }

        static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(RevivaIntegration),name);
        static void Report(string message) { if(reported.Add(message)) Log.Info(message); }
        public static void Invalidate() { choices.Clear(); reported.Clear(); }
        public static void Install(Harmony harmony)
        {
            revivaType=Find("Reviva.ModuleIVASwitch"); Installed=revivaType!=null;
            if(!Installed) return;
            try
            {
                switchType=Find("B9PartSwitch.ModuleB9PartSwitch");
                if(switchType==null) throw new MissingMemberException("B9PartSwitch.ModuleB9PartSwitch");
                subtypeType=switchType.Assembly.GetType("B9PartSwitch.PartSubtype",true);
                modifierType=switchType.Assembly.GetType("B9PartSwitch.ModuleModifierInfo",true);
                structuralTank=switchType.Assembly.GetType("B9PartSwitch.B9TankSettings",true).GetProperty("StructuralTankType",BindingFlags.Public|BindingFlags.Static);
                updateConfig=revivaType.GetField("updateConfig",Flags); needUpdate=revivaType.GetField("needUpdate",Flags); internalName=revivaType.GetField("internalName",Flags); subtypes=switchType.GetField("subtypes",Flags);
                moduleID=switchType.GetField("moduleID",Flags); subtypeName=subtypeType.GetField("subtypeName",Flags);
                subtypeTitle=subtypeType.GetField("title",Flags); currentIndex=switchType.GetField("currentSubtypeIndex",Flags);
                setupSubtype=subtypeType.GetMethod("Setup",new[]{switchType,typeof(bool)});
                setupGUI=AccessTools.Method(switchType,"SetupGUI"); updateWindow=AccessTools.Method(switchType,"UpdatePartActionWindow");
                var required=ImplementedMethod(revivaType,"GetRequiredInternalName");
                var changed=ImplementedMethod(revivaType,"HasInternalNameChanged");
                var update=ImplementedMethod(revivaType,"UpdateInternalConfig");
                var perform=ImplementedMethod(revivaType,"DoIVASwitch");
                if(new object[]{updateConfig,needUpdate,internalName,subtypes,moduleID,subtypeName,subtypeTitle,currentIndex,structuralTank,setupSubtype,setupGUI,required,changed,update,perform}.Any(o=>o==null)) throw new MissingMemberException("Reviva/B9 switch API");
                CreateSubtype(RevivaChoices.Subtype("UIVA_PROBE","Probe","UIVA_PROBE"));
                var matcher=switchType.Assembly.GetType("B9PartSwitch.ModuleMatcher",true);
                var prefabNode=ImplementedMethod(matcher,"FindPrefabNode");
                if(prefabNode==null) throw new MissingMemberException("B9 ModuleMatcher.FindPrefabNode");
                harmony.Patch(prefabNode,prefix:Hook(nameof(PrefabModule)));
                harmony.Patch(required,postfix:Hook(nameof(RequiredInternal)));
                harmony.Patch(changed,postfix:Hook(nameof(SelectionChanged)));
                harmony.Patch(update,postfix:Hook(nameof(ConfigUpdated)));
                harmony.Patch(perform,prefix:Hook(nameof(BeforeSwitch)));
                harmony.Patch(ImplementedMethod(switchType,"OnStart"),prefix:Hook(nameof(BeforeStart)));
                harmony.Patch(ImplementedMethod(switchType,"OnLoad"),prefix:Hook(nameof(BeforeLoad)));
                harmony.Patch(ImplementedMethod(typeof(Part),"LoadModule"),prefix:Hook(nameof(BeforePartLoad)));
                harmony.Patch(ImplementedMethod(typeof(Part),"InitializeModules"),prefix:Hook(nameof(BeforePartInitialize)));
                ready=true; Status="Reviva/B9 adapter connected; flight validation pending";
                Refresh(); Log.Info(Status);
            }
            catch(Exception e) { ready=false; Status="Reviva adapter unavailable: "+e.Message; Log.Error(Status,e); }
        }
        static List<RevivaChoice> Choices(Part part)
        {
            var name=part.partInfo.name;
            if(!choices.TryGetValue(name,out var list)) choices[name]=list=RevivaChoices.Build(PresetStore.Library,name,part.CrewCapacity,Report);
            return list;
        }
        static PartModule SwitchModule(Part part) => part?.Modules.Cast<PartModule>().FirstOrDefault(m=>revivaType!=null && revivaType.IsInstanceOfType(m));
        static RevivaChoice Choice(Part part,string key) => Choices(part).FirstOrDefault(c=>c.Key==key || c.Aliases.Contains(key));
        static string Selected(Part part) => SwitchModule(part) is PartModule m ? internalName?.GetValue(m) as string : null;
        public static string Selection(Part part)
        {
            if(!ready) return null;
            var key=Selected(part); if(!RevivaChoices.Owned(key)) return key;
            var choice=Choice(part,key); return key+":"+(choice?.Signature ?? "missing");
        }
        public static ConfigNode Resolve(Part part)
        {
            if(!ready) return null;
            var key=Selected(part); if(!RevivaChoices.Owned(key) || key==RevivaChoices.None) return null;
            var entry=Choice(part,key);
            if(entry==null) { Report("Reviva preset selection missing for "+part.partInfo.name+": "+key); return null; }
            return Configuration.Copy(entry.Snapshot);
        }
        public static void Refresh()
        {
            if(!ready || refreshing) return;
            refreshing=true;
            try
            {
                var parts=new HashSet<Part>();
                if(PartLoader.LoadedPartsList!=null) foreach(var info in PartLoader.LoadedPartsList) if(info.partPrefab!=null) parts.Add(info.partPrefab);
                if(HighLogic.LoadedSceneIsEditor && EditorLogic.fetch?.ship!=null) foreach(var part in EditorLogic.fetch.ship.parts) parts.Add(part);
                if(HighLogic.LoadedSceneIsFlight) foreach(var vessel in FlightGlobals.Vessels.Where(v=>v.loaded)) foreach(var part in vessel.parts) parts.Add(part);
                foreach(var part in parts) try { EnsurePart(part,true); } catch(Exception e) { Report("Reviva list refresh failed for "+part.partInfo?.name+": "+e.GetBaseException().Message); }
            }
            finally { refreshing=false; }
        }
        static void BeforePartLoad(Part __instance) { Guard(()=>EnsurePart(__instance,false)); }
        static void BeforePartInitialize(Part __instance) { Guard(()=>EnsurePart(__instance,false)); }
        static void BeforeLoad(PartModule __instance,ref ConfigNode __0)
        {
            if(!ready || !switchType.IsInstanceOfType(__instance) || __instance.part?.partInfo==null || (string)moduleID.GetValue(__instance)!="IVASwitch") return;
            string requested=__0.GetValue("currentSubtype");
            if(RevivaChoices.Owned(requested))
            {
                var canonical=Choice(__instance.part,requested)?.Key;
                if(canonical!=null && canonical!=requested) { __0=Configuration.Copy(__0); __0.SetValue("currentSubtype",canonical,true); requested=canonical; }
            }
            Guard(()=>Sync(__instance,false,requested));
            if(RevivaChoices.Owned(requested) && Choice(__instance.part,requested)==null)
            {
                var list=(IList)subtypes.GetValue(__instance);
                if(list.Count>0) { __0=Configuration.Copy(__0); __0.SetValue("currentSubtype",(string)subtypeName.GetValue(list[0]),true); Report("Missing Reviva preset choice; restored first available IVA for "+__instance.part.partInfo.name); }
            }
        }
        static void BeforeStart(PartModule __instance) { if(!ready || !switchType.IsInstanceOfType(__instance) || (string)moduleID.GetValue(__instance)!="IVASwitch") return; Guard(()=> { EnsurePart(__instance.part,false); Sync(__instance,false); }); }
        static void Guard(Action action) { if(!ready || adding) return; try { action(); } catch(Exception e) { Report("Reviva integration: "+e.GetBaseException().Message); } }
        static void EnsurePart(Part part,bool updateUI)
        {
            if(part?.partInfo==null || part.CrewCapacity<=0 || part.Modules.Contains("ProbeControlRoomPart")) return;
            var list=Choices(part);
            var reviva=SwitchModule(part);
            var switcher=part.Modules.Cast<PartModule>().FirstOrDefault(m=>switchType.IsInstanceOfType(m) && (string)moduleID.GetValue(m)=="IVASwitch");
            if(list.Count==0 && (reviva==null || switcher==null)) return;
            bool newSwitcher=switcher==null;
            PartModule addedReviva=null,addedSwitcher=null;
            try
            {
                if(reviva==null || switcher==null)
                {
                    string original=part.GetComponent<RuntimeState>()?.Original?.GetValue("name") ?? part.partInfo.internalConfig?.GetValue("name");
                    var prepared=new List<object>();
                    if(newSwitcher)
                    {
                        prepared.Add(CreateSubtype(RevivaChoices.Subtype("UIVA_BASE",string.IsNullOrEmpty(original)?"None":"Original IVA",original ?? RevivaChoices.None)));
                        foreach(var choice in list) prepared.Add(CreateSubtype(RevivaChoices.Subtype(choice.Key,choice.Title,choice.Key)));
                    }
                    adding=true;
                    try
                    {
                        if(reviva==null) { addedReviva=reviva=part.AddModule("ModuleIVASwitch",true); internalName.SetValue(reviva,original ?? RevivaChoices.None); }
                        if(newSwitcher)
                        {
                            addedSwitcher=switcher=part.AddModule("ModuleB9PartSwitch",true);
                            moduleID.SetValue(switcher,"IVASwitch");
                            foreach(var field in new[]{"switcherDescription","switcherDescriptionPlural","uiGroupName","uiGroupDisplayName"}) switchType.GetField(field,Flags).SetValue(switcher,field=="uiGroupName"?"IVASwitch":field=="uiGroupDisplayName"?"IVA Switch":"IVA");
                            switchType.GetField("switchInFlight",Flags).SetValue(switcher,true);
                            switchType.GetField("affectDragCubes",Flags).SetValue(switcher,false); switchType.GetField("affectFARVoxels",Flags).SetValue(switcher,false);
                            var target=(IList)subtypes.GetValue(switcher); foreach(var item in prepared) target.Add(item);
                            currentIndex.SetValue(switcher,0);
                        }
                    }
                    finally { adding=false; }
                }
                Sync(switcher,updateUI && !newSwitcher);
                if(newSwitcher && part.started) { var state=part.GetModuleStartState(); switcher.OnStart(state); switcher.OnStartFinished(state); }
            }
            catch
            {
                if(addedSwitcher!=null) part.RemoveModule(addedSwitcher);
                if(addedReviva!=null) part.RemoveModule(addedReviva);
                throw;
            }
        }
        static object BuildDefinition(Type subtype,Type modifier,object tank,ConfigNode node)
        {
            string name=Configuration.Required(node,"name");
            if(tank==null) throw new InvalidOperationException("B9 structural tank definition is unavailable");
            object item=Activator.CreateInstance(subtype);
            foreach(var field in new[]{"subtypeName","title","descriptionSummary","mirrorSymmetrySubtype"})
                subtype.GetField(field,Flags).SetValue(item,field=="subtypeName" || field=="mirrorSymmetrySubtype"?name:node.GetValue(field) ?? name);
            subtype.GetField("tankType",Flags).SetValue(item,tank);
            var modules=(IList)subtype.GetField("moduleModifierInfos",Flags).GetValue(item);
            foreach(var module in node.GetNodes("MODULE"))
            {
                var identifier=module.GetNode("IDENTIFIER") ?? throw new FormatException("Missing B9 module identifier");
                var data=module.GetNode("DATA") ?? throw new FormatException("Missing B9 module data");
                object info=Activator.CreateInstance(modifier);
                modifier.GetField("identifierNode",Flags).SetValue(info,Configuration.Copy(identifier));
                modifier.GetField("dataNode",Flags).SetValue(info,Configuration.Copy(data));
                modules.Add(info);
            }
            if(modules.Count!=1) throw new FormatException("An IVA subtype requires exactly one Reviva module binding");
            return item;
        }
        static object CreateSubtype(ConfigNode node) => BuildDefinition(subtypeType,modifierType,structuralTank.GetValue(null,null),node);
        static void Sync(PartModule switcher,bool updateUI,string requested=null)
        {
            if(switcher?.part?.partInfo==null || !switchType.IsInstanceOfType(switcher) || (string)moduleID.GetValue(switcher)!="IVASwitch") return;
            var list=(IList)subtypes.GetValue(switcher); if(list==null) throw new InvalidOperationException("B9 subtype list is unavailable");
            int oldIndex=(int)currentIndex.GetValue(switcher);
            object previous=oldIndex>=0 && oldIndex<list.Count?list[oldIndex]:null;
            string selected=requested ?? (previous==null?null:(string)subtypeName.GetValue(previous));
            var desired=Choices(switcher.part);
            selected=desired.FirstOrDefault(c=>c.Aliases.Contains(selected))?.Key ?? selected;
            var nodes=desired.ToDictionary(c=>c.Key,c=>RevivaChoices.Subtype(c.Key,c.Title,c.Key),StringComparer.Ordinal);
            var candidate=new List<object>(); var added=new List<object>();
            var titles=new Dictionary<object,string>();
            foreach(var item in list)
            {
                string name=(string)subtypeName.GetValue(item);
                if(!RevivaChoices.Owned(name) || name=="UIVA_BASE") candidate.Add(item);
                else if(nodes.TryGetValue(name,out var node)) { candidate.Add(item); titles[item]=node.GetValue("title"); nodes.Remove(name); }
            }
            if(candidate.Count==0)
            {
                var original=Fallback(switcher.part);
                var baseline=CreateSubtype(RevivaChoices.Subtype("UIVA_BASE",string.IsNullOrEmpty(original)?"None":"Original IVA",string.IsNullOrEmpty(original)?RevivaChoices.None:original));
                candidate.Add(baseline); added.Add(baseline);
            }
            foreach(var node in nodes.Values) { var item=CreateSubtype(node); candidate.Add(item); added.Add(item); }
            bool live=updateUI && switcher.part!=switcher.part.partInfo.partPrefab;
            if(live) foreach(var item in added) setupSubtype.Invoke(item,new object[]{switcher,false});
            int restored=candidate.FindIndex(item=>(string)subtypeName.GetValue(item)==selected);
            var oldTitles=titles.Keys.ToDictionary(item=>item,item=>(string)subtypeTitle.GetValue(item));
            var oldTitle=switchType.GetField("currentSubtypeTitle",Flags).GetValue(switcher);
            var reviva=SwitchModule(switcher.part);
            var oldInternal=reviva==null?null:internalName.GetValue(reviva);
            var oldPending=reviva==null?null:needUpdate.GetValue(reviva);
            var oldConfig=reviva==null?null:updateConfig.GetValue(reviva);
            RevivaListTransaction.Commit(list,candidate,()=> {
                currentIndex.SetValue(switcher,restored>=0?restored:0);
                foreach(var item in titles) subtypeTitle.SetValue(item.Key,item.Value);
                if(live)
                {
                    if(previous!=null && restored<0) subtypeType.GetMethod("DeactivateOnSwitch").Invoke(previous,null);
                    switchType.GetField("currentSubtypeTitle",Flags).SetValue(switcher,subtypeTitle.GetValue(candidate[(int)currentIndex.GetValue(switcher)]));
                    setupGUI.Invoke(switcher,null); updateWindow?.Invoke(switcher,null);
                    if(selected!=null && restored<0) switchType.GetMethod("SwitchSubtype").Invoke(switcher,new object[]{subtypeName.GetValue(candidate[0])});
                }
            },()=> {
                currentIndex.SetValue(switcher,oldIndex);
                if(reviva!=null) { internalName.SetValue(reviva,oldInternal); needUpdate.SetValue(reviva,oldPending); updateConfig.SetValue(reviva,oldConfig); }
                foreach(var item in oldTitles) subtypeTitle.SetValue(item.Key,item.Value);
                switchType.GetField("currentSubtypeTitle",Flags).SetValue(switcher,oldTitle);
            });
        }
        static bool PrefabModule(PartModule __0,ref ConfigNode __result)
        {
            if(!ready || !revivaType.IsInstanceOfType(__0) || __0.part?.partInfo?.partConfig==null) return true;
            if(__0.part.partInfo.partConfig.GetNodes("MODULE").Any(n=>n.GetValue("name")=="ModuleIVASwitch")) return true;
            __result=new ConfigNode("MODULE"); __result.AddValue("name","ModuleIVASwitch");
            __result.AddValue("internalName",Fallback(__0.part)); return false;
        }
        static string Fallback(Part part) => part.partInfo.partPrefab!=null ? part.partInfo.partPrefab.partInfo.internalConfig?.GetValue("name") ?? "" : part.GetComponent<RuntimeState>()?.Original?.GetValue("name") ?? "";
        static bool EmptySelection(Part part,string key) => key==RevivaChoices.None || (RevivaChoices.Owned(key) && Choice(part,key)==null && string.IsNullOrEmpty(Fallback(part)));
        static void RequiredInternal(PartModule __instance,ref string __result)
        {
            if(!ready) return;
            string key=(string)internalName.GetValue(__instance);
            if(EmptySelection(__instance.part,key)) { __result=RevivaChoices.None; return; }
            if(!RevivaChoices.Owned(key)) return;
            var choice=Choice(__instance.part,key);
            __result=choice?.Snapshot.GetNode("UIVA_LAYOUT").GetValue("internal") ?? Fallback(__instance.part);
        }
        static void SelectionChanged(PartModule __instance,ref bool __result)
        {
            if(!ready) return;
            var part=__instance.part; string key=(string)internalName.GetValue(__instance);
            var state=part.GetComponent<RuntimeState>();
            if(EmptySelection(part,key)) { __result=part.internalModel!=null || part.partInfo.internalConfig?.HasValue("name")==true; return; }
            if(part.internalModel==null) return;
            if(RevivaChoices.Owned(key))
            {
                if(Choice(part,key)==null) { if(state?.Snapshot!=null) __result=true; return; }
                if(state==null || state.Selection!=Selection(part)) __result=true;
            }
            else if(state?.Snapshot!=null) __result=true;
        }
        static void ConfigUpdated(PartModule __instance)
        {
            if(!ready) return;
            var part=__instance.part;
            if(EmptySelection(part,(string)internalName.GetValue(__instance))) part.partInfo.internalConfig=new ConfigNode("INTERNAL");
            var state=part.GetComponent<RuntimeState>() ?? part.gameObject.AddComponent<RuntimeState>();
            RuntimeCameras.Reset(part); RuntimeEVA.Reset(part);
            state.Original=Configuration.Copy(part.partInfo.internalConfig ?? new ConfigNode("INTERNAL"));
            state.Snapshot=null; state.Selection=null; state.Failed=false; state.Initialized=false; state.Spawned=false;
        }
        static bool BeforeSwitch(PartModule __instance)
        {
            if(!ready || !EmptySelection(__instance.part,(string)internalName.GetValue(__instance))) return true;
            var part=__instance.part;
            needUpdate.SetValue(__instance,false);
            part.DespawnIVA(); RuntimeCameras.Reset(part); RuntimeEVA.Reset(part);
            part.partInfo=new AvailablePart(part.partInfo); part.partInfo.internalConfig=new ConfigNode("INTERNAL");
            ConfigUpdated(__instance); return false;
        }
    }
}
