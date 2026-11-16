// 适航安全上下文的判据 / Criteria for the airworthiness security context.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
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

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **适航安全上下文** / The airworthiness security context.
/// </summary>
/// <remarks>
/// 上下文的 `Register` 是**两步**：聚合注册（只建符号）→ 14 条管线（才成行）。判据的重点在第二步——
/// 只做第一步会得到一个"注册成功、一行约束都没有"的模型，而这在日志里完全看不出来。
///
/// The context's `Register` is **two steps**: the aggregation's registration (symbols only) then the fourteen pipelines (which become rows). The criteria focus on the second
/// step — doing only the first yields a model that registered "successfully" with not one constraint row, and nothing in the logs shows it.
/// </remarks>
public class Demo2SecurityContextTests {
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

    /// <summary>
    /// **从三层上游真的建出聚合** / The aggregation is really built from the three upstream contexts.
    /// </summary>
    /// <remarks>
    /// 五点成对：飞机 → 配载 → MAC 逐层初始化都成功 / 适航上下文在 `Init` 之前 `Aggregation` 是 `null` / `Init` 之后非空 /
    /// 再看一次得到的是**同一个实例** / 三次上游没变。 记下的缺口就是这条正路——没有它，`Init` 可以把"看起来成功"演给所有人看：
    /// **返回 `Ok`、`Aggregation` 却还是 `null`，而之后每一次注册都会以"尚未初始化"失败**，调用方会一路怀疑上游。
    ///
    /// Five paired points: aircraft → stowage → MAC each initialise successfully; the airworthiness context's `Aggregation` is `null` before `Init`; it is non-null after; a second
    /// call yields **the same instance**; and the three upstreams are untouched. This is the path recorded as missing: without it, `Init` can perform "looks successful" for
    /// everyone — **returning `Ok` while `Aggregation` stays `null`, so every later registration fails with "not initialised"** and the caller blames the upstreams.
    /// </remarks>
    [Fact]
    public void Init_ShouldBuildTheAggregationFromTheUpstreamContexts() {
        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();
        // **示例请求自身也必须过得了飞机初始化器**： 给 `PositionInput` 补了空间归属、装载顺序与矩形边界，
        // 示例请求一度没跟上，"演示"连第一步都走不过去。这三条钉的就是那份请求的形状。
        // **The sample request itself must get past the aircraft initialiser too**: added space ownership, loading order and rectangle bounds to `PositionInput` and the
        // sample request fell behind, so the "demo" could not clear its first step. These three pin that request's shape.
        input.Spaces.Should().NotBeNullOrEmpty("positions name a space, so the space table must exist");
        input.Positions.Should().OnlyContain(position => position.Geometry != null);
        input.Positions.Select(position => position.LoadingOrder).Should().OnlyHaveUniqueItems(
            "the initialiser rejects duplicate loading orders");

        var aircraftContext = new AircraftContext();
        aircraftContext.Init(input).IsOk.Should().BeTrue();
        var stowageContext = new StowageContext();
        stowageContext.Init(aircraftContext, input).IsOk.Should().BeTrue();
        var macContext = new MacContext();
        macContext.Init(aircraftContext, stowageContext, input).IsOk.Should().BeTrue();

        var context = new AirworthinessSecurityContext();
        context.Aggregation.Should().BeNull("nothing is built before Init");

        Result<Success, ErrorCode, Error<ErrorCode>> result = context.Init(aircraftContext, stowageContext, macContext, input);
        result.IsOk.Should().BeTrue(result.ToString());
        context.Aggregation.Should().NotBeNull("Init must hand back a real aggregation, not just Ok");

        AirworthinessSecurityAggregation first = context.Aggregation!;
        context.Init(aircraftContext, stowageContext, macContext, input).IsOk.Should().BeTrue();
        context.Aggregation.Should().BeSameAs(first, "a second Init must not rebuild it");
    }

