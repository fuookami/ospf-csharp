// BLA 排样算法测试 / Tests for the bottom-up left-justified algorithm.

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
/// 底部向上左对齐算法 / Bottom-up left-justified algorithm.
/// </summary>
public sealed class BottomUpLeftJustifiedAlgorithmTest {
    /// <summary>
    /// **第一个件必须落在原点**：BLA 的整个语义就是"左下优先"，若首件不在原点，后面的候选点推导都无从谈起。
    /// **The first item must land at the origin**: BLA is entirely about bottom-left priority, and if the first item
    /// misses the origin there is no basis for the later candidate derivation.
    /// </summary>
    [Fact]
    public void FirstItem_ShouldLandAtOrigin() {
        Bla2dLayoutResult<FltX> result = Layout(Sheet(10, 10), new[] { (Item("a", 2, 2), 1) });

        result.Placements.Should().HaveCount(1);
        D(result.Placements[0].X).Should().BeApproximately(0.0, 1e-9);
        D(result.Placements[0].Y).Should().BeApproximately(0.0, 1e-9);
        result.FullyPlaced.Should().BeTrue();
    }

    /// <summary>第二个件优先贴在第一个件的右侧，而不是上方 / The second item prefers the right of the first.</summary>
    [Fact]
    public void SecondItem_ShouldPreferRightThenUp() {
        Bla2dLayoutResult<FltX> result = Layout(
            Sheet(20, 20),
            new[] { (Item("a", 5, 5), 1), (Item("b", 5, 5), 1) });

        result.Placements.Should().HaveCount(2);
        Placement2<FltX> second = result.Placements[1];
        D(second.Y).Should().BeApproximately(0.0, 1e-9, "同一行优先 / same row first");
        D(second.X).Should().BeApproximately(5.0, 1e-9, "贴在右侧 / flush to the right");
    }

    /// <summary>放置之间不能重叠 / Placements must not overlap.</summary>
    [Fact]
    public void Placements_ShouldNotOverlap() {
        Bla2dLayoutResult<FltX> result = Layout(
            Sheet(20, 20),
            new[] { (Item("a", 4, 4), 3) });

        result.Placements.Should().HaveCount(3);
        for (int i = 0; i < result.Placements.Count; i++) {
            for (int j = i + 1; j < result.Placements.Count; j++) {
                result.Placements[i].Overlapped(result.Placements[j]).Value
                    .Should().BeFalse($"放置 {i} 与 {j} 不得重叠 / placements {i} and {j} must not overlap");
            }
        }
    }

    /// <summary>所有放置都在板材内 / Every placement stays inside the sheet.</summary>
    [Fact]
    public void Placements_ShouldStayInsideSheet() {
        Sheet2Shape<FltX> sheet = Sheet(10, 10);
        Bla2dLayoutResult<FltX> result = Layout(
            sheet, new[] { (Item("a", 3, 3), 5) });

        result.Placements.Should().NotBeEmpty();
        foreach (Placement2<FltX> placement in result.Placements) {
            placement.InsideSheet(sheet).Value.Should().BeTrue();
        }
    }

    /// <summary>
    /// **放不下的件必须记入跳过列表而不是丢进放置列表**：静默丢弃会让调用方以为全都排好了。
    /// **An item that does not fit must be recorded as skipped, not silently dropped into the placements**;
    /// dropping it would tell the caller everything was packed.
    /// </summary>
    [Fact]
    public void OversizeItem_ShouldBeSkipped() {
        Bla2dLayoutResult<FltX> result = Layout(
            Sheet(10, 10), new[] { (Item("huge", 20, 20), 1) });

        result.Placements.Should().BeEmpty();
        result.SkippedUnfittedItems.Should().ContainSingle().Which.Should().Be("huge");
        result.FullyPlaced.Should().BeFalse();
    }

    /// <summary>装到装不下为止，超出的件记入跳过 / Packs until it no longer fits, then records skips.</summary>
    [Fact]
    public void ExcessAmount_ShouldBeSkipped() {
        // 10×10 板材，4×4 件最多放 4 个（每行 2 个、共 2 行）。/ a 10x10 sheet holds at most four 4x4 items.
        Bla2dLayoutResult<FltX> result = Layout(
            Sheet(10, 10), new[] { (Item("a", 4, 4), 6) });

        result.Placements.Should().HaveCount(4);
        result.SkippedUnfittedItems.Should().HaveCount(2);
    }

