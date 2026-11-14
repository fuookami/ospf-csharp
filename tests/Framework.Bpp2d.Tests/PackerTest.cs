// 装箱器与装载顺序测试 / Tests for the packer and loading order.

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
/// 装箱器与装载顺序 / Packer and loading order.
/// </summary>
public sealed class PackerTest {
    /// <summary>
    /// **装载顺序按"先下后上、先左后右"**：这是装载机执行顺序，顺序错了会先放上层件导致无法再放。
    /// **Loading order is bottom-first then left-to-right**: that is the loader's execution order, and getting it
    /// wrong places upper items first and blocks the rest.
    /// </summary>
    [Fact]
    public void LoadingOrder_ShouldBeBottomThenLeft() {
        Placement2<FltX> bottomLeft = Placed(0, 0, 2, 2);
        Placement2<FltX> bottomRight = Placed(5, 0, 2, 2);
        Placement2<FltX> topLeft = Placed(0, 5, 2, 2);

        // 返回值与输入同序：输入为 [topLeft, bottomRight, bottomLeft]。
        // The result is aligned with the input, which is [topLeft, bottomRight, bottomLeft].
        IReadOnlyList<ulong> orders =
            LoadingOrderCalculator.Invoke(new[] { topLeft, bottomRight, bottomLeft });

        orders[2].Should().Be(1, "bottomLeft 最先装载 / bottomLeft loads first");
        orders[1].Should().Be(2, "bottomRight 其次 / then bottomRight");
        orders[0].Should().Be(3, "topLeft 最后 / topLeft last");
    }

    /// <summary>同一位置时包围面积大者优先 / At equal position the larger bounding area comes first.</summary>
    [Fact]
    public void LoadingOrder_ShouldPreferLargerAreaAtSamePosition() {
        Placement2<FltX> small = Placed(0, 0, 2, 2);
        Placement2<FltX> large = Placed(0, 0, 4, 4);

        IReadOnlyList<ulong> orders =
            LoadingOrderCalculator.Invoke(new[] { small, large });

        orders[1].Should().Be(1, "large 优先 / large first");
        orders[0].Should().Be(2);
    }

    /// <summary>装载顺序从 1 开始连续无空缺 / Loading orders start at 1 and are contiguous.</summary>
    [Fact]
    public void LoadingOrder_ShouldBeContiguousFromOne() {
        IReadOnlyList<ulong> orders = LoadingOrderCalculator.Invoke(new[] {
            Placed(0, 0, 1, 1), Placed(1, 0, 1, 1), Placed(2, 0, 1, 1),
        });

        orders.OrderBy(value => value).Should().Equal(1UL, 2UL, 3UL);
    }

    /// <summary>空放置列表得到空顺序表 / An empty placement list yields an empty order map.</summary>
    [Fact]
    public void LoadingOrder_ShouldBeEmptyForNoPlacements() =>
        LoadingOrderCalculator.Invoke(Array.Empty<Placement2<FltX>>()).Should().BeEmpty();

    /// <summary>装箱结果包含聚合、余料与附加信息 / The result carries aggregation, remnants and extra info.</summary>
    [Fact]
    public void Packer_ShouldBuildResult() {
        var context = new PackingContext(Info: new Dictionary<string, string> { ["job"] = "J-1" });

        PackingResult result = new Packer().Invoke(
            new[] { new PackedSheetInput(Sheet(10, 10), new[] { Placed(0, 0, 4, 4) }) },
            context).Value;

        result.Aggregation.Bins.Should().ContainSingle();
        result.Aggregation.Bins[0].PackedItems.Should().ContainSingle();
        result.RemnantList.Should().NotBeEmpty("板材未排满仍有余料 / the sheet is not full");
        result.ExtraInfo["job"].Should().Be("J-1");
    }

    /// <summary>板材按序号命名 / Sheets are named by index.</summary>
    [Fact]
    public void Packer_ShouldNameSheetsByIndex() {
        PackingResult result = new Packer().Invoke(new[] {
            new PackedSheetInput(Sheet(10, 10), Array.Empty<Placement2<FltX>>()),
            new PackedSheetInput(Sheet(5, 5), Array.Empty<Placement2<FltX>>()),
        }).Value;

        result.Aggregation.Bins.Select(bin => bin.Name).Should().Equal("sheet-1", "sheet-2");
    }

    /// <summary>
    /// **几何不合法的排样必须被拒绝，而不是产出一份看起来正常的装箱结果**：下游会照着这份结果去切料。
    /// **An invalid layout must be rejected rather than producing a plausible-looking packing result**: downstream
    /// will cut material from it.
    /// </summary>
    [Fact]
    public void Packer_ShouldRejectInvalidGeometry() {
        Result<PackingResult, ErrorCode, Error<ErrorCode>> result = new Packer().Invoke(new[] {
            new PackedSheetInput(Sheet(10, 10), new[] { Placed(8, 8, 5, 5) }),
        });

        result.IsOk.Should().BeFalse();
        ((Failed<PackingResult, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("type=outside_sheet");
    }

    /// <summary>非法旋转同样在装箱入口被拒绝 / Illegal rotation is rejected at the packer entry.</summary>
    [Fact]
    public void Packer_ShouldRejectIllegalRotation() {
        var catalog = new Dictionary<string, IAbstractItem<FltX>> {
            ["locked"] = new ActualItem<FltX>(
                new ItemId("locked"), "locked",
                new RectanglePackingShape2<FltX>(Meters(2), Meters(5)),
                false, Meters(0)),
        };

        Result<PackingResult, ErrorCode, Error<ErrorCode>> result = new Packer(catalog).Invoke(new[] {
            new PackedSheetInput(Sheet(10, 10), new[] { Placed(0, 0, 5, 2, rotated: true, itemId: "locked") }),
        });

        result.IsOk.Should().BeFalse();
        ((Failed<PackingResult, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("type=illegal_rotation");
    }

    /// <summary>
    /// 物料汇总本批**刻意留空**：`materialAmounts` 随物料模块缺失尚未移植，宁可给空列表也不编一个数字。
    /// The material summary is **deliberately empty**: `materialAmounts` is missing along with the material module, so
    /// an empty list is returned rather than an invented number.
    /// </summary>
    [Fact]
    public void Packer_ShouldLeaveMaterialSummaryEmpty() =>
        new Packer().Invoke(new[] {
            new PackedSheetInput(Sheet(10, 10), new[] { Placed(0, 0, 4, 4, itemId: "a") }),
        }).Value.MaterialSummaries.Should().BeEmpty();

    /// <summary>已装箱物品带上物品编号与装载顺序 / Packed items carry the item id and loading order.</summary>
    [Fact]
    public void Packer_ShouldTagPackedItems() {
        PackingResult result = new Packer().Invoke(new[] {
            new PackedSheetInput(Sheet(10, 10), new[] { Placed(0, 0, 4, 4, itemId: "tag") }),
        }).Value;

        PackedItem item = result.Aggregation.Bins[0].PackedItems[0];
        item.ItemId.Should().Be("tag");
        item.LoadingOrder.Should().Be(1UL);
    }

    /// <summary>构造板材 / Build a sheet.</summary>
    private static Sheet2Shape<FltX> Sheet(double w, double h) => new(Meters(w), Meters(h));

    /// <summary>构造放置 / Build a placement.</summary>
    private static Placement2<FltX> Placed(
        double x, double y, double w, double h, bool rotated = false, string? itemId = null) =>
        new(
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            new QuantityPoint2<FltX>(Meters(x), Meters(y)),
            rotated,
            itemId);

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
