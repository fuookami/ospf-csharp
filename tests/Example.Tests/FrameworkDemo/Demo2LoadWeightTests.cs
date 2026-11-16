// 装载模型此前的判据：重量符号与已装载符号 / Criteria for the second load slice: weight and loaded symbols.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **重量与已装载符号** / Weight and loaded symbols.
/// </summary>
/// <remarks>
/// 四族符号，两两成对：
///
/// | 族 | 与谁成对 | 差在哪 |
/// | --- | --- | --- |
/// | `load_weight` | `actual_load_weight` | 前者多 `y`/`z` 两项，且**只在多装载方案下存在** |
/// | `estimate_loaded` | `actual_loaded` | 前者是内核的占用变量，后者是"不可用恒 1 / 单槽位即装载量 / 多槽位二值化" |
///
/// 把这四族写成同一族是这一带最典型的错：编译过、能解、报告里数字看着合理，只是"推荐"和"实际"永远相等，
/// 于是多装载方案的整个意义消失。
///
/// Four symbol families, in two pairs: `load_weight` vs `actual_load_weight` (the former carries two extra terms and **exists only under the multi-schema**) and `estimate_loaded`
/// vs `actual_loaded` (the former is the kernel's occupancy variable, the latter is "constant one when unavailable / the amount when single-slot / a binarization when
/// multi-slot"). Collapsing the four into one family is the classic mistake here: it compiles, it solves, the numbers look plausible — except "recommended" always equals
/// "actual" and the multi-loading schema means nothing.
/// </remarks>
public class Demo2LoadWeightTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(string name, double weight) =>
        new(
            name: name,
            weight: weight,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromCode(CargoCode.BAL),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(
        string name,
        ulong id,
        ulong mla,
        bool stowageNeeded = true,
        bool predicateWeightNeeded = true,
        bool recommendedWeightNeeded = true) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(0.0, 1.0, -0.5, 0.5),
            shape: new PositionShape(1.0, 1.0, 1.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(
                stowageNeeded: stowageNeeded,
                adjustmentNeeded: false,
                predicateWeightNeeded: predicateWeightNeeded,
                recommendedWeightNeeded: recommendedWeightNeeded),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: mla,
            // 起"需要谓词重量"就必须带三元组规格：`Load` 的松弛右端取 `min`，缺规格是**错误而不是跳过**
            // （跳过会让 `y` 无下界参照，模型照解）。这批既有 fixture 默认给了 `mlw` 那一口径的值。
            // Since "needs the predicate weight" requires the triple: `Load`'s slack takes `min` and a missing specification is an **error, not a skip** (skipping leaves
            // `y` with no reference bound and the model still solves). These pre-existing fixtures default the bounds to the `mlw` figure.
            plw: new PositionPredicateLoadWeight(Plw: 1000.0, Min: 0.0, Max: 1000.0));
    }

    private static DiscreteLoadingContext<Flt64> MakeKernel(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions) {
        var loadingItems = items.Select(item => new LoadingItem<Flt64>(item.Name, Grams(item.Weight))).ToList();
        var loadingPositions = positions
            .Select(position => new LoadingPosition<Flt64>(position.Name, position.Mla, Grams(position.Mlw.Mlw)))
            .ToList();
        var allowed = new HashSet<AssignmentKey>();
        for (int i = 0; i < items.Count; ++i) {
            for (int j = 0; j < positions.Count; ++j) {
                if (Stowage.StowageNeeded(items[i], positions[j])) {
                    allowed.Add(new AssignmentKey(items[i].Name, positions[j].Name));
                }
            }
        }
        var problem = new DiscreteLoadingProblem<Flt64>(
            Items: loadingItems,
            Positions: loadingPositions,
            AllowedAssignments: allowed,
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
        return new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter());
    }

    private static Load Register(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        Stowage stowage,
        bool multiSchema,
        LinearMetaModel<Flt64> model) {
        stowage.Register(model).IsOk.Should().BeTrue();
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: multiSchema);
        load.Register(model).IsOk.Should().BeTrue();
        return load;
    }

    /// <summary>
    /// **`load_weight` 比 `actual_load_weight` 多 `y`/`z` 两项** / `load_weight` carries two terms `actual_load_weight` does not.
    /// </summary>
    /// <remarks>
    /// 成对断言：两者都含 2 个货物重量项（系数是货物重量），但只有 `load_weight` 含 `y_P1` 与 `z_P1`。
    /// 只断言"有重量项"会让两族被写成同一个符号——那样推荐与实际永远相等。
    ///
    /// Paired: both carry the two item-weight terms (weighted by item weight), but only `load_weight` carries `y_P1` and `z_P1`. Asserting only "has weight terms" would pass if both
    /// families were the same symbol — leaving "recommended" permanently equal to "actual".
    /// </remarks>
    [Fact]
    public void LoadWeight_ShouldAddTheSlackAndRecommendedTermsThatActualDoesNot() {
        var items = new List<Item> { MakeItem("I1", 100.0), MakeItem("I2", 250.0) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 3UL) };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        using var model = new LinearMetaModel<Flt64>();
        Load load = Register(items, positions, stowage, multiSchema: true, model);

        LinearExpressionSymbol estimate = load.EstimateLoadWeightSymbol(0)!;
        LinearExpressionSymbol actual = load.ActualLoadWeightSymbol(0)!;

        estimate.Name.Should().Be("load_weight_P1");
        actual.Name.Should().Be("actual_load_weight_P1");

        actual.Polynomial.Monomials.Should().HaveCount(2, "the actual weight is items only");
        actual.Polynomial.Monomials.Select(monomial => monomial.Coefficient.ToDouble())
            .Should().BeEquivalentTo(new[] { 100.0, 250.0 });

        estimate.Polynomial.Monomials.Should().HaveCount(4, "the estimate adds y and z on top of the two items");
        estimate.Polynomial.Monomials.Select(monomial => monomial.Symbol)
            .Should().Contain(new Fuookami.Ospf.Math.Symbol.ISymbol[] { load.Y![0], load.Z![0] }, "the slack and the recommended count are both in the estimate");
        actual.Polynomial.Monomials.Select(monomial => monomial.Symbol)
            .Should().NotContain(load.Y![0], "the actual weight carries no predicate slack");
    }

    /// <summary>
    /// **`load_weight` 只在多装载方案下存在** / `load_weight` exists only under the multi-schema.
    /// </summary>
    /// <remarks>
    /// 单方案下 `y`/`z` 不存在，`load_weight` 若仍然建出来就退化成 `actual_load_weight` 的复制品——
    /// 而"推荐重量"字段会在报告里有值，那个值是假的。
    ///
    /// Under the single schema `y`/`z` do not exist; building `load_weight` anyway makes it a copy of `actual_load_weight`, and the report then shows a "recommended weight" that is
    /// simply the actual one.
    /// </remarks>
    [Fact]
    public void LoadWeight_ShouldNotExistUnderTheSingleLoadingSchema() {
        var items = new List<Item> { MakeItem("I1", 100.0) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 1UL) };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        using var model = new LinearMetaModel<Flt64>();
        Load load = Register(items, positions, stowage, multiSchema: false, model);

        load.EstimateLoadWeightSymbol(0).Should().BeNull("no multi-schema means no recommended weight");
        load.ActualLoadWeightSymbol(0).Should().NotBeNull("the actual weight is schema-independent");
    }

    /// <summary>
    /// **`estimate_loaded` 是内核的占用变量** / `estimate_loaded` is the kernel's occupancy variable.
    /// </summary>
    /// <remarks>
    /// 与"数量"成对：它是 `occupied_P1` 这个变量，不是 `load_amount`，也不是项数。取错了会让"舱位是否被占用"与
    /// 内核的容量联动脱钩。
    ///
    /// Paired with the amount: it is the variable `occupied_P1`, not `load_amount` and not a count. Taking the wrong one decouples "is the position occupied" from the kernel's
    /// capacity linkage.
    /// </remarks>
    [Fact]
    public void EstimateLoaded_ShouldBeTheKernelOccupancyVariable() {
        var items = new List<Item> { MakeItem("I1", 100.0) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 3UL) };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        using var model = new LinearMetaModel<Flt64>();
        Load load = Register(items, positions, stowage, multiSchema: false, model);

        LinearExpressionSymbol loaded = load.EstimateLoadedSymbol(0)!;
        loaded.Name.Should().Be("estimate_loaded_P1");
        loaded.Polynomial.Monomials.Should().ContainSingle();
        loaded.Polynomial.Monomials[0].Symbol.Should().BeSameAs(
            stowage.LoadingContext!.Aggregation.Occupied["P1"],
            "the occupancy variable comes from the kernel, not from a recomputed count");
    }

    /// <summary>
    /// **`actual_loaded` 的三条分支各走各的** / The three `actual_loaded` branches each take their own path.
    /// </summary>
    /// <remarks>
    /// 多位舱位 → 二值化函数（不是装载量本身）；单位舱位 → 装载量；不可用 → 常数 1。
    /// 三条写成一条时，多槽位舱位的"是否装载"会变成"装了几件"——一个大于 1 的数被当成布尔用。
    ///
    /// Multi-slot → a binarization (not the amount itself); single-slot → the amount; unavailable → constant one. Collapsed into one, a multi-slot position's "is it loaded" becomes
    /// "how many pieces did it take" — a number greater than one used as a boolean.
    /// </remarks>
    [Fact]
    public void ActualLoaded_ShouldTakeTheBranchMatchingItsPosition() {
        var items = new List<Item> { MakeItem("I1", 100.0) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 3UL) };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        using var model = new LinearMetaModel<Flt64>();
        Load multi = Register(items, positions, stowage, multiSchema: false, model);
        ILinearIntermediateSymbol<Flt64> multiLoaded = multi.ActualLoadedSymbol(0)!;
        multiLoaded.Should().BeAssignableTo<LinearFunctionSymbolAdapter<Flt64>>(
            "a multi-slot position needs the binarization adapter");

        var singleItems = new List<Item> { MakeItem("I1", 100.0) };
        var singlePositions = new List<StowagePosition> { MakePosition("P1", 1UL, 1UL) };
        var singleStowage = new Stowage(singleItems, singlePositions) {
            LoadingContext = MakeKernel(singleItems, singlePositions),
        };
        using var singleModel = new LinearMetaModel<Flt64>();
        Load single = Register(singleItems, singlePositions, singleStowage, multiSchema: false, singleModel);
        ILinearIntermediateSymbol<Flt64> singleLoaded = single.ActualLoadedSymbol(0)!;
        singleLoaded.Polynomial.Monomials.Should().HaveCount(1, "a single slot's actual loaded is its stowage amount");
        singleLoaded.Polynomial.Monomials[0].Symbol.Should().BeSameAs(singleStowage.StowageSymbol(0, 0));
    }

    /// <summary>
    /// **不可用舱位的 `actual_loaded` 是常数 1** / An unavailable position's `actual_loaded` is constant one.
    /// </summary>
    /// <remarks>
    /// 与 `estimate_loaded` 成对：`estimate_loaded` 取内核占用变量（不可用舱位在那里恒 1），`actual_loaded` 单独再写一次常数 1。
    /// 两者都得是 1，不能一个 1 一个 0。
    ///
 /// Paired with `estimate_loaded`: the implementation takes the kernel occupancy there (constant one for an unavailable position) and writes constant one again for `actual_loaded`.
    /// Both must be one, never one-and-zero.
    /// </remarks>
    [Fact]
    public void ActualLoaded_ShouldBeConstantOneForAnUnavailablePosition() {
        var items = new List<Item> { MakeItem("I1", 100.0) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 3UL, stowageNeeded: false) };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        using var model = new LinearMetaModel<Flt64>();
        Load load = Register(items, positions, stowage, multiSchema: false, model);

        ILinearIntermediateSymbol<Flt64> loaded = load.ActualLoadedSymbol(0)!;
        loaded.Polynomial.Monomials.Should().BeEmpty();
        loaded.Polynomial.Constant.ToDouble().Should().BeApproximately(1.0, 1e-9);
    }
}
