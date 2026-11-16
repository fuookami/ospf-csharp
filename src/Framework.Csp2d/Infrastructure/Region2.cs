// 二维裁切区域 / A two-dimensional cutting region.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Infrastructure;

/// <summary>刀线方向 / Cut line orientation.</summary>
public enum CutOrientation2 {
    /// <summary>竖刀 / A vertical cut.</summary>
    Vertical,

    /// <summary>横刀 / A horizontal cut.</summary>
    Horizontal,
}

/// <summary>
/// 二维裁切区域，以左下角与宽高表示 / A 2D cutting region, expressed by its lower-left corner and size.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="X">左下角 x 坐标 / The lower-left x coordinate.</param>
/// <param name="Y">左下角 y 坐标 / The lower-left y coordinate.</param>
/// <param name="Width">宽度 / The width.</param>
/// <param name="Height">高度 / The height.</param>
/// <remarks>
/// **裁切与装箱的差别就在这个类型**：装箱只关心"放在哪里"，套裁还关心**每一刀把料切成哪两块**，
/// 所以需要一个可递归切分的矩形区域，而不是只有"板"与"件"。
///
/// **`MaxX` / `MaxY` 返回 `Result` 而不是直接抛**：物理量的加法在**单位不一致**时会失败，
/// 而抛异常会把一个可恢复的输入错误变成进程级事故。两个单位相同的区域相加永远成功。
///
/// **This type is where cutting stock differs from bin packing**: packing only cares "where it sits", cutting stock also cares **which two pieces each cut produces**,
/// so a recursively splittable region is needed rather than only "sheet" and "piece". **`MaxX` / `MaxY` return a `Result` instead of throwing**: adding physical
/// quantities fails on **mismatched units**, and throwing would turn a recoverable input error into a process-level incident. Two regions with the same unit always add.
/// </remarks>
public sealed record Region2<V>(
    Quantity<V> X,
    Quantity<V> Y,
    Quantity<V> Width,
    Quantity<V> Height)
    where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 面积 / The area.
    /// </summary>
    /// <remarks>
    /// `Width.Multiply(Height)` 同时**乘数值与单位**，所以结果是真正的面积量（m²），**不是把两个长度数值乘起来了事**。
    ///
    /// `Width.Multiply(Height)` multiplies **both values and units**, so the result is a genuine area quantity (m²), **not two length numbers multiplied together**.
    /// </remarks>
    public Quantity<V> Area => Width.Multiply(Height);

    /// <summary>右边界 x 坐标 / The right edge x coordinate.</summary>
    /// <returns>右边界或失败 / The right edge or a failure.</returns>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxX() => X.Add(Width);

    /// <summary>上边界 y 坐标 / The top edge y coordinate.</summary>
    /// <returns>上边界或失败 / The top edge or a failure.</returns>
    public Result<Quantity<V>, ErrorCode, Error<ErrorCode>> MaxY() => Y.Add(Height);

    /// <summary>
    /// 是否退化（宽或高非正） / Whether the region is degenerate (non-positive width or height).
    /// </summary>
    /// <returns>是否退化 / Whether it is degenerate.</returns>
    /// <remarks>
    /// **零宽与负宽同样退化**：负宽的区域在几何上不存在，放它进去会让后续所有重叠判定失去意义。
    ///
    /// **Zero and negative widths are equally degenerate**: a negative-width region does not exist geometrically, and letting one in makes every later overlap test
    /// meaningless.
    /// </remarks>
    public bool IsDegenerate() =>
        Width.Value.ToFlt64().ToDouble() <= 0.0 || Height.Value.ToFlt64().ToDouble() <= 0.0;

    /// <summary>
    /// 是否完全包含另一个区域 / Whether this region fully contains another region.
    /// </summary>
    /// <param name="rhs">另一个区域 / The other region.</param>
    /// <returns>是否包含或失败 / Whether it contains it, or a failure.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> Contains(Region2<V> rhs) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ownRight = MaxX();
        if (ownRight is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> ownRightOk) {
            return Propagate<bool, Quantity<V>>(ownRight);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ownTop = MaxY();
        if (ownTop is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> ownTopOk) {
            return Propagate<bool, Quantity<V>>(ownTop);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> otherRight = rhs.MaxX();
        if (otherRight is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> otherRightOk) {
            return Propagate<bool, Quantity<V>>(otherRight);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> otherTop = rhs.MaxY();
        if (otherTop is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> otherTopOk) {
            return Propagate<bool, Quantity<V>>(otherTop);
        }

        // **单位不可比时一律"不包含"**：判不了就不说包含，否则会把一个越界的放置放进料域。
        // **An incomparable unit always yields "does not contain"**: when it cannot be judged, do not claim containment, or an out-of-bounds placement enters the region.
        return Results.Ok(
            X.LessThanOrEqual(rhs.X) is true
            && Y.LessThanOrEqual(rhs.Y) is true
            && ownRightOk.Value.GreaterThanOrEqual(otherRightOk.Value) is true
            && ownTopOk.Value.GreaterThanOrEqual(otherTopOk.Value) is true);
    }

    /// <summary>
    /// 是否完全包含一个几何包围盒 / Whether this region fully contains a geometric box.
    /// </summary>
    /// <param name="box">包围盒 / The geometric box.</param>
    /// <returns>是否包含或失败 / Whether it contains it, or a failure.</returns>
    /// <remarks>
    /// 放置的越界判定用的是**包围盒**而不是区域，所以这里做一次转换再复用同一套比较——
    /// **不要为"盒"另写一份包含判定**，两份判定的边界条件迟早会分家。
    ///
    /// Out-of-bounds placement checks work on a **box** rather than a region, so this converts once and reuses the same comparisons; **do not write a second containment
    /// test for boxes** — the two boundary rules eventually drift apart.
    /// </remarks>
    public Result<bool, ErrorCode, Error<ErrorCode>> Contains(Box2Geometry<V> box) =>
        Contains(new Region2<V>(box.X, box.Y, box.Width, box.Height));

    /// <summary>
    /// 按刀线把区域切成两块，并扣除刀缝 / Split the region into two by a cut line, removing the kerf.
    /// </summary>
    /// <param name="orientation">刀线方向 / The cut orientation.</param>
    /// <param name="position">刀缝起始坐标 / The coordinate where the kerf strip starts.</param>
    /// <param name="kerf">刀缝宽度 / The kerf width.</param>
    /// <returns>两块区域，顺序为「左/下」与「右/上」，或失败 / The two regions, ordered left/bottom then right/top, or a failure.</returns>
    /// <remarks>
    /// **刀缝是被切掉的一条窄带**：竖刀占 `[position, position + kerf)`，所以右块的起点是 `position + kerf` 而不是 `position`。
    /// 少了这一步，**两块料会重叠一条刀缝宽**，而它们在业务上明明是分开的两块。
    ///
    /// **非正的刀缝按零处理**：负刀缝会让右块从位置左侧开始，也就是**两块重叠**。
    ///
    /// **The kerf is a removed narrow strip**: a vertical cut occupies `[position, position + kerf)`, so the right region starts at `position + kerf`, not `position`.
    /// Dropping that step makes **the two regions overlap by one kerf width** although they are clearly separate pieces in the domain. **A non-positive kerf is treated as
    /// zero**: a negative kerf would start the right region before the position, i.e. **the two regions overlap**.
    /// </remarks>
    public Result<(Region2<V> Left, Region2<V> Right), ErrorCode, Error<ErrorCode>> Cut(
        CutOrientation2 orientation,
        Quantity<V> position,
        Quantity<V> kerf) {
        V zero = Width.Value.Constants.Zero;
        Quantity<V> effectiveKerf = kerf.Value.ToFlt64().ToDouble() > 0.0
            ? kerf
            : new Quantity<V>(zero, Width.Unit);

        return orientation switch {
            CutOrientation2.Vertical => CutVertical(position, effectiveKerf),
            _ => CutHorizontal(position, effectiveKerf),
        };
    }

    /// <summary>竖刀切分 / A vertical cut.</summary>
    /// <param name="position">刀缝起始坐标 / The coordinate where the kerf strip starts.</param>
    /// <param name="kerf">刀缝宽度 / The kerf width.</param>
    /// <returns>两块区域或失败 / The two regions or a failure.</returns>
    private Result<(Region2<V> Left, Region2<V> Right), ErrorCode, Error<ErrorCode>> CutVertical(
        Quantity<V> position,
        Quantity<V> kerf) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> upper = position.Add(kerf);
        if (upper is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> upperOk) {
            return Propagate<(Region2<V>, Region2<V>), Quantity<V>>(upper);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> right = MaxX();
        if (right is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> rightOk) {
            return Propagate<(Region2<V>, Region2<V>), Quantity<V>>(right);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> leftWidth = position.Subtract(X);
        if (leftWidth is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> leftWidthOk) {
            return Propagate<(Region2<V>, Region2<V>), Quantity<V>>(leftWidth);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> rightWidth = rightOk.Value.Subtract(upperOk.Value);
        if (rightWidth is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> rightWidthOk) {
            return Propagate<(Region2<V>, Region2<V>), Quantity<V>>(rightWidth);
        }
        if (leftWidthOk.Value.Value.ToFlt64().ToDouble() <= 0.0
            || rightWidthOk.Value.Value.ToFlt64().ToDouble() <= 0.0) {
            return InvalidCut(position);
        }

        return Results.Ok((
            new Region2<V>(X, Y, leftWidthOk.Value, Height),
            new Region2<V>(upperOk.Value, Y, rightWidthOk.Value, Height)));
    }

    /// <summary>横刀切分 / A horizontal cut.</summary>
    /// <param name="position">刀缝起始坐标 / The coordinate where the kerf strip starts.</param>
    /// <param name="kerf">刀缝宽度 / The kerf width.</param>
    /// <returns>两块区域或失败 / The two regions or a failure.</returns>
    private Result<(Region2<V> Left, Region2<V> Right), ErrorCode, Error<ErrorCode>> CutHorizontal(
        Quantity<V> position,
        Quantity<V> kerf) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> upper = position.Add(kerf);
        if (upper is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> upperOk) {
            return Propagate<(Region2<V>, Region2<V>), Quantity<V>>(upper);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> top = MaxY();
        if (top is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> topOk) {
            return Propagate<(Region2<V>, Region2<V>), Quantity<V>>(top);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> lowerHeight = position.Subtract(Y);
        if (lowerHeight is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> lowerHeightOk) {
            return Propagate<(Region2<V>, Region2<V>), Quantity<V>>(lowerHeight);
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> upperHeight = topOk.Value.Subtract(upperOk.Value);
        if (upperHeight is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> upperHeightOk) {
            return Propagate<(Region2<V>, Region2<V>), Quantity<V>>(upperHeight);
        }
        if (lowerHeightOk.Value.Value.ToFlt64().ToDouble() <= 0.0
            || upperHeightOk.Value.Value.ToFlt64().ToDouble() <= 0.0) {
            return InvalidCut(position);
        }

        return Results.Ok((
            new Region2<V>(X, Y, Width, lowerHeightOk.Value),
            new Region2<V>(X, upperOk.Value, Width, upperHeightOk.Value)));
    }

    /// <summary>构造"刀线位置非法"的失败 / Build the "invalid cut position" failure.</summary>
    /// <param name="position">刀缝起始坐标 / The coordinate where the kerf strip starts.</param>
    /// <returns>失败 / The failure.</returns>
    private static Result<(Region2<V>, Region2<V>), ErrorCode, Error<ErrorCode>> InvalidCut(Quantity<V> position) =>
        Results.Failed<(Region2<V>, Region2<V>)>(new Csp2dValidationError(
            "刀线位置非法，切不出两块非退化区域 / Invalid cut position, cannot yield two non-degenerate regions: "
            + position));

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<S, ErrorCode, Error<ErrorCode>> Propagate<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<S>(new Err<ErrorCode>(ErrorCode.ApplicationError, "unreachable")),
        };
}

/// <summary>由坐标与尺寸构造裁切区域 / Build a cutting region from a corner and a size.</summary>
public static class Region2Factory {
    /// <summary>构造裁切区域 / Build a cutting region.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="x">左下角 x 坐标 / The lower-left x coordinate.</param>
    /// <param name="y">左下角 y 坐标 / The lower-left y coordinate.</param>
    /// <param name="width">宽度 / The width.</param>
    /// <param name="height">高度 / The height.</param>
    /// <returns>裁切区域 / The cutting region.</returns>
    public static Region2<V> Region2Of<V>(
        Quantity<V> x,
        Quantity<V> y,
        Quantity<V> width,
        Quantity<V> height)
        where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V> =>
        new(x, y, width, height);
}
