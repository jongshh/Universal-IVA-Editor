using System;
using UnityEngine;
namespace UniversalIVAEditor
{
    public static class Log
    {
                                                                                                    
        public static bool DebugEnabled { get; set; }
        public static void Trace(string text) { if(DebugEnabled) Debug.Log("[UniversalIVAEditor][DEBUG] "+text); }
        public static void Info(string text) => Debug.Log("[UniversalIVAEditor] "+text);
        public static void Error(string text,Exception e=null) => Debug.LogError("[UniversalIVAEditor] "+text+(e==null ? "" : "\n"+e));
    }
}
