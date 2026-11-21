using System;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed partial class EditorWindow
    {
        bool connections,cameraAdvanced;
        string cameraSlotChoice; GUIStyle cameraNorthStyle;
        readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>> cameraRenames=new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string,string>>();
        void RestoreCameraSelection()
        {
            if(!selectedKey.StartsWith("camera:")) return;
            var nodes=session.Data.GetNode("INTEGRATION")?.GetNodes("CAMERA") ?? new ConfigNode[0];
            selectedKey=CameraPlacement.RestoreSelection(selectedKey,nodes.Select(Key).ToArray(),cameraRenames) ?? "";
            if(selectedKey.Length==0) { selected=null; selectedItem=null; fields.Clear(); return; }
            selected=FindSelected(); selectedItem=preview?.Items.FirstOrDefault(i=>Key(i.Node)==selectedKey);
            if(selected==null) { selectedKey=""; selectedItem=null; fields.Clear(); }
            else if(mode==2) { mode=0; hoverAxis=-1; }
        }
        void SelectCamera(ConfigNode node)
        {
            EndPointer(true); selectedKey=Key(node); selected=node;
            selectedItem=preview?.Items.FirstOrDefault(i=>Key(i.Node)==selectedKey); fields.Clear();
            if(Log.DebugEnabled && !CameraPlacement.Simple(node)) cameraAdvanced=true;
            if(mode==2) { mode=0; hoverAxis=-1; }
        }
        void ReplaceCamera(ConfigNode original,ConfigNode replacement)
        {
            var data=Configuration.Copy(session.Data); var integration=data.GetNode("INTEGRATION");
            var nodes=integration.GetNodes("CAMERA"); integration.RemoveNodes("CAMERA");
            foreach(var node in nodes) integration.AddNode(Configuration.Copy(node.GetValue("slot")==original.GetValue("slot")?replacement:node));
            IntegrationConfiguration.Validate(data); EndPointer(true);
            if(Key(original)!=Key(replacement)) {
                cameraRenames.Add(new System.Collections.Generic.KeyValuePair<string,string>(Key(original),Key(replacement)));
                if(cameraRenames.Count>64) cameraRenames.RemoveAt(0);
            }
            session.Replace(data); selectedKey=Key(replacement); Changed(false);
        }
        void DrawConnections()
        {
            GUILayout.Label("Camera");
            if(Log.DebugEnabled) {
                int next=GUILayout.Toolbar(cameraAdvanced?1:0,new[]{"Simple","Advanced"});
                if((next==1)!=cameraAdvanced) { EndPointer(true); cameraAdvanced=next==1; fields.Clear(); }
            }
            bool advanced=Log.DebugEnabled && cameraAdvanced;
            var required=IntegrationConfiguration.Slots(session.Layout,catalog).ToArray();
            var integration=session.Data.GetNode("INTEGRATION"); var nodes=integration?.GetNodes("CAMERA") ?? new ConfigNode[0];
            var choices=CameraPlacement.SlotChoices(required,nodes.Select(n=>n.GetValue("slot")).ToArray());
            GUILayout.Label("Choose slot to add");
            foreach(var slot in choices) {
                bool occupied=nodes.Any(n=>n.GetValue("slot")==slot);
                var old=GUI.enabled; GUI.enabled=old && !occupied;
                bool chosen=GUILayout.Toggle(cameraSlotChoice==slot,slot+(occupied?" (assigned)":""),"Button");
                if(chosen && cameraSlotChoice!=slot) cameraSlotChoice=slot;
                GUI.enabled=old;
            }
            GUI.enabled=cameraSlotChoice!=null && !nodes.Any(n=>n.GetValue("slot")==cameraSlotChoice) && preview!=null;
            if(GUILayout.Button("Add camera")) Run(()=> {
                var position=preview.SpawnCameraPosition();
                var node=new ConfigNode("CAMERA"); node.AddValue("slot",cameraSlotChoice); node.AddValue("provider","rpm"); node.AddValue("source","local"); node.AddValue("enabled","true");
                EditSession.SetVector(node,"position",position); EditSession.SetRotation(node,Quaternion.identity);
                var data=Configuration.Copy(session.Data); var target=data.GetNode("INTEGRATION") ?? data.AddNode("INTEGRATION"); target.AddNode(node);
                IntegrationConfiguration.Validate(data); EndPointer(true); session.Replace(data); selectedKey=Key(node); cameraSlotChoice=null; Changed(false);
                if(mode==2) mode=0; message="Camera added. Export to save or Apply for Part to activate.";
            });
            GUI.enabled=true;
            foreach(var node in nodes) {
                bool active=selectedKey==Key(node);
                if(GUILayout.Toggle(active,node.GetValue("slot")+(node.GetValue("enabled")=="false"?" (disabled)":""),"Button") && !active) SelectCamera(node);
            }
            selected=FindSelected(); if(selected?.name!="CAMERA") return;
            var camera=selected; string slotName=camera.GetValue("slot"),key="camera:"+slotName+":";
            selectedItem=preview?.Items.FirstOrDefault(i=>Key(i.Node)==selectedKey);
            if(selectedItem?.Error!=null) GUILayout.Label(selectedItem.Error);
            else if(selectedItem?.ReadOnly==true) GUILayout.Label("Existing source position (read-only).");
            if(!advanced && !CameraPlacement.Simple(camera)) {
                GUILayout.Label("This camera uses an Advanced source.");
                GUI.enabled=selectedItem?.Transform!=null && selectedItem.Error==null;
                if(GUILayout.Button("Convert to Local camera")) Run(()=> { var copy=preview.LocalCamera(selectedItem); ReplaceCamera(camera,copy); message="Local camera set. Undo restores the previous source."; });
                GUI.enabled=true;
            } else if(!advanced) {
                if(camera.GetValue("enabled")=="false" && GUILayout.Button("Enable camera")) Run(()=> { var copy=Configuration.Copy(camera); copy.SetValue("enabled","true",true); ReplaceCamera(camera,copy); });
                GUI.enabled=selectedItem?.Transform!=null && selectedItem.Error==null;
                VectorFields("Position","position",Configuration.Vector(camera,"position",Vector3.zero));
                VectorFields("Rotation °","euler",Configuration.Rotation(camera).eulerAngles); GUI.enabled=true;
            } else DrawAdvancedCamera(camera,key);
            if(GUILayout.Button("Remove camera")) {
                EndPointer(true); session.Checkpoint(); session.Data.GetNode("INTEGRATION").RemoveNode(camera);
                selectedKey=""; selectedItem=null; Changed(false);
            }
        }
        void DrawAdvancedCamera(ConfigNode node,string key)
        {
            int provider=GUILayout.SelectionGrid(CameraFieldIndex(key,"provider",node.GetValue("provider"),new[]{"rpm","hullcam"}),new[]{"RPM video","HullCam switching"},1);
            string nextProvider=provider==0?"rpm":"hullcam"; fields[key+"provider"]=nextProvider;
            var sources=nextProvider=="rpm"?new[]{"local","existing-rpm","existing-hullcam"}:new[]{"local","existing-hullcam"};
            var labels=nextProvider=="rpm"?new[]{"Local position","Existing RPM","Existing HullCam"}:new[]{"Local position","Existing HullCam"};
            int input=GUILayout.SelectionGrid(CameraFieldIndex(key,"source",node.GetValue("source"),sources),labels,1); string source=sources[input]; fields[key+"source"]=source;
            ConnectionField(key,"slot",node.GetValue("slot"));
            if(source=="local") ConnectionField(key,"anchor",node.GetValue("anchor")??"","Blank uses the part model root; offsets use anchor axes.");
            else {
                ConnectionField(key,"camera",node.GetValue("camera")??"");
                foreach(var choice in RuntimeCameras.Choices(module.part,source)) if(GUILayout.Button("Use "+choice)) fields[key+"camera"]=choice;
            }
            bool readOnly=nextProvider=="hullcam" && source=="existing-hullcam";
            if(!readOnly) {
                ConnectionField(key,"position",node.GetValue("position")??"0,0,0");
                var angles=Configuration.Rotation(node).eulerAngles;
                ConnectionField(key,"rotation degrees",string.Join(",",new[]{angles.x,angles.y,angles.z}.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture))));
            }
            if(GUILayout.Button("Update camera")) Run(()=> {
                var copy=Configuration.Copy(node); copy.SetValue("slot",fields[key+"slot"],true); copy.SetValue("enabled","true",true); copy.SetValue("provider",nextProvider); copy.SetValue("source",source);
                copy.RemoveValue("anchor"); copy.RemoveValue("camera");
                if(source=="local") copy.SetValue("anchor",fields[key+"anchor"],true); else copy.SetValue("camera",fields[key+"camera"],true);
                var xyz=readOnly?new float[3]:Configuration.Numbers(fields[key+"position"],3); var euler=readOnly?new float[3]:Configuration.Numbers(fields[key+"rotation degrees"],3);
                EditSession.SetVector(copy,"position",new Vector3(xyz[0],xyz[1],xyz[2])); EditSession.SetRotation(copy,Quaternion.Euler(euler[0],euler[1],euler[2]));
                ReplaceCamera(node,copy); message="Camera updated. Export to save or Apply for Part to activate.";
            });
        }
        int CameraFieldIndex(string key,string name,string fallback,string[] options)
        { if(!fields.ContainsKey(key+name)) fields[key+name]=fallback; return Math.Max(0,Array.IndexOf(options,fields[key+name])); }
        void ConnectionField(string key,string name,string fallback,string tooltip="")
        { string id=key+name; if(!fields.ContainsKey(id)) fields[id]=fallback; GUILayout.Label(new GUIContent(name,tooltip)); GUI.SetNextControlName("UIVA:"+id); fields[id]=GUILayout.TextField(fields[id]); }
        void DrawCameraMarkers(Rect rect)
        {
            if(Event.current.type!=EventType.Repaint || preview==null) return;
            var savedColor=GUI.color; if(cameraNorthStyle==null) { cameraNorthStyle=new GUIStyle(GUI.skin.label){alignment=TextAnchor.MiddleCenter,fontStyle=FontStyle.Bold,fontSize=15}; cameraNorthStyle.normal.textColor=Color.red; }
                                                                                                                                 
            GUI.BeginClip(rect);
            try {
            var localRect=new Rect(0,0,rect.width,rect.height);
            foreach(var item in preview.Items.Where(i=>i.CameraItem)) {
                float size=preview.CameraMarkerSize(item,localRect); if(size<=0) continue;
                var t=item.Transform; var center=t.position; var right=t.right*size; var up=t.up*size; var forward=t.forward*size;
                var corners=new[]{center-right-up,center+right-up,center+right+up,center-right+up};
                GUI.color=Key(item.Node)==selectedKey?new Color(1,0.7f,0.05f):item.Node.GetValue("enabled")=="false"?Color.gray:new Color(0.1f,0.55f,0.8f);
                for(int i=0;i<4;i++) Line(preview.ScreenPoint(corners[i],localRect),preview.ScreenPoint(corners[(i+1)%4],localRect));
                var tip=center+forward*2.6f; var screen=preview.ScreenPoint(center,localRect); var end=preview.ScreenPoint(tip,localRect);
                Line(screen,end); Line(end,preview.ScreenPoint(tip-forward*0.6f+right*0.45f,localRect)); Line(end,preview.ScreenPoint(tip-forward*0.6f-right*0.45f,localRect));
                for(int i=0;i<4;i++) Line(preview.ScreenPoint(center+forward*0.6f,localRect),preview.ScreenPoint(center+forward*2+right*((i&1)==0?-1:1)*1.4f+up*((i&2)==0?-1:1)*0.9f,localRect));
                GUI.color=Color.white; var north=preview.ScreenPoint(center+up*1.7f,localRect); GUI.Label(new Rect(north.x-10,north.y-10,20,20),"N",cameraNorthStyle);
            }
            } finally { GUI.EndClip(); GUI.color=savedColor; }
        }
    }
}







