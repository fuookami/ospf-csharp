// 简单块与复杂块生成测试 / Tests for simple and complex block generation.

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
/// 简单块与复杂块生成 / Simple and complex block generation.
/// </summary>
public sealed class BlockGeneratorsTest {
    /// <summary>简单块铺满区域 / A simple block fills the region.</summary>
    [Fact]
    public void Simple_ShouldFillRegion() {
        IReadOnlyList<Block2<FltX>> blocks = SimpleBlockGenerator
            .Invoke(RectItem("a", 2, 2), Space(0, 0, 10, 10)).Value;

        blocks.Should().ContainSingle();
        blocks[0].Placements.Should().HaveCount(25);
        blocks[0].Source.Should().Be("simple-block");
    }

    /// <summary>块形状紧贴内容 / The block's space hugs its content.</summary>
    [Fact]
    public void Simple_BlockSpaceShouldHugContent() {
        Block2<FltX> block = SimpleBlockGenerator
            .Invoke(RectItem("a", 3, 3), Space(0, 0, 10, 10)).Value[0];

        block.Space.Width.Value.ToFlt64().ToDouble().Should().BeApproximately(9.0, 1e-9);
        block.Space.Height.Value.ToFlt64().ToDouble().Should().BeApproximately(9.0, 1e-9);
    }

    /// <summary>
    /// **旋转候选同时受配置与物品许可约束**：只检查配置会让未允许旋转的物品产出旋转块，与旋转守卫语义矛盾。
    /// **Rotated candidates need both the config and the item's allowance**: checking only the config would let an item
    /// that disallows rotation yield rotated blocks, contradicting the rotation guard.
    /// </summary>
    [Fact]
    public void Simple_ShouldRespectItemRotationAllowance() {
        IReadOnlyList<Block2<FltX>> blocks = SimpleBlockGenerator
            .Invoke(RectItem("locked", 2, 5, allowRotate: false), Space(0, 0, 20, 20)).Value;

        blocks.Should().OnlyContain(block =>
            block.Placements.All(placement => !placement.Rotated));
    }

    /// <summary>允许旋转时会额外产出旋转块 / An item that allows rotation yields rotated blocks too.</summary>
    [Fact]
    public void Simple_ShouldYieldRotatedBlockWhenAllowed() {
        IReadOnlyList<Block2<FltX>> blocks = SimpleBlockGenerator
            .Invoke(RectItem("free", 2, 5, allowRotate: true), Space(0, 0, 20, 20)).Value;

        blocks.Select(block => block.Source)
            .Should().Contain("simple-block-rotated");
    }

    /// <summary>配置关闭旋转时即使物品允许也不产出旋转块 / A disabled config yields no rotated block even when allowed.</summary>
    [Fact]
    public void Simple_ShouldRespectConfigRotation() {
        IReadOnlyList<Block2<FltX>> blocks = SimpleBlockGenerator
            .Invoke(
                RectItem("free", 2, 5, allowRotate: true),
                Space(0, 0, 20, 20),
                new SimpleBlockConfig(WithRotation: false)).Value;

        blocks.Select(block => block.Source).Should().NotContain("simple-block-rotated");
    }

    /// <summary>圆形件不产生简单块 / A circle yields no simple block.</summary>
    [Fact]
    public void Simple_ShouldSkipCircle() =>
        SimpleBlockGenerator.Invoke(CircleItem("c", 2), Space(0, 0, 10, 10)).Value.Should().BeEmpty();

    /// <summary>退化区域不产生块 / A degenerate region yields no block.</summary>
    [Fact]
    public void Simple_ShouldSkipDegenerateSpace() =>
        SimpleBlockGenerator.Invoke(RectItem("a", 2, 2), Space(0, 0, 0, 10)).Value.Should().BeEmpty();

    /// <summary>装不下一件时返回空 / A region too small for one piece yields nothing.</summary>
    [Fact]
    public void Simple_ShouldSkipUndersizedSpace() =>
        SimpleBlockGenerator.Invoke(RectItem("a", 5, 5), Space(0, 0, 1, 1)).Value.Should().BeEmpty();

