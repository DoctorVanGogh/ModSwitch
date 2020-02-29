using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Verse;

namespace DoctorVanGogh.ModSwitch {
    public static class ModConfigUtil {

        public static string GetConfigFilename(string foldername, string modClassName) => $"Mod_{foldername}_{modClassName}.xml";
        public static string GetConfigFilename(this ModMetaData md, string modClassName) => GetConfigFilename(md.FolderName, modClassName);


        public static (IEnumerable<TResult> Resolved, IEnumerable<TResult> Unresolved) TryResolveModsList<T, TKey, TResult>(IEnumerable<T> candidates, 
                                                                                                                            Func<ModMetaData, TKey> installedKeyFactory,
                                                                                                                            Func<T, TKey> candidateKeyFactory,
                                                                                                                            Func<ModMetaData, T, TResult> resultFactory) {

            var tmp = candidates.FullOuterJoin(
                ModLister.AllInstalledMods,
                candidateKeyFactory,
                installedKeyFactory,
                (c, mmd, key) => new {
                                         Key = key,
                                         ModMetadata = mmd,
                                         Candidate = c
                                     });

            Dictionary<bool, TResult[]> partition = tmp.GroupBy(t => t.ModMetadata == null)
                                                       .ToDictionary(
                                                           g => g.Key,
                                                           g => g.Select(t => resultFactory(t.ModMetadata, t.Candidate)).ToArray());

            return (Resolved: partition[false], Unresolved: partition[true]);
        }


        public static (IEnumerable<TResolved> Resolved, IEnumerable<TUnresolved> Unresolved) TryResolveModsList<T, TKey, TResolved, TUnresolved>(
            IEnumerable<T> candidates,
            Func<ModMetaData, TKey> installedKeyFactory,
            Func<T, TKey> candidateKeyFactory,
            Func<ModMetaData, T, TResolved> resolvedProjection,
            Func<ModMetaData, T, TUnresolved> unresolvedProjection) {

            var tmp = candidates.FullOuterJoin(
                ModLister.AllInstalledMods,
                candidateKeyFactory,
                installedKeyFactory,
                (c, mmd, key) => new {
                                         Key = key,
                                         ModMetadata = mmd,
                                         Candidate = c
                                     });

            var partition = tmp.GroupBy(t => t.ModMetadata == null)
                               .ToDictionary(
                                   g => g.Key);


            return (
                Resolved: partition[false].Select(t => resolvedProjection(t.ModMetadata, t.Candidate)).ToArray(),
                Unresolved: partition[true].Select(t => unresolvedProjection(t.ModMetadata, t.Candidate)).ToArray()
                );
        }
    }
}
