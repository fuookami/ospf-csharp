// 裁切区域判据 / Criteria for the cutting region.

using FluentAssertions;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Infrastructure;

/// <summary>
/// **刀缝、退化与包含** / Kerf, degeneracy and containment.
/// </summary>
/// <remarks>
/// 对应。**裁切与装箱的差别就在这个类型**——装箱只关心放在哪里，套裁还关心每一刀切成哪两块。
///
///. **This type is where cutting stock differs from bin packing**: packing only cares where something sits, cutting stock also cares
/// which two pieces each cut produces.
/// </remarks>
public class Region2Tests {
    /// <summary>**面积是真正的面积量** / The area is a genuine area quantity.</summary>
    /// <remarks>
    /// `Multiply` 同时乘数值与单位，所以 2 m × 3 m = 6 m²。**只乘数值会让单位停留在米**，
    /// 于是"面积"与"长度"在数值层混为一谈。
    ///
    /// `Multiply` multiplies both value and unit, so 2 m × 3 m = 6 m². **Multiplying values only would leave the unit as metres**, conflating "area" and "length" at the
    /// numeric level.
    /// </remarks>
    [Fact]
    public void Area_ShouldCarryTheSquaredUnit() {
        Region2<Flt64> region = Region(Metres(2.0), Metres(3.0));

        region.Area.Value.ToDouble().Should().BeApproximately(6.0, 1e-12);
        // 单位对象是派生的新实例，**比的是量纲而不是引用**。
        // The unit object is a freshly derived instance: **compare the dimension, not the reference**.
        region.Area.Unit.Quantity.Should().Be(AreaUnits.SquareMeter.Quantity);
    }

    /// <summary>**零宽与负宽同样退化** / Zero and negative widths are equally degenerate.</summary>
    [Fact]
    public void IsDegenerate_ShouldRejectNonPositiveExtents() {
        Region(Metres(0.0), Metres(1.0)).IsDegenerate().Should().BeTrue();
        Region(Metres(-1.0), Metres(1.0)).IsDegenerate().Should().BeTrue();
        Region(Metres(1.0), Metres(0.0)).IsDegenerate().Should().BeTrue();
        Region(Metres(1.0), Metres(1.0)).IsDegenerate().Should().BeFalse();
    }

    /// <summary>**竖刀把刀缝切掉，两块不重叠** / A vertical cut removes the kerf so the two regions do not overlap.</summary>
    /// <remarks>
    /// 右块的起点是 `position + kerf` 而不是 `position`。少了这一步，**两块料会重叠一条刀缝宽**，
    /// 而它们在业务上明明是分开的两块。
    ///
    /// The right region starts at `position + kerf`, not `position`. Dropping that step makes **the two regions overlap by one kerf width** although they are clearly
    /// separate pieces in the domain.
    /// </remarks>
    [Fact]
    public void Cut_ShouldRemoveTheKerfFromTheRightRegion() {
        Region2<Flt64> region = Region(Metres(4.0), Metres(2.0));

        Result<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode,
            Error<ErrorCode>> cut =
            region.Cut(CutOrientation2.Vertical, Metres(2.0), Metres(0.5));

        cut.IsOk.Should().BeTrue(cut.ToString());
        (Region2<Flt64> left, Region2<Flt64> right) =
            ((Ok<(Region2<Flt64>, Region2<Flt64>), ErrorCode,
                Error<ErrorCode>>)cut).Value;

        left.Width.Value.ToDouble().Should().BeApproximately(2.0, 1e-12);
        right.X.Value.ToDouble().Should().BeApproximately(2.5, 1e-12, "the right region starts after the kerf");
        right.Width.Value.ToDouble().Should().BeApproximately(1.5, 1e-12);
        left.MaxX().IsOk.Should().BeTrue();
        left.MaxX().ValueOrThrow().Value.ToDouble().Should().BeApproximately(2.0, 1e-12);
    }

