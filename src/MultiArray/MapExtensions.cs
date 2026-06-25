#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.MultiArray
{
    /// <summary>
    /// Map/Dictionary 扩展方法
    /// Map/Dictionary extension methods
    /// </summary>
    public static class MapArrayExtensions
    {
        /// <summary>使用 All 索引获取所有值 / Get all values using All index</summary>
        public static IEnumerable<T> GetAllValues<K, T>(this IReadOnlyDictionary<K, T> map, DummyIndex.All allIndex)
            where K : notnull
        {
            return map.Values;
        }

        /// <summary>通过键和线性索引从 MultiArray 值获取元素 / Get element from MultiArray value by key and linear index</summary>
        public static T? GetArrayElement<K, T, S>(this IReadOnlyDictionary<K, MultiArray<T, S>> map, K key, int i)
            where K : notnull
            where T : notnull
            where S : IShape
        {
            return map.TryGetValue(key, out var arr) ? arr[i] : default;
        }

        /// <summary>通过键和向量索引从 MultiArray 值获取元素 / Get element by key and vector index</summary>
        public static T? GetArrayElement<K, T, S>(this IReadOnlyDictionary<K, MultiArray<T, S>> map, K key, int[] v)
            where K : notnull
            where T : notnull
            where S : IShape
        {
            return map.TryGetValue(key, out var arr) ? arr[v] : default;
        }
    }
}
