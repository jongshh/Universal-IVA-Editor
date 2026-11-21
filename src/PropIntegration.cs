using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
namespace UniversalIVAEditor
{
    public static class PropIntegration
    {
        static ConditionalWeakTable<ConfigNode,ConfigNode[]> references=new ConditionalWeakTable<ConfigNode,ConfigNode[]>();
        static readonly Regex pluginVariable=new Regex(@"PLUGIN_([A-Za-z0-9_]+):([A-Za-z0-9_]+)",RegexOptions.Compiled);
        public static void ClearCache() { references=new ConditionalWeakTable<ConfigNode,ConfigNode[]>(); }
        public static IEnumerable<ConfigNode> Requirements(ConfigNode definition) => references.GetValue(definition,n=>Scan(n).ToArray());
        static IEnumerable<ConfigNode> Scan(ConfigNode definition)
        {
            foreach(ConfigNode.Value value in definition.values) foreach(Match match in pluginVariable.Matches(value.value??"")) {
                var reference=new ConfigNode("PLUGIN_REF"); reference.AddValue("name",match.Groups[1].Value); reference.AddValue("method",match.Groups[2].Value); yield return reference;
            }
            foreach(ConfigNode node in definition.nodes) {
                if(node.name=="MODULE" && MASConfiguration.Module(node.GetValue("name"))) { var dependency=new ConfigNode("PLUGIN_REF"); dependency.AddValue("name","AvionicsSystems"); dependency.AddValue("method",node.GetValue("name")); yield return dependency; }
                if(node.name=="PLUGINACTION" || node.name=="BACKGROUNDHANDLER") {
                    var reference=new ConfigNode("PLUGIN_REF"); reference.AddValue("name",node.GetValue("name"));
                    foreach(var key in new[]{"actionMethod","stateMethod","method","isPartModule"}) if(node.HasValue(key)) reference.AddValue(key,node.GetValue(key));
                    yield return reference;
                }
                if(node.HasValue("cameraTransform")) foreach(var camera in node.GetValue("cameraTransform").Split('|')) {
                    var reference=new ConfigNode("CAMERA_REF"); reference.AddValue("transform",camera.Trim()); yield return reference;
                }
                foreach(var reference in Scan(node)) yield return reference;
            }
        }
        public static IEnumerable<string> PropNames(ConfigNode layout,ConfigNode source)
        {
            var deleted=new HashSet<int>(layout.GetNodes("PROP_EDIT").Where(n=>n.GetValue("delete")=="true").Select(n=>int.Parse(n.GetValue("index"))));
            return source.GetNodes("PROP").Where((n,i)=>!deleted.Contains(i)).Concat(layout.GetNodes("PROP_ADD")).Select(n=>n.GetValue("name")).Distinct();
        }
        public static void FlightDiagnostics(ConfigNode layout,Catalog catalog)
        {
            if(!catalog.Internals.TryGetValue(layout.GetValue("internal"),out var source)) return;
            bool needsFar=PropNames(layout,source).Any(name=>catalog.Props.TryGetValue(name,out var definition) && Requirements(definition).Any(n=>n.GetValue("name")=="JSIFAR"));
            if(!needsFar) return;
            var type=AssemblyLoader.loadedAssemblies.Select(a=>a.assembly.GetType("JSI.JSIFAR")).FirstOrDefault(t=>t!=null);
            try {
                var status=type?.GetField("farFound",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
                if(status==null || !(bool)status.GetValue(null)) Log.Info("Integration: RPM JSIFAR adapter unavailable or initialization failed. Check RPM/FAR compatibility; preserving Prop configuration cannot replace the adapter.");
                else Log.Trace("Integration: RPM JSIFAR adapter initialized; flap actions use the vessel's FAR control surfaces.");
            } catch(Exception e) { Log.Error("Integration: RPM JSIFAR adapter status could not be read",e); }
        }
        public static IEnumerable<string> Warnings(ConfigNode layout,Catalog catalog,Part part)
        {
            if(!catalog.Internals.TryGetValue(layout.GetValue("internal"),out var source)) yield break;
            bool farInstalled=AssemblyLoader.loadedAssemblies.Any(a=>a.assembly.GetName().Name=="FerramAerospaceResearch");
            var parts=part.vessel==null?new[]{part}:part.vessel.parts.ToArray();
            var seen=new HashSet<string>();
            foreach(var name in PropNames(layout,source)) {
                if(!catalog.Props.TryGetValue(name,out var definition)) continue;
                foreach(var requirement in Requirements(definition)) {
                    string message=null;
                    if(requirement.name=="PLUGIN_REF" && requirement.GetValue("name")=="JSIFAR" && !farInstalled) message="FAR plugin unavailable: "+name+" needs FerramAerospaceResearch and RPM JSIFAR.";
                    if(requirement.name=="CAMERA_REF") {
                        var camera=requirement.GetValue("transform");
                        if(camera=="CurrentReferenceDockingPortCamera") continue;                                                
                        bool found=parts.Any(p=>p.FindModelTransform(camera)!=null || p.Modules.Cast<PartModule>().Any(m=>m.moduleName=="JSIExternalCameraSelector" && m.Fields["cameraIDPrefix"]!=null && camera.StartsWith((string)m.Fields["cameraIDPrefix"].GetValue(m),StringComparison.Ordinal)));
                        if(!found) message="Camera source missing: "+name+" needs '"+camera+"' on the vessel. A Prop alone does not supply the source cockpit's external camera module/model.";
                    }
                    if(message!=null && seen.Add(message)) yield return message;
                }
            }
        }
    }
}

