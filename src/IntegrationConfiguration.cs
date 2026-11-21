using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
    public static class IntegrationConfiguration
    {
        public static void Validate(ConfigNode snapshot)
        {
            if(snapshot.GetNodes("INTEGRATION").Length>1) throw new FormatException("Duplicate INTEGRATION block");
            MASConfiguration.Validate(snapshot);
            var integration=snapshot.GetNode("INTEGRATION"); if(integration==null) return;
            var slots=new HashSet<string>(StringComparer.Ordinal);
            foreach(var camera in integration.GetNodes("CAMERA")) {
                var slot=Configuration.Required(camera,"slot");
                if(slot.Any(c=>!char.IsLetterOrDigit(c) && c!='_') || !slots.Add(slot)) throw new FormatException("Invalid or duplicate camera slot: "+slot);
                if(slot=="CurrentReferenceDockingPortCamera") throw new FormatException("Reserved RPM virtual camera slot");
                var provider=Configuration.Required(camera,"provider"); var source=Configuration.Required(camera,"source");
                if(provider!="rpm" && provider!="hullcam") throw new FormatException("Unknown camera provider: "+provider);
                if(source!="local" && source!="existing-rpm" && source!="existing-hullcam") throw new FormatException("Unknown camera source: "+source);
                if(camera.HasValue("enabled") && camera.GetValue("enabled")!="false" && camera.GetValue("enabled")!="true") throw new FormatException("Invalid camera enabled flag");
                if(source!="local" && camera.GetValue("enabled")!="false") Configuration.Required(camera,"camera");
                if(provider=="hullcam" && source=="existing-rpm") throw new FormatException("Choose a HullCam source or a local position for HullCam output");
                Configuration.Vector(camera,"position",Vector3.zero); Configuration.Rotation(camera);
                if(provider=="hullcam" && source=="existing-hullcam") {
                    var position=Configuration.Vector(camera,"position",Vector3.zero); var rotation=Configuration.Rotation(camera);
                    if(position.sqrMagnitude>0.00000001f || rotation.x*rotation.x+rotation.y*rotation.y+rotation.z*rotation.z>0.00000001f) throw new FormatException("Existing HullCam position belongs to its source part; use RPM output to create an offset view");
                }
            }
        }
        public static IEnumerable<string> Slots(ConfigNode layout,Catalog catalog)
        {
            if(!catalog.Internals.TryGetValue(layout.GetValue("internal"),out var source)) return Enumerable.Empty<string>();
            return PropIntegration.PropNames(layout,source).Where(catalog.Props.ContainsKey).SelectMany(n=>PropIntegration.Requirements(catalog.Props[n])).Where(n=>n.name=="CAMERA_REF").Select(n=>n.GetValue("transform")).Where(n=>!string.IsNullOrEmpty(n) && n!="CurrentReferenceDockingPortCamera").Distinct();
        }
    }
}

