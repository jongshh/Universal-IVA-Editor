using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace UniversalIVAEditor
{
    public static class Configuration
    {
        public static string FileText(ConfigNode root) => string.Join(Environment.NewLine,root.nodes.Cast<ConfigNode>().Select(n=>n.ToString()));
        public static ConfigNode Copy(ConfigNode node) { var copy = new ConfigNode(node.name); node.CopyTo(copy); return copy; }
        public static string Required(ConfigNode n, string key) => !string.IsNullOrWhiteSpace(n.GetValue(key)) ? n.GetValue(key) : throw new FormatException("Missing " + key + " in " + n.name);
        public static void Versioned(ConfigNode n)
        {
            if (Required(n, "version") != "2") throw new FormatException("Unsupported preset format; version 2 required");
            Required(n, "id");
        }
        public static float[] Numbers(string value, int count)
        {
            var pieces = (value ?? "").Split(',');
            if (pieces.Length != count) throw new FormatException("Expected " + count + " components: " + value);
            var result = pieces.Select(p => float.Parse(p.Trim(), CultureInfo.InvariantCulture)).ToArray();
            if (result.Any(x => float.IsNaN(x) || float.IsInfinity(x))) throw new FormatException("Non-finite transform");
            return result;
        }
        public static Vector3 Vector(ConfigNode n, string key, Vector3 fallback)
        {
            if (!n.HasValue(key)) return fallback;
            var v = Numbers(n.GetValue(key), 3); return new Vector3(v[0], v[1], v[2]);
        }
        public static Quaternion Rotation(ConfigNode n,string key="rotation")
        {
            if (!n.HasValue(key)) return new Quaternion(0,0,0,1);
            var v = Numbers(n.GetValue(key), 4);
            double length = Math.Sqrt(v.Sum(x => (double)x*x));
            if (length < 1e-8) throw new FormatException("Zero quaternion");
            return new Quaternion((float)(v[0]/length), (float)(v[1]/length), (float)(v[2]/length), (float)(v[3]/length));
        }
        public static void ValidateTransform(ConfigNode n)
        {
            if (n == null) return;
            Vector(n, "position", new Vector3()); Rotation(n);
            var s = Vector(n, "scale", new Vector3(1,1,1));
            if (s.x <= 0 || s.y <= 0 || s.z <= 0) throw new FormatException("Scale must be positive");
        }
        public static void ApplyTransform(Transform t, ConfigNode n)
        {
            if (n == null) return;
            ValidateTransform(n);
            t.localPosition = Vector(n, "position", t.localPosition);
            if (n.HasValue("rotation")) t.localRotation = Rotation(n);
            t.localScale = Vector(n, "scale", t.localScale);
        }
        public static string Signature(ConfigNode source)
        {
            var b = new StringBuilder(); Canonical(source, b);
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-", "").ToLowerInvariant();
        }
        static void Canonical(ConfigNode n, StringBuilder b)
        {
            Append(b, n.name);
            foreach (ConfigNode.Value v in n.values.Cast<ConfigNode.Value>().OrderBy(v => v.name, StringComparer.Ordinal)) { Append(b,v.name); Append(b,v.value); }
            b.Append('|'); foreach (ConfigNode child in n.nodes) Canonical(child,b); b.Append('}');
        }
        static void Append(StringBuilder b, string s) { s = s ?? ""; b.Append(s.Length).Append(':').Append(s); }
        public static void ValidateSnapshot(ConfigNode snapshot, ConfigNode source, Func<string,bool> propExists, int capacity, bool requireSeatCapacity=true)
        {
            Versioned(snapshot);
            IntegrationConfiguration.Validate(snapshot);
            EVAConfiguration.Validate(snapshot);
            var layout = snapshot.GetNode("UIVA_LAYOUT") ?? throw new FormatException("Missing layout snapshot");
            Versioned(layout); Required(layout,"internal");
            if(snapshot.GetValue("version")!=layout.GetValue("version")) throw new FormatException("Mixed preset/layout versions");
            Required(layout,"sourceSignature");
            if (!FreeIvaCompatibility.Matches(layout,source)) throw new FormatException("Source IVA changed beyond FreeIva metadata; IVA="+layout.GetValue("internal")+" saved="+layout.GetValue("sourceSignature")+" current="+Signature(source)+"; select the installed IVA again to rebuild this layout");
            FreeIvaCompatibility.Reconcile(layout,source);
            ValidateTransform(snapshot.GetNode("TRANSFORM"));
            var original = source.GetNodes("PROP"); var touched = new HashSet<int>(); var ids = new HashSet<string>(StringComparer.Ordinal); var references=new HashSet<string>(StringComparer.Ordinal);
            foreach (var edit in layout.GetNodes("PROP_EDIT"))
            {
                string reference=edit.GetValue("sourceIdentity")+":"+edit.GetValue("sourceOccurrence");
                if(!references.Add(reference)) throw new FormatException("Duplicate edited source placement");
                if(FreeIvaCompatibility.Missing(edit)) { Required(edit,"name"); ValidateTransform(edit); continue; }
                int index = int.Parse(Required(edit,"index"),CultureInfo.InvariantCulture);
                if (index < 0 || index >= original.Length || original[index].GetValue("name") != Required(edit,"name") || !touched.Add(index)) throw new FormatException("Ambiguous source prop: " + index);
                if(layout.GetValue("version")=="2") Required(edit,"path");
                if (edit.HasValue("delete") && edit.GetValue("delete") != "true" && edit.GetValue("delete") != "false") throw new FormatException("Invalid delete flag");
                ValidateTransform(edit);
            }
            foreach (var add in layout.GetNodes("PROP_ADD"))
            {
                if (!ids.Add(Required(add,"id"))) throw new FormatException("Duplicate prop ID");
                if (!propExists(Required(add,"name")) && !FreeIvaCompatibility.Optional(add)) throw new FormatException("Missing prop: " + add.GetValue("name"));
                if(add.HasValue("cloneIndex") && !FreeIvaCompatibility.Missing(add))
                {
                    int index=int.Parse(add.GetValue("cloneIndex"),CultureInfo.InvariantCulture);
                    if(index<0 || index>=original.Length || original[index].GetValue("name")!=add.GetValue("name")) throw new FormatException("Invalid cloned prop source");
                }
                ValidateTransform(add);
            }
                                                                                  
                                                                                
                                                                             
                                                                     
            var slots = new HashSet<int>();
            foreach (var seat in layout.GetNodes("SEAT"))
            {
                int slot = int.Parse(Required(seat,"slot"),CultureInfo.InvariantCulture);
                if (slot < 0 || (requireSeatCapacity && slot >= capacity) || !slots.Add(slot)) throw new FormatException("Invalid or duplicate seat slot");
                Required(seat,"transform"); ValidateTransform(seat); Vector(seat,"eyeOffset",new Vector3()); Rotation(seat,"viewRotation");
            }
            if (requireSeatCapacity && slots.Count != 0 && slots.Count != capacity) throw new FormatException("Seat overrides must define every crew slot");
            if (requireSeatCapacity && slots.Count == 0 && source.GetNodes("MODULE").Count(n=>n.GetValue("name")=="InternalSeat") < capacity) throw new FormatException("Not enough seats; add a complete seat map");
        }
    }
}





