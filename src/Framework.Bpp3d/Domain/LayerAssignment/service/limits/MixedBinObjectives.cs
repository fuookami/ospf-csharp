#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 箱型体积消耗最小化目标：系数是"权重 × 箱型体积" /
/// Bin volume consumption minimisation: the coefficient is the weight times the bin type's volume.
/// </summary>
/// <remarks>
/// 与的 `BinVolumeMinimization` 对应。系数写成
/// `weight * solverValueAdapter.volumeToSolver(bin.type.volume)`，把数值换算留给适配器；这里同样只产出**系数**，
/// 不替调用方决定符号域——**注册多项式是模型侧的事，判据能测的是"哪个箱拿到什么系数"。**
///
/// The code writes the coefficient as
/// `weight * solverValueAdapter.volumeToSolver(bin.type.volume)` and leaves the value conversion to an adapter; this does the same and only produces the
/// **coefficient** — registering the polynomial is the model's business, and what a criterion can check is "which bin gets what coefficient".
/// </remarks>
/// <param name="Weight">目标权重 / The objective weight.</param>
public sealed record BinVolumeMinimization(Flt64? Weight = null) {
    /// <summary>目标名称 / Objective name.</summary>
    public string Name => "bin_volume_minimization";

    /// <summary>实际权重，缺省 1 / The effective weight, one by default.</summary>
    public Flt64 EffectiveWeight => Weight ?? Flt64.One;

    /// <summary>是否应当注册：零权重不注册 / Whether to register: a zero weight registers nothing.</summary>
    public bool ShouldRegister => EffectiveWeight.CompareTo(Flt64.Zero) != 0;

    /// <summary>某个箱型的系数（权重 × 体积）/ The coefficient of one bin type (weight times volume).</summary>
    /// <param name="type">箱型 / The bin type.</param>
    /// <returns>系数；不注册时为 null / The coefficient, or null when nothing is registered.</returns>
    public Flt64? CoefficientFor(BinType<Flt64> type) {
        ArgumentNullException.ThrowIfNull(type);
        return ShouldRegister ? (EffectiveWeight * type.Volume.Value)! : null;
    }
}

/// <summary>
/// 箱型成本最小化目标：系数是"权重 × 该箱型的成本" / Per-bin-type cost minimisation: weight times the type's cost.
/// </summary>
/// <remarks>
/// 与基线 `BinTypeCostMinimization` 对应，三个跳过条件逐条照搬：**成本表为空**、**权重为零**、**该箱型未配置成本或系数恰好为零**。
/// 最后一条是`mapIndexedNotNull`：**未配置成本的箱型根本不进多项式**，而不是以 0 系数进去——
/// 0 系数项在求和中是噪声，在报告里却会被读成"这个箱型免费"。
///
/// And copies its three skip conditions: an empty cost table, a zero weight, and a bin type with no
/// configured cost or a coefficient that lands on zero. The last one is its `mapIndexedNotNull`: a type without a cost never enters the
/// polynomial at all rather than entering with a zero coefficient — zero terms are noise in the sum while reading as "this bin type is free" in a report.
/// </remarks>
/// <param name="TypeCost">箱型代码到成本的映射 / Bin type code to cost.</param>
/// <param name="Weight">目标权重 / The objective weight.</param>
public sealed record BinTypeCostMinimization(
    IReadOnlyDictionary<string, Flt64> TypeCost,
    Flt64? Weight = null) {

    /// <summary>目标名称 / Objective name.</summary>
    public string Name => "bin_type_cost_minimization";

    /// <summary>实际权重，缺省 1 / The effective weight, one by default.</summary>
    public Flt64 EffectiveWeight => Weight ?? Flt64.One;

    /// <summary>是否应当注册 / Whether to register.</summary>
    public bool ShouldRegister => TypeCost.Count > 0 && EffectiveWeight.CompareTo(Flt64.Zero) != 0;

    /// <summary>某个箱型的系数；未配置或为零时返回 null（该项不进多项式）/ The coefficient, or null when the term is skipped.</summary>
    /// <param name="type">箱型 / The bin type.</param>
    /// <returns>系数或 null / The coefficient, or null.</returns>
    public Flt64? CoefficientFor(BinType<Flt64> type) {
        ArgumentNullException.ThrowIfNull(type);
        if (!ShouldRegister) {
            return null;
        }
        if (!TypeCost.TryGetValue(type.TypeCode, out Flt64 cost)) {
            return null;
        }
        Flt64 coefficient = (EffectiveWeight * cost)!;
        return coefficient.CompareTo(Flt64.Zero) == 0 ? null : coefficient;
    }
}

/// <summary>
/// 按箱型计价的单箱成本函数 / A per-bin cost function priced by type.
/// </summary>
/// <remarks>
/// **缺省成本不是零，除非调用方明确给零**：`Packing.BinCost.PerType` 已经按这个理由实现过一次，
/// 这里的缺省值同样交给调用方决定，而不是替它选一个"看起来安全"的零。
///
/// **The default is not zero unless the caller asks for zero**: `Packing.BinCost.PerType` was implemented for the same reason, and the default here is
/// likewise the caller's decision rather than a "looks safe" zero.
/// </remarks>
/// <returns>成本构造函数 / The cost-function builder.</returns>
public static class PerTypeBinCost {
    /// <summary>构造成本函数 / Build the cost function.</summary>
    /// <param name="typeCost">箱型代码到成本的映射 / Bin type code to cost.</param>
    /// <param name="defaultCost">未配置箱型的缺省成本 / The cost for unconfigured types.</param>
    /// <returns>成本函数 / The cost function.</returns>
    public static Func<BinType<Flt64>, Flt64> Of(
        IReadOnlyDictionary<string, Flt64> typeCost,
        Flt64? defaultCost = null) {
        ArgumentNullException.ThrowIfNull(typeCost);
        Flt64 fallback = defaultCost ?? Flt64.Zero;
        return type => typeCost.TryGetValue(type.TypeCode, out Flt64 configured) ? configured : fallback;
    }
}