#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Utils.Functional;
/// <summary>Map 扩展方法 / Map extension methods.</summary>
public static class MapExtensions {
    /// <summary>获取或放入 / Get or put.</summary>
    public static V GetOrPut<K, V>(this IDictionary<K, V> dict, K key, Func<V> defaultValue)
        where K : notnull {
        if (!dict.TryGetValue(key, out V? value)) {
            value = defaultValue();
            dict[key] = value;
        }
        return value;
    }

    /// <summary>安全获取 / Safe get.</summary>
    public static V? GetOrDefault<K, V>(this IReadOnlyDictionary<K, V> dict, K key)
        where K : notnull =>
        dict.TryGetValue(key, out V? value) ? value : default;
}
