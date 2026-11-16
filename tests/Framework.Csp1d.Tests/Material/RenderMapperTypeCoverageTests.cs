// 渲染映射的类型覆盖的判据 / Criteria for the render mapper's type coverage.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Infrastructure.Dto;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;
using N = Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.Material;

/// <summary>
/// **渲染映射的类型覆盖** / The render mapper's type coverage.
/// </summary>
/// <remarks>
/// 判据的重点：**`Flt32` / `Int32` 这些原本抛 `NotSupportedException` 的类型现在能渲染**，
/// 且**宽度真的传过去**。原实现只认 `Flt64` / `FltX` / `Int64` / `UInt64` 四种。
///
/// The criteria focus on **`Flt32` / `Int32` and friends, which used to throw `NotSupportedException`, now rendering**, and on **the width really coming through**. The old implementation
/// recognised only `Flt64` / `FltX` / `Int64` / `UInt64`.
/// </remarks>
public class RenderMapperTypeCoverageTests {
    private static Product<V> ProductOf<V>(V width) where V : struct =>
        new(
            id: "P1",
            name: "part",
            width: new[] { new Quantity<V>(width, LengthUnits.Millimeter) });

    /// <summary>
    /// **十二个数值类型都能渲染出宽度** / All twelve numeric types render a width.
    /// </summary>
    /// <remarks>
    /// 两点成对：每个类型渲染出的 `Width` **等于期望值**（不是只断言"没抛"）；
    /// 且 `Flt32` 这类**原本会抛**的类型现在也在其中——那正是这里修的缺陷。
    ///
    /// Two paired points: every type's rendered `Width` **equals the expected value** rather than merely "did not throw", and the types that **used to throw**, such as `Flt32`, are among them —
    /// which is exactly the defect fixed.
    /// </remarks>
    [Fact]
    public void ToRenderDto_ShouldCoverEveryNumericType() {
        RenderCuttingPlanProductionDTO Rendered<V>(V width) where V : struct =>
            ProductOf(width).ToRenderDto(x: FltX.Zero);

        Rendered(new N.FltX(1.5m)).Width.Should().Be(new FltX(1.5m));
        Rendered(new N.Flt64(1.5)).Width.Should().Be(new FltX(1.5m));
        Rendered(new N.Flt32(1.5f)).Width.Should().Be(new FltX(1.5m));
        Rendered(new N.Int64(-7L)).Width.Should().Be(new FltX(-7m));
        Rendered(new N.Int32(-7)).Width.Should().Be(new FltX(-7m));
        Rendered(new N.Int16(-7)).Width.Should().Be(new FltX(-7m));
        Rendered(new N.Int8(-7)).Width.Should().Be(new FltX(-7m));
        Rendered(new N.UInt64(7UL)).Width.Should().Be(new FltX(7m));
        Rendered(new N.UInt32(7U)).Width.Should().Be(new FltX(7m));
        Rendered(new N.UInt16(7)).Width.Should().Be(new FltX(7m));
        Rendered(new N.UInt8(7)).Width.Should().Be(new FltX(7m));
        Rendered(new N.IntX(new System.Numerics.BigInteger(-7))).Width.Should().Be(new FltX(-7m));
        Rendered(new N.UIntX(new System.Numerics.BigInteger(7))).Width.Should().Be(new FltX(7m));
    }

    /// <summary>
    /// **没有宽度时渲染 0，而不是抛** / Rendering without a width yields zero rather than throwing.
    /// </summary>
    /// <remarks>
    /// 两点成对：空宽度表的乘积 `MaxWidth` 返回 `null`，渲染出的 `Width` 是 **`FltX.Zero`**；
    /// 且 `UnitLength` 为 **`null`**（缺值不伪装成 0，否则下游会把"未知长度"当"零长度"）。
    ///
    /// Two paired points: a product with no widths makes `MaxWidth` return `null` and the rendered `Width` is **`FltX.Zero`**; and `UnitLength` is **`null`**, since a missing value must not
    /// masquerade as zero or downstream code would read "unknown length" as "zero length".
    /// </remarks>
    [Fact]
    public void ToRenderDto_ShouldRenderZeroWidthWhenThereIsNone() {
        var product = new Product<N.Flt32>(id: "P1", name: "part", width: Array.Empty<Quantity<N.Flt32>>());

        RenderCuttingPlanProductionDTO rendered = product.ToRenderDto(x: FltX.Zero);

        rendered.Width.Should().Be(FltX.Zero);
        rendered.UnitLength.Should().BeNull();
    }

    /// <summary>
    /// **解渲染那条路也走同一个转换** / The solution-rendering path goes through the same conversion.
    /// </summary>
    /// <remarks>
    /// 两点成对：`QuantityToFltX` 对 `Flt32` / `Int32` 给出**真值**；且对 `FltX` 原样返回。
    /// 这条是"解渲染退回只认 `FltX`"那个变异的检出点——旧判据只走 `Product.ToRenderDto`，碰不到这条路。
    ///
    /// Two paired points: `QuantityToFltX` yields the **real value** for `Flt32` / `Int32`, and passes `FltX` through unchanged. This is what catches the "solution rendering falls back to
    /// recognising only `FltX`" mutation, which the older criteria missed because they went through `Product.ToRenderDto` and never touched this path.
    /// </remarks>
    [Fact]
    public void QuantityToFltX_ShouldCoverEveryNumericType() {
        Fuookami.Ospf.Framework.Csp1d.Application.Model.DefaultCsp1dSolutionAnalyzer<N.Flt32>.QuantityToFltX(
            new Quantity<N.Flt32>(new N.Flt32(1.5f), LengthUnits.Millimeter)).Should().Be(new FltX(1.5m));
        Fuookami.Ospf.Framework.Csp1d.Application.Model.DefaultCsp1dSolutionAnalyzer<N.Int32>.QuantityToFltX(
            new Quantity<N.Int32>(new N.Int32(-7), LengthUnits.Millimeter)).Should().Be(new FltX(-7m));
        Fuookami.Ospf.Framework.Csp1d.Application.Model.DefaultCsp1dSolutionAnalyzer<N.FltX>.QuantityToFltX(
            new Quantity<N.FltX>(new FltX(2.5m), LengthUnits.Millimeter)).Should().Be(new FltX(2.5m));
    }
}
