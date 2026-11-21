using System;
using System.Collections;
using System.Linq;
namespace UniversalIVAEditor
{
    public static class ProviderRegistry
    {
        public static object AddMissing(IList list,Type type,Func<object> create)
        {
            var existing=list.Cast<object>().FirstOrDefault(value=>value?.GetType()==type); if(existing!=null) return existing;
            var instance=create(); if(instance==null || instance.GetType()!=type) throw new InvalidOperationException("Adapter factory returned incompatible instance");
            list.Add(instance); return instance;
        }
    }
}