    /// <summary>
    /// **`WithRemainder` 是空转的**：右侧余量恒为 `W - cols × pitch`，而 `cols = ⌊W / pitch⌋`，因此余量必然
    /// 小于一个节距，递归进去只会得到 `cols = 0`。这里保留该配置项以免改动签名，并用测试把这一事实固定下来，
    /// 免得后来者以为"打开了余量却没用"是本实现的 bug。
    ///
    /// **`WithRemainder` is a no-op**: the right remainder is always `W - cols × pitch` with
    /// `cols = ⌊W / pitch⌋`, so it is necessarily narrower than one pitch and the recursion only ever sees `cols = 0`.
    /// The option is kept for signature parity, and this test pins the fact so nobody later reads "enabled but nothing
    /// happens" as a defect of this implementation.
    /// </summary>
    [Fact]
    public void Simple_RemainderRecursionIsEffectivelyNoOp() {
        IReadOnlyList<Block2<FltX>> blocks = SimpleBlockGenerator
            .Invoke(
                RectItem("a", 3, 3),
                Space(0, 0, 10, 10),
                new SimpleBlockConfig(WithRemainder: true)).Value;

        // 3 宽在 10 宽里铺 3 列（9），余量 1 宽 < 节距 3，递归无产出。
        // Three 3-wide columns fit in width 10 (9 used), the 1-wide remainder is below the pitch 3, so nothing recurses.
        blocks.Should().ContainSingle();
        blocks[0].Placements.Should().HaveCount(9);
    }

    /// <summary>
    /// **刀缝使每个方向的件数减少**：节距 = 包围尺寸 + kerf，这是刀缝在块生成里的唯一体现方式。
    /// **The kerf reduces the count in each direction**: pitch = bounding size + kerf, the only way the kerf shows up
    /// here.
    /// </summary>
    [Fact]
    public void Simple_KerfShouldReduceCount() {
        int without = SimpleBlockGenerator
            .Invoke(RectItem("a", 2, 2), Space(0, 0, 10, 10)).Value[0].Placements.Count;
        int with = SimpleBlockGenerator
            .Invoke(RectItem("a", 2, 2), Space(0, 0, 10, 10), kerf: Meters(1)).Value[0].Placements.Count;

        with.Should().BeLessThan(without);
    }

    /// <summary>块内放置都从区域原点起排 / Placements start at the region origin.</summary>
    [Fact]
    public void Simple_ShouldRespectSpaceOrigin() {
        Block2<FltX> block = SimpleBlockGenerator
            .Invoke(RectItem("a", 2, 2), Space(5, 7, 4, 4)).Value[0];

        block.Placements.Min(placement => placement.X.Value.ToFlt64().ToDouble())
            .Should().BeApproximately(5.0, 1e-9);
        block.Placements.Min(placement => placement.Y.Value.ToFlt64().ToDouble())
            .Should().BeApproximately(7.0, 1e-9);
    }

    /// <summary>
    /// **复杂块逐行拼接多种物品**：每行只用一种物品，因此块的来源是各行的来源用 `+` 连接。
    /// **A complex block stitches multiple items row by row**: each row uses one item, so the source joins the per-row
    /// sources with `+`.
    /// </summary>
    [Fact]
    public void Complex_ShouldStitchRows() {
        IReadOnlyList<Block2<FltX>> blocks = ComplexBlockGenerator
            .Invoke(
                new[] { RectItem("a", 2, 2), RectItem("b", 3, 3) },
                Space(0, 0, 10, 20)).Value;

        blocks.Should().ContainSingle();
        blocks[0].Placements.Select(placement => placement.ItemId).Distinct()
            .Should().BeEquivalentTo(new[] { "a", "b" });
        blocks[0].Source.Should().Be("complex-block", "两行都未旋转，来源相同 / both rows unrotated");
    }

