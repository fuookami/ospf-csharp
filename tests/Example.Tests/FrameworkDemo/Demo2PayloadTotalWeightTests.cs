// 业载与总重的判据 / Criteria for payload and total weight.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **业载与总重（B585）** / Payload and total weight (B585).
/// </summary>
/// <remarks>
/// 两个空壳一起落地。业载这一层的核心是一张**按装载模式的分支表**：
///
/// | 模式 | 推荐业载取自 | 实际业载取自 |
/// | --- | --- | --- |
/// | `FullLoad` | **实际**装载重量 | 实际装载重量 |
/// | `Predistribution` / `WeightRecommendation` | **推荐**装载重量 | 实际装载重量 |
///
/// 把 `FullLoad` 的推荐业载写成推荐重量，是这个域最典型的错：能编译、能求解，只是"全配载"模式下报告的是规划值。
/// 总重这一层的核心是**零油重不加燃油**——那正是"零油重"的定义，而它同时是 `MaxLoadWeight` 分段规格的输入。
///
/// Both shells land together. Payload's core is a **branch table by loading mode** — `FullLoad` takes the recommended payload from the **actual** weights while the other modes take
/// the recommended ones; writing `FullLoad`'s from recommended weights is the classic mistake (it compiles and solves, "full load" just reports a planned figure). Total weight's
/// core is **no fuel at zero-fuel** — that is the definition, and the figure also feeds `MaxLoadWeight`'s piecewise specification.
/// </remarks>
public class Demo2PayloadTotalWeightTests {
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

