// 空间区域、块与旋转校验 / Space region, block and rotation guard.
// 与固定 Kotlin 基线 `bpp2d/domain/block_loading/model/Space2.kt`（147 行）与
// `bpp2d/domain/block_loading/service/RotationGuard.kt`（37 行）对应。
// Mirrors the pinned baseline's `Space2.kt` and `RotationGuard.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 矩形区域 / A rectangular region.
/// </summary>
/// <param name="X">左下角 x 坐标 / Lower-left x coordinate.</param>
/// <param name="Y">左下角 y 坐标 / Lower-left y coordinate.</param>
/// <param name="Width">宽度 / Width.</param>
/// <param name="Height">Height / Height.</param>
public sealed record Space2<V>
    (Quantity<V> X, Quantity<V> Y, Quantity<V> Width, Quantity<V> Height)
    where V : struct, IFloatingNumber<V> {
    /// <summary>区域面积 / Region area.</summary>
    public Quantity<V> Area => QuantityOps.QuantityProduct(Width, Height);

    /// <summary>
    /// 判断给定包围尺寸能否放入本区域 / Check whether a bounding size fits the region.
    /// </summary>
    /// <remarks>
    /// 放得下是**边界含等号**：恰好等宽等高视为可放。若改成严格小于，一块正好填满的区域会被判为不可用，
    /// 排样会白白留下一条恰好够宽的缝。
    /// Fitting is **inclusive**: exactly equal width and height counts as fitting. Strict inequality would mark a
    /// region that is filled exactly as unusable, leaving a gap that is precisely wide enough.
    /// </remarks>
    /// <param name="boxWidth">包围宽度 / Bounding width.</param>
    /// <param name="boxHeight">包围高度 / Bounding height.</param>
    /// <returns>是否可容纳 / Whether it fits.</returns>
    public bool Enabled(Quantity<V> boxWidth, Quantity<V> boxHeight) =>
        QuantityCore.LessOrEqual(boxWidth, Width) && QuantityCore.LessOrEqual(boxHeight, Height);

    /// <summary>
    /// 判断给定装载形状能否放入本区域 / Check whether a packing shape fits the region.
    /// </summary>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <returns>是否可容纳 / Whether it fits.</returns>
    public bool Enabled(PackingShape2<V> shape) {
        ArgumentNullException.ThrowIfNull(shape);
        return Enabled(shape.BoundingWidth, shape.BoundingHeight);
    }

    /// <summary>
    /// 沿 X 方向切出左侧一块 / Cut a left-hand piece along X.
    /// </summary>
    /// <param name="cutWidth">切出宽度 / Cut width.</param>
    /// <returns>切出区域与剩余区域，单位不可运算时失败 / The cut region and the remainder, failing when units cannot
    /// be combined.</returns>
    public Result<(Space2<V> Left, Space2<V> Right), ErrorCode, Error<ErrorCode>> SplitX(
        Quantity<V> cutWidth) =>
        Combine(
            QuantityOperators.Add(X, cutWidth),
            QuantityOperators.Subtract(Width, cutWidth),
            (offset, remainder) => (
                new Space2<V>(X, Y, cutWidth, Height),
                new Space2<V>(offset, Y, remainder, Height)));

    /// <summary>
    /// 沿 Y 方向切出底部一块 / Cut a bottom piece along Y.
    /// </summary>
    /// <param name="cutHeight">切出高度 / Cut height.</param>
    /// <returns>切出区域与剩余区域，单位不可运算时失败 / The cut region and the remainder, failing when units cannot
    /// be combined.</returns>
    public Result<(Space2<V> Bottom, Space2<V> Top), ErrorCode, Error<ErrorCode>> SplitY(
        Quantity<V> cutHeight) =>
        Combine(
            QuantityOperators.Add(Y, cutHeight),
            QuantityOperators.Subtract(Height, cutHeight),
            (offset, remainder) => (
                new Space2<V>(X, Y, Width, cutHeight),
                new Space2<V>(X, offset, Width, remainder)));

    /// <summary>
    /// 合并偏移量与余量的运算结果 / Combine the offset and remainder quantities.
    /// </summary>
    /// <remarks>
    /// 基线的切片是纯算术、遇到单位不符直接抛异常；这里改成返回结果，因为**单位不符是输入错误，不是编程错误**，
    /// 不应该用异常打断整批排样。
    /// The baseline's split is plain arithmetic and throws on unit mismatch; this returns a result because **mismatched
    /// units are an input error, not a programming error** and should not abort a whole packing run with an exception.
    /// </remarks>
    /// <param name="offset">切出块的偏移量 / Offset of the cut piece.</param>
    /// <param name="remainder">剩余尺寸 / Remaining size.</param>
    /// <param name="build">由两者构造区域对 / Builds the region pair from both.</param>
    /// <returns>区域对或失败 / The region pair, or a failure.</returns>
    private static Result<(Space2<V> First, Space2<V> Second), ErrorCode, Error<ErrorCode>> Combine(
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> offset,
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> remainder,
        Func<Quantity<V>, Quantity<V>, (Space2<V> First, Space2<V> Second)> build) {
        if (!offset.IsOk) {
            return offset switch {
                Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> failure =>
                    Results.Failed<(Space2<V>, Space2<V>)>(failure.Error),
                Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<(Space2<V>, Space2<V>), ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<(Space2<V>, Space2<V>)>(new Bpp2dInternalError(
                    "切片内部失败 / Split failed internally")),
            };
        }
        if (!remainder.IsOk) {
            return remainder switch {
                Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> failure =>
                    Results.Failed<(Space2<V>, Space2<V>)>(failure.Error),
                Fatal<Quantity<V>, ErrorCode, Error<ErrorCode>> fatal =>
                    new Fatal<(Space2<V>, Space2<V>), ErrorCode, Error<ErrorCode>>(fatal.Errors),
                _ => Results.Failed<(Space2<V>, Space2<V>)>(new Bpp2dInternalError(
                    "切片内部失败 / Split failed internally")),
            };
        }
        return Results.Ok(build(offset.Value, remainder.Value));
    }

    /// <summary>
    /// 判断区域是否已退化（宽或高非正）/ Check whether the region is degenerate.
    /// </summary>
    /// <returns>是否退化 / Whether degenerate.</returns>
    public bool IsDegenerate() =>
        QuantityCore.IsNonPositive(Width) || QuantityCore.IsNonPositive(Height);
}

