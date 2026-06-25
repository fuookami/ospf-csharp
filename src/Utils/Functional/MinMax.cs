#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Utils.Functional
{
    /// <summary>MinMax 辅助方法 / MinMax helper methods.</summary>
    public static class MinMaxExtensions
    {
        /// <summary>安全最小值（空集合返回 null）/ Safe min (returns null for empty).</summary>
        public static T? MinOrNull<T>(this IEnumerable<T> source) where T : IComparable<T>
        {
            using var enumerator = source.GetEnumerator();
            if (!enumerator.MoveNext()) return default;
            var min = enumerator.Current;
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.CompareTo(min) < 0) min = enumerator.Current;
            }
            return min;
        }

        /// <summary>安全最大值（空集合返回 null）/ Safe max (returns null for empty).</summary>
        public static T? MaxOrNull<T>(this IEnumerable<T> source) where T : IComparable<T>
        {
            using var enumerator = source.GetEnumerator();
            if (!enumerator.MoveNext()) return default;
            var max = enumerator.Current;
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.CompareTo(max) > 0) max = enumerator.Current;
            }
            return max;
        }

        /// <summary>按键安全最小值 / Safe min by key.</summary>
        public static T? MinByOrNull<T, K>(this IEnumerable<T> source, Func<T, K> keySelector)
            where K : IComparable<K>
        {
            using var enumerator = source.GetEnumerator();
            if (!enumerator.MoveNext()) return default;
            var min = enumerator.Current;
            var minKey = keySelector(min);
            while (enumerator.MoveNext())
            {
                var key = keySelector(enumerator.Current);
                if (key.CompareTo(minKey) < 0) { min = enumerator.Current; minKey = key; }
            }
            return min;
        }

        /// <summary>按键安全最大值 / Safe max by key.</summary>
        public static T? MaxByOrNull<T, K>(this IEnumerable<T> source, Func<T, K> keySelector)
            where K : IComparable<K>
        {
            using var enumerator = source.GetEnumerator();
            if (!enumerator.MoveNext()) return default;
            var max = enumerator.Current;
            var maxKey = keySelector(max);
            while (enumerator.MoveNext())
            {
                var key = keySelector(enumerator.Current);
                if (key.CompareTo(maxKey) > 0) { max = enumerator.Current; maxKey = key; }
            }
            return max;
        }
    }
}
