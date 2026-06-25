#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Utils.Functional
{
    /// <summary>Collection 辅助方法 / Collection helper methods.</summary>
    public static class CollectionExtensions
    {
        /// <summary>随机打乱 / Shuffle.</summary>
        public static List<T> Shuffle<T>(this List<T> list, Random? random = null)
        {
            random ??= Random.Shared;
            var result = new List<T>(list);
            for (int i = result.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (result[i], result[j]) = (result[j], result[i]);
            }
            return result;
        }

        /// <summary>收集枚举器 / Collect enumerator.</summary>
        public static List<T> Collect<T>(this IEnumerator<T> enumerator)
        {
            var list = new List<T>();
            while (enumerator.MoveNext()) list.Add(enumerator.Current);
            return list;
        }
    }
}
