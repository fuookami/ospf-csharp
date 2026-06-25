#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Ordinary
{
    /// <summary>
    /// 最小最大值 / MinMax Functions.
    /// <para>提供计算可比较类型的最小值、最大值和同时返回两者的功能。</para>
    /// <para>Provides min, max, and simultaneous min-max for comparable types.</para>
    /// </summary>
    public static class MinMax
    {
        /// <summary>返回两个值中的较小者 / Return the smaller of two values.</summary>
        public static T Min<T>(T lhs, T rhs) where T : IOrd<T>
            => lhs.PartialOrd(rhs) is Order.Less ? lhs : rhs;

        /// <summary>返回多个值中的最小值 / Return the minimum of multiple values.</summary>
        public static T Min<T>(T lhs, params T[] rest) where T : IOrd<T>
        {
            var min = lhs;
            foreach (var e in rest) if (e.PartialOrd(min) is Order.Less or Order.Equal) min = e;
            return min;
        }

        /// <summary>返回两个值中的较大者 / Return the larger of two values.</summary>
        public static T Max<T>(T lhs, T rhs) where T : IOrd<T>
            => lhs.PartialOrd(rhs) is Order.Greater ? lhs : rhs;

        /// <summary>返回多个值中的最大值 / Return the maximum of multiple values.</summary>
        public static T Max<T>(T lhs, params T[] rest) where T : IOrd<T>
        {
            var max = lhs;
            foreach (var e in rest) if (e.PartialOrd(max) is Order.Greater or Order.Equal) max = e;
            return max;
        }

        /// <summary>同时返回两个值的最小值和最大值 / Return both min and max of two values.</summary>
        public static (T Min, T Max) MinMaxValue<T>(T lhs, T rhs) where T : IOrd<T> => (Min(lhs, rhs), Max(lhs, rhs));

        /// <summary>同时返回多个值的最小值和最大值 / Return both min and max of multiple values.</summary>
        public static (T Min, T Max) MinMaxValue<T>(T lhs, params T[] rest) where T : IOrd<T>
        {
            var min = lhs; var max = lhs;
            foreach (var e in rest)
            {
                if (e.PartialOrd(min) is Order.Less) min = e;
                if (e.PartialOrd(max) is Order.Greater) max = e;
            }
            return (min, max);
        }

        /// <summary>返回集合的最小值和最大值，空集合返回 Failed / Min and max of collection; Failed for empty.</summary>
        public static Result<(T Min, T Max), ErrorCode, Error<ErrorCode>> MinMaxSafe<T>(this IEnumerable<T> source) where T : IOrd<T>
        {
            using var e = source.GetEnumerator();
            if (!e.MoveNext())
                return new Failed<(T, T), ErrorCode, Error<ErrorCode>>(ErrorCode.DataEmpty,
                    "无法对空集合计算 minMax / Cannot compute minMax of an empty collection.");
            var min = e.Current; var max = min;
            while (e.MoveNext()) { var v = e.Current; if (v.PartialOrd(min) is Order.Less) min = v; if (v.PartialOrd(max) is Order.Greater) max = v; }
            return Results.Ok((min, max));
        }

        /// <summary>返回集合的最小值和最大值 / Min and max of collection.</summary>
        public static Result<(T Min, T Max), ErrorCode, Error<ErrorCode>> MinMaxOf<T>(this IEnumerable<T> source) where T : IOrd<T> => source.MinMaxSafe();

        /// <summary>返回集合的最小值和最大值，空集合返回 null / Min and max; null for empty.</summary>
        public static (T Min, T Max)? MinMaxOrNull<T>(this IEnumerable<T> source) where T : IOrd<T>
        {
            using var e = source.GetEnumerator();
            if (!e.MoveNext()) return null;
            var min = e.Current; var max = min;
            while (e.MoveNext()) { var v = e.Current; if (v.PartialOrd(min) is Order.Less) min = v; if (v.PartialOrd(max) is Order.Greater) max = v; }
            return (min, max);
        }

        /// <summary>通过提取器返回集合中最小和最大元素 / Return min and max elements via extractor.</summary>
        public static (U MinE, U MaxE) MinMaxBy<T, U>(this IEnumerable<U> source, Func<U, T> extractor) where T : IOrd<T>
        {
            using var iter = source.GetEnumerator();
            if (!iter.MoveNext()) throw new InvalidOperationException("Cannot compute minMaxBy of an empty collection.");
            var minE = iter.Current; var maxE = minE;
            var min = extractor(minE); var max = min;
            while (iter.MoveNext())
            {
                var e = iter.Current; var v = extractor(e);
                if (v.PartialOrd(min) is Order.Less or Order.Equal) { minE = e; min = v; }
                if (v.PartialOrd(max) is Order.Greater or Order.Equal) { maxE = e; max = v; }
            }
            return (minE, maxE);
        }

        /// <summary>通过提取器返回集合中最小和最大元素，空集合返回 null / MinMaxBy; null for empty.</summary>
        public static (U MinE, U MaxE)? MinMaxByOrNull<T, U>(this IEnumerable<U> source, Func<U, T> extractor) where T : IOrd<T>
        {
            using var iter = source.GetEnumerator();
            if (!iter.MoveNext()) return null;
            var minE = iter.Current; var maxE = minE;
            var min = extractor(minE); var max = min;
            while (iter.MoveNext())
            {
                var e = iter.Current; var v = extractor(e);
                if (v.PartialOrd(min) is Order.Less or Order.Equal) { minE = e; min = v; }
                if (v.PartialOrd(max) is Order.Greater or Order.Equal) { maxE = e; max = v; }
            }
            return (minE, maxE);
        }

        /// <summary>通过提取器返回集合中最小和最大提取值 / Min and max extracted values.</summary>
        public static (T Min, T Max) MinMaxExtracted<T, U>(this IEnumerable<U> source, Func<U, T> extractor) where T : IOrd<T>
        {
            using var iter = source.GetEnumerator();
            if (!iter.MoveNext()) throw new InvalidOperationException("Cannot compute minMaxOf of an empty collection.");
            var min = extractor(iter.Current); var max = min;
            while (iter.MoveNext())
            {
                var v = extractor(iter.Current);
                if (v.PartialOrd(min) is Order.Less) min = v;
                if (v.PartialOrd(max) is Order.Greater) max = v;
            }
            return (min, max);
        }

        /// <summary>通过提取器返回集合中最小和最大提取值，空集合返回 null / MinMaxExtracted; null for empty.</summary>
        public static (T Min, T Max)? MinMaxExtractedOrNull<T, U>(this IEnumerable<U> source, Func<U, T> extractor) where T : IOrd<T>
        {
            using var iter = source.GetEnumerator();
            if (!iter.MoveNext()) return null;
            var min = extractor(iter.Current); var max = min;
            while (iter.MoveNext())
            {
                var v = extractor(iter.Current);
                if (v.PartialOrd(min) is Order.Less) min = v;
                if (v.PartialOrd(max) is Order.Greater) max = v;
            }
            return (min, max);
        }

        /// <summary>使用自定义比较器返回集合的最小值和最大值 / Min and max using custom comparer.</summary>
        public static (T Min, T Max) MinMaxWith<T>(this IEnumerable<T> source, IComparer<T> comparer)
        {
            using var iter = source.GetEnumerator();
            if (!iter.MoveNext()) throw new InvalidOperationException("Cannot compute minMaxWith of an empty collection.");
            var min = iter.Current; var max = min;
            while (iter.MoveNext())
            {
                var v = iter.Current;
                if (comparer.Compare(v, min) < 0) min = v;
                if (comparer.Compare(v, max) > 0) max = v;
            }
            return (min, max);
        }

        /// <summary>使用自定义比较器返回集合的最小值和最大值，空集合返回 null / MinMaxWith; null for empty.</summary>
        public static (T Min, T Max)? MinMaxWithOrNull<T>(this IEnumerable<T> source, IComparer<T> comparer)
        {
            using var iter = source.GetEnumerator();
            if (!iter.MoveNext()) return null;
            var min = iter.Current; var max = min;
            while (iter.MoveNext())
            {
                var v = iter.Current;
                if (comparer.Compare(v, min) < 0) min = v;
                if (comparer.Compare(v, max) > 0) max = v;
            }
            return (min, max);
        }
    }
}
