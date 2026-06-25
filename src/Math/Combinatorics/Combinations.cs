#nullable enable

using Fuookami.Ospf.Utils.Parallel;
using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Math.Combinatorics;
/// <summary>
/// 组合算法 / Combinations.
/// </summary>
public static class Combinations {
    /// <summary>
    /// 生成输入列表的所有子集组合
    /// Generate all subset combinations of the input list.
    /// </summary>
    public static List<List<T>> Combine<T>(
        IReadOnlyList<T> input,
        Action<List<T>>? callBack = null,
        Func<List<T>, bool>? stopped = null) {
        var result = new List<List<T>>();
        int total = 1 << input.Count;
        for (int i = 1; i < total; i++) {
            var combination = new List<T>();
            for (int j = 0; j < input.Count; j++) {
                if ((i & (1 << j)) != 0) {
                    combination.Add(input[j]);
                }
            }
            result.Add(combination);
            callBack?.Invoke(combination);
            if (stopped?.Invoke(combination) == true) {
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// 生成指定大小的所有组合
    /// Generate all combinations of specified size.
    /// </summary>
    public static List<List<T>> Combine<T>(
        IReadOnlyList<T> input, int choose,
        Action<List<T>>? callBack = null,
        Func<List<T>, bool>? stopped = null) {
        var result = new List<List<T>>();
        foreach (List<T> combination in CombineSequence(input, choose)) {
            result.Add(combination);
            callBack?.Invoke(combination);
            if (stopped?.Invoke(combination) == true) {
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// 组合数 C(n, choose) / Combination count.
    /// </summary>
    public static long CombineCount(int n, int choose) {
        if (choose < 0 || choose > n) {
            return 0L;
        }

        if (choose == 0 || choose == n) {
            return 1L;
        }

        int k = global::System.Math.Min(choose, n - choose);
        long value = 1L;
        for (int i = 1; i <= k; i++) {
            value = value * (n - k + i) / i;
        }

        return value;
    }

    /// <summary>
    /// 惰性序列生成所有子集组合 / Lazy iterator for all subset combinations.
    /// </summary>
    public static IEnumerable<List<T>> CombineSequence<T>(IReadOnlyList<T> input) {
        for (int k = 1; k <= input.Count; k++) {
            foreach (List<T> c in CombineSequence(input, k)) {
                yield return c;
            }
        }
    }

    /// <summary>
    /// 惰性序列生成指定大小的组合 / Lazy iterator for combinations of specified size.
    /// </summary>
    public static IEnumerable<List<T>> CombineSequence<T>(IReadOnlyList<T> input, int choose) {
        if (choose < 0 || choose > input.Count) {
            yield break;
        }

        if (choose == 0) { yield return new List<T>(); yield break; }

        int[] indices = new int[choose];
        for (int i = 0; i < choose; i++) {
            indices[i] = i;
        }

        while (true) {
            yield return new List<T>(Array.ConvertAll(indices, idx => input[idx]));

            int i2 = choose - 1;
            while (i2 >= 0 && indices[i2] == input.Count - choose + i2) {
                i2--;
            }

            if (i2 < 0) {
                break;
            }

            indices[i2]++;
            for (int j = i2 + 1; j < choose; j++) {
                indices[j] = indices[j - 1] + 1;
            }
        }
    }

    /// <summary>
    /// 异步生成组合，通过通道返回 / Async combination generation via channel.
    /// </summary>
    public static ChannelGuard<List<T>> CombineAsync<T>(IReadOnlyList<T> input) {
        var ch = Channel.CreateUnbounded<List<T>>();
        CombinatoricsAsyncScope.Factory.StartNew(async () => {
            try {
                int total = 1 << input.Count;
                for (int i = 1; i < total; i++) {
                    var combination = new List<T>();
                    for (int j = 0; j < input.Count; j++) {
                        if ((i & (1 << j)) != 0) {
                            combination.Add(input[j]);
                        }
                    }
                    await ch.Writer.WriteAsync(combination);
                }
            }
            catch (Exception) { /* log debug */ }
            finally { ch.Writer.Complete(); }
        }, TaskCreationOptions.DenyChildAttach);
        return new ChannelGuard<List<T>>(ch);
    }
}
