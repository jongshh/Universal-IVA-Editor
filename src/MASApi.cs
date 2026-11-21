using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace UniversalIVAEditor
{
    public sealed class MASApi
    {
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        public Type Computer;
        public MethodInfo ComputerStart,ComputerDestroy,PartConfig,Instance;
        public PropertyInfo Initialized,PersistentsLoaded;
        public FieldInfo ComputerId,Script,Proxy;
        public readonly Dictionary<Type,MethodInfo> PropStarts=new Dictionary<Type,MethodInfo>();
        public readonly Dictionary<Type,MethodInfo> PropDestroys=new Dictionary<Type,MethodInfo>();
        public static MethodInfo Declared(Type type,string method)
        {
            for(var cursor=type;cursor!=null;cursor=cursor.BaseType)
            {
                var found=cursor.GetMethod(method,Flags|BindingFlags.DeclaredOnly);
                if(found==null) continue;
                if(found.GetMethodBody()==null) throw new MissingMethodException("Unimplemented MAS method: "+found);
                return found;
            }
            throw new MissingMethodException(type.FullName,method);
        }
        public static MASApi Bind(Assembly assembly)
        {
            var api=new MASApi(); api.Computer=assembly.GetType("AvionicsSystems.MASFlightComputer",true);
            api.ComputerStart=Declared(api.Computer,"Start"); api.ComputerDestroy=Declared(api.Computer,"OnDestroy");
            api.PartConfig=Declared(assembly.GetType("AvionicsSystems.Utility",true),"GetPartModuleConfigNode");
            api.Instance=Declared(api.Computer,"Instance");
            api.Initialized=api.Computer.GetProperty("initialized",Flags);
            api.PersistentsLoaded=assembly.GetType("AvionicsSystems.MASPersistent",true).GetProperty("PersistentsLoaded",Flags);
            api.ComputerId=api.Computer.GetField("flightComputerId",Flags); api.Script=api.Computer.GetField("script",Flags); api.Proxy=api.Computer.GetField("fcProxy",Flags);
            if(new object[]{api.Initialized,api.Initialized?.GetSetMethod(true),api.PersistentsLoaded,api.ComputerId,api.Script,api.Proxy}.Any(x=>x==null)) throw new MissingMemberException("MAS readiness API differs from supported 1.3.7");
            foreach(var type in assembly.GetTypes().Where(t=>!t.IsAbstract && typeof(InternalModule).IsAssignableFrom(t)))
            {
                var start=type.GetMethod("Start",Flags|BindingFlags.DeclaredOnly);
                if(start==null) continue;
                api.PropStarts.Add(type,Declared(type,"Start"));
                var destroy=type.GetMethod("OnDestroy",Flags|BindingFlags.DeclaredOnly);
                if(destroy!=null) api.PropDestroys.Add(type,Declared(type,"OnDestroy"));
            }
            if(!api.PropStarts.Keys.Any(t=>t.Name=="MASComponent") || !api.PropStarts.Keys.Any(t=>t.Name=="MASMonitor")) throw new MissingMemberException("MAS Prop startup API is unavailable");
            return api;
        }
        public bool Ready(PartModule computer) => computer!=null && (bool)Initialized.GetValue(computer,null) && Script.GetValue(computer)!=null && Proxy.GetValue(computer)!=null;
    }
}
