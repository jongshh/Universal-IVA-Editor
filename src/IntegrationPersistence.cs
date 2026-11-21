using System;
using System.Linq;
namespace UniversalIVAEditor
{
    public static class IntegrationPersistence
    {
        public static int[] TransientIndices(string[] saved,string[] expected,bool[] owned)
        {
            if(saved.Length!=expected.Length || owned.Length!=saved.Length || !saved.SequenceEqual(expected,StringComparer.Ordinal)) return new int[0];
            return Enumerable.Range(0,saved.Length).Where(i=>owned[i]).Reverse().ToArray();
        }
    }
}

