using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
                                                                                       
    public sealed class PropVisualFactory : IDisposable
    {
        readonly List<Material> materials=new List<Material>();
        readonly List<Mesh> bakedMeshes=new List<Mesh>();
        public int Layer=30;
        public Transform CopyProp(Transform source,Transform parent,Action<string> warning=null)
        {
            var copy=CopyTree(source,parent); copy.localPosition=Vector3.zero; copy.localRotation=Quaternion.identity; copy.localScale=Vector3.one;
            copy.gameObject.SetActive(true); var prop=source.GetComponent<InternalProp>(); if(prop!=null) PreviewNeutralPose.Apply(prop,t=>LayoutEngine.At(copy,LayoutEngine.Path(source,t)),warning); return copy;
        }
        public static bool Bounds(Transform root,out Bounds bounds,out int count)
        {
            bounds=new Bounds(root.position,Vector3.zero); count=0;
            foreach(var r in root.GetComponentsInChildren<Renderer>())
            {
                var mesh=r.GetComponent<MeshFilter>();
                if(!r.enabled || !r.gameObject.activeInHierarchy || mesh?.sharedMesh==null || mesh.sharedMesh.vertexCount==0) continue;
                if(count++==0) bounds=r.bounds; else bounds.Encapsulate(r.bounds);
            }
            return count>0;
        }
        public Transform CopyTree(Transform source,Transform parent,bool transparent=false,Part owner=null)
        {
            var t=new GameObject(source.name).transform; t.SetParent(parent,false); t.gameObject.layer=Layer;
            t.localPosition=source.localPosition; t.localRotation=source.localRotation; t.localScale=source.localScale;
            t.gameObject.SetActive(source.gameObject.activeSelf);
            Geometry(source.gameObject,t.gameObject,transparent);
            foreach(Transform child in source) if(owner==null || child.GetComponent<Part>()==null || child.GetComponent<Part>()==owner) CopyTree(child,t,transparent,owner); return t;
        }
        public void Geometry(GameObject source,GameObject destination,bool transparent)
        {
            var filter=source.GetComponent<MeshFilter>(); Renderer renderer=source.GetComponent<MeshRenderer>(); Mesh mesh=filter?.sharedMesh;
            if(mesh==null)
            {
                var skinned=source.GetComponent<SkinnedMeshRenderer>(); if(skinned==null) return;
                mesh=new Mesh(); skinned.BakeMesh(mesh); bakedMeshes.Add(mesh); renderer=skinned;
            }
            if(renderer==null) return;
            destination.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=destination.AddComponent<MeshRenderer>(); r.enabled=renderer.enabled;
            if(!transparent) r.sharedMaterials=renderer.sharedMaterials;
            else r.sharedMaterials=renderer.sharedMaterials.Select(original=>
            {
                var m=new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Sprites/Default") ?? original.shader);
                if(original.HasProperty("_MainTex")) m.mainTexture=original.GetTexture("_MainTex");
                if(m.HasProperty("_Color")) m.color=new Color(0.4f,0.8f,1,0.18f);
                materials.Add(m); return m;
            }).ToArray();
        }
        public void ExteriorTransparency(float percent)
        {
            float alpha=1-Mathf.Clamp(percent,0,100)/100f;
            foreach(var material in materials) if(material && material.HasProperty("_Color")) { var color=material.color; color.a=alpha; material.color=color; }
        }
        public void Dispose()
        {
            foreach(var m in materials) if(m) UnityEngine.Object.DestroyImmediate(m); materials.Clear();
            foreach(var m in bakedMeshes) if(m) UnityEngine.Object.DestroyImmediate(m); bakedMeshes.Clear();
        }
    }
}


