using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
namespace UniversalIVAEditor
{
                                                                                      
                                                                                      
    public static class FreeIvaPatchRules
    {
        static ConfigNode[] rules=new ConfigNode[0]; static string loadedPath;
        public static void Load(string path)
        {
            if(loadedPath==path) return;
            FreeIvaCompatibility.Invalidate();
            rules=File.Exists(path)?ConfigNode.Load(path)?.GetNodes("UIVA_FREEIVA_RULE")??new ConfigNode[0]:new ConfigNode[0]; loadedPath=path;
        }
        public static ConfigNode Describe(ConfigNode patch)
        {
            string header=patch.name;
            if(!header.StartsWith("@INTERNAL",StringComparison.Ordinal)) return null;
            var name=Regex.Match(header,@"^@INTERNAL\[([^\]]+)\]");
            var model=Regex.Match(header,@"#model\[([^\]]+)\]");
            if(!name.Success && !model.Success) return null;
            var n=new ConfigNode("UIVA_FREEIVA_RULE");
            if(name.Success) n.AddValue("internal",name.Groups[1].Value);
            if(model.Success) n.AddValue("model",model.Groups[1].Value);
            n.AddNode(Configuration.Copy(patch)); return n;
        }
        static bool Pattern(string value,string pattern) => pattern.Split('|').Any(p=>Regex.IsMatch(value??"","^"+Regex.Escape(p).Replace(@"\*",".*").Replace(@"\?",".")+"$"));
        static IEnumerable<ConfigNode> For(ConfigNode source) => rules.Where(r=>(!r.HasValue("internal")||Pattern(source.GetValue("name"),r.GetValue("internal"))) && (!r.HasValue("model")||source.GetNodes("MODEL").Any(m=>Pattern(m.GetValue("model"),r.GetValue("model"))))).Select(r=>r.nodes.Cast<ConfigNode>().Single());
        public static Dictionary<string,int> AddedProps(ConfigNode source)
        {
            var result=new Dictionary<string,int>();
            foreach(var patch in For(source)) foreach(var prop in patch.GetNodes("PROP")) {
                string key=FreeIvaCompatibility.PropSignature(prop);
                result[key]=result.TryGetValue(key,out var count)?count+1:1;
            }
            return result;
        }
        public static IEnumerable<ConfigNode> Predict(ConfigNode source)
        {
            var result=Configuration.Copy(source); var patches=For(source).ToArray();
            if(patches.Length==0) yield break;
            foreach(var patch in patches) Apply(result,patch);
            yield return result;
            var belted=Configuration.Copy(result); int belt=0;
            foreach(var seat in belted.GetNodes("MODULE").Where(n=>n.GetValue("name")=="InternalSeat" && n.HasValue("FreeIva_SeatBelt"))) {
                string path=seat.GetValue("FreeIva_SeatBelt");
                var model=belted.AddNode("MODEL"); model.AddValue("model",path); model.AddValue("name","SeatBelt_"+belt);
                var module=belted.AddNode("MODULE"); module.AddValue("name","PropBuckleButton"); module.AddValue("transformName","Release,"+belt);
                if(path=="FreeIva/Props/Harness_5pt") module.AddValue("soundName","FreeIva/Sounds/BuckleButton");
                else if(path=="FreeIva/Props/ShoulderBelt") module.AddValue("soundName","FreeIva/Sounds/SeatBelt");
                else module.AddValue("modelName",path);
                var internalModule=belted.GetNodes("MODULE").FirstOrDefault(n=>n.GetValue("name")=="InternalModuleFreeIva");
                if(internalModule!=null) { var reparent=internalModule.AddNode("Reparent"); reparent.AddValue("childTransformName","SeatBelt_"+belt); reparent.AddValue("parentTransformName",seat.GetValue("seatTransformName")); reparent.AddValue("localPosition","0,0,0"); reparent.AddValue("localRotation","0,0,0"); }
                belt++;
            }
            if(belt>0) { belted.SetValue("releaseButtonIndex",belt-1,true); yield return belted; }
        }
        static void Apply(ConfigNode target,ConfigNode patch)
        {
            foreach(ConfigNode.Value value in patch.values) {
                var name=value.name;
                if(name.StartsWith("@") || name.StartsWith("%")) target.SetValue(name.Substring(1),value.value,true);
                else if(!name.StartsWith("!") && !name.StartsWith("-")) target.AddValue(name,value.value);
            }
            foreach(ConfigNode child in patch.nodes) {
                if(child.name=="MODEL" || child.name=="MODULE" || child.name=="PROP") { target.AddNode(Configuration.Copy(child)); continue; }
                var match=Regex.Match(child.name,@"^([@!-])(MODULE|PROP)\[([^\]]+)\](?:,(\*|\d+))?$");
                if(!match.Success) continue;
                var candidates=target.GetNodes(match.Groups[2].Value).Where(n=>Pattern(n.GetValue("name"),match.Groups[3].Value));
                string ordinal=match.Groups[4].Value;
                if(ordinal!="*") candidates=candidates.Skip(string.IsNullOrEmpty(ordinal)?0:int.Parse(ordinal)).Take(1);
                foreach(var item in candidates.ToArray()) { if(match.Groups[1].Value=="@") Apply(item,child); else target.RemoveNode(item); }
            }
        }
    }
}
