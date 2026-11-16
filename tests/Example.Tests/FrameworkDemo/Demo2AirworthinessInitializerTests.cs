// 适航安全聚合初始化器的判据 / Criteria for the airworthiness security aggregation initializer.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using AirworthinessInitializer = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.AggregationInitializer;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **适航安全聚合初始化器** / The airworthiness security aggregation initializer.
/// </summary>
/// <remarks>
/// 这是一整片 DTO → 模型的翻译，没有算法。因此判据集中在**最容易抄漏的四个地方**：
///
/// 1. 四条校验的边界（尤其"业载上限为负"与"包线区间反了"这两条）。
/// 2. `maxOf(1.0, …)` 的除零保护——写成 `maxOf(0.0, …)` 时包线退化。
/// 3. 下甲板载荷下限只在**有下甲板舱位**时非零。
/// 4. 累积载荷两个区域的扫描方向相反（前向按后臂升序、后向按前臂降序）。
///
/// It is one long DTO-to-model translation with no algorithm, so the criteria target **the four things most easily dropped**: the four validation boundaries (especially the
/// negative payload bound and the inverted envelope interval); the `maxOf(1.0,...)` divide-by-zero guard (with `maxOf(0.0,...)` the envelope collapses); the low-deck payload
/// floor being non-zero only **with a low-deck position**; and the two cumulative zones sweeping in opposite directions.
/// </remarks>
public class Demo2AirworthinessInitializerTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(string name, double weight = 100.0) =>
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
        double frontArm,
        double backArm,
        PositionLocation? location = null) {
        var position = new AircraftPosition(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(frontArm, backArm, -40.0, -40.0),
            shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
            location: location ?? PositionLocation.NormalMainLocation);
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

    private sealed class Rig
    {
        public Rig(
            RequestDTO? input = null,
            bool withLowDeck = false,
            AircraftType type = AircraftType.B737,
            IReadOnlyList<StowagePosition>? positions = null,
            double cargoWeight = 400.0) {
            var items = new List<Item> { MakeItem("I1", cargoWeight) };
            // 两个位置的**前臂顺序与后臂顺序必须相反**：否则"前向按后臂升序"与"按前臂升序"给出同一个结果，方向写错也测不出来。
            // P1 front 400 / back 900，P2 front 600 / back 700 —— 按前臂升序是 P1,P2，按后臂升序是 P2,P1。
            // The two positions' **front-arm and back-arm orders must oppose**: otherwise "forward by back arm ascending" and "by front arm ascending" coincide and a wrong
            // direction is invisible. P1 is front 400 / back 900 and P2 is front 600 / back 700 — front-ascending gives P1,P2 while back-ascending gives P2,P1.
            positions ??= new List<StowagePosition> {
                MakePosition("P1", 1UL, frontArm: 400.0, backArm: 900.0),
                MakePosition("P2", 2UL, frontArm: 600.0, backArm: 700.0),
            };
            if (withLowDeck) {
                var low = new PositionLocation(new HashSet<PositionLocationTag> { PositionLocationTag.LowForward });
                positions = positions.Concat(new[] { MakePosition("P3", 3UL, frontArm: 800.0, backArm: 1000.0, location: low) }).ToList();
            }
            Items = items;
            Positions = positions;

            Aircraft = new AircraftModel(type, type.ToString(), new AircraftMinorModel(type.ToString()));
            var fuselage = new Fuselage(null, Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);
            var formula = new Formula(Aircraft, lip: 100.0, chord: 20.0, standardDatum: 120.0, forceDistanceCoefficient: 5000.0, doiCorrection: 7.0);
            var fuel = new Dictionary<FlightPhase, FuelConstant> {
                [FlightPhase.TakeOff] = new FuelConstant(Density: 0.8, Weight: 1000.0, Index: 1000.0),
                [FlightPhase.Landing] = new FuelConstant(Density: 0.8, Weight: 2000.0, Index: 2000.0),
            };
            var deck = new Deck(DeckLocation.Main, new List<HatchDoor>(), positions.Select(p => p.AircraftPosition).ToList(), new Dictionary<HatchDoor, IReadOnlyList<DoorUbiety>>());
            AircraftAggregation = new AircraftAggregation(
                new RegNo("B-0001"),
                Aircraft,
                formula,
                fuselage,
                new List<FuelTank>(),
                fuel,
                new List<Deck> { deck },
                new Dictionary<NeighbourType, IReadOnlyList<Neighbour>>());

            var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
            var flight = new Flight(new FlightNo("CA1234"), new IATACode("PEK"), new IATACode("SHA"));
            StowageAggregation = new StowageAggregation(
                Aircraft,
                flight,
                items,
                positions,
                new Appointment(new Dictionary<Item, AircraftPosition>()),
                new BiologicalLimit(new List<(CargoType, CargoType)>(), new List<(CargoType, CargoType)>(), new List<CargoType>()),
                new Dictionary<NeighbourType, IReadOnlyList<Neighbour>>(),
                plannedPayload: 5000.0,
                maxPayload: 20000.0,
                withMultiLoadingSchema: true,
                formula: formula,
                fuselage: fuselage,
                fuel: fuel);

            MacAggregation = new MacAggregation(
                fuselage,
                fuel,
                formula,
                positions,
                StowageAggregation.Load,
                StowageAggregation.TotalWeight,
                new Dictionary<HorizontalStabilizer.Key, (IReadOnlyList<HorizontalStabilizer.Point>, HorizontalStabilizer.Limit)>(),
                type.IsWideBody());

            Input = input ?? new RequestDTO(
                Id: "REQ-1",
                Cargos: new List<CargoInput>(),
                Positions: new List<PositionInput>(),
                PayloadUpperBound: 5000.0,
                MinPayloadRatio: 0.5,
                MaxAdjacentLoadGap: 8.0,
                MaxCumulativeForwardLoad: 20.0,
                MaxCumulativeBackwardLoad: 30.0,
                EnvelopeLongitudinalMomentMin: -20.0,
                EnvelopeLongitudinalMomentMax: 40.0,
                TargetLongitudinalMoment: 5.0,
                MaxLongitudinalMomentDeviation: 15.0,
                MaxLateralImbalance: 12.0);
        }

        public IReadOnlyList<Item> Items { get; }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public AircraftModel Aircraft { get; }

        public AircraftAggregation AircraftAggregation { get; }

        public StowageAggregation StowageAggregation { get; }

        public MacAggregation MacAggregation { get; }

        public RequestDTO Input { get; }

        public Result<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>> Run() =>
            AirworthinessInitializer.Initialize(AircraftAggregation, StowageAggregation, MacAggregation, Input);
    }

    /// <summary>
    /// **合法输入构造出完整聚合，DTO 值逐个落地** / A valid input builds the whole aggregation with every DTO value landing.
    /// </summary>
    /// <remarks>
    /// 成对：`IsOk` 为真，且每个 DTO 数值在聚合里都能找到——`minPayload = min(上限, 货重) × 比例`、三个力矩值、`maxAdjacentLoadGap`、
    /// 两条累积载荷上限各自进了**自己方向**的区域（前 20 / 后 30，不交叉）。
    ///
    /// 只断言 `IsOk` 无法区分"真的翻译了"与"返回一个空聚合"——后者正是这个文件此前最坏的形态。
    ///
    /// Paired: `IsOk` is true and every DTO number is found in the aggregation — `minPayload = min(bound, cargo) × ratio`, the three moment values, `maxAdjacentLoadGap`, and each
    /// cumulative limit landing in **its own direction's** zone (20 forward, 30 aft, no crossing). Asserting only `IsOk` cannot distinguish a real translation from an empty
    /// aggregation — the worst form this file previously took.
    /// </remarks>
    [Fact]
    public void Initialize_ShouldTranslateEveryDtoValue() {
        var rig = new Rig();
        Result<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>> result = rig.Run();
        result.IsOk.Should().BeTrue();
        AirworthinessSecurityAggregation aggregation = ((Ok<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>>)result).Value!;

        // min(5000, 400) × 0.5 = 200：**上限与货重取小**，不是直接用上限。
        // min(5000, 400) × 0.5 = 200: the **smaller of the bound and the cargo**, not the bound itself.
        aggregation.MinPayload.Should().BeApproximately(200.0, 1e-9);
        aggregation.TargetLongitudinalMoment.Should().BeApproximately(5.0, 1e-9);
        aggregation.MaxLongitudinalMomentDeviation.Should().BeApproximately(15.0, 1e-9);
        aggregation.MaxLateralImbalance.Should().BeApproximately(12.0, 1e-9);
        aggregation.MaxAdjacentLoadGap.Should().BeApproximately(8.0, 1e-9);

        IReadOnlyList<CumulativeLoadLimitZone> zones = aggregation.MaxCumulativeLoadWeight.LimitZones;
        zones.Should().HaveCount(2);
        CumulativeLoadLimitZone forward = zones.Single(zone => zone.Direction == CumulativeLoadDirection.FWD);
        CumulativeLoadLimitZone aft = zones.Single(zone => zone.Direction == CumulativeLoadDirection.AFT);
        forward.Points.Should().OnlyContain(point => point.MaxSum == 20.0, "the forward limit stays forward");
        aft.Points.Should().OnlyContain(point => point.MaxSum == 30.0, "the aft limit stays aft");
        forward.CheckPoints.Should().NotBeEmpty("the forward zone has checkpoints");
        aft.CheckPoints.Should().NotBeEmpty("the aft zone has checkpoints");
    }

    /// <summary>
    /// **累积载荷两个区域的扫描方向相反** / The two cumulative zones sweep in opposite directions.
    /// </summary>
    /// <remarks>
    /// 成对断言：前向区域的 `FromArm` 是**最小后臂所属舱位的前臂**，检查点按后臂**升序**；后向区域的 `FromArm` 是**最大前臂所属舱位的后臂**，
    /// 检查点按前臂**降序**。把两个排序写成同一个方向不会报错，只会让限制区间反着来。
    ///
    /// Paired: the forward zone's `FromArm` is the **front arm of the position with the smallest back arm** and its checkpoints ascend by back arm; the aft zone's `FromArm` is the
    /// **back arm of the position with the largest front arm** and its checkpoints descend by front arm. Writing both orders the same way raises no error and merely reverses a
    /// limit interval.
    /// </remarks>
    [Fact]
    public void Initialize_ShouldSweepTheTwoCumulativeZonesInOppositeDirections() {
        var rig = new Rig();
        AirworthinessSecurityAggregation aggregation =
            ((Ok<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>>)rig.Run()).Value!;

        IReadOnlyList<CumulativeLoadLimitZone> zones = aggregation.MaxCumulativeLoadWeight.LimitZones;
        CumulativeLoadLimitZone forward = zones.Single(zone => zone.Direction == CumulativeLoadDirection.FWD);
        CumulativeLoadLimitZone aft = zones.Single(zone => zone.Direction == CumulativeLoadDirection.AFT);

        forward.FromArm.Should().BeApproximately(600.0, 1e-9, "P2 has the smallest back arm (700), so its front arm starts the forward sweep");
        forward.Points.Select(point => point.ToArm).Should().BeInAscendingOrder("the forward sweep walks by back arm upward");
        forward.Points[0].ToArm.Should().BeApproximately(700.0, 1e-9);

        aft.FromArm.Should().BeApproximately(700.0, 1e-9, "P2 has the largest front arm (600), so its back arm (700) starts the aft sweep");
        aft.Points.Select(point => point.ToArm).Should().BeInDescendingOrder("the aft sweep walks by front arm downward");
        aft.Points[0].ToArm.Should().BeApproximately(600.0, 1e-9);
    }

    /// <summary>
    /// **下甲板载荷下限只在有下甲板舱位时非零** / The low-deck payload floor is non-zero only with a low-deck position.
    /// </summary>
    /// <remarks>
    /// 成对：全是主甲板舱位时两个点的下限都是 `0`；加一个下甲板舱位后变成 `minPayload`。没有下甲板却给 `minPayload` 会要求主甲板承担本该由下货舱承担的下限。
    ///
    /// Paired: with main-deck positions only both points carry `0`; adding one low-deck position makes them `minPayload`. Demanding `minPayload` without a low deck makes the main
    /// deck carry a floor meant for the lower hold.
    /// </remarks>
    [Fact]
    public void Initialize_ShouldApplyTheLowDeckFloorOnlyWithALowDeckPosition() {
        var mainOnly = new Rig(withLowDeck: false);
        AirworthinessSecurityAggregation withoutLow =
            ((Ok<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>>)mainOnly.Run()).Value!;
        withoutLow.MinLowPayload.Points.Should().OnlyContain(point => point.MinLowPayloadValue == 0.0);
        withoutLow.MinLowPayload.Points.Should().HaveCount(2, "the floor is a two-point interpolation either way");

        var withLow = new Rig(withLowDeck: true);
        AirworthinessSecurityAggregation withLowDeck =
            ((Ok<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>>)withLow.Run()).Value!;
        withLowDeck.MinLowPayload.Points.Should().OnlyContain(point => point.MinLowPayloadValue == withLowDeck.MinPayload);
        withLowDeck.MinLowPayload.Points[1].Zfw.Should().BeApproximately(mainOnly.Input.PayloadUpperBound, 1e-9,
            "the second point sits at the payload upper bound");
    }

    /// <summary>
    /// **包线上端点用 `maxOf(1.0, …)` 而不是 `maxOf(0.0, …)`** / The envelope's upper weight end uses `maxOf(1.0,...)`, not `maxOf(0.0,...)`.
    /// </summary>
    /// <remarks>
    /// 把业载上限与货量都设成 0，上端点仍必须是 `1.0`。写成 `maxOf(0.0, …)` 时两个端点重量相等、包线退化成一个点，插值会除以零。
    ///
    /// With both the payload bound and the cargo set to zero the upper end must still be `1.0`. With `maxOf(0.0,...)` the two end weights are equal, the envelope collapses to a
    /// point, and interpolation divides by zero.
    /// </remarks>
    [Fact]
    public void Initialize_ShouldGuardTheEnvelopeUpperWeightAgainstZero() {
        // **货量也必须是 0**：上端点取 `max(1, 上限, 货量)`，货量 400 时上端点就是 400，除零保护根本不会触发。
        // **The cargo must be zero too**: the upper end is `max(1, bound, cargo)`, so with 400 kg of cargo it is 400 and the guard never fires.
        var rig = new Rig(cargoWeight: 0.0, input: new RequestDTO(
            Id: "REQ-1",
            Cargos: new List<CargoInput>(),
            Positions: new List<PositionInput>(),
            PayloadUpperBound: 0.0,
            MinPayloadRatio: 0.0,
            MaxCumulativeForwardLoad: 20.0,
            MaxCumulativeBackwardLoad: 30.0,
            EnvelopeLongitudinalMomentMin: -20.0,
            EnvelopeLongitudinalMomentMax: 40.0));
        AirworthinessSecurityAggregation aggregation =
            ((Ok<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>>)rig.Run()).Value!;

        Envelope envelope = aggregation.Envelopes[FlightPhase.TakeOff].Cast<Envelope>().Single();
        envelope.LhsSide.Points.Select(point => point.TotalWeight).Should().BeEquivalentTo(new[] { 0.0, 1.0 });
        envelope.LhsSide.Points[1].TotalWeight.Should().BeGreaterThan(0.0, "a zero-length envelope divides by zero during interpolation");
        envelope.RhsSide.Points.Select(point => point.Index).Should().OnlyContain(index => index == 40.0);
        envelope.LhsSide.Points.Select(point => point.Index).Should().OnlyContain(index => index == -20.0);
    }

    /// <summary>
    /// **四条校验各自挡住自己的坏输入** / Each of the four validations rejects its own bad input.
    /// </summary>
    /// <remarks>
    /// 每条成对：坏输入失败、同一条的好输入成功。四条分别试**只改一个字段**，否则无法判断是哪条校验起的作用。
    /// 第一条（无舱位）用一个空位置列表——此时 `positions[0]` 会越界，所以它必须在构造累积载荷之前就挡住。
    ///
    /// Each is paired: the bad input fails and a good one with the same shape succeeds. The four change **exactly one field each**, otherwise it is impossible to tell which
    /// validation acted. The first (no positions) passes an empty list — `positions[0]` would go out of range there, so it has to reject before the cumulative zones are built.
    /// </remarks>
    [Fact]
    public void Initialize_ShouldRejectEachBadInputAtItsOwnBoundary() {
        RequestDTO good = new Rig().Input;

        var noPositions = new Rig(positions: new List<StowagePosition>());
        noPositions.Run().IsOk.Should().BeFalse("an empty position list would index out of range otherwise");

        var negativeForward = new Rig(input: good with { MaxCumulativeForwardLoad = -1.0 });
        negativeForward.Run().IsOk.Should().BeFalse("a negative cumulative limit is not a limit");
        new Rig(input: good with { MaxCumulativeForwardLoad = 0.0 }).Run().IsOk.Should().BeTrue("zero is allowed");

        var negativePayload = new Rig(input: good with { PayloadUpperBound = -1.0 });
        negativePayload.Run().IsOk.Should().BeFalse("a negative payload bound is not a bound");
        new Rig(input: good with { PayloadUpperBound = 0.0 }).Run().IsOk.Should().BeTrue("zero is allowed");

        var ratioTooBig = new Rig(input: good with { MinPayloadRatio = 1.5 });
        ratioTooBig.Run().IsOk.Should().BeFalse("the minimum ratio lives in [0, 1]");
        new Rig(input: good with { MinPayloadRatio = 1.0 }).Run().IsOk.Should().BeTrue("1.0 is the closed end");

        var invertedEnvelope = new Rig(input: good with { EnvelopeLongitudinalMomentMin = 50.0 });
        invertedEnvelope.Run().IsOk.Should().BeFalse("an inverted envelope interval is not an interval");
        new Rig(input: good with { EnvelopeLongitudinalMomentMin = 40.0, EnvelopeLongitudinalMomentMax = 40.0 }).Run().IsOk.Should().BeTrue(
            "a degenerate but ordered interval is allowed");

        var infiniteGap = new Rig(input: good with { MaxAdjacentLoadGap = double.PositiveInfinity });
        infiniteGap.Run().IsOk.Should().BeFalse("an infinite gap cannot bound a constraint");
    }

    /// <summary>
    /// **CLIM 只在宽体机上生成，密度区覆盖全部甲板位置** / CLIM exists only on a wide body; the density zones span every deck location.
    /// </summary>
    /// <remarks>
    /// 成对：窄体机 `MaxClimPoints` 为 `null`、宽体机非 `null`；密度区的 `Locations` 是**位置里出现的甲板集合**，`FrontArm`/`BackArm` 是
    /// 所有位置的最小前臂与最大后臂。把 `maxClim` 用 `abs(min)` 而不是 `max(abs(min), abs(max))` 会让 `-20 / 40` 的输入得到 20 而不是 40。
    ///
    /// **一处不可观测已记录**：把初始化器里的 `aircraftModel.WideBody` 改成 `true`（永远生成点表）时判据照常通过——
    /// `AirworthinessSecurityAggregation` 的构造函数**也**查 `aircraftModel.WideBody`，窄体机照样拿不到 `MaxCLIM`。这是双层防护，
    /// 单改一层在聚合这一出口上不可观测。两层的条件都去掉时 CLIM 判据会红。
    ///
    /// Paired: a narrow body yields `null` `MaxClimPoints` and a wide body a non-null list; the density zone's `Locations` is **the set of decks present** and its
    /// `FrontArm`/`BackArm` are the minimum front arm and maximum back arm over all positions. Using `abs(min)` instead of `max(abs(min), abs(max))` gives 20 rather than 40 for a
    /// `-20 / 40` input. **One unobservable recorded**: flipping the initializer's `aircraftModel.WideBody` to `true` leaves the criteria passing, because
    /// `AirworthinessSecurityAggregation`'s constructor **also** checks `aircraftModel.WideBody` and a narrow body still gets no `MaxCLIM` — two-layer protection, invisible at the
    /// aggregation exit when only one layer changes. Removing both would turn the CLIM criterion red.
    /// </remarks>
    [Fact]
    public void Initialize_ShouldBuildClimAndDensityZonesFromTheAircraft() {
        var narrow = new Rig(type: AircraftType.B737);
        AirworthinessSecurityAggregation narrowAggregation =
            ((Ok<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>>)narrow.Run()).Value!;
        narrowAggregation.MaxCLIM.Should().BeNull("a narrow body registers no CLIM");

        var wide = new Rig(type: AircraftType.B747);
        AirworthinessSecurityAggregation wideAggregation =
            ((Ok<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>>)wide.Run()).Value!;
        wideAggregation.MaxCLIM.Should().NotBeNull();
        wideAggregation.MaxCLIM!.Points.Should().OnlyContain(point => point.MaxClim == 40.0,
            "max(abs(-20), abs(40)) is 40, not 20");

        wideAggregation.LinearDensity.LimitZones.Should().HaveCount(1);
        LinearDensityLimitZone zone = wideAggregation.LinearDensity.LimitZones[0];
        zone.Name.Should().Be("request_linear_density");
        zone.MaxLinearDensity.Should().BeApproximately(500.0, 1e-9);
        zone.FrontArm.Should().BeApproximately(400.0, 1e-9, "the minimum front arm over all positions");
        zone.BackArm.Should().BeApproximately(900.0, 1e-9, "the maximum back arm over all positions");
        zone.Locations.Should().BeEquivalentTo(new[] { DeckLocation.Main });
        wideAggregation.LinearDensity.LimitLines.Should().NotBeEmpty("the built lines feed the linear density model");

        wideAggregation.SurfaceDensity.LimitZones.Should().HaveCount(1);
        wideAggregation.SurfaceDensity.LimitZones[0].MaxSurfaceDensity.Should().BeApproximately(1000.0, 1e-9);
    }
}
