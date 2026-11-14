// 需求聚合与物品上下文测试 / Tests for the demand aggregation and the item context.

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
/// 需求聚合与物品上下文 / Demand aggregation and item context.
/// </summary>
public sealed class AggregationTest {
    /// <summary>初始剩余需求等于原始需求 / The initial remainder equals the original demand.</summary>
    [Fact]
    public void RestItems_ShouldStartAtDemand() =>
        AggregationOf(new Dictionary<ItemId, ulong> { [Id("a")] = 5 }).RestItems[Id("a")].Should().Be(5UL);

    /// <summary>使用后剩余减少 / Using items reduces the remainder.</summary>
    [Fact]
    public void UseItems_ShouldReduceRest() {
        Aggregation<FltX> aggregation = AggregationOf(new Dictionary<ItemId, ulong> { [Id("a")] = 5 });
        aggregation.UseItems(new Dictionary<ItemId, ulong> { [Id("a")] = 3 });

        aggregation.RestItems[Id("a")].Should().Be(2UL);
    }

    /// <summary>用满后该物品从剩余集合消失 / A fully used item leaves the remainder set.</summary>
    [Fact]
    public void UseItems_ShouldDropFullyUsedItem() {
        Aggregation<FltX> aggregation = AggregationOf(new Dictionary<ItemId, ulong> { [Id("a")] = 5 });
        aggregation.UseItems(new Dictionary<ItemId, ulong> { [Id("a")] = 5 });

        aggregation.RestItems.Should().NotContainKey(Id("a"));
    }

    /// <summary>
    /// **超额使用钳到零而不是负剩余**：负剩余会让下游以为还需要更多件。
    /// **Over-use clamps to zero rather than negative**: a negative remainder would tell downstream more pieces are needed.
    /// </summary>
    [Fact]
    public void UseItems_ShouldClampOverUse() {
        Aggregation<FltX> aggregation = AggregationOf(new Dictionary<ItemId, ulong> { [Id("a")] = 5 });
        aggregation.UseItems(new Dictionary<ItemId, ulong> { [Id("a")] = 9 });

        aggregation.RestItems.Should().NotContainKey(Id("a"));
    }

    /// <summary>不限量的板材剩余为 MaxValue / An unlimited sheet type reports MaxValue.</summary>
    [Fact]
    public void RestSheets_ShouldReportUnlimited() =>
        AggregationOf(
            new Dictionary<ItemId, ulong>(),
            new Dictionary<SheetType<FltX>, ulong?> { [Sheet("T1")] = null })
            .RestSheets[Sheet("T1")].Should().Be(ulong.MaxValue);

    /// <summary>有上限的板材使用后剩余减少 / A limited sheet type's remainder decreases after use.</summary>
    [Fact]
    public void UseSheets_ShouldReduceRest() {
        Aggregation<FltX> aggregation = AggregationOf(
            new Dictionary<ItemId, ulong>(),
            new Dictionary<SheetType<FltX>, ulong?> { [Sheet("T1")] = 4UL });
        aggregation.UseSheets(new Dictionary<SheetType<FltX>, ulong> { [Sheet("T1")] = 1UL });

        aggregation.RestSheets[Sheet("T1")].Should().Be(3UL);
    }

    /// <summary>释放板材后剩余回升 / Releasing sheets raises the remainder again.</summary>
    [Fact]
    public void ReleaseSheets_ShouldRestore() {
        Aggregation<FltX> aggregation = AggregationOf(
            new Dictionary<ItemId, ulong>(),
            new Dictionary<SheetType<FltX>, ulong?> { [Sheet("T1")] = 4UL });
        aggregation.UseSheets(new Dictionary<SheetType<FltX>, ulong> { [Sheet("T1")] = 2UL });
        aggregation.ReleaseSheets(new Dictionary<SheetType<FltX>, ulong> { [Sheet("T1")] = 1UL });

        aggregation.RestSheets[Sheet("T1")].Should().Be(3UL);
    }

    /// <summary>
    /// **过度释放钳到零，不会把已用量变负**：负的已用量会让剩余超过上限，比少释放更危险。
    /// **Over-release clamps to zero and never makes used negative**: a negative used amount would push the remainder above
    /// the limit, worse than under-releasing.
    /// </summary>
    [Fact]
    public void ReleaseSheets_ShouldClampToZero() {
        Aggregation<FltX> aggregation = AggregationOf(
            new Dictionary<ItemId, ulong>(),
            new Dictionary<SheetType<FltX>, ulong?> { [Sheet("T1")] = 4UL });
        aggregation.UseSheets(new Dictionary<SheetType<FltX>, ulong> { [Sheet("T1")] = 1UL });
        aggregation.ReleaseSheets(new Dictionary<SheetType<FltX>, ulong> { [Sheet("T1")] = 5UL });

        aggregation.RestSheets[Sheet("T1")].Should().Be(4UL);
    }