    private static StowagePosition MakePosition(string name, ulong id, PositionLocation location) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(500.0, 500.0, 60.0, 60.0),
            shape: new PositionShape(1.0, 1.0, 1.0),
            location: location);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(true, false, true, true),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 3UL,
            plw: new PositionPredicateLoadWeight(350.0, 300.0, 400.0));
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

    private static (Load Load, Payload Payload, TotalWeight TotalWeight, LinearMetaModel<Flt64> Model) Register(
        StowageMode mode,
        IReadOnlyDictionary<FlightPhase, double>? computedTotalWeight = null) {
        var items = new List<Item> { MakeItem("I1", 100.0) };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation),
            MakePosition("P2", 2UL, PositionLocation.LowForwardNotBulkLocation),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var payload = new Payload(positions, load, plannedPayload: 5000.0, maxPayload: 20000.0);
        var fuselage = new Fuselage(new Liferaft(50.0, 1.0), Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);
        var fuel = new Dictionary<FlightPhase, FuelConstant> {
            [FlightPhase.TakeOff] = new FuelConstant(0.8, 8000.0, 5.0),
            [FlightPhase.Landing] = new FuelConstant(0.8, 2000.0, 2.0),
        };
        var totalWeight = new TotalWeight(fuselage, fuel, payload, computedTotalWeight: computedTotalWeight);

        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();
        payload.Register(mode, model).IsOk.Should().BeTrue();
        totalWeight.Register(model).IsOk.Should().BeTrue();
        return (load, payload, totalWeight, model);
    }

    /// <summary>
    /// **`FullLoad` 的推荐业载取自实际重量** / `FullLoad`'s recommended payload comes from the actual weights.
    /// </summary>
    /// <remarks>
    /// 成对：同一个符号名 `main_estimate_payload`，在 `FullLoad` 下必须**不含** `y`/`z`（那是推荐重量才有的两项），
    /// 在 `Predistribution` 下必须**含**。只测后者会让"两模式一样"通过。
    ///
    /// Paired: the same symbol `main_estimate_payload` must carry **no** `y`/`z` under `FullLoad` (those two terms belong to the recommended weight) and must carry them under
    /// `Predistribution`. Testing only the latter would pass if both modes behaved the same.
    /// </remarks>
    [Fact]
    public void MainEstimatePayload_ShouldUseActualWeightsUnderFullLoad() {
        (Load load, Payload fullLoad, TotalWeight _, LinearMetaModel<Flt64> _) = Register(StowageMode.FullLoad);
        LinearExpressionSymbol mainEstimate = fullLoad.MainEstimatePayload!;
        mainEstimate.Name.Should().Be("main_estimate_payload");
        mainEstimate.Polynomial.Monomials.Select(monomial => monomial.Symbol)
            .Should().NotContain(load.Y![0], "FullLoad has no planning slack in its recommended payload");

        (Load preLoad, Payload predistribution, TotalWeight _, LinearMetaModel<Flt64> _) = Register(StowageMode.Predistribution);
        LinearExpressionSymbol mainEstimatePre = predistribution.MainEstimatePayload!;
        // 用**这一次**的 `load`：两次 `Register` 各建一套变量，拿上一次的 `Y` 去比是拿两个不同模型的变量比。
        // Use **this** registration's `load`: each `Register` builds its own variables, so comparing against the previous one's `Y` compares two different models.
        mainEstimatePre.Polynomial.Monomials.Select(monomial => monomial.Symbol)
            .Should().Contain(preLoad.Y![0], "Predistribution plans with the recommended weight");
    }

    /// <summary>
    /// **主舱与下舱业载各取各的甲板** / The main-deck and lower-hold payloads take their own decks.
    /// </summary>
    /// <remarks>
    /// 成对：`main_*_payload` 只含 `P1`（主）的项，`low_*_payload` 只含 `P2`（下）的项，两者不相交。
    /// 都取全部舱位时相加会得到两倍业载——而每个数字单独看都"合理"。
    ///
    /// Paired: `main_*_payload` holds only `P1` (main) terms and `low_*_payload` only `P2` (lower) terms, with no overlap. Taking every position in both would double the payload when
    /// summed, while each number looks fine on its own.
    /// </remarks>
    [Fact]
    public void Payloads_ShouldSplitByDeck() {
        (Load _, Payload payload, TotalWeight _, LinearMetaModel<Flt64> _) = Register(StowageMode.FullLoad);

        var mainSymbols = payload.MainActualPayload!.Polynomial.Monomials
            .Select(monomial => monomial.Symbol.Name).ToHashSet();
        var lowSymbols = payload.LowActualPayload!.Polynomial.Monomials
            .Select(monomial => monomial.Symbol.Name).ToHashSet();

        mainSymbols.Should().NotBeEmpty();
        lowSymbols.Should().NotBeEmpty();
        mainSymbols.Overlaps(lowSymbols).Should().BeFalse("the two decks must not share a weight term");

        payload.ActualPayload!.Polynomial.Monomials.Should().HaveCount(
            mainSymbols.Count + lowSymbols.Count,
            "the total is the union of the two decks");
    }

    /// <summary>
    /// **零油重不加燃油** / Zero-fuel adds no fuel.
    /// </summary>
    /// <remarks>
    /// 成对：`TakeOff` 与 `Landing` 的常数里含燃油，`ZeroFuel` 不含。把燃油无条件加上会让零油重凭空重出一次起飞油量，
    /// 而它是 `MaxLoadWeight` 分段规格的输入——错一次会连带错一片。
    ///
    /// Paired: `TakeOff` and `Landing` include fuel in their constant while `ZeroFuel` does not. Adding fuel unconditionally inflates zero-fuel weight by a take-off fuel load, and
    /// that figure feeds `MaxLoadWeight`'s piecewise specification — one error cascades.
    /// </remarks>
    [Fact]
    public void TotalWeight_ShouldAddFuelOnlyForTakeOffAndLanding() {
        (Load _, Payload _, TotalWeight totalWeight, LinearMetaModel<Flt64> _) = Register(StowageMode.FullLoad);

        double dow = 40000.0;
        double liferaft = 50.0;

        double zeroFuelConstant = totalWeight.EstimateTotalWeight(FlightPhase.ZeroFuel)!.Polynomial.Constant.ToDouble();
        double takeOffConstant = totalWeight.EstimateTotalWeight(FlightPhase.TakeOff)!.Polynomial.Constant.ToDouble();
        double landingConstant = totalWeight.EstimateTotalWeight(FlightPhase.Landing)!.Polynomial.Constant.ToDouble();

        zeroFuelConstant.Should().BeApproximately(dow + liferaft, 1e-9, "zero fuel carries no fuel at all");
        takeOffConstant.Should().BeApproximately(dow + liferaft + 8000.0, 1e-9);
        landingConstant.Should().BeApproximately(dow + liferaft + 2000.0, 1e-9);
        takeOffConstant.Should().NotBeApproximately(landingConstant, 1e-9,
            "the two phases carry different fuel loads");
    }

    /// <summary>
    /// **给出已计算总重时推荐总重退化成常数** / A computed total degenerates the recommended total into a constant.
    /// </summary>
    /// <remarks>
    /// 与"没给就按公式拼"成对。有意义的是**只影响推荐那一族**：实际总重仍然按公式算，因为"实际"是求解之后才知道的数，
    /// 不能被一个外部常数顶掉。
    ///
    /// Paired with "the formula applies when absent". What matters is that it affects the **recommended** family only: the actual total still comes from the formula, because
    /// "actual" is known after solving and must not be replaced by an outside constant.
    /// </remarks>
    [Fact]
    public void TotalWeight_ShouldUseTheComputedValueForTheEstimateOnly() {
        (Load _, Payload _, TotalWeight computed, LinearMetaModel<Flt64> model) = Register(
            StowageMode.FullLoad,
            new Dictionary<FlightPhase, double> { [FlightPhase.ZeroFuel] = 45000.0 });

        LinearExpressionSymbol estimate = computed.EstimateTotalWeight(FlightPhase.ZeroFuel)!;
        estimate.Name.Should().Be("total_weight_zerofuel");
        estimate.Polynomial.Monomials.Should().BeEmpty("the computed total is a constant");
        estimate.Polynomial.Constant.ToDouble().Should().BeApproximately(45000.0, 1e-9);

        LinearExpressionSymbol actual = computed.ActualTotalWeight(FlightPhase.ZeroFuel)!;
        actual.Name.Should().Be("actual_total_weight_zerofuel");
        actual.Polynomial.Monomials.Should().NotBeEmpty(
            "the actual total still follows the payload decisions");
        _ = model;
    }

    /// <summary>
    /// **每个飞行阶段都有两族符号** / Every flight phase has both families.
    /// </summary>
    /// <remarks>
    /// 与"两族是不同的符号"成对：同一个阶段取两次名字不同、对象不同。用同一份符号填两个阶段会让"推荐/实际"在
    /// 报告里永远是同一个数，而分行显示看起来完全正常。
    ///
    /// Paired with "the two families are distinct": the same phase returns two different names and objects. Filling two phases from one symbol makes recommended and actual
    /// identical in the report while the per-row layout looks perfectly normal.
    /// </remarks>
    [Fact]
    public void TotalWeight_ShouldRegisterBothFamiliesForEveryPhase() {
        (Load _, Payload _, TotalWeight totalWeight, LinearMetaModel<Flt64> _) = Register(StowageMode.FullLoad);

        FlightPhase[] phases = new[] { FlightPhase.TakeOff, FlightPhase.Landing, FlightPhase.ZeroFuel };
        var estimates = new List<LinearExpressionSymbol>();
        foreach (FlightPhase phase in phases) {
            LinearExpressionSymbol? estimate = totalWeight.EstimateTotalWeight(phase);
            LinearExpressionSymbol? actual = totalWeight.ActualTotalWeight(phase);
            estimate.Should().NotBeNull();
            actual.Should().NotBeNull();
            actual!.Name.Should().StartWith("actual_total_weight_");
            estimate!.Name.Should().NotBe(actual.Name);
            estimates.Add(estimate);
        }
        estimates.Select(symbol => symbol.Name).Should().OnlyHaveUniqueItems(
            "each phase gets its own recommended symbol");
    }

    /// <summary>
    /// **没有业载符号就明确失败** / Without payload symbols, fail loudly.
    /// </summary>
    /// <remarks>
    /// 返回 `Ok` 会让调用方以为总重建好了，而实际上一族符号都没有——模型照样求解，只是这个域被静默关掉。
    ///
    /// Returning `Ok` would let the caller believe the totals exist when there is not one symbol: the model still solves, the domain silently switched off.
    /// </remarks>
    [Fact]
    public void TotalWeight_ShouldFailWithoutRegisteredPayload() {
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation),
        };
        var items = new List<Item> { MakeItem("I1", 100.0) };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var payload = new Payload(positions, load, 5000.0, 20000.0);
        var fuselage = new Fuselage(null, 40000.0, 10.0, 900.0);
        var totalWeight = new TotalWeight(fuselage, new Dictionary<FlightPhase, FuelConstant>(), payload);
        using var model = new LinearMetaModel<Flt64>();

        totalWeight.Register(model).IsOk.Should().BeFalse("no payload symbols means no total weight to build");
    }
}
