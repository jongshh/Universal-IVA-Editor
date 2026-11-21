using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UniversalIVAEditor
{
                                                                                                        
    public sealed class EditSession
    {
        public ConfigNode Data { get; private set; }
        public readonly Stack<ConfigNode> UndoStack=new Stack<ConfigNode>();
        public readonly Stack<ConfigNode> RedoStack=new Stack<ConfigNode>();
        string savedSignature;
        public bool IsDirty => savedSignature!=Configuration.Signature(Data);
        public void MarkSaved() { savedSignature=Configuration.Signature(Data); }
        public void MarkUnsaved() { savedSignature=null; }
        public EditSession(ConfigNode data) { Data=Configuration.Copy(data); MarkSaved(); }
        public void Checkpoint() { UndoStack.Push(Configuration.Copy(Data)); RedoStack.Clear(); }
        public void Replace(ConfigNode data) { Checkpoint(); Data=Configuration.Copy(data); }
        public void Undo() { if(UndoStack.Count==0) return; RedoStack.Push(Configuration.Copy(Data)); Data=UndoStack.Pop(); }
        public void Redo() { if(RedoStack.Count==0) return; UndoStack.Push(Configuration.Copy(Data)); Data=RedoStack.Pop(); }
        public ConfigNode Layout => Data.GetNode("UIVA_LAYOUT");
        public static ConfigNode New(string part,ConfigNode source)
        {
            string id=Guid.NewGuid().ToString("N"); var data=new ConfigNode("UIVA_SNAPSHOT");
            data.AddValue("version",2); data.AddValue("id","user.preset."+id); data.AddValue("part",part); data.AddValue("title","Custom IVA");
            var layout=new ConfigNode("UIVA_LAYOUT"); layout.AddValue("version",2); layout.AddValue("id","user.layout."+id);
            layout.AddValue("internal",source.GetValue("name")); layout.AddValue("sourceSignature",Configuration.Signature(source));
            layout.AddValue("freeIvaBaselineSignature",Configuration.Signature(FreeIvaCompatibility.Baseline(source)));
            FreeIvaCompatibility.Capture(layout,source);
            data.AddNode(layout); data.AddValue("layout",layout.GetValue("id")); data.AddNode("TRANSFORM"); return data;
        }
        public ConfigNode ExistingProp(int index,ConfigNode source)
        {
            var found=Layout.GetNodes("PROP_EDIT").FirstOrDefault(n=>!FreeIvaCompatibility.Missing(n) && n.GetValue("index")==index.ToString());
            if(found!=null) return found;
            var prop=source.GetNodes("PROP")[index]; var edit=new ConfigNode("PROP_EDIT"); edit.AddValue("name",prop.GetValue("name")); edit.AddValue("index",index); FreeIvaCompatibility.StampEdit(edit,source,index);
            return edit;
        }
        public ConfigNode AddProp(string name)
        {
            var n=Layout.AddNode("PROP_ADD"); n.AddValue("id",Guid.NewGuid().ToString("N")); n.AddValue("name",name);
            n.AddValue("position","0,0,0"); n.AddValue("rotation","0,0,0,1"); n.AddValue("scale","1,1,1"); return n;
        }
        public void ForkIds()
        {
            string id=Guid.NewGuid().ToString("N"); Data.SetValue("id","user.preset."+id,true);
            Layout.SetValue("id","user.layout."+id,true); Data.SetValue("layout",Layout.GetValue("id"),true);
        }
        public static void SetVector(ConfigNode n,string key,Vector3 v) => n.SetValue(key,string.Join(",",new[]{v.x,v.y,v.z}.Select(x=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture))),true);
        public static void SetRotation(ConfigNode n,Quaternion q) => n.SetValue("rotation",string.Join(",",new[]{q.x,q.y,q.z,q.w}.Select(x=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture))),true);
    }
}



