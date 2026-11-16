#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Infra;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;
/// <summary>
/// 箱容量约束 / Bin capacity constraint.
/// </summary>
/// <remarks>
/// 移植基线 `bpp3d/domain/layer_assignment/service/limits/BinCapacityConstraint.kt` 的**右端项**部分。
///
/// **修复前**：这个类没有构造函数参数，`RightHandSide` 恒返回**空字典**——一个永远"没有右端项"的约束。
/// 而它同时挂着 `EnableFinalBinCapacityConstraint = true` 这个开关（默认开启），于是"容量约束已启用"与
/// "容量约束什么都不做"同时成立，**两者在类型上无法区分**。测试更是把"返回空"写成了预期结果。
///
/// Ports the **right-hand-side** half of the baseline's `BinCapacityConstraint.kt`.
/// **Before the fix**: the class had no constructor parameters and `RightHandSide` always returned an **empty dictionary** — a constraint that
/// always has no right-hand side. It also carried the `EnableFinalBinCapacityConstraint = true` switch (on by default), so "the capacity constraint is
/// enabled" and "the capacity constraint does nothing" both held, **and no type could tell them apart**. The test even asserted the empty result as
/// expected.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class BinCapacityConstraint<V> : IBpp3dConstraint<V>
    where V : struct, IFloatingNumber<V> {
    private readonly IReadOnlyList<Quantity<V>> _capacities;

    /// <summary>
    /// 以箱型容量构造约束 / Build the constraint from the bin-type capacities.
    /// </summary>
    /// <param name="capacities">每个箱子的承重容量 / Weight capacity of each bin.</param>
    public BinCapacityConstraint(IReadOnlyList<Quantity<V>> capacities) {
        ArgumentNullException.ThrowIfNull(capacities);
        _capacities = capacities;
    }

    /// <summary>参与约束的箱子数 / Number of bins taking part in the constraint.</summary>
    public int Count => _capacities.Count;

    /// <summary>
    /// 刷新对偶信息 / Refresh dual info from the shadow-price map.
    /// </summary>
    /// <remarks>
    /// 基线在 `invoke(model)` 里逐箱加一条 `Σ 装载重量 ≤ capacity` 的线性约束，**不做对偶刷新**；
    /// 这里同样不做，因此恒成功——但右端项**不再是空的**。
    ///
    /// The baseline adds one `Σ loading weight ≤ capacity` row per bin inside `invoke(model)` and performs **no dual refresh**; this does the same, so it
    /// always succeeds — but the right-hand side is **no longer empty**.
    /// </remarks>
    /// <param name="shadowPriceMap">影子价格表 / Shadow-price map.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>刷新结果 / The refresh result.</returns>
    public Task<Result<Success, ErrorCode, Error<ErrorCode>>> RefreshAsync(
        AbstractBpp3dShadowPriceMap shadowPriceMap,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Results.Ok<Success>(Results.SuccessInstance));

    /// <summary>
    /// 构建约束右端项 / Build the RHS vector for the master problem.
    /// </summary>
    /// <remarks>
    /// 每个箱子一条右端项，键为 `ByMaterial(bin_&lt;i&gt;)`——**用箱序号而不是箱型编码**，因为同型箱子可以有多个，
    /// 而每只箱子各有一条独立的载重上限。
    ///
    /// One right-hand side per bin, keyed `ByMaterial(bin_&lt;i&gt;)` — **by bin index, not by type code**, because several bins can share a type and
    /// each one has its own independent weight limit.
    /// </remarks>
    /// <returns>右端项 / The right-hand sides.</returns>
    public Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<V>>, ErrorCode, Error<ErrorCode>> RightHandSide() {
        var sides = new Dictionary<DemandShadowPriceKey, Quantity<V>>();
        for (int index = 0; index < _capacities.Count; ++index) {
            sides[new DemandShadowPriceKey.ByMaterial($"bin_{index}")] = _capacities[index];
        }
        return Results.Ok<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<V>>>(sides);
    }

    /// <summary>
    /// 某只箱子的剩余载重是否够装下给定重量 / Whether a bin's remaining capacity covers the given weight.
    /// </summary>
    /// <param name="binIndex">箱序号 / Bin index.</param>
    /// <param name="used">已装重量 / Weight already loaded.</param>
    /// <param name="added">待装重量 / Weight to add.</param>
    /// <returns>是否装得下 / Whether it fits.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> CanHold(int binIndex, Quantity<V> used, Quantity<V> added) {
        if (binIndex < 0 || binIndex >= _capacities.Count) {
            return Results.Failed<bool>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"bin index {binIndex} is outside [0, {_capacities.Count})"));
        }
        Quantity<V> capacity = _capacities[binIndex];
        // 单位不同无法相加——**必须报错，不能当成"装得下"**。
        // Different units cannot be added — **this must fail, not be treated as "it fits"**.
        if (!used.Unit.Equals(added.Unit) || !used.Unit.Equals(capacity.Unit)) {
            return Results.Failed<bool>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"weight units {used.Unit} / {added.Unit} do not match capacity unit {capacity.Unit}"));
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> total = used.Add(added);
        if (total is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> totalFailed) {
            return Results.Failed<bool>(totalFailed.Error);
        }
        Quantity<V> sum = ((Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>)total).Value;
        return Results.Ok(sum.Value.CompareTo(capacity.Value) <= 0);
    }
}
