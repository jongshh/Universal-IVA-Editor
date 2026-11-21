using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UniversalIVAEditor
{
    public static class RevivaListTransaction
    {
        public static void Commit(IList target,IList staged,Action apply,Action rollback)
        {
            if(staged.Count==0) throw new FormatException("An IVA switch must retain at least one subtype");
            var previous=target.Cast<object>().ToArray(); var next=staged.Cast<object>().ToArray();
            try { target.Clear(); foreach(var item in next) target.Add(item); apply(); }
            catch { target.Clear(); foreach(var item in previous) target.Add(item); rollback(); throw; }
        }
    }
    public sealed class RevivaChoice
    {
        public string Key,Title,Signature,Source;
        public ConfigNode Snapshot;
        public readonly List<string> Aliases=new List<string>();
    }

    public static class RevivaChoices
    {
        public const string Prefix="UIVA_";
        public const string None="UIVA_NONE";
        public static bool Owned(string key) => key!=null && key.StartsWith(Prefix,StringComparison.Ordinal);
        public static string Key(string part,string source,string id)
        {
            string normalized=(source ?? "").Replace('\\','/');
            string marker="UniversalIVAEditor/UserPresets/";
            int offset=normalized.IndexOf(marker,StringComparison.OrdinalIgnoreCase);
            if(offset>=0) normalized=normalized.Substring(offset+marker.Length);
            var identity=new ConfigNode("CHOICE"); identity.AddValue("part",part);
            identity.AddValue("source",normalized.ToLowerInvariant());
            if(string.IsNullOrEmpty(normalized) || normalized=="memory") identity.AddValue("id",id);
            return Prefix+Configuration.Signature(identity);
        }
        public static string Content(ConfigNode snapshot)
        {
            var copy=Configuration.Copy(snapshot); copy.name="CONTENT";
            foreach(var field in new[]{"version","id","part","title","author","description","layout","ownsIVA"}) copy.RemoveValues(field);
            var layout=copy.GetNode("UIVA_LAYOUT"); layout?.RemoveValues("id"); layout?.RemoveValues("version");
            return Configuration.Signature(copy);
        }
        public static List<RevivaChoice> Build(Catalog catalog,string part,int capacity,Action<string> warning=null)
        {
            var result=new List<RevivaChoice>();
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var entry in catalog.Presets.Where(e=>e.Value.GetValue("part")==part)
                .OrderBy(e=>Managed(catalog,e.Key)?1:0).ThenBy(e=>Source(catalog,e.Key),StringComparer.Ordinal).ThenBy(e=>e.Key,StringComparer.Ordinal))
            {
                try
                {
                    var snapshot=catalog.Snapshot(entry.Key,part,capacity);
                    var source=Source(catalog,entry.Key);
                    bool multiple=catalog.Presets.Count(e=>Source(catalog,e.Key)==source && e.Value.GetValue("part")==part)>1;
                    string key=Key(part,multiple?source+"#"+entry.Key:source,entry.Key),signature=Content(snapshot);
                    var duplicate=result.FirstOrDefault(e=>e.Signature==signature);
                    if(duplicate!=null) { if(duplicate.Key!=key) duplicate.Aliases.Add(key); continue; }
                    string title=snapshot.GetValue("title");
                    if(string.IsNullOrWhiteSpace(title) && !Managed(catalog,entry.Key) && source!="memory") title=Path.GetFileNameWithoutExtension(source.Replace('\\','/'));
                    if(string.IsNullOrWhiteSpace(title)) title=part+(result.Count+1);
                    string unique=title; int suffix=2; while(!names.Add(unique)) unique=title+" ("+(suffix++)+")";
                    result.Add(new RevivaChoice { Key=key,Title=unique,Signature=signature,Snapshot=snapshot,Source=source });
                }
                catch(Exception e) { warning?.Invoke("Reviva preset "+entry.Key+": "+e.Message); }
            }
            return result;
        }
        static string Source(Catalog catalog,string id) => catalog.Sources.TryGetValue("UIVA_PRESET:"+id,out var source)?source:"memory";
        static bool Managed(Catalog catalog,string id) => Source(catalog,id).Replace('\\','/').IndexOf("/Active/",StringComparison.OrdinalIgnoreCase)>=0;
        public static ConfigNode Subtype(string key,string title,string internalName)
        {
            var subtype=new ConfigNode("SUBTYPE"); subtype.AddValue("name",key); subtype.AddValue("title",title);
            subtype.AddValue("descriptionSummary",title);
            var module=subtype.AddNode("MODULE"); module.AddNode("IDENTIFIER").AddValue("name","ModuleIVASwitch");
            module.AddNode("DATA").AddValue("internalName",internalName);
            return subtype;
        }
    }
}
