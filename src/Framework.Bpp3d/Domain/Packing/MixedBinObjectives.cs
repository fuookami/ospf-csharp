// 混合箱型目标与余料统计 / Mixed bin-type objectives and waste statistics.
// 与固定 Kotlin 基线 `bpp3d/domain/.../MixedBinObjectives.kt` 的纯函数部分对应。
// Mirrors the pure-function part of the pinned Kotlin baseline's `MixedBinObjectives.kt`.
//
// **B1306 更正：** **这里原来写着"基线里的两个 `*Minimization` 类依赖 `PreciseAssignment`（C# 侧不存在）"，
// 而那句话早就不成立了——`src/Framework.Bpp3d/Domain/LayerAssignment/Model/PreciseAssignment.cs` 就在仓库里。**
// **所以两个目标类已经补齐，在 `Domain/LayerAssignment/service/limits/MixedBinObjectives.cs`，本文件只留纯函数。**
//
// **B1306 correction:** **this used to claim "the baseline's two `*Minimization` classes depend on `PreciseAssignment`, which does not exist on the
// C# side" — and that stopped being true long ago: `src/Framework.Bpp3d/Domain/LayerAssignment/Model/PreciseAssignment.cs` is right here in the repo.**
// **So both objectives now exist in `Domain/LayerAssignment/service/limits/MixedBinObjectives.cs`; this file keeps only the pure functions.**

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing;

/// <summary>
/// 余料统计 / Waste statistics.
/// </summary>
/// <remarks>
/// **余料是统计量，不是优化目标**：基线文件头明确写了这一点。体积浪费不能直接当目标，因为需求体积固定时
/// "少用体积" 与 "少用箱子" 并不总是等价——把余料当目标会在边界上选出一个装得更满但用了更多箱子的解。
///
/// **Waste is a statistic, not an objective**: the baseline file header says so explicitly. Volume waste cannot be the objective directly, because with a fixed demanded
/// volume "using less volume" and "using fewer bins" are not always equivalent — optimising waste can pick a solution that packs tighter yet uses more bins.
/// </remarks>
public static class BinWaste {
    /// <summary>
    /// 单箱的余料体积 / The waste volume of one bin.
    /// </summary>
    /// <param name="containerVolume">箱型体积 / The bin volume.</param>
    /// <param name="loadedLayerVolumes">已装层体积 / The loaded layer volumes.</param>
    /// <returns>余料体积，不小于零 / The waste volume, never negative.</returns>
    /// <remarks>
    /// **超出容量时返回零而不是负数**：负数余料是一个"装得比箱子还多"的无声声明，
    /// 它会在求和里悄悄抵消别的箱子的余料，让总浪费看起来更低。
    ///
    /// **Over-capacity yields zero, not a negative value**: a negative waste is a silent claim that more was packed than the bin holds, and it would quietly cancel out
    /// another bin's waste in any sum, making the total look smaller.
    /// </remarks>
    public static FltX Volume(FltX containerVolume, IReadOnlyList<Quantity<FltX>> loadedLayerVolumes) {
        System.ArgumentNullException.ThrowIfNull(loadedLayerVolumes);

        FltX occupied = FltX.Zero;
        foreach (Quantity<FltX> volume in loadedLayerVolumes) {
            occupied += volume.Value;
        }
        return containerVolume.CompareTo(occupied) > 0 ? containerVolume - occupied : FltX.Zero;
    }

    /// <summary>
    /// 单箱的余料率 / The waste ratio of one bin.
    /// </summary>
    /// <param name="containerVolume">箱型体积 / The bin volume.</param>
    /// <param name="loadedLayerVolumes">已装层体积 / The loaded layer volumes.</param>
    /// <returns>落在 `[0, 1]` 的余料率 / The waste ratio within `[0, 1]`.</returns>
    /// <remarks>
    /// 体积为零或为负时返回零：这是唯一的除零入口，而**除零返回零比返回 `NaN` 好**——`NaN` 会让整条链上的比较全部为假，
    /// 于是一个明显异常的箱型反而静默通过所有检查。
    ///
    /// A zero or negative volume returns zero: this is the only division entry point, and **returning zero beats returning `NaN`**, because `NaN` makes every comparison
    /// down the chain false, so a clearly broken bin type silently passes every check instead.
    /// </remarks>
    public static FltX Ratio(FltX containerVolume, IReadOnlyList<Quantity<FltX>> loadedLayerVolumes) {
        if (containerVolume.CompareTo(FltX.Zero) <= 0) {
            return FltX.Zero;
        }
        return Volume(containerVolume, loadedLayerVolumes) / containerVolume;
    }
}

/// <summary>
/// 按箱型计价的单箱成本 / Per-bin cost priced by type.
/// </summary>
/// <remarks>
/// **未配置的箱型取缺省成本，而不是零**：取零会让"没配价"看起来像"免费"，求解器于是**偏爱**那些恰好没配价的箱型。
///
/// **An unconfigured bin type uses the default cost, not zero**: zero would make "no price configured" look like "free", so the solver would actually **prefer** the bin
/// types that happen to be missing from the price table.
/// </remarks>
public static class BinCost {
    /// <summary>
    /// 构造按箱型取值的成本函数 / Build a cost lookup keyed by bin type code.
    /// </summary>
    /// <param name="typeCost">箱型到成本的映射 / The bin type to cost map.</param>
    /// <param name="defaultCost">未配置箱型的缺省成本 / The default cost for unconfigured types.</param>
    /// <returns>成本函数 / The cost function.</returns>
    public static System.Func<string, FltX> PerType(
        IReadOnlyDictionary<string, FltX> typeCost,
        FltX? defaultCost = null) {
        System.ArgumentNullException.ThrowIfNull(typeCost);
        FltX fallback = defaultCost ?? FltX.Zero;
        return typeCode => typeCost.TryGetValue(typeCode, out FltX configured) ? configured : fallback;
    }
}
