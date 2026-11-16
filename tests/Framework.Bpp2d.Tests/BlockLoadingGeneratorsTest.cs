// 块装载层生成器测试 / Tests for the block-loading layer generators.

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
/// 块装载层生成器 / Block-loading layer generators.
/// </summary>
public sealed class BlockLoadingGeneratorsTest {
    /// <summary>
 /// **默认生成器集合现在（八个）**：这是 earlier 记下的缺口，这里必须由测试把它钉住，否则下一次
    /// 重构可能悄悄少装一个而无人察觉。
 /// **The default generator set now matches the code (eight)**: that was the gap recorded in earlier, and this
    /// test pins it so a later refactor cannot silently drop one unnoticed.
    /// </summary>
    [Fact]
    public void DefaultGenerators_ShouldMatchBaselineCount() =>
        LayerGenerationContext<FltX>.DefaultGenerators().Should().HaveCount(8);

    /// <summary>默认集合包含三个块装载生成器 / The default set contains the three block-loading generators.</summary>
    [Fact]
    public void DefaultGenerators_ShouldIncludeBlockLoading() {
        IReadOnlyList<IBpp2dLayerGenerator<FltX>> generators =
            LayerGenerationContext<FltX>.DefaultGenerators();

        generators.Should().Contain(generator => generator is BlLocalLayerGenerator<FltX>);
        generators.Should().Contain(generator => generator is BlGlobalLayerGenerator<FltX>);
        generators.Should().Contain(generator => generator is SpaceSplitLayerGenerator<FltX>);
    }

    /// <summary>局部生成器为每个物品各出一块 / The local generator emits one block per item.</summary>
    [Fact]
    public void BlLocal_ShouldEmitOneBlockPerItem() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results = new BlLocalLayerGenerator<FltX>()
            .Generate(Request(SheetTypes.Of(10, 10), (Item("a", 2, 2), 1), (Item("b", 3, 3), 1)));

        results.Should().HaveCount(2);
        results.Should().OnlyContain(result => result.Source == "block-local");
    }

    /// <summary>局部生成器的层带上物品编号 / Local layers carry the item id.</summary>
    [Fact]
    public void BlLocal_ShouldTagItemId() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results = new BlLocalLayerGenerator<FltX>()
            .Generate(Request(SheetTypes.Of(10, 10), (Item("tag", 2, 2), 1)));

        results.Should().ContainSingle();
        results[0].Layer.Units.Should().OnlyContain(unit => unit.ItemId == "tag");
    }

    /// <summary>全局生成器把多物品拼成一块 / The global generator stitches items into one block.</summary>
    [Fact]
    public void BlGlobal_ShouldStitch() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results = new BlGlobalLayerGenerator<FltX>()
            .Generate(Request(SheetTypes.Of(10, 20), (Item("a", 2, 2), 1), (Item("b", 3, 3), 1)));

        results.Should().ContainSingle();
        results[0].Source.Should().Be("block-global");
        results[0].Layer.Units.Select(unit => unit.ItemId).Distinct().Should().HaveCount(2);
    }

    /// <summary>空间切分生成器同时给出 DFS 与多行两条候选 / The space-split generator yields both DFS and multi-row.</summary>
    [Fact]
    public void SpaceSplit_ShouldYieldBothSearches() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results = new SpaceSplitLayerGenerator<FltX>()
            .Generate(Request(SheetTypes.Of(10, 10), (Item("a", 2, 2), 20)));

        results.Select(result => result.Source)
            .Should().BeEquivalentTo(new[] { "space-split-dfs", "space-split-multi-row" });
    }

    /// <summary>
    /// **两种搜索各自独立**：DFS 跳过圆形件而多行也跳过，但只要有任一方产出就应保留——用纯矩形件验证两条都在，
    /// 用圆形件验证两者都空且不抛异常。
    /// **The two searches are independent**: with rectangles both appear; with circles both are empty and nothing throws.
    /// </summary>
    [Fact]
    public void SpaceSplit_ShouldHandleCircleOnlyRequest() =>
        new SpaceSplitLayerGenerator<FltX>()
            .Generate(Request(SheetTypes.Of(10, 10), (Circle("c", 2), 3)))
            .Should().BeEmpty();

    /// <summary>没有主板材时三个生成器都返回空 / All three generators need a primary sheet.</summary>
    [Fact]
    public void BlockLoadingGenerators_ShouldNeedSheet() {
        var request = new Bpp2dLayerGenerationRequest<FltX>(
            0, Items: new[] { (Item("a", 2, 2), 1) });

        new BlLocalLayerGenerator<FltX>().Generate(request).Should().BeEmpty();
        new BlGlobalLayerGenerator<FltX>().Generate(request).Should().BeEmpty();
        new SpaceSplitLayerGenerator<FltX>().Generate(request).Should().BeEmpty();
    }

    /// <summary>
    /// **组合上下文能产出块装载候选**：这是缺口被补上的最终判据——不是"类存在"，而是"从入口能拿到这些来源"。
    /// **The composite context yields block-loading candidates**: the real criterion for closing the gap is not that the
    /// classes exist but that these sources come out of the entry point.
    /// </summary>
    [Fact]
    public void Context_ShouldYieldBlockLoadingSources() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results = new LayerGenerationContext<FltX>()
            .Generate(Request(SheetTypes.Of(10, 10), (Item("a", 2, 2), 20)));

        results.Should().NotBeEmpty();
        results.Select(result => result.Source)
            .Should().Contain(source => source.StartsWith("block-", StringComparison.Ordinal));
        results.Select(result => result.Source)
            .Should().Contain(source => source.StartsWith("space-split-", StringComparison.Ordinal));
    }

    /// <summary>候选结果互不相同，去重后不出现重复层 / Candidates are deduplicated by layer.</summary>
    [Fact]
    public void Context_ShouldDeduplicateLayers() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results = new LayerGenerationContext<FltX>()
            .Generate(Request(SheetTypes.Of(10, 10), (Item("a", 2, 2), 20)));

        results.Select(result => result.Layer).Should().OnlyHaveUniqueItems();
    }

    /// <summary>构造请求 / Build a request.</summary>
    private static Bpp2dLayerGenerationRequest<FltX> Request(
        SheetType<FltX> sheet,
        params (ActualItem<FltX> Item, int Amount)[] items) =>
        new(0, Sheet: sheet, Items: items);

    /// <summary>构造矩形件 / Build a rectangle item.</summary>
    private static ActualItem<FltX> Item(string id, double w, double h) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Meters(w), Meters(h)), false, Meters(0));

    /// <summary>构造圆形件（入参为直径，此处折半为半径）/ Build a circle item (diameter argument, halved to radius).</summary>
    private static ActualItem<FltX> Circle(string id, double diameter) =>
        new(new ItemId(id), id, new CirclePackingShape2<FltX>(Meters(diameter / 2.0)), true, Meters(0));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);

    /// <summary>板材类型构造辅助 / Sheet-type construction helper.</summary>
    private static class SheetTypes {
        /// <summary>构造板材类型 / Build a sheet type.</summary>
        /// <param name="w">宽度 / Width.</param>
        /// <param name="h">高度 / Height.</param>
        /// <returns>板材类型 / The sheet type.</returns>
        public static SheetType<FltX> Of(double w, double h) =>
            new(Meters(w), Meters(h), Meters(0), new SheetTypeId("T"));
    }
}
