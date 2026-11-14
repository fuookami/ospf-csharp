// 物品域：放置工厂与物品合并测试 / Tests for the placement factory and the item merger.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 放置工厂与物品合并 / Placement factory and item merger.
/// </summary>
public sealed class PlacementFactoryTest {
    /// <summary>未旋转时使用原形状 / An unrotated placement keeps the original shape.</summary>
    [Fact]
    public void ItemPlacement_ShouldKeepShape() {
        Placement2<FltX> placement = PlacementFactory
            .ItemPlacement2Of(Item("a", 2, 4, allowRotate: true), Point(1, 1)).Value;

        placement.Shape.BoundingWidth.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
        placement.Rotated.Should().BeFalse();
        placement.ItemId.Should().Be("a");
    }

    /// <summary>旋转时宽高互换 / Rotation swaps width and height.</summary>
    [Fact]
    public void ItemPlacement_ShouldSwapOnRotate() {
        Placement2<FltX> placement = PlacementFactory
            .ItemPlacement2Of(Item("a", 2, 4, allowRotate: true), Point(0, 0), rotated: true).Value;

        placement.Shape.BoundingWidth.Value.ToFlt64().ToDouble().Should().BeApproximately(4.0, 1e-9);
        placement.Shape.BoundingHeight.Value.ToFlt64().ToDouble().Should().BeApproximately(2.0, 1e-9);
        placement.Rotated.Should().BeTrue();
    }

