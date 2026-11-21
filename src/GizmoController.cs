using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
    public static class ManipulationMath
    {
        public static float ScreenSegmentDistance(Vector2 point,Vector2 a,Vector2 b)
        {
            var d=b-a; float t=d.sqrMagnitude<0.001f?0:Mathf.Clamp01(Vector2.Dot(point-a,d)/d.sqrMagnitude);
            return Vector2.Distance(point,a+d*t);
        }
        public static float Snap(float value,float step,bool enabled) => enabled?(float)Math.Round(value/step,MidpointRounding.AwayFromZero)*step:value;
        public static float ScaleFactor(float pixels,float step,bool snap) => Math.Max(0.001f,Snap((float)Math.Exp(Math.Max(-13,Math.Min(13,pixels*0.01f))),step,snap));
        public static float ScaleValue(float initial,float factor) => (float)Math.Max(float.Epsilon,Math.Min(float.MaxValue,(double)initial*factor));
        public static float AngleDelta(float previous,float current)
        { float delta=current-previous; while(delta>180) delta-=360; while(delta< -180) delta+=360; return delta; }
    }
                                                                                   
    public sealed class GizmoController : IDisposable
    {
        const int Layer=29;
        GameObject root; Camera overlay; readonly List<Mesh> meshes=new List<Mesh>(); readonly List<Material> materials=new List<Material>();
        readonly Dictionary<int,List<Renderer>> handles=new Dictionary<int,List<Renderer>>();
        int builtMode=-1; public int Mode; public Transform Target; public float Size; public Quaternion Basis;
        public Vector3 Origin=>Target.position;
        public Vector3 Axis(int index)=>Basis*(index==0?Vector3.right:index==1?Vector3.up:Vector3.forward);
        public void Configure(Transform target,int mode,bool local,Camera camera,float height,int hover,int active)
        {
            Target=target; Mode=mode;
            if(target==null) { if(root) root.SetActive(false); return; }
            if(root==null) { root=new GameObject("UIVA_GizmoOverlay"); root.layer=Layer; }
            if(builtMode!=mode) Build(mode);
            Basis=local||mode==2?target.rotation:Quaternion.identity;
            float depth=Vector3.Dot(target.position-camera.transform.position,camera.transform.forward);
            Size=Mathf.Max(0.00001f,depth*2*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*0.5f)*90/Mathf.Max(1,height));
            root.SetActive(depth>camera.nearClipPlane); float normalize=3/Mathf.Max(depth,0.00001f); root.transform.position=Quaternion.Inverse(camera.transform.rotation)*(target.position-camera.transform.position)*normalize; root.transform.rotation=Quaternion.Inverse(camera.transform.rotation)*Basis; root.transform.localScale=Vector3.one*Size*normalize;
            foreach(var pair in handles)
            {
                Color color=pair.Key==6?Color.white:pair.Key%3==0?new Color(1,0.22f,0.18f):pair.Key%3==1?new Color(0.2f,0.85f,0.3f):new Color(0.2f,0.55f,1);
                if(pair.Key==hover || pair.Key==active) color=new Color(1,0.85f,0.15f);
                foreach(var r in pair.Value) r.sharedMaterial.color=color;
            }
        }
        public void Render(Camera camera)
        {
            if(root==null || !root.activeSelf) return;
            if(overlay==null) { var go=new GameObject("UIVA_GizmoCamera"); overlay=go.AddComponent<Camera>(); overlay.enabled=false; }
            overlay.CopyFrom(camera); overlay.transform.position=Vector3.zero; overlay.transform.rotation=Quaternion.identity; overlay.nearClipPlane=0.01f; overlay.farClipPlane=100; overlay.enabled=false; overlay.clearFlags=CameraClearFlags.Depth; overlay.cullingMask=1<<Layer; overlay.targetTexture=camera.targetTexture; overlay.Render();
        }
        void Add(GameObject go,int handle,Vector3 position,Quaternion rotation,Vector3 scale)
        {
            go.layer=Layer; go.transform.SetParent(root.transform,false); go.transform.localPosition=position; go.transform.localRotation=rotation; go.transform.localScale=scale;
            var collider=go.GetComponent<Collider>(); if(collider) UnityEngine.Object.DestroyImmediate(collider);
            var material=new Material(Shader.Find("Unlit/Color")); materials.Add(material); var r=go.GetComponent<Renderer>(); r.sharedMaterial=material;
            if(!handles.ContainsKey(handle)) handles[handle]=new List<Renderer>(); handles[handle].Add(r);
        }
        GameObject MeshObject(Mesh mesh)
        { var go=new GameObject("Gizmo mesh"); go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>(); return go; }
        void Build(int mode)
        {
            while(root.transform.childCount>0) UnityEngine.Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
            foreach(var m in materials) UnityEngine.Object.DestroyImmediate(m); materials.Clear();
            foreach(var m in meshes) UnityEngine.Object.DestroyImmediate(m); meshes.Clear(); handles.Clear(); builtMode=mode;
            Mesh ring=null,cone=null;
            if(mode==1) { ring=Torus(); meshes.Add(ring); }
            if(mode==0) { cone=Cone(); meshes.Add(cone); }
            for(int i=0;i<3;i++)
            {
                var axis=i==0?Vector3.right:i==1?Vector3.up:Vector3.forward; var rot=Quaternion.FromToRotation(Vector3.up,axis);
                if(mode==1) Add(MeshObject(ring),i,Vector3.zero,rot,Vector3.one);
                else
                {
                    Add(GameObject.CreatePrimitive(PrimitiveType.Cylinder),i,axis*0.4f,rot,new Vector3(0.025f,0.4f,0.025f));
                    if(mode==0) Add(MeshObject(cone),i,axis*0.8f,rot,Vector3.one);
                    else Add(GameObject.CreatePrimitive(PrimitiveType.Cube),i,axis*0.95f,Quaternion.identity,Vector3.one*0.105f);
                }
                if(mode==0)
                {
                    var position=Vector3.one*0.28f; position[i]=0;
                    var size=Vector3.one*0.18f; size[i]=0.012f;
                    Add(GameObject.CreatePrimitive(PrimitiveType.Cube),i+3,position,Quaternion.identity,size);
                }
            }
            if(mode==2) Add(GameObject.CreatePrimitive(PrimitiveType.Cube),6,Vector3.zero,Quaternion.identity,Vector3.one*0.13f);
        }
        static readonly Vector3[][] ringPoints=CreateRingPoints();
        readonly Vector2[] pickCorners=new Vector2[4];
        Matrix4x4 pickMatrix; Vector2 pickCenter,pickScale;
        static Vector3[][] CreateRingPoints()
        {
            var rings=new Vector3[3][];
            for(int i=0;i<3;i++) {
                rings[i]=new Vector3[129]; var axis=i==0?Vector3.right:i==1?Vector3.up:Vector3.forward;
                var rotation=Quaternion.FromToRotation(Vector3.up,axis);
                for(int n=0;n<=128;n++) { float a=n*Mathf.PI*2/128; rings[i][n]=rotation*new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)); }
            }
            return rings;
        }
        Vector2 Project(Vector3 local)
        {
            var p=pickMatrix.MultiplyPoint3x4(local);
            return new Vector2(pickCenter.x+p.x/p.z*pickScale.x,pickCenter.y-p.y/p.z*pickScale.y);
        }
        public int Pick(Vector2 point,Rect rect,Camera camera)
        {
            if(Target==null || root==null || !root.activeSelf) return -1;
                                                                                    
                                                                                      
            pickMatrix=root.transform.localToWorldMatrix; pickCenter=rect.center; float half=Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*0.5f);
            pickScale=new Vector2(rect.width*0.5f/half/camera.aspect,rect.height*0.5f/half);
            int winner=-1; float best=10;
            for(int i=0;i<3;i++)
            {
                var axis=i==0?Vector3.right:i==1?Vector3.up:Vector3.forward;
                if(Mode==1)
                {
                    var previous=Project(ringPoints[i][0]);
                    for(int n=1;n<=128;n++) {
                        var next=Project(ringPoints[i][n]);
                        float d=ManipulationMath.ScreenSegmentDistance(point,previous,next); if(d<best) { best=d; winner=i; } previous=next;
                    }
                }
                else {
                    float d=ManipulationMath.ScreenSegmentDistance(point,Project(axis*0.12f),Project(axis*(Mode==0?1.02f:1.0f)));
                    if(d<best) { best=d; winner=i; }
                }
            }
            if(Mode==0) for(int i=0;i<3;i++) {
                var center=Vector3.one*0.28f; center[i]=0;
                var a=Vector3.zero; a[(i+1)%3]=0.09f; var b=Vector3.zero; b[(i+2)%3]=0.09f;
                var corners=pickCorners; corners[0]=Project(center-a-b); corners[1]=Project(center+a-b); corners[2]=Project(center+a+b); corners[3]=Project(center-a+b);
                bool inside=true; float sign=0;
                for(int j=0;j<4;j++) { var edge=corners[(j+1)%4]-corners[j]; var v=point-corners[j]; float cross=edge.x*v.y-edge.y*v.x; if(j==0) sign=cross; else if(sign*cross<0) inside=false; }
                float d=float.PositiveInfinity; for(int j=0;j<4;j++) d=Mathf.Min(d,ManipulationMath.ScreenSegmentDistance(point,corners[j],corners[(j+1)%4]));
                if(inside && Mathf.Abs(sign)>0.01f || d<6) return i+3;
            }
            if(Mode==2 && Vector2.Distance(point,Project(Vector3.zero))<12) return 6;
            return winner;
        }
        public static bool RelativePlanePoint(Ray ray,Vector3 origin,Vector3 normal,out Vector3 point)
        {
            float denominator=Vector3.Dot(normal,ray.direction);
            var relative=ray.origin-origin;
            if(Mathf.Abs(denominator)>0.00001f) { float distance=-Vector3.Dot(normal,relative)/denominator; if(distance>=0) { point=relative+ray.direction*distance; return true; } }
            point=Vector3.zero; return false;
        }
        public static bool PlanePoint(Ray ray,Vector3 origin,Vector3 normal,out Vector3 point)
        { var plane=new Plane(normal,origin); if(plane.Raycast(ray,out var distance)) { point=ray.GetPoint(distance); return true; } point=origin; return false; }
        static Mesh Cone()
        {
            const int n=24; var vertices=new Vector3[n*6]; var triangles=new int[n*6];
            for(int i=0;i<n;i++)
            {
                float a=i*Mathf.PI*2/n,b=(i+1)*Mathf.PI*2/n;
                vertices[i*3]=new Vector3(Mathf.Cos(a)*0.075f,0,Mathf.Sin(a)*0.075f);
                vertices[i*3+1]=new Vector3(0,0.22f,0); vertices[i*3+2]=new Vector3(Mathf.Cos(b)*0.075f,0,Mathf.Sin(b)*0.075f);
                triangles[i*3]=i*3; triangles[i*3+1]=i*3+1; triangles[i*3+2]=i*3+2;
                int cap=n*3+i*3; vertices[cap]=Vector3.zero; vertices[cap+1]=vertices[i*3]; vertices[cap+2]=vertices[i*3+2];
                triangles[cap]=cap; triangles[cap+1]=cap+1; triangles[cap+2]=cap+2;
            }
            var mesh=new Mesh{name="UIVA arrow"}; mesh.vertices=vertices; mesh.triangles=triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        static Mesh Torus()
        {
            const int n=96,m=8; var vertices=new Vector3[n*m]; var triangles=new int[n*m*6];
            for(int i=0;i<n;i++) for(int j=0;j<m;j++)
            {
                float a=i*Mathf.PI*2/n,b=j*Mathf.PI*2/m; float radius=1+0.014f*Mathf.Cos(b);
                vertices[i*m+j]=new Vector3(Mathf.Cos(a)*radius,0.014f*Mathf.Sin(b),Mathf.Sin(a)*radius);
                int k=(i*m+j)*6,p=i*m+j,q=((i+1)%n)*m+j,r=((i+1)%n)*m+(j+1)%m,s=i*m+(j+1)%m;
                triangles[k]=p; triangles[k+1]=s; triangles[k+2]=q; triangles[k+3]=s; triangles[k+4]=r; triangles[k+5]=q;
            }
            var mesh=new Mesh{name="UIVA rotation ring"}; mesh.vertices=vertices; mesh.triangles=triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public void Dispose()
        {
            if(overlay) { overlay.targetTexture=null; UnityEngine.Object.DestroyImmediate(overlay.gameObject); } overlay=null;
            if(root) UnityEngine.Object.DestroyImmediate(root); root=null; Target=null; builtMode=-1;
            foreach(var m in materials) if(m) UnityEngine.Object.DestroyImmediate(m); materials.Clear();
            foreach(var m in meshes) if(m) UnityEngine.Object.DestroyImmediate(m); meshes.Clear(); handles.Clear();
        }
    }
}