    /// <summary>用满的板材从剩余集合消失 / A fully used sheet type leaves the remainder set.</summary>
    [Fact]
    public void UseSheets_ShouldDropExhaustedType() {
        Aggregation<FltX> aggregation = AggregationOf(
            new Dictionary<ItemId, ulong>(),
            new Dictionary<SheetType<FltX>, ulong?> { [Sheet("T1")] = 1UL });
        aggregation.UseSheets(new Dictionary<SheetType<FltX>, ulong> { [Sheet("T1")] = 1UL });

        aggregation.RestSheets.Should().NotContainKey(Sheet("T1"));
    }

    /// <summary>显式标记的主板材优先 / The explicitly flagged main sheet wins.</summary>
    [Fact]
    public void MainSheet_ShouldPreferExplicitFlag() =>
        AggregationOf(
            new Dictionary<ItemId, ulong>(),
            new Dictionary<SheetType<FltX>, ulong?> {
                [Sheet("big", 20, 20, isMain: false)] = null,
                [Sheet("flagged", 5, 5, isMain: true)] = null,
            })
            .MainSheet.Should().Be(Sheet("flagged", 5, 5, isMain: true),
                "显式标记是业务意图，面积最大只是兜底 / an explicit flag is intent, largest-by-area is only a fallback");

    /// <summary>无显式标记时取面积最大者 / Without a flag the largest by area wins.</summary>
    [Fact]
    public void MainSheet_ShouldFallBackToLargest() =>
        AggregationOf(
            new Dictionary<ItemId, ulong>(),
            new Dictionary<SheetType<FltX>, ulong?> {
                [Sheet("small", 5, 5, isMain: false)] = null,
                [Sheet("big", 20, 20, isMain: false)] = null,
            })
            .MainSheet.Should().Be(Sheet("big", 20, 20, isMain: false));

    /// <summary>无板材类型时主板材为空 / With no sheet type the main sheet is null.</summary>
    [Fact]
    public void MainSheet_ShouldBeNullWithoutTypes() =>
        AggregationOf(new Dictionary<ItemId, ulong>()).MainSheet.Should().BeNull();

    /// <summary>需求面积等于单件面积乘件数之和 / The demanded area is unit area times amount, summed.</summary>
    [Fact]
    public void DemandArea_ShouldSumScaledAreas() {
        var items = new Dictionary<ItemId, IAbstractItem<FltX>> {
            [Id("a")] = Item("a", 2, 3),
            [Id("b")] = Item("b", 4, 5),
        };
        Aggregation<FltX> aggregation = AggregationOf(
            new Dictionary<ItemId, ulong> { [Id("a")] = 2, [Id("b")] = 1 });

        aggregation.DemandArea(items)!.Value.ToFlt64().ToDouble()
            .Should().BeApproximately(32.0, 1e-9, "2×6 + 1×20 / 2 times 6 plus 1 times 20");
    }

    /// <summary>
    /// **目录缺失任一需求物品时返回 null，而不是跳过**：跳过会让面积偏小，下游估算板材数不足。
    /// **Returns null when the catalogue lacks a demanded item rather than skipping it**: skipping understates the area
    /// and downstream estimates too few sheets.
    /// </summary>
    [Fact]
    public void DemandArea_ShouldReturnNullForMissingItem() {
        var items = new Dictionary<ItemId, IAbstractItem<FltX>> { [Id("a")] = Item("a", 2, 3) };

        AggregationOf(new Dictionary<ItemId, ulong> { [Id("a")] = 1, [Id("missing")] = 1 })
            .DemandArea(items).Should().BeNull();
    }

    /// <summary>装载率估算按面积比再除以装载率 / The estimate divides the area ratio by the target loading rate.</summary>
    [Fact]
    public void EstimateSheetAmount_ShouldDivideByRate() {
        Aggregation<FltX> aggregation = AggregationOf(new Dictionary<ItemId, ulong>());

        double estimate = aggregation
            .EstimateSheetAmount(Meters(100), Sheet("T1", 10, 10, isMain: false), new FltX(0.5m))
            .ToFlt64().ToDouble();

        estimate.Should().BeApproximately(2.0, 1e-9, "100/100 = 1 块，装载率 0.5 → 2 块 / 1 sheet at 100% → 2 at 50%");
    }

