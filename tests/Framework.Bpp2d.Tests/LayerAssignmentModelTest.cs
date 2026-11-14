// 层分配模型测试 / Tests for the layer assignment model.

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
/// 层分配模型 / Layer assignment model.
/// </summary>
public sealed class LayerAssignmentModelTest {
    /// <summary>新列去重后进入聚合 / New columns enter the aggregation deduplicated.</summary>
    [Fact]
    public void AddColumns_ShouldDeduplicate() {
        var aggregation = new LayerAggregation<FltX>();
        Layer<FltX> layer = LayerOf("a", 2, 2);

        aggregation.AddColumns(new[] { layer }).Should().ContainSingle();
        aggregation.AddColumns(new[] { layer }).Should().BeEmpty("已存在的层不再新增 / the layer already exists");
        aggregation.Layers.Should().ContainSingle();
    }

    /// <summary>
    /// **同一批内部的重复列也要去重**：只与已有层比较会把批内重复留下，随后列数与候选统计不符。
    /// **Duplicates inside one batch are removed too**: comparing only against existing layers keeps them and then the
    /// column count disagrees with candidate statistics.
    /// </summary>
    [Fact]
    public void AddColumns_ShouldDeduplicateWithinBatch() {
        var aggregation = new LayerAggregation<FltX>();
        Layer<FltX> layer = LayerOf("a", 2, 2);

        aggregation.AddColumns(new[] { layer, layer }).Should().ContainSingle();
    }

    /// <summary>空批次也会被记入迭代历史，保持轮次对齐 / An empty batch still lands in the history, keeping rounds aligned.</summary>
    [Fact]
    public void AddColumns_ShouldRecordEmptyBatch() {
        var aggregation = new LayerAggregation<FltX>();

        aggregation.AddColumns(Array.Empty<Layer<FltX>>());

        aggregation.LayersIteration.Should().ContainSingle();
        aggregation.LayersIteration[0].Should().BeEmpty();
    }

    /// <summary>最近一次迭代取最后一个非空批次 / The last iteration is the last non-empty batch.</summary>
    [Fact]
    public void LastIterationLayers_ShouldSkipEmptyBatches() {
        var aggregation = new LayerAggregation<FltX>();
        aggregation.AddColumns(new[] { LayerOf("a", 2, 2) });
        aggregation.AddColumns(Array.Empty<Layer<FltX>>());

        aggregation.LastIterationLayers.Should().ContainSingle();
    }

    /// <summary>无任何批次时最近一次迭代为空 / With no batch at all the last iteration is empty.</summary>
    [Fact]
    public void LastIterationLayers_ShouldBeEmptyWhenNothingAdded() =>
        new LayerAggregation<FltX>().LastIterationLayers.Should().BeEmpty();

    /// <summary>移除的列进入已移除集合并从当前集合消失 / Removed columns leave the current set and land in removed.</summary>
    [Fact]
    public void RemoveColumn_ShouldBookkeep() {
        var aggregation = new LayerAggregation<FltX>();
        Layer<FltX> layer = LayerOf("a", 2, 2);
        aggregation.AddColumns(new[] { layer });

        aggregation.RemoveColumn(layer);

        aggregation.Layers.Should().BeEmpty();
        aggregation.RemovedLayers.Should().Contain(layer);
    }

    /// <summary>
    /// **重复移除是幂等的**：上层在收敛过程中会对同一列重复调用移除，若第二次仍改变状态，`RemovedLayers` 就会失真。
    /// **Removing twice is idempotent**: the caller retries removals while converging, and a second mutation would make
    /// `RemovedLayers` lie.
    /// </summary>
    [Fact]
    public void RemoveColumn_ShouldBeIdempotent() {
        var aggregation = new LayerAggregation<FltX>();
        Layer<FltX> layer = LayerOf("a", 2, 2);
        aggregation.AddColumns(new[] { layer });

        aggregation.RemoveColumn(layer);
        aggregation.RemoveColumn(layer);

        aggregation.RemovedLayers.Should().ContainSingle();
    }

    /// <summary>批量移除逐个生效 / Removing several columns applies each.</summary>
    [Fact]
    public void RemoveColumns_ShouldRemoveEach() {
        var aggregation = new LayerAggregation<FltX>();
        Layer<FltX> first = LayerOf("a", 2, 2);
        Layer<FltX> second = LayerOf("b", 3, 3);
        aggregation.AddColumns(new[] { first, second });

        aggregation.RemoveColumns(new[] { first, second });

        aggregation.Layers.Should().BeEmpty();
        aggregation.RemovedLayers.Should().HaveCount(2);
    }