    /// <summary>构造一份最小请求 DTO / Build a minimal request DTO.</summary>
    /// <remarks>
    /// 这两个判据都**走不到初始化器**：一个被短路挡住，一个被上游守卫挡住，所以空的货物与舱位表不会被校验。
    /// 换成"能真的建出聚合"的 DTO 是另一件事，见计划文档里记的缺口。
    ///
    /// Neither criterion **reaches the initialiser** — one is stopped by the short-circuit, the other by the upstream guard — so empty cargo and position lists are never
    /// validated. A DTO that really builds an aggregation is a separate job, recorded as a gap in the plan document.
    /// </remarks>
    /// <returns>请求 DTO / The request DTO.</returns>
    private static RequestDTO DemoRequest() =>
        new(
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

    private sealed class Rig
    {
        public Rig(IReadOnlyList<MaxClimPoint>? maxClimPoints = null, bool wideBody = false) {
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
            var fuel = new Dictionary<FlightPhase, FuelConstant>();
            TotalWeight = new TotalWeight(fuselage, fuel, Payload);
            // 力矩按**窄体**建（没有 `clim` 符号），而飞机型号可以是宽体——用于制造"聚合能建 `MaxCLIM`、管线却失败"的场景。
            // The torque is built as a **narrow body** (no `clim` symbol) while the aircraft type may be wide, which is how "the aggregation can build `MaxCLIM` yet the pipeline
            // fails" is produced.
            Torque = new Torque(fuselage, fuel, formula, positions, Load, wideBody: false);

            Model = new LinearMetaModel<Flt64>();
            stowage.Register(Model).IsOk.Should().BeTrue();
            Load.Register(Model).IsOk.Should().BeTrue();
            Payload.Register(StowageMode.FullLoad, Model).IsOk.Should().BeTrue();
            TotalWeight.Register(Model).IsOk.Should().BeTrue();
            Torque.Register(Model).IsOk.Should().BeTrue();

            Aggregation = new AirworthinessSecurityAggregation(
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
                2500.0);
        }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public AircraftModel Aircraft { get; }

        public Load Load { get; }

        public Payload Payload { get; }

        public TotalWeight TotalWeight { get; }

        public Torque Torque { get; }

        public LinearMetaModel<Flt64> Model { get; }

        public AirworthinessSecurityAggregation Aggregation { get; }
    }

    /// <summary>
    /// **注册是两步：聚合的符号 + 管线的行** / Registration is two steps: the aggregation's symbols and the pipelines' rows.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 聚合的符号被建出来（`min_low_payload`）。
    /// 2. **管线独有的行**也被加进去（`payload_limit_minimum`）——只做第一步时这条不存在。
    /// 3. 约束数确实增长。
    ///
    /// 第 2 点是这条判据的全部价值：只断言"注册成功"或"约束数 > 0"都无法区分"只跑了第一步"。
    ///
    /// Three paired points: the aggregation's symbols appear (`min_low_payload`), the **rows only the pipelines create** appear (`payload_limit_minimum` — absent if only step one
    /// ran), and the constraint count grows. Point two is the whole value: asserting "registration succeeded" or "count > 0" cannot tell that only step one ran.
    /// </remarks>
    [Fact]
    public void Context_ShouldRunBothTheAggregationAndThePipelines() {
        var rig = new Rig();
        var context = new AirworthinessSecurityContext(rig.Aggregation);
        int before = rig.Model.Constraints.Count;

        context.Register(StowageMode.FullLoad, rig.Model).IsOk.Should().BeTrue();

        rig.Aggregation.MinLowPayload.MinLowPayloadSymbol.Should().NotBeNull("the aggregation registered its symbols");
        rig.Model.Constraints.Select(constraint => constraint.Name)
            .Should().Contain("payload_limit_minimum", "the pipelines ran as well");
        rig.Model.Constraints.Count.Should().BeGreaterThan(before);
    }

    /// <summary>
    /// **未初始化时明确失败** / An uninitialised context fails explicitly.
    /// </summary>
    /// <remarks>
    /// 成对：无参构造的上下文注册失败并说明"尚未初始化"；同一个模型在已初始化的上下文上成功。
    ///
    /// Paired: the parameterless context fails with "not initialised", while the same model succeeds on an initialised context.
    /// </remarks>
    [Fact]
    public void Context_ShouldFailWhenItHasNoAggregation() {
        var rig = new Rig();
        var context = new AirworthinessSecurityContext();
        context.Aggregation.Should().BeNull();

        Result<Success, ErrorCode, Error<ErrorCode>> result = context.Register(StowageMode.FullLoad, rig.Model);
        result.IsOk.Should().BeFalse();
        (result as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("尚未初始化");

        var okRig = new Rig();
        new AirworthinessSecurityContext(okRig.Aggregation).Register(StowageMode.FullLoad, okRig.Model)
            .IsOk.Should().BeTrue("an initialised context registers");
    }

    /// <summary>
    /// **已初始化过就不再重建：短路在"上游未初始化"之前** / Once initialised it is never rebuilt: the short-circuit precedes the upstream guard.
    /// </summary>
    /// <remarks>
    /// 三点成对：已初始化的上下文配上**三个未初始化的上游**仍然成功 / **实例不变**（`ReferenceEquals`）/ 上游守卫那条错误消息不出现。
    /// 顺序反过来（先查上游）会让这段红——**而那个顺序错误在真实调用里表现为"第二次 `Init` 偶发失败"**，
    /// 因为第二次调用往往不再带齐三个上游上下文。
    ///
    /// Three paired points: an initialised context with **three uninitialised upstreams** still succeeds; **the instance is unchanged** (`ReferenceEquals`); and the upstream guard's
    /// message does not appear. Swapping the order (guard first) turns this red — **and that order bug shows up in real use as "the second `Init` fails intermittently"**, because a
    /// second call typically no longer carries all three upstream contexts.
    /// </remarks>
    [Fact]
    public void Init_ShouldShortCircuitBeforeTheUpstreamGuard() {
        var rig = new Rig();
        var context = new AirworthinessSecurityContext(rig.Aggregation);

        Result<Success, ErrorCode, Error<ErrorCode>> result = context.Init(
            new AircraftContext(), new StowageContext(), new MacContext(), DemoRequest());

        result.IsOk.Should().BeTrue("an initialised context has nothing to rebuild");
        context.Aggregation.Should().BeSameAs(rig.Aggregation, "the aggregation must not be rebuilt");
    }

    /// <summary>
    /// **上游未初始化时明确失败，不把 `null` 放进初始化器** / An uninitialised upstream fails loudly instead of handing `null` to the initialiser.
    /// </summary>
    /// <remarks>
    /// 两点成对：返回失败 / 消息点名"上游聚合未初始化"。让 `null` 穿过去会变成 `NullReferenceException` 或 `ArgumentNullException`，
    /// **而异常类型对调用方毫无用处**：它分不清"忘了先 `Init` 上游"与"适航那套自身坏了"。
    ///
    /// Two paired points: it returns a failure; and the message names the uninitialised upstream. Letting `null` through becomes a `NullReferenceException` or
    /// `ArgumentNullException`, **and the exception type tells a caller nothing**: it cannot distinguish "forgot to `Init` the upstream" from "the airworthiness layer itself broke".
    /// </remarks>
    [Fact]
    public void Init_ShouldFailWhenAnUpstreamIsUninitialised() {
        var context = new AirworthinessSecurityContext();

        Result<Success, ErrorCode, Error<ErrorCode>> result = context.Init(
            new AircraftContext(), new StowageContext(), new MacContext(), DemoRequest());

        result.IsOk.Should().BeFalse();
        (result as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message
            .Should().Contain("上游聚合未初始化");
        context.Aggregation.Should().BeNull("a failed initialisation must not leave a half-built aggregation");
    }

    /// <summary>
    /// **管线失败会向上传播** / A failing pipeline propagates.
    /// </summary>
    /// <remarks>
    /// 造一个"聚合能过、管线会失败"的场景：**宽体机型号 + 非空 CLIM 点表**（聚合因此建出 `MaxCLIM`），
    /// 但**力矩按窄体建**（没有 `clim` 符号），于是 `max_clim_limit` 那一步必然失败。
    ///
    /// 成对——同一个聚合在力矩也按宽体建的模型上成功，在这个模型上失败。把 `if (!invoked.IsOk) return invoked;`
    /// 换成 `_ = pipeline.Invoke(model);`，整条注册会"成功"，而 CLIM 的上下界一行都没有——**这正是 记过的那种静默缺陷**。
    ///
    /// 第一版用的是"缺业载符号"的模型，**失败在聚合那一步**而不是管线那一步，所以那个变异存活。失败点必须落在**管线**里。
    ///
    /// Builds a "the aggregation passes, a pipeline fails" scenario: a **wide-body type with a non-empty CLIM point list** (so the aggregation builds `MaxCLIM`) but a torque
    /// built as a **narrow body** (no `clim` symbol), which makes `max_clim_limit` fail. Paired — the same aggregation succeeds when the torque is wide too. Replacing
    /// `if (!invoked.IsOk) return invoked;` with `_ = pipeline.Invoke(model);` makes the registration "succeed" with no CLIM bound at all: the silent defect recorded. The
    /// first draft used a model missing the payload symbol, which **failed inside the aggregation** rather than inside a pipeline, so the mutation survived; the failure has to
    /// land in the pipeline.
    /// </remarks>
    [Fact]
    public void Context_ShouldPropagateAFailingPipeline() {
        MaxClimPoint[] points = { new(40000.0, 8.0), new(60000.0, 4.0) };
        var rig = new Rig(maxClimPoints: points, wideBody: true);
        rig.Aggregation.MaxCLIM.Should().NotBeNull("the aircraft is wide and the point list is not empty");
        rig.Torque.ClimSymbol.Should().BeNull("the torque was built as a narrow body, so it has no clim symbol");
        var context = new AirworthinessSecurityContext(rig.Aggregation);

        Result<Success, ErrorCode, Error<ErrorCode>> result = context.Register(StowageMode.FullLoad, rig.Model);
        result.IsOk.Should().BeFalse("the CLIM pipeline cannot build its rows");
        (result as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("CLIM");
        rig.Model.Constraints.Select(constraint => constraint.Name).Should().NotContain("max_clim_limit_ub");
    }

    /// <summary>
 /// **三个 Benders 钩子** / The three Benders hooks match the implementation.
    /// </summary>
    /// <remarks>
    /// 四点成对，全部用**约束行数**与**符号**观察：
    ///
    /// 1. `RegisterForBendersMP` 成功且**模型里一行都没加**。
    /// 2. `FlushForBendersSP` 成功且一行都没加。
    /// 3. `RegisterForBendersSP` 加的行与 `Register` 相同。
    /// 4. `RegisterForBendersSP` **忽略 `solution`**——空解也照常注册。
    ///
    /// Four paired points, all observed through row counts and symbols: the master registration adds no row; the flush adds no row; the sub-problem registration matches `Register`;
    /// and it **ignores `solution`**, so an empty solution still registers.
    /// </remarks>
    [Fact]
    public void Context_ShouldKeepTheBendersHooksAsTheyAre() {
        var rig = new Rig();
        var context = new AirworthinessSecurityContext(rig.Aggregation);
        int before = rig.Model.Constraints.Count;
        context.RegisterForBendersMP(rig.Model).IsOk.Should().BeTrue();
        rig.Model.Constraints.Count.Should().Be(before, "the master registration adds nothing");
        context.FlushForBendersSP(rig.Model, new[] { 0.0 }).IsOk.Should().BeTrue();
        rig.Model.Constraints.Count.Should().Be(before, "the flush adds nothing");
        rig.Aggregation.MinLowPayload.MinLowPayloadSymbol.Should().BeNull("no registration ran at all");

        var registerRig = new Rig();
        new AirworthinessSecurityContext(registerRig.Aggregation).Register(StowageMode.FullLoad, registerRig.Model).IsOk.Should().BeTrue();
        int expected = registerRig.Model.Constraints.Count;
        expected.Should().BeGreaterThan(0);

        var subRig = new Rig();
        new AirworthinessSecurityContext(subRig.Aggregation)
            .RegisterForBendersSP(StowageMode.FullLoad, subRig.Model, new double[0]).IsOk.Should().BeTrue();
        subRig.Model.Constraints.Count.Should().Be(expected, "the sub-problem equals Register and ignores the solution");
    }

    /// <summary>
    /// **已有聚合时 `Init` 不重建** / `Init` does not rebuild an existing aggregation.
    /// </summary>
    /// <remarks>
    /// 守卫是 `if (!::aggregation.isInitialized)`。这里用构造时注入的聚合调 `Init`：应当**直接成功**、聚合仍是同一个实例，
    /// 而且**不去碰**传入的三个上下文——若守卫写反（每次都重建），初始化器会明确失败，这条会红。
    ///
    /// guard is `if (!::aggregation.isInitialized)`. Calling `Init` on a context built with an aggregation must **succeed directly**, keep the same instance, and
    /// **not touch** the three contexts — if the guard were reversed (rebuilding every time) the initialiser would fail explicitly and this turns red.
    /// </remarks>
    [Fact]
    public void Context_ShouldNotRebuildAnExistingAggregation() {
        var rig = new Rig();
        var context = new AirworthinessSecurityContext(rig.Aggregation);
        AirworthinessSecurityAggregation original = context.Aggregation!;

        var aircraftContext = new AircraftContext();
        var stowageContext = new StowageContext();
        var macContext = new MacContext();
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            context.Init(aircraftContext, stowageContext, macContext, AircraftLoadingDemo.CreateSampleRequest());

        result.IsOk.Should().BeTrue("an existing aggregation short-circuits the initialiser");
        context.Aggregation.Should().BeSameAs(original);
    }

    /// <summary>
    /// **没有上游聚合时 `Init` 明确失败，绝不返回空聚合** / Without an upstream aggregation `Init` fails explicitly and never returns an empty one.
    /// </summary>
    /// <remarks>
    /// 这条判据在 时断言的是"初始化器尚未实现，所以失败"。 把初始化器实现了，那条断言随之过时——现在失败来自**上游聚合为空**
    /// （`StowageContext` 没有聚合，初始化器拒绝空位置表），而不是"未实现"。
    ///
    /// 成对：失败后 `Aggregation` 仍是 `null`，不会有半个聚合被交出去。
    ///
    /// This criterion asserted that "the initialiser is not implemented, hence failure". implemented the initialiser, making that assertion stale — the failure now comes
    /// from an **empty upstream aggregation** (the `StowageContext` has no aggregation and the initialiser rejects an empty position list), not from "not implemented". Paired: after the
    /// failure `Aggregation` is still `null`, so no half-built aggregation is handed out.
    /// </remarks>
    [Fact]
    public void Context_InitShouldFailExplicitlyWithoutAnUpstreamAggregation() {
        var context = new AirworthinessSecurityContext();
        Result<Success, ErrorCode, Error<ErrorCode>> result = context.Init(
            new AircraftContext(),
            new StowageContext(),
            new MacContext(),
            AircraftLoadingDemo.CreateSampleRequest());

        result.IsOk.Should().BeFalse("the stowage context carries no aggregation");
        (result as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("上游聚合",
            "the guard names the real cause instead of letting a null surface as an ArgumentNullException");
        context.Aggregation.Should().BeNull("no half-built aggregation is handed out");
    }
}