    /// <summary>
    /// **装载率为零或板材面积非正时估算为零**：否则会得到无穷大，下游拿它当板材数直接崩掉。
    /// **A zero rate or non-positive sheet area estimates zero**: otherwise the result is infinity and downstream, treating
    /// it as a sheet count, falls over.
    /// </summary>
    [Fact]
    public void EstimateSheetAmount_ShouldGuardDegenerateInput() {
        Aggregation<FltX> aggregation = AggregationOf(new Dictionary<ItemId, ulong>());

        aggregation.EstimateSheetAmount(Meters(100), Sheet("T1", 10, 10, isMain: false), new FltX(0m))
            .ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
        aggregation.EstimateSheetAmount(Meters(100), Sheet("T1", 0, 0, isMain: false), new FltX(0.5m))
            .ToFlt64().ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>
    /// **未给上限时登记全部已知板材类型**：只登记一部分会让后续排布找不到可用板材。
    /// **With no limits every known sheet type is registered**: registering only some would leave later packing with no
    /// usable sheet.
    /// </summary>
    [Fact]
    public void Aggregate_ShouldRegisterAllTypesWhenNoLimits() {
        Aggregation<FltX> aggregation = ContextOf().Aggregate();

        aggregation.SheetTypes.Should().HaveCount(2);
        aggregation.SheetTypes.Values.Should().OnlyContain(limit => limit == null);
    }

    /// <summary>
    /// **给了上限就完全采用给定映射，不再补入未列出的类型**：混用会让"我只要这两种板"变成"我还要所有已知板"。
    /// **Given limits, the mapping is used as-is with no unlisted type added**: mixing turns "I only want these two" into
    /// "plus all known sheets".
    /// </summary>
    [Fact]
    public void Aggregate_ShouldUseGivenLimitsAsIs() {
        SheetType<FltX> only = Sheet("T1");

        Aggregation<FltX> aggregation = ContextOf()
            .Aggregate(new Dictionary<SheetType<FltX>, ulong?> { [only] = 3UL });

        aggregation.SheetTypes.Should().ContainSingle();
        aggregation.SheetTypes[only].Should().Be(3UL);
    }

    /// <summary>空上限映射视为未给上限 / An empty limits map counts as no limits.</summary>
    [Fact]
    public void Aggregate_ShouldTreatEmptyLimitsAsNone() =>
        ContextOf()
            .Aggregate(new Dictionary<SheetType<FltX>, ulong?>())
            .SheetTypes.Should().HaveCount(2);

    /// <summary>上下文每次聚合都是新实例，互不共享记账 / Each aggregate call yields a fresh instance with no shared ledger.</summary>
    [Fact]
    public void Aggregate_ShouldIsolateLedgers() {
        Aggregation<FltX> first = ContextOf().Aggregate();
        first.UseItems(new Dictionary<ItemId, ulong> { [Id("a")] = 1 });

        ContextOf().Aggregate().RestItems[Id("a")].Should().Be(2UL, "新实例从未被使用 / a fresh instance was never used");
    }

    /// <summary>上下文的主板材类型 / The context's main sheet type.</summary>
    [Fact]
    public void Context_ShouldReportMainSheetType() =>
        ContextOf().MainSheetType().Should().Be(Sheet("T2", 20, 20, isMain: true));

    /// <summary>构造聚合 / Build an aggregation.</summary>
    private static Aggregation<FltX> AggregationOf(
        IReadOnlyDictionary<ItemId, ulong> demands,
        IReadOnlyDictionary<SheetType<FltX>, ulong?>? sheets = null) =>
        new(demands, sheets);

    /// <summary>构造物品上下文 / Build an item context.</summary>
    private static ItemContext<FltX> ContextOf() => new(
        new Dictionary<ItemId, ActualItem<FltX>> { [Id("a")] = Item("a", 2, 3) },
        new[] { Sheet("T1"), Sheet("T2", 20, 20, isMain: true) },
        new Dictionary<ItemId, ulong> { [Id("a")] = 2 });

    /// <summary>构造物品编号 / Build an item id.</summary>
    private static ItemId Id(string value) => new(value);

    /// <summary>构造板材类型 / Build a sheet type.</summary>
    private static SheetType<FltX> Sheet(
        string code, double w = 10, double h = 10, bool isMain = false) =>
        new(Meters(w), Meters(h), Meters(0), new SheetTypeId(code), isMain);

    /// <summary>构造矩形件 / Build a rectangle item.</summary>
    private static ActualItem<FltX> Item(string id, double w, double h) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Meters(w), Meters(h)), false, Meters(0));

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
