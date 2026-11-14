// 建列请求构造器（B658）的判据 / Criteria for the layer-request builder (B658).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **建列请求构造器（B658）** / The layer-request builder (B658).
/// </summary>
/// <remarks>
/// 判据的重点：**候选板材取主板材**（无主板材才退到第一块）、**目录里没有的物品跳过**、
/// **每轮列数来自列生成配置**（不是执行器配置）。
///
/// The criteria focus on **the candidate sheet being the main one**, falling back to the first only when none is main, on **items absent from the catalogue being skipped**, and on **the per-round
/// column count coming from the column-generation config** rather than the executor config.
/// </remarks>
public class StandardLayerRequestBuilderTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static SheetType<FltX> SheetType(string code, bool isMain) =>
        new(Q(100.0), Q(100.0), Q(1000.0), new SheetTypeId(code), isMain);

    private static ActualItem<FltX> Item(string id) =>
        new(new ItemId(id), id, new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
            AllowRotate: true, Weight: Q(1.0));

    private static StandardLayerRequestBuilder Builder(
        IReadOnlyDictionary<ItemId, ulong>? items = null,
        IReadOnlyDictionary<ItemId, ActualItem<FltX>>? catalogue = null,
        IReadOnlyList<SheetType<FltX>>? sheets = null) =>
        new(
            items ?? new Dictionary<ItemId, ulong>(),
            catalogue ?? new Dictionary<ItemId, ActualItem<FltX>>(),
            sheets ?? Array.Empty<SheetType<FltX>>());

    /// <summary>
    /// **候选板材取主板材，没有主板材才退到第一块** / The candidate sheet is the main one, falling back to the first.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 三块板材里第二块是主板材 → 候选是**第二块**，不是第一块。
    /// 2. 没有主板材 → 退到**第一块**（兜底，不是"随便一块"）。
    /// 3. 一块都没有 → **null**，不抛。
    /// 4. 请求里同时带候选板材与全部板材：生成器两者都要（候选用于单个排版，全部用于选型）。
    ///
    /// Four paired points: with the second of three sheets main the candidate is **the second**, not the first; with no main sheet it falls back to **the first**; with no sheets at all it is **null**
    /// rather than a throw; and the request carries both the candidate and every sheet, since the generator needs the candidate for a single layout and the full set for type selection.
    /// </remarks>
    [Fact]
    public void CandidateSheet_ShouldPreferTheMainSheet() {
        SheetType<FltX>[] sheets = { SheetType("A", false), SheetType("B", true), SheetType("C", false) };
        StandardLayerRequestBuilder builder = Builder(sheets: sheets);
        builder.CandidateSheet!.TypeCode.Value.Should().Be("B");

        StandardLayerRequestBuilder noMain = Builder(sheets: new[] { SheetType("X", false), SheetType("Y", false) });
        noMain.CandidateSheet!.TypeCode.Value.Should().Be("X", "the first sheet is the fallback");

        Builder().CandidateSheet.Should().BeNull("no sheets means no candidate, not a throw");

        Bpp2dLayerGenerationRequest<FltX> request = builder.Build(new ColumnGenerationState(3, Array.Empty<Layer<FltX>>()), new ColumnGenerationConfig());
        request.Iteration.Should().Be(3);
        request.Sheet!.TypeCode.Value.Should().Be("B");
        request.Participants.Should().HaveCount(3, "every sheet type takes part");
    }

    /// <summary>
    /// **目录里没有的物品跳过，每轮列数来自列生成配置** / Catalogue-missing items are skipped, and the column cap comes from the column-generation config.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两个物品需求、目录里只有一个 → **清单只有一项**，不抛。
    /// 2. 数量原样带过去。
    /// 3. **`MaxCandidates` 等于列生成配置的 `MaxColumnsPerIteration`**——不是执行器配置里的某个数。
    /// 4. 已有列与影子价格**原样透传**，请求是装配而不是拷贝计算。
    ///
    /// Four paired points: two item demands with one in the catalogue yield **one entry** without throwing; the amount passes through; **`MaxCandidates` equals the column-generation config's
    /// `MaxColumnsPerIteration`** rather than some executor-config number; and the existing columns and shadow prices **pass through unchanged**, since the request assembles rather than
    /// recomputes.
    /// </remarks>
    [Fact]
    public void Build_ShouldSkipCatalogueMissesAndTakeTheCapFromTheColumnConfig() {
        Dictionary<ItemId, ulong> items = new()
        {
            [new ItemId("I1")] = 5UL,
            [new ItemId("I2")] = 7UL,
        };
        Dictionary<ItemId, ActualItem<FltX>> catalogue = new()
        {
            [new ItemId("I1")] = Item("I1"),
        };
        StandardLayerRequestBuilder builder = Builder(items, catalogue, new[] { SheetType("A", true) });

        IReadOnlyList<(ActualItem<FltX> Item, int Amount)> generation = builder.GenerationItems();
        generation.Should().HaveCount(1, "I2 is not in the catalogue");
        generation[0].Amount.Should().Be(5);

        Layer<FltX>[] columns = {
            new(0L, null, SheetType("A", true), new Sheet2Shape<FltX>(Q(100.0), Q(100.0)),
                new[] {
                    new Placement2<FltX>(
                        Shape: new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
                        Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                        Rotated: false,
                        ItemId: "I1"),
                }),
        };
        Dictionary<DemandModeKey, FltX> prices = new() { [new DemandModeKey("item_amount", "I1")] = new FltX(2.5) };
        ColumnGenerationState state = new(4, columns, null, prices);
        Bpp2dLayerGenerationRequest<FltX> request = builder.Build(state, new ColumnGenerationConfig(IterationLimit: 8, MaxColumnsPerIteration: 11));

        request.MaxCandidates.Should().Be(11, "the cap comes from the column-generation config");
        request.LayerList.Should().HaveCount(1, "the existing columns pass through");
        request.ShadowPriceMap.Should().HaveCount(1);
        request.ShadowPriceMap![new DemandModeKey("item_amount", "I1")].ToFlt64().ToDouble().Should().Be(2.5);
    }

    /// <summary>
    /// **没有物品或板材时仍能构造请求** / A request is still built with no items or sheets.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 空物品、空板材 → **请求仍然构造成功**，不抛。
    /// 2. 候选板材为 null，物品清单为空。
    /// 3. 锯齿缝宽为 null 时请求里也是 null（不是零——零缝宽与"没配置"是两回事）。
    ///
    /// Three paired points: empty items and sheets still yield **a request**, not a throw; the candidate sheet is null and the item list empty; and a null kerf stays null in the request rather than
    /// becoming zero, since zero kerf differs from an unconfigured one.
    /// </remarks>
    [Fact]
    public void Build_ShouldTolerateEmptyInputs() {
        Bpp2dLayerGenerationRequest<FltX> request = Builder().Build(
            new ColumnGenerationState(0, Array.Empty<Layer<FltX>>()),
            new ColumnGenerationConfig());

        request.Sheet.Should().BeNull();
        request.ItemList.Should().BeEmpty();
        request.Kerf.Should().BeNull("zero kerf differs from an unconfigured one");
        request.MaxCandidates.Should().Be(32, "the column-generation default applies");
    }
}
