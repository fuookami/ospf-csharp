#nullable enable

using Fuookami.Ospf.Utils.Parallel;
using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Math.Combinatorics;
/// <summary>
/// 排列算法 / Permutations (QuickPerm).
/// </summary>
public static class Permutations {
    /// <summary>
    /// 使用 QuickPerm 算法生成输入列表的所有全排列
    /// Generate all full permutations of input list using QuickPerm algorithm.
    /// </summary>
    public static List<List<T>> Permute<T>(
        IReadOnlyList<T> input,
        Action<List<T>>? callBack = null,
        Func<List<T>, bool>? stopped = null) {
        var a = new List<T>(input);
        int[] p = new int[input.Count];
        var perms = new List<List<T>> { new List<T>(a) };
        callBack?.Invoke(new List<T>(a));
        if (stopped?.Invoke(new List<T>(a)) == true) {
            return perms;
        }

        for (int i = 1; i < input.Count;) {
            if (p[i] < i) {
                Swap(a, i, i % 2 == 0 ? 0 : p[i]);
                var copy = new List<T>(a);
                perms.Add(copy);
                callBack?.Invoke(copy);
                if (stopped?.Invoke(copy) == true) {
                    return perms;
                }

                p[i]++;
                i = 1;
            }
            else {
                p[i] = 0;
                i++;
            }
        }
        return perms;
    }

    /// <summary>
    /// 生成指定大小的所有排列
    /// Generate all permutations of specified size.
    /// </summary>
    public static List<List<T>> Permute<T>(
        IReadOnlyList<T> input, int choose,
        Action<List<T>>? callBack = null,
        Func<List<T>, bool>? stopped = null) {
        var result = new List<List<T>>();
        foreach (List<T> permutation in PermuteSequence(input, choose)) {
            result.Add(permutation);
            callBack?.Invoke(permutation);
            if (stopped?.Invoke(permutation) == true) {
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// 排列数 P(n, choose) / Permutation count.
    /// </summary>
    public static long PermuteCount(int n, int choose) {
        if (choose < 0 || choose > n) {
            return 0L;
        }

        long value = 1L;
        for (int i = 0; i < choose; i++) {
            value *= (n - i);
        }

        return value;
    }

    /// <summary>
    /// 惰性序列生成所有全排列 / Lazy iterator for full permutations.
    /// </summary>
    public static IEnumerable<List<T>> PermuteSequence<T>(IReadOnlyList<T> input)
        => PermuteSequence(input, input.Count);

    /// <summary>
    /// 惰性序列生成指定大小的排列 / Lazy iterator for permutations of specified size.
    /// </summary>
    public static IEnumerable<List<T>> PermuteSequence<T>(IReadOnlyList<T> input, int choose) {
        if (choose < 0 || choose > input.Count) {
            yield break;
        }

        if (choose == 0) { yield return new List<T>(); yield break; }

        bool[] used = new bool[input.Count];
        var path = new List<T>(choose);

        foreach (List<T> perm in DfsPermute(input, choose, used, path)) {
            yield return perm;
        }
    }

    private static IEnumerable<List<T>> DfsPermute<T>(
        IReadOnlyList<T> input, int choose, bool[] used, List<T> path) {
        if (path.Count == choose) {
            yield return new List<T>(path);
            yield break;
        }
        for (int i = 0; i < input.Count; i++) {
            if (used[i]) {
                continue;
            }

            used[i] = true;
            path.Add(input[i]);
            foreach (List<T> perm in DfsPermute(input, choose, used, path)) {
                yield return perm;
            }

            path.RemoveAt(path.Count - 1);
            used[i] = false;
        }
    }

    /// <summary>
    /// 异步生成排列，通过通道返回 / Async permutation generation via channel.
    /// </summary>
    public static ChannelGuard<List<T>> PermuteAsync<T>(IReadOnlyList<T> input) {
        var ch = Channel.CreateUnbounded<List<T>>();
        CombinatoricsAsyncScope.Factory.StartNew(async () => {
            try {
                var a = new List<T>(input);
                int[] p = new int[input.Count];
                await ch.Writer.WriteAsync(new List<T>(a));

                for (int i = 1; i < input.Count;) {
                    if (p[i] < i) {
                        Swap(a, i, i % 2 == 0 ? 0 : p[i]);
                        await ch.Writer.WriteAsync(new List<T>(a));
                        p[i]++;
                        i = 1;
                    }
                    else {
                        p[i] = 0;
                        i++;
                    }
                }
            }
            catch (Exception) { /* log debug */ }
            finally { ch.Writer.Complete(); }
        }, TaskCreationOptions.DenyChildAttach);
        return new ChannelGuard<List<T>>(ch);
    }

    private static void Swap<T>(IList<T> a, int i, int j) => (a[i], a[j]) = (a[j], a[i]);
}
