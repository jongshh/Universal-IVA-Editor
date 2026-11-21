using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalIVAEditor
{
                                                                                              
    public static class PropMapping
    {
        public sealed class Candidate { public string FieldName,ObjectName,Path; }
        public sealed class Result { public readonly Dictionary<int,int> Matches=new Dictionary<int,int>(); public readonly List<string> Warnings=new List<string>(); }
        public static string Identity(Candidate candidate,ISet<string> names)
        {
            string objectName=candidate.ObjectName ?? "";
            if(objectName.EndsWith("(Clone)",StringComparison.Ordinal)) objectName=objectName.Substring(0,objectName.Length-7).TrimEnd();
            bool field=names.Contains(candidate.FieldName ?? ""), obj=names.Contains(objectName);
            if(field && obj && candidate.FieldName!=objectName) return null;
            return obj?objectName:field?candidate.FieldName:null;
        }
        public static Result Resolve(string[] source,Candidate[] candidates)
        {
            var result=new Result(); var names=new HashSet<string>(source,StringComparer.Ordinal);
            var identities=candidates.Select(c=>Identity(c,names)).ToArray();
            foreach(var name in source.Distinct())
            {
                var indices=Enumerable.Range(0,source.Length).Where(i=>source[i]==name).ToArray();
                var loaded=Enumerable.Range(0,candidates.Length).Where(i=>identities[i]==name).ToArray();
                if(indices.Length!=loaded.Length || loaded.Select(i=>candidates[i].Path).Distinct().Count()!=loaded.Length)
                {
                    result.Warnings.Add(name+": source="+indices.Length+", loaded="+loaded.Length+"; original geometry retained, individual editing unavailable"); continue;
                }
                for(int i=0;i<indices.Length;i++) result.Matches.Add(indices[i],loaded[i]);
            }
            return result;
        }
    }
}

