// 检验数的判据 / Criteria for the reduced cost.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **检验数** / The reduced cost.
/// </summary>
/// <remarks>
/// 判据的重点：**阈值是负容差而不是零**（浮点检验数不会精确为零）、**零影子价格的需求跳过**、
/// **只保留需求项里出现过的键**。
///
/// The criteria focus on **the threshold being the negative tolerance rather than zero** (a floating-point reduced cost is never exactly zero), on **demands with a zero shadow price being skipped**,
/// and on **only keys present in the demand entries surviving**.
/// </remarks>
public class ReducedCostTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Layer<FltX> Layer(double itemWidth, double itemHeight, string itemId) =>
        new(0L, null, null, new Sheet2Shape<FltX>(Q(100.0), Q(100.0)),
            new[] {
                new Placement2<FltX>(
                    Shape: new RectanglePackingShape2<FltX>(Q(itemWidth), Q(itemHeight)),
                    Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                    Rotated: false,
                    ItemId: itemId),
            });

    private static Bpp2dAssignmentDemandEntry AmountDemand(string itemId, ulong amount) =>
        new(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId(itemId),
            new Bpp2dAssignmentDemandValue.Amount(amount), null);

    /// <summary>
    /// **检验数 = 面积 × 系数 − Σ(价格 × 需求系数)** / The reduced cost is area × coefficient minus the dual sum.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 层面积 200、面积系数 1、一条需求价格 5、该列对该需求的系数 1 → 检验数 **200 − 5 = 195**。
    /// 2. **面积系数乘在面积上**，不是乘在整个结果上（系数 2 → 400 − 5 = 395）。
    /// 3. **零价格的需求被跳过**：加一条价格为零的需求，结果**不变**。
    /// 4. **没有价格的需求被跳过**：加一条完全不在价格表里的需求，结果也不变。
    ///
    /// Four paired points: an area of 200, area coefficient 1, one demand priced 5 with a coefficient of 1 gives **200 − 5 = 195**; **the area coefficient multiplies the area**, not the whole
    /// result (coefficient 2 gives 400 − 5 = 395); **a zero-priced demand is skipped**, adding one leaves the result **unchanged**; and **an unpriced demand is skipped** too.
    /// </remarks>
    [Fact]
    public void LayerReducedCost_ShouldSubtractTheDualSum() {
        Layer<FltX> layer = Layer(20.0, 10.0, "I1");
        Bpp2dAssignmentDemandEntry[] demands = { AmountDemand("I1", 5UL) };
        var prices = new Dictionary<Bpp2dDemandShadowPriceKey, FltX>
        {
            [new Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId("I1"))] = new FltX(5.0),
        };

        ReducedCost.LayerReducedCost(layer, demands, prices)
            .ToFlt64().ToDouble().Should().BeApproximately(195.0, 1e-9);

        ReducedCost.LayerReducedCost(layer, demands, prices, new FltX(2.0))
            .ToFlt64().ToDouble().Should().BeApproximately(395.0, 1e-9, "the coefficient multiplies the area");

        var zeroPriced = new Dictionary<Bpp2dDemandShadowPriceKey, FltX>
        {
            [new Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId("I1"))] = new FltX(5.0),
            [new Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode.ItemArea, new ItemId("I2"))] = new FltX(0.0),
        };
        Bpp2dAssignmentDemandEntry[] withZero = {
            AmountDemand("I1", 5UL),
            new(Bpp2dAssignmentDemandMode.ItemArea, new ItemId("I2"),
                new Bpp2dAssignmentDemandValue.Area(Q(50.0)), null),
        };
        ReducedCost.LayerReducedCost(layer, withZero, zeroPriced)
            .ToFlt64().ToDouble().Should().BeApproximately(195.0, 1e-9, "a zero price changes nothing");

        Bpp2dAssignmentDemandEntry[] withUnpriced = {
            AmountDemand("I1", 5UL),
            AmountDemand("I9", 3UL),
        };
        ReducedCost.LayerReducedCost(layer, withUnpriced, prices)
            .ToFlt64().ToDouble().Should().BeApproximately(195.0, 1e-9, "an unpriced demand is skipped, not treated as zero");
    }

    /// <summary>
    /// **只保留需求项里出现过的键** / Only keys present in the demand entries survive.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 价格表里有一个不在需求项里的键 → **结果里没有它**。
    /// 2. 需求项里有但价格表没有 → 也不补。
    /// 3. 模式不同就是不同的键：件数需求与面积需求各一个，互不覆盖。
    ///
    /// Three paired points: a key in the price table that no demand entry mentions **does not survive**; a demand entry missing from the price table is not filled in; and different modes are
    /// different keys, so an amount demand and an area demand coexist without overwriting each other.
    /// </remarks>
    [Fact]
    public void ShadowPriceKeysOf_ShouldKeepOnlyDemandedKeys() {
        Bpp2dAssignmentDemandEntry[] demands = {
            AmountDemand("I1", 5UL),
            new(Bpp2dAssignmentDemandMode.ItemArea, new ItemId("I1"),
                new Bpp2dAssignmentDemandValue.Area(Q(50.0)), null),
            AmountDemand("I2", 3UL),
        };
        var prices = new Dictionary<DemandModeKey, FltX>
        {
            [new DemandModeKey("item_amount", "I1")] = new FltX(1.5),
            [new DemandModeKey("item_area", "I1")] = new FltX(2.5),
            [new DemandModeKey("item_amount", "I9")] = new FltX(9.0),
        };

        IReadOnlyDictionary<Bpp2dDemandShadowPriceKey, FltX> converted =
            ReducedCost.ShadowPriceKeysOf(demands, prices);

        converted.Should().HaveCount(2, "I9 is not demanded and I2 has no price");
        converted[new Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId("I1"))]
            .ToFlt64().ToDouble().Should().Be(1.5);
        converted[new Bpp2dDemandShadowPriceKey(Bpp2dAssignmentDemandMode.ItemArea, new ItemId("I1"))]
            .ToFlt64().ToDouble().Should().Be(2.5);
    }

    /// <summary>
    /// **筛选阈值是负容差，结果按检验数升序** / The filter threshold is the negative tolerance, and results are ascending.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 三个候选：检验数 −5、−0.0000005、+3 → **只留 −5**（−0.0000005 在容差内不算改进）。
    /// 2. **阈值是负容差而不是零**：容差 `1e-6` 时 `−0.0000005` 被排除；若阈值取零，它会被留下。
    /// 3. 结果按检验数**升序**（最负的在前）。
    /// 4. 每个留下的候选**带上算出来的检验数**，不是原值。
    ///
    /// Four paired points: three candidates with reduced costs −5, −0.0000005 and +3 leave **only −5**, since −0.0000005 is within tolerance and not an improvement; **the threshold is the negative
    /// tolerance rather than zero**, so with a `1e-6` tolerance −0.0000005 is excluded where a zero threshold would keep it; results are **ascending** by reduced cost; and each survivor **carries the
    /// computed reduced cost** rather than its original value.
    /// </remarks>
    [Fact]
    public void Filter_ShouldKeepOnlyGenuineImprovements() {
        Bpp2dAssignmentDemandEntry[] demands = { AmountDemand("I1", 1UL) };
        // **四个候选的面积不同**，这样同一个价格下检验数才不同：面积 205/100/150 → 检验数 5/−100/−50（价格 200）。
        // 前两个是真正的改进，**顺序必须是升序**（−100 在前）；205 面积那个是正检验数，被筛掉。
        // **The four candidates differ in area**, so one price of 200 yields different reduced costs: areas 205 / 100 / 150 give 5 / −100 / −50. The last two genuinely improve and **must come
        // ascending** (−100 first); the 205 one has a positive reduced cost and is filtered out.
        Layer<FltX> losing = Layer(20.5, 10.0, "I1");
        Layer<FltX> winnerBig = Layer(10.0, 10.0, "I1");
        Layer<FltX> winnerSmall = Layer(15.0, 10.0, "I1");
        Bpp2dLayerGenerationResult<FltX>[] candidates = {
            new Bpp2dLayerGenerationResult<FltX>(losing),
            new Bpp2dLayerGenerationResult<FltX>(winnerSmall),
            new Bpp2dLayerGenerationResult<FltX>(winnerBig),
        };
        var prices = new Dictionary<DemandModeKey, FltX>
        {
            [new DemandModeKey("item_amount", "I1")] = new FltX(200.0),
        };
        var state = new ColumnGenerationState(0, Array.Empty<Layer<FltX>>(), null, prices);
        var filter = new ReducedCostFilter(demands);

        IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> filtered = filter.Filter(state, candidates);
        filtered.Should().HaveCount(2, "only the genuinely improving candidates survive");
        filtered[0].Layer.Should().BeSameAs(winnerBig, "the most negative reduced cost comes first");
        filtered[0].ReducedCost!.Value.ToFlt64().ToDouble().Should().BeApproximately(-100.0, 1e-6,
            "the survivor carries the computed reduced cost");
        filtered[1].Layer.Should().BeSameAs(winnerSmall);
        filtered[1].ReducedCost!.Value.ToFlt64().ToDouble().Should().BeApproximately(-50.0, 1e-6);

        // **容差边界**：检验数 −5e-7 在默认容差 1e-6 内，不算改进；容差收紧到 1e-9 时才算。
        // **The tolerance edge**: a reduced cost of −5e-7 is inside the default 1e-6 tolerance and not an improvement; a tighter 1e-9 tolerance admits it.
        Layer<FltX> marginal = Layer(19.99999995, 10.0, "I1");
        filter.Filter(state, new[] { new Bpp2dLayerGenerationResult<FltX>(marginal) })
            .Should().BeEmpty("−5e-7 is inside the 1e-6 tolerance");
        ReducedCostFilter looseFilter = new(demands, tolerance: new FltX(1e-9));
        looseFilter.Filter(state, new[] { new Bpp2dLayerGenerationResult<FltX>(marginal) })
            .Should().HaveCount(1, "a tighter tolerance admits it");    }
}