    /// <summary>允许旋转时，竖放不下横放能放下的件会被排入 / With rotation allowed a portrait-only miss still fits.</summary>
    [Fact]
    public void Rotation_ShouldAllowFit() {
        // 板材 10×4，件 3×9：竖着高 9 超过 4，横过来 9×3 可以放。
        // Sheet 10x4, item 3x9: upright height 9 exceeds 4, rotated 9x3 fits.
        ActualItem<FltX> rotatable = Item("r", 3, 9, allowRotate: true);
        Bla2dLayoutResult<FltX> result = Layout(Sheet(10, 4), new[] { (rotatable, 1) });

        result.Placements.Should().HaveCount(1);
        result.Placements[0].Rotated.Should().BeTrue();
    }

    /// <summary>不允许旋转时，同样尺寸的件放不下 / With rotation disallowed the same item does not fit.</summary>
    [Fact]
    public void NoRotation_ShouldSkip() {
        ActualItem<FltX> fixedItem = Item("r", 3, 9);
        Bla2dLayoutResult<FltX> result = Layout(Sheet(10, 4), new[] { (fixedItem, 1) });

        result.Placements.Should().BeEmpty();
        result.SkippedUnfittedItems.Should().ContainSingle();
    }

    /// <summary>
    /// **启用刀缝后，贴边的第二件会被拒绝**：候选点只来自已放置件的角点，不会自动让出 kerf 的距离，
    /// 因此贴边候选因间隙不足被拒，且没有"偏移 kerf"的候选点可用，件被记入跳过列表。
    /// **With a kerf, the flush second item is rejected**: candidates come only from placed corners and are not
    /// shifted by the kerf, so the flush candidate fails the gap test and no kerf-shifted candidate exists, leaving
    /// the item skipped.
    /// </summary>
    /// <remarks>
    /// 这是**基线候选点生成方式的固有限制**，不是 C# 移植偏差：基线的 `feasiblePoints` 同样只产出角点，
    /// 刀缝只参与可行性判定、不参与候选点偏移。如实记录，避免把它当成"实现得更好"或悄悄删掉这条测试。
    /// This is an inherent limitation of the baseline's candidate generation, not a porting deviation: the baseline's
    /// `feasiblePoints` likewise emits only corners and the kerf only participates in the feasibility test. It is
    /// recorded honestly rather than "improved away" or dropped from the suite.
    /// </remarks>
    [Fact]
    public void Kerf_ShouldRejectFlushNeighbour() {
        Sheet2Shape<FltX> sheet = Sheet(20, 20);
        var algorithm = new BottomUpLeftJustifiedAlgorithm<FltX>(
            sheet,
            new BottomUpLeftJustifiedAlgorithm<FltX>.Config(),
            Meters(1));

        Bla2dLayoutResult<FltX> result = algorithm
            .Invoke(AsAbstract(new[] { (Item("a", 5, 5), 2) })).Value;

        result.Placements.Should().HaveCount(1, "贴边候选被刀缝判定拒绝 / the flush candidate fails the kerf test");
        result.SkippedUnfittedItems.Should().ContainSingle();
    }

    /// <summary>
    /// 无刀缝时同样两件可以贴边排下——与上一条对照，说明是刀缝而非其它原因导致跳过。
    /// Without a kerf the same pair packs flush — the contrast with the previous case shows the kerf is what causes
    /// the skip.
    /// </summary>
    [Fact]
    public void NoKerf_ShouldPackFlush() {
        Bla2dLayoutResult<FltX> result = Layout(
            Sheet(20, 20), new[] { (Item("a", 5, 5), 2) });

        result.Placements.Should().HaveCount(2);
        D(result.Placements[1].X).Should().BeApproximately(5.0, 1e-9);
    }

    /// <summary>结果含条目编号，便于回溯到原物料 / Placements carry the item id for traceability.</summary>
    [Fact]
    public void Placement_ShouldCarryItemId() {
        Bla2dLayoutResult<FltX> result = Layout(Sheet(10, 10), new[] { (Item("tag", 2, 2), 1) });

        result.Placements[0].ItemId.Should().Be("tag");
    }

