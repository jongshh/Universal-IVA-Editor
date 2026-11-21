using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UniversalIVAEditor
{
    public sealed class Catalog
    {
        public readonly Dictionary<string,ConfigNode> Internals = new Dictionary<string,ConfigNode>(StringComparer.Ordinal);
        public readonly Dictionary<string,ConfigNode> Props = new Dictionary<string,ConfigNode>(StringComparer.Ordinal);
        public readonly Dictionary<string,ConfigNode> Layouts = new Dictionary<string,ConfigNode>(StringComparer.Ordinal);
        public readonly Dictionary<string,ConfigNode> Presets = new Dictionary<string,ConfigNode>(StringComparer.Ordinal);
        public readonly Dictionary<string,string> Defaults = new Dictionary<string,string>(StringComparer.Ordinal);
        public readonly Dictionary<string,string> Sources = new Dictionary<string,string>(StringComparer.Ordinal);
        public readonly List<string> Errors = new List<string>();
        public readonly HashSet<string> Disabled=new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> conflicts = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> defaultConflicts=new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> managedParts=new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> definitionIds = new HashSet<string>(StringComparer.Ordinal);
        public void AddDefinition(ConfigNode node,string source="memory") => Definition(node,source);
        public static string UserDirectory => Path.Combine(KSPUtil.ApplicationRootPath,"GameData/UniversalIVAEditor/UserPresets");
        public static Catalog Read()
        {
            PreviewNeutralPose.ClearCache(); MASBootstrap.ClearProfiles();
            FreeIvaPatchRules.Load(Path.Combine(KSPUtil.ApplicationRootPath,"GameData/UniversalIVAEditor/Compatibility/FreeIvaRules.cfg"));
            PropIntegration.ClearCache();
            var c = new Catalog();
            foreach (var kind in new[]{"INTERNAL","PROP"})
                foreach (var item in GameDatabase.Instance.GetConfigs(kind))
                {
                    var key = item.config.GetValue("name"); if (string.IsNullOrEmpty(key)) continue;
                    c.Add(kind == "INTERNAL" ? c.Internals : c.Props,kind,key,item.config);
                    c.Sources[kind+":"+key] = item.url;
                }
            foreach(var prefab in PartLoader.Instance.internalParts)
                                if(prefab?.internalConfig!=null) {
                    if(!c.Internals.ContainsKey(prefab.internalName)) c.Internals[prefab.internalName]=Configuration.Copy(prefab.internalConfig);
                    else if(Configuration.Signature(c.Internals[prefab.internalName])!=Configuration.Signature(prefab.internalConfig))
                        Log.Info("Source definition differs from mutable prefab; using patched database IVA="+prefab.internalName+" database="+Configuration.Signature(c.Internals[prefab.internalName])+" prefab="+Configuration.Signature(prefab.internalConfig));
                }
            foreach (var kind in new[]{"UIVA_LAYOUT","UIVA_PRESET","UIVA_DEFAULT"})
                foreach (var item in GameDatabase.Instance.GetConfigs(kind))
                {
                    if (item.url.StartsWith("UniversalIVAEditor/UserPresets/",StringComparison.OrdinalIgnoreCase)) continue;
                    c.Definition(item.config,item.url);
                }
            if (Directory.Exists(UserDirectory)) foreach (var file in Directory.GetFiles(UserDirectory,"*.cfg",SearchOption.AllDirectories).OrderBy(x=>x.StartsWith(Path.Combine(UserDirectory,"Active")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)?1:0).ThenBy(x=>x,StringComparer.Ordinal))
            {
                try { var root = ConfigNode.Load(file); if (root == null) throw new FormatException("Cannot parse cfg"); foreach (ConfigNode n in root.nodes) c.Definition(n,file); }
                catch (Exception e) { c.Errors.Add(file+": "+e.Message); }
            }
            Log.Trace("Library loaded internals="+c.Internals.Count+" props="+c.Props.Count+" presets="+c.Presets.Count+" errors="+c.Errors.Count);
            return c;
        }
        void Add(Dictionary<string,ConfigNode> map,string kind,string id,ConfigNode node)
        {
            var key=kind+":"+id;
            if (conflicts.Contains(key)) return;
            if (map.ContainsKey(id) && (kind=="INTERNAL" || kind=="PROP")) { map[id]=Configuration.Copy(node); Log.Trace("Using last loaded asset definition: "+key); return; }
            if (map.ContainsKey(id)) { map.Remove(id); conflicts.Add(key); Errors.Add("Duplicate "+key); return; }
            map.Add(id,Configuration.Copy(node));
        }
        void Definition(ConfigNode n,string path)
        {
            if(n.name=="UIVA_DISABLED") { Disabled.Add(Configuration.Required(n,"part")); return; }
            if (!new[]{"UIVA_LAYOUT","UIVA_PRESET","UIVA_DEFAULT"}.Contains(n.name)) return;
            try
            {
                Configuration.Versioned(n); var id=n.GetValue("id");
                if (!definitionIds.Add(id))
                {
                    Layouts.Remove(id); Presets.Remove(id);
                    throw new FormatException("Duplicate definition ID: "+id);
                }
                if (n.name=="UIVA_LAYOUT") Add(Layouts,n.name,id,n);
                else if (n.name=="UIVA_PRESET") Add(Presets,n.name,id,n);
                else
                {
                    var part=Configuration.Required(n,"part"); var preset=Configuration.Required(n,"preset");
                    bool managed=n.GetValue("managed")=="true";
                    if(defaultConflicts.Contains(part) && !managed) throw new FormatException("Conflicting defaults for "+part);
                    if((Defaults.ContainsKey(part) && !managed) || (managed && managedParts.Contains(part)))
                    { Defaults.Remove(part); defaultConflicts.Add(part); throw new FormatException("Multiple defaults for "+part); }
                    if(managed) { managedParts.Add(part); defaultConflicts.Remove(part); }
                    Defaults[part]=preset;
                }
                Sources[n.name+":"+id]=path;
            }
            catch (Exception e) { Errors.Add(path+": "+e.Message); }
        }
        public ConfigNode Snapshot(string presetId,string partName,int capacity,bool requireSeatCapacity=true)
        {
            if (!Presets.TryGetValue(presetId,out var preset)) throw new FormatException("Missing preset: "+presetId);
            if (Configuration.Required(preset,"part")!=partName) throw new FormatException("Preset targets another part");
            if (!Layouts.TryGetValue(Configuration.Required(preset,"layout"),out var layout)) throw new FormatException("Missing layout");
            var result=Configuration.Copy(preset); result.name="UIVA_SNAPSHOT"; result.RemoveNodes("UIVA_LAYOUT"); result.AddNode(Configuration.Copy(layout));
            Validate(result,partName,capacity,requireSeatCapacity); return result;
        }
        public void Validate(ConfigNode snapshot,string partName,int capacity,bool requireSeatCapacity=true)
        {
            if (Configuration.Required(snapshot,"part")!=partName) throw new FormatException("Snapshot targets another part");
            var layout=snapshot.GetNode("UIVA_LAYOUT") ?? throw new FormatException("Missing layout");
            if (!Internals.TryGetValue(Configuration.Required(layout,"internal"),out var source)) throw new FormatException("Missing IVA");
            Configuration.ValidateSnapshot(snapshot,source,name=>Props.ContainsKey(name),capacity,requireSeatCapacity);
        }
        public static ConfigNode Document(ConfigNode snapshot)
        {
            var root=new ConfigNode(); root.AddNode(Configuration.Copy(snapshot.GetNode("UIVA_LAYOUT")));
            var preset=Configuration.Copy(snapshot); preset.RemoveValues("ownsIVA"); preset.name="UIVA_PRESET"; preset.RemoveNodes("UIVA_LAYOUT");
            preset.SetValue("layout",snapshot.GetNode("UIVA_LAYOUT").GetValue("id"),true); root.AddNode(preset); return root;
        }
        public static string NormalizeFilename(string name)
        {
            name=Path.GetFileName(name ?? "").Trim();
            foreach(var c in Path.GetInvalidFileNameChars()) name=name.Replace(c,'_');
            name=name.TrimEnd('.', ' '); if(string.IsNullOrWhiteSpace(name)) name="my-iva";
            if(!name.EndsWith(".cfg",StringComparison.OrdinalIgnoreCase)) name+=".cfg";
            if(new[]{"CON","PRN","AUX","NUL","COM1","LPT1"}.Contains(Path.GetFileNameWithoutExtension(name).ToUpperInvariant())) name="preset-"+name;
            return name;
        }
        public static void Export(ConfigNode snapshot,string filename)
        {
            snapshot=MASBootstrap.ForSave(snapshot,PresetStore.Library);
            filename=NormalizeFilename(filename);
            var layout=snapshot.GetNode("UIVA_LAYOUT") ?? throw new FormatException("Missing layout");
            var root=new ConfigNode(); root.AddNode(Configuration.Copy(layout));
            var preset=Configuration.Copy(snapshot); preset.RemoveValues("ownsIVA"); preset.name="UIVA_PRESET"; preset.RemoveNodes("UIVA_LAYOUT");
            preset.SetValue("layout",Configuration.Required(layout,"id"),true); root.AddNode(preset);
            var requirements=root.AddNode("UIVA_REQUIREMENTS");
            requirements.AddValue("internal",Configuration.Required(layout,"internal"));
            var catalog=Read();
            catalog.Internals.TryGetValue(layout.GetValue("internal"),out var source);
            foreach(var name in layout.GetNodes("PROP_ADD").Concat(source?.GetNodes("PROP") ?? new ConfigNode[0]).Select(n=>n.GetValue("name")).Distinct())
            {
                var dependency=requirements.AddNode("PROP"); dependency.AddValue("name",name);
                if(catalog.Sources.TryGetValue("PROP:"+name,out var url)) dependency.AddValue("source",url);
                if(catalog.Props.TryGetValue(name,out var definition)) foreach(var reference in PropIntegration.Requirements(definition)) dependency.AddNode(reference);
            }
            if(catalog.Sources.TryGetValue("INTERNAL:"+layout.GetValue("internal"),out var internalUrl)) requirements.AddValue("source",internalUrl);
            Directory.CreateDirectory(UserDirectory); var path=Path.Combine(UserDirectory,filename);
            PresetStore.AtomicWrite(path,Configuration.FileText(root)); PresetStore.Invalidate(); RevivaIntegration.Refresh();
        }
    }
}