    /// <summary>非正件数折算为零 / Non-positive counts collapse to zero.</summary>
    [Fact]
    public void CountOf_ShouldCollapseNonPositive() {
        LayerAggregationMath.CountOf(-1).Should().Be(0UL);
        LayerAggregationMath.CountOf(0).Should().Be(0UL);
        LayerAggregationMath.CountOf(7).Should().Be(7UL);
    }

    /// <summary>需求值可换算为求解器数值 / A demand value converts to a solver number.</summary>
    [Fact]
    public void DemandValue_ShouldConvertToSolver() {
        new Bpp2dAssignmentDemandValue.Amount(5).ToSolver().ToFlt64().ToDouble()
            .Should().BeApproximately(5.0, 1e-9);
        new Bpp2dAssignmentDemandValue.Area(Meters(12)).ToSolver().ToFlt64().ToDouble()
            .Should().BeApproximately(12.0, 1e-9);
    }

    /// <summary>件数需求的贡献系数等于层内件数 / An amount demand's coefficient is the layer's item amount.</summary>
    [Fact]
    public void LayerDemandCoefficient_ShouldCountAmount() {
        Layer<FltX> layer = LayerOf("a", 2, 2, copies: 3);

        LayerAssignmentMath
            .LayerDemandCoefficient(layer, AmountDemand("a"))
            .ToFlt64().ToDouble().Should().BeApproximately(3.0, 1e-9);
    }

    /// <summary>
    /// **面积需求用单件面积乘件数，而不是层总面积**：层里可能混有多种物品，用总面积会把别的物品的面积记到这个需求上。
    /// **An area demand uses unit area times amount, not the layer's total area**: a layer may mix items and the total would
    /// charge other items' area to this demand.
    /// </summary>
    [Fact]
    public void LayerDemandCoefficient_ShouldUseUnitArea() {
        var layer = new Layer<FltX>(
            0, null, SheetTypeOf(), new Sheet2Shape<FltX>(Meters(10), Meters(10)),
            new[] {
                Placed(0, 0, 2, 3, "a"),
                Placed(3, 0, 2, 3, "a"),
                Placed(6, 0, 5, 5, "other"),
            });

        // a 单件面积 6，两件共 12；层总面积 6 + 6 + 25 = 37。
        // Each a is 6 in area and there are two, so 12; the layer's total area is 6 + 6 + 25 = 37.
        LayerAssignmentMath
            .LayerDemandCoefficient(layer, AreaDemand("a"))
            .ToFlt64().ToDouble().Should().BeApproximately(12.0, 1e-9);
    }

