using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using KSP.UI.Screens;

namespace UniversalIVAEditor
{
    [KSPAddon(KSPAddon.Startup.EditorAny,false)]
    public sealed partial class EditorWindow : MonoBehaviour
    {
        static EditorWindow instance;
        PartTarget module; Catalog catalog; EditSession session; Preview preview;
        ApplicationLauncherButton launcher; Texture2D icon,background; GUISkin skin;
        bool choosingTarget,filesPanel,localAxes=true,resizing,thumbnailGrid=true;
        float sidebarWidth=240,inspectorWidth=280;
        Rect pointerStartWindow,screenView; Vector2 pointerStart; float pointerStartPanel;
        int pointerControl,hoverAxis=-1,dragMode; bool dragChanged; ConfigNode[] dragRedoBefore;
        readonly GizmoController gizmos=new GizmoController(); readonly PropThumbnailCache thumbnails=new PropThumbnailCache();
        Vector3 dragOrigin,dragNormal,dragPoint,dragAxisWorld; Matrix4x4 dragParentInverse; Quaternion dragParentRotation;
        Vector3[] dragAxes; float dragSize,dragAngle,dragPreviousAngle;
        string fileFolder=Catalog.UserDirectory;
        Rect window=new Rect(30,75,1100,740);
        Vector2 listScroll,detailScroll; string search="",message="",filename="my-iva.cfg",importPath="";
        string previewError; bool displayDirty; float nextDirtyCheck; Catalog propListCatalog; string propListSearch; string[] propListNames;
        bool open,propsMode,showExterior=true,dirtyPreview; int page,mode;
        string selectedKey=""; ConfigNode selected; Preview.Item selectedItem;
        float exteriorTransparency=82;
        float moveSnap=0.01f,rotateSnap=5,scaleSnap=0.05f; bool snap=true;
        int dragAxis=-1; Vector2 dragStart,dragScreenDirection; Vector3 dragValue; Quaternion dragRotation;
        readonly Dictionary<string,string> fields=new Dictionary<string,string>();
        public void Awake()
        {
            instance=this; LoadUI(); GameEvents.onGUIApplicationLauncherReady.Add(AddLauncher);
            if(ApplicationLauncher.Ready) AddLauncher(); Log.Trace("SPH/VAB editor ready");
        }
        void AddLauncher()
        {
            if(launcher!=null || ApplicationLauncher.Instance==null) return;
                                                                                                    
            icon=GameDatabase.Instance.GetTexture("UniversalIVAEditor/Textures/Icon",false);
            if(icon==null) { Log.Error("Editor icon texture is missing: UniversalIVAEditor/Textures/Icon"); icon=Texture2D.whiteTexture; }
            launcher=ApplicationLauncher.Instance.AddModApplication(()=>{open=true;choosingTarget=true;catalog=Catalog.Read();},()=>Close(),null,null,null,null,ApplicationLauncher.AppScenes.SPH|ApplicationLauncher.AppScenes.VAB,icon);
        }
        public static void Open(PartTarget target,bool props)
        {
            if(instance==null) return;
            instance.Begin(target,props);
        }
        void Begin(PartTarget target,bool props)
        {
            EndPointer(true); gizmos.Dispose();
            if(module?.part!=null) module.part.SetHighlight(false,false);
            preview?.Dispose(); preview=null; session=null; module=target; propsMode=props; open=true; catalog=Catalog.Read();
            target.part.SetHighlight(true,false); choosingTarget=false; fields.Clear(); cameraRenames.Clear();
            showExterior=true;
            ConfigNode active=null; string activeError=null;
            try { active=target.Snapshot; }
            catch(Exception e) { activeError="Active preset rejected: "+e.Message; Log.Error(activeError,e); }
            if(active!=null) session=new EditSession(active);
            else
            {
                var name=target.part.partInfo.internalConfig?.GetValue("name");
                if(name!=null && catalog.Internals.TryGetValue(name,out var source)) session=new EditSession(EditSession.New(target.part.partInfo.name,source));
            }
            presetInfoData=null; presetAuthor=""; presetDescription=""; metadataEditControl=null; SyncPresetInfo();
            filename=active==null?"my-iva.cfg":Catalog.NormalizeFilename(active.GetValue("title")??"my-iva");
            page=0; selected=null; selectedKey=""; selectedItem=null; cameraSlotChoice=null; previewError=null; dirtyPreview=true;
            message=activeError ?? (session==null ? "Choose an Internal Space to start." : "Save with Export or Apply for Part.");
            Log.Trace("Opened editor part="+target.part.partInfo.name+" propsMode="+props);
        }
        void Close()
        {
            EndPointer(true); gizmos.Dispose(); thumbnails.Dispose();
            if(module?.part!=null) module.part.SetHighlight(false,false);
            SaveUI();
            preview?.Dispose(); preview=null; session=null; presetInfoData=null; presetAuthor=""; presetDescription=""; metadataEditControl=null; open=false; selected=null; selectedItem=null;
            dragAxis=-1; propListNames=null; propListCatalog=null; propListSearch=null; displayDirty=false; fields.Clear(); InputLockManager.RemoveControlLock("UniversalIVAEditor");
        }
        void OnDestroy()
        {
            Close(); GameEvents.onGUIApplicationLauncherReady.Remove(AddLauncher);
            if(launcher!=null && ApplicationLauncher.Instance!=null) ApplicationLauncher.Instance.RemoveModApplication(launcher);
            if(background) Destroy(background); if(skin) Destroy(skin);
            if(instance==this) instance=null;
        }
        void OnApplicationFocus(bool focus) { if(!focus) { EndPointer(true); InputLockManager.RemoveControlLock("UniversalIVAEditor"); } }
        void Update()
        {
            if(!open) return;
            SyncPresetInfo();
            UpdatePointer(); if(!PointerActive) thumbnails.RenderNext();
            if(EditorLogic.fetch?.ship==null) { Close(); return; }
            if(module!=null && (module.part==null || !EditorLogic.fetch.ship.parts.Contains(module.part))) { EndPointer(true); gizmos.Dispose(); preview?.Dispose(); preview=null; session=null; module=null; choosingTarget=true; }
            if(dirtyPreview && session!=null)
            {
                dirtyPreview=false; previewError=null;
                try
                {
                    Configuration.Versioned(session.Data);
                    preview=preview ?? new Preview(); preview.Build(session,catalog,module.part); preview.Exterior(showExterior); preview.ExteriorTransparency(exteriorTransparency);
                    selectedItem=preview.Items.FirstOrDefault(i=>Key(i.Node)==selectedKey); selected=FindSelected();
                    RestoreCameraSelection();
                    if(selectedKey.StartsWith("eva:")) {
                        if(selected==null) { selectedKey=""; selectedItem=null; fields.Clear(); }
                        else if(mode==2 && selected.name!="LADDER") mode=0;
                    }
                }
                catch(Exception e) { previewError="IVA preview failed: "+e.Message; message=previewError; Log.Error(message,e); preview?.Dispose(); preview=null; }
            }
        }
        void OnGUI()
        {
            if(!HighLogic.LoadedSceneIsEditor) return;
            if(!open) return;
            pointerControl=GUIUtility.GetControlID(731963,FocusType.Passive);
            if(PointerActive)
            {
                GUIUtility.hotControl=pointerControl;
                if(Event.current.type==EventType.KeyDown && Event.current.keyCode==KeyCode.Escape) { EndPointer(true); Event.current.Use(); }
                else if(Event.current.type==EventType.MouseUp && Event.current.button==0) { EndPointer(false); Event.current.Use(); }
                else if(Event.current.type==EventType.MouseDrag) Event.current.Use();
            }
            EnsureSkin(); var oldSkin=GUI.skin; GUI.skin=skin;
            window.width=Mathf.Min(window.width,Screen.width-20); window.height=Mathf.Min(window.height,Screen.height-20);
            window.x=Mathf.Clamp(window.x,0,Screen.width-window.width); window.y=Mathf.Clamp(window.y,0,Screen.height-window.height);
            sidebarWidth=Mathf.Clamp(sidebarWidth,170,window.width*0.32f); inspectorWidth=Mathf.Clamp(inspectorWidth,210,window.width*0.35f);
            bool capture=window.Contains(Event.current.mousePosition) || PointerActive;
            if(capture) InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK,"UniversalIVAEditor"); else InputLockManager.RemoveControlLock("UniversalIVAEditor");
            if(Event.current.type==EventType.Repaint && Time.unscaledTime>=nextDirtyCheck) { displayDirty=session?.IsDirty==true; nextDirtyCheck=Time.unscaledTime+0.25f; }
            window=GUI.Window(731962,window,DrawWindow,"Universal IVA Editor — "+(module?.part?.partInfo.title ?? "Choose IVA target")+(displayDirty?" *":""));
            if(!string.IsNullOrEmpty(GUI.tooltip)) GUI.Box(new Rect(Mathf.Max(0,Mathf.Min(Screen.width-310,ScreenMouse.x+14)),Mathf.Min(Screen.height-90,ScreenMouse.y+16),300,72),GUI.tooltip);
            GUI.skin=oldSkin;
        }
        void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("IVA : "+(module?.part.partInfo.title ?? "select target"))) choosingTarget=!choosingTarget;
            if(GUILayout.Button("Close",GUILayout.Width(55))) { Close(); GUILayout.EndHorizontal(); return; }
            GUILayout.EndHorizontal();
            if(choosingTarget || module==null)
            {
                GUILayout.Label("Choose a crewed part. Apply affects its part type.");
                listScroll=GUILayout.BeginScrollView(listScroll);
                var parts=EditorLogic.fetch.ship.parts.Where(p=>p.CrewCapacity>0).ToArray();
                for(int i=0;i<parts.Length;i++)
                {
                    var p=parts[i];
                    if(GUILayout.Button("#"+(i+1)+"  "+p.partInfo.title+"  ("+p.partInfo.name+")")) Run(()=>Begin(new PartTarget(p),propsMode));
                }
                if(parts.Length==0) GUILayout.Label("Place a crewed part first.");
                GUILayout.EndScrollView(); DrawResize(); GUI.DragWindow(new Rect(0,0,window.width,20)); return;
            }
            GUILayout.BeginHorizontal();
            if(GUILayout.Toggle(!propsMode && !connections && !evaPanel,"IVA placement","Button",GUILayout.Width(120)) && (propsMode || connections || evaPanel)) SetEditorMode(false);
            if(GUILayout.Toggle(propsMode && !connections && !evaPanel,"Props","Button",GUILayout.Width(75)) && (!propsMode || connections || evaPanel)) SetEditorMode(true);
            var cameraPanel=GUILayout.Toggle(connections,"Camera","Button",GUILayout.Width(75));
            if(cameraPanel!=connections) { EndPointer(true); connections=cameraPanel; evaPanel=false; if(EVAConfiguration.Point(selected)) ClearCameraSelection(); if(!connections && selected?.name=="CAMERA") ClearCameraSelection(); }
            var eva=GUILayout.Toggle(evaPanel,"EVA","Button",GUILayout.Width(55));
            if(eva!=evaPanel) { EndPointer(true); evaPanel=eva; connections=false; ClearCameraSelection(); }
            GUI.enabled=session!=null && session.UndoStack.Count>0;
            if(GUILayout.Button("Undo",GUILayout.Width(55))) { session.Undo(); Changed(false); }
            GUI.enabled=session!=null && session.RedoStack.Count>0;
            if(GUILayout.Button("Redo",GUILayout.Width(55))) { session.Redo(); Changed(false); } GUI.enabled=true;
            filesPanel=GUILayout.Toggle(filesPanel,"User Presets","Button",GUILayout.Width(110));
            GUILayout.FlexibleSpace(); GUILayout.BeginVertical(GUILayout.Width(190));
            var debug=GUILayout.Toggle(Log.DebugEnabled,"Debug");
            if(debug!=Log.DebugEnabled) { EndPointer(true); Log.DebugEnabled=debug; if(!debug) cameraAdvanced=false; fields.Clear(); }
            GUILayout.Label("Including Experimental Functions"); GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            bool compact=window.width<1250;
            int nextMode=mode; foreach(var choice in new[]{0,1,2}) { GUI.enabled=choice!=2 || (selected?.name!="CAMERA" && selected?.name!="HATCH" && selected?.name!="EXIT"); if(GUILayout.Toggle(mode==choice,new[]{"Move","Rotate","Scale"}[choice],"Button",GUILayout.Width(compact?65:75)) && mode!=choice) nextMode=choice; } GUI.enabled=true; if(nextMode!=mode) { EndPointer(true); hoverAxis=-1; mode=nextMode; }
            GUI.enabled=mode!=2; if(mode==2) localAxes=true; localAxes=GUILayout.Toggle(localAxes,"Local axes",GUILayout.Width(compact?80:85)); GUI.enabled=true; snap=GUILayout.Toggle(snap,"Snap",GUILayout.Width(compact?65:72)); GUILayout.Space(10);
            GUILayout.Label("m / ° / scale",GUILayout.Width(compact?75:85));
            moveSnap=SnapField("moveSnap",moveSnap); rotateSnap=SnapField("rotateSnap",rotateSnap); scaleSnap=SnapField("scaleSnap",scaleSnap);
            var exterior=GUILayout.Toggle(showExterior,"Exterior",GUILayout.Width(compact?75:85)); if(exterior!=showExterior) { showExterior=exterior; preview?.Exterior(exterior); }
            if(!compact) DrawTransparency();
            if(GUILayout.Button("Move Camera to Center",GUILayout.Width(compact?145:160))) preview?.Focus(selectedItem);
            GUILayout.EndHorizontal();
            if(compact && showExterior) { GUILayout.BeginHorizontal(); DrawTransparency(); GUILayout.EndHorizontal(); }
            float fileListHeight=CameraPlacement.FileListHeight(window.height,Log.DebugEnabled);
            float height=Mathf.Max(100,window.height-225-(compact && showExterior?25:0)-(filesPanel?fileListHeight+CameraPlacement.PresetPanelOverhead(window.height):0)-(Log.DebugEnabled?95:0));
            float centerWidth=Mathf.Max(180,window.width-sidebarWidth-inspectorWidth-50);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(sidebarWidth));
            search=GUILayout.TextField(search);
            if(!evaPanel) page=GUILayout.Toolbar(page,propsMode?new[]{"Library","Placed","Seats"}:new[]{"Internals","Seats"});
            listScroll=GUILayout.BeginScrollView(listScroll,GUILayout.Height(height-60));
            if(evaPanel) DrawEVAList(); else if(page==(propsMode?2:1)) DrawSeats(); else if(propsMode && page==1) DrawPlaced(); else if(propsMode) DrawPropLibrary(); else DrawInternals();
            GUILayout.EndScrollView(); GUILayout.EndVertical();
            Splitter(true,height);
            GUILayout.BeginVertical(GUILayout.Width(centerWidth));
            var rect=GUILayoutUtility.GetRect(centerWidth,height-28);
            if(Event.current.type==EventType.Repaint) screenView=new Rect(window.x+rect.x,window.y+rect.y,rect.width,rect.height);
            if(preview?.Image!=null)
            {
                if(Event.current.type==EventType.Repaint) { preview.Resize((int)(rect.width),(int)(rect.height)); preview.Render(); }
                DrawHandles(rect); GUI.DrawTexture(rect,preview.Image,ScaleMode.StretchToFill,false); DrawSelection(rect); if(connections) DrawCameraMarkers(rect); if(evaPanel) DrawEVAMarkers(rect); HandlePreviewInput(rect);
            }
            else
            {
                GUI.Box(rect,previewError ?? ("Choose an Internal Space"));
                if(previewError!=null && GUI.Button(new Rect(rect.center.x-60,rect.center.y+30,120,28),"Retry preview")) dirtyPreview=true;
            }
            GUILayout.Label("RMB orbit · MMB pan · wheel zoom · click select"); GUILayout.EndVertical();
            Splitter(false,height);
            detailScroll=GUILayout.BeginScrollView(detailScroll,GUILayout.Width(inspectorWidth),GUILayout.Height(height)); DrawInspector(); GUILayout.EndScrollView();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Apply for Part")) Run(()=> {
                if(session==null) throw new Exception("Choose an IVA first"); CommitPresetInfo(); module.Commit(session.Data); catalog=PresetStore.Library; session.MarkSaved(); message=RevivaIntegration.Installed && !RevivaIntegration.Available ? "Preset saved. "+RevivaIntegration.Status : RevivaIntegration.Installed ? "Preset registered in IVA Switch. Select it there to activate." : "Part preset saved externally.";
            });
            if(GUILayout.Button("Reset for Part")) Run(()=> { module.RestoreOriginal(); session=null; Begin(module,propsMode); });


            GUILayout.EndHorizontal();

            if(filesPanel) DrawUserPresets();
            if(Log.DebugEnabled)
            {
                GUILayout.TextArea(DiagnosticText(),GUILayout.Height(65));
                if(GUILayout.Button("Copy diagnostics")) GUIUtility.systemCopyBuffer=DiagnosticText();
            }
            else GUILayout.Label((preview?.Warnings.Count>0?"IVA remains editable — "+preview.Warnings.Count+" notice(s). Enable Debug for details. ":"")+message.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "");
            DrawResize(); GUI.DragWindow(new Rect(0,0,window.width,20));
        }
        void DrawTransparency()
        {
            if(!showExterior) return;
            GUILayout.Label("Transparency",GUILayout.Width(90));
            var value=GUILayout.HorizontalSlider(exteriorTransparency,0,100,GUILayout.Width(90));
            GUILayout.Label(Mathf.RoundToInt(value)+"%",GUILayout.Width(35));
            if(value!=exteriorTransparency) { exteriorTransparency=value; preview?.ExteriorTransparency(value); }
        }
        float SnapField(string key,float value)
        {
            if(!fields.ContainsKey(key)) fields[key]=value.ToString(CultureInfo.InvariantCulture);
            fields[key]=GUILayout.TextField(fields[key],GUILayout.Width(window.width<980?40:45));
            return float.TryParse(fields[key],NumberStyles.Float,CultureInfo.InvariantCulture,out var parsed) && parsed>0 && !float.IsInfinity(parsed) ? parsed : value;
        }
        Vector2 fileScroll;
        int splitDrag;
        bool PointerActive => resizing || splitDrag!=0 || dragAxis>=0;
        static Vector2 ScreenMouse => new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);
        void CapturePointer() { GUIUtility.hotControl=pointerControl; pointerStart=ScreenMouse; InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK,"UniversalIVAEditor"); }
        void Splitter(bool left,float height)
        {
            var rect=GUILayoutUtility.GetRect(5,height,GUILayout.Width(5)); GUI.Box(rect,""); var e=Event.current;
            if(!PointerActive && e.type==EventType.MouseDown && e.button==0 && rect.Contains(e.mousePosition))
            { splitDrag=left?1:2; pointerStartPanel=left?sidebarWidth:inspectorWidth; CapturePointer(); e.Use(); }
        }
        void DrawResize()
        {
            var handle=new Rect(window.width-22,window.height-22,20,20); GUI.Box(handle,"◢"); var e=Event.current;
            if(!PointerActive && e.type==EventType.MouseDown && e.button==0 && handle.Contains(e.mousePosition))
            { resizing=true; pointerStartWindow=window; CapturePointer(); e.Use(); }
        }
        void UpdatePointer()
        {
            if(!PointerActive) return;
            if(!Input.GetMouseButton(0)) { EndPointer(false); return; }
            var delta=ScreenMouse-pointerStart;
            if(resizing)
            {
                float maxWidth=Screen.width-pointerStartWindow.x-10,maxHeight=Screen.height-pointerStartWindow.y-10;
                window.width=Mathf.Clamp(pointerStartWindow.width+delta.x,Mathf.Min(850,maxWidth),maxWidth);
                window.height=Mathf.Clamp(pointerStartWindow.height+delta.y,Mathf.Min(560,maxHeight),maxHeight); return;
            }
            if(splitDrag!=0)
            {
                if(splitDrag==1) sidebarWidth=Mathf.Clamp(pointerStartPanel+delta.x,170,window.width*0.32f);
                else inspectorWidth=Mathf.Clamp(pointerStartPanel-delta.x,210,window.width*0.35f); return;
            }
            try { UpdateGizmoDrag(); } catch(Exception e) { EndPointer(true); message="Gizmo edit cancelled: "+e.Message; Log.Error(message,e); }
        }
        void EndPointer(bool cancel)
        {
            if(cancel)
            {
                if(resizing) window=pointerStartWindow;
                if(splitDrag==1) sidebarWidth=pointerStartPanel; else if(splitDrag==2) inspectorWidth=pointerStartPanel;
            }
            if(dragAxis>=0 && session!=null)
            {
                if(cancel || !dragChanged)
                {
                    session.Undo(); session.RedoStack.Clear();
                    if(dragRedoBefore!=null) for(int i=dragRedoBefore.Length-1;i>=0;i--) session.RedoStack.Push(dragRedoBefore[i]);
                }
                dirtyPreview=true; metadataEditControl=null; fields.Clear();
            }
            dragAxis=-1; dragRedoBefore=null; resizing=false; splitDrag=0;
            if(GUIUtility.hotControl==pointerControl) GUIUtility.hotControl=0;
        }
        void EnsureSkin()
        {
            if(skin) return; skin=Instantiate(HighLogic.Skin);
            background=new Texture2D(1,1); background.SetPixel(0,0,new Color(0.17f,0.19f,0.22f,1)); background.Apply();
            skin.window.normal.background=background; skin.window.onNormal.background=background;
            foreach(var style in new[]{skin.label,skin.button,skin.toggle,skin.textField,skin.textArea,skin.window}) { style.normal.textColor=new Color(0.93f,0.94f,0.96f); style.fontSize=12; }
            skin.label.wordWrap=true;
        }
        string UIPath=>Path.Combine(KSPUtil.ApplicationRootPath,"GameData/UniversalIVAEditor/PluginData/UI.cfg");
        void LoadUI()
        {
            try
            {
                if(!File.Exists(UIPath)) return; var n=ConfigNode.Load(UIPath).GetNode("UIVA_UI"); if(n==null) return;
                var v=Configuration.Numbers(n.GetValue("window"),4); window=new Rect(v[0],v[1],v[2],v[3]);
                cameraAdvanced=n.GetValue("cameraAdvanced")=="true";
                if(float.TryParse(n.GetValue("exteriorTransparency"),NumberStyles.Float,CultureInfo.InvariantCulture,out var transparency) && !float.IsNaN(transparency) && !float.IsInfinity(transparency)) exteriorTransparency=Mathf.Clamp(transparency,0,100);
                sidebarWidth=float.Parse(n.GetValue("sidebar"),CultureInfo.InvariantCulture); inspectorWidth=float.Parse(n.GetValue("inspector"),CultureInfo.InvariantCulture);
            }
            catch(Exception e) { Log.Error("UI preferences",e); }
        }
        void SaveUI()
        {
            try
            {
                var root=new ConfigNode(); var n=root.AddNode("UIVA_UI"); var r=window;
                n.AddValue("window",string.Join(",",new[]{r.x,r.y,r.width,r.height}.Select(v=>v.ToString("R",CultureInfo.InvariantCulture))));
                n.AddValue("cameraAdvanced",cameraAdvanced?"true":"false");
                n.AddValue("exteriorTransparency",exteriorTransparency.ToString("R",CultureInfo.InvariantCulture));
                n.AddValue("sidebar",sidebarWidth.ToString(CultureInfo.InvariantCulture)); n.AddValue("inspector",inspectorWidth.ToString(CultureInfo.InvariantCulture));
                PresetStore.AtomicWrite(UIPath,Configuration.FileText(root));
            }
            catch(Exception e) { Log.Error("Saving UI preferences",e); }
        }
        string DiagnosticText() => MASBootstrap.Status+"\n"+RevivaIntegration.Diagnostics+"\n"+message+"\n"+string.Join("\n",preview?.Warnings ?? Enumerable.Empty<string>())+"\n"+string.Join("\n",catalog.Errors)+"\nLast flight (unverified):\n"+string.Join("\n",module==null?Enumerable.Empty<string>():IntegrationState.Last(module.part.partInfo.name));
        bool Match(string name) => name.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0;
        void DrawInternals()
        {
            foreach(var entry in catalog.Internals.OrderBy(p=>p.Key).Where(p=>Match(p.Key)))
            {
                int seats=entry.Value.GetNodes("MODULE").Count(n=>n.GetValue("name")=="InternalSeat");
                if(GUILayout.Button(entry.Key+"  ["+seats+" seats]"))
                {
                    if(session==null) session=new EditSession(EditSession.New(module.part.partInfo.name,entry.Value));
                    else session.Replace(EditSession.New(module.part.partInfo.name,entry.Value));
                    selectedKey="whole"; Changed(false); message=catalog.Sources.TryGetValue("INTERNAL:"+entry.Key,out var source) ? source : entry.Key;
                }
            }
        }
        void AddProp(string name)
        {
            session.Checkpoint(); var n=session.AddProp(name); if(catalog.Sources.TryGetValue("PROP:"+name,out var propSource) && propSource.StartsWith("FreeIva/",StringComparison.OrdinalIgnoreCase)) n.SetValue("freeIvaOptional","true",true); if(preview!=null) EditSession.SetVector(n,"position",preview.SpawnPosition()); selectedKey=Key(n); Changed(false);
            message="Added "+name+" at view center; default rotation and scale retained.";
        }
        void DrawPropLibrary()
        {
            if(session==null) { GUILayout.Label("Choose an IVA first."); return; }
            thumbnailGrid=GUILayout.Toggle(thumbnailGrid,"Thumbnails");
            if(propListCatalog!=catalog || propListSearch!=search) { propListCatalog=catalog; propListSearch=search; propListNames=catalog.Props.Keys.Where(Match).OrderBy(n=>n).ToArray(); }
            var names=propListNames;
            if(Event.current.type==EventType.Repaint) thumbnails.BeginVisibleFrame();
            if(!thumbnailGrid) { foreach(var name in names) if(GUILayout.Button("+ "+name)) AddProp(name); return; }
            int columns=Mathf.Max(1,Mathf.FloorToInt((sidebarWidth-24)/88)); float width=(sidebarWidth-24)/columns;
            var area=GUILayoutUtility.GetRect(sidebarWidth-24,Mathf.CeilToInt((float)names.Length/columns)*110);
            float top=listScroll.y,bottom=top+Mathf.Max(100,window.height-260);
            for(int i=0;i<names.Length;i++)
            {
                var cell=new Rect(area.x+(i%columns)*width,area.y+(i/columns)*110,width-3,106);
                if(cell.yMax<top || cell.y>bottom) continue;
                string name=names[i],source=catalog.Sources.TryGetValue("PROP:"+name,out var path)?path:"";
                var image=Event.current.type==EventType.Repaint?thumbnails.Get(name):null;
                string error=thumbnails.Error(name); string tooltip=name+"\n"+source+(error==null?"":"\n"+error);
                if(GUI.Button(cell,new GUIContent("",tooltip))) AddProp(name);
                var picture=new Rect(cell.x+5,cell.y+4,cell.width-10,73);
                if(image) GUI.DrawTexture(picture,image,ScaleMode.ScaleToFit,false);
                else GUI.Label(picture,error==null?"…":"◇");
                GUI.Label(new Rect(cell.x+3,cell.y+78,cell.width-6,25),new GUIContent(name,tooltip));
            }
        }
        void DrawPlaced()
        {
            if(preview==null) return;
            GUILayout.Label("Built-in model geometry is not a separate editable Prop.");
            if(preview.Warnings.Count>0) GUILayout.Label("Read-only");
            if(preview.UnavailableEdits.Count>0 && GUILayout.Button("Discard unavailable Prop edits"))
            {
                session.Checkpoint(); foreach(var edit in preview.UnavailableEdits) session.Layout.RemoveNode(edit);
                selectedKey="whole"; selectedItem=null; Changed(false); message="Unavailable edits removed. Original geometry retained.";
            }
            foreach(var item in preview.Items.Where(i=>i.Node.name!="SEAT" && !i.CameraItem && !i.EVAItem && Match(i.Label)))
                if(GUILayout.Button(item.Label)) Select(item);
        }
        void DrawSeats()
        {
            if(session==null) return;
            if(GUILayout.Button("Create / fill seat map")) Run(()=>
            {
                var prefab=PartLoader.GetInternalPart(session.Layout.GetValue("internal"));
                var pending=new List<ConfigNode>();
                for(int i=0;i<module.part.CrewCapacity;i++)
                {
                    if(session.Layout.GetNodes("SEAT").Any(n=>n.GetValue("slot")==i.ToString())) continue;
                    var n=new ConfigNode("SEAT"); n.AddValue("slot",i); n.AddValue("transform","seat-"+i);
                    var original=i<prefab.seats.Count ? prefab.seats[i] : null;
                    var seatModule=catalog.Internals[session.Layout.GetValue("internal")].GetNodes("MODULE").Where(m=>m.GetValue("name")=="InternalSeat").ElementAtOrDefault(i);
                    if(seatModule!=null) n.AddNode(Configuration.Copy(seatModule));
                    var t=original?.seatTransform;
                    if(t==null) t=prefab.FindModelTransform(original?.seatTransformName ?? seatModule?.GetValue("seatTransformName") ?? "seat-"+i);
                    if(t!=null && t!=prefab.transform && !t.IsChildOf(prefab.transform)) throw new InvalidOperationException("Original seat transform is outside this IVA instance");
                    if(t!=null) LayoutCoordinates.CaptureSeat(n,prefab.transform.worldToLocalMatrix*t.localToWorldMatrix);
                    else { EditSession.SetVector(n,"position",new Vector3(i*0.5f,0,0)); EditSession.SetRotation(n,Quaternion.identity); EditSession.SetVector(n,"scale",Vector3.one); }
                    EditSession.SetVector(n,"eyeOffset",original?.kerbalEyeOffset ?? Configuration.Vector(seatModule ?? new ConfigNode(),"kerbalEyeOffset",Vector3.zero));
                    pending.Add(n);
                }
                if(pending.Count==0) return;
                session.Checkpoint(); foreach(var n in pending) session.Layout.AddNode(n);
                Changed(false);
            });
            foreach(var item in preview?.Items.Where(i=>i.Node.name=="SEAT") ?? Enumerable.Empty<Preview.Item>())
                if(GUILayout.Button(item.Label)) Select(item);
            if(session.Layout.GetNodes("SEAT").Length==0) GUILayout.Label("Using original seats. Create a map to edit positions and views.");
            if(selectedItem?.Node.name=="SEAT" && GUILayout.Button("Preview seat view")) preview.SeatView(selectedItem);
        }
        void Select(Preview.Item item) { if(item.EVAItem) { SelectEVA(item.Node); return; } if(item.CameraItem) { SelectCamera(item.Node); return; } EndPointer(true); selectedItem=item; selected=item.Node; selectedKey=Key(selected); fields.Clear(); }
        static string Key(ConfigNode n) => EVAConfiguration.Point(n)?"eva:"+n.GetValue("id"):n.name=="CAMERA" ? "camera:"+n.GetValue("slot") : n.name=="PROP_ADD" ? "add:"+n.GetValue("id") : n.name=="PROP_EDIT" ? "edit:"+(n.HasValue("sourceIdentity")?n.GetValue("sourceIdentity")+":"+n.GetValue("sourceOccurrence"):n.GetValue("index")) : n.name=="SEAT" ? "seat:"+n.GetValue("slot") : "whole";
        ConfigNode FindSelected()
        {
            if(session==null) return null;
            if(selectedKey=="whole" || string.IsNullOrEmpty(selectedKey)) return session.Data.GetNode("TRANSFORM");
            if(selectedKey.StartsWith("eva:")) return EVAConfiguration.Nodes(session.Data).FirstOrDefault(n=>Key(n)==selectedKey);
            if(selectedKey.StartsWith("camera:")) return session.Data.GetNode("INTEGRATION")?.GetNodes("CAMERA").FirstOrDefault(n=>Key(n)==selectedKey);
            return session.Layout.nodes.Cast<ConfigNode>().FirstOrDefault(n=>Key(n)==selectedKey) ?? preview?.Items.FirstOrDefault(i=>Key(i.Node)==selectedKey)?.Node;
        }
        void DrawInspector()
        {
            if(session==null) return;
            if(evaPanel) { DrawEVAInspector(); return; }
            if(connections) { DrawConnections(); return; }
            if(!propsMode && page!=1)
            {
                if(GUILayout.Button("Select whole IVA")) { EndPointer(true); selectedKey="whole"; selected=FindSelected(); selectedItem=null; fields.Clear(); }
            }
            selected=FindSelected(); if(selected==null) { GUILayout.Label("Select an item to edit."); return; }
            GUILayout.Label(selectedKey=="whole" ? "Whole IVA" : selectedItem?.Label ?? selectedKey);
            VectorFields("Position", "position",Configuration.Vector(selected,"position",Vector3.zero));
            VectorFields("Rotation °", "euler",Configuration.Rotation(selected).eulerAngles);
            if(selected.name!="SEAT" && selected.name!="CAMERA") VectorFields("Scale", "scale",Configuration.Vector(selected,"scale",Vector3.one));
            else if(selected.name=="SEAT")
            {
                VectorFields("Eye offset", "eyeOffset",Configuration.Vector(selected,"eyeOffset",Vector3.zero));
                VectorFields("View °","viewEuler",Configuration.Rotation(selected,"viewRotation").eulerAngles);
                if(!fields.ContainsKey("slot")) fields["slot"]=selected.GetValue("slot");
                GUILayout.BeginHorizontal(); GUILayout.Label("Crew slot",GUILayout.Width(85)); fields["slot"]=GUILayout.TextField(fields["slot"],GUILayout.Width(75));
                if(GUILayout.Button("Assign slot")) Run(()=>
                {
                    int slot=int.Parse(fields["slot"],CultureInfo.InvariantCulture);
                    if(slot<0 || slot>=module.part.CrewCapacity) throw new Exception("Slot must be between 0 and "+(module.part.CrewCapacity-1));
                    session.Checkpoint(); var other=session.Layout.GetNodes("SEAT").FirstOrDefault(n=>n!=selected && n.GetValue("slot")==slot.ToString());
                    if(other!=null) other.SetValue("slot",selected.GetValue("slot"),true);
                    selected.SetValue("slot",slot,true); selectedKey=Key(selected); Changed(false);
                });
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if(selected.name=="PROP_EDIT" || selected.name=="PROP_ADD")
            {
                if(GUILayout.Button("Duplicate")) Run(()=>
                {
                    session.Checkpoint(); var n=Configuration.Copy(selected); if(selected.name=="PROP_EDIT") {
                        n.SetValue("cloneIndex",selected.GetValue("index"),true);
                        foreach(var suffix in new[]{"Identity","Occurrence","Count"}) if(selected.HasValue("source"+suffix)) { n.SetValue("clone"+suffix,selected.GetValue("source"+suffix),true); n.RemoveValues("source"+suffix); }
                    } n.name="PROP_ADD"; n.RemoveValues("index"); n.RemoveValues("delete");
                    n.SetValue("id",Guid.NewGuid().ToString("N"),true); session.Layout.AddNode(n); selectedKey=Key(n); Changed(false);
                });
                if(GUILayout.Button("Delete")) { CheckpointSelection(); if(selected.name=="PROP_ADD") session.Layout.RemoveNode(selected); else selected.SetValue("delete","true",true); selectedKey="whole"; Changed(false); }
            }
            if(GUILayout.Button("Reset selection"))
            {
                session.Checkpoint();
                if(selected.name=="PROP_EDIT") session.Layout.RemoveNode(selected);
                else { EditSession.SetVector(selected,"position",Vector3.zero); EditSession.SetRotation(selected,Quaternion.identity); EditSession.SetVector(selected,"scale",Vector3.one); }
                Changed(false);
            }
            GUILayout.EndHorizontal();
        }
        void VectorFields(string label,string key,Vector3 current)
        {
            GUILayout.Label(label); GUILayout.BeginHorizontal(); var values=new[]{current.x,current.y,current.z};
            for(int i=0;i<3;i++)
            {
                string fieldKey=selectedKey+":"+key+":"+i;
                if(!fields.TryGetValue(fieldKey,out var text)) text=values[i].ToString("0.####",CultureInfo.InvariantCulture);
                GUI.SetNextControlName("UIVA:"+fieldKey);
                fields[fieldKey]=GUILayout.TextField(text,GUILayout.MinWidth(38),GUILayout.ExpandWidth(true));
            }
            if(GUILayout.Button("Set",GUILayout.Width(45))) Run(()=>
            {
                var v=new Vector3(float.Parse(fields[selectedKey+":"+key+":0"],CultureInfo.InvariantCulture),float.Parse(fields[selectedKey+":"+key+":1"],CultureInfo.InvariantCulture),float.Parse(fields[selectedKey+":"+key+":2"],CultureInfo.InvariantCulture));
                if(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z)) throw new Exception("Enter finite numbers");
                if(key=="scale" && (v.x<=0||v.y<=0||v.z<=0)) throw new Exception("Scale must be positive");
                if(key=="scale" && selected?.name=="LADDER" && (v.x>20||v.y>20||v.z>20)) throw new Exception("Ladder dimensions must be at most 20 m");
                CheckpointSelection();
                if(key=="euler") EditSession.SetRotation(selected,Quaternion.Euler(v));
                else if(key=="viewEuler") { var temp=new ConfigNode(); EditSession.SetRotation(temp,Quaternion.Euler(v)); selected.SetValue("viewRotation",temp.GetValue("rotation"),true); }
                else EditSession.SetVector(selected,key,v);
                Changed(false);
            });
            GUILayout.EndHorizontal();
        }
        void Changed(bool checkpoint)
        {
            if(checkpoint) session.Checkpoint(); metadataEditControl=null; fields.Clear(); dirtyPreview=true;
            Log.Trace("Editing selection="+selectedKey);
        }
        void CheckpointSelection()
        {
            session.Checkpoint();
            if(selected?.name=="PROP_EDIT" && !session.Layout.GetNodes("PROP_EDIT").Contains(selected)) session.Layout.AddNode(selected);
        }
                Transform SelectionTransform => selectedKey=="whole" || string.IsNullOrEmpty(selectedKey) ? preview.Group : selectedItem?.Transform;
        void DrawSelection(Rect rect)
        {
            if(Event.current.type!=EventType.Repaint) return;
            if(selectedItem==null || selectedItem.CameraItem || selectedItem.EVAItem) return;
            var rs=selectedItem.Transform.GetComponentsInChildren<Renderer>(); if(rs.Length==0) return;
            var bounds=rs[0].bounds; foreach(var r in rs) bounds.Encapsulate(r.bounds);
            var points=new Vector2[8];
            for(int i=0;i<8;i++) points[i]=preview.ScreenPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)),rect);
            GUI.color=new Color(1,0.65f,0.1f);
            for(int i=0;i<8;i++) for(int bit=1;bit<=4;bit*=2) if((i&bit)==0 && rect.Contains(points[i]) && rect.Contains(points[i|bit])) Line(points[i],points[i|bit]);
            GUI.color=Color.white;
        }
        void DrawHandles(Rect rect)
        {
            selected=FindSelected(); var target=SelectionTransform;
            if((selected?.name=="SEAT" || selected?.name=="CAMERA" || selected?.name=="HATCH" || selected?.name=="EXIT") && mode==2) target=null;
            if(selectedItem?.CameraItem==true && (!connections || selectedItem.ReadOnly)) target=null;
            if(connections && selected?.name!="CAMERA") target=null;
            if(selectedItem?.EVAItem==true && !evaPanel) target=null;
            if(evaPanel && !EVAConfiguration.Point(selected)) target=null;
            gizmos.Configure(target,mode,localAxes,preview.Camera,rect.height,hoverAxis,dragAxis);
            if(!PointerActive && target!=null && rect.Contains(Event.current.mousePosition) && (Event.current.type==EventType.Repaint || Event.current.type==EventType.MouseDown)) hoverAxis=gizmos.Pick(Event.current.mousePosition,rect,preview.Camera);
            else if(!PointerActive && (target==null || !rect.Contains(Event.current.mousePosition))) hoverAxis=-1;
            if(Event.current.type==EventType.MouseDown && Event.current.button==0 && !PointerActive && hoverAxis>=0 && rect.Contains(Event.current.mousePosition))
            { StartGizmoDrag(hoverAxis,rect); Event.current.Use(); }
            if(Event.current.type==EventType.Repaint) gizmos.Render(preview.Camera);
        }
        void StartGizmoDrag(int handle,Rect rect)
        {
            dragRedoBefore=session.RedoStack.ToArray(); CheckpointSelection(); dragAxis=handle; dragMode=mode; dragChanged=false; CapturePointer();
            var t=SelectionTransform; dragOrigin=t.position; dragSize=gizmos.Size;
            dragAxes=new[]{gizmos.Axis(0),gizmos.Axis(1),gizmos.Axis(2)}; dragAxisWorld=dragAxes[handle%3];
            dragValue=Configuration.Vector(selected,dragMode==2?"scale":"position",dragMode==2?Vector3.one:Vector3.zero);
            dragRotation=Configuration.Rotation(selected); dragParentInverse=t.parent.worldToLocalMatrix; dragParentRotation=t.parent.rotation;
            if(selectedItem?.CameraItem==true || selectedItem?.EVAItem==true) { dragParentInverse=selectedItem.CameraFrame.inverse; dragParentRotation=selectedItem.CameraBasis*selectedItem.CameraOffsetRotation; }
            dragStart=ScreenMouse;
            var origin=preview.ScreenPoint(dragOrigin,rect); var endpoint=preview.ScreenPoint(dragOrigin+dragAxisWorld*dragSize,rect);
            dragScreenDirection=(endpoint-origin).normalized; if(dragScreenDirection.sqrMagnitude<0.01f) dragScreenDirection=Vector2.up;
            if(handle==6) dragScreenDirection=new Vector2(1,-1).normalized;
            if(dragMode==1 || handle>=3) dragNormal=dragAxisWorld;
            else
            {
                dragNormal=Vector3.Cross(dragAxisWorld,Vector3.Cross(preview.Camera.transform.forward,dragAxisWorld)).normalized;
                if(dragNormal.sqrMagnitude<0.01f) dragNormal=preview.Camera.transform.up;
            }
            var ray=preview.Ray(Event.current.mousePosition,rect);
            GizmoController.RelativePlanePoint(ray,dragOrigin,dragNormal,out dragPoint);
            dragPreviousAngle=RingAngle(ray); dragAngle=0;
        }
        float RingAngle(Ray ray)
        {
            if(GizmoController.RelativePlanePoint(ray,dragOrigin,dragAxisWorld,out var point))
            {
                var v=point; return Mathf.Atan2(Vector3.Dot(v,dragAxes[(dragAxis+2)%3]),Vector3.Dot(v,dragAxes[(dragAxis+1)%3]))*Mathf.Rad2Deg;
            }
            var center=preview.ScreenPoint(dragOrigin,screenView); var d=ScreenMouse-center;
            return Mathf.Atan2(-d.y,d.x)*Mathf.Rad2Deg;
        }
        void UpdateGizmoDrag()
        {
            if(preview==null || SelectionTransform==null) { EndPointer(true); return; }
            var mouse=ScreenMouse; var ray=preview.Ray(mouse,screenView); var pixels=mouse-dragStart;
            if(pixels.sqrMagnitude<0.5f && !dragChanged) return;
            dragChanged=true;
            if(dragMode==1)
            {
                float angle=RingAngle(ray); dragAngle+=ManipulationMath.AngleDelta(dragPreviousAngle,angle); dragPreviousAngle=angle;
                float snapped=ManipulationMath.Snap(dragAngle,rotateSnap,snap);
                var axis=Quaternion.Inverse(dragParentRotation)*dragAxisWorld;
                EditSession.SetRotation(selected,Quaternion.AngleAxis(snapped,axis)*dragRotation);
            }
            else if(dragMode==2)
            {
                float factor=ManipulationMath.ScaleFactor(Vector2.Dot(pixels,dragScreenDirection),scaleSnap,snap);
                var scale=dragValue;
                if(dragAxis==6) for(int i=0;i<3;i++) scale[i]=ManipulationMath.ScaleValue(scale[i],factor); else scale[dragAxis]=ManipulationMath.ScaleValue(scale[dragAxis],factor);
                if(selected?.name=="LADDER") for(int i=0;i<3;i++) scale[i]=Mathf.Min(20,scale[i]);
                EditSession.SetVector(selected,"scale",scale);
            }
            else
            {
                Vector3 movement;
                if(GizmoController.RelativePlanePoint(ray,dragOrigin,dragNormal,out var point))
                {
                    var d=point-dragPoint;
                    if(dragAxis<3) movement=dragAxisWorld*ManipulationMath.Snap(Vector3.Dot(d,dragAxisWorld),moveSnap,snap);
                    else
                    {
                        var a=dragAxes[(dragAxis-3+1)%3]; var b=dragAxes[(dragAxis-3+2)%3];
                        movement=a*ManipulationMath.Snap(Vector3.Dot(d,a),moveSnap,snap)+b*ManipulationMath.Snap(Vector3.Dot(d,b),moveSnap,snap);
                    }
                }
                else movement=dragAxis<3?dragAxisWorld*ManipulationMath.Snap(Vector2.Dot(pixels,dragScreenDirection)*dragSize/90,moveSnap,snap):Vector3.zero;
                EditSession.SetVector(selected,"position",dragValue+dragParentInverse.MultiplyVector(movement));
            }
            if(selectedItem?.EVAItem==true) preview.UpdateEVA(selectedItem); else if(selectedItem?.CameraItem==true) preview.UpdateCamera(selectedItem); else Configuration.ApplyTransform(SelectionTransform,selected); fields.Clear();
        }
        static void Line(Vector2 from,Vector2 to)
        {
            var matrix=GUI.matrix; var d=to-from;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg,from);
            GUI.DrawTexture(new Rect(from.x,from.y,d.magnitude,2),Texture2D.whiteTexture); GUI.matrix=matrix;
        }
        void HandlePreviewInput(Rect rect)
        {
            var e=Event.current;
            if(PointerActive) return;
            if(!rect.Contains(e.mousePosition)) return;
            if(e.type==EventType.ScrollWheel) { preview.Zoom(e.delta.y); e.Use(); }
            else if(e.type==EventType.MouseDrag && e.button==1) { preview.Orbit(e.delta); e.Use(); }
            else if(e.type==EventType.MouseDrag && e.button==2) { preview.Pan(e.delta); e.Use(); }
            else if(e.type==EventType.MouseDown && e.button==0)
            {
                var item=evaPanel?preview.PickEVA(e.mousePosition,rect):(connections?preview.PickCamera(e.mousePosition,rect):null) ?? preview.Pick(e.mousePosition,rect); if(item!=null) Select(item); e.Use();
            }
        }
        void Run(Action action)
        {
            try { action(); }
            catch(Exception e) { message=e.Message; Log.Error("Editor operation: "+message,e); }
        }
    }
}







