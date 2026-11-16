// 区域与累积载荷上限的判据 / Criteria for the zone and cumulative load bounds.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
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
/// **区域与累积载荷上限** / The zone and cumulative load bounds.
/// </summary>
/// <remarks>
/// 两条限制都按"重叠比例"把舱位重量分摊到区间里。四处容易做错：
///
/// | 处 | 做错了会怎样 |
/// | --- | --- |
/// | 系数不是 1 | 舱位跨两个区时每区都算整个舱位，区越多约束越假 |
/// | 区域载荷的救生筏是常数项 | 当成单项式会跟着决策变 |
/// | 空区跳过、缺符号失败 | 两者互换会让合法配置报错、集成错误静默 |
/// | 力臂用不变文化 | 同一份数据在两台机器上产生不同约束名 |
///
/// Both bounds apportion position weight by **overlap ratio**. Four easy mistakes tabulated above.
/// </remarks>
public class Demo2ZoneCumulativeLimitTests {
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

    private static (IReadOnlyList<StowagePosition> Positions, LinearMetaModel<Flt64> Model, Load Load) Prepare(
        bool registerLoad = true) {
        var items = new List<Item> { MakeItem("I1") };
        // P1 覆盖 500—900（长 400），P2 覆盖 800—1200（长 400）。两者在 800—900 重叠 100。
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation, frontArm: 500.0, backArm: 900.0),
            MakePosition("P2", 2UL, PositionLocation.LowForwardNotBulkLocation, frontArm: 800.0, backArm: 1200.0),
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
        return (positions, model, load);
    }

    private static LinearInequalityConstraint<Flt64> Row(LinearMetaModel<Flt64> model, string name) =>
        model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == name);

    /// <summary>
    /// 把一行的左端按符号求和 / Sum a left side by symbol.
    /// </summary>
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
    /// **区域：系数是重叠比例，不是 1** / Zone: the coefficient is the overlap ratio, not 1.
    /// </summary>
    /// <remarks>
    /// 区域 600—1000 覆盖 P1（500—900）的 300/400 = 0.75 与 P2（800—1200）的 200/400 = 0.5。系数写成 1 会让两个舱位都算满。
    ///
    /// A zone of 600—1000 covers 300/400 = 0.75 of P1 (500—900) and 200/400 = 0.5 of P2 (800—1200). Writing 1 charges both in full.
    /// </remarks>
    [Fact]
    public void ZoneLoadWeight_ShouldScaleByTheOverlapRatio() {
        (IReadOnlyList<StowagePosition> prepared, LinearMetaModel<Flt64> model, Load load) = Prepare();
        var limits = new MaxZoneLoadWeight {
            LimitZones = new[] {
                new ZoneLoadLimitZone("main", 5000.0, new[] {
                    new ZoneLoadLimitPart(0, 0.75),
                    new ZoneLoadLimitPart(1, 0.5),
                }),
            },
        };
        var limit = new ZoneLoadWeightLimit(limits, prepared, load);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().Equal("zone_load_weight_limit_main");
        LinearInequalityConstraint<Flt64> row = Row(model, "zone_load_weight_limit_main");
        row.Inequality.Comparison.Should().Be(Comparison.LE);
        row.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(5000.0, 1e-9);

        Dictionary<object, double> bySymbol = SumBySymbol(row);
        var first = load.EstimateLoadWeightSymbol(0)!.Polynomial.Monomials
            .GroupBy(monomial => monomial.Symbol)
            .ToDictionary(group => (object)group.Key, group => group.Sum(monomial => monomial.Coefficient.ToDouble()));
        var second = load.EstimateLoadWeightSymbol(1)!.Polynomial.Monomials
            .GroupBy(monomial => monomial.Symbol)
            .ToDictionary(group => (object)group.Key, group => group.Sum(monomial => monomial.Coefficient.ToDouble()));

        foreach (KeyValuePair<object, double> pair in first) {
            bySymbol[pair.Key].Should().BeApproximately(pair.Value * 0.75, 1e-9,
                "P1 enters the zone at three quarters of its weight");
        }
        foreach (KeyValuePair<object, double> pair in second) {
            bySymbol[pair.Key].Should().BeApproximately(pair.Value * 0.5, 1e-9,
                "P2 enters the zone at half of its weight");
        }
    }

    /// <summary>
    /// **区域：救生筏重量进常数项** / Zone: the liferaft weight enters the constant term.
    /// </summary>
    /// <remarks>
    /// 有救生筏与没有救生筏的两行，左端常数差恰是救生筏重量，**单项式集合相同**。当成单项式会跟着决策变，而它不随决策变化。
    ///
    /// Two rows, with and without a liferaft, differ in the left constant by exactly the liferaft weight and have the **same** monomial set. As a monomial it would vary with
    /// decisions, and it does not.
    /// </remarks>
    [Fact]
    public void ZoneLoadWeight_ShouldAddTheLiferaftAsAConstant() {
        (IReadOnlyList<StowagePosition> prepared, LinearMetaModel<Flt64> withRaftModel, Load load) = Prepare();
        var withRaft = new MaxZoneLoadWeight {
            LimitZones = new[] {
                new ZoneLoadLimitZone("main", 5000.0, new[] { new ZoneLoadLimitPart(0, 1.0) }, LiferaftWeight: 50.0),
            },
        };
        new ZoneLoadWeightLimit(withRaft, prepared, load).Invoke(withRaftModel).IsOk.Should().BeTrue();

        (IReadOnlyList<StowagePosition> preparedNoRaft, LinearMetaModel<Flt64> withoutRaftModel, Load _) = Prepare();
        var withoutRaft = new MaxZoneLoadWeight {
            LimitZones = new[] {
                new ZoneLoadLimitZone("main", 5000.0, new[] { new ZoneLoadLimitPart(0, 1.0) }),
            },
        };
        new ZoneLoadWeightLimit(withoutRaft, preparedNoRaft, load).Invoke(withoutRaftModel).IsOk.Should().BeTrue();

        LinearInequalityConstraint<Flt64> a = Row(withRaftModel, "zone_load_weight_limit_main");
        LinearInequalityConstraint<Flt64> b = Row(withoutRaftModel, "zone_load_weight_limit_main");
        (a.Inequality.Lhs.Constant.ToDouble() - b.Inequality.Lhs.Constant.ToDouble())
            .Should().BeApproximately(50.0, 1e-9, "the liferaft weight is a constant");
        a.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
            .Should().BeEquivalentTo(b.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol),
                "the liferaft adds no variable");
    }

    /// <summary>
    /// **区域：空区跳过，缺符号失败** / Zone: an empty zone is skipped, a missing symbol fails.
    /// </summary>
    /// <remarks>
    /// 成对的两条：整区舱位都不可用时**跳过**（合法配置）；有可用舱位但 `Load.Register` 没跑时**失败**（集成错误）。
    /// 两者互换会让合法配置报错、集成错误静默。
    ///
    /// The pair: a zone whose positions are all unavailable is **skipped** (legitimate); a zone with an available position but no `Load.Register` **fails** (integration error).
    /// Swapping them makes legitimate configurations error out and integration errors silent.
    /// </remarks>
    [Fact]
    public void ZoneLoadWeight_ShouldSkipAnEmptyZoneAndFailOnAMissingSymbol() {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation, available: false),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        var limits = new MaxZoneLoadWeight {
            LimitZones = new[] {
                new ZoneLoadLimitZone("empty", 5000.0, new[] { new ZoneLoadLimitPart(0, 1.0) }),
            },
        };
        var skipped = new ZoneLoadWeightLimit(limits, positions, load);
        skipped.Invoke(model).IsOk.Should().BeTrue("a zone with no available position is a legitimate configuration");
        skipped.LastAddedNames.Should().BeEmpty();

        var available = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation),
        };
        var availableLoad = new Load(items, available, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var failing = new ZoneLoadWeightLimit(limits, available, availableLoad);
        failing.Invoke(model).IsOk.Should().BeFalse("Load.Register never ran");
        failing.LastAddedNames.Should().BeEmpty();
    }

    /// <summary>
    /// **累积：检查点按力臂建，重叠比例进系数** / Cumulative: checkpoints follow the arms and the overlap ratio is the coefficient.
    /// </summary>
    /// <remarks>
    /// 起点 500、检查点 900 与 1200（`FWD`，升序）：
    ///
    /// | 检查点 | 区间 | P1 (500—900) | P2 (800—1200) |
    /// | --- | --- | --- | --- |
    /// | 900 | 500—900 | 400/400 = 1.0 | 100/400 = 0.25 |
    /// | 1200 | 500—1200 | 1.0 | 1.0 |
    ///
    /// 名字带力臂：`…_cumulative_fwd_500_900` 与 `…_cumulative_fwd_500_1200`。
    ///
    /// Start 500, checkpoints at 900 and 1200 (`FWD`, ascending), as tabulated. The names carry the arm.
    /// </remarks>
    [Fact]
    public void CumulativeLoadWeight_ShouldBuildOneRowPerCheckpoint() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load) = Prepare();
        var zone = new CumulativeLoadLimitZone(
            CumulativeLoadDirection.FWD,
            fromArm: 500.0,
            points: new[] {
                new CumulativeLoadPoint(900.0, 3000.0),
                new CumulativeLoadPoint(1200.0, 6000.0),
            },
            positions: positions.Select(position => position.AircraftPosition).ToList());
        var limits = new MaxCumulativeLoadWeight { LimitZones = new[] { zone } };
        var limit = new CumulativeLoadWeightLimit(limits, positions, load);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().Equal(
            "cumulative_load_weight_limit_cumulative_fwd_500_900",
            "cumulative_load_weight_limit_cumulative_fwd_500_1200");
        zone.CheckPoints[0].Parts.Should().HaveCount(2, "both positions overlap the 500—900 interval");
        zone.CheckPoints[0].Parts.Single(part => part.PositionIndex == 0).Weight.Should().BeApproximately(1.0, 1e-9);
        zone.CheckPoints[0].Parts.Single(part => part.PositionIndex == 1).Weight.Should().BeApproximately(0.25, 1e-9);
        zone.CheckPoints[1].Parts.Single(part => part.PositionIndex == 1).Weight.Should().BeApproximately(1.0, 1e-9);

        Row(model, "cumulative_load_weight_limit_cumulative_fwd_500_900")
            .Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(3000.0, 1e-9);
        Row(model, "cumulative_load_weight_limit_cumulative_fwd_500_1200")
            .Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(6000.0, 1e-9);

        // 断言**约束行**的系数，不只是模型类里的 `Parts`：系数是限制这一侧乘上去的，只查 `Parts` 等于没测限制。
        // 900 那一行：P1 满额 1.0、P2 四分之一 0.25；1200 那一行两个都是 1.0。
        // Assert the **constraint row** coefficients, not just the model class's `Parts`: the limit is what multiplies by them, so checking `Parts` alone tests nothing.
        // The 900 row has P1 at 1.0 and P2 at a quarter; the 1200 row has both at 1.0.
        Dictionary<object, double> narrow = SumBySymbol(Row(model, "cumulative_load_weight_limit_cumulative_fwd_500_900"));
        Dictionary<object, double> wide = SumBySymbol(Row(model, "cumulative_load_weight_limit_cumulative_fwd_500_1200"));
        foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in load.EstimateLoadWeightSymbol(1)!.Polynomial.Monomials) {
            narrow[monomial.Symbol].Should().BeApproximately(monomial.Coefficient.ToDouble() * 0.25, 1e-9,
                "P2 is only a quarter inside the 500—900 interval");
            wide[monomial.Symbol].Should().BeApproximately(monomial.Coefficient.ToDouble(), 1e-9,
                "P2 is fully inside the 500—1200 interval");
        }
    }

    /// <summary>
    /// **累积：`toArm` 小于起点时区间仍归一化** / Cumulative: the interval normalises when `toArm` is below the start.
    /// </summary>
    /// <remarks>
    /// 起点 800、检查点 500（`toArm < fromArm`，`AFT` 方向常见）。归一化后区间是 500—800，P1（500—900）重叠 300/400 = 0.75。
    /// 不归一化时 `intervalStart = 800`、`intervalEnd = 500`，`min(back, 500) − max(front, 800)` 对所有舱位都是负的，
    /// **检查点一个舱位都没有**——而它看起来只是一条"没有货"的约束。
    ///
    /// Start 800, checkpoint 500 (`toArm < fromArm`, common in `AFT`). Normalised the interval is 500—800 and P1 (500—900) overlaps 300/400 = 0.75. Without normalisation
    /// `intervalStart` is 800 and `intervalEnd` is 500, so `min(back, 500) − max(front, 800)` is negative for every position and the checkpoint has **no** parts — and it merely
    /// looks like an empty constraint.
    /// </remarks>
    [Fact]
    public void CumulativeLoadWeight_ShouldNormaliseAReversedInterval() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load) = Prepare();
        var zone = new CumulativeLoadLimitZone(
            CumulativeLoadDirection.AFT,
            fromArm: 800.0,
            points: new[] { new CumulativeLoadPoint(500.0, 2000.0) },
            positions: positions.Select(position => position.AircraftPosition).ToList());

        zone.CheckPoints.Should().ContainSingle();
        zone.CheckPoints[0].Parts.Should().ContainSingle("P1 spans 500—900 and overlaps the 500—800 interval");
        zone.CheckPoints[0].Parts[0].PositionIndex.Should().Be(0);
        zone.CheckPoints[0].Parts[0].Weight.Should().BeApproximately(0.75, 1e-9);

        var limit = new CumulativeLoadWeightLimit(new MaxCumulativeLoadWeight { LimitZones = new[] { zone } }, positions, load);
        limit.Invoke(model).IsOk.Should().BeTrue();
        limit.LastAddedNames.Should().ContainSingle();
    }

    /// <summary>
    /// **累积：整检查点没有可用舱位就跳过** / Cumulative: a checkpoint with no available position is skipped.
    /// </summary>
    /// <remarks>
    /// 与区域载荷上限同形。缺这条判据时，"不跳过"的变异存活——因为其余 fixture 的检查点都至少有一个可用舱位。
    ///
    /// Same shape as the zone bound. Without this criterion the "does not skip" mutation survives, because every other fixture's checkpoint has at least one available position.
    /// </remarks>
    [Fact]
    public void CumulativeLoadWeight_ShouldSkipAnUnavailableCheckpoint() {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation, available: false),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        var zone = new CumulativeLoadLimitZone(
            CumulativeLoadDirection.FWD,
            fromArm: 500.0,
            points: new[] { new CumulativeLoadPoint(900.0, 3000.0) },
            positions: positions.Select(position => position.AircraftPosition).ToList());
        zone.CheckPoints[0].Parts.Should().ContainSingle("the position still overlaps the interval");

        var limit = new CumulativeLoadWeightLimit(new MaxCumulativeLoadWeight { LimitZones = new[] { zone } }, positions, load);
        limit.Invoke(model).IsOk.Should().BeTrue("a checkpoint whose positions are all unavailable is legitimate");
        limit.LastAddedNames.Should().BeEmpty();
    }

    /// <summary>
    /// **累积：`AFT` 方向把检查点倒序** / Cumulative: `AFT` reverses the checkpoints.
    /// </summary>
    /// <remarks>
    /// 同一组点，`FWD` 给 900、1200，`AFT` 给 1200、900。不倒序会让后向累积的检查点顺序与前进方向一致，区间语义反了。
    /// 成对：两者的**点集相同**，只是顺序不同。
    ///
    /// The same points: `FWD` yields 900, 1200 and `AFT` yields 1200, 900. Without the reversal an aft accumulation runs in the forward direction and the interval semantics invert.
    /// Paired: the two have the **same point set**, only the order differs.
    /// </remarks>
    [Fact]
    public void CumulativeLoadWeight_ShouldReverseTheCheckpointsForAft() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> _, Load _) = Prepare();
        CumulativeLoadPoint[] points = new[] {
            new CumulativeLoadPoint(900.0, 3000.0),
            new CumulativeLoadPoint(1200.0, 6000.0),
        };
        var aircraftPositions = positions.Select(position => position.AircraftPosition).ToList();
        var forward = new CumulativeLoadLimitZone(CumulativeLoadDirection.FWD, 500.0, points, aircraftPositions);
        var aft = new CumulativeLoadLimitZone(CumulativeLoadDirection.AFT, 500.0, points, aircraftPositions);

        forward.CheckPoints.Select(point => point.ToArm).Should().Equal(900.0, 1200.0);
        aft.CheckPoints.Select(point => point.ToArm).Should().Equal(1200.0, 900.0);
        aft.CheckPoints.Select(point => point.ToArm).Should().BeEquivalentTo(forward.CheckPoints.Select(point => point.ToArm),
            "the same points, only reordered");
        forward.Name.Should().Be("cumulative_fwd_500");
        aft.Name.Should().Be("cumulative_aft_500");
    }

    /// <summary>
    /// **累积：力臂相同的点保留第一次出现的那个** / Cumulative: duplicate arms keep the first occurrence.
    /// </summary>
    /// <remarks>
    /// 两个 900 点（3000 与 9999）只留一个，且是**先出现**的 3000。`distinctBy { toArm }`，先排序后去重会留下另一个。
    /// 成对：检查点因此只有一个，右端常数是 3000。
    ///
 /// Two points at 900 (3000 and 9999) collapse to one, the **first** — 3000. Its `distinctBy { toArm }` keeps the first; sorting first would keep the other.
    /// Paired: exactly one checkpoint remains, with right side 3000.
    /// </remarks>
    [Fact]
    public void CumulativeLoadWeight_ShouldKeepTheFirstOfDuplicateArms() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load) = Prepare();
        var zone = new CumulativeLoadLimitZone(
            CumulativeLoadDirection.FWD,
            fromArm: 500.0,
            points: new[] {
                new CumulativeLoadPoint(900.0, 3000.0),
                new CumulativeLoadPoint(900.0, 9999.0),
            },
            positions: positions.Select(position => position.AircraftPosition).ToList());
        zone.CheckPoints.Should().HaveCount(1);
        zone.CheckPoints[0].MaxSum.Should().BeApproximately(3000.0, 1e-9, "the first occurrence wins");
        zone.CheckPoints[0].MaxSum.Should().NotBeApproximately(9999.0, 1e-9);

        var limit = new CumulativeLoadWeightLimit(new MaxCumulativeLoadWeight { LimitZones = new[] { zone } }, positions, load);
        limit.Invoke(model).IsOk.Should().BeTrue();
        limit.LastAddedNames.Should().HaveCount(1);
    }

    /// <summary>
    /// **累积：跨度为零的舱位不参与** / Cumulative: a zero-span position does not contribute.
    /// </summary>
    /// <remarks>
    /// 舱位的 `BackArm == FrontArm` 时长度为 0，这里直接排除（否则重叠比例是除零）。判据钉住它不出现在任何检查点的 `Parts` 里。
    ///
 /// When `BackArm == FrontArm` the length is zero and the implementation drops the position (otherwise the ratio divides by zero). The criterion pins that it appears in no
    /// checkpoint's `Parts`.
    /// </remarks>
    [Fact]
    public void CumulativeLoadWeight_ShouldDropZeroSpanPositions() {
        var positions = new List<Position> {
            new(
                id: 1UL,
                spaceName: "ZERO",
                sizeCode: "A",
                linearLoadingOrder: 0,
                coordinate: new PositionCoordinate(700.0, 700.0, 40.0, 60.0),
                shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
                location: PositionLocation.NormalMainLocation),
            new(
                id: 2UL,
                spaceName: "REAL",
                sizeCode: "A",
                linearLoadingOrder: 1,
                coordinate: new PositionCoordinate(600.0, 800.0, 40.0, 60.0),
                shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
                location: PositionLocation.NormalMainLocation),
        };
        var zone = new CumulativeLoadLimitZone(
            CumulativeLoadDirection.FWD,
            fromArm: 500.0,
            points: new[] { new CumulativeLoadPoint(900.0, 3000.0) },
            positions: positions);

        zone.CheckPoints[0].Parts.Should().HaveCount(1);
        zone.CheckPoints[0].Parts[0].PositionIndex.Should().Be(1, "only the position with real length contributes");
    }

    /// <summary>
    /// **累积：力臂用不变文化，名字不随机器变** / Cumulative: the arm uses the invariant culture.
    /// </summary>
    /// <remarks>
    /// 力臂 900.5 在德语/法语环境下若用当前文化会写成 `900,5`，同一份数据在两台机器上产生不同的约束名，报告无法对照。
    /// 判据把当前文化临时切到 `de-DE`，名字里必须仍是 `900.5`。
    ///
    /// Under de-DE or fr-FR the current culture would render 900.5 as `900,5`, so the same data yields different constraint names on two machines and reports cannot be compared.
    /// The criterion temporarily switches the culture to `de-DE` and requires the name to still carry `900.5`.
    /// </remarks>
    [Fact]
    public void CumulativeLoadWeight_ShouldUseTheInvariantCultureForTheArm() {
        CultureInfo original = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load) = Prepare();
            var zone = new CumulativeLoadLimitZone(
                CumulativeLoadDirection.FWD,
                fromArm: 500.0,
                points: new[] { new CumulativeLoadPoint(900.5, 3000.0) },
                positions: positions.Select(position => position.AircraftPosition).ToList());
            var limit = new CumulativeLoadWeightLimit(new MaxCumulativeLoadWeight { LimitZones = new[] { zone } }, positions, load);
            limit.Invoke(model).IsOk.Should().BeTrue();

            limit.LastAddedNames.Should().ContainSingle();
            limit.LastAddedNames[0].Should().EndWith("_900.5");
            limit.LastAddedNames[0].Should().NotEndWith("_900,5", "the arm must not follow the current culture");
        } finally {
            CultureInfo.CurrentCulture = original;
        }
    }
}