    /// <summary>层内没有该物品时系数为零 / A missing item yields a zero coefficient.</summary>
    [Fact]
    public void LayerDemandCoefficient_ShouldBeZeroForMissingItem() =>
        LayerAssignmentMath
            .LayerDemandCoefficient(LayerOf("a", 2, 2), AmountDemand("absent"))
            .ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);

    /// <summary>
    /// **检验数公式 `area·c − Σπᵢ·aᵢ`**：无影子价格时退化为面积项；有正影子价格时应被扣减。
    /// **The reduced cost is `area·c − Σπᵢ·aᵢ`**: with no shadow price it degenerates to the area term, and a positive price
    /// is subtracted.
    /// </summary>
    [Fact]
    public void LayerReducedCost_ShouldSubtractDuals() {
        Layer<FltX> layer = LayerOf("a", 2, 2, copies: 2);

        LayerAssignmentMath
            .LayerReducedCost(layer, new[] { AmountDemand("a") }, Prices())
            .ToFlt64().ToDouble().Should().BeApproximately(8.0, 1e-9);

        LayerAssignmentMath
            .LayerReducedCost(layer, new[] { AmountDemand("a") }, Prices(("a", 5)))
            .ToFlt64().ToDouble().Should().BeApproximately(-2.0, 1e-9, "8 − 5×2 / area minus dual times amount");
    }

    /// <summary>影子价格为零的需求被跳过，结果与无价格一致 / A zero price is skipped, matching no price.</summary>
    [Fact]
    public void LayerReducedCost_ShouldSkipZeroPrice() {
        Layer<FltX> layer = LayerOf("a", 2, 2);

        LayerAssignmentMath
            .LayerReducedCost(layer, new[] { AmountDemand("a") }, Prices(("a", 0)))
            .ToFlt64().ToDouble().Should().BeApproximately(4.0, 1e-9);
    }

    /// <summary>负检验数判为改进列 / A negative reduced cost counts as improving.</summary>
    [Fact]
    public void IsImprovingColumn_ShouldDetectImprovement() {
        Layer<FltX> layer = LayerOf("a", 2, 2);

        LayerAssignmentMath
            .IsImprovingColumn(layer, new[] { AmountDemand("a") }, Prices(("a", 9)))
            .Should().BeTrue();
        LayerAssignmentMath
            .IsImprovingColumn(layer, new[] { AmountDemand("a") }, Prices(("a", 1)))
            .Should().BeFalse();
    }

    /// <summary>
    /// **容差内不算改进**：1e-12 量级的对偶噪声不该被当成改进列，否则列生成会在原地反复迭代。
    /// **Within tolerance is not improving**: dual noise around 1e-12 must not count, or column generation loops in place.
    /// </summary>
    [Fact]
    public void IsImprovingColumn_ShouldRespectTolerance() {
        Layer<FltX> layer = LayerOf("a", 2, 2);

        // 检验数恰为 -1e-9，落在默认容差 1e-6 之内。
        // The reduced cost is exactly -1e-9, inside the default tolerance of 1e-6.
        LayerAssignmentMath
            .IsImprovingColumn(
                layer,
                new[] { AmountDemand("a") },
                Prices(("a", 4.000000001)))
            .Should().BeFalse();
    }

    /// <summary>比较器按检验数升序排列 / The comparator orders by ascending reduced cost.</summary>
    [Fact]
    public void ReducedCostComparator_ShouldOrderAscending() {
        Layer<FltX> small = LayerOf("a", 2, 2);
        Layer<FltX> large = LayerOf("a", 4, 4);

        var layers = new List<Layer<FltX>> { large, small };
        layers.Sort(LayerAssignmentMath.ReducedCostComparator(
            new[] { AmountDemand("a") }, Prices(("a", 5))));

        // 检验数：小层 4 − 5 = −1，大层 16 − 5 = 11，升序应把小层排前。
        // Reduced costs: small = 4 − 5 = −1, large = 16 − 5 = 11, so ascending puts the small one first.
        layers[0].Should().BeSameAs(small);
        layers[1].Should().BeSameAs(large);
    }

    /// <summary>影子价格键的相等性只看需求种类与物品编号 / Key equality looks only at kind and item id.</summary>
    [Fact]
    public void ShadowPriceKey_ShouldEqualOnKindAndItem() {
        new Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId("a"))
            .Equals(new Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId("a")))
            .Should().BeTrue();
        new Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId("a"))
            .Equals(new Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode.ItemArea, new ItemId("a")))
            .Should().BeFalse();
    }

    /// <summary>键名称包含需求种类与物品编号 / The key name carries kind and item id.</summary>
    [Fact]
    public void ShadowPriceKey_ShouldRenderName() =>
        new Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode.ItemArea, new ItemId("a"))
            .Name.Should().Contain("ItemArea").And.Contain("a");

    /// <summary>构造板材类型 / Build a sheet type.</summary>
    private static SheetType<FltX> SheetTypeOf() =>
        new(Meters(10), Meters(10), Meters(0), new SheetTypeId("T"));

    /// <summary>用给定放置构造层 / Build a layer over the given placements.</summary>
    private static Layer<FltX> LayerOf(IReadOnlyList<Placement2<FltX>> units) => new(
        0, null, SheetTypeOf(), new Sheet2Shape<FltX>(Meters(10), Meters(10)), units);

    /// <summary>构造含若干同编号放置的层 / Build a layer holding several placements of one item.</summary>
    private static Layer<FltX> LayerOf(string itemId, double w, double h, int copies = 1) =>
        LayerOf(Enumerable.Range(0, copies)
            .Select(index => Placed(index * w, 0, w, h, itemId))
            .ToList());

    /// <summary>构造放置 / Build a placement.</summary>
    private static Placement2<FltX> Placed(
        double x, double y, double w, double h, string? itemId) =>
        new(
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            new QuantityPoint2<FltX>(Meters(x), Meters(y)),
            false,
            itemId);

    /// <summary>构造件数需求 / Build an amount demand.</summary>
    private static Bpp2dAssignmentDemandEntry AmountDemand(string itemId) => new(
        Bpp2dAssignmentDemandMode.ItemAmount,
        new ItemId(itemId),
        new Bpp2dAssignmentDemandValue.Amount(1));

    /// <summary>构造面积需求 / Build an area demand.</summary>
    private static Bpp2dAssignmentDemandEntry AreaDemand(string itemId) => new(
        Bpp2dAssignmentDemandMode.ItemArea,
        new ItemId(itemId),
        new Bpp2dAssignmentDemandValue.Area(Meters(1)));

    /// <summary>构造影子价格映射 / Build a shadow price map.</summary>
    private static IReadOnlyDictionary<Bpp2dDemandShadowPriceKey, FltX> Prices(
        params (string ItemId, double Price)[] entries) =>
        entries.ToDictionary(
            entry => new Bpp2dDemandShadowPriceKey(
                Bpp2dAssignmentDemandMode.ItemAmount, new ItemId(entry.ItemId)),
            entry => new FltX((decimal)entry.Price));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
