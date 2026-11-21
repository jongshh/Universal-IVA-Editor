using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed class PropThumbnailCache : IDisposable
    {
        sealed class Entry { public RenderTexture Image; public string Error; public long Used; }
        readonly Dictionary<string,Entry> cache=new Dictionary<string,Entry>();
        readonly Queue<string> queue=new Queue<string>(); readonly HashSet<string> pending=new HashSet<string>();
        long tick; int renderedFrame=-1,visibleFrame=-1;
        public void BeginVisibleFrame() { queue.Clear(); pending.Clear(); visibleFrame=Time.frameCount; }
        public RenderTexture Get(string name)
        {
            if(cache.TryGetValue(name,out var entry)) { entry.Used=++tick; return entry.Image; }
            if(pending.Add(name)) queue.Enqueue(name); return null;
        }
        public string Error(string name)=>cache.TryGetValue(name,out var entry)?entry.Error:null;
        public void RenderNext()
        {
            if(queue.Count==0 || renderedFrame==Time.frameCount || Time.frameCount-visibleFrame>2) return; renderedFrame=Time.frameCount;
            string name=queue.Dequeue(); pending.Remove(name); var entry=new Entry{Used=++tick};
            GameObject root=null; Camera camera=null; var factory=new PropVisualFactory{Layer=28};
            try
            {
                var prop=PartLoader.Instance.internalProps.FirstOrDefault(p=>p.propName==name);
                if(prop==null) throw new InvalidOperationException("Prop model is not loaded");
                root=new GameObject("UIVA_Thumbnail"); root.transform.position=new Vector3(0,-20000,0);
                var model=factory.CopyProp(prop.transform,root.transform);
                if(!PropVisualFactory.Bounds(model,out var bounds,out var count)) throw new InvalidOperationException("No active renderable mesh");
                camera=new GameObject("UIVA_ThumbnailCamera").AddComponent<Camera>(); camera.enabled=false; camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(0.88f,0.9f,0.92f); camera.cullingMask=1<<28; camera.fieldOfView=35; camera.nearClipPlane=0.0001f; camera.farClipPlane=10000; camera.aspect=1;
                camera.transform.rotation=Quaternion.Euler(12,165,0); float distance=Mathf.Max(0.01f,bounds.extents.magnitude/Mathf.Sin(camera.fieldOfView*Mathf.Deg2Rad*0.5f)*1.15f);
                camera.transform.position=bounds.center-camera.transform.forward*distance;
                foreach(var angle in new[]{new Vector3(30,30,0),new Vector3(-30,200,0)})
                {
                    var go=new GameObject("Thumbnail light"); go.transform.SetParent(root.transform,false); go.transform.rotation=Quaternion.Euler(angle);
                    var light=go.AddComponent<Light>(); light.type=LightType.Directional; light.cullingMask=1<<28; light.intensity=1;
                }
                entry.Image=new RenderTexture(128,128,16); entry.Image.Create(); camera.targetTexture=entry.Image; camera.Render(); camera.targetTexture=null;
            }
            catch(Exception e) { entry.Error=e.Message; if(entry.Image) { entry.Image.Release(); UnityEngine.Object.DestroyImmediate(entry.Image); entry.Image=null; } Log.Trace("Thumbnail "+name+": "+e.Message); }
            finally { if(camera) UnityEngine.Object.DestroyImmediate(camera.gameObject); if(root) UnityEngine.Object.DestroyImmediate(root); factory.Dispose(); }
            cache[name]=entry;
            while(cache.Count>128)
            {
                var oldest=cache.OrderBy(p=>p.Value.Used).First(); Release(oldest.Value); cache.Remove(oldest.Key);
            }
        }
        static void Release(Entry entry) { if(entry.Image) { entry.Image.Release(); UnityEngine.Object.DestroyImmediate(entry.Image); } }
        public void Dispose() { foreach(var entry in cache.Values) Release(entry); cache.Clear(); queue.Clear(); pending.Clear(); }
    }
}