    /// <summary>**非正刀缝按零处理** / A non-positive kerf is treated as zero.</summary>
    /// <remarks>
    /// 负刀缝会让右块从位置左侧开始，也就是**两块重叠**。
    ///
    /// A negative kerf would start the right region before the position, i.e. **the two regions overlap**.
    /// </remarks>
    [Fact]
    public void Cut_ShouldTreatANegativeKerfAsZero() {
        Region2<Flt64> region = Region(Metres(4.0), Metres(2.0));

        Result<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode,
            Error<ErrorCode>> cut =
            region.Cut(CutOrientation2.Vertical, Metres(2.0), Metres(-0.5));

        cut.IsOk.Should().BeTrue(cut.ToString());
        (Region2<Flt64> left, Region2<Flt64> right) =
            ((Ok<(Region2<Flt64>, Region2<Flt64>), ErrorCode,
                Error<ErrorCode>>)cut).Value;

        right.X.Value.ToDouble().Should().BeApproximately(2.0, 1e-12, "a negative kerf must not shift the cut");
        left.Width.Value.ToDouble().Should().BeApproximately(2.0, 1e-12);
    }

    /// <summary>**切不出两块非退化区域时必须失败** / A cut that cannot yield two non-degenerate regions must fail.</summary>
    [Fact]
    public void Cut_ShouldRejectAPositionThatCollapsesARegion() {
        Region2<Flt64> region = Region(Metres(2.0), Metres(1.0));

        Result<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode,
            Error<ErrorCode>> cut =
            region.Cut(CutOrientation2.Vertical, Metres(0.0), Metres(0.0));

        cut.IsFailed.Should().BeTrue("the left region would be zero-wide");
        ((Failed<(Region2<Flt64>, Region2<Flt64>), ErrorCode,
            Error<ErrorCode>>)cut).Error
            .Should().BeOfType<Csp2dValidationError>("the semantic subtype identifies an input problem");
    }

    /// <summary>**包含判定在单位不可比时一律不成立** / Containment never holds when the units are incomparable.</summary>
    [Fact]
    public void Contains_ShouldRejectIncomparableUnitsAndOutsideRegions() {
        Region2<Flt64> outer = Region(Metres(4.0), Metres(3.0));

        outer.Contains(Region(Metres(2.0), Metres(2.0))).ValueOrThrow().Should().BeTrue();
        outer.Contains(Region2Of(Metres(5.0), Metres(0.0), Metres(1.0), Metres(1.0)))
            .ValueOrThrow().Should().BeFalse("the other region starts beyond the right edge");
        outer.Contains(Region2Of(Kilograms(1.0), Kilograms(1.0), Kilograms(1.0), Kilograms(1.0)))
            .ValueOrThrow().Should().BeFalse("length and mass are not comparable");
    }

    /// <summary>以原点构造一个区域 / Build a region anchored at the origin.</summary>
    /// <param name="width">宽度 / The width.</param>
    /// <param name="height">高度 / The height.</param>
    /// <returns>区域 / The region.</returns>
    private static Region2<Flt64> Region(Quantity<Flt64> width, Quantity<Flt64> height) =>
        Region2Of(Metres(0.0), Metres(0.0), width, height);

    /// <summary>按左下角构造区域 / Build a region from its lower-left corner.</summary>
    /// <param name="x">左下角 x / The lower-left x.</param>
    /// <param name="y">左下角 y / The lower-left y.</param>
    /// <param name="width">宽度 / The width.</param>
    /// <param name="height">高度 / The height.</param>
    /// <returns>区域 / The region.</returns>
    private static Region2<Flt64> Region2Of(
        Quantity<Flt64> x,
        Quantity<Flt64> y,
        Quantity<Flt64> width,
        Quantity<Flt64> height) =>
        Region2Factory.Region2Of(x, y, width, height);

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) => new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造千克量 / Build a mass in kilograms.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) => new(new Flt64(value), SIBaseUnits.Kilogram);
}
