using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UniversalIVAEditor
{
    public static class LayoutEngine
    {
        public sealed class Binding { public int Index; public string Name,Path; public InternalProp Prop; }
        public static string Path(Transform root,Transform target)
        {
            var indices=new List<int>();
            while(target!=root) { if(target.parent==null) throw new FormatException("Prop outside internal hierarchy"); indices.Add(target.GetSiblingIndex()); target=target.parent; }
            indices.Reverse(); return string.Join("/",indices);
        }
        public static Transform At(Transform root,string path)
        {
            foreach(var segment in path.Split(new[]{'/'},StringSplitOptions.RemoveEmptyEntries))
            { int index=int.Parse(segment); if(index<0 || index>=root.childCount) throw new FormatException("Internal hierarchy changed: "+path); root=root.GetChild(index); }
            return root;
        }
        static InternalProp[] AllProps(InternalModel model) => (model.props ?? new List<InternalProp>()).Concat(model.GetComponentsInChildren<InternalProp>(true)).Where(p=>p!=null && (p.transform==model.transform || p.transform.IsChildOf(model.transform))).Distinct().ToArray();
        public static Binding[] Bindings(InternalModel prefab,ConfigNode source,Action<string> warning=null)
        {
            var compiled=AllProps(prefab); var definitions=source.GetNodes("PROP");
            var candidates=compiled.Select(p=>new PropMapping.Candidate{FieldName=p.propName,ObjectName=p.gameObject.name,Path=Path(prefab.transform,p.transform)}).ToArray();
            var mapping=PropMapping.Resolve(definitions.Select(n=>n.GetValue("name")).ToArray(),candidates);
            Log.Trace("Mapping "+prefab.internalName+" sourceProps="+definitions.Length+" hierarchyProps="+compiled.Length+" mapped="+mapping.Matches.Count);
            foreach(var problem in mapping.Warnings) { warning?.Invoke(problem); Log.Info("Prop mapping warning IVA="+prefab.internalName+" "+problem); }
            foreach(var c in candidates) Log.Trace("Prop candidate field="+(c.FieldName??"<null>")+" object="+c.ObjectName+" path="+c.Path);
            return mapping.Matches.OrderBy(p=>p.Key).Select(p=>new Binding{Index=p.Key,Name=definitions[p.Key].GetValue("name"),Path=candidates[p.Value].Path,Prop=compiled[p.Value]}).ToArray();
        }
        static Binding RequireBinding(Binding[] bindings,int index)
        {
            return bindings.FirstOrDefault(b=>b.Index==index) ?? throw new FormatException("Cannot safely apply edited prop ["+index+"]. Remove its edit or reconnect it; other IVA editing remains available.");
        }
        public static Transform Delta(Transform target,ConfigNode change)
        {
            var baseline=new GameObject("UIVA_Baseline").transform; baseline.SetParent(target.parent,false);
            baseline.localPosition=target.localPosition; baseline.localRotation=target.localRotation; baseline.localScale=target.localScale;
            var delta=new GameObject("UIVA_Delta").transform; delta.SetParent(baseline,false);
            target.SetParent(delta,false); target.localPosition=Vector3.zero; target.localRotation=Quaternion.identity; target.localScale=Vector3.one;
            Configuration.ApplyTransform(delta,change); return delta;
        }
        public static void RebindInstance(InternalModel model,InternalModel template,Catalog catalog)
        {
                                                                                                                                    
            var originalProps=AllProps(template);
            var sourceDefinition=catalog.Internals[template.internalName];
            var placements=Bindings(template,sourceDefinition).ToDictionary(b=>b.Prop,b=>sourceDefinition.GetNodes("PROP")[b.Index]);
            var resolved=originalProps.Select(p=>At(model.transform,Path(template.transform,p.transform)).GetComponent<InternalProp>()).ToArray();
            if(resolved.Any(p=>p==null)) throw new FormatException("Cloned IVA is missing an InternalProp component");
            Log.Trace("Rebind IVA="+model.internalName+" templateProps="+template.props.Count+" clonedList="+model.props.Count+" hierarchyProps="+resolved.Length);
            model.props=resolved.ToList(); model.seats=new List<InternalSeat>(); model.internalConfig=Configuration.Copy(template.internalConfig);
            for(int i=0;i<resolved.Length;i++)
            {
                var p=resolved[i]; var original=originalProps[i]; p.internalModel=model; p.propID=original.propID;
                var names=new HashSet<string>(template.internalConfig.GetNodes("PROP").Select(n=>n.GetValue("name")),StringComparer.Ordinal);
                var identity=PropMapping.Identity(new PropMapping.Candidate{FieldName=original.propName,ObjectName=original.gameObject.name},names);
                p.propName=identity ?? original.propName; p.hasModel=identity!=null || original.hasModel;
                var sourceModules=original.GetComponents<InternalModule>(); var clonedModules=p.GetComponents<InternalModule>();
                p.internalModules=new List<InternalModule>();
                foreach(var originalModule in original.internalModules.Count>0?original.internalModules:sourceModules.ToList())
                {
                    int index=Array.IndexOf(sourceModules,originalModule);
                    if(index<0 || index>=clonedModules.Length || clonedModules[index].GetType()!=originalModule.GetType()) throw new FormatException("Internal module mapping changed: "+original.propName);
                    var m=clonedModules[index]; m.internalProp=p; m.moduleID=originalModule.moduleID; p.internalModules.Add(m);
                    if(m is InternalSeat seat) model.seats.Add(seat);
                }
                if(identity!=null && catalog.Props.TryGetValue(identity,out var definition)) PropModuleConfiguration.Restore(p,PropModuleConfiguration.Merge(definition,placements.TryGetValue(original,out var placement)?placement:null),original);
                Log.Trace("Prop "+i+" name="+p.propName+" path="+Path(model.transform,p.transform)+" modules="+p.internalModules.Count);
            }
        }
        public static void Preflight(ConfigNode snapshot,Catalog catalog)
        {
            var layout=snapshot.GetNode("UIVA_LAYOUT"); var template=PartLoader.GetInternalPart(layout.GetValue("internal"));
            if(template==null) throw new FormatException("Internal prefab is unavailable");
            var bindings=Bindings(template,catalog.Internals[layout.GetValue("internal")]);
            FreeIvaCompatibility.ReconnectPaths(layout,catalog.Internals[layout.GetValue("internal")],bindings);
            foreach(var edit in layout.GetNodes("PROP_EDIT").Where(n=>!FreeIvaCompatibility.Missing(n)))
            {
                var b=RequireBinding(bindings,int.Parse(edit.GetValue("index")));
                if(edit.GetValue("path")!=b.Path) throw new FormatException("Prop hierarchy mismatch: "+b.Name);
            }
            foreach(var add in layout.GetNodes("PROP_ADD"))
            {
                if(FreeIvaCompatibility.Missing(add) || (FreeIvaCompatibility.Optional(add) && !catalog.Props.ContainsKey(add.GetValue("name")))) continue;
                if(add.HasValue("cloneIndex")) RequireBinding(bindings,int.Parse(add.GetValue("cloneIndex")));
                if(!PartLoader.Instance.internalProps.Any(p=>p.propName==add.GetValue("name"))) throw new FormatException("Prop prefab is unavailable: "+add.GetValue("name"));
            }
        }
        public static Transform Apply(InternalModel model,ConfigNode snapshot,Catalog catalog)
        {
            if(snapshot.GetValue("version")!="2") throw new FormatException("Unsupported preset format; version 2 required");
            var layout=snapshot.GetNode("UIVA_LAYOUT"); var source=catalog.Internals[layout.GetValue("internal")];
            var template=PartLoader.GetInternalPart(layout.GetValue("internal"));
            var bindings=Bindings(template,source);
            var targets=bindings.ToDictionary(b=>b.Index,b=>At(model.transform,b.Path).GetComponent<InternalProp>());
            foreach(var edit in layout.GetNodes("PROP_EDIT").Where(n=>!FreeIvaCompatibility.Missing(n)))
            {
                var binding=RequireBinding(bindings,int.Parse(edit.GetValue("index")));
                if(edit.HasValue("path") && edit.GetValue("path")!=binding.Path) throw new FormatException("Prop hierarchy mismatch: "+binding.Name);
            }
            var children=model.transform.Cast<Transform>().ToArray();
            var group=LayoutCoordinates.CreateGroup(model.transform);
            foreach(var child in children) child.SetParent(group,false);
            foreach(var edit in layout.GetNodes("PROP_EDIT").Where(n=>!FreeIvaCompatibility.Missing(n)))
            {
                var prop=targets[int.Parse(edit.GetValue("index"))];
                if(edit.GetValue("delete")=="true")
                {
                                                                                                   
                    foreach(var r in prop.GetComponentsInChildren<Renderer>(true)) r.enabled=false;
                    foreach(var c in prop.GetComponentsInChildren<Collider>(true)) c.enabled=false;
                    var visibility=model.GetComponent<DeletedPropVisibility>() ?? model.gameObject.AddComponent<DeletedPropVisibility>();
                    visibility.Track(prop.gameObject);
                }
                else Delta(prop.transform,edit);
            }
            foreach(var add in layout.GetNodes("PROP_ADD"))
            {
                if(FreeIvaCompatibility.Missing(add) || (FreeIvaCompatibility.Optional(add) && !catalog.Props.ContainsKey(add.GetValue("name")))) continue;
                var clone=add.HasValue("cloneIndex")?RequireBinding(bindings,int.Parse(add.GetValue("cloneIndex"))):null;
                if(clone!=null && clone.Name!=add.GetValue("name")) throw new FormatException("Clone source name mismatch");
                var n=clone==null?new ConfigNode("PROP"):Configuration.Copy(source.GetNodes("PROP")[clone.Index]);
                n.SetValue("name",add.GetValue("name"),true); n.SetValue("position","0,0,0",true); n.SetValue("rotation","0,0,0,1",true); n.SetValue("scale","1,1,1",true);
                var prop=model.AddProp(n) ?? throw new FormatException("Cannot create prop "+add.GetValue("name"));
                PropModuleConfiguration.Restore(prop,catalog.Props[add.GetValue("name")]);
                if(clone!=null)
                {
                                                                                                                  
                    var parent=targets[clone.Index].transform.parent;
                    if(parent.name=="UIVA_Delta") parent=parent.parent.parent;
                    prop.transform.SetParent(parent,false); prop.transform.localPosition=clone.Prop.transform.localPosition;
                    prop.transform.localRotation=clone.Prop.transform.localRotation; prop.transform.localScale=clone.Prop.transform.localScale;
                    Delta(prop.transform,add); continue;
                }
                var placement=new GameObject("UIVA_AddedPlacement").transform; placement.SetParent(group,false);
                prop.transform.SetParent(placement,false); Configuration.ApplyTransform(placement,add);
            }
            var seats=layout.GetNodes("SEAT");
            if(seats.Length>0)
            {
                var originals=model.seats.ToArray(); model.seats.Clear();
                foreach(var old in originals) foreach(var p in model.props) p.internalModules.Remove(old);
                foreach(var seat in seats.OrderBy(n=>int.Parse(n.GetValue("slot"))))
                {
                    var t=new GameObject("UIVA_Seat_"+seat.GetValue("slot")).transform; t.SetParent(group,false); Configuration.ApplyTransform(t,seat);
                    var n=seat.HasNode("MODULE") ? Configuration.Copy(seat.GetNode("MODULE")) : new ConfigNode("MODULE");
                    n.SetValue("name","InternalSeat",true); n.SetValue("seatTransformName",t.name,true); n.SetValue("kerbalEyeOffset",seat.GetValue("eyeOffset") ?? "0,0,0",true);
                    model.AddPropModule(n);
                }
            }
            Configuration.ApplyTransform(group,snapshot.GetNode("TRANSFORM"));
            Log.Trace("Flight coordinate basis IVA="+model.internalName+" rootScale="+model.transform.localScale+" layoutPosition="+group.localPosition+" layoutScale="+group.localScale); return group;
        }
    }
}



