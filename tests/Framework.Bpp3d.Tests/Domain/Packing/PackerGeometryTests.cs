// 装箱器几何与装载顺序测试 / Tests for the packer's geometry and loading order.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

using InfraItem = Fuookami.Ospf.Framework.Bpp3d.Infra.Item;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain.Packing;

/// <summary>
/// 装箱器的几何与装载顺序 / The packer's geometry and loading order.
/// </summary>
public sealed class PackerGeometryTests {
    private static Quantity<FltX> M(double value) =>
        new(new FltX(value), Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter);

    private static IPackingShape3<FltX> Cuboid(double width, double height, double depth) =>
        new CuboidPackingShape3<FltX>(
            new Fuookami.Ospf.Quantities.Geometry.QuantityCuboid3<FltX>(
                M(width), M(height), M(depth)));

    private static PackingResult Unwrap(
        Result<PackingResult, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<PackingResult, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("装箱必须成功 / the packing must succeed: " + result);

    private static Bin<BinLayer, FltX> MakeBin(params (InfraItem Item, double X)[] placements) {
        var layer = new BinLayer {
            Shape = new Container3Shape<FltX>(M(10), M(10), M(10)),
            Units = placements
                .Select(placement => new QuantityPlacement3<InfraItem, FltX>(
                    placement.Item,
                    new QuantityPoint3<FltX>(M(placement.X), M(0), M(0))))
                .ToList(),
        };
        var binType = new BinType<FltX>("B1", M(10), M(10), M(10), M(1000));
        return new Bin<BinLayer, FltX>(binType, new[] {
            new QuantityPlacement3<BinLayer, FltX>(layer, new QuantityPoint3<FltX>(M(0), M(0), M(0))),
        });
    }

    /// <summary>**装箱结果保留每个物品的包围盒与装载顺序** / The packing result keeps each item's bounding box and loading order.</summary>
    /// <remarks>
    /// 修复前 `Packer` **把位置和顺序整个丢掉**：`PackingItem` 只有物品与数量，`Geometry` 恒为 `null`、
    /// `LoadingOrder` 恒为 0。**"装箱成功"在两种实现下都成立**，所以只有逐项断言才分得出来。
    ///
    /// Before the fix the `Packer` **dropped position and order outright**: `PackingItem` held only the item and amount, `Geometry` was always `null`
    /// and `LoadingOrder` was always 0. **"The pack succeeded" holds under both implementations**, so only per-item assertions can tell them apart.
    /// </remarks>
    [Fact]
    public async Task Packer_ShouldKeepGeometryAndLoadingOrder() {
        Packer packer = new();
        Bin<BinLayer, FltX> bin = MakeBin(
            (new InfraItem("I1", "Item 1", Cuboid(2, 3, 4)), 0.0),
            (new InfraItem("I2", "Item 2", Cuboid(2, 3, 4)), 5.0));

        PackingResult result = Unwrap(await packer.InvokeAsync(new[] { bin }));

        IReadOnlyList<PackingItem> items = result.Aggregation.Bins[0].Items;
        items.Should().HaveCount(2);
        items[0].LoadingOrder.Should().Be(0);
        items[1].LoadingOrder.Should().Be(1);
        items[0].Geometry.Should().NotBeNull();
        PackingItemGeometry first = items[0].Geometry!;
        first.MinX.Should().Be(0.0);
        first.MaxX.Should().Be(2.0);
        first.MaxY.Should().Be(3.0);
        first.MaxZ.Should().Be(4.0);
        PackingItemGeometry second = items[1].Geometry!;
        second.MinX.Should().Be(5.0);
        second.MaxX.Should().Be(7.0);
    }

    /// <summary>**装箱结果保留箱子身份与容器尺寸** / The packing result keeps the bin identity and container extents.</summary>
    /// <remarks>
    /// 越界检测要靠 `Type` 才知道"箱子多大"。丢掉它，越界检测就只剩"缺箱子类型"这一种结果。
    ///
    /// The outside-bin check needs `Type` to know how large the container is. Drop it and the check has only one possible outcome: "missing bin type".
    /// </remarks>
    [Fact]
    public async Task Packer_ShouldKeepTheBinIdentity() {
        Packer packer = new();
        Bin<BinLayer, FltX> bin = MakeBin((new InfraItem("I1", "Item 1", Cuboid(2, 3, 4)), 0.0));

        PackingResult result = Unwrap(await packer.InvokeAsync(new[] { bin }));

        PackingBin packed = result.Aggregation.Bins[0];
        packed.Name.Should().Be("B1");
        packed.Type.Should().NotBeNull();
        packed.Type!.Width.ToDouble().Should().Be(10.0);
        packed.Type.Height.ToDouble().Should().Be(10.0);
        packed.Type.Depth.ToDouble().Should().Be(10.0);
    }

    /// <summary>**没有形状的货物得到 `null` 几何，而不是零尺寸的假几何** / A shapeless item gets `null` geometry, not a zero-sized fake.</summary>
    /// <remarks>
    /// 零尺寸包围盒会被重叠判据当成"什么都不占"，**于是它和任何东西都不重叠**——这比缺几何更难发现。
    ///
    /// A zero-sized box is treated by the overlap test as occupying nothing, **so it overlaps nothing** — harder to notice than missing geometry.
    /// </remarks>
    [Fact]
    public async Task Packer_ShouldLeaveShapelessItemsWithoutGeometry() {
        Packer packer = new();
        Bin<BinLayer, FltX> bin = MakeBin((new InfraItem("I1", "Item 1"), 0.0));

        PackingResult result = Unwrap(await packer.InvokeAsync(new[] { bin }));

        result.Aggregation.Bins[0].Items[0].Geometry.Should().BeNull();
    }

    /// <summary>**装箱器的输出能通过几何守卫（物品互不重叠时）** / The packer's output passes the geometry guard when the items are disjoint.</summary>
    [Fact]
    public async Task PackerOutput_ShouldPassTheGeometryGuard() {
        Packer packer = new();
        Bin<BinLayer, FltX> bin = MakeBin(
            (new InfraItem("I1", "Item 1", Cuboid(2, 3, 4)), 0.0),
            (new InfraItem("I2", "Item 2", Cuboid(2, 3, 4)), 5.0));

        PackingResult result = Unwrap(await packer.InvokeAsync(new[] { bin }));

        PackingGeometryGuard.RequirePackedBinShapeGeometry(
            result.Aggregation.Bins[0], "packer-test").IsOk.Should().BeTrue();
    }

    /// <summary>**同一位置的两个物品被几何守卫拒绝** / Two items at the same position are rejected by the geometry guard.</summary>
    /// <remarks>
    /// 这条是端到端的证据链：它同时要求 `Packer` **留下了几何**、`PackingItemGeometry.From` **算对了**、
    /// 守卫**真的比较了**。三者缺一，这项断言就会失败。
    ///
    /// This is the end-to-end evidence: it requires the `Packer` to **keep the geometry**, `PackingItemGeometry.From` to **compute it correctly** and the
    /// guard to **actually compare**. Remove any one of the three and this assertion fails.
    /// </remarks>
    [Fact]
    public async Task PackerOutput_ShouldBeRejectedWhenTwoItemsShareAPosition() {
        Packer packer = new();
        Bin<BinLayer, FltX> bin = MakeBin(
            (new InfraItem("I1", "Item 1", Cuboid(2, 3, 4)), 0.0),
            (new InfraItem("I2", "Item 2", Cuboid(2, 3, 4)), 1.0));

        PackingResult result = Unwrap(await packer.InvokeAsync(new[] { bin }));

        PackingGeometryGuard.RequirePackedBinShapeGeometry(
            result.Aggregation.Bins[0], "packer-test").IsOk.Should().BeFalse();
    }

    /// <summary>**放置通过货物自己的形状解析，而不是返回占位对象** / A placement resolves its real shape instead of a placeholder.</summary>
    /// <remarks>
    /// 原先 `ResolvedPackingShape` 返回 `new { Axis = (Axis3?)null }` —— 一个既无尺寸也无半径的匿名对象。
    /// **任何按它算包围盒的代码都只能得到零尺寸**，而零尺寸看起来和"没有体积"完全一样。
    ///
    /// `ResolvedPackingShape` used to return `new { Axis = (Axis3?)null }` — an anonymous object with neither extents nor a radius. **Any bounding box
    /// derived from it could only be zero-sized**, and a zero-sized box looks exactly like "has no volume".
    /// </remarks>
    [Fact]
    public void ResolvedPackingShape_ShouldComeFromTheItem() {
        var item = new InfraItem("I1", "Item 1", Cuboid(2, 3, 4));
        var placement = new QuantityPlacement3<InfraItem, FltX>(
            item, new QuantityPoint3<FltX>(M(0), M(0), M(0)));

        IPackingShape3<FltX>? shape = placement.ResolvedPackingShape();

        shape.Should().NotBeNull();
        shape!.BoundingWidth.ToDouble().Should().Be(2.0);
        shape.BoundingHeight.ToDouble().Should().Be(3.0);
        shape.BoundingDepth.ToDouble().Should().Be(4.0);

        var shapeless = new QuantityPlacement3<InfraItem, FltX>(
            new InfraItem("I2", "Item 2"), new QuantityPoint3<FltX>(M(0), M(0), M(0)));
        shapeless.ResolvedPackingShape().Should().BeNull();
    }
}
