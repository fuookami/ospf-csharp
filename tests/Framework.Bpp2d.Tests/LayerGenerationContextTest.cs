// 层生成器与组合上下文测试 / Tests for the layer generators and the composite context.

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
/// 层生成器与组合上下文 / Layer generators and the composite context.
/// </summary>
public sealed class LayerGenerationContextTest {
    /// <summary>历史生成器原样回放请求中的层 / The historical generator replays the request's layers.</summary>
    [Fact]
    public void Historical_ShouldReplayLayers() {
        Layer<FltX> layer = LayerOf(new[] { Placed(0, 0, 2, 2, "a") });
        var request = new Bpp2dLayerGenerationRequest<FltX>(0, ExistingLayers: new[] { layer });

        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results =
            new HistoricalLayerGenerator<FltX>().Generate(request);

        results.Should().ContainSingle();
        results[0].Layer.Should().BeSameAs(layer);
        results[0].Source.Should().Be("historical");
    }

    /// <summary>图层形状紧贴内容而非等于板材 / A layer's shape hugs its content, not the whole sheet.</summary>
    [Fact]
    public void LayerOf_ShouldHugContent() {
        Layer<FltX> layer = LayerGenerationSupport.LayerOf(
            0, null, SheetTypeOf(10, 10), new[] { Placed(0, 0, 3, 4, "a") });

        layer.Shape.Width.Value.ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
        layer.Shape.Height.Value.ToFlt64().ToDouble().Should().BeApproximately(4.0, 1e-9);
    }

    /// <summary>BLA 生成器在板材上排出矩形层 / The BLA generator lays out a rectangle layer.</summary>
    [Fact]
    public void Bla_ShouldProduceLayer() {
        var request = new Bpp2dLayerGenerationRequest<FltX>(
            0,
            Sheet: SheetTypeOf(10, 10),
            Items: new[] { (RectItem("a", 4, 4), 2) });

        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results =
            new BlaLayerGenerator<FltX>().Generate(request);

        results.Should().ContainSingle();
        results[0].Layer.Units.Should().HaveCount(2);
        results[0].Source.Should().Be("bla");
    }

    /// <summary>没有主板材时 BLA 不产出 / Without a primary sheet BLA produces nothing.</summary>
    [Fact]
    public void Bla_ShouldNeedSheet() =>
        new BlaLayerGenerator<FltX>()
            .Generate(new Bpp2dLayerGenerationRequest<FltX>(0, Items: new[] { (RectItem("a", 4, 4), 1) }))
            .Should().BeEmpty();

    /// <summary>图样生成器铺满整板 / The pattern generator fills the sheet.</summary>
    [Fact]
    public void Pattern_ShouldFillSheet() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results = new PatternLayerGenerator<FltX>()
            .Generate(new Bpp2dLayerGenerationRequest<FltX>(
                0, Sheet: SheetTypeOf(10, 10), Items: new[] { (RectItem("a", 2, 2), 1) }));

