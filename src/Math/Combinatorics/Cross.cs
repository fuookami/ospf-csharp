#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Parallel;

namespace Fuookami.Ospf.Math.Combinatorics
{
    /// <summary>
    /// 笛卡尔积 / Cartesian product (cross).
    /// </summary>
    public static class Cross
    {
        /// <summary>
        /// 计算多个集合的笛卡尔积 / Calculate Cartesian product of multiple sets.
        /// </summary>
        public static List<List<T>> CrossProduct<T>(
            IReadOnlyList<IReadOnlyList<T>> input,
            Action<List<T>>? callBack = null,
            Func<List<T>, bool>? stopped = null)
        {
            var result = new List<List<T>>();
            if (input.Count == 0) return result;

            var n = input.Count;
            var indices = new int[n];
            while (true)
            {
                var row = new List<T>(n);
                for (var i = 0; i < n; i++)
                    row.Add(input[i][indices[i]]);

                result.Add(row);
                callBack?.Invoke(row);
                if (stopped?.Invoke(row) == true) break;

                var k = n - 1;
                while (k >= 0 && indices[k] == input[k].Count - 1) k--;
                if (k < 0) break;
                indices[k]++;
                for (var j = k + 1; j < n; j++)
                    indices[j] = 0;
            }
            return result;
        }

        /// <summary>
        /// 笛卡尔积元素总数 / Total count of Cartesian product.
        /// </summary>
        public static long CrossCount<T>(IReadOnlyList<IReadOnlyList<T>> input)
        {
            if (input.Count == 0) return 0L;
            long value = 1L;
            foreach (var s in input)
            {
                if (s.Count == 0) return 0L;
                value *= s.Count;
            }
            return value;
        }

        /// <summary>
        /// 惰性序列生成笛卡尔积 / Lazy iterator for Cartesian product.
        /// </summary>
        public static IEnumerable<List<T>> CrossSequence<T>(IReadOnlyList<IReadOnlyList<T>> input)
        {
            if (input.Count == 0) yield break;
            foreach (var s in input)
                if (s.Count == 0) yield break;

            var n = input.Count;
            var indices = new int[n];
            while (true)
            {
                var row = new List<T>(n);
                for (var i = 0; i < n; i++)
                    row.Add(input[i][indices[i]]);
                yield return row;

                var k = n - 1;
                while (k >= 0 && indices[k] == input[k].Count - 1) k--;
                if (k < 0) break;
                indices[k]++;
                for (var j = k + 1; j < n; j++)
                    indices[j] = 0;
            }
        }

        /// <summary>
        /// 两个集合的笛卡尔积 / Cartesian product of two sets.
        /// </summary>
        public static List<(A, B)> Cross2<A, B>(IReadOnlyList<A> lhs, IReadOnlyList<B> rhs)
        {
            var result = new List<(A, B)>(lhs.Count * rhs.Count);
            foreach (var l in lhs)
                foreach (var r in rhs)
                    result.Add((l, r));
            return result;
        }

        /// <summary>
        /// 惰性序列两个集合的笛卡尔积 / Lazy Cartesian product of two sets.
        /// </summary>
        public static IEnumerable<(A, B)> Cross2Sequence<A, B>(IReadOnlyList<A> lhs, IReadOnlyList<B> rhs)
        {
            foreach (var l in lhs)
                foreach (var r in rhs)
                    yield return (l, r);
        }

        /// <summary>
        /// 三个集合的笛卡尔积 / Cartesian product of three sets.
        /// </summary>
        public static List<(A, B, C)> Cross3<A, B, C>(IReadOnlyList<A> a, IReadOnlyList<B> b, IReadOnlyList<C> c)
        {
            var result = new List<(A, B, C)>(a.Count * b.Count * c.Count);
            foreach (var x in a)
                foreach (var y in b)
                    foreach (var z in c)
                        result.Add((x, y, z));
            return result;
        }

        /// <summary>
        /// 惰性序列三个集合的笛卡尔积 / Lazy Cartesian product of three sets.
        /// </summary>
        public static IEnumerable<(A, B, C)> Cross3Sequence<A, B, C>(IReadOnlyList<A> a, IReadOnlyList<B> b, IReadOnlyList<C> c)
        {
            foreach (var x in a)
                foreach (var y in b)
                    foreach (var z in c)
                        yield return (x, y, z);
        }

        /// <summary>
        /// 异步生成笛卡尔积 / Async Cartesian product via channel.
        /// </summary>
        public static ChannelGuard<List<T>> CrossAsync<T>(IReadOnlyList<IReadOnlyList<T>> input)
        {
            var ch = Channel.CreateUnbounded<List<T>>();
            CombinatoricsAsyncScope.Factory.StartNew(async () =>
            {
                try
                {
                    var n = input.Count;
                    var indices = new int[n];
                    while (true)
                    {
                        var row = new List<T>(n);
                        for (var i = 0; i < n; i++)
                            row.Add(input[i][indices[i]]);

                        await ch.Writer.WriteAsync(row);

                        var k = n - 1;
                        while (k >= 0 && indices[k] == input[k].Count - 1) k--;
                        if (k < 0) break;
                        indices[k]++;
                        for (var j = k + 1; j < n; j++)
                            indices[j] = 0;
                    }
                }
                catch (Exception) { /* log debug */ }
                finally { ch.Writer.Complete(); }
            }, TaskCreationOptions.DenyChildAttach);
            return new ChannelGuard<List<T>>(ch);
        }
    }
}
