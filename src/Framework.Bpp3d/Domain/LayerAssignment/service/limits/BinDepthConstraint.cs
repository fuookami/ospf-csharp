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
/// 箱深度约束 / Bin depth constraint.
/// </summary>
/// <remarks>
/// 实现的**右端项**部分。
///
/// **修复前**：这个类同样没有构造函数参数，`RightHandSide` 恒返回**空字典**——与 `BinCapacityConstraint` 是同一个空壳形态。
/// 两个空壳互相掩护：一个被当成"还没接线"，另一个被当成"已经有约定"。
///
/// The **right-hand-side** half.
/// **Before the fix**: this class also had no constructor parameters and `RightHandSide` always returned an **empty dictionary** — the same shell shape
/// as `BinCapacityConstraint`. The two shells covered for each other: one looked "not yet wired", the other "already has a contract".
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public sealed class BinDepthConstraint<V> : IBpp3dConstraint<V>
    where V : struct, IFloatingNumber<V> {
    private readonly IReadOnlyList<Quantity<V>> _depths;

    /// <summary>
    /// 以箱型深度构造约束 / Build the constraint from the bin-type depths.
    /// </summary>
    /// <param name="depths">每个箱子的可用深度 / Usable depth of each bin.</param>
    public BinDepthConstraint(IReadOnlyList<Quantity<V>> depths) {
        ArgumentNullException.ThrowIfNull(depths);
        _depths = depths;
    }

    /// <summary>参与约束的箱子数 / Number of bins taking part in the constraint.</summary>
    public int Count => _depths.Count;

    /// <summary>
    /// 刷新对偶信息 / Refresh dual info from the shadow-price map.
    /// </summary>
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
    /// <returns>右端项 / The right-hand sides.</returns>
    public Result<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<V>>, ErrorCode, Error<ErrorCode>> RightHandSide() {
        var sides = new Dictionary<DemandShadowPriceKey, Quantity<V>>();
        for (int index = 0; index < _depths.Count; ++index) {
            sides[new DemandShadowPriceKey.ByMaterial($"bin_{index}")] = _depths[index];
        }
        return Results.Ok<IReadOnlyDictionary<DemandShadowPriceKey, Quantity<V>>>(sides);
    }

    /// <summary>
    /// 某只箱子的剩余深度是否够再放一层 / Whether a bin has room for one more layer of the given depth.
    /// </summary>
    /// <param name="binIndex">箱序号 / Bin index.</param>
    /// <param name="used">已用深度 / Depth already used.</param>
    /// <param name="added">待用深度 / Depth to add.</param>
    /// <returns>是否放得下 / Whether it fits.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> CanHold(int binIndex, Quantity<V> used, Quantity<V> added) {
        if (binIndex < 0 || binIndex >= _depths.Count) {
            return Results.Failed<bool>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"bin index {binIndex} is outside [0, {_depths.Count})"));
        }
        Quantity<V> available = _depths[binIndex];
        if (!used.Unit.Equals(added.Unit) || !used.Unit.Equals(available.Unit)) {
            return Results.Failed<bool>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"depth units {used.Unit} / {added.Unit} do not match the bin depth unit {available.Unit}"));
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> total = used.Add(added);
        if (total is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> totalFailed) {
            return Results.Failed<bool>(totalFailed.Error);
        }
        Quantity<V> sum = ((Ok<Quantity<V>, ErrorCode, Error<ErrorCode>>)total).Value;
        return Results.Ok(sum.Value.CompareTo(available.Value) <= 0);
    }
}
