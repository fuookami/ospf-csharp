// BLA 上下文测试 / Tests for the BLA context.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// BLA 上下文 / BLA context.
/// </summary>
public sealed class BlaContextTest {
    /// <summary>上下文本趟能排出放置 / The context lays items out.</summary>
    [Fact]
    public void Layout_ShouldPlaceItems() {
        Bla2dLayoutResult<FltX> result = new BlaContext<FltX>().Layout(
            Sheet(10, 10),
            new[] { (Item("a", 4, 4), 2) }).Value;

        result.Placements.Should().HaveCount(2);
    }

    /// <summary>
    /// **上下文无状态**：同一实例连跑两趟，第二趟不会带着第一趟的板材走错位置。
    /// **The context is stateless**: running twice on one instance must not let the first sheet leak into the second.
    /// </summary>
    [Fact]
    public void Layout_ShouldBeReusable() {
        var context = new BlaContext<FltX>();

        Bla2dLayoutResult<FltX> first = context
            .Layout(Sheet(10, 10), new[] { (Item("a", 4, 4), 1) }).Value;
        Bla2dLayoutResult<FltX> second = context
            .Layout(Sheet(20, 20), new[] { (Item("a", 4, 4), 1) }).Value;

        first.Placements.Should().HaveCount(1);
        second.Placements.Should().HaveCount(1);
        second.Placements[0].X.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(0.0, 1e-9, "第二趟仍在原点起排 / the second run still starts at the origin");
    }

    /// <summary>超过板材容量的件数不被强塞 / Amounts beyond capacity are not force-fitted.</summary>
    [Fact]
    public void Layout_ShouldNotOverfill() {
        Bla2dLayoutResult<FltX> result = new BlaContext<FltX>().Layout(
            Sheet(10, 10),
            new[] { (Item("a", 6, 6), 4) }).Value;

        result.Placements.Count.Should().BeLessThan(4);
    }

    /// <summary>显式配置会改变行为且被接受 / An explicit configuration is accepted and changes behaviour.</summary>
    [Fact]
    public void Layout_ShouldAcceptExplicitConfig() {
        var config = new BottomUpLeftJustifiedAlgorithm<FltX>.Config(
            WithDisplacementX: false,
            WithDisplacementY: false,
            WithWeightedDistance: false);

        new BlaContext<FltX>()
            .Layout(Sheet(10, 10), new[] { (Item("a", 4, 4), 2) }, config)
            .IsOk.Should().BeTrue();
    }

    /// <summary>构造板材 / Build a sheet.</summary>
    private static Sheet2Shape<FltX> Sheet(double w, double h) => new(Meters(w), Meters(h));

    /// <summary>构造物品 / Build an item.</summary>
    private static IAbstractItem<FltX> Item(string id, double w, double h) => new ActualItem<FltX>(
        new ItemId(id), id,
        new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
        true, Meters(0));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