/// <summary>
/// 块：一组放置连同其包围区域 / Block: a group of placements together with their bounding region.
/// </summary>
/// <param name="Space">块的包围区域 / Bounding region of the block.</param>
/// <param name="Placements">块内放置 / Placements inside the block.</param>
/// <param name="Source">生成来源 / Generation source.</param>
public sealed record Block2<V>
    (Space2<V> Space, IReadOnlyList<Placement2<V>> Placements, string Source)
    where V : struct, IFloatingNumber<V> {
    /// <summary>块内件数 / Amount inside the block.</summary>
    public int Amount => Placements.Count;

    /// <summary>块内实际占用面积 / Actually occupied area inside the block.</summary>
    public Quantity<V> Area =>
        QuantityCore.Sum(Placements.Select(placement => placement.Shape.Area))
        ?? QuantityOps.QuantityZeroOf(Space.Width);

    /// <summary>块的利用率，区域面积非正时为零 / Block utilisation, zero when the region area is not positive.</summary>
    public V Utilization {
        get {
            Quantity<V> spaceArea = Space.Area;
            if (!QuantityCore.IsPositive(spaceArea)) {
                return QuantityOps.QuantityZeroOf(spaceArea).Value;
            }
            return Area.Value.Div(spaceArea.Value);
        }
    }

    /// <summary>块内件数映射，按物品编号统计 / Item amount map inside the block.</summary>
    public IReadOnlyDictionary<string, int> ItemAmounts =>
        Placements
            .Where(placement => placement.ItemId is not null)
            .GroupBy(placement => placement.ItemId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}

/// <summary>
/// 旋转候选校验 / Rotation candidate guard.
/// </summary>
public static class RotationGuard {
    /// <summary>
    /// 校验物品是否允许进入旋转候选生成 / Validate that an item may enter rotated candidate generation.
    /// </summary>
    /// <remarks>
    /// **圆形件永远放行**：圆形件旋转 90 度后占用区域不变，"是否允许旋转"对它没有几何意义。若在这里拦下，
    /// 调用方会以为圆形件不能旋转，从而凭空少掉一半候选位。
    /// **Circles always pass**: rotating a circle by 90 degrees changes nothing, so "rotation allowed" has no geometric
    /// meaning for it. Rejecting here would make the caller believe circles cannot rotate and shrink the candidate set
    /// for no reason.
    /// </remarks>
    /// <param name="item">物品 / The item.</param>
    /// <returns>允许时成功，否则失败 / Success when allowed, otherwise a failure.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> RequireRotationAllowed(
        IAbstractItem<FltX> item) {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsCircleItem()) {
            return Results.OkInstance;
        }
        if (!item.AllowRotate) {
            return Results.Failed<Success>(new Bpp2dValidationError(
                string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "物品 {0} 未允许旋转，不能生成旋转候选 / "
                    + "Item {0} does not allow rotation; rotated candidates are rejected",
                    item.Id.Value)));
        }
        return Results.OkInstance;
    }
}
