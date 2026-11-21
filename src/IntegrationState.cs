using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace UniversalIVAEditor
{
    public sealed class IntegrationState : MonoBehaviour
    {
        readonly Dictionary<string,string> rows=new Dictionary<string,string>();
        static readonly Dictionary<string,Dictionary<string,string>> last=new Dictionary<string,Dictionary<string,string>>();
        static readonly Queue<string> order=new Queue<string>();
        public IEnumerable<string> Rows => rows.Values;
        public static IEnumerable<string> Last(string partName) => last.TryGetValue(partName,out var values)?values.Values:Enumerable.Empty<string>();
        public static void Report(Part part,string key,string status,string detail)
        {
            if(part==null || part.partInfo==null) return;
            var state=part.GetComponent<IntegrationState>() ?? part.gameObject.AddComponent<IntegrationState>();
            string value=key+" | "+status+" | "+detail;
            if(state.rows.TryGetValue(key,out var old) && old==value) return;
            if(state.rows.Count>=2048 && !state.rows.ContainsKey(key)) state.rows.Remove(state.rows.Keys.First());
            state.rows[key]=value; Log.Info("Integration part="+part.partInfo.name+" IVA="+(part.internalModel?.internalName ?? part.GetComponent<RuntimeState>()?.Snapshot?.GetNode("UIVA_LAYOUT")?.GetValue("internal"))+" "+value);
            if(!last.ContainsKey(part.partInfo.name)) { order.Enqueue(part.partInfo.name); if(order.Count>8) last.Remove(order.Dequeue()); }
            last[part.partInfo.name]=state.rows;
        }
        public static bool Owns(Part part) => part!=null && part.GetComponent<RuntimeState>()?.Snapshot!=null && part.GetComponent<RuntimeState>()?.Failed!=true;
    }
}

