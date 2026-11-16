// 三个收尾限制（压舱重量 / 水平安定面 / 不对称线性密度）的判据 / Criteria for the three closing bounds.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;
using Limits = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
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
/// **三个收尾限制** / The three closing bounds.
/// </summary>
/// <remarks>
/// | 限制 | 容易做错的地方 |
/// | --- | --- |
/// | `BallastWeightLimit` | 名字写死为 `ballast_weight_limit`（不带 `name` 前缀）；两个前提都满足才加行 |
/// | `HorizontalStabilizerLimit` | 取值用 `??`、命名用"是否非空"——两个不同的条件；常规下界与警告下界可同时存在 |
/// | `UnsymmetricalLinearDensityLimit` | 每个曲线点**两行**；系数为 `null` 时该项**不出现**而非乘 0 |
///
/// Three closing bounds with the easy mistakes tabulated above.
/// </remarks>
public class Demo2ClosingLimitsTests {
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
        double leftArm = -40.0,
        double rightArm = -40.0,
        bool available = true) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(frontArm, backArm, leftArm, rightArm),
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
        IReadOnlyList<StowagePosition>? positions = null) {
        var items = new List<Item> { MakeItem("I1") };
        positions ??= new List<StowagePosition> {
            MakePosition("L1", 1UL, PositionLocation.NormalMainLocation, leftArm: -40.0, rightArm: -40.0),
            MakePosition("R1", 2UL, PositionLocation.NormalMainLocation, leftArm: 40.0, rightArm: 40.0),
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

    /// <summary>
    /// **压舱重量：名字写死，方向是 `GE`** / Ballast weight: the name is hard-coded and the direction is `GE`.
    /// </summary>
    /// <remarks>
    /// 名字必须是字面量 `ballast_weight_limit`，**不带** `name` 前缀（基线写死）。方向是 `ballast_weight ≥ min_ballast_weight`。
    ///
    /// The name must be the literal `ballast_weight_limit` with **no** `name` prefix (the baseline hard-codes it), and the direction is
    /// `ballast_weight ≥ min_ballast_weight`.
    /// </remarks>
    [Fact]
    public void BallastWeightLimit_ShouldAddTheHardCodedRow() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel aircraft) = Prepare();
        var ballast = new Ballast(aircraft, positions, minBallastWeight: 120.0, load);
        ballast.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        var limit = new BallastWeightLimit(ballast);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().Equal("ballast_weight_limit");
        LinearInequalityConstraint<Flt64> row = Row(model, "ballast_weight_limit");
        row.Inequality.Comparison.Should().Be(Comparison.GE);
        row.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(120.0, 1e-9);
        row.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
            .Should().BeEquivalentTo(ballast.BallastWeightSymbol!.Polynomial.Monomials.Select(monomial => monomial.Symbol));
    }

    /// <summary>
    /// **压舱重量：没有最低值时不加行** / Ballast weight: no row without a minimum.
    /// </summary>
    /// <remarks>
    /// 与"有最低值就加行"成对。没有最低压舱重量是合法配置——不装压舱物的航班不该有这条约束。
    /// 若这里仍加一行，会变成 `ballast_weight ≥ 0`，看起来无害，但它把"没有要求"变成了"要求非负"。
    ///
    /// Paired with "a minimum yields a row". No minimum is a legitimate configuration — a flight carrying no ballast should not have the constraint. Adding one anyway gives
    /// `ballast_weight ≥ 0`, which looks harmless but turns "no requirement" into "must be non-negative".
    /// </remarks>
    [Fact]
    public void BallastWeightLimit_ShouldAddNothingWithoutAMinimum() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel aircraft) = Prepare();
        var ballast = new Ballast(aircraft, positions, minBallastWeight: null, load);
        ballast.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        var limit = new BallastWeightLimit(ballast);
        limit.Invoke(model).IsOk.Should().BeTrue();
        limit.LastAddedNames.Should().BeEmpty();
        model.Constraints.Select(constraint => constraint.Name).Should().NotContain("ballast_weight_limit");
    }

    /// <summary>
    /// **压舱重量：缺符号时明确失败** / Ballast weight: a missing symbol fails loudly.
    /// </summary>
    /// <remarks>
    /// 空壳对任何输入都返回 `Ok`。缺符号时报成功会让调用方以为压舱下限在模型里，而实际没有。
    ///
    /// The shell returned `Ok` for any input. Reporting success while the symbol is missing tells the caller the ballast floor is in the model when it is not.
    /// </remarks>
    [Fact]
    public void BallastWeightLimit_ShouldFailWithoutTheSymbols() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel aircraft) = Prepare();
        var ballast = new Ballast(aircraft, positions, minBallastWeight: 120.0, load);
        // 故意不调 Register：两个符号都没有。

        var limit = new BallastWeightLimit(ballast);
        limit.Invoke(model).IsOk.Should().BeFalse();
        limit.LastAddedNames.Should().BeEmpty();
    }

    private static HorizontalStabilizer MakeStabilizer(HorizontalStabilizer.Limit limit) =>
        new(
            new HorizontalStabilizer.Key(new HorizontalStabilizerAngle("0"), null),
            new[] { new HorizontalStabilizer.Point(40000.0, 20.0, 5.0), new HorizontalStabilizer.Point(60000.0, 25.0, 9.0) },
            limit);

    private static (LinearMetaModel<Flt64> Model, Dictionary<HorizontalStabilizer.Key, HorizontalStabilizer> Map) PrepareStabilizers(
        HorizontalStabilizer.Limit limit) {
        var model = new LinearMetaModel<Flt64>();
        HorizontalStabilizer stabilizer = MakeStabilizer(limit);
        // **起 `Register` 收装载模式**：`withMacOptimization` 为真时才注册警告松弛。
        // **Since `Register` takes the stowage mode**: the warning slack is registered only when `withMacOptimization` is true.
        stabilizer.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        return (model, new Dictionary<HorizontalStabilizer.Key, HorizontalStabilizer> { [stabilizer.Configuration] = stabilizer });
    }

    /// <summary>
    /// **水平安定面：非建议模式只有硬上下界** / Horizontal stabilizer: non-recommended mode has only the hard bounds.
    /// </summary>
    /// <remarks>
    /// 限制里四档全给，但 `FullLoad` 只加 `_ub` 与 `_lb`——警告档只在建议模式下生效。
    ///
    /// All four limits are supplied, but `FullLoad` adds only `_ub` and `_lb`; the warning bounds apply in recommended mode alone.
    /// </remarks>
    [Fact]
    public void HorizontalStabilizerLimit_ShouldIgnoreWarningsOutsideRecommendation() {
        (LinearMetaModel<Flt64> model, Dictionary<HorizontalStabilizer.Key, HorizontalStabilizer> map) = PrepareStabilizers(
            new HorizontalStabilizer.Limit(MinTrim: 2.0, MaxTrim: 10.0, WarnMinTrim: 4.0, WarnMaxTrim: 8.0));
        var limit = new HorizontalStabilizerLimit(map, StowageMode.FullLoad);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().HaveCount(2);
        limit.LastAddedNames.Should().Contain(name => name.EndsWith("_ub", System.StringComparison.Ordinal));
        limit.LastAddedNames.Should().Contain(name => name.EndsWith("_lb", System.StringComparison.Ordinal));
        limit.LastAddedNames.Should().NotContain(name => name.Contains("warn", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// **水平安定面：建议模式下警告档生效，且常规下界仍在** / Recommended mode takes the warning bounds and keeps the regular floor.
    /// </summary>
    /// <remarks>
    /// 三行：`_warn_ub`（8）、`_warn_lb`（4）、`_lb`（2）。警告下界**不替代**常规下界——两者同时存在，因为常规下界是硬边界。
    /// 若把两者合成一行（取更严的 4），2 那条就消失了，而"硬边界 2"在警告档之外仍然有效。
    ///
    /// Three rows: `_warn_ub` (8), `_warn_lb` (4), `_lb` (2). The warning floor does **not** replace the regular one — both are present, because the regular one is the hard limit.
    /// Folding them into one (the tighter 4) drops the 2, and the hard limit still applies outside the warning band.
    /// </remarks>
    [Fact]
    public void HorizontalStabilizerLimit_ShouldAddTheWarningBoundsUnderRecommendation() {
        (LinearMetaModel<Flt64> model, Dictionary<HorizontalStabilizer.Key, HorizontalStabilizer> map) = PrepareStabilizers(
            new HorizontalStabilizer.Limit(MinTrim: 2.0, MaxTrim: 10.0, WarnMinTrim: 4.0, WarnMaxTrim: 8.0));
        var limit = new HorizontalStabilizerLimit(map, StowageMode.WeightRecommendation);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().HaveCount(3);
        LinearInequalityConstraint<Flt64> warnUpper = Row(model, limit.LastAddedNames.Single(name => name.EndsWith("_warn_ub", System.StringComparison.Ordinal)));
        LinearInequalityConstraint<Flt64> warnLower = Row(model, limit.LastAddedNames.Single(name => name.EndsWith("_warn_lb", System.StringComparison.Ordinal)));
        LinearInequalityConstraint<Flt64> lower = Row(model, limit.LastAddedNames.Single(name => name.EndsWith("_lb", System.StringComparison.Ordinal) && !name.Contains("warn", System.StringComparison.Ordinal)));

        warnUpper.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(8.0, 1e-9);
        warnUpper.Inequality.Comparison.Should().Be(Comparison.LE);
        warnLower.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(4.0, 1e-9);
        warnLower.Inequality.Comparison.Should().Be(Comparison.GE);
        lower.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(2.0, 1e-9);
    }

    /// <summary>
    /// **水平安定面：建议模式无警告值时退回 `_ub` 而不是 `_warn_ub`** / Recommended mode without a warning value falls back to `_ub`, not `_warn_ub`.
    /// </summary>
    /// <remarks>
    /// 这是这里最细的一处：**取值**用 `warnMaxTrim ?? maxTrim`，**命名**用"`warnMaxTrim` 是否非空"。两者是不同的条件。
    /// 只有 `maxTrim` 时，取值是 10、名字是 `_ub`。用同一个条件时会产生 `_warn_ub` 这个名字却没有警告值——而报告里看不出区别。
    ///
    /// The finest point of: the **value** uses `warnMaxTrim ?? maxTrim` while the **name** follows whether `warnMaxTrim` is non-null — two different conditions. With only
    /// `maxTrim` the value is 10 and the name is `_ub`. One condition for both yields a `_warn_ub` row carrying no warning value, and no report would show it.
    /// </remarks>
    [Fact]
    public void HorizontalStabilizerLimit_ShouldNameTheFallbackRowUb() {
        (LinearMetaModel<Flt64> model, Dictionary<HorizontalStabilizer.Key, HorizontalStabilizer> map) = PrepareStabilizers(
            new HorizontalStabilizer.Limit(MinTrim: 2.0, MaxTrim: 10.0));
        var limit = new HorizontalStabilizerLimit(map, StowageMode.WeightRecommendation);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().HaveCount(2);
        limit.LastAddedNames.Should().NotContain(name => name.Contains("warn", System.StringComparison.Ordinal),
            "there is no warning value, so no name may claim one");
        LinearInequalityConstraint<Flt64> upper = Row(model, limit.LastAddedNames.Single(name => name.EndsWith("_ub", System.StringComparison.Ordinal)));
        upper.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(10.0, 1e-9);
    }

    /// <summary>
    /// **不对称密度：每个曲线点两行，方向相反** / Unsymmetrical density: two opposite rows per curve point.
    /// </summary>
    /// <remarks>
    /// 一个曲线点 (lhs=100, rhs=150) → 两行：`+1·左 −1·右 ≤ 100` 与 `−1·左 +1·右 ≤ 150`。只加一行时只限制单侧不对称。
    ///
    /// One curve point (lhs=100, rhs=150) → two rows: `+1·left −1·right ≤ 100` and `−1·left +1·right ≤ 150`. One row alone bounds a single side.
    /// </remarks>
    [Fact]
    public void UnsymmetricalDensity_ShouldAddTwoOppositeRowsPerPoint() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel aircraft) = Prepare();
        var linearDensity = new LinearDensity(
            new[] {
                new LinearDensityLimitZone("zone", new HashSet<DeckLocation> { DeckLocation.Main }, 500.0, 900.0, 3000.0),
            },
            new[] {
                new LinearDensityLimitLine(
                    new LinearDensityLimitZone("zone", new HashSet<DeckLocation> { DeckLocation.Main }, 500.0, 900.0, 3000.0),
                    700.0,
                    new[] { 0, 1 }),
            },
            positions.Select(position => position.AircraftPosition).ToList(),
            load);
        linearDensity.Register(model).IsOk.Should().BeTrue();

        var zone = new UnsymmetricalLinearDensityLimitZone(
            "zone",
            frontArm: 500.0,
            backArm: 900.0,
            points: new[] { new UnsymmetricalLinearDensityLimitPoint(Lhs: 100.0, Rhs: 150.0) },
            positions: positions.Select(position => position.AircraftPosition).ToList());
        zone.Limits.Should().HaveCount(2);
        zone.Limits[0].LeftCoefficient.Should().Be(1.0);
        zone.Limits[0].RightCoefficient.Should().Be(-1.0);
        zone.Limits[0].MaxSum.Should().BeApproximately(100.0, 1e-9);
        zone.Limits[1].LeftCoefficient.Should().Be(-1.0);
        zone.Limits[1].RightCoefficient.Should().Be(1.0);
        zone.Limits[1].MaxSum.Should().BeApproximately(150.0, 1e-9);

        var limit = new Limits.UnsymmetricalLinearDensityLimit(
            new MaxUnsymmetricalLinearDensity { LimitZones = new[] { zone } },
            linearDensity,
            positions);
        limit.Invoke(model).IsOk.Should().BeTrue();

        // 候选力臂 = **区域两端 + 区域舱位的前后力臂**，去重后排序。本例两舱位的 `FrontArm` 都是 500、`BackArm` 都是 900，
        // 与区域两端重合，所以候选只有 {500, 900} —— **700 不在候选集里**，因为没有任何舱位的边界落在 700。
        // 最初把 700 当成了候选，判据当场红了。
        // Candidate arms are the **zone ends plus the zone positions' front and back arms**, deduplicated and sorted. Here both positions have `FrontArm` 500 and `BackArm` 900,
        // coinciding with the zone ends, so the candidates are only {500, 900} — **700 is not a candidate**, because no position boundary falls there. The first draft assumed
        // 700 was one and went red.
        zone.Lines.Select(line => line.Arm).Should().Equal(500.0, 900.0);
        limit.LastAddedNames.Should().HaveCount(4);
        limit.LastAddedNames.Should().BeEquivalentTo(new[] {
            "unsymmetrical_linear_density_limit_zone_500_0",
            "unsymmetrical_linear_density_limit_zone_500_1",
            "unsymmetrical_linear_density_limit_zone_900_0",
            "unsymmetrical_linear_density_limit_zone_900_1",
        });
        Row(model, "unsymmetrical_linear_density_limit_zone_500_0")
            .Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(100.0, 1e-9);
        Row(model, "unsymmetrical_linear_density_limit_zone_500_1")
            .Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(150.0, 1e-9);
        Row(model, "unsymmetrical_linear_density_limit_zone_500_0")
            .Inequality.Comparison.Should().Be(Comparison.LE);
    }

    /// <summary>
    /// **不对称密度：整线两侧都不可用才跳过** / Unsymmetrical density: the line is skipped only when both sides are unavailable.
    /// </summary>
    /// <remarks>
    /// 基线是 `line.positions.none { it.status.available }`——**两侧都不可用**才跳过。只有一侧不可用时仍加两行。
    ///
    /// **记录一处不可观测**：候选力臂集合（区域两端 + 舱位前后力臂）里，区域两端只有在**恰好**有舱位落在其上时才会产生限制线；
    /// 本例两舱位的 `FrontArm`/`BackArm` 都是 500/900，把区域放大到 400—1000 之后，400 与 1000 上没有任何舱位满足 `On(arm)`，
    /// 所以 `Lines` 仍是 {500, 900}。把"区域两端进候选"那一行删掉，`Lines` 与所有约束**完全不变**（实测变异存活）。
    /// 这条性质只在"区域端点恰好落在某舱位的区间内且该点有左右配对"时才可观测，而那种输入与"舱位边界进候选"无法区分。
    /// 因此照抄基线的写法，并在此记录它没有被判据覆盖。
    ///
    /// The baseline is `line.positions.none { it.status.available }` — skipped only when **both** sides are unavailable; with one side unavailable the two rows stay.
    /// **One unobservable recorded**: among the candidate arms (zone ends plus position boundaries) a zone end only produces a line when a position happens to satisfy `On(arm)`
    /// there. Here both positions have `FrontArm`/`BackArm` of 500/900, so after widening the zone to 400—1000 neither 400 nor 1000 has any position on it and `Lines` stays
    /// {500, 900}. Deleting the "zone ends are candidates" line changes `Lines` and every constraint **not at all** (the mutation survives, measured). That property is only
    /// observable when a zone end falls inside some position's span *and* has a left/right pair there, and such an input cannot be told apart from a position boundary.
    /// The baseline's form is copied and its lack of coverage recorded here.
    /// </remarks>
    [Fact]
    public void UnsymmetricalDensity_ShouldSkipOnlyWhenBothSidesAreUnavailable() {
        var bothAvailable = new List<StowagePosition> {
            MakePosition("L1", 1UL, PositionLocation.NormalMainLocation, leftArm: -40.0, rightArm: -40.0),
            MakePosition("R1", 2UL, PositionLocation.NormalMainLocation, leftArm: 40.0, rightArm: 40.0),
        };
        (IReadOnlyList<StowagePosition> _, LinearMetaModel<Flt64> oneModel, Load oneLoad, AircraftModel _) = Prepare(positions: bothAvailable);
        var oneZone = new UnsymmetricalLinearDensityLimitZone(
            "zone",
            frontArm: 500.0,
            backArm: 900.0,
            points: new[] { new UnsymmetricalLinearDensityLimitPoint(Lhs: 100.0, Rhs: 150.0) },
            positions: bothAvailable.Select(position => position.AircraftPosition).ToList());
        var oneDensity = new LinearDensity(
            new[] { new LinearDensityLimitZone("zone", new HashSet<DeckLocation> { DeckLocation.Main }, 500.0, 900.0, 3000.0) },
            new[] { new LinearDensityLimitLine(new LinearDensityLimitZone("zone", new HashSet<DeckLocation> { DeckLocation.Main }, 500.0, 900.0, 3000.0), 700.0, new[] { 0, 1 }) },
            bothAvailable.Select(position => position.AircraftPosition).ToList(),
            oneLoad);
        oneDensity.Register(oneModel).IsOk.Should().BeTrue();
        var oneLimit = new Limits.UnsymmetricalLinearDensityLimit(
            new MaxUnsymmetricalLinearDensity { LimitZones = new[] { oneZone } },
            oneDensity,
            bothAvailable);
        oneLimit.Invoke(oneModel).IsOk.Should().BeTrue();
        oneLimit.LastAddedNames.Should().HaveCount(4, "two candidate arms times two rows, both sides available");

        var leftUnavailable = new List<StowagePosition> {
            MakePosition("L1", 1UL, PositionLocation.NormalMainLocation, leftArm: -40.0, rightArm: -40.0, available: false),
            MakePosition("R1", 2UL, PositionLocation.NormalMainLocation, leftArm: 40.0, rightArm: 40.0),
        };
        (IReadOnlyList<StowagePosition> _, LinearMetaModel<Flt64> halfModel, Load halfLoad, AircraftModel _) = Prepare(positions: leftUnavailable);
        var halfZone = new UnsymmetricalLinearDensityLimitZone(
            "zone",
            frontArm: 500.0,
            backArm: 900.0,
            points: new[] { new UnsymmetricalLinearDensityLimitPoint(Lhs: 100.0, Rhs: 150.0) },
            positions: leftUnavailable.Select(position => position.AircraftPosition).ToList());
        var halfDensity = new LinearDensity(
            new[] { new LinearDensityLimitZone("zone", new HashSet<DeckLocation> { DeckLocation.Main }, 500.0, 900.0, 3000.0) },
            new[] { new LinearDensityLimitLine(new LinearDensityLimitZone("zone", new HashSet<DeckLocation> { DeckLocation.Main }, 500.0, 900.0, 3000.0), 700.0, new[] { 0, 1 }) },
            leftUnavailable.Select(position => position.AircraftPosition).ToList(),
            halfLoad);
        halfDensity.Register(halfModel).IsOk.Should().BeTrue();
        var halfLimit = new Limits.UnsymmetricalLinearDensityLimit(
            new MaxUnsymmetricalLinearDensity { LimitZones = new[] { halfZone } },
            halfDensity,
            leftUnavailable);
        halfLimit.Invoke(halfModel).IsOk.Should().BeTrue();
        halfLimit.LastAddedNames.Should().HaveCount(4, "one side is still available, so the rows stay");
    }

    /// <summary>
    /// **不对称密度：一个力臂上左右各超过一个舱位时不建线** / A line needs exactly one position per side.
    /// </summary>
    /// <remarks>
    /// 上一条判据每个力臂上恰好一左一右，所以"恰好一个"的条件被掩盖了——放宽成"至少一个"结果不变。
    /// 这条在 500 力臂上放**两个左侧**舱位：那里不能建线（两侧数量不等就无法定义"不对称"），而 900 力臂上仍是一左一右，照建。
    ///
    /// The criterion above has exactly one per side at every arm, so the "exactly one" condition is masked — relaxing it to "at least one" changes nothing. This one puts **two
    /// left** positions at the 500 arm: no line can be built there (asymmetry is undefined when the sides are unbalanced), while the 900 arm still has one each and keeps its line.
    /// </remarks>
    [Fact]
    public void UnsymmetricalDensity_ShouldRequireExactlyOnePositionPerSide() {
        var positions = new List<StowagePosition> {
            MakePosition("L1", 1UL, PositionLocation.NormalMainLocation, leftArm: -40.0, rightArm: -40.0),
            MakePosition("L2", 2UL, PositionLocation.NormalMainLocation, leftArm: -80.0, rightArm: -80.0),
            MakePosition("R1", 3UL, PositionLocation.NormalMainLocation, leftArm: 40.0, rightArm: 40.0),
        };
        // L1 覆盖 500—900、L2 只覆盖 500—500（用相同的 500—900 也行：两者都在 500 力臂上）。
        // 用同一个区间让两个左侧舱位都落在 500 力臂上。
        // L1 and L2 both cover 500—900, so both sit on the 500 arm; R1 covers it too.
        var zone = new UnsymmetricalLinearDensityLimitZone(
            "unbalanced",
            frontArm: 500.0,
            backArm: 900.0,
            points: new[] { new UnsymmetricalLinearDensityLimitPoint(Lhs: 100.0, Rhs: 150.0) },
            positions: positions.Select(position => position.AircraftPosition).ToList());

        zone.Lines.Select(line => line.Arm).Should().BeEmpty(
            "every candidate arm has two left positions, so no line can be built");
        zone.Lines.Should().BeEmpty();
    }

    /// <summary>
    /// **不对称密度：缺线密度符号时明确失败** / Unsymmetrical density: a missing density symbol fails loudly.
    /// </summary>
    /// <remarks>
    /// 空壳对任何输入都返回 `Ok`。缺符号时报成功会让调用方以为不对称限制在模型里，而实际没有。
    ///
    /// The shell returned `Ok` for any input. Reporting success while the symbol is missing tells the caller the unsymmetrical bound is in the model when it is not.
    /// </remarks>
    [Fact]
    public void UnsymmetricalDensity_ShouldFailWithoutTheDensitySymbols() {
        (IReadOnlyList<StowagePosition> positions, LinearMetaModel<Flt64> model, Load load, AircraftModel _) = Prepare();
        var zone = new UnsymmetricalLinearDensityLimitZone(
            "zone",
            frontArm: 500.0,
            backArm: 900.0,
            points: new[] { new UnsymmetricalLinearDensityLimitPoint(Lhs: 100.0, Rhs: 150.0) },
            positions: positions.Select(position => position.AircraftPosition).ToList());
        // 不注册线密度：`LinearDensity.Register` 从未调用。
        var density = new LinearDensity(
            new[] { new LinearDensityLimitZone("zone", new HashSet<DeckLocation> { DeckLocation.Main }, 500.0, 900.0, 3000.0) },
            new[] { new LinearDensityLimitLine(new LinearDensityLimitZone("zone", new HashSet<DeckLocation> { DeckLocation.Main }, 500.0, 900.0, 3000.0), 700.0, new[] { 0, 1 }) },
            positions.Select(position => position.AircraftPosition).ToList(),
            load);
        var limit = new Limits.UnsymmetricalLinearDensityLimit(
            new MaxUnsymmetricalLinearDensity { LimitZones = new[] { zone } },
            density,
            positions);

        limit.Invoke(model).IsOk.Should().BeFalse();
        limit.LastAddedNames.Should().BeEmpty();
    }
}
