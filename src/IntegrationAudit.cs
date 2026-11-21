using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed class IntegrationAudit : MonoBehaviour
    {
        public InternalModel Model;
        bool dirty;
        void Awake() { GameEvents.onVesselWasModified.Add(Modified); }
        void Modified(Vessel vessel) { if(Model?.part!=null && Model.part.vessel==vessel) dirty=true; }
        void LateUpdate() { if(!dirty) return; dirty=false; if(Model!=null && IntegrationState.Owns(Model.part)) Audit(); }
        void OnDestroy() { GameEvents.onVesselWasModified.Remove(Modified); }
        IEnumerator Start() { if(HighLogic.LoadedSceneIsEditor) yield break; yield return null; if(Model!=null && IntegrationState.Owns(Model.part)) Audit(); }
        void Audit()
        {
            var part=Model.part; var library=PresetStore.Library;
            bool farSurface=part.vessel!=null && part.vessel.parts.Any(p=>p.Modules.Cast<PartModule>().Any(m=>m.moduleName.StartsWith("FAR",StringComparison.Ordinal) && m.moduleName.IndexOf("Surface",StringComparison.OrdinalIgnoreCase)>=0));
            foreach(var prop in Model.props) {
                if(prop==null || !library.Props.TryGetValue(prop.propName??"",out var definition)) continue;
                foreach(var requirement in PropIntegration.Requirements(definition)) {
                    var adapter=requirement.GetValue("name");
                    string key="Prop "+prop.propID+" "+prop.propName+" "+(adapter??requirement.GetValue("transform"));
                    if(requirement.name=="CAMERA_REF") {
                        var slot=requirement.GetValue("transform"); if(slot=="CurrentReferenceDockingPortCamera") continue;
                        int count=part.vessel?.parts.Count(p=>p.FindModelTransform(slot)!=null) ?? 0;
                        IntegrationState.Report(part,key,count==1?"Connected / flight unverified":"Camera source missing / ambiguous",slot+" matches="+count); continue;
                    }
                    if(adapter!="JSIFAR" && adapter!="JSIChatterer" && adapter!="JSIInternalRPMButtons") continue;                            
                    try {
                    var type=AssemblyLoader.loadedAssemblies.Select(a=>a.assembly.GetType("JSI."+adapter)).FirstOrDefault(t=>t!=null);
                    if(type==null) { IntegrationState.Report(part,key,"Not installed","RPM adapter type missing"); continue; }
                    var flag=type.GetField(adapter=="JSIFAR"?"farFound":"chattererFound",BindingFlags.Public|BindingFlags.Static);
                    if(flag!=null && !(bool)flag.GetValue(null)) { IntegrationState.Report(part,key,"Not installed / incompatible API","RPM optional adapter reports dependency unavailable"); continue; }
                    foreach(var name in new[]{"actionMethod","stateMethod","method"}) {
                        var method=requirement.GetValue(name); if(string.IsNullOrEmpty(method)) continue;
                        bool found=type.GetMethods(BindingFlags.Public|BindingFlags.Instance).Any(m=>m.Name==method);
                        IntegrationState.Report(part,key+" "+method,found?"Available / flight unverified":"Method binding failed",found?"Adapter method present; native delegate results logged separately":"Adapter API does not expose method");
                    }
                    if(adapter=="JSIFAR") IntegrationState.Report(part,key+" surfaces",farSurface?"Available / flight unverified":"Unavailable on vessel",farSurface?"FAR control surface module found":"No FAR control surface module found; cockpit modules are not copied");
                    } catch(Exception e) { IntegrationState.Report(part,key,"Initialization failed",e.GetBaseException().Message); }
                }
            }
        }
    }
}

