using System.Collections.Generic;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed class DeletedPropVisibility : MonoBehaviour
    {
        readonly List<GameObject> roots=new List<GameObject>();
        readonly List<Renderer> renderers=new List<Renderer>();
        readonly List<Collider> colliders=new List<Collider>();
        readonly List<Renderer> rendererBuffer=new List<Renderer>();
        readonly List<Collider> colliderBuffer=new List<Collider>();
        int scannedFrame=-1,layerMask;
        bool initialized;
        public void Track(GameObject root) { if(!roots.Contains(root)) { roots.Add(root); scannedFrame=-1; } }
        public bool Contains(Transform target) { foreach(var root in roots) if(root!=null && (target==root.transform || target.IsChildOf(root.transform))) return true; return false; }
        public void Retire() { initialized=true; Enforce(); }
        void OnEnable() { Camera.onPreCull+=BeforeRender; }
        void OnDisable() { Camera.onPreCull-=BeforeRender; }
        void BeforeRender(Camera camera) { if(initialized) { Refresh(); if((camera.cullingMask & layerMask)!=0) Enforce(); } }
        void LateUpdate() { if(initialized) Enforce(); }
        void FixedUpdate() { if(initialized) Enforce(); }
        void Refresh()
        {
            if(scannedFrame==Time.frameCount) return;
            scannedFrame=Time.frameCount; renderers.Clear(); colliders.Clear(); layerMask=0;
            foreach(var root in roots) if(root) {
                rendererBuffer.Clear(); colliderBuffer.Clear();
                root.GetComponentsInChildren(true,rendererBuffer); root.GetComponentsInChildren(true,colliderBuffer);
                renderers.AddRange(rendererBuffer); colliders.AddRange(colliderBuffer);
            }
            foreach(var renderer in renderers) if(renderer) layerMask|=1<<renderer.gameObject.layer;
        }
        void Enforce()
        {
            Refresh();
            foreach(var renderer in renderers) if(renderer && renderer.enabled) renderer.enabled=false;
            foreach(var collider in colliders) if(collider && collider.enabled) collider.enabled=false;
        }
    }
}

