using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
                                                                                
    public sealed class RuntimeEVA : MonoBehaviour
    {
        readonly List<GameObject> internalObjects=new List<GameObject>();
        readonly EVAExternalAnchors<Transform> externalObjects=new EVAExternalAnchors<Transform>(t=>t!=null);
        readonly HashSet<string> usedExternal=new HashSet<string>();
        readonly List<EVAEntryBinding> bindings=new List<EVAEntryBinding>();
        readonly List<InternalProp> props=new List<InternalProp>();
        readonly List<UIVAEVAHandle> handles=new List<UIVAEVAHandle>();
        Part owner; InternalModel model; Transform originalAirlock,assignedAirlock;
        public static void Apply(Part owner,InternalModel model,Transform group,ConfigNode snapshot)
        {
            if(!HighLogic.LoadedSceneIsFlight) return;
            var old=owner.GetComponent<RuntimeEVA>();
            if(old!=null && old.model==model) { old.RestoreBindings(); return; }
            var block=snapshot.GetNode("EVA"); if(block==null) { if(old!=null) old.Clear(); return; }
            var state=old??owner.gameObject.AddComponent<RuntimeEVA>();
            if(old==null) { state.owner=owner; state.originalAirlock=owner.airlock; }
                                                                               
                                                                               
            state.ClearInternal(); state.model=model; state.usedExternal.Clear();
            try {
                EVAConfiguration.Validate(snapshot);
                foreach(var port in block.GetNodes("PORT")) state.BuildPort(port,group);
                foreach(var node in block.GetNodes("LADDER")) state.BuildLadder(node);
                state.externalObjects.ReleaseUnused(state.usedExternal,state.RetireExternal);
            } catch(Exception e) {
                state.Clear(); IntegrationState.Report(owner,"EVA","Initialization failed",e.GetBaseException().Message); Log.Error("EVA placement failed: "+owner.partInfo.name,e);
            }
        }
        Transform External(ConfigNode n,string kind)
        {
            string key=kind+":"+n.GetValue("id"); usedExternal.Add(key);
            var t=externalObjects.Resolve(key,()=>new GameObject("UIVA_"+kind+"_"+n.GetValue("id")).transform);
            t.SetParent(owner.transform.Find("model")??owner.transform,false); Configuration.ApplyTransform(t,n); return t;
        }
        void BuildLadder(ConfigNode n)
        {
            var t=External(n,"Ladder"); t.localScale=Vector3.one; t.gameObject.layer=21; t.gameObject.tag="Ladder";
            var collider=t.GetComponent<BoxCollider>()??t.gameObject.AddComponent<BoxCollider>(); collider.enabled=true; collider.isTrigger=true; collider.size=EVAConfiguration.Size(n);
            IntegrationState.Report(owner,"Ladder "+n.GetValue("id"),"Connected / actual operation unverified","Invisible grab area; +Y climb, +Z facing");
        }
        public Transform EnsureExit(ConfigNode exitNode,bool boarding=true)
        {
            var exit=External(exitNode,"Airlock"); exit.localScale=Vector3.one;
            exit.localRotation=EVAConfiguration.AirlockRotation(Configuration.Rotation(exitNode)); exit.gameObject.layer=21; exit.gameObject.tag="Airlock";
            if(owner.airlock==null) { owner.airlock=exit; assignedAirlock=exit; }
            var collider=exit.GetComponent<BoxCollider>();
            if(boarding) { collider=collider??exit.gameObject.AddComponent<BoxCollider>(); collider.enabled=true; collider.isTrigger=true; collider.size=new Vector3(0.55f,0.9f,0.6f); }
            else if(collider!=null) collider.enabled=false;
            exit.gameObject.SetActive(true); return exit;
        }
        void BuildPort(ConfigNode config,Transform group)
        {
            var exitNode=config.GetNode("EXIT"); var exit=EnsureExit(exitNode,config.GetValue("boarding")!="false");
            var hatch=config.GetNode("HATCH"); var go=new GameObject("UIVA_Hatch_"+config.GetValue("id"));
            go.transform.SetParent(group,false); Configuration.ApplyTransform(go.transform,hatch); internalObjects.Add(go);
            var prop=go.AddComponent<InternalProp>(); prop.propName=go.name; prop.internalModel=model; prop.propID=model.props.Count; prop.hasModel=true;
            var handle=new GameObject("UIVA_EVA_Handle"); handle.transform.SetParent(go.transform,false); handle.layer=20;
            var hit=handle.AddComponent<BoxCollider>(); hit.size=new Vector3(0.24f,0.24f,0.04f);
                                                                             
            var free=EVAEntryBinding.Available && model.GetComponentsInChildren<InternalModule>(true).Any(m=>m.GetType().FullName=="FreeIva.InternalModuleFreeIva");
            if(free) {
                var node=new ConfigNode("MODULE"); node.AddValue("name","FreeIvaHatch"); node.AddValue("handleTransformName",handle.name);
                node.AddValue("attachNodeId",string.Empty); node.AddValue("airlockName",exit.name); node.AddValue("isEvaHatch",true); node.AddValue("inwardsDirection","0,0,1");
                var native=prop.AddModule(node); if(native==null) throw new InvalidOperationException("FreeIVA entry module could not be created");
                var binding=go.AddComponent<EVAEntryBinding>(); binding.State=this; binding.ExitConfig=Configuration.Copy(exitNode); binding.Native=native; binding.External=exit; binding.Boarding=config.GetValue("boarding")!="false";
                bindings.Add(binding);
                                                                                              
            } else {
                var click=handle.AddComponent<UIVAEVAHandle>(); click.Owner=owner; click.Exit=exit; handles.Add(click);
            }
            model.props.Add(prop); props.Add(prop);
            IntegrationState.Report(owner,"EVA "+config.GetValue("id"),"Connected / actual operation unverified",free?"FreeIVA Internal Entry linked to External Entry":"Seated Internal Entry; walking return requires a FreeIVA-supported internal");
        }
        void Update()
        {
            if(handles.Count==0 || !HighLogic.LoadedSceneIsFlight || !Input.GetMouseButtonDown(0) || CameraManager.Instance==null || CameraManager.Instance.currentCameraMode!=CameraManager.CameraMode.IVA || InternalCamera.Instance==null) return;
            var camera=InternalCamera.Instance.GetComponent<Camera>(); if(camera==null) return;
            RaycastHit hit;
            if(Physics.Raycast(camera.ScreenPointToRay(Input.mousePosition),out hit,10,(1<<16)|(1<<20),QueryTriggerInteraction.Collide)) {
                var handle=hit.collider.GetComponent<UIVAEVAHandle>(); if(handle!=null && handle.Owner==owner) handle.Activate();
            }
        }
        public static void Reset(Part owner) { var state=owner.GetComponent<RuntimeEVA>(); if(state!=null) state.Clear(); }
        public static void Initialized(InternalModel model)
        {
            var state=model.part?.GetComponent<RuntimeEVA>(); if(state!=null && state.model==model) state.RestoreBindings();
        }
        void RestoreBindings()
        {
            foreach(var binding in bindings) if(binding!=null) binding.Restore();
            if(owner!=null && owner.airlock==null && assignedAirlock!=null) owner.airlock=assignedAirlock;
        }
        public void TraceBinding(string id,string message) => Log.Trace("Entry part="+owner.partInfo.name+" external="+id+" "+message);
        static void Retire(GameObject obj)
        {
            if(obj==null) return;
                                                                               
                                                                                
            obj.name="UIVA_Retired_"+obj.GetInstanceID(); obj.SetActive(false); Destroy(obj);
        }
        void RetireExternal(Transform t)
        {
            if(owner!=null && t!=null && owner.airlock==t) owner.airlock=originalAirlock;
            if(assignedAirlock==t) assignedAirlock=null;
            if(t!=null) Retire(t.gameObject);
        }
        void ClearInternal()
        {
            foreach(var p in props) if(p!=null && model!=null) model.props.Remove(p);
            props.Clear(); handles.Clear(); bindings.Clear();
            foreach(var obj in internalObjects) Retire(obj); internalObjects.Clear(); model=null;
        }
        void Clear()
        {
            ClearInternal();
            externalObjects.ReleaseUnused(new HashSet<string>(),RetireExternal);
            usedExternal.Clear(); assignedAirlock=null;
        }
        void OnDestroy() { Clear(); }
    }
    public sealed class UIVAEVAHandle : MonoBehaviour
    {
        public Part Owner; public Transform Exit; int activatedFrame=-1;
        void OnMouseDown() { Activate(); }
        public void Activate()
        {
            if(activatedFrame==Time.frameCount || Owner==null || Exit==null || CameraManager.Instance==null || CameraManager.Instance.currentCameraMode!=CameraManager.CameraMode.IVA) return;
            activatedFrame=Time.frameCount;
            var kerbal=CameraManager.Instance.IVACameraActiveKerbal;
            if(kerbal==null || kerbal.InPart!=Owner) return;
            try {
                float level=ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex);
                bool unlocked=GameVariables.Instance.UnlockedEVA(level);
                if(Owner.vessel==null || Owner.vessel.packed || kerbal.protoCrewMember.type==ProtoCrewMember.KerbalType.Tourist || !HighLogic.CurrentGame.Parameters.Flight.CanEVA || !GameVariables.Instance.EVAIsPossible(unlocked,Owner.vessel)) {
                    ScreenMessages.PostScreenMessage("EVA is unavailable for this crew member or vessel.",5,ScreenMessageStyle.UPPER_CENTER); return;
                }
                if(FlightEVA.fetch==null) return;
                                                                                       
                var spawned=FlightEVA.fetch.spawnEVA(kerbal.protoCrewMember,Owner,Exit,false);
                if(spawned!=null) CameraManager.Instance.SetCameraFlight();
            } catch(Exception e) { Log.Error("EVA exit activation failed",e); ScreenMessages.PostScreenMessage("EVA exit failed: "+e.GetBaseException().Message,5,ScreenMessageStyle.UPPER_CENTER); }
        }
    }
}