    /// <summary>
    /// 行的旋转状态不同时，块的来源用 `+` 把各来源连起来——这一位信息决定了块能否被后续识别为"异构块"。
    /// When rows differ in rotation the block's source joins the per-row sources with `+`; that bit is what lets
    /// downstream identify the block as heterogeneous.
    /// </summary>
    [Fact]
    public void Complex_ShouldJoinSourcesWhenRotationDiffers() {
        IReadOnlyList<Block2<FltX>> blocks = ComplexBlockGenerator
            .Invoke(
                // 2×9 允许旋转时按最大宽度选中 9×2（旋转行）；3×3 两种候选同宽，保持未旋转。
                // A rotatable 2×9 picks 9×2 by widest candidate (a rotated row); 3×3 has equal widths and stays
                // unrotated.
                new[] { RectItem("a", 2, 9), RectItem("b", 3, 3) },
                Space(0, 0, 10, 20)).Value;

        blocks.Should().ContainSingle();
        blocks[0].Source.Should().Be("complex-block-rotated+complex-block");
    }

    /// <summary>空物品列表返回空 / An empty item list yields nothing.</summary>
    [Fact]
    public void Complex_ShouldHandleEmptyItems() =>
        ComplexBlockGenerator.Invoke(Array.Empty<IAbstractItem<FltX>>(), Space(0, 0, 10, 10))
            .Value.Should().BeEmpty();

    /// <summary>圆形件在复杂块中被跳过 / Circles are skipped in a complex block.</summary>
    [Fact]
    public void Complex_ShouldSkipCircles() =>
        ComplexBlockGenerator
            .Invoke(new[] { CircleItem("c", 2) }, Space(0, 0, 10, 10))
            .Value.Should().BeEmpty();

    /// <summary>
    /// 高度不足以放下一行时停止。用**不可旋转**的 2×4 件，行高确定为 4，板材高 10 只能放两行——若允许旋转，
    /// 按最大宽度选候选会选中 4×2，行高变 2 而能放五行，测的就不是"耗尽即停"了。
    /// Stitching stops once the remaining height cannot fit another row. Uses a **non-rotatable** 2×4 item so the row
    /// height is definitely 4 and only two rows fit in a 10-tall sheet — with rotation allowed the widest candidate is
    /// 4×2, the row height becomes 2 and five rows fit, which would not test the stop condition at all.
    /// </summary>
    [Fact]
    public void Complex_ShouldStopWhenHeightExhausted() {
        IReadOnlyList<Block2<FltX>> blocks = ComplexBlockGenerator
            .Invoke(
                Enumerable.Range(0, 5)
                    .Select(index => RectItem($"i{index}", 2, 4, allowRotate: false))
                    .ToList(),
                Space(0, 0, 10, 10)).Value;

        blocks.Should().ContainSingle();
        blocks[0].Placements.Select(placement => placement.ItemId).Distinct().Should().HaveCount(2);
    }

    /// <summary>复杂块不接受未允许旋转的物品的旋转行 / A locked item contributes no rotated row.</summary>
    [Fact]
    public void Complex_ShouldRespectItemRotationAllowance() {
        IReadOnlyList<Block2<FltX>> blocks = ComplexBlockGenerator
            .Invoke(
                new[] { RectItem("locked", 2, 5, allowRotate: false) },
                Space(0, 0, 20, 20)).Value;

        blocks.Should().OnlyContain(block =>
            block.Placements.All(placement => !placement.Rotated));
    }

    /// <summary>构造区域 / Build a region.</summary>
    private static Space2<FltX> Space(double x, double y, double w, double h) =>
        new(Meters(x), Meters(y), Meters(w), Meters(h));

    /// <summary>构造矩形件 / Build a rectangle item.</summary>
    private static IAbstractItem<FltX> RectItem(
        string id, double w, double h, bool allowRotate = true) =>
        new ActualItem<FltX>(
            new ItemId(id), id,
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            allowRotate, Meters(0));

    /// <summary>构造圆形件（入参为直径，此处折半为半径）/ Build a circle item (diameter argument, halved to radius).</summary>
    private static IAbstractItem<FltX> CircleItem(string id, double diameter) =>
        new ActualItem<FltX>(
            new ItemId(id), id,
            new CirclePackingShape2<FltX>(Meters(diameter / 2.0)),
            true, Meters(0));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
