// VRP 距离计算 / VRP distance calculation.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;

/// <summary>
/// 距离计算扩展点 / Distance-calculation extension point.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IDistanceCalculator<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 计算两节点距离 / Calculate the distance between two nodes.
    /// </summary>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <returns>距离或计算失败 / The distance, or a calculation failure.</returns>
    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Distance(NetworkNode<V> from, NetworkNode<V> to);

    /// <summary>
    /// 按稳定弧标识计算距离；默认退回端点策略 / Calculate distance by stable arc identity; defaults to the endpoint policy.
    /// </summary>
    /// <param name="arc">资源弧 / Resource arc.</param>
    /// <param name="from">起点 / Origin.</param>
    /// <param name="to">终点 / Destination.</param>
    /// <returns>距离或计算失败 / The distance, or a calculation failure.</returns>
    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Distance(
        ResourceArc<VehicleTypeId> arc, NetworkNode<V> from, NetworkNode<V> to) {
        ArgumentNullException.ThrowIfNull(arc);
        return Distance(from, to);
    }
}

/// <summary>
/// 完整精度欧氏距离策略 / Full-precision Euclidean distance policy.
/// </summary>
/// <remarks>
/// **坐标轴必须完全一致且非空**：两点的轴集不同（一个二维、一个三维）时，距离无从定义；若按"取较短者"或"缺失补零"来
/// 凑合，会静默地把一个三维点投影到平面上，得到一个看起来合理但错误的距离。
/// **The coordinate axes must match exactly and be non-empty**: with different axis sets (one 2-D, one 3-D) the distance is
/// undefined, and papering over it — taking the shorter set or padding zeros — silently projects a 3-D point onto a plane and
/// yields a plausible-looking wrong distance.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="distanceUnit">距离单位 / Distance unit.</param>
public class EuclideanDistanceCalculator<V>(PhysicalUnit distanceUnit) : IDistanceCalculator<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>距离单位 / Distance unit.</summary>
    protected PhysicalUnit DistanceUnit { get; } = distanceUnit;

    /// <inheritdoc/>
    public virtual Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Distance(
        NetworkNode<V> from,
        NetworkNode<V> to) {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);

        IReadOnlyList<Quantity<V>> fromCoordinates = from.VrpCoordinates();
        IReadOnlyList<Quantity<V>> toCoordinates = to.VrpCoordinates();
        IReadOnlyList<(string Axis, Quantity<V> Value)> fromAxes = from.VrpCoordinateAttributes();
        IReadOnlyList<(string Axis, Quantity<V> Value)> toAxes = to.VrpCoordinateAttributes();
        if (fromAxes.Count == 0 || !AxesMatch(fromAxes, toAxes)) {
            return NetworkSchedulingErrors.Failure<Quantity<V>>(
                "计算距离失败：节点坐标轴必须相同且非空 / Failed to calculate distance: node coordinate axes must be "
                + "equal and non-empty");
        }

        V squared = fromCoordinates[0].Value.Constants.Zero;
        for (int index = 0; index < fromCoordinates.Count; index++) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> lhs = fromCoordinates[index].ConvertTo(DistanceUnit);
            if (lhs is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
                return IncompatibleCoordinate(from);
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> rhs = toCoordinates[index].ConvertTo(DistanceUnit);
            if (rhs is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
                return IncompatibleCoordinate(to);
            }
            V difference = lhs.Value.Value.Minus(rhs.Value.Value);
            squared = squared.Plus(difference.Times(difference));
        }
        return Results.Ok(new Quantity<V>(squared.Sqrt(), DistanceUnit));
    }

    /// <summary>比较两条轴名序列是否完全一致 / Compare two axis-name sequences for exact equality.</summary>
    /// <param name="left">左轴序列 / Left axis sequence.</param>
    /// <param name="right">右轴序列 / Right axis sequence.</param>
    /// <returns>是否一致 / Whether they match.</returns>
    private static bool AxesMatch(
        IReadOnlyList<(string Axis, Quantity<V> Value)> left,
        IReadOnlyList<(string Axis, Quantity<V> Value)> right) {
        if (left.Count != right.Count) {
            return false;
        }
        for (int index = 0; index < left.Count; index++) {
            if (!string.Equals(left[index].Axis, right[index].Axis, StringComparison.Ordinal)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>构造坐标单位不兼容失败 / Build a coordinate-unit incompatibility failure.</summary>
    /// <param name="node">出问题的节点 / The offending node.</param>
    /// <returns>失败结果 / The failure.</returns>
    private Result<Quantity<V>, ErrorCode, Error<ErrorCode>> IncompatibleCoordinate(NetworkNode<V> node) =>
        NetworkSchedulingErrors.Failure<Quantity<V>>(
            $"计算距离失败：节点 {node.Id.Value} 的坐标无法转换到 {DistanceUnit.Symbol} / Failed to calculate distance: "
            + $"coordinates of node {node.Id.Value} cannot be converted to {DistanceUnit.Symbol}");
}

/// <summary>
/// Solomon 一位小数距离策略 / Solomon one-decimal distance policy.
/// </summary>
/// <remarks>
/// **取整必须发生在欧氏距离算完之后**：先把坐标取整再算距离，会把 0.04 的坐标差抹成 0，让两个不同位置的点重合；
/// Solomon 基准要求的是"距离保留一位小数"，不是"坐标保留一位小数"。
/// **Rounding must happen after the Euclidean distance is computed**: rounding coordinates first wipes a 0.04 difference to
/// zero and collapses two distinct points; the Solomon benchmarks require one-decimal *distances*, not one-decimal
/// *coordinates*.
///
/// **与一处实现差异**：`V.roundTo(1)`，而 C# 的 `IFloatingNumber&lt;TSelf&gt;` 接口不暴露取整能力（`RoundTo`
/// 只存在于 `Flt32`／`Flt64`／`FltX` 的具体类型上）。因此本实现把取整作为构造参数传入。语义不变，只是把"如何取整"交给
/// 调用方显式提供，`ForFlt64`／`ForFltX` 两个工厂给出常用实现。
/// **One implementation divergence from the code**: the code calls `V.roundTo(1)`, but C#'s
/// `IFloatingNumber&lt;TSelf&gt;` does not expose rounding (`RoundTo` lives only on the concrete `Flt32`／`Flt64`／`FltX` types).
/// This implementation therefore takes rounding as a constructor argument. Semantics are unchanged; only "how to round" is made
/// explicit, with `ForFlt64`／`ForFltX` factories supplying the common cases.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class SolomonDistancePolicy<V> : IDistanceCalculator<V> where V : struct, IFloatingNumber<V> {
    private readonly EuclideanDistanceCalculator<V> _delegate;
    private readonly Func<V, V> _roundToOneDecimal;
    private readonly PhysicalUnit _distanceUnit;

    /// <summary>
    /// 构造 Solomon 距离策略 / Construct the Solomon distance policy.
    /// </summary>
    /// <param name="distanceUnit">距离单位 / Distance unit.</param>
    /// <param name="roundToOneDecimal">保留一位小数的取整函数 / Rounding function keeping one decimal.</param>
    public SolomonDistancePolicy(PhysicalUnit distanceUnit, Func<V, V> roundToOneDecimal) {
        ArgumentNullException.ThrowIfNull(distanceUnit);
        ArgumentNullException.ThrowIfNull(roundToOneDecimal);
        _distanceUnit = distanceUnit;
        _roundToOneDecimal = roundToOneDecimal;
        _delegate = new EuclideanDistanceCalculator<V>(distanceUnit);
    }

    /// <inheritdoc/>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Distance(
        NetworkNode<V> from,
        NetworkNode<V> to) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> distance = _delegate.Distance(from, to);
        if (distance is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>) {
            return NetworkSchedulingErrors.Failure<Quantity<V>>(
                "计算 Solomon 距离失败：欧氏距离计算失败 / Failed to calculate Solomon distance: Euclidean calculation "
                + "failed");
        }
        return Results.Ok(new Quantity<V>(_roundToOneDecimal(distance.Value.Value), _distanceUnit));
    }
}
