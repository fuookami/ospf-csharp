#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Utils.Functional;
/// <summary>MinMax 辅助方法 / MinMax helper methods.</summary>
public static class MinMaxExtensions {
    /// <summary>单遍计算最小值和最大值 / Compute minimum and maximum in one pass.</summary>
    public static (T Min, T Max) MinMax<T>(this IEnumerable<T> source)
        where T : IComparable<T> {
        using IEnumerator<T> enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) {
            throw new InvalidOperationException("Sequence contains no elements.");
        }

        T min = enumerator.Current;
        T max = min;
        while (enumerator.MoveNext()) {
            T value = enumerator.Current;
            if (value.CompareTo(min) < 0) {
                min = value;
            }
            if (value.CompareTo(max) > 0) {
                max = value;
            }
        }
        return (min, max);
    }

    /// <summary>单遍计算最小值和最大值（空集合返回 null）/ Compute nullable minimum and maximum in one pass.</summary>
    public static (T Min, T Max)? MinMaxOrNull<T>(this IEnumerable<T> source)
        where T : IComparable<T> {
        using IEnumerator<T> enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) {
            return null;
        }

        T min = enumerator.Current;
        T max = min;
        while (enumerator.MoveNext()) {
            T value = enumerator.Current;
            if (value.CompareTo(min) < 0) {
                min = value;
            }
            if (value.CompareTo(max) > 0) {
                max = value;
            }
        }
        return (min, max);
    }

    /// <summary>按键单遍计算最小值和最大值 / Compute minimum and maximum by key in one pass.</summary>
    public static (T Min, T Max)? MinMaxByOrNull<T, K>(
        this IEnumerable<T> source,
        Func<T, K> keySelector)
        where K : IComparable<K> {
        using IEnumerator<T> enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) {
            return null;
        }

        T min = enumerator.Current;
        T max = min;
        K minKey = keySelector(min);
        K maxKey = minKey;
        while (enumerator.MoveNext()) {
            T value = enumerator.Current;
            K key = keySelector(value);
            if (key.CompareTo(minKey) < 0) {
                min = value;
                minKey = key;
            }
            if (key.CompareTo(maxKey) > 0) {
                max = value;
                maxKey = key;
            }
        }
        return (min, max);
    }

    /// <summary>安全最小值（空集合返回 null）/ Safe min (returns null for empty).</summary>
    public static T? MinOrNull<T>(this IEnumerable<T> source) where T : IComparable<T> {
        using IEnumerator<T> enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) {
            return default;
        }

        T min = enumerator.Current;
        while (enumerator.MoveNext()) {
            if (enumerator.Current.CompareTo(min) < 0) {
                min = enumerator.Current;
            }
        }
        return min;
    }

    /// <summary>安全最大值（空集合返回 null）/ Safe max (returns null for empty).</summary>
    public static T? MaxOrNull<T>(this IEnumerable<T> source) where T : IComparable<T> {
        using IEnumerator<T> enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) {
            return default;
        }

        T max = enumerator.Current;
        while (enumerator.MoveNext()) {
            if (enumerator.Current.CompareTo(max) > 0) {
                max = enumerator.Current;
            }
        }
        return max;
    }

    /// <summary>按键安全最小值 / Safe min by key.</summary>
    public static T? MinByOrNull<T, K>(this IEnumerable<T> source, Func<T, K> keySelector)
        where K : IComparable<K> {
        using IEnumerator<T> enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) {
            return default;
        }

        T? min = enumerator.Current;
        K minKey = keySelector(min);
        while (enumerator.MoveNext()) {
            K key = keySelector(enumerator.Current);
            if (key.CompareTo(minKey) < 0) { min = enumerator.Current; minKey = key; }
        }
        return min;
    }

    /// <summary>按键安全最大值 / Safe max by key.</summary>
    public static T? MaxByOrNull<T, K>(this IEnumerable<T> source, Func<T, K> keySelector)
        where K : IComparable<K> {
        using IEnumerator<T> enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) {
            return default;
        }

        T? max = enumerator.Current;
        K maxKey = keySelector(max);
        while (enumerator.MoveNext()) {
            K key = keySelector(enumerator.Current);
            if (key.CompareTo(maxKey) > 0) { max = enumerator.Current; maxKey = key; }
        }
        return max;
    }
}
