using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace UniversalIVAEditor
{
                                                                                      
    public static class PreviewNeutralPose
    {
        public sealed class Rule
        {
            public string Target, Channel; public Vector3 Value; public Quaternion Rotation; public bool Absolute;
        }
        sealed class Plan { public readonly List<Rule> Rules=new List<Rule>(); public readonly List<string> Warnings=new List<string>(); }
        static ConditionalWeakTable<InternalProp,Plan> plans=new ConditionalWeakTable<InternalProp,Plan>();
        static readonly Dictionary<Type,FieldInfo> horizonFields=new Dictionary<Type,FieldInfo>();
        public static void ClearCache() { plans=new ConditionalWeakTable<InternalProp,Plan>(); }
                                                                                       
                                                                                        
        public static Rule ParseRpmRule(ConfigNode set)
        {
            if(set.HasValue("threshold") || set.HasValue("modulo") || set.HasValue("animationName")) return null;
            var range=Configuration.Numbers(Configuration.Required(set,"scale"),2);
            if(range[0]==range[1]) return null;
            float amount=Mathf.Clamp01((0-range[0])/(range[1]-range[0]));
            if(string.Equals(set.GetValue("reverse"),"true",StringComparison.OrdinalIgnoreCase)) amount=1-amount;
            foreach(var channel in new[]{"localRotation","localTranslation","localScale"}) {
                if(!set.HasValue(channel+"Start") || !set.HasValue(channel+"End")) continue;
                var start=Configuration.Vector(set,channel+"Start",Vector3.zero); var end=Configuration.Vector(set,channel+"End",Vector3.zero);
                var rule=new Rule{Target=Configuration.Required(set,"controlledTransform").Trim(),Channel=channel,Value=Vector3.Lerp(start,end,amount)};
                if(channel=="localRotation") rule.Rotation=set.HasValue("longPath")?Quaternion.Euler(rule.Value):Quaternion.Slerp(Quaternion.Euler(start),Quaternion.Euler(end),amount);
                return rule;
            }
            return null;
        }
        static bool Dynamic(ConfigNode node)
        {
            if(node.HasValue("animationName") || node.HasValue("controlledTransform") || node.HasValue("localRotationStart")) return true;
            return node.nodes.Cast<ConfigNode>().Any(Dynamic);
        }
        static Plan Detect(InternalProp source)
        {
            var plan=new Plan();
            foreach(var module in source.GetComponents<InternalModule>()) if(module.GetType().Name=="FSartificialHorizon") {
                var type=module.GetType();
                if(!horizonFields.TryGetValue(type,out var field)) horizonFields[type]=field=type.GetField("outerRingName");
                var target=field?.GetValue(module) as string;
                if(!string.IsNullOrEmpty(target)) plan.Rules.Add(new Rule{Target=target,Channel="localRotation",Rotation=Quaternion.identity,Absolute=true});
            }
            var name=source.propName;
            if(string.IsNullOrEmpty(name)) name=source.gameObject.name.Replace("(Clone)","");
            if(PresetStore.Library.Props.TryGetValue(name,out var definition)) foreach(var module in definition.GetNodes("MODULE")) {
                var type=module.GetValue("name");
                if(type=="FSartificialHorizon") continue;
                if(type=="JSIVariableAnimator") foreach(var set in module.GetNodes("VARIABLESET")) {
                    if(!Dynamic(set)) continue;
                    try {
                        var rule=ParseRpmRule(set);
                        if(rule!=null) plan.Rules.Add(rule);
                        else plan.Warnings.Add(type+": dynamic animation retained in authored pose");
                    } catch(Exception e) when(e is FormatException || e is OverflowException) { plan.Warnings.Add(type+": expression-driven pose retained (no flight variable evaluation)"); }
                }
                else if(Dynamic(module)) plan.Warnings.Add(type+": dynamic pose has no preview adapter; authored pose retained");
            }
            Log.Trace("Preview pose detection: "+name+" rules="+plan.Rules.Count+" warnings="+plan.Warnings.Count);
            return plan;
        }
        public static void Apply(InternalProp source,Func<Transform,Transform> copy,Action<string> warning=null)
        {
            var plan=plans.GetValue(source,Detect);
            var conflicts=new HashSet<string>(plan.Rules.GroupBy(r=>r.Target+":"+r.Channel).Where(g=>g.Count()>1).Select(g=>g.Key));
            foreach(var rule in plan.Rules) {
                var original=source.FindModelTransform(rule.Target); var target=original==null?null:copy(original);
                if(target==null) { warning?.Invoke(source.propName+": preview target missing: "+rule.Target); continue; }
                if(conflicts.Contains(rule.Target+":"+rule.Channel)) { warning?.Invoke(source.propName+": multiple pose controllers; authored pose retained: "+rule.Target); continue; }
                if(rule.Channel=="localRotation") target.localRotation=rule.Absolute?rule.Rotation:original.localRotation*rule.Rotation;
                else if(rule.Channel=="localTranslation") target.localPosition=original.localPosition+rule.Value;
                else if(rule.Channel=="localScale") target.localScale=original.localScale+rule.Value;
            }
            if(warning!=null) foreach(var message in plan.Warnings.Distinct()) warning(source.propName+": "+message);
        }
    }
}

