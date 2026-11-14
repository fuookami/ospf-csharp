// 相邻载荷间隙与压舱物的判据 / Criteria for the adjacent gap bound and the ballast model.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **相邻载荷间隙与压舱物（B597）** / The adjacent gap bound and the ballast model (B597).
/// </summary>
/// <remarks>
/// 两件事各自的风险都在"少做一半"上：
///
/// | 对象 | 少做一半会怎样 |
/// | --- | --- |
/// | `AdjacentGapLimit` | 只加一行时只限制单向，重心可以单侧堆到超出间隙 |
/// | `Ballast` | 压舱重量用全部舱位而非可用舱位；`min_ballast_weight` 在硬安保下也注册 |
///
/// Both risks are about doing only half the job, tabulated above.
/// </remarks>
public class Demo2AdjacentBallastTests {
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
        PositionLocation location,
        double frontArm = 500.0,
        double backArm = 900.0,
        bool available = true) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(frontArm, backArm, 40.0, 60.0),
            shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
            location: location);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(available, false, true, true),
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

    private static (IReadOnlyList<StowagePosition> Positions, LinearMetaModel<Flt64> Model, Load Load, AircraftModel Aircraft) Prepare(
        bool registerLoad = true,
        bool secondAvailable = true) {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation),
            MakePosition("P2", 2UL, PositionLocation.LowForwardNotBulkLocation, available: secondAvailable),
            MakePosition("P3", 3UL, PositionLocation.NormalMainLocation),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        if (registerLoad) {
            load.Register(model).IsOk.Should().BeTrue();
        }
        return (positions, model, load, aircraftModel);
    }

    private static LinearInequalityConstraint<Flt64> Row(LinearMetaModel<Flt64> model, string name) =>
        model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == name);

    private static Dictionary<object, double> SumBySymbol(LinearInequalityConstraint<Flt64> row) {
        var bySymbol = new Dictionary<object, double>();
        foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in row.Inequality.Lhs.Monomials) {
            double value = monomial.Coefficient.ToDouble();
            bySymbol[monomial.Symbol] = bySymbol.TryGetValue(monomial.Symbol, out double existing)
                ? existing + value
                : value;
        }
        return bySymbol;
    }

    /// <summary>
    /// **相邻间隙：每对舱位两行，方向相反** / Adjacent gap: two rows per pair, in opposite directions.
    /// </summary>
    /// <remarks>
    /// 三个舱位 → 两对 → **四行**。`pos_0` 的 `load[0]` 系数是 +1、`load[1]` 是 −1；`neg_0` 反过来。只加一行时只限制单向。
    ///
    /// Three positions → two pairs → **four rows**. In `pos_0` the `load[0]` coefficient is +1 and `load[1]` is −1; `neg_0` reverses them. One row alone bounds only one
    /// direction.
    /// </remarks>
    [Fact]
    public void AdjacentGap_ShouldAddTwoOppositeRowsPerPair() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel _) = Prepare();
        var limit = new AdjacentGapLimit(positions, load, maxAdjacentLoadGap: 250.0);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().Equal(
            "adjacent_gap_limit_pos_0",
            "adjacent_gap_limit_neg_0",
            "adjacent_gap_limit_pos_1",
            "adjacent_gap_limit_neg_1");

        Dictionary<object, double> positive = SumBySymbol(Row(model, "adjacent_gap_limit_pos_0"));
        Dictionary<object, double> negative = SumBySymbol(Row(model, "adjacent_gap_limit_neg_0"));
        var firstSymbols = load.EstimateLoadWeightSymbol(0)!.Polynomial.Monomials.Select(monomial => (object)monomial.Symbol).ToHashSet();
        var secondSymbols = load.EstimateLoadWeightSymbol(1)!.Polynomial.Monomials.Select(monomial => (object)monomial.Symbol).ToHashSet();
        firstSymbols.Should().NotBeEmpty("the fixture registers estimated load weights");
        secondSymbols.Should().NotBeEmpty();

        foreach (object symbol in firstSymbols) {
            positive[symbol].Should().BeApproximately(
                load.EstimateLoadWeightSymbol(0)!.Polynomial.Monomials
                    .Where(monomial => (object)monomial.Symbol == symbol)
                    .Sum(monomial => monomial.Coefficient.ToDouble()), 1e-9);
            negative[symbol].Should().BeApproximately(
                -load.EstimateLoadWeightSymbol(0)!.Polynomial.Monomials
                    .Where(monomial => (object)monomial.Symbol == symbol)
                    .Sum(monomial => monomial.Coefficient.ToDouble()), 1e-9,
                    "the negative row flips the first position's sign");
        }
        foreach (object symbol in secondSymbols.Where(symbol => !firstSymbols.Contains(symbol))) {
            positive[symbol].Should().BeApproximately(
                -load.EstimateLoadWeightSymbol(1)!.Polynomial.Monomials
                    .Where(monomial => (object)monomial.Symbol == symbol)
                    .Sum(monomial => monomial.Coefficient.ToDouble()), 1e-9);
        }
        Row(model, "adjacent_gap_limit_pos_0").Inequality.Comparison.Should().Be(Comparison.LE);
        Row(model, "adjacent_gap_limit_pos_0").Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(250.0, 1e-9);
        Row(model, "adjacent_gap_limit_neg_0").Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(250.0, 1e-9);
    }

    /// <summary>
    /// **相邻间隙：`Count - 1` 对，不是 `Count` 对** / Adjacent gap: `Count - 1` pairs, not `Count`.
    /// </summary>
    /// <remarks>
    /// 两个舱位 → **一对 → 两行**。取到 `Count` 会越界，而"三个舱位给四行"那条判据看不出这一点——它只证明行数随舱位数增长。
    ///
    /// Two positions → **one pair → two rows**. Indexing to `Count` overruns, and the three-position criterion above cannot show it: it only proves the row count grows with the
    /// position count.
    /// </remarks>
    [Fact]
    public void AdjacentGap_ShouldStopOneShortOfThePositionCount() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel _) = Prepare();
        StowagePosition[] two = new[] { positions[0], positions[1] };
        var limit = new AdjacentGapLimit(two, load, maxAdjacentLoadGap: 250.0);
        limit.Invoke(model).IsOk.Should().BeTrue();
        limit.LastAddedNames.Should().HaveCount(2);
        limit.LastAddedNames.Should().Equal("adjacent_gap_limit_pos_0", "adjacent_gap_limit_neg_0");
    }

    /// <summary>
    /// **相邻间隙：缺装载符号时明确失败** / Adjacent gap: a missing load symbol fails loudly.
    /// </summary>
    /// <remarks>
    /// 空壳对任何输入都返回 `Ok`。缺符号时报成功会让调用方以为间隙受约束，而模型里一行都没有。
    ///
    /// The shell returned `Ok` for any input. Reporting success while the symbol is missing tells the caller the gap is bounded when no row exists.
    /// </remarks>
    [Fact]
    public void AdjacentGap_ShouldFailWithoutTheLoadSymbols() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel _) = Prepare(registerLoad: false);
        var limit = new AdjacentGapLimit(positions, load, maxAdjacentLoadGap: 250.0);

        limit.Invoke(model).IsOk.Should().BeFalse();
        limit.LastAddedNames.Should().BeEmpty();
    }

    /// <summary>
    /// **压舱：压舱重量只用可用舱位** / Ballast: the ballast weight sums available positions only.
    /// </summary>
    /// <remarks>
    /// 三个舱位、中间那个不可用 → 压舱舱位是两个，`ballast_weight` 的单项式只含这两个舱位的符号。
    /// 用全部舱位会让不可用的舱位也算进压舱重量。
    ///
    /// Three positions with the middle one unavailable → two ballast positions, and `ballast_weight` carries only their symbols. Using all positions would count the unavailable
    /// one too.
    /// </remarks>
    [Fact]
    public void Ballast_ShouldSumOnlyAvailablePositions() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel aircraft) = Prepare(secondAvailable: false);
        var ballast = new Ballast(aircraft, positions, minBallastWeight: null, load);
        ballast.BallastPositions.Should().HaveCount(2);
        ballast.BallastPositions.Select(position => position.Name).Should().Equal("P1", "P3");

        ballast.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        ballast.BallastWeightSymbol.Should().NotBeNull();
        ballast.BallastWeightSymbol!.Name.Should().Be("ballast_weight");

        var expected = new HashSet<object>();
        foreach (int j in new[] { 0, 2 }) {
            foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in load.EstimateLoadWeightSymbol(j)!.Polynomial.Monomials) {
                expected.Add(monomial.Symbol);
            }
        }
        var unavailable = new HashSet<object>(
            load.EstimateLoadWeightSymbol(1)!.Polynomial.Monomials.Select(monomial => (object)monomial.Symbol));
        unavailable.ExceptWith(expected);

        ballast.BallastWeightSymbol!.Polynomial.Monomials.Select(monomial => (object)monomial.Symbol)
            .Should().BeEquivalentTo(expected);
        foreach (object symbol in unavailable) {
            ballast.BallastWeightSymbol!.Polynomial.Monomials.Select(monomial => (object)monomial.Symbol)
                .Should().NotContain(symbol, "the unavailable position must not enter the ballast weight");
        }
    }

    /// <summary>
    /// **压舱：`min_ballast_weight` 只在软安保模式下注册** / Ballast: `min_ballast_weight` registers only under soft security.
    /// </summary>
    /// <remarks>
    /// 成对：软安保下有两个符号（`ballast_weight` 与 `min_ballast_weight`）；另一模式只有 `ballast_weight`。
    /// 非软安保下注册它会让约束生成器以为有一条最低压舱要求可加。
    ///
    /// **`WithSoftSecurity()` 的定义是 `mode != WeightRecommendation`** —— 软安保是预配载与全配载，**不是**"建议打板"。
    /// 这个名字很容易反着猜（"建议"听起来最软），我第一版就猜错了，判据当场红了。判据按定义写，不按名字猜。
    ///
    /// Paired: soft security yields two symbols, the other mode only `ballast_weight`. Registering it under non-soft security tells the constraint generator there is a minimum
    /// ballast requirement to add. **`WithSoftSecurity()` is defined as `mode != WeightRecommendation`** — soft security is predistribution and full load, **not** "weight
    /// recommendation". The name invites the opposite guess ("recommendation" sounds softest); the first draft made exactly that guess and went red. Criteria follow the definition,
    /// not the name.
    /// </remarks>
    [Fact]
    public void Ballast_ShouldRegisterTheMinimumOnlyUnderSoftSecurity() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> softModel, Load load, AircraftModel aircraft) = Prepare();
        var soft = new Ballast(aircraft, positions, minBallastWeight: 120.0, load);
        soft.Register(StowageMode.FullLoad, softModel).IsOk.Should().BeTrue();
        soft.BallastWeightSymbol.Should().NotBeNull();
        soft.AdaptiveMinBallastWeightSymbol.Should().NotBeNull("full load runs soft security");
        soft.AdaptiveMinBallastWeightSymbol!.Name.Should().Be("min_ballast_weight");
        soft.AdaptiveMinBallastWeightSymbol!.Polynomial.Constant.ToDouble().Should().BeApproximately(120.0, 1e-9);

        (IReadOnlyList<StowagePosition> hardPositions, LinearMetaModel<Flt64> hardModel, Load hardLoad, AircraftModel hardAircraft) = Prepare();
        var hard = new Ballast(hardAircraft, hardPositions, minBallastWeight: 120.0, hardLoad);
        hard.Register(StowageMode.WeightRecommendation, hardModel).IsOk.Should().BeTrue();
        hard.BallastWeightSymbol.Should().NotBeNull();
        hard.AdaptiveMinBallastWeightSymbol.Should().BeNull("weight recommendation is not soft security");
    }

    /// <summary>
    /// **压舱：没有最低值时显式注册零** / Ballast: an absent minimum registers an explicit zero.
    /// </summary>
    /// <remarks>
    /// 软安保 + 没有最低值 → 符号仍在，常数是 **0**。留空会让下游无法区分"没有要求"与"要求为零"，而两者的约束行为不同。
    ///
    /// Soft security with no minimum → the symbol still exists with constant **0**. Leaving it absent makes downstream unable to tell "no requirement" from "a requirement of
    /// zero", and the two behave differently.
    /// </remarks>
    [Fact]
    public void Ballast_ShouldRegisterAnExplicitZeroWhenNoMinimumIsGiven() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel aircraft) = Prepare();
        var ballast = new Ballast(aircraft, positions, minBallastWeight: null, load);
        ballast.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        ballast.AdaptiveMinBallastWeightSymbol.Should().NotBeNull("the symbol exists even with no minimum");
        ballast.AdaptiveMinBallastWeightSymbol!.Polynomial.Monomials.Should().BeEmpty();
        ballast.AdaptiveMinBallastWeightSymbol!.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>
    /// **压舱：缺装载符号时明确失败** / Ballast: a missing load symbol fails loudly.
    /// </summary>
    /// <remarks>
    /// 与相邻间隙的同名判据成对：两个类的失败分支各测一次。
    ///
    /// Paired with the adjacent gap's criterion of the same name so each class's failure branch has its own check.
    /// </remarks>
    [Fact]
    public void Ballast_ShouldFailWithoutTheLoadSymbols() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel aircraft) = Prepare(registerLoad: false);
        var ballast = new Ballast(aircraft, positions, minBallastWeight: 120.0, load);

        ballast.Register(StowageMode.FullLoad, model).IsOk.Should().BeFalse();
        ballast.BallastWeightSymbol.Should().BeNull();
    }
}