    /// <summary>
    /// **圆形件必须被跳过并登记**：圆形没有"向左下推挤"的语义，让它参与 BLA 会得到一个看似紧凑、
    /// 实际与几何不符的排布。登记而不是静默丢弃，调用方才能知道有件没排。
    /// **Circle items must be skipped and recorded**: circles have no "push to bottom-left" semantics, so letting
    /// them into BLA yields a layout that looks tight but contradicts the geometry. Recording rather than dropping
    /// tells the caller something was not packed.
    /// </summary>
    [Fact]
    public void CircleItem_ShouldBeSkipped() {
        Bla2dLayoutResult<FltX> result = Layout(
            Sheet(10, 10),
            new[] { (CircleItem("c1", 2), 1), (CircleItem("c2", 3), 1), (Item("r", 2, 2), 1) });

        result.SkippedCircleItems.Should().Equal("c1", "c2");
        result.SkippedUnfittedItems.Should().BeEmpty();
        result.Placements.Should().ContainSingle("只有矩形件被排入 / only the rectangle is placed");
        result.Placements[0].ItemId.Should().Be("r");
        result.FullyPlaced.Should().BeFalse("有圆形件被跳过 / circle items were skipped");
    }

    /// <summary>
    /// 圆形件按**件**登记一次，不按件数重复：登记的是"哪些物品没参与"，不是"多少件没参与"。
    /// Circle items are recorded once per item, not once per piece.
    /// </summary>
    [Fact]
    public void CircleItem_ShouldBeRecordedOncePerItem() {
        Bla2dLayoutResult<FltX> result = Layout(Sheet(10, 10), new[] { (CircleItem("c", 2), 5) });

        result.SkippedCircleItems.Should().Equal("c");
        result.Placements.Should().BeEmpty();
    }

    /// <summary>没有圆形件时跳过列表为空 / The skip list is empty when there are no circles.</summary>
    [Fact]
    public void SkippedCircleItems_ShouldBeEmptyWithoutCircles() {
        Bla2dLayoutResult<FltX> result = Layout(Sheet(10, 10), new[] { (Item("a", 2, 2), 1) });

        result.SkippedCircleItems.Should().BeEmpty();
    }

    /// <summary>执行排样 / Run the layout.</summary>
    private static Bla2dLayoutResult<FltX> Layout(
        Sheet2Shape<FltX> sheet,
        IReadOnlyList<(ActualItem<FltX> Item, int Amount)> items) =>
        new BottomUpLeftJustifiedAlgorithm<FltX>(sheet).Invoke(AsAbstract(items)).Value;

    /// <summary>
    /// 把具体物品元组提升为抽象物品元组：C# 的元组不随元素类型协变，必须显式转换。
    /// Lift concrete item tuples to abstract ones: C# tuples do not covary with their element types, so the
    /// conversion must be explicit.
    /// </summary>
    private static IEnumerable<(IAbstractItem<FltX> Item, int Amount)> AsAbstract(
        IEnumerable<(ActualItem<FltX> Item, int Amount)> items) =>
        items.Select(pair => ((IAbstractItem<FltX>)pair.Item, pair.Amount));

    /// <summary>构造板材 / Build a sheet.</summary>
    private static Sheet2Shape<FltX> Sheet(double w, double h) => new(Meters(w), Meters(h));

    /// <summary>构造矩形物料 / Build a rectangle item.</summary>
    private static ActualItem<FltX> Item(string id, double w, double h, bool allowRotate = false) =>
        new(
            new ItemId(id),
            id,
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            allowRotate,
            Meters(0));

    /// <summary>构造圆形物料 / Build a circle item.</summary>
    private static ActualItem<FltX> CircleItem(string id, double radius) =>
        new(new ItemId(id), id, new CirclePackingShape2<FltX>(Meters(radius)), false, Meters(0));

    /// <summary>取出数值 / Read the number.</summary>
    private static double D(Quantity<FltX> quantity) => quantity.Value.ToFlt64().ToDouble();

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
