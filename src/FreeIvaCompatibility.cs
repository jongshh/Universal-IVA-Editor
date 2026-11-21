using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
namespace UniversalIVAEditor
{
    public static class FreeIvaCompatibility
    {
        sealed class View
        {
            public ConfigNode Node; public string Comparison; public string Hash => Comparison??Configuration.Signature(Node); public readonly Dictionary<ConfigNode,int> Origins=new Dictionary<ConfigNode,int>();
            public View(ConfigNode source) { Node=Configuration.Copy(source); var props=Node.GetNodes("PROP"); for(int i=0;i<props.Length;i++) Origins[props[i]]=i; }
            public View Copy() { var v=new View(Node); v.Comparison=Comparison; var before=Node.GetNodes("PROP"); var after=v.Node.GetNodes("PROP"); for(int i=0;i<after.Length;i++) v.Origins[after[i]]=Origins[before[i]]; return v; }
            public int Index(int saved) { var props=Node.GetNodes("PROP"); return saved<0||saved>=props.Length?-2:Origins[props[saved]]; }
        }
        sealed class Facts
        {
            public string[] Hashes; public Dictionary<string,int[]> Occurrences; public Dictionary<string,int> Additions;
        }
        static readonly Dictionary<string,Facts> facts=new Dictionary<string,Facts>();
        public static void Invalidate() { facts.Clear(); }
        static Facts Inspect(ConfigNode source)
        {
            string signature=Configuration.Signature(source);
            if(facts.TryGetValue(signature,out var found)) return found;
            var hashes=source.GetNodes("PROP").Select(PropSignature).ToArray();
            var result=new Facts { Hashes=hashes,Occurrences=Enumerable.Range(0,hashes.Length).GroupBy(i=>hashes[i]).ToDictionary(g=>g.Key,g=>g.ToArray()),Additions=Evidence(source)?FreeIvaPatchRules.AddedProps(source):new Dictionary<string,int>() };
            if(facts.Count>=32) facts.Remove(facts.Keys.First()); facts[signature]=result; return result;
        }
        static bool FreeName(string name) => (name??"").StartsWith("FreeIva_",StringComparison.Ordinal) || name=="HandRailInvisible";
        public static bool Optional(ConfigNode n) => FreeName(n.GetValue("name")) || n.GetValue("freeIvaOptional")=="true";
        public static bool Missing(ConfigNode n) => n.GetValue("freeIvaMissing")=="true";
        static bool Evidence(ConfigNode source) => source.GetNodes("MODEL").Any(n=>(n.GetValue("model")??"").StartsWith("FreeIva/",StringComparison.OrdinalIgnoreCase)) || source.GetNodes("MODULE").Any(n=>n.GetValue("name")=="InternalModuleFreeIva");
                                                                                   
                                                                     
        static void Strip(ConfigNode node,bool modern)
        {
            if(modern) {
                foreach(var value in node.values.Cast<ConfigNode.Value>().Where(v=>v.name.StartsWith("FreeIva_",StringComparison.Ordinal)).ToArray()) node.values.Remove(value);
                if(node.HasValue("tags")) {
                    string tags=Regex.Replace(node.GetValue("tags"),@"(?: ?\?nofreeiva)+$","");
                    if(tags.Length==0) node.RemoveValues("tags"); else node.SetValue("tags",tags);
                }
                if(node.name=="MODULE" && node.GetValue("name")=="FreeIvaInternalCameraSwitch") node.SetValue("name","InternalCameraSwitch");
                if(node.GetNodes("MODEL").Any(m=>(m.GetValue("model")??"").StartsWith("FreeIva/Props/",StringComparison.OrdinalIgnoreCase))) node.RemoveValues("releaseButtonIndex");
            }
            foreach(var child in node.nodes.Cast<ConfigNode>().ToArray()) {
                string name=child.GetValue("name");
                bool module=child.name=="MODULE" && (name=="InternalModuleFreeIva" || name=="HatchConfig" || name=="ModuleFreeIva" || name=="HandRail" || (modern && (name=="FreeIvaHatch" || (name=="PropBuckleButton" && ((child.GetValue("modelName")??"").StartsWith("FreeIva/") || (child.GetValue("soundName")??"").StartsWith("FreeIva/"))))));
                bool model=child.name=="MODEL" && (child.GetValue("model")??"").StartsWith("FreeIva/",StringComparison.OrdinalIgnoreCase);
                if(module || model || (child.name=="PROP" && FreeName(name))) node.RemoveNode(child); else Strip(child,modern);
            }
        }
        static View Normal(ConfigNode source)
        {
            var v=new View(source); var info=Inspect(source); var additions=new Dictionary<string,int>(info.Additions);
                                                                                     
                                                                                   
            foreach(var prop in v.Node.GetNodes("PROP").Reverse()) {
                var signature=info.Hashes[v.Origins[prop]];
                if(additions.TryGetValue(signature,out var count) && count>0) { v.Node.RemoveNode(prop); additions[signature]=count-1; }
            }
            Strip(v.Node,true); return v;
        }
        public static ConfigNode Baseline(ConfigNode source) => Normal(source).Node;
        public static string PropSignature(ConfigNode prop) { var n=Configuration.Copy(prop); Strip(n,true); return Configuration.Signature(n); }
        static IEnumerable<View> Views(ConfigNode source)
        {
            var exact=new View(source); yield return exact;
            var legacy=new View(source); Strip(legacy.Node,false); yield return legacy;
            yield return Normal(source);
                                                                                
                                                                                    
            foreach(bool modern in new[]{false,true}) {
                var v=new View(source); Strip(v.Node,modern);
                for(int i=0;i<64;i++) {
                    var last=v.Node.GetNodes("PROP").LastOrDefault();
                    if(last==null || !Evidence(source) || !((last.GetValue("name")??"").StartsWith("Hatch_",StringComparison.Ordinal) || last.GetNodes("MODULE").Any(m=>m.GetValue("name")=="HatchConfig"))) break;
                    v.Node.RemoveNode(last); yield return v.Copy();
                }
            }
        }
        static IEnumerable<View> LegacyPredictions(ConfigNode source)
        {
            foreach(var prediction in new[]{Configuration.Copy(source)}.Concat(FreeIvaPatchRules.Predict(source))) {
                var current=Inspect(source); var original=new View(prediction); var occurrences=new Dictionary<string,int>();
                foreach(var prop in original.Node.GetNodes("PROP")) {
                    string hash=PropSignature(prop); int ordinal=occurrences.TryGetValue(hash,out var count)?count:0; occurrences[hash]=ordinal+1;
                    original.Origins[prop]=current.Occurrences.TryGetValue(hash,out var candidates) && ordinal<candidates.Length?candidates[ordinal]:-1;
                }
                var portable=original.Copy(); portable.Comparison=Normal(prediction).Hash;
                yield return portable;
                foreach(bool camera in new[]{false,true}) foreach(bool tags in new[]{false,true}) {
                    var v=original.Copy();
                    if(camera) foreach(var m in v.Node.GetNodes("MODULE").Where(m=>m.GetValue("name")=="InternalCameraSwitch")) m.SetValue("name","FreeIvaInternalCameraSwitch");
                    if(tags) v.Node.SetValue("tags",(v.Node.GetValue("tags")??"")+" ?nofreeiva",true);
                    yield return v;
                    var old=v.Copy(); var stripped=Configuration.Copy(v.Node); Strip(stripped,false); old.Comparison=Configuration.Signature(stripped); yield return old;
                }
            }
        }
        static View Match(ConfigNode layout,ConfigNode source)
        {
            foreach(var key in new[]{"sourceSignature","freeIvaBaselineSignature","portableSourceSignature"}) {
                var hash=layout.GetValue(key); if(hash!=null && (hash.Length!=64 || hash.Any(c=>!Uri.IsHexDigit(c)))) return null;
            }
            string full=layout.GetValue("sourceSignature"),baseline=layout.GetValue("freeIvaBaselineSignature");
            if(full==Configuration.Signature(source)) return new View(source);
            if(layout.GetValue("portableSourceSignature")==Configuration.Signature(Baseline(source))) return new View(source);
            var views=Views(source).ToArray();
            foreach(var view in views) if(full==view.Hash) return view;
                                                                                 
                                                                                
                                                                               
            var predictions=LegacyPredictions(source).ToArray();
            foreach(var view in predictions) if(full==view.Hash) return view;
            if(!string.IsNullOrEmpty(baseline)) {
                foreach(var view in predictions) if(baseline==view.Hash) return view;
                foreach(var view in views) if(baseline==view.Hash) return view;
            }
            return null;
        }
        public static bool Matches(ConfigNode layout,ConfigNode source) => Match(layout,source)!=null;
        public static void Capture(ConfigNode layout,ConfigNode source)
        {
            layout.SetValue("portableSourceSignature",Configuration.Signature(Baseline(source)),true);
        }
        static void Stamp(ConfigNode n,ConfigNode source,int index,string key,Facts info=null)
        {
            var props=source.GetNodes("PROP"); if(index<0 || index>=props.Length) throw new FormatException("Source prop index outside definition: "+index);
            info=info??Inspect(source); string hash=info.Hashes[index]; var positions=info.Occurrences[hash];
            n.SetValue(key+"Identity",hash,true); n.SetValue(key+"Occurrence",Array.IndexOf(positions,index),true);
            n.SetValue(key+"Count",positions.Length,true);
            bool optional=FreeName(props[index].GetValue("name")) || info.Additions.ContainsKey(hash);
            if(optional) n.SetValue("freeIvaOptional","true",true);
        }
        public static void StampEdit(ConfigNode edit,ConfigNode source,int index) => Stamp(edit,source,index,"source");
        public static void Reconcile(ConfigNode layout,ConfigNode source)
        {
            var view=Match(layout,source); if(view==null) throw new FormatException("Source IVA changed beyond FreeIVA metadata: "+layout.GetValue("internal"));
            var actual=source.GetNodes("PROP"); var info=Inspect(source);
            foreach(var n in layout.GetNodes("PROP_EDIT").Concat(layout.GetNodes("PROP_ADD").Where(p=>p.HasValue("cloneIndex")))) {
                bool clone=n.name=="PROP_ADD"; string key=clone?"clone":"source",indexKey=clone?"cloneIndex":"index";
                int resolved;
                if(n.HasValue(key+"Identity")) {
                    var candidates=info.Occurrences.TryGetValue(n.GetValue(key+"Identity"),out var found)?found:new int[0];
                    int count=int.Parse(Configuration.Required(n,key+"Count"),CultureInfo.InvariantCulture),ordinal=int.Parse(Configuration.Required(n,key+"Occurrence"),CultureInfo.InvariantCulture);
                    if(ordinal<0 || ordinal>=count) throw new FormatException("Invalid source occurrence");
                    if(candidates.Length==0 && Optional(n)) { n.SetValue("freeIvaMissing","true",true); continue; }
                    if(candidates.Length!=count) throw new FormatException("Ambiguous source prop after FreeIVA change: "+n.GetValue("name"));
                    resolved=candidates[ordinal];
                } else {
                    int index=int.Parse(Configuration.Required(n,indexKey),CultureInfo.InvariantCulture); resolved=view.Index(index);
                    if(resolved==-1) {
                        var old=view.Node.GetNodes("PROP")[index];
                        if(!(FreeName(old.GetValue("name")) || FreeIvaPatchRules.AddedProps(source).ContainsKey(PropSignature(old)))) throw new FormatException("Removed source prop: "+n.GetValue("name"));
                        Stamp(n,view.Node,index,key); n.SetValue("freeIvaOptional","true",true); n.SetValue("freeIvaMissing","true",true); continue;
                    }
                    if(resolved<0 || actual[resolved].GetValue("name")!=n.GetValue("name")) throw new FormatException("Source prop identity mismatch: "+n.GetValue("name"));
                    Stamp(n,source,resolved,key,info);
                }
                if(actual[resolved].GetValue("name")!=n.GetValue("name")) throw new FormatException("Source prop name changed: "+n.GetValue("name"));
                n.RemoveValues("freeIvaMissing"); n.SetValue(indexKey,resolved,true);
            }
            Capture(layout,source);
        }
        public static void ReconnectPaths(ConfigNode layout,ConfigNode source,LayoutEngine.Binding[] bindings)
        {
            bool same=layout.GetValue("sourceSignature")==Configuration.Signature(source);
            Reconcile(layout,source);
            if(same) return;
            foreach(var edit in layout.GetNodes("PROP_EDIT").Where(n=>!Missing(n))) {
                var target=bindings.SingleOrDefault(b=>b.Index.ToString()==edit.GetValue("index") && b.Name==edit.GetValue("name"));
                if(target!=null && edit.HasValue("path")) edit.SetValue("path",target.Path);
            }
        }
    }
}
