using System;
using System.Collections.Generic;
using System.Linq;

namespace DoctorVanGogh.ModSwitch {
    internal static class LinqExtensions {
        internal static IEnumerable<TResult> FullOuterGroupJoin<TA, TB, TKey, TResult>(
            this IEnumerable<TA> a,
            IEnumerable<TB> b,
            Func<TA, TKey> selectKeyA,
            Func<TB, TKey> selectKeyB,
            Func<IEnumerable<TA>, IEnumerable<TB>, TKey, TResult> projection,
            IEqualityComparer<TKey> cmp = null) {
            cmp = cmp ?? EqualityComparer<TKey>.Default;
            ILookup<TKey, TA> alookup = a.ToLookup(selectKeyA, cmp);
            ILookup<TKey, TB> blookup = b.ToLookup(selectKeyB, cmp);

            HashSet<TKey> keys = new HashSet<TKey>(alookup.Select(p => p.Key), cmp);
            keys.UnionWith(blookup.Select(p => p.Key));

            IEnumerable<TResult> join = from key in keys
                                        let xa = alookup[key]
                                        let xb = blookup[key]
                                        select projection(xa, xb, key);

            return join;
        }

        internal static IEnumerable<TResult> FullOuterJoin<TA, TB, TKey, TResult>(
            this IEnumerable<TA> a,
            IEnumerable<TB> b,
            Func<TA, TKey> selectKeyA,
            Func<TB, TKey> selectKeyB,
            Func<TA, TB, TKey, TResult> projection,
            TA defaultA = default(TA),
            TB defaultB = default(TB),
            IEqualityComparer<TKey> cmp = null) {
            cmp = cmp ?? EqualityComparer<TKey>.Default;
            ILookup<TKey, TA> alookup = a.ToLookup(selectKeyA, cmp);
            ILookup<TKey, TB> blookup = b.ToLookup(selectKeyB, cmp);

            HashSet<TKey> keys = new HashSet<TKey>(alookup.Select(p => p.Key), cmp);
            keys.UnionWith(blookup.Select(p => p.Key));

            IEnumerable<TResult> join = from key in keys
                                        from xa in alookup[key].DefaultIfEmpty(defaultA)
                                        from xb in blookup[key].DefaultIfEmpty(defaultB)
                                        select projection(xa, xb, key);

            return join;
        }
    }
}