// 一维装箱指标 / One-dimensional packing indicators.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;

/// <summary>
/// 保留领域精度和长度口径的装箱指标 / Packing indicators preserving domain precision and length units.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="BinCount">箱数 / Bin count.</param>
/// <param name="ItemCount">件数 / Item count.</param>
/// <param name="ConsumedLength">消耗箱长 / Consumed bin length.</param>
/// <param name="RemnantLength">可用尾余长度，不包含间距 / Free tail length, excluding spacing.</param>
/// <param name="Utilization">长度利用率 / Length utilization.</param>
/// <param name="Cost">成本 / Cost.</param>
public sealed record PackingKpi<V>(
    ulong BinCount,
    ulong ItemCount,
    Quantity<V> ConsumedLength,
    Quantity<V> RemnantLength,
    V Utilization,
    V Cost)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 生成物理量 KPI / Build quantity-aware KPIs.
/// </summary>
public static class PackingKpiExtensions {
    /// <summary>
    /// 由装箱结果计算指标 / Compute indicators from a packing result.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="result">装箱结果 / Packing result.</param>
    /// <param name="unit">统计长度单位 / Reporting length unit.</param>
    /// <returns>指标或量纲错误 / Indicators or a dimension error.</returns>
    /// <remarks>
    /// **长度统一换算到调用方给定的单位**，而不是各自用箱型自己的单位：
    /// 混合箱型的箱长单位可能不同（毫米与米），直接相加会得到无意义的数。
    /// 换算失败（量纲不是长度）时返回 `Failed`，**不静默跳过那一项**——
    /// 跳过会让 KPI 偏小，而偏小的 KPI 看起来像"装箱更紧凑"。
    ///
    /// **Lengths are converted to the caller-supplied unit**, not each bin's own unit: mixed bin types may report millimetres and metres, and adding them raw yields
    /// nonsense. A failed conversion (not a length) returns `Failed` rather than **silently skipping** that item — skipping would shrink the KPI, and a smaller KPI looks
    /// like tighter packing.
    /// </remarks>
    public static Result<PackingKpi<V>, ErrorCode, Error<ErrorCode>> Kpi<V>(
        this PackingResult<V> result,
        PhysicalUnit unit)
        where V : struct, IFloatingNumber<V> {
        V zero = NumericConstantsRegistry.For<V>().Zero;
        V consumed = zero;
        V free = zero;
        V cost = zero;
        ulong count = 0;

        foreach (PackingPlan<V> plan in result.Plans) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted = plan.Bin.Type.Length.To(unit);
            if (converted is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> convertedOk) {
                return Results.Failed<PackingKpi<V>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "KPI 长度量纲无效 / Invalid KPI length dimension"));
            }
            consumed = consumed.Plus(convertedOk.Value.Value);
            cost = cost.Plus(plan.Bin.Type.Cost);
            count += (ulong)plan.Placements.Count;
        }

        foreach (Remnant<V> remnant in result.Remnants) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted = remnant.Length.To(unit);
            if (converted is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> convertedOk) {
                return Results.Failed<PackingKpi<V>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "KPI 余量量纲无效 / Invalid KPI remnant dimension"));
            }
            free = free.Plus(convertedOk.Value.Value);
        }

        return Results.Ok(new PackingKpi<V>(
            BinCount: (ulong)result.Plans.Count,
            ItemCount: count,
            ConsumedLength: new Quantity<V>(consumed, unit),
            RemnantLength: new Quantity<V>(free, unit),
            Utilization: result.Utilization,
            Cost: cost));
    }
}
