#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
/// <summary>
/// 刀数下界可达性剪枝辅助方法 / Knife-count lower-bound reachability pruning helpers.
/// </summary>
internal static class GenerationKnifePruning {
    /// <summary>
    /// 判断最小刀数是否不可达 / Check whether min knife count is unreachable.
    /// </summary>
    /// <param name="minKnifeCount">最小刀数（可空）/ Min knife count (nullable).</param>
    /// <param name="currentCuts">当前已切刀数 / Current cut count.</param>
    /// <param name="maxAdditionalCuts">最大可增加刀数 / Max additional cuts possible.</param>
    /// <returns>true 如果最小刀数不可达 / true if min knife count is unreachable.</returns>
    public static bool IsMinKnifeCountUnreachable(
        UInt64? minKnifeCount,
        UInt64 currentCuts,
        UInt64 maxAdditionalCuts) {
        UInt64? requiredCuts = minKnifeCount;
        if (requiredCuts is null) {
            return false;
        }

        if (currentCuts.Geq(requiredCuts.Value)) {
            return false;
        }

        return currentCuts.Plus(maxAdditionalCuts).Ls(requiredCuts.Value);
    }

    /// <summary>
    /// 计算剩余切割容量 / Calculate remaining cut capacity.
    /// </summary>
    /// <param name="maxKnifeCount">最大刀数（可空）/ Max knife count (nullable).</param>
    /// <param name="currentCuts">当前已切刀数 / Current cut count.</param>
    /// <param name="searchCutCapacity">搜索切割容量（可空）/ Search cut capacity (nullable).</param>
    /// <returns>剩余切割容量（可空）/ Remaining cut capacity (nullable).</returns>
    public static UInt64? RemainingCutCapacity(
        UInt64? maxKnifeCount,
        UInt64 currentCuts,
        UInt64? searchCutCapacity = null) {
        UInt64? maxKnifeCapacity = maxKnifeCount.HasValue
            ? (currentCuts.Geq(maxKnifeCount.Value)
                ? UInt64.Zero
                : maxKnifeCount.Value.Minus(currentCuts))
            : null;

        return (maxKnifeCapacity, searchCutCapacity) switch {
            (not null, not null) => maxKnifeCapacity.Value.Leq(searchCutCapacity.Value)
                ? maxKnifeCapacity : searchCutCapacity,
            (not null, null) => maxKnifeCapacity,
            (null, not null) => searchCutCapacity,
            _ => null
        };
    }
}
