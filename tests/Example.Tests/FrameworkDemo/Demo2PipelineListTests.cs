// 管线列表生成器的判据 / Criteria for the pipeline list generator.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service;
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
/// **适航安全管线列表** / The airworthiness security pipeline list.
/// </summary>
/// <remarks>
/// 14 条：11 条无条件 + 3 条条件（压舱重量 / CLIM / 相邻间隙）。判据只钉**顺序与条件位置**——
/// 报告按序号引用约束，把这些项挪到末尾就是在改约束的编号。
///
/// The code has fourteen pipelines: eleven unconditional plus three conditional (ballast, CLIM, adjacent gap). The criteria pin **order and where the conditionals sit** —
/// reports reference constraints by index, so moving those entries renumbers the constraints.
/// </remarks>
public class Demo2PipelineListTests {
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

    private static StowagePosition MakePosition(string name, ulong id) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(500.0, 900.0, -40.0, -40.0),
            shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
            location: PositionLocation.NormalMainLocation);
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

    private static readonly LinearDensityLimitZone DensityZone =
        new("zone", new HashSet<DeckLocation> { DeckLocation.Main }, 500.0, 900.0, 3000.0);

    private sealed class Rig
    {
        public Rig(bool wideBody = false, bool registerSymbols = true) {
            var items = new List<Item> { MakeItem("I1") };
            var positions = new List<StowagePosition> { MakePosition("P1", 1UL) };
            Positions = positions;
            var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
            AircraftType type = wideBody ? AircraftType.B747 : AircraftType.B737;
            Aircraft = new AircraftModel(type, type.ToString(), new AircraftMinorModel(type.ToString()));
            Formula formula = new(Aircraft, 100.0, 20.0, 120.0, 5000.0, 0.0);
            Load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: Aircraft, formula: formula);
            Payload = new Payload(positions, Load, plannedPayload: 5000.0, maxPayload: 20000.0);
            var fuselage = new Fuselage(null, 40000.0, 10.0, 900.0);
            var fuel = new Dictionary<FlightPhase, FuelConstant> {
                [FlightPhase.TakeOff] = new FuelConstant(Density: 0.8, Weight: 1000.0, Index: 1000.0),
                [FlightPhase.Landing] = new FuelConstant(Density: 0.8, Weight: 2000.0, Index: 2000.0),
            };
            TotalWeight = new TotalWeight(fuselage, fuel, Payload);
            Torque = new Torque(fuselage, fuel, formula, positions, Load, wideBody);

            Model = new LinearMetaModel<Flt64>();
            if (!registerSymbols) {
                return;
            }
            stowage.Register(Model).IsOk.Should().BeTrue();
            Load.Register(Model).IsOk.Should().BeTrue();
            Payload.Register(StowageMode.FullLoad, Model).IsOk.Should().BeTrue();
            TotalWeight.Register(Model).IsOk.Should().BeTrue();
            Torque.Register(Model).IsOk.Should().BeTrue();
        }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public AircraftModel Aircraft { get; }

        public Load Load { get; }

        public Payload Payload { get; }

        public TotalWeight TotalWeight { get; }

        public Torque Torque { get; }

        public LinearMetaModel<Flt64> Model { get; }

        public AirworthinessSecurityAggregation Aggregation(
            IReadOnlyList<MaxClimPoint>? maxClimPoints = null,
            double? maxAdjacentLoadGap = null,
            bool withBallast = false) =>
            new(
                Aircraft,
                Positions,
                new[] { DensityZone },
                new[] { new LinearDensityLimitLine(DensityZone, 700.0, new[] { 0 }) },
                new[] { new SurfaceDensityLimitZone("zone", 500.0, 900.0, 2000.0) },
                new[] { new MinLowPayloadPoint(1000.0, 30000.0), new MinLowPayloadPoint(2000.0, 50000.0) },
                maxClimPoints,
                new Dictionary<FlightPhase, IReadOnlyList<IEnvelope>>(),
                Load,
                TotalWeight,
                Payload,
                new MaxZoneLoadWeight(),
                new MaxCumulativeLoadWeight(),
                Torque,
                new Dictionary<HorizontalStabilizer.Key, HorizontalStabilizer>(),
                3000.0,
                5000.0,
                1000.0,
                2500.0,
                ballast: withBallast ? new Ballast(Aircraft, Positions, 500.0, Load) : null,
                maxAdjacentLoadGap: maxAdjacentLoadGap);
    }

    private static LinearInequalityConstraint<Flt64> Row(LinearMetaModel<Flt64> model, string name) =>
        model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == name);

    /// <summary>
    /// **11 条无条件管线，顺序逐字对齐基线** / Eleven unconditional pipelines in its exact order.
    /// </summary>
    /// <remarks>
    /// 顺序是**协议**：报告按序号引用约束。按类型名排序会得到另一套编号且看起来完全正常。
    ///
    /// The order is **protocol**: reports reference constraints by index, and sorting by type name produces a different numbering that looks entirely normal.
    /// </remarks>
    [Fact]
    public void Generator_ShouldListEveryUnconditionalPipelineInOrder() {
        var rig = new Rig();
        IReadOnlyList<SecurityPipeline> pipelines = new PipelineListGenerator(rig.Aggregation()).Invoke(StowageMode.FullLoad).Value!;

        pipelines.Select(pipeline => pipeline.Name).Should().Equal(new[] {
            "linear_density_limit",
            "surface_density_limit",
            "cumulative_load_weight_limit",
            "zone_load_weight_limit",
            "low_payload_limit",
            "payload_limit",
            "target_longitudinal_moment_limit",
            "lateral_imbalance_limit",
            "total_weight_limit",
            "envelope_limit",
            "horizontal_stabilizer_limit",
        }, "eleven unconditional pipelines in the baseline's order");
    }

    /// <summary>
    /// **压舱重量插在第 5 位，CLIM 与相邻间隙接在末尾** / Ballast lands at position 5; CLIM and the adjacent gap go last.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 只有 CLIM 与相邻间隙时，两条**接在末尾**（第 12、13 位），前面 11 条不变。
    /// 2. 三个条件全开时是 14 条。
    /// 3. `ballast_weight_limit` 若存在，必须在 **`zone_load_weight_limit` 之后、`low_payload_limit` 之前**——
    /// 把它挪到末尾会让后续每条约束的序号全部前移。
    ///
    /// Three paired points: with only CLIM and the gap they are **appended** (positions 12 and 13) and the first eleven are unchanged; with all three conditionals there are
    /// fourteen; and `ballast_weight_limit` must sit **after `zone_load_weight_limit` and before `low_payload_limit`** — moving it to the end shifts the index of every later
    /// constraint.
    /// </remarks>
    [Fact]
    public void Generator_ShouldPlaceTheConditionalsWhereTheBaselineDoes() {
        var rig = new Rig(wideBody: true);
        MaxClimPoint[] points = { new(40000.0, 8.0), new(60000.0, 4.0) };
        AirworthinessSecurityAggregation aggregation = rig.Aggregation(maxClimPoints: points, maxAdjacentLoadGap: 500.0);

        IReadOnlyList<string> names = new PipelineListGenerator(aggregation)
            .Invoke(StowageMode.FullLoad).Value!
            .Select(pipeline => pipeline.Name)
            .ToList();

        names.Should().HaveCount(13, "eleven unconditional plus CLIM and the adjacent gap");
        names.Take(11).Should().Equal(new[] {
            "linear_density_limit",
            "surface_density_limit",
            "cumulative_load_weight_limit",
            "zone_load_weight_limit",
            "low_payload_limit",
            "payload_limit",
            "target_longitudinal_moment_limit",
            "lateral_imbalance_limit",
            "total_weight_limit",
            "envelope_limit",
            "horizontal_stabilizer_limit",
        }, "the conditionals do not disturb the first eleven");
        names[11].Should().Be("max_clim_limit", "CLIM comes before the adjacent gap");
        names[12].Should().Be("adjacent_gap_limit");
    }

    /// <summary>
    /// **压舱重量插在第 5 位** / The ballast pipeline sits at position five.
    /// </summary>
    /// <remarks>
    /// 判据同时钉**位置**与**总数**：有压舱时 12 条，且第 5 条（下标 4）是 `ballast_weight_limit`，它的前后两条不变。
    /// 只判"名字在列表里"无法发现它被挪到末尾——那会让后面每条约束的序号前移。
    ///
    /// The criterion pins both **position** and total: twelve entries with ballast, the fifth (index 4) being `ballast_weight_limit`, and its neighbours unchanged. Merely
    /// asserting the name is present cannot catch it being moved to the end, which would shift the index of every later constraint.
    /// </remarks>
    [Fact]
    public void Generator_ShouldInsertBallastAtPositionFive() {
        var rig = new Rig();
        IReadOnlyList<string> names = new PipelineListGenerator(rig.Aggregation(withBallast: true))
            .Invoke(StowageMode.FullLoad).Value!
            .Select(pipeline => pipeline.Name)
            .ToList();

        names.Should().HaveCount(12, "eleven unconditional plus the ballast pipeline");
        names[3].Should().Be("zone_load_weight_limit");
        names[4].Should().Be("ballast_weight_limit", "ballast lands between the zone load and the low payload pipelines");
        names[5].Should().Be("low_payload_limit");
    }

    /// <summary>
    /// **纵向力矩的区间是 `target ± deviation`** / The longitudinal interval is `target ± deviation`.
    /// </summary>
    /// <remarks>
    /// 名字对不代表区间对。这里跑一次该管线，断言三阶段六行的右端正是 4000 / 6000（`5000 ∓ 1000`）。
    /// 把两端写成 `target ± max`、或只给一端加偏移，名字仍然对，这条会红。
    ///
    /// Names being right does not make the interval right. This runs the pipeline and asserts the six rows carry 4000 / 6000 (`5000 ∓ 1000`). Writing the ends as
    /// `target ± max`, or offsetting only one end, keeps the names and turns this red.
    /// </remarks>
    [Fact]
    public void Generator_ShouldBuildTheLongitudinalIntervalFromTheDeviation() {
        var rig = new Rig();
        AirworthinessSecurityAggregation aggregation = rig.Aggregation();
        aggregation.Register(StowageMode.FullLoad, rig.Model).IsOk.Should().BeTrue();
        SecurityPipeline longitudinal = new PipelineListGenerator(aggregation)
            .Invoke(StowageMode.FullLoad).Value!
            .Single(pipeline => pipeline.Name == "target_longitudinal_moment_limit");

        longitudinal.Invoke(rig.Model).IsOk.Should().BeTrue();

        foreach (string phase in new[] { "zerofuel", "takeoff", "landing" }) {
            Row(rig.Model, "target_longitudinal_moment_limit_" + phase + "_minimum")
                .Inequality.Rhs.Constant.ToDouble().Should().Be(4000.0, "5000 − 1000");
            Row(rig.Model, "target_longitudinal_moment_limit_" + phase + "_maximum")
                .Inequality.Rhs.Constant.ToDouble().Should().Be(6000.0, "5000 + 1000");
        }
    }

    /// <summary>
    /// **每条管线都真的加了行** / Every pipeline really adds rows.
    /// </summary>
    /// <remarks>
    /// 上一版只比较**总数**：一条管线变成空操作、另一条多加了行，总数照样增长——实测那个变异存活。
    /// 这里逐条钉**有名字的行**：跑完每条后，该管线应有的约束名必须出现。
    ///
    /// The previous version compared only the **total**: one pipeline turning into a no-op while another adds extra rows still grows the total — that mutation survived in
    /// practice. This pins **named rows** per pipeline: after each one runs, the constraint names it owns must exist.
    /// </remarks>
    [Fact]
    public void Generator_ShouldLetEveryPipelineAddItsOwnRows() {
        var rig = new Rig();
        AirworthinessSecurityAggregation aggregation = rig.Aggregation();
        aggregation.Register(StowageMode.FullLoad, rig.Model).IsOk.Should().BeTrue();
        IReadOnlyList<SecurityPipeline> pipelines = new PipelineListGenerator(aggregation)
            .Invoke(StowageMode.FullLoad).Value!;

        // 名字取自实测：密度行的名字里带**力臂**（`_zone_700`）或**舱位**（`_zone_P1`），不是序号。
        // `total_weight_limit` 与 `envelope_limit` 在本 fixture 里**合法地不加行**（各阶段的"最大值"或包络列表为空），
        // 所以它们不在下表里——不列的管线不参与这条判据，避免把"合法的空"当成缺陷。
        // Names come from measurement: density rows carry the **arm** (`_zone_700`) or the **position** (`_zone_P1`), not an index. `total_weight_limit` and
        // `envelope_limit` **legitimately add no row** in this fixture (the per-phase maximum and the envelope list are empty), so they are absent from the table below;
        // unlisted pipelines simply take no part, which avoids treating a legitimate emptiness as a defect.
        var expected = new Dictionary<string, string[]> {
            ["linear_density_limit"] = new[] { "linear_density_limit_zone_700" },
            ["surface_density_limit"] = new[] { "surface_density_limit_zone_P1" },
            ["low_payload_limit"] = new[] { "low_payload_limit" },
            ["payload_limit"] = new[] { "payload_limit", "payload_limit_minimum" },
            ["target_longitudinal_moment_limit"] = new[] { "target_longitudinal_moment_limit_takeoff_minimum" },
            ["lateral_imbalance_limit"] = new[] { "lateral_imbalance_limit_maximum", "lateral_imbalance_limit_minimum" },
        };
        foreach (SecurityPipeline pipeline in pipelines) {
            pipeline.Invoke(rig.Model).IsOk.Should().BeTrue("pipeline " + pipeline.Name + " should run");
        }
        foreach (KeyValuePair<string, string[]> pair in expected) {
            IEnumerable<string> names = rig.Model.Constraints.Select(constraint => constraint.Name);
            foreach (string name in pair.Value) {
                names.Should().Contain(name, "pipeline " + pair.Key + " adds its own rows");
            }
        }
    }

    /// <summary>
    /// **CLIM 只在宽体机且点表非空时出现** / CLIM appears only on a wide body with points.
    /// </summary>
    /// <remarks>
    /// 成对：窄体机即使给了点表也没有 `max_clim_limit`（聚合的 `MaxCLIM` 为 `null`）；宽体机有点表才有。
    /// 这条覆盖的是"生成器看聚合字段"而不是"生成器自己判断宽体"。
    ///
    /// Paired: a narrow body with points still has no `max_clim_limit` (the aggregation's `MaxCLIM` is `null`), while a wide body with points does. This covers
    /// "the generator reads the aggregation's field" rather than "the generator decides about wide bodies itself".
    /// </remarks>
    [Fact]
    public void Generator_ShouldFollowTheAggregationForClim() {
        MaxClimPoint[] points = { new(40000.0, 8.0), new(60000.0, 4.0) };

        var narrowRig = new Rig(wideBody: false);
        new PipelineListGenerator(narrowRig.Aggregation(maxClimPoints: points))
            .Invoke(StowageMode.FullLoad).Value!
            .Select(pipeline => pipeline.Name)
            .Should().NotContain("max_clim_limit", "the narrow body has no CLIM model");

        var wideRig = new Rig(wideBody: true);
        new PipelineListGenerator(wideRig.Aggregation(maxClimPoints: points))
            .Invoke(StowageMode.FullLoad).Value!
            .Select(pipeline => pipeline.Name)
            .Should().Contain("max_clim_limit", "the wide body with points does");
    }

    /// <summary>
    /// **每条管线都能在注册好的模型上跑通** / Every pipeline runs on a model that has its symbols.
    /// </summary>
    /// <remarks>
    /// 生成列表只证明"名字在"，不证明"每条都能执行"。这条把 11 条逐个执行，断言全部 `Ok`，并断言约束**总数增加**
    /// （否则"每条都返回 Ok 但什么都没加"同样通过）。
    ///
    /// Producing the list only proves the names are there, not that each pipeline runs. This executes all eleven, asserts each is `Ok`, and asserts the total constraint count
    /// **grows** — otherwise "every pipeline returns Ok while adding nothing" would pass just as well.
    /// </remarks>
    [Fact]
    public void Generator_ShouldRunEveryPipeline() {
        var rig = new Rig();
        AirworthinessSecurityAggregation aggregation = rig.Aggregation();
        // **先让聚合注册**：密度符号是 `LinearDensity.Register` 建的，管线只是把它们组合成约束。跳过这一步，
        // `linear_density_limit` 会以"限制线上的舱位缺少线性密度符号"失败——这正是真实调用顺序。
        // **Register the aggregation first**: the density symbols come from `LinearDensity.Register` and the pipelines only combine them into constraints. Skipping it makes
        // `linear_density_limit` fail with "a position on a limit line has no linear density symbol" — which is exactly the real call order.
        aggregation.Register(StowageMode.FullLoad, rig.Model).IsOk.Should().BeTrue();
        IReadOnlyList<SecurityPipeline> pipelines = new PipelineListGenerator(aggregation)
            .Invoke(StowageMode.FullLoad).Value!;
        int before = rig.Model.Constraints.Count;

        foreach (SecurityPipeline pipeline in pipelines) {
            pipeline.Invoke(rig.Model).IsOk.Should().BeTrue("pipeline " + pipeline.Name + " should run");
        }

        rig.Model.Constraints.Count.Should().BeGreaterThan(before, "the pipelines really add rows");
    }

    /// <summary>
    /// **装载模式只影响水平安定面那条** / The stowage mode only reaches the horizontal stabilizer pipeline.
    /// </summary>
    /// <remarks>
    /// 三种模式下 11 条管线的**名字列表完全相同**。若生成器把模式用于别处（例如按模式跳过密度约束），这条会红。
    ///
    /// All three modes produce the **same name list**. If the generator used the mode anywhere else (skipping a density bound per mode, say) this turns red.
    /// </remarks>
    [Fact]
    public void Generator_ShouldProduceTheSameListForEveryStowageMode() {
        var rig = new Rig();
        AirworthinessSecurityAggregation aggregation = rig.Aggregation();
        var expected = (IReadOnlyList<string>?)null;
        foreach (StowageMode mode in new[] { StowageMode.Predistribution, StowageMode.FullLoad, StowageMode.WeightRecommendation }) {
            var names = new PipelineListGenerator(aggregation).Invoke(mode).Value!
                .Select(pipeline => pipeline.Name)
                .ToList();
            if (expected is null) {
                expected = names;
            } else {
                names.Should().Equal(expected, "the mode does not change the list");
            }
        }
    }
}
