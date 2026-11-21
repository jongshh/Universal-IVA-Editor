using System;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed partial class EditorWindow
    {
        bool evaPanel;
        void SelectEVA(ConfigNode n) {
            EndPointer(true); selectedKey=Key(n); selected=n; fields.Clear();
            selectedItem=preview?.Items.FirstOrDefault(i=>Key(i.Node)==selectedKey);
            if(n.name!="LADDER" && mode==2) mode=0;
        }
        void DrawEVAList()
        {
            GUI.enabled=session!=null && preview!=null;
            if(GUILayout.Button("Add Entrance")) Run(()=> {
                EndPointer(true); session.Checkpoint();
                var block=session.Data.GetNode("EVA")??session.Data.AddNode("EVA");
                var port=EVAConfiguration.NewPort(preview.SpawnPosition(),preview.SpawnCameraPosition()); block.AddNode(port);
                selectedKey=Key(port.GetNode("HATCH")); if(mode==2) mode=0; Changed(false);
                message="Place Internal Entry inside and External Entry outside. Arrow head: EVA Entry; tail: IVA Entry.";
            });
            if(GUILayout.Button("Add Ladder area")) Run(()=> {
                EndPointer(true); session.Checkpoint(); var block=session.Data.GetNode("EVA")??session.Data.AddNode("EVA");
                var n=block.AddNode("LADDER"); n.AddValue("id",Guid.NewGuid().ToString("N"));
                EditSession.SetVector(n,"position",preview.SpawnCameraPosition()); EditSession.SetRotation(n,Quaternion.identity); EditSession.SetVector(n,"scale",new Vector3(0.4f,1,0.3f));
                selectedKey=Key(n); Changed(false);
                message="Ladder area added. Place it over the EVA spawn marker to allow grabbing.";
            }); GUI.enabled=true;
            foreach(var port in session?.Data.GetNode("EVA")?.GetNodes("PORT")??new ConfigNode[0]) {
                GUILayout.Label(EVAConfiguration.EntranceTitle(port)+" ["+port.GetValue("id").Substring(0,Math.Min(6,port.GetValue("id").Length))+"]");
                foreach(var name in new[]{"HATCH","EXIT"}) {
                    var n=port.GetNode(name); if(n==null) continue;
                    bool active=selectedKey==Key(n);
                    if(GUILayout.Toggle(active,name=="HATCH"?"Internal Entry":"External Entry","Button") && !active) SelectEVA(n);
                }
            }
            foreach(var n in session?.Data.GetNode("EVA")?.GetNodes("LADDER")??new ConfigNode[0]) {
                bool active=selectedKey==Key(n); if(GUILayout.Toggle(active,"Ladder "+n.GetValue("id").Substring(0,Math.Min(6,n.GetValue("id").Length)),"Button") && !active) SelectEVA(n);
            }
        }
        void DrawEVAInspector()
        {
            selected=FindSelected(); selectedItem=preview?.Items.FirstOrDefault(i=>Key(i.Node)==selectedKey);
            if(!EVAConfiguration.Point(selected)) { GUILayout.Label("Select an Entry or add an Entrance."); return; }
            GUILayout.Label(selected.name=="HATCH"?"Internal Entry — IVA local":selected.name=="EXIT"?"External Entry — part model local":"Ladder — part model local");
            GUILayout.Label(selected.name=="HATCH"?"Head: EVA Entry · Tail: IVA Entry. Return: 0.3 m inward.":selected.name=="EXIT"?"Head: EVA Entry · Tail: IVA Entry. Spawn: 0.18 m outward.":"Facing arrow: Kerbal view direction. Up: climb axis.");
            VectorFields("Position (m)","position",Configuration.Vector(selected,"position",Vector3.zero));
            VectorFields("Rotation °","euler",Configuration.Rotation(selected).eulerAngles);
            if(selected.name=="LADDER") VectorFields("Width / height / depth (m)","scale",EVAConfiguration.Size(selected));
            var port=EVAConfiguration.Port(session.Data,selected);
            if(port!=null) {
                bool board=port.GetValue("boarding")!="false",next=GUILayout.Toggle(board,"Allow boarding from outside");
                if(next!=board) { session.Checkpoint(); port.SetValue("boarding",next?"true":"false",true); Changed(false); }
            }
            if(GUILayout.Button(selected.name=="LADDER"?"Delete Ladder":"Delete Entrance")) {
                EndPointer(true); session.Checkpoint(); session.Data.GetNode("EVA").RemoveNode(port??selected); selectedKey=""; selectedItem=null; Changed(false);
            }
            GUILayout.Label("Apply for Part activates these points for this part type.");
        }
        void MarkerLabel(Vector3 point,string text,Rect rect,bool right)
        {
            if(Vector3.Dot(point-preview.Camera.transform.position,preview.Camera.transform.forward)<=preview.Camera.nearClipPlane) return;
            var screen=preview.ScreenPoint(point,rect);
            GUI.Label(new Rect(screen.x+(text=="Up"?-12:right?7:-100),screen.y-10,text=="Up"?40:98,22),text);
        }
        void MarkerLine(Vector3 start,Vector3 end,Rect rect)
        {
            var camera=preview.Camera; float near=camera.nearClipPlane*1.1f;
            float a=Vector3.Dot(start-camera.transform.position,camera.transform.forward),b=Vector3.Dot(end-camera.transform.position,camera.transform.forward);
            if(a<near && b<near) return;
            if(a<near) start=Vector3.Lerp(start,end,(near-a)/(b-a));
            else if(b<near) end=Vector3.Lerp(start,end,(near-a)/(b-a));
            Line(preview.ScreenPoint(start,rect),preview.ScreenPoint(end,rect));
        }
        void DrawEVAMarkers(Rect rect)
        {
            if(Event.current.type!=EventType.Repaint || preview==null) return;
            var color=GUI.color; GUI.BeginClip(rect);
            try {
                var r=new Rect(0,0,rect.width,rect.height);
                foreach(var item in preview.Items.Where(i=>i.EVAItem)) {
                    float size=preview.CameraMarkerSize(item,r); if(size<=0) continue;
                    GUI.color=Key(item.Node)==selectedKey?Color.yellow:item.Node.name=="HATCH"?Color.cyan:item.Node.name=="EXIT"?new Color(1,0.5f,0.15f):Color.green;
                    var t=item.Transform; var center=preview.ScreenPoint(t.position,r);
                    Vector3[] corners;
                    if(item.Node.name=="LADDER") {
                        var ext=EVAConfiguration.Size(item.Node)*0.5f;
                        corners=Enumerable.Range(0,8).Select(i=>preview.EVAWorldPoint(item,Vector3.Scale(ext,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)))).ToArray();
                        for(int i=0;i<8;i++) for(int bit=1;bit<=4;bit*=2) if((i&bit)==0) MarkerLine(corners[i],corners[i|bit],r);
                    } else {
                        var right=t.right*size; var up=t.up*size;
                        corners=new[]{t.position-right-up,t.position+right-up,t.position+right+up,t.position-right+up};
                        for(int i=0;i<4;i++) MarkerLine(corners[i],corners[(i+1)%4],r);
                    }
                    if(item.Node.name=="LADDER") {
                                                                                            
                        var head=t.position+t.forward*size*4;
                        MarkerLine(t.position,head,r);
                        foreach(var wing in new[]{t.right,-t.right,t.up,-t.up})
                            MarkerLine(head,head-t.forward*size+wing*size*0.55f,r);
                        MarkerLabel(head,"Facing",r,true);
                    } else {
                        var direction=t.rotation*EVAConfiguration.OutwardAxis(item.Node.name);
                        var head=t.position+direction*size*4; var tail=t.position-direction*size*4;
                        MarkerLine(head,tail,r);
                        MarkerLine(head,head-direction*size+t.right*size*0.55f,r);
                        MarkerLine(head,head-direction*size-t.right*size*0.55f,r);
                        MarkerLabel(head,"EVA Entry",r,true);
                        MarkerLabel(tail,"IVA Entry",r,false);
                    }
                    var above=t.position+t.up*size*2;
                    MarkerLabel(above,"Up",r,true);
                    if(item.Node.name=="EXIT") {
                        var spawn=preview.ScreenPoint(preview.EVAWorldPoint(item,Vector3.forward*0.18f),r);
                        Line(spawn-Vector2.one*4,spawn+Vector2.one*4); Line(spawn+new Vector2(-4,4),spawn+new Vector2(4,-4));
                        var port=EVAConfiguration.Port(session.Data,item.Node); var hatch=preview.Items.FirstOrDefault(i=>i.Node==port?.GetNode("HATCH"));
                        if(hatch!=null) { GUI.color=new Color(0.5f,0.5f,0.5f,0.7f); MarkerLine(t.position,hatch.Transform.position,r); }
                    }
                }
            } finally { GUI.EndClip(); GUI.color=color; }
        }
    }
}
