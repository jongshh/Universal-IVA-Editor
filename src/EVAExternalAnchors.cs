using System;
using System.Collections.Generic;
using System.Linq;
namespace UniversalIVAEditor
{
                                                                                  
                                                                                     
    public sealed class EVAExternalAnchors<T> where T:class
    {
        readonly Dictionary<string,T> entries=new Dictionary<string,T>(StringComparer.Ordinal);
        readonly Func<T,bool> alive;
        public EVAExternalAnchors(Func<T,bool> isAlive) { alive=isAlive; }
        public T Resolve(string key,Func<T> create)
        {
            if(!entries.TryGetValue(key,out var value) || !alive(value)) entries[key]=value=create();
            return value;
        }
        public void ReleaseUnused(ISet<string> retained,Action<T> retire)
        {
            foreach(var key in entries.Keys.Where(k=>!retained.Contains(k)).ToArray()) {
                var value=entries[key]; entries.Remove(key); if(alive(value)) retire(value);
            }
        }
    }
}
