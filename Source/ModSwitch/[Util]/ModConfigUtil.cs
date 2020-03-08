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


        public static (TResult[] Resolved, TResult[] Unresolved) TryResolveModsList<T, TKey, TResult>(
            IEnumerable<T> candidates,
            Func<ModMetaData, TKey> installedKeyFactory,
            Func<T, TKey> candidateKeyFactory,
            Func<ModMetaData, T, TResult> resultFactory) => TryResolveModsList(candidates, 
                                                                               installedKeyFactory, 
                                                                               candidateKeyFactory, 
                                                                               resultFactory, 
                                                                               resultFactory);


        public static (TResolved[] Resolved, TUnresolved[] Unresolved) TryResolveModsList<T, TKey, TResolved, TUnresolved>(
            IEnumerable<T> candidates,
            Func<ModMetaData, TKey> installedKeyFactory,
            Func<T, TKey> candidateKeyFactory,
            Func<ModMetaData, T, TResolved> resolvedProjection,
            Func<ModMetaData, T, TUnresolved> unresolvedProjection) {

            var tmp = LetOuterJoin(candidates, installedKeyFactory, candidateKeyFactory);


            var partition = tmp.GroupBy(t => t.MetaData == null)
                               .ToDictionary(g => g.Key);

            partition.TryGetValue(false, out var resolved);
            partition.TryGetValue(true, out var unresolved);

            return (
                Resolved: resolved?.Select(t => resolvedProjection(t.MetaData, t.Candidate)).ToArray() ?? new TResolved[0],
                Unresolved: unresolved?.Select(t => unresolvedProjection(t.MetaData, t.Candidate)).ToArray() ?? new TUnresolved[0]
            );
        }

        public static IEnumerable<(TKey Key, ModMetaData MetaData, T Candidate)> LetOuterJoin<T, TKey>(
            IEnumerable<T> candidates,
            Func<ModMetaData, TKey> installedKeyFactory,
            Func<T, TKey> candidateKeyFactory) {
            var tmp = candidates.FullOuterJoin(
                                    ModLister.AllInstalledMods,
                                    candidateKeyFactory,
                                    installedKeyFactory,
                                    (c, mmd, key) => (Key: key, MetaData: mmd, Candidate: c))
                                .Where(t => t.Candidate != null); // we dont care about installed mods not in the candidate set
            return tmp;
        }
    }
}
