using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed partial class EditorWindow
    {
        ConfigNode presetInfoData;
        string presetAuthor="",presetDescription="",metadataEditControl;
        void SyncPresetInfo()
        {
            if(ReferenceEquals(presetInfoData,session?.Data)) return;
            presetInfoData=session?.Data; metadataEditControl=null;
            presetAuthor=presetInfoData?.GetValue("author") ?? "";
            presetDescription=PresetInfo.DecodeDescription(presetInfoData?.GetValue("description") ?? "");
        }
        string MetadataField(string key,string value,bool multiline)
        {
            string control="UIVA:preset:"+key; GUI.SetNextControlName(control);
            string next=multiline?GUILayout.TextArea(value,GUILayout.Height(window.height<650?20:42)):GUILayout.TextField(value);
            if(next!=value && session!=null) {
                if(metadataEditControl!=control) { session.Checkpoint(); metadataEditControl=control; }
                session.Data.SetValue(key,key=="description"?PresetInfo.EncodeDescription(next):next,true);
            }
            return next;
        }
        void CommitPresetInfo()
        {
            SyncPresetInfo();
            var data=PresetInfo.WithMetadata(session.Data,filename,presetAuthor,presetDescription);
            data=MASBootstrap.ForSave(data,catalog,module.part);
            if(Configuration.Signature(data)!=Configuration.Signature(session.Data)) session.Replace(data);
            presetInfoData=session.Data;
        }
        void ClearCameraSelection() { selectedKey="whole"; selectedItem=null; selected=FindSelected(); fields.Clear(); }
        void SetEditorMode(bool props)
        {
            EndPointer(true); propsMode=props; connections=false; evaPanel=false; page=0; search="";
            if(selected?.name=="CAMERA" || EVAConfiguration.Point(selected)) ClearCameraSelection();
        }
        void ReloadPresets()
        {
            EndPointer(true); thumbnails.Dispose(); PresetStore.Invalidate(); catalog=Catalog.Read(); RevivaIntegration.Refresh(); dirtyPreview=true;
            message="Presets reloaded.";
        }
        void ImportSelected()
        {
            if(!File.Exists(importPath) || !importPath.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase)) throw new FormatException("Select a preset cfg in User Presets first");
            var root=ConfigNode.Load(importPath) ?? throw new FormatException("Cannot parse selected preset");
            var refreshed=Catalog.Read(); var next=PresetFile.Snapshot(root,refreshed,module.part.partInfo.name,module.part.CrewCapacity);
            LayoutEngine.Preflight(next,refreshed); EndPointer(true); catalog=refreshed;
            if(session==null) { session=new EditSession(next); session.MarkUnsaved(); } else session.Replace(next);
            selectedKey="whole"; selectedItem=null; selected=null; fields.Clear(); connections=false; propsMode=false; page=0; search="";
            presetInfoData=null; SyncPresetInfo(); Changed(false);
            message="Opened "+Path.GetFileName(importPath)+" for "+module.part.partInfo.name+". Apply for Part to activate.";
            try { refreshed.Validate(next,module.part.partInfo.name,module.part.CrewCapacity); }
            catch(FormatException e) { message+=" Needs editing before Apply: "+e.Message; }
        }
        void Export()
        {
            if(session==null) throw new FormatException("Choose an IVA first");
            CommitPresetInfo(); catalog.Validate(session.Data,module.part.partInfo.name,module.part.CrewCapacity,false);
            var exported=new EditSession(session.Data); exported.ForkIds();
            Catalog.Export(exported.Data,filename); catalog=Catalog.Read(); session.MarkSaved(); message="Preset saved: "+Catalog.NormalizeFilename(filename);
        }
        void DrawUserPresets()
        {
            SyncPresetInfo();
            GUILayout.BeginHorizontal(); GUILayout.Label("Name",GUILayout.Width(65)); GUI.SetNextControlName("UIVA:preset:name"); filename=GUILayout.TextField(filename,GUILayout.Width(180));
            GUI.enabled=session!=null; if(GUILayout.Button("Export",GUILayout.Width(75))) Run(Export); GUI.enabled=true;
            if(GUILayout.Button("Import selected",GUILayout.Width(120))) Run(ImportSelected);
            if(GUILayout.Button(new GUIContent("Reload Presets","Reload Presets"),GUILayout.Width(120))) Run(ReloadPresets);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("Author",GUILayout.Width(65)); presetAuthor=MetadataField("author",presetAuthor,false); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUILayout.Label("Description",GUILayout.Width(65)); presetDescription=MetadataField("description",presetDescription,true); GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(); GUI.SetNextControlName("UIVA:preset:folder"); fileFolder=GUILayout.TextField(fileFolder);
            if(GUILayout.Button("Parent",GUILayout.Width(65))) Run(()=>fileFolder=Directory.GetParent(fileFolder)?.FullName ?? fileFolder); GUILayout.EndHorizontal();
            fileScroll=GUILayout.BeginScrollView(fileScroll,GUILayout.Height(CameraPlacement.FileListHeight(window.height,Log.DebugEnabled)));
            if(Directory.Exists(fileFolder)) {
                foreach(var folder in Directory.GetDirectories(fileFolder)) if(GUILayout.Button("[+] "+Path.GetFileName(folder))) fileFolder=folder;
                foreach(var file in Directory.GetFiles(fileFolder,"*.cfg"))
                    if(GUILayout.Button(Path.GetFileName(file))) { importPath=file; filename=Path.GetFileName(file); message="Selected "+file; }
            }
            GUILayout.EndScrollView();
        }
    }
    public static class PresetInfo
    {
        public static string EncodeDescription(string value) => value.Replace("\r\n","\n").Replace("\r","\n").Replace("\n","<br>");
        public static string DecodeDescription(string value) => value.Replace("<br>","\n");
        public static ConfigNode WithMetadata(ConfigNode data,string filename,string author,string description)
        {
            var result=Configuration.Copy(data); result.SetValue("title",Path.GetFileNameWithoutExtension(Catalog.NormalizeFilename(filename)),true);
            result.SetValue("author",author??"",true); result.SetValue("description",EncodeDescription(description??""),true); return result;
        }
    }
}



