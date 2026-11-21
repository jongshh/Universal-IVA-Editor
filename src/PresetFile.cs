using System;
using System.Linq;
namespace UniversalIVAEditor
{
                                                                                                             
    public static class PresetFile
    {
        public static ConfigNode Snapshot(ConfigNode root,Catalog assets,string part,int capacity)
        {
            if(root==null) throw new FormatException("Cannot parse selected preset");
            var presets=root.GetNodes("UIVA_PRESET");
            if(presets.Length!=1) throw new FormatException("File must contain exactly one UIVA_PRESET");
            var choice=presets[0]; var candidate=new Catalog();
            foreach(var entry in assets.Internals) candidate.Internals.Add(entry.Key,entry.Value);
            foreach(var entry in assets.Props) candidate.Props.Add(entry.Key,entry.Value);
            foreach(var node in root.nodes.Cast<ConfigNode>().Where(n=>n.name=="UIVA_LAYOUT" || n.name=="UIVA_PRESET")) candidate.AddDefinition(node,"selected preset");
            if(candidate.Errors.Count>0) throw new FormatException(string.Join("; ",candidate.Errors));
            var layoutId=Configuration.Required(choice,"layout");
            if(!candidate.Layouts.ContainsKey(layoutId) && assets.Layouts.TryGetValue(layoutId,out var installed)) candidate.Layouts.Add(layoutId,Configuration.Copy(installed));
            var data=candidate.Snapshot(Configuration.Required(choice,"id"),Configuration.Required(choice,"part"),capacity,false);
            if(data.GetValue("part")!=part) {
                data.SetValue("part",part); data.RemoveValues("ownsIVA");
                var imported=new EditSession(data); imported.ForkIds(); data=imported.Data;
            }
            data.RemoveValues("ownsIVA");
            return data;
        }
    }
}
