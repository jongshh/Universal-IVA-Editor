using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace UniversalIVAEditor
{
    public class MASStartGate
    {
        public bool Attempted,Invoking,Failed;
        public bool Begin(bool ready)
        {
            if(!ready || Attempted || Invoking || Failed) return false;
            Invoking=true; return true;
        }
        public void Complete(bool success) { Invoking=false; Attempted=true; Failed|=!success; }
    }
    public static class MASConfiguration
    {
        public sealed class Candidate
        {
            public string Part;
            public string[] Internals;
            public ConfigNode Module;
        }
        public static bool Module(string name) => name=="MASComponent" || name=="MASMonitor" || name=="MASNavBall" || (name??"").StartsWith("AvionicsSystems.",StringComparison.Ordinal);
        public static bool Needs(ConfigNode snapshot,Catalog catalog)
        {
            var layout=snapshot?.GetNode("UIVA_LAYOUT");
            if(layout==null || !catalog.Internals.TryGetValue(layout.GetValue("internal")??"",out var source)) return false;
            if(source.GetNodes("MODULE").Any(n=>Module(n.GetValue("name")))) return true;
            return PropIntegration.PropNames(layout,source).Any(name=>catalog.Props.TryGetValue(name??"",out var prop) && prop.GetNodes("MODULE").Any(n=>Module(n.GetValue("name"))));
        }
        public static ConfigNode Sanitize(ConfigNode module)
        {
            if(module==null || module.GetValue("name")!="MASFlightComputer") throw new FormatException("Expected a MASFlightComputer configuration");
            var result=Configuration.Copy(module); result.name="MODULE";
            foreach(var key in new[]{"flightComputerId","shipDescription","persistentId","isEnabled"}) result.RemoveValues(key);
            return result;
        }
        public static ConfigNode Defaults()
        {
            var node=new ConfigNode("MODULE"); node.AddValue("name","MASFlightComputer"); node.AddValue("requiresPower",true);
            node.AddNode("PERSISTENT_VARIABLES"); node.AddNode("RPM_COLOROVERRIDE"); return node;
        }
        static ConfigNode Profile(string kind,string part,ConfigNode module)
        {
            var result=new ConfigNode("MAS"); result.AddValue("source",kind);
            if(!string.IsNullOrEmpty(part)) result.AddValue("sourcePart",part);
            result.AddNode(Sanitize(module)); return result;
        }
        public static ConfigNode Select(ConfigNode snapshot,ConfigNode existing,string targetPart,IEnumerable<Candidate> candidates)
        {
            if(existing!=null) return Profile("native",targetPart,existing);
            var saved=snapshot.GetNode("INTEGRATION")?.GetNode("MAS");
            if(saved!=null) { var result=Configuration.Copy(saved); result.RemoveNodes("MODULE"); result.AddNode(Sanitize(saved.GetNode("MODULE"))); return result; }
            string iva=snapshot.GetNode("UIVA_LAYOUT").GetValue("internal");
            var hashes=new HashSet<string>(StringComparer.Ordinal);
            foreach(var source in candidates.OrderBy(c=>c.Part,StringComparer.Ordinal))
            {
                if(source.Module==null || !(source.Internals??new string[0]).Contains(iva,StringComparer.Ordinal)) continue;
                var module=Sanitize(source.Module);
                if(hashes.Add(Configuration.Signature(module))) return Profile("original",source.Part,module);
            }
            return Profile("default",null,Defaults());
        }
        public static ConfigNode Attach(ConfigNode snapshot,ConfigNode profile)
        {
            var result=Configuration.Copy(snapshot);
            var integration=result.GetNode("INTEGRATION") ?? result.AddNode("INTEGRATION");
            integration.RemoveNodes("MAS"); integration.AddNode(Configuration.Copy(profile)); return result;
        }
        public static void Validate(ConfigNode snapshot)
        {
            var integration=snapshot.GetNode("INTEGRATION"); if(integration==null) return;
            var profiles=integration.GetNodes("MAS");
            if(profiles.Length>1) throw new FormatException("Duplicate MAS profile");
            if(profiles.Length==0) return;
            if(profiles[0].GetNodes("MODULE").Length!=1) throw new FormatException("MAS profile requires one computer MODULE");
            Sanitize(profiles[0].GetNode("MODULE"));
            if(profiles[0].GetNodes("MODULE").Any(n=>n.GetValue("name")!="MASFlightComputer")) throw new FormatException("MAS profile contains an unrelated module");
        }
        public static string ComputerId(uint persistentPartId)
        {
            if(persistentPartId==0) throw new InvalidOperationException("Waiting for KSP part persistent identity");
            using(var hash=SHA256.Create())
            {
                var bytes=hash.ComputeHash(Encoding.UTF8.GetBytes("UniversalIVAEditor.MAS/v1:"+persistentPartId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                return new Guid(bytes.Take(16).ToArray()).ToString("D");
            }
        }
    }
}