    /// <summary>
    /// **未许可旋转时失败而不是静默忽略**：静默忽略会让调用方拿到未旋转的放置却没有任何报错。
    /// **Rotation without permission fails instead of being silently ignored**: ignoring it would hand the caller an
    /// unrotated placement with no error.
    /// </summary>
    [Fact]
    public void ItemPlacement_ShouldFailWhenRotationNotAllowed() {
        Result<Placement2<FltX>, Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            PlacementFactory.ItemPlacement2Of(
                Item("a", 2, 4, allowRotate: false), Point(0, 0), rotated: true);

        result.IsOk.Should().BeFalse();
        ((Failed<Placement2<FltX>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("旋转");
    }

    /// <summary>形状放置不校验旋转许可 / The shape factory does not check rotation permission.</summary>
    [Fact]
    public void ShapePlacement_ShouldNotCheckRotation() {
        Placement2<FltX> placement = PlacementFactory.ShapePlacement2Of(
            new RectanglePackingShape2<FltX>(Meters(2), Meters(4)),
            Point(0, 0),
            rotated: true);

        placement.Rotated.Should().BeTrue("形状本身不带许可信息 / a bare shape carries no permission");
        placement.ItemId.Should().BeNull();
    }

    /// <summary>图样放置共享形状与旋转状态，只改落点 / A pattern placement shares shape and rotation, changing only position.</summary>
    [Fact]
    public void PlacedAt_ShouldKeepShapeAndRotation() {
        Placement2<FltX> template = PlacementFactory.ItemPlacement2Of(
            Item("a", 2, 4, allowRotate: true), Point(0, 0), rotated: true).Value;

        Placement2<FltX> moved = PlacementFactory.PlacedAt(template, Point(5, 5));

        moved.Position.X.Value.ToFlt64().ToDouble().Should().BeApproximately(5.0, 1e-9);
        moved.Rotated.Should().BeTrue();
        moved.ItemId.Should().Be("a");
        moved.Shape.Should().Be(template.Shape, "形状是共享引用 / the shape is shared");
    }

    /// <summary>原点落点的两轴都为零且沿用样本单位 / The origin position is zero on both axes in the sample's unit.</summary>
    [Fact]
    public void OriginPoint_ShouldBeZero() {
        QuantityPoint2<FltX> origin = PlacementFactory.OriginPoint2Of(Meters(3));

        origin.X.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
        origin.Y.Value.ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
        origin.X.Unit.Should().Be(SIBaseUnits.Meter);
    }

    /// <summary>同形状同批次同许可的物品合并件数 / Identically shaped items with the same batch and permission merge amounts.</summary>
    [Fact]
    public void Merge_ShouldAccumulateAmounts() {
        IReadOnlyList<(ActualItem<FltX> Item, ulong Amount)> merged = new ItemMerger<FltX>().Merge(
            new[] { (Item("a", 2, 2, allowRotate: false), 3UL), (Item("b", 2, 2, allowRotate: false), 5UL) });

        merged.Should().ContainSingle();
        merged[0].Amount.Should().Be(8UL);
        merged[0].Item.Id.Value.Should().Be("a", "代表物品取首次出现 / the representative is the first occurrence");
    }

    /// <summary>尺寸不同的物品不合并 / Items with different dimensions stay separate.</summary>
    [Fact]
    public void Merge_ShouldKeepDifferentShapesApart() {
        IReadOnlyList<(ActualItem<FltX> Item, ulong Amount)> merged = new ItemMerger<FltX>().Merge(
            new[] { (Item("a", 2, 2, allowRotate: false), 3UL), (Item("b", 3, 2, allowRotate: false), 5UL) });

        merged.Should().HaveCount(2);
    }

    /// <summary>
    /// **旋转许可不同的同形状物品不合并**：合并会丢掉"其中一批不许旋转"这个约束，随后排布出现不该有的旋转件。
    /// **Identically shaped items differing in rotation permission stay apart**: merging would drop the "one batch may not
    /// rotate" constraint and later produce rotations that should not happen.
    /// </summary>
    [Fact]
    public void Merge_ShouldRespectRotationPermission() {
        IReadOnlyList<(ActualItem<FltX> Item, ulong Amount)> merged = new ItemMerger<FltX>().Merge(
            new[] { (Item("a", 2, 2, allowRotate: true), 3UL), (Item("b", 2, 2, allowRotate: false), 5UL) });

        merged.Should().HaveCount(2);
    }

    /// <summary>批次号不同的物品不合并 / Items in different batches stay apart.</summary>
    [Fact]
    public void Merge_ShouldRespectBatchNo() {
        IReadOnlyList<(ActualItem<FltX> Item, ulong Amount)> merged = new ItemMerger<FltX>().Merge(
            new[] { (BatchItem("a", 2, 2, "B1"), 3UL), (BatchItem("b", 2, 2, "B2"), 5UL) });

        merged.Should().HaveCount(2);
    }

    /// <summary>
    /// **矩形与圆即使包围尺寸相同也不合并**：签名把类型名放在首位，正是为此。
    /// **A rectangle and a circle never merge even with equal bounding size**: that is why the kind leads the signature.
    /// </summary>
    [Fact]
    public void Merge_ShouldKeepShapeKindsApart() {
        IReadOnlyList<(ActualItem<FltX> Item, ulong Amount)> merged = new ItemMerger<FltX>().Merge(
            new[] {
                (Item("a", 4, 4, allowRotate: false), 3UL),
                (Circle("c", 4), 5UL),
            });

        merged.Should().HaveCount(2);
    }

    /// <summary>合并保持首次出现顺序 / Merging preserves first-occurrence order.</summary>
    [Fact]
    public void Merge_ShouldPreserveOrder() {
        IReadOnlyList<(ActualItem<FltX> Item, ulong Amount)> merged = new ItemMerger<FltX>().Merge(
            new[] {
                (Item("a", 2, 2, allowRotate: false), 1UL),
                (Item("b", 3, 3, allowRotate: false), 1UL),
                (Item("c", 2, 2, allowRotate: false), 1UL),
            });

        merged.Select(entry => entry.Item.Id.Value).Should().Equal(new[] { "a", "b" });
    }

    /// <summary>空输入合并为空 / An empty input merges to nothing.</summary>
    [Fact]
    public void Merge_ShouldHandleEmpty() =>
        new ItemMerger<FltX>()
            .Merge(Array.Empty<(ActualItem<FltX>, ulong)>())
            .Should().BeEmpty();

    /// <summary>按形状分组并保持首次出现顺序 / Grouping by shape preserves first-occurrence order.</summary>
    [Fact]
    public void GroupByShape_ShouldGroupAndPreserveOrder() {
        IReadOnlyList<IReadOnlyList<ActualItem<FltX>>> groups = new ItemMerger<FltX>().GroupByShape(new[] {
            Item("a", 2, 2, allowRotate: false),
            Item("b", 3, 3, allowRotate: false),
            Item("c", 2, 2, allowRotate: false),
        });

        groups.Should().HaveCount(2);
        groups[0].Select(item => item.Id.Value).Should().Equal(new[] { "a", "c" });
        groups[1].Select(item => item.Id.Value).Should().Equal(new[] { "b" });
    }

    /// <summary>
    /// **分组不会把同形状物品拆到不同组**：这正是改用结构签名而不是形状哈希的理由。
    /// **Grouping never splits identically shaped items**: this is exactly why the structural signature replaced the shape
    /// hash.
    /// </summary>
    [Fact]
    public void GroupByShape_ShouldNotSplitEqualShapes() {
        IReadOnlyList<IReadOnlyList<ActualItem<FltX>>> groups = new ItemMerger<FltX>().GroupByShape(
            Enumerable.Range(0, 5).Select(index => Item($"i{index}", 2, 2, allowRotate: false)).ToList());

        groups.Should().ContainSingle();
        groups[0].Should().HaveCount(5);
    }

    /// <summary>构造矩形件 / Build a rectangle item.</summary>
    private static ActualItem<FltX> Item(string id, double w, double h, bool allowRotate) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Meters(w), Meters(h)), allowRotate, Meters(0));

    /// <summary>构造带批次的矩形件 / Build a batched rectangle item.</summary>
    private static ActualItem<FltX> BatchItem(string id, double w, double h, string batchNo) =>
        new(
            new ItemId(id), id,
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            false, Meters(0), BatchNo: new BatchNo(batchNo));

    /// <summary>构造圆形件（入参为直径，折半为半径）/ Build a circle item (diameter argument, halved to radius).</summary>
    private static ActualItem<FltX> Circle(string id, double diameter) =>
        new(new ItemId(id), id, new CirclePackingShape2<FltX>(Meters(diameter / 2.0)), true, Meters(0));

    /// <summary>构造落点 / Build a position.</summary>
    private static QuantityPoint2<FltX> Point(double x, double y) =>
        new(Meters(x), Meters(y));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
