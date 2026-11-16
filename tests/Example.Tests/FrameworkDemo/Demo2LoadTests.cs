// 装载模型此前的判据 / Criteria for the first slice of the load model.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
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
/// **装载模型此前：变量、装载量、满载** / The first slice of the load model: variables, stowage amount, full.
/// </summary>
/// <remarks>
/// 基线 `Load.kt` 585 行，这里只落四样：`y`、`z`、`load_amount_{舱位}`、`full_{舱位}`。挑这四样是因为它们**互相依赖的最底层**
/// ——后面 585 行里的扭矩、CLI、重量符号全都建在 `load_amount` 之上。
///
/// 判据盯三处最容易安静出错的地方：
/// 1. `y`/`z` **只给需要的舱位建变量**（其余钉 0 且不进模型），否则求解规模虚增、且零范围变量会被误当决策；
/// 2. `load_amount` 的每一项必须指着**该舱位在 `stowage` 里的那批符号**，系数 1——重算一遍项数一样、变量不同；
/// 3. `full` 的三条分支（不可用 → 恒 1、单位 → 就是装载量、多位 → `SameAsFunction`）必须各走各的。
///
/// The baseline `Load.kt` is 585 lines; lands four things (`y`, `z`, `load_amount_{position}`, `full_{position}`) because they are the bottom of the dependency stack —
/// the torque/CLI/weight symbols all sit on top of `load_amount`. The criteria target the three places that fail quietly: variables only for positions that need them; every
/// `load_amount` term pointing at the **stowage** symbols of that position with coefficient 1 (recomputing gives the same count but different variables); and the three `full`
/// branches (unavailable → constant 1, single slot → the amount itself, multi-slot → `SameAsFunction`) each taking its own path.
/// </remarks>
public class Demo2LoadTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(string name) =>
        new(
            name: name,
            weight: 100.0,
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
        bool recommendedWeightNeeded = false) {
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

    private static (List<Item> Items, List<StowagePosition> Positions, Stowage Stowage) Prepare(
        ulong singleSlotMla = 1UL,
        ulong multiSlotMla = 3UL,
        bool unavailableFirst = false) {
        var items = new List<Item> { MakeItem("I1"), MakeItem("I2") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, singleSlotMla, stowageNeeded: !unavailableFirst,
                predicateWeightNeeded: true, recommendedWeightNeeded: true),
            MakePosition("P2", 2UL, multiSlotMla, predicateWeightNeeded: false, recommendedWeightNeeded: false),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        return (items, positions, stowage);
    }

    /// <summary>
    /// **`y`/`z` 只给需要的舱位建变量** / `y`/`z` are created only for the positions that need them.
    /// </summary>
    /// <remarks>
    /// 成对：需要谓词重量的舱位有 `y`（上界是 `mlw`），不需要的舱位**没有** `y` 且范围钉成 0；`z` 同理（上界是 `⌊mlw⌋`）。
    /// 只测"有的确实有"会让"全都建"通过——那样求解规模虚增，零范围变量还可能被别的行引用。
    ///
    /// Paired: a position needing the predicate weight has `y` bounded by `mlw`, one that does not **has none** and is pinned to zero; likewise `z` bounded by `⌊mlw⌋`. Testing only
    /// "the needed ones exist" would pass if everything were created — inflating the model and leaving zero-range variables for other rows to reference.
    /// </remarks>
    [Fact]
    public void Register_ShouldCreateWeightVariablesOnlyWhereNeeded() {
        (List<Item> items, List<StowagePosition> positions, Stowage stowage) = Prepare();
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        load.Y.Should().NotBeNull();
        load.Z.Should().NotBeNull();

        load.Y![0].Name.Should().Be("y_P1");
        load.Y[0].Range.Fixed.Should().BeFalse("P1 needs the predicate weight, so its weight variable is a real decision");
        load.Y[1].Range.Fixed.Should().BeTrue("P2 has predicateWeightNeeded = false, so y_P2 is pinned to zero");

        load.Z![0].Range.Fixed.Should().BeFalse("P1 needs the recommended weight, so z_P1 is a real decision");
        load.Z[1].Range.Fixed.Should().BeTrue("P2 has recommendedWeightNeeded = false, so z_P2 is pinned to zero");
    }

    /// <summary>
    /// **`y`/`z` 与多装载方案无关** / `y`/`z` do not depend on the multi-loading schema.
    /// </summary>
    /// <remarks>
    /// 基线里 `y`/`z` 不在任何 `if` 里，`if (withMultiLoadingSchema)` 只包 `estimateLoadWeight`。 曾把它们包进守卫，
    /// 结果单方案下"谓词重量松弛"拿不到 `y`。**这条判据是那一版错误理解的留档改写**：现在钉住"单方案下 `Y`/`Z` 仍然存在，
    /// 且范围按状态钉零"，与多方案成对。
    ///
    /// In the baseline `y`/`z` sit inside no `if` at all; `if (withMultiLoadingSchema)` wraps only `estimateLoadWeight`. wrongly placed them under the guard and the predicate
    /// weight slack then had no `y` under the single schema. **This criterion is the rewritten record of that misunderstanding**: it now pins "under the single schema `Y`/`Z` still
    /// exist, pinned to zero by status", paired with the multi-schema case.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildWeightVariablesRegardlessOfTheSchema() {
        (List<Item> items, List<StowagePosition> positions, Stowage stowage) = Prepare();
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: false);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        load.Register(model).IsOk.Should().BeTrue();

        load.Y.Should().NotBeNull("the baseline builds y outside every if");
        load.Z.Should().NotBeNull("the baseline builds z outside every if");
        load.Y![1].Range.Fixed.Should().BeTrue("P2 does not need the predicate weight, so y_P2 is pinned to zero");
        load.Z![1].Range.Fixed.Should().BeTrue("P2 does not need the recommended weight, so z_P2 is pinned to zero");
        load.EstimateLoadWeightSymbol(0).Should().BeNull(
            "the estimated load weight is the one thing the schema actually gates");
    }

    /// <summary>
    /// **`load_amount` 项数与该舱位的合法装载数一致，且指着 `stowage` 的符号** / `load_amount` has one term per legal pair, pointing at the `stowage` symbols.
    /// </summary>
    /// <remarks>
    /// 项数只说明"数量对"，说明不了"用对了变量"：自己按合法组合重建一批变量，项数一样、变量全不同，主问题与报告从此脱钩。
    ///
    /// The count only says "how many"; it says nothing about "which variables". Rebuilding a fresh set from the legal pairs yields the same count with entirely different variables,
    /// decoupling the master problem from the report.
    /// </remarks>
    [Fact]
    public void LoadAmount_ShouldAggregateTheStowageSymbolsOfItsPosition() {
        (List<Item> items, List<StowagePosition> positions, Stowage stowage) = Prepare();
        var load = new Load(items, positions, stowage);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        LinearExpressionSymbol amount0 = load.LoadAmountSymbol(0)!;
        amount0.Name.Should().Be("load_amount_P1");
        amount0.Polynomial.Monomials.Should().HaveCount(2, "both items are legal in P1");
        amount0.Polynomial.Monomials.Select(monomial => monomial.Symbol)
            .Should().Contain(new[] { stowage.StowageSymbol(0, 0)!, stowage.StowageSymbol(1, 0)! });
        amount0.Polynomial.Monomials.Should().OnlyContain(monomial => monomial.Coefficient.ToDouble() == 1.0);
    }

    /// <summary>
    /// **`load_amount` 的上界是舱位的 `mla`** / `load_amount` is bounded by the position's `mla`.
    /// </summary>
    /// <remarks>
    /// 基线给装载量加了 `range.leq(mla)`。少了这一行，一个 3 槽位舱位里装 5 件在模型层面完全合法——五个二元 `stowage`
    /// 变量各自为 1，加起来就是 5，"件数上限"只在报告里被当成常识，不在模型里。
    ///
    /// The baseline bounds the amount by `range.leq(mla)`. Without it a 3-slot position holding 5 pieces is perfectly legal: five binary `stowage` variables each set to one sum to
    /// five, and the piece limit lives only as a convention in the report, not in the model.
    /// </remarks>
    [Fact]
    public void LoadAmount_ShouldBeBoundedByThePositionMaximum() {
        (List<Item> items, List<StowagePosition> positions, Stowage stowage) = Prepare();
        var load = new Load(items, positions, stowage);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        LinearExpressionSymbol single = load.LoadAmountSymbol(0)!;
        single.Range.UpperBound.Should().NotBeNull("the amount is bounded");
        single.Range.UpperBound!.Value.Unwrap().ToDouble().Should().BeApproximately(1.0, 1e-9,
            "P1 has mla = 1, so at most one piece");

        LinearExpressionSymbol multi = load.LoadAmountSymbol(1)!;
        multi.Range.UpperBound!.Value.Unwrap().ToDouble().Should().BeApproximately(3.0, 1e-9,
            "P2 has mla = 3");
        single.Range.UpperBound!.Value.Unwrap().ToDouble().Should().NotBeApproximately(
            multi.Range.UpperBound!.Value.Unwrap().ToDouble(), 1e-9,
            "each position uses its own mla, not a shared constant");
    }

    /// <summary>
    /// **`full` 的三条分支各走各的** / The three `full` branches each take their own path.
    /// </summary>
    /// <remarks>
    /// 单位舱位（`mla == 1`）：`full` 就是 `load_amount`；多位舱位（`mla &gt; 1`）：`full` 是 `SameAsFunction`；
    /// 不可用舱位：`full` 恒 1。三条分支写成一条也能编译、也能解，只是某类舱位的满载判断会静默错掉。
    ///
    /// Single-slot (`mla == 1`): `full` is the amount; multi-slot (`mla > 1`): `full` is a `SameAsFunction`; unavailable: `full` is constant 1. Collapsing the three into one still
    /// compiles and still solves — it just makes the fullness of one kind of position silently wrong.
    /// </remarks>
    [Fact]
    public void Full_ShouldTakeTheBranchMatchingItsPosition() {
        (List<Item> items, List<StowagePosition> positions, Stowage stowage) = Prepare();
        var load = new Load(items, positions, stowage);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        ILinearIntermediateSymbol<Flt64> single = load.FullSymbol(0)!;
        single.Name.Should().Be("full_P1");
        single.Polynomial.Monomials.Should().HaveCount(2, "a single-slot position's full is exactly its stowage amount");

        ILinearIntermediateSymbol<Flt64> multi = load.FullSymbol(1)!;
        multi.Name.Should().Be("full_P2");
        multi.Should().BeAssignableTo<Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>>(
            "a multi-slot position needs the same-as function, not the amount itself");
    }

    /// <summary>
    /// **不可用舱位恒满（常数 1）** / An unavailable position is always full (constant one).
    /// </summary>
    /// <remarks>
    /// "不可用"与"空"是两件事：不可用舱位不参与装载，若它的 `full` 是 0，别的行会把它当成可用容量来用。
    /// 这条与"单位舱位"成对——两者都在 `full` 上，但一条是常数、一条是装载量。
    ///
    /// "Unavailable" and "empty" are not the same thing: an unavailable position takes no cargo, and if its `full` were 0 another row would count it as free capacity. This is paired
    /// with the single-slot case — both live in `full`, one as a constant and one as the amount.
    /// </remarks>
    [Fact]
    public void Full_ShouldBeConstantOneForAnUnavailablePosition() {
        (List<Item> items, List<StowagePosition> positions, Stowage stowage) = Prepare(unavailableFirst: true);
        var load = new Load(items, positions, stowage);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        ILinearIntermediateSymbol<Flt64> full = load.FullSymbol(0)!;
        full.Polynomial.Monomials.Should().BeEmpty("a constant has no terms");
        full.Polynomial.Constant.ToDouble().Should().BeApproximately(1.0, 1e-9);
    }
}
