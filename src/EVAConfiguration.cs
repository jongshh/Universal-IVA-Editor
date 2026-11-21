using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
    public static class EVAConfiguration
    {
        public static bool Point(ConfigNode n) => n!=null && (n.name=="HATCH" || n.name=="EXIT" || n.name=="LADDER");
        public static IEnumerable<ConfigNode> Nodes(ConfigNode snapshot) => snapshot?.GetNode("EVA")?.nodes.Cast<ConfigNode>().SelectMany(n=>n.name=="PORT"?n.nodes.Cast<ConfigNode>().Where(c=>c.name=="HATCH"||c.name=="EXIT"):n.name=="LADDER"?new[]{n}:Enumerable.Empty<ConfigNode>()) ?? Enumerable.Empty<ConfigNode>();
        public static ConfigNode Port(ConfigNode snapshot,ConfigNode point) => snapshot.GetNode("EVA")?.GetNodes("PORT").FirstOrDefault(n=>n.nodes.Cast<ConfigNode>().Contains(point));
        public static ConfigNode NewPort(Vector3 inside,Vector3 outside)
        {
            var port=new ConfigNode("PORT"); port.AddValue("id",Guid.NewGuid().ToString("N")); port.AddValue("title","Entrance");
            port.AddValue("boarding","true");
            foreach(var type in new[]{"HATCH","EXIT"}) {
                var n=port.AddNode(type); n.AddValue("id",Guid.NewGuid().ToString("N"));
                EditSession.SetVector(n,"position",type=="HATCH"?inside:outside); EditSession.SetRotation(n,Quaternion.identity);
            }
            return port;
        }
        public static void Validate(ConfigNode snapshot)
        {
            if(snapshot.GetNodes("EVA").Length>1) throw new FormatException("Duplicate EVA block");
            var block=snapshot.GetNode("EVA"); if(block==null) return;
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var port in block.GetNodes("PORT")) {
                Id(port,ids); Flag(port,"boarding");
                foreach(var name in new[]{"HATCH","EXIT"}) {
                    var points=port.GetNodes(name); if(points.Length!=1) throw new FormatException("Each EVA port requires one "+name);
                    Id(points[0],ids); Configuration.ValidateTransform(points[0]);
                    var scale=Configuration.Vector(points[0],"scale",Vector3.one);
                    if(scale!=Vector3.one) throw new FormatException("EVA Hatch and Exit support position and rotation only");
                }
            }
            foreach(var ladder in block.GetNodes("LADDER")) { Id(ladder,ids); Configuration.ValidateTransform(ladder); Size(ladder); }
        }
        static void Id(ConfigNode n,HashSet<string> ids) {
            var id=Configuration.Required(n,"id");
            if(id.Length>80 || id.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'&&c!='-') || !ids.Add(id)) throw new FormatException("Invalid or duplicate EVA ID: "+id);
        }
        static void Flag(ConfigNode n,string key) {
            if(n.HasValue(key) && n.GetValue(key)!="true" && n.GetValue(key)!="false") throw new FormatException("Invalid EVA "+key+" flag");
        }
        public static Vector3 Size(ConfigNode ladder) {
            var size=Configuration.Vector(ladder,"scale",new Vector3(0.4f,1f,0.3f));
            if(size.x<=0 || size.y<=0 || size.z<=0 || size.x>20 || size.y>20 || size.z>20) throw new FormatException("Ladder dimensions must be greater than zero and at most 20 m");
            return size;
        }
        public static string EntranceTitle(ConfigNode n) => string.IsNullOrEmpty(n.GetValue("title")) || n.GetValue("title")=="EVA exit"?"Entrance":n.GetValue("title");
        public static string PointLabel(ConfigNode n) => n.name=="HATCH"?"Internal Entry":n.name=="EXIT"?"External Entry":"Ladder";
                                                                                          
        public static Vector3 OutwardAxis(string kind) => kind=="HATCH"?Vector3.back:Vector3.forward;
                                                                              
        public static Quaternion AirlockRotation(Quaternion outward) => outward*new Quaternion(0,1,0,0);
        public static Vector3 SpawnOffset(Quaternion outward) => outward*Vector3.forward*0.18f;
    }
}
