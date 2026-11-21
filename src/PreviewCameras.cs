using System;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed partial class Preview
    {
        Transform partFrame;
        void BuildCameras(ConfigNode snapshot)
        {
            foreach(var node in snapshot.GetNode("INTEGRATION")?.GetNodes("CAMERA") ?? new ConfigNode[0]) {
                var item=new Item{Node=node,Label=node.GetValue("slot"),CameraItem=true}; Items.Add(item);
                try {
                    var pose=CameraPlacement.Resolve(owner,node);
                    item.CameraFrame=CameraPlacement.PreviewAnchor(partFrame.localToWorldMatrix,owner.transform.worldToLocalMatrix,pose.Anchor.localToWorldMatrix);
                    item.CameraBasis=partFrame.rotation*Quaternion.Inverse(owner.transform.rotation)*pose.Anchor.rotation;
                    item.CameraOffset=pose.Position-Configuration.Vector(node,"position",Vector3.zero);
                    item.CameraOffsetRotation=pose.Rotation*Quaternion.Inverse(Configuration.Rotation(node)); item.ReadOnly=pose.ReadOnly;
                    item.Transform=new GameObject("Camera marker "+item.Label).transform; item.Transform.SetParent(root.transform,false);
                    UpdateCamera(item);
                } catch(Exception e) { item.Error=e.Message; item.ReadOnly=true; Warnings.Add(item.Label+": "+e.Message); }
            }
        }
        public void UpdateCamera(Item item)
        {
            if(item?.Transform==null || !item.CameraItem) return;
            item.Transform.position=item.CameraFrame.MultiplyPoint3x4(item.CameraOffset+Configuration.Vector(item.Node,"position",Vector3.zero));
            item.Transform.rotation=item.CameraBasis*item.CameraOffsetRotation*Configuration.Rotation(item.Node);
        }
        public Vector3 SpawnCameraPosition()
        {
            var anchor=owner.transform.Find("model") ?? throw new InvalidOperationException("Part model root missing");
            var frame=CameraPlacement.PreviewAnchor(partFrame.localToWorldMatrix,owner.transform.worldToLocalMatrix,anchor.localToWorldMatrix);
            return frame.inverse.MultiplyPoint3x4(camera.transform.position+camera.transform.forward*Mathf.Max(0.1f,distance));
        }
        public ConfigNode LocalCamera(Item item)
        {
            if(item?.Transform==null || item.Error!=null) throw new InvalidOperationException("Camera source pose unavailable");
            if(item.ReadOnly && item.Node.GetValue("provider")=="rpm") throw new InvalidOperationException("Choose an unused output slot in Advanced before converting this existing RPM source.");
            var model=owner.transform.Find("model") ?? throw new InvalidOperationException("Part model root missing");
            var frame=CameraPlacement.PreviewAnchor(partFrame.localToWorldMatrix,owner.transform.worldToLocalMatrix,model.localToWorldMatrix);
            var basis=partFrame.rotation*Quaternion.Inverse(owner.transform.rotation)*model.rotation;
            var copy=Configuration.Copy(item.Node); copy.SetValue("provider","rpm",true); copy.SetValue("source","local",true); copy.RemoveValue("anchor"); copy.RemoveValue("camera");
            EditSession.SetVector(copy,"position",frame.inverse.MultiplyPoint3x4(item.Transform.position));
            EditSession.SetRotation(copy,Quaternion.Inverse(basis)*item.Transform.rotation); return copy;
        }
        public float CameraMarkerSize(Item item,Rect rect)
        {
            if(item.Transform==null) return 0;
            float depth=Vector3.Dot(item.Transform.position-camera.transform.position,camera.transform.forward);
            return depth<=camera.nearClipPlane?0:depth*2*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*0.5f)*18/Mathf.Max(1,rect.height);
        }
        public Item PickCamera(Vector2 point,Rect rect)
        {
            return Items.Where(i=>i.CameraItem && CameraMarkerSize(i,rect)>0).Where(i=>Vector2.Distance(ScreenPoint(i.Transform.position,rect),point)<=13)
                .OrderBy(i=>Vector3.Distance(camera.transform.position,i.Transform.position)).FirstOrDefault();
        }
    }
}

