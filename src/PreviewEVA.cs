using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed partial class Preview
    {
        void BuildEVA(ConfigNode data)
        {
            var model=owner.transform.Find("model") ?? owner.transform;
            foreach(var node in EVAConfiguration.Nodes(data)) {
                var item=new Item{Node=node,Label=EVAConfiguration.PointLabel(node),EVAItem=true};
                item.CameraFrame=node.name=="HATCH"?group.localToWorldMatrix:CameraPlacement.PreviewAnchor(partFrame.localToWorldMatrix,owner.transform.worldToLocalMatrix,model.localToWorldMatrix);
                item.CameraBasis=node.name=="HATCH"?group.rotation:partFrame.rotation*Quaternion.Inverse(owner.transform.rotation)*model.rotation;
                item.CameraOffsetRotation=Quaternion.identity;
                item.Transform=new GameObject("EVA marker "+node.GetValue("id")).transform; item.Transform.SetParent(root.transform,false);
                Items.Add(item); UpdateEVA(item);
            }
        }
        public void UpdateEVA(Item item)
        {
            if(item?.Transform==null || !item.EVAItem) return;
            item.Transform.position=item.CameraFrame.MultiplyPoint3x4(Configuration.Vector(item.Node,"position",Vector3.zero));
            item.Transform.rotation=item.CameraBasis*Configuration.Rotation(item.Node);
        }
        public Item PickEVA(Vector2 point,Rect rect) => Items.Where(i=>i.EVAItem && CameraMarkerSize(i,rect)>0)
            .Where(i=>Vector2.Distance(ScreenPoint(i.Transform.position,rect),point)<16)
            .OrderBy(i=>Vector3.Distance(camera.transform.position,i.Transform.position)).FirstOrDefault();
        public Vector3 EVAWorldPoint(Item item,Vector3 local) => item.CameraFrame.MultiplyPoint3x4(Configuration.Vector(item.Node,"position",Vector3.zero)+Configuration.Rotation(item.Node)*local);
    }
}
