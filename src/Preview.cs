using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UniversalIVAEditor
{
                                                                                                
    public sealed partial class Preview : IDisposable
    {
        public sealed class Item {
            public Transform Transform; public ConfigNode Node; public string Label; public Bounds Bounds;
            public bool CameraItem,EVAItem,ReadOnly; public string Error;
            public Matrix4x4 CameraFrame; public Quaternion CameraBasis,CameraOffsetRotation; public Vector3 CameraOffset;
        }
        readonly List<Material> materials=new List<Material>();
        readonly PropVisualFactory visuals=new PropVisualFactory();
        public readonly List<Item> Items=new List<Item>();
        public readonly List<string> Warnings=new List<string>();
        public readonly List<ConfigNode> UnavailableEdits=new List<ConfigNode>();
        GameObject root,external; Transform group; Camera camera; RenderTexture image;
        public RenderTexture Image => image;
        Vector3 pivot; float distance=3,yaw=0,pitch=10; Quaternion? seatRotation; Part owner;
        const int Layer=30;
        public void Build(EditSession session,Catalog catalog,Part part)
        {
            Clear(); owner=part; root=new GameObject("UIVA_Preview"); root.transform.position=new Vector3(0,-10000,0); root.layer=Layer;
            var layout=session.Layout; var prefab=PartLoader.GetInternalPart(layout.GetValue("internal"));
            if(prefab==null) throw new InvalidOperationException("Internal model is not loaded");
                                                                                         
            var basis=new GameObject("IVA source basis").transform; basis.SetParent(root.transform,false);
            basis.localScale=LayoutCoordinates.RootScale(prefab.transform.localScale); basis.gameObject.layer=Layer;
            group=LayoutCoordinates.CreateGroup(basis); group.gameObject.layer=Layer;
            var baseline=new GameObject("Original IVA model").transform; baseline.SetParent(group,false);
            var map=new Dictionary<Transform,Transform>(); map[prefab.transform]=baseline;
            foreach(Transform source in prefab.GetComponentsInChildren<Transform>(true))
            {
                if(source==prefab.transform) continue;
                var t=new GameObject(source.name).transform; t.SetParent(map[source.parent],false);
                t.localPosition=source.localPosition; t.localRotation=source.localRotation; t.localScale=source.localScale; t.gameObject.layer=Layer;
                t.gameObject.SetActive(source.gameObject.activeSelf);
                map[source]=t; visuals.Geometry(source.gameObject,t.gameObject,false);
            }
            visuals.Geometry(prefab.gameObject,baseline.gameObject,false);
            var bindings=LayoutEngine.Bindings(prefab,catalog.Internals[layout.GetValue("internal")],Warnings.Add);
            FreeIvaCompatibility.ReconnectPaths(layout,catalog.Internals[layout.GetValue("internal")],bindings);
            foreach(var edit in layout.GetNodes("PROP_EDIT").Where(n=>!FreeIvaCompatibility.Missing(n))) if(!bindings.Any(b=>b.Index.ToString()==edit.GetValue("index"))) { UnavailableEdits.Add(edit); Warnings.Add("Saved edit ["+edit.GetValue("index")+"] cannot be previewed; editing data retained; restore the source or remove this edit before Apply."); }
            foreach(var binding in bindings)
            {
                var n=session.ExistingProp(binding.Index,catalog.Internals[layout.GetValue("internal")]);
                if(n.HasValue("path") && n.GetValue("path")!=binding.Path) { UnavailableEdits.Add(n); Warnings.Add(binding.Name+": saved hierarchy changed; original retained, individual editing unavailable"); continue; }
                if(!n.HasValue("path")) { if(layout.GetNodes("PROP_EDIT").Contains(n)) { UnavailableEdits.Add(n); Warnings.Add(binding.Name+": saved edit has no hierarchy identity; original retained"); continue; } n.AddValue("path",binding.Path); }
                var t=LayoutEngine.Delta(map[binding.Prop.transform],n);
                if(n.GetValue("delete")=="true") t.gameObject.SetActive(false);
                else Items.Add(new Item{Transform=t,Node=n,Label="["+binding.Index+"] "+binding.Name});
            }
            foreach(var n in layout.GetNodes("PROP_EDIT").Concat(layout.GetNodes("PROP_ADD")).Where(FreeIvaCompatibility.Missing)) Warnings.Add("FreeIVA source prop unavailable; edit retained for reinstall: "+n.GetValue("name"));
            foreach(var n in layout.GetNodes("PROP_ADD"))
            {
                if(FreeIvaCompatibility.Missing(n)) continue;
                if(FreeIvaCompatibility.Optional(n) && !catalog.Props.ContainsKey(n.GetValue("name"))) { Warnings.Add("Optional FreeIVA prop unavailable; placement retained: "+n.GetValue("name")); continue; }
                var prop=PartLoader.Instance.internalProps.FirstOrDefault(p=>p.propName==n.GetValue("name")); if(prop==null) { UnavailableEdits.Add(n); Warnings.Add("Missing added prop: "+n.GetValue("name")+"; editing data retained"); continue; }
                var placement=new GameObject("Added placement").transform; placement.SetParent(group,false);
                if(n.HasValue("cloneIndex"))
                {
                    var binding=bindings.FirstOrDefault(b=>b.Index.ToString()==n.GetValue("cloneIndex"));
                    if(binding==null) { UnavailableEdits.Add(n); Warnings.Add("Cannot preview cloned prop: "+n.GetValue("name")+"; editing data retained"); continue; }
                    placement.SetParent(map[binding.Prop.transform.parent],false);
                    var copy=visuals.CopyTree(binding.Prop.transform,placement);
                    PreviewNeutralPose.Apply(binding.Prop,source=>LayoutEngine.At(copy,LayoutEngine.Path(binding.Prop.transform,source)),Warnings.Add);
                    var delta=LayoutEngine.Delta(copy,n); UnityEngine.Object.DestroyImmediate(placement.gameObject.GetComponent<Renderer>());
                    Items.Add(new Item{Transform=delta,Node=n,Label="+ "+n.GetValue("name")}); continue;
                }
                var t=visuals.CopyProp(prop.transform,placement,Warnings.Add);
                Configuration.ApplyTransform(placement,n);
                if(!PropVisualFactory.Bounds(placement,out var visibleBounds,out var rendererCount)) Warnings.Add("Added Prop has no active renderable mesh: "+n.GetValue("name"));
                Log.Trace("Added preview Prop="+n.GetValue("name")+" sourceActive="+prop.gameObject.activeSelf+" copyActive="+t.gameObject.activeSelf+" renderers="+rendererCount);
                Items.Add(new Item{Transform=placement,Node=n,Label="+ "+n.GetValue("name")});
            }
            foreach(var n in layout.GetNodes("SEAT"))
            {
                if(!n.HasValue("scale") && prefab.transform.localScale!=Vector3.one) Warnings.Add("Seat "+n.GetValue("slot")+": saved scale defaults to 1. Coordinates are preserved; compare this older seat map with the source seats.");
                var marker=GameObject.CreatePrimitive(PrimitiveType.Sphere); marker.name="Seat "+n.GetValue("slot"); marker.layer=Layer;
                UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>()); marker.transform.SetParent(group,false);
                var anchor=new GameObject("Seat anchor").transform; anchor.SetParent(group,false); Configuration.ApplyTransform(anchor,n);
                marker.transform.SetParent(anchor,false); marker.transform.localScale=Vector3.one*0.12f;
                var m=new Material(Shader.Find("Unlit/Color")); m.color=Color.cyan; materials.Add(m); marker.GetComponent<Renderer>().sharedMaterial=m;
                Items.Add(new Item{Transform=anchor,Node=n,Label=marker.name});
            }
            var editedRoots=Items.Where(i=>i.Node.name=="PROP_EDIT" && layout.GetNodes("PROP_EDIT").Contains(i.Node)).Select(i=>i.Transform).ToArray();
            float maxError=0; int audited=0;
            foreach(var pair in map)
            {
                if(editedRoots.Any(t=>pair.Value==t || pair.Value.IsChildOf(t))) continue;
                var expected=prefab.transform.worldToLocalMatrix*pair.Key.localToWorldMatrix;
                var actual=group.worldToLocalMatrix*pair.Value.localToWorldMatrix;
                for(int row=0;row<4;row++) for(int col=0;col<4;col++) maxError=Mathf.Max(maxError,Mathf.Abs(expected[row,col]-actual[row,col]));
                audited++;
            }
            Log.Trace("Preview baseline audit IVA="+prefab.internalName+" transforms="+audited+" maxMatrixError="+maxError);
            if(maxError>0.002f) Warnings.Add("Preview baseline transform audit difference: "+maxError+". Check alignment before saving.");
            Configuration.ApplyTransform(group,session.Data.GetNode("TRANSFORM"));
            Log.Trace("Preview coordinate basis IVA="+prefab.internalName+" rootScale="+basis.localScale+" layoutPosition="+group.localPosition+" layoutScale="+group.localScale);
            external=new GameObject("Exterior overlay"); external.transform.SetParent(root.transform,false); external.layer=Layer;
            external.transform.localRotation=Quaternion.Inverse(Quaternion.Euler(90,180,0));
            var exteriorRoot=visuals.CopyTree(part.transform,external.transform,true,part);
            exteriorRoot.localPosition=Vector3.zero; exteriorRoot.localRotation=Quaternion.identity; partFrame=exteriorRoot;
            external.SetActive(true);
            var cameraObject=new GameObject("UIVA_PreviewCamera"); cameraObject.transform.SetParent(root.transform,false);
            camera=cameraObject.AddComponent<Camera>(); camera.enabled=false; camera.cullingMask=1<<Layer;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(0.94f,0.95f,0.96f);
            camera.nearClipPlane=0.005f; camera.farClipPlane=100; camera.fieldOfView=60;
            image=new RenderTexture(1024,768,24); image.Create(); camera.targetTexture=image;
            foreach(var angle in new[]{new Vector3(35,25,0),new Vector3(-25,200,0)})
            {
                var lightObject=new GameObject("Preview light"); lightObject.transform.SetParent(root.transform,false); lightObject.transform.localRotation=Quaternion.Euler(angle);
                var light=lightObject.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1; light.cullingMask=1<<Layer;
            }
            foreach(var prop in prefab.GetComponentsInChildren<InternalProp>(true)) PreviewNeutralPose.Apply(prop,t=>map.TryGetValue(t,out var copy)?copy:null,warning=> { if(!Warnings.Contains(warning)) Warnings.Add(warning); });
            if(pivot==Vector3.zero) pivot=root.transform.position;
            Warnings.AddRange(PropIntegration.Warnings(layout,catalog,part));
            BuildCameras(session.Data); BuildEVA(session.Data); RefreshBounds(); Log.Trace("Preview built: "+layout.GetValue("internal")+" selectable="+Items.Count);
        }
        void RefreshBounds()
        {
            foreach(var item in Items)
            {
                if(item.Transform==null) continue;
                var renderers=item.Transform.GetComponentsInChildren<Renderer>();
                var b=new Bounds(item.Transform.position,Vector3.one*0.05f);
                foreach(var r in renderers) b.Encapsulate(r.bounds); item.Bounds=b;
            }
        }
        public void Render()
        {
            if(camera==null) return;
            camera.transform.rotation=seatRotation ?? Quaternion.Euler(pitch,yaw,0);
            camera.transform.position=pivot-camera.transform.forward*distance; camera.farClipPlane=Mathf.Max(100,distance*4); camera.Render();
        }
        public void Resize(int width,int height)
        {
            float factor=Mathf.Min(1,2048f/Mathf.Max(width,height)); width=Mathf.Max(16,Mathf.RoundToInt(width*factor)); height=Mathf.Max(16,Mathf.RoundToInt(height*factor));
            if(camera==null || (image!=null && image.width==width && image.height==height)) return;
            camera.targetTexture=null; if(image) { image.Release(); UnityEngine.Object.DestroyImmediate(image); }
            image=new RenderTexture(width,height,24); image.Create(); camera.targetTexture=image; camera.aspect=(float)width/height;
        }
        public void Orbit(Vector2 delta) { seatRotation=null; yaw+=delta.x*0.3f; pitch=Mathf.Clamp(pitch+delta.y*0.3f,-89,89); }
        public void Pan(Vector2 delta) { pivot+=(-camera.transform.right*delta.x+camera.transform.up*delta.y)*distance*0.002f; }
        public void Zoom(float delta) { distance=Mathf.Clamp(distance*Mathf.Exp(delta*0.1f),0.03f,30); }
        public void Focus(Item item)
        {
            seatRotation=null; var target=item?.Transform ?? group; pivot=target.position;
            if(PropVisualFactory.Bounds(target,out var bounds,out var count)) { pivot=bounds.center; distance=Mathf.Max(0.08f,bounds.extents.magnitude/Mathf.Sin(camera.fieldOfView*Mathf.Deg2Rad*0.5f)*1.25f); }
            else distance=0.5f;
        }
        public Camera Camera => camera;
        public Ray Ray(Vector2 point,Rect rect)
        {
            float tan=Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*0.5f);
            var direction=new Vector3(((point.x-rect.x)/rect.width*2-1)*camera.aspect*tan,(1-(point.y-rect.y)/rect.height*2)*tan,1).normalized;
            return new Ray(camera.transform.position,camera.transform.rotation*direction);
        }
        public Vector3 SpawnPosition() => group.InverseTransformPoint(camera.transform.position+camera.transform.forward*Mathf.Max(0.1f,distance));
        public void SeatView(Item item)
        {
            int slot=int.Parse(item.Node.GetValue("slot"));
            var crew=owner.protoModuleCrew.ElementAtOrDefault(slot) ?? new ProtoCrewMember(ProtoCrewMember.KerbalType.Crew);
            var model=AssetBase.GetPrefab(crew.GetKerbalIVAName());
            var kerbal=model==null?null:model.GetComponent<Kerbal>();
            if(kerbal==null || kerbal.eyeTransform==null) throw new InvalidOperationException("Kerbal eye model unavailable; seat preview cannot match flight");
            var eye=kerbal.eyeTransform; var offset=Configuration.Vector(item.Node,"eyeOffset",Vector3.zero);
            if(crew.gender==ProtoCrewMember.Gender.Female) offset=offset*GameSettings.FEMALE_EYE_OFFSET_SCALE+new Vector3(GameSettings.FEMALE_EYE_OFFSET_X,GameSettings.FEMALE_EYE_OFFSET_Y,GameSettings.FEMALE_EYE_OFFSET_Z);
            var module=item.Node.GetNode("MODULE") ?? new ConfigNode();
            var body=LayoutCoordinates.Matrix(Configuration.Vector(module,"kerbalOffset",Vector3.zero),Quaternion.identity,Vector3.Scale(kerbal.transform.localScale,Configuration.Vector(module,"kerbalScale",Vector3.one)));
            var eyeParent=kerbal.transform.worldToLocalMatrix*eye.parent.localToWorldMatrix;
            var position=LayoutCoordinates.EyePosition(item.Transform.localToWorldMatrix,body,eyeParent,kerbal.capturedInitial?kerbal.eyeInitialPos:eye.localPosition,offset);
            var rotation=item.Transform.rotation*Quaternion.Inverse(kerbal.transform.rotation)*eye.rotation*Configuration.Rotation(item.Node,"viewRotation");
            seatRotation=rotation;
            distance=0.03f; pivot=position+rotation*Vector3.forward*distance;
            var e=rotation.eulerAngles; yaw=e.y; pitch=e.x>180 ? e.x-360 : e.x;
        }
        public void ExteriorTransparency(float percent) => visuals.ExteriorTransparency(percent);
        public void Exterior(bool show) { if(external) external.SetActive(show); }
        public Item Pick(Vector2 point,Rect rect)
        {
            var ray=camera.ViewportPointToRay(new Vector3((point.x-rect.x)/rect.width,1-(point.y-rect.y)/rect.height,0));
            RefreshBounds(); return Items.Where(i=>!i.CameraItem && !i.EVAItem && i.Bounds.IntersectRay(ray)).OrderBy(i=>Vector3.Distance(ray.origin,i.Bounds.center)).FirstOrDefault();
        }
        public Vector2 ScreenPoint(Vector3 world,Rect rect)
        {
            var p=camera.WorldToViewportPoint(world); return new Vector2(rect.x+p.x*rect.width,rect.y+(1-p.y)*rect.height);
        }
        public Transform Group => group;
        public void Dispose() { Clear(); pivot=Vector3.zero; }
        void Clear()
        {
            Items.Clear(); Warnings.Clear(); UnavailableEdits.Clear();
            if(camera) camera.targetTexture=null;
            if(image) { image.Release(); UnityEngine.Object.DestroyImmediate(image); } image=null; camera=null;
            if(root) UnityEngine.Object.DestroyImmediate(root); root=null;
            foreach(var m in materials) if(m) UnityEngine.Object.DestroyImmediate(m); materials.Clear();
            visuals.Dispose();
        }
    }
}