        results.Should().NotBeEmpty();
        results[0].Layer.Units.Should().HaveCount(25);
        results[0].Source.Should().Be("pattern");
    }

    /// <summary>图样生成器跳过圆形件 / The pattern generator skips circles.</summary>
    [Fact]
    public void Pattern_ShouldSkipCircles() =>
        new PatternLayerGenerator<FltX>()
            .Generate(new Bpp2dLayerGenerationRequest<FltX>(
                0, Sheet: SheetTypeOf(10, 10), Items: new[] { (CircleItem("c", 2), 1) }))
            .Should().BeEmpty();

    /// <summary>圆网格生成器为圆形件产出候选 / The circle grid generator produces candidates for circles.</summary>
    [Fact]
    public void CircleGrid_ShouldProduceCandidates() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results = new CircleGridLayerGenerator<FltX>()
            .Generate(new Bpp2dLayerGenerationRequest<FltX>(
                0, Sheet: SheetTypeOf(20, 20), Items: new[] { (CircleItem("c", 2), 1) }));

        results.Should().HaveCount(2, "直角与六角各一条 / one square and one hex");
        results.Select(result => result.Source)
            .Should().BeEquivalentTo(new[] { "circle-rect-grid", "circle-hex-grid" });
    }

    /// <summary>圆网格生成器跳过矩形件 / The circle grid generator skips rectangles.</summary>
    [Fact]
    public void CircleGrid_ShouldSkipRectangles() =>
        new CircleGridLayerGenerator<FltX>()
            .Generate(new Bpp2dLayerGenerationRequest<FltX>(
                0, Sheet: SheetTypeOf(10, 10), Items: new[] { (RectItem("a", 2, 2), 1) }))
            .Should().BeEmpty();

    /// <summary>混合生成器需要同时有圆形与矩形件 / The mixed generator needs both shapes.</summary>
    [Fact]
    public void Mixed_ShouldNeedBothShapes() {
        var onlyRects = new Bpp2dLayerGenerationRequest<FltX>(
            0, Sheet: SheetTypeOf(10, 10), Items: new[] { (RectItem("a", 2, 2), 1) });

        new MixedShapeLayerGenerator<FltX>().Generate(onlyRects).Should().BeEmpty();
    }

    /// <summary>
    /// **混合层的每一对放置都互不重叠**：圆网格是在上移后的子板材里生成的，上移后可能与底部矩形行相撞，
    /// 逐对复核是唯一能证明整层合法的办法。
    /// **Every pair in a mixed layer is disjoint**: circle grids are generated in a shifted sub-sheet and may collide with
    /// the bottom rectangle row once shifted; a pairwise sweep is the only proof of legality.
    /// </summary>
    [Fact]
    public void Mixed_ShouldProduceNonOverlappingLayers() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results = new MixedShapeLayerGenerator<FltX>()
            .Generate(new Bpp2dLayerGenerationRequest<FltX>(
                0,
                Sheet: SheetTypeOf(20, 20),
                Items: new[] { (RectItem("r", 4, 4), 1), (CircleItem("c", 2), 1) }));

        results.Should().NotBeEmpty();
        foreach (Bpp2dLayerGenerationResult<FltX> result in results) {
            IReadOnlyList<Placement2<FltX>> units = result.Layer.Units;
            for (int i = 0; i < units.Count; i++) {
                for (int j = i + 1; j < units.Count; j++) {
                    units[i].Overlapped(units[j]).Value
                        .Should().BeFalse("混合层内不得重叠 / a mixed layer must not overlap");
                }
            }
        }
    }

    /// <summary>
    /// 组合上下文默认装配八个生成器。Batch 85 时这里是 5（缺三个块装载生成器），Batch 88 补齐后改为 8——
    /// **这条断言随缺口关闭而更新，不是放宽**。
    /// The composite context assembles eight generators. It was 5 at Batch 85 (missing the three block-loading ones) and
    /// becomes 8 once Batch 88 closed the gap — **this assertion tracks the gap closing, it is not a relaxation**.
    /// </summary>
    [Fact]
    public void Context_ShouldShipAllBaselineGenerators() =>
        LayerGenerationContext<FltX>.DefaultGenerators().Should().HaveCount(8);

    /// <summary>组合上下文对纯矩形请求产出候选 / The composite context yields candidates for a rectangle request.</summary>
    [Fact]
    public void Context_ShouldGenerateForRectangles() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results =
            new LayerGenerationContext<FltX>().Generate(new Bpp2dLayerGenerationRequest<FltX>(
                0, Sheet: SheetTypeOf(10, 10), Items: new[] { (RectItem("a", 2, 2), 1) }));

        results.Should().NotBeEmpty();
    }

    /// <summary>组合上下文对纯圆形请求只走圆网格与历史生成器 / A pure-circle request uses only the circle grid and history.</summary>
    [Fact]
    public void Context_ShouldRoutePureCircleRequest() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results =
            new LayerGenerationContext<FltX>().Generate(new Bpp2dLayerGenerationRequest<FltX>(
                0, Sheet: SheetTypeOf(20, 20), Items: new[] { (CircleItem("c", 2), 1) }));

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(result =>
            result.Source == "circle-rect-grid" || result.Source == "circle-hex-grid"
            || result.Source == "historical");
    }

    /// <summary>候选数受 maxCandidates 限制 / maxCandidates caps the output.</summary>
    [Fact]
    public void Context_ShouldRespectMaxCandidates() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results =
            new LayerGenerationContext<FltX>().Generate(new Bpp2dLayerGenerationRequest<FltX>(
                0,
                Sheet: SheetTypeOf(20, 20),
                Items: new[] { (RectItem("a", 2, 2), 1) },
                MaxCandidates: 1));

        results.Should().HaveCount(1);
    }

    /// <summary>无生成器时返回空 / An empty generator list yields nothing.</summary>
    [Fact]
    public void Context_ShouldHandleEmptyGenerators() =>
        new LayerGenerationContext<FltX>(Array.Empty<IBpp2dLayerGenerator<FltX>>())
            .Generate(new Bpp2dLayerGenerationRequest<FltX>(0, Sheet: SheetTypeOf(10, 10)))
            .Should().BeEmpty();

    /// <summary>
    /// **多板材类型各生成一批候选，且候选层带上各自的板材类型**：否则多板材排样无法区分列属于哪种板。
    /// **Each sheet type gets its own candidates, and layers carry their sheet type**: otherwise multi-sheet packing
    /// cannot tell which sheet a column belongs to.
    /// </summary>
    [Fact]
    public void Context_ShouldGeneratePerSheetType() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results =
            new LayerGenerationContext<FltX>().Generate(new Bpp2dLayerGenerationRequest<FltX>(
                0,
                Sheets: new[] { SheetTypeOf(10, 10, "A"), SheetTypeOf(20, 20, "B") },
                Items: new[] { (RectItem("a", 2, 2), 1) }));

        results.Select(result => result.Layer.Sheet!.TypeCode.Value)
            .Distinct().Should().BeEquivalentTo(new[] { "A", "B" });
    }

    /// <summary>给定评分函数时按评分降序 / With a scorer, results are ordered by score descending.</summary>
    [Fact]
    public void Context_ShouldRankByScore() {
        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> results =
            new LayerGenerationContext<FltX>().Generate(new Bpp2dLayerGenerationRequest<FltX>(
                0,
                Sheet: SheetTypeOf(20, 20),
                Items: new[] { (RectItem("a", 2, 2), 1) },
                ScoreByShadowPrice: (layer, _) => layer.Area.Value));

        results.Should().NotBeEmpty();
        results.Select(result => result.Score!.Value).Should().BeInDescendingOrder();
    }

    /// <summary>构造板材类型 / Build a sheet type.</summary>
    private static SheetType<FltX> SheetTypeOf(double w, double h, string code = "T") =>
        new(Meters(w), Meters(h), Meters(0), new SheetTypeId(code));

    /// <summary>用给定放置构造一个层 / Build a layer over the given placements.</summary>
    private static Layer<FltX> LayerOf(IReadOnlyList<Placement2<FltX>> units) => new(
        0, null, SheetTypeOf(10, 10), new Sheet2Shape<FltX>(Meters(10), Meters(10)), units);

    /// <summary>构造圆形件（入参为直径，此处折半为半径）/ Build a circle item (diameter argument, halved to radius).</summary>
    private static ActualItem<FltX> CircleItem(string id, double diameter) =>
        new(new ItemId(id), id, new CirclePackingShape2<FltX>(Meters(diameter / 2.0)), true, Meters(0));

    /// <summary>构造矩形件 / Build a rectangle item.</summary>
    private static ActualItem<FltX> RectItem(string id, double w, double h) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Meters(w), Meters(h)), true, Meters(0));

    /// <summary>构造放置 / Build a placement.</summary>
    private static Placement2<FltX> Placed(
        double x, double y, double w, double h, string? itemId) =>
        new(
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            new QuantityPoint2<FltX>(Meters(x), Meters(y)),
            false,
            itemId);

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
