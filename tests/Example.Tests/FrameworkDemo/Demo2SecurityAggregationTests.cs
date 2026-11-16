// 适航安全聚合的判据 / Criteria for the airworthiness security aggregation.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
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
/// **适航安全聚合** / The airworthiness security aggregation.
/// </summary>
/// <remarks>
/// 这里落的是**组装层**：把 4 个派生模型建起来并按基线顺序注册。三处风险：
///
/// | 处 | 做错了会怎样 |
/// | --- | --- |
/// | `MaxCLIM` 的两个前提 | 只看点表 → 窄体机上建一个用不到的 CLIM；只看宽体 → 空点表让构造器抛异常 |
/// | 注册顺序 | 按序号引用约束的报告会指错行 |
/// | Benders MP 是空操作 | 加上不该有的行，或以为它做了事 |
///
/// lands the **assembly layer**: build four derived models and register them in the baseline's order, with the three risks tabulated above.
/// </remarks>
public class Demo2SecurityAggregationTests {
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

    private static StowagePosition MakePosition(string name, ulong id, PositionLocation location) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(500.0, 900.0, -40.0, -40.0),
            shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
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

    private static readonly LinearDensityLimitZone DensityZone =
        new("zone", new HashSet<DeckLocation> { DeckLocation.Main }, 500.0, 900.0, 3000.0);

    private sealed class Rig
    {
        public Rig(bool wideBody = false, bool registerTotalWeight = true) {
            var items = new List<Item> { MakeItem("I1") };
            var positions = new List<StowagePosition> {
                MakePosition("P1", 1UL, PositionLocation.NormalMainLocation),
            };
            Positions = positions;
            var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
            Stowage = stowage;
            AircraftType type = wideBody ? AircraftType.B747 : AircraftType.B737;
            Aircraft = new AircraftModel(type, type.ToString(), new AircraftMinorModel(type.ToString()));
            var formula = new Formula(Aircraft, 100.0, 20.0, 120.0, 5000.0, 0.0);
            Load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: Aircraft, formula: formula);
            Payload = new Payload(positions, Load, plannedPayload: 5000.0, maxPayload: 20000.0);
            var fuselage = new Fuselage(null, Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);
            var fuel = new Dictionary<FlightPhase, FuelConstant>();
            TotalWeight = new TotalWeight(fuselage, fuel, Payload);
            Torque = new Torque(fuselage, fuel, formula, positions, Load, wideBody: type == AircraftType.B747);

            // 装载符号必须先注册：密度类符号是装载估计的线性组合，缺了它们聚合注册会失败。
            // The load symbols must be registered first: the density symbols are linear combinations of the load estimates, and the aggregation's registration fails without them.
            Model = new LinearMetaModel<Flt64>();
            stowage.Register(Model).IsOk.Should().BeTrue();
            Load.Register(Model).IsOk.Should().BeTrue();
            // `MaxCLIM` 的线性支要读起飞总重估计，`MinLowPayload` 也要读零油重估计，所以总重符号必须先在模型里。
            // `MaxCLIM`'s linear branch reads the take-off total weight estimate and `MinLowPayload` reads the zero-fuel one, so the total weight symbols must be registered too.
            Payload.Register(StowageMode.FullLoad, Model).IsOk.Should().BeTrue();
            if (registerTotalWeight) {
                TotalWeight.Register(Model).IsOk.Should().BeTrue();
                Torque.Register(Model).IsOk.Should().BeTrue();
            }
        }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public Stowage Stowage { get; }

        public AircraftModel Aircraft { get; }

        public Load Load { get; }

        public TotalWeight TotalWeight { get; }

        public LinearMetaModel<Flt64> Model { get; }

        public Payload Payload { get; private set; } = null!;

        public Torque Torque { get; private set; } = null!;

        public AirworthinessSecurityAggregation Aggregation(
            IReadOnlyList<MaxClimPoint>? maxClimPoints = null,
            IReadOnlyDictionary<FlightPhase, IReadOnlyList<IEnvelope>>? envelopes = null,
            double? maxAdjacentLoadGap = null) =>
            new(
                Aircraft,
                Positions,
                new[] { DensityZone },
                new[] { new LinearDensityLimitLine(DensityZone, 700.0, new[] { 0 }) },
                new[] { new SurfaceDensityLimitZone("zone", 500.0, 900.0, 2000.0) },
                new[] { new MinLowPayloadPoint(1000.0, 30000.0), new MinLowPayloadPoint(2000.0, 50000.0) },
                maxClimPoints,
                envelopes ?? new Dictionary<FlightPhase, IReadOnlyList<IEnvelope>>(),
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
                ballast: null,
                maxAdjacentLoadGap: maxAdjacentLoadGap);
    }

    private static MaxClimPoint[] ClimPoints() => new[] {
        new MaxClimPoint(40000.0, 8.0),
        new MaxClimPoint(60000.0, 4.0),
    };

    /// <summary>
    /// **宽体机 + 点表非空才有 `MaxCLIM`** / `MaxCLIM` needs both a wide body and a non-empty point list.
    /// </summary>
    /// <remarks>
    /// 五种组合逐个断言：宽体+有点 → 有；宽体+空表 → 无；宽体+`null` → 无；窄体+有点 → 无；窄体+`null` → 无。
    /// 只测一个组合时，把条件写成"只看点表"或"只看宽体"都能通过。
    ///
    /// Five combinations as tabulated. Testing one combination lets either condition alone pass.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldBuildMaxClimOnlyForWideBodiesWithPoints() {
        new Rig(wideBody: true).Aggregation(maxClimPoints: ClimPoints()).MaxCLIM.Should().NotBeNull("a wide body with points gets a CLIM");
        new Rig(wideBody: true).Aggregation(maxClimPoints: new MaxClimPoint[0]).MaxCLIM.Should().BeNull("an empty list counts as no points");
        new Rig(wideBody: true).Aggregation(maxClimPoints: null).MaxCLIM.Should().BeNull("a wide body without points has no CLIM");
        new Rig(wideBody: false).Aggregation(maxClimPoints: ClimPoints()).MaxCLIM.Should().BeNull("a narrow body never gets a CLIM");
        new Rig(wideBody: false).Aggregation(maxClimPoints: null).MaxCLIM.Should().BeNull();
    }

    /// <summary>
    /// **四个派生模型在构造时就建好** / The four derived models exist right after construction.
    /// </summary>
    /// <remarks>
    /// 与"注册成功"成对：构造完成即可取到 `LinearDensity` / `SurfaceDensity` / `MinLowPayload`，注册成功后才取到各符号。
    /// 若把它们推迟到 `Register`，`PipelineListGenerator` 在注册前就读不到聚合字段。
    ///
    /// Paired with "registration succeeds": the derived models are reachable right after construction, while their symbols only appear after a successful registration.
    /// Deferring them to `Register` would leave `PipelineListGenerator` unable to read the aggregation's fields before registration.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldBuildTheDerivedModelsAtConstruction() {
        var rig = new Rig();
        AirworthinessSecurityAggregation aggregation = rig.Aggregation();
        aggregation.LinearDensity.Should().NotBeNull();
        aggregation.SurfaceDensity.Should().NotBeNull();
        aggregation.MinLowPayload.Should().NotBeNull();
        aggregation.Envelopes.Should().BeEmpty();
        aggregation.Positions.Should().ContainSingle();
        aggregation.LinearDensity.Register(rig.Model).IsOk.Should().BeTrue();
    }

    /// <summary>
    /// **注册成功并把符号放进模型** / Registration succeeds and puts the symbols into the model.
    /// </summary>
    /// <remarks>
    /// 四个符号族都在，宽体机再加 `max_clim`。成对：窄体机注册后**没有** `max_clim`。
    ///
    /// All four symbol families are present, with `max_clim` only on a wide body — paired, so a narrow body must not carry it.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldRegisterEveryDerivedModel() {
        var wideRig = new Rig(wideBody: true);
        AirworthinessSecurityAggregation wide = wideRig.Aggregation(maxClimPoints: ClimPoints());
        wide.Register(StowageMode.FullLoad, wideRig.Model).IsOk.Should().BeTrue();
        wide.LinearDensity.LinearDensitySymbol(0).Should().NotBeNull();
        wide.SurfaceDensity.SurfaceDensitySymbol(0).Should().NotBeNull("the surface density step ran");
        wide.MinLowPayload.MinLowPayloadSymbol.Should().NotBeNull();
        wide.MaxCLIM!.MaxClimSymbol.Should().NotBeNull();
        wide.MaxCLIM.MaxClimSymbol!.Name.Should().Be("max_clim");

        var narrowRig = new Rig(wideBody: false);
        AirworthinessSecurityAggregation narrow = narrowRig.Aggregation(maxClimPoints: ClimPoints());
        narrow.Register(StowageMode.FullLoad, narrowRig.Model).IsOk.Should().BeTrue();
        narrow.LinearDensity.LinearDensitySymbol(0).Should().NotBeNull();
        narrow.MinLowPayload.MinLowPayloadSymbol.Should().NotBeNull();
        narrow.MaxCLIM.Should().BeNull("the narrow body has no CLIM to register");
    }

    /// <summary>
    /// **任一派生模型注册失败都会向上传播** / A failed derived registration propagates.
    /// </summary>
    /// <remarks>
    /// 造一个**装载符号齐备、但缺总重符号**的模型：线性密度与表面密度都能成功，`MinLowPayload` 是第一个失败的步骤。
    /// 成对——同一个聚合在 `Rig.Model`（总重也注册了）上成功，在这个模型上失败，且失败时 `MinLowPayloadSymbol` 仍是 `null`。
    ///
    /// 若把 `if (!low.IsOk) return low;` 换成 `_ = low;`，这个模型也会"成功"，而后续的包络注册照跑——正是"吞掉错误"的形状。
    ///
    /// Build a model with the **load symbols present but the total weight symbols missing**: linear and surface density both succeed and `MinLowPayload` is the first step to
    /// fail. Paired — the same aggregation succeeds on `Rig.Model` (which also has the total weight) and fails here, leaving `MinLowPayloadSymbol` `null`. Replacing
    /// `if (!low.IsOk) return low;` with `_ = low;` makes this model "succeed" and the envelope step run on: the shape of a swallowed error.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldPropagateAFailedDerivedRegistration() {
        var rig = new Rig(registerTotalWeight: false);
        AirworthinessSecurityAggregation aggregation = rig.Aggregation();

        aggregation.Register(StowageMode.FullLoad, rig.Model).IsOk.Should().BeFalse("the total weight symbols are missing");
        aggregation.MinLowPayload.MinLowPayloadSymbol.Should().BeNull("the failure stops the chain");

        var okRig = new Rig();
        AirworthinessSecurityAggregation ok = okRig.Aggregation();
        ok.Register(StowageMode.FullLoad, okRig.Model).IsOk.Should().BeTrue("the same aggregation succeeds with the total weight present");
        ok.MinLowPayload.MinLowPayloadSymbol.Should().NotBeNull();
    }

    /// <summary>
    /// **第一个失败点也会传播：密度失败时后面一步都不跑** / The first failure propagates too: when density fails, no later step runs.
    /// </summary>
    /// <remarks>
    /// 上一条判据的失败点在 `MinLowPayload`（密度能过），所以"吞掉**密度**失败"那个变异存活——同一个缺陷换了位置。
    /// 这条让密度成为**第一个**失败点：模型里只有舱位变量，没有装载估计符号，密度注册必然失败。
    ///
    /// **不能复用 `Rig` 的 `Stowage`**：`Stowage.Register` 对同一个对象只允许注册一次（"上下文已注册"），所以这里现造一套。
    ///
    /// The previous criterion fails at `MinLowPayload` (density passes), which is why "swallow the **density** failure" survives — the same defect in another slot. This one makes
    /// density the **first** failure: the model holds only the position variables and no load estimates, so the density registration must fail. **`Rig`'s `Stowage` cannot be
    /// reused**: `Stowage.Register` allows one registration per object ("context already registered"), so a fresh one is built here.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldStopAtTheDensityFailure() {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, PositionLocation.NormalMainLocation) };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        AircraftType type = AircraftType.B737;
        var aircraft = new AircraftModel(type, type.ToString(), new AircraftMinorModel(type.ToString()));
        var formula = new Formula(aircraft, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraft, formula: formula);
        var payload = new Payload(positions, load, plannedPayload: 5000.0, maxPayload: 20000.0);
        var totalWeight = new TotalWeight(new Fuselage(null, 40000.0, 10.0, 900.0), new Dictionary<FlightPhase, FuelConstant>(), payload);
        var fuel = new Dictionary<FlightPhase, FuelConstant>();
        var fuselage = new Fuselage(null, 40000.0, 10.0, 900.0);
        var aggregation = new AirworthinessSecurityAggregation(
            aircraft,
            positions,
            new[] { DensityZone },
            new[] { new LinearDensityLimitLine(DensityZone, 700.0, new[] { 0 }) },
            new[] { new SurfaceDensityLimitZone("zone", 500.0, 900.0, 2000.0) },
            new[] { new MinLowPayloadPoint(1000.0, 30000.0), new MinLowPayloadPoint(2000.0, 50000.0) },
            null,
            new Dictionary<FlightPhase, IReadOnlyList<IEnvelope>>(),
            load,
            totalWeight,
            payload,
            new MaxZoneLoadWeight(),
            new MaxCumulativeLoadWeight(),
            new Torque(fuselage, fuel, formula, positions, load, wideBody: false),
            new Dictionary<HorizontalStabilizer.Key, HorizontalStabilizer>(),
            3000.0,
            5000.0,
            1000.0,
            2500.0);

        using var positionOnlyModel = new LinearMetaModel<Flt64>();
        stowage.Register(positionOnlyModel).IsOk.Should().BeTrue("the position variables are in");

        aggregation.Register(StowageMode.FullLoad, positionOnlyModel).IsOk.Should().BeFalse("the density symbols cannot be built without the load estimates");
        aggregation.LinearDensity.LinearDensitySymbol(0).Should().BeNull();

        // **记录一处不可观测**：`if (!density.IsOk) return density;` 换成 `_ = density;` 之后，表面密度与最低业载**各自也会失败**
        // （它们同样缺装载/总重符号），最终 `IsOk` 仍是 `false`，且失败时都不清空自己的符号——所以两种写法下三个符号都是 `null`，
        // 任何断言都区分不出"提前返回"与"继续跑但逐个失败"。实测三个 `_ = x;` 变异全部存活。
        //
        // 要让它可观测，得让某个后续步骤**不依赖**前面失败的那个输入（例如给一个只有表面密度能过的模型），而那与本类
        // "全部派生模型共享同一组装载符号"的结构冲突。因此保留基线写法，并在此记录它没有被判据覆盖。
        //
        // **One unobservable recorded**: replacing `if (!density.IsOk) return density;` with `_ = density;` lets the surface density and low payload **fail in turn** (they need
        // the same load and total weight symbols), so `IsOk` stays `false` and neither clears its symbol on failure — all three symbols are `null` either way and no assertion can
        // separate "returned early" from "kept going and failed each step". All three `_ = x;` mutations survive (measured). Making it observable would need a later step that does
        // **not** depend on the input the earlier one failed on, which conflicts with this class's structure where every derived model shares the same load symbols. The baseline's
        // form is kept and its lack of coverage recorded here.
    }

    /// <summary>
    /// **Benders 主问题是空操作，子问题等价于 `Register`** / Benders master is a no-op and the sub-problem equals `Register`.
    /// </summary>
    /// <remarks>
    /// 四点成对。**这里不能比 `Constraints.Count`**：适航安全聚合只注册符号，一行约束都不加，所以那个数恒为 0，
    /// 拿它当判据会让"跳过注册"和"偷偷注册"都通过——最初就是这样，三条变异全部存活。可观测的是**符号**：
    /// 注册成功才把 `LinearDensitySymbol(0)` 填上。
    ///
    /// 1. `RegisterForBendersMP` 成功且**符号仍是 `null`**（空操作）。
    /// 2. `RegisterForBendersSP` 成功且符号被填上（等价于 `Register`）。
    /// 3. 给不同的解，结果一样（`solution` 被忽略）。
    /// 4. **空解也照常注册**——`solution` 不能被当作"要不要干活"的开关。
    ///
    /// Four paired points. **`Constraints.Count` cannot be compared here**: the airworthiness security aggregation registers symbols only and adds not one constraint row, so that
    /// count is always 0 and using it as a criterion lets both "skip the registration" and "sneak one in" pass — which is exactly what happened in the first draft, where all three
    /// mutations survived. What is observable is the **symbol**: `LinearDensitySymbol(0)` is filled only by a successful registration. The four points are tabulated above.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldKeepTheBendersHooksAsTheyAre() {
        var rig = new Rig();
        AirworthinessSecurityAggregation masterAggregation = rig.Aggregation();
        masterAggregation.RegisterForBendersMP(rig.Model).IsOk.Should().BeTrue();
        masterAggregation.LinearDensity.LinearDensitySymbol(0).Should().BeNull("the master registration is a no-op");
        masterAggregation.MinLowPayload.MinLowPayloadSymbol.Should().BeNull("nothing at all was registered");

        var firstRig = new Rig();
        AirworthinessSecurityAggregation firstAggregation = firstRig.Aggregation();
        firstAggregation.RegisterForBendersSP(StowageMode.FullLoad, firstRig.Model, new[] { 0.0 }).IsOk.Should().BeTrue();
        firstAggregation.LinearDensity.LinearDensitySymbol(0).Should().NotBeNull("the sub-problem re-adds everything");
        firstAggregation.MinLowPayload.MinLowPayloadSymbol.Should().NotBeNull();

        var secondRig = new Rig();
        AirworthinessSecurityAggregation secondAggregation = secondRig.Aggregation();
        secondAggregation.RegisterForBendersSP(StowageMode.FullLoad, secondRig.Model, new[] { 1.0, 2.0, 3.0 }).IsOk.Should().BeTrue();
        secondAggregation.LinearDensity.LinearDensitySymbol(0).Should().NotBeNull("a different solution changes nothing");

        var emptyRig = new Rig();
        AirworthinessSecurityAggregation emptyAggregation = emptyRig.Aggregation();
        emptyAggregation.RegisterForBendersSP(StowageMode.FullLoad, emptyRig.Model, new double[0]).IsOk.Should().BeTrue();
        emptyAggregation.LinearDensity.LinearDensitySymbol(0).Should().NotBeNull("an empty solution is ignored too, not a gate");
    }

    /// <summary>
    /// **装载模式不影响聚合注册的行** / The stowage mode does not change what the aggregation registers.
    /// </summary>
    /// <remarks>
    /// 基线的 `Aggregation.register` 收 `stowageMode` 却不用它（只往 `registerForBendersSP` 传）。三种模式注册出的约束数相同。
    /// 若把模式误用进某处（例如按模式跳过密度约束），这条会红。
    ///
    /// The baseline accepts `stowageMode` and does not use it on this path. All three modes register the same number of constraints; misusing the mode somewhere turns this red.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldIgnoreTheStowageModeWhenRegistering() {
        foreach (StowageMode mode in new[] { StowageMode.Predistribution, StowageMode.FullLoad, StowageMode.WeightRecommendation }) {
            var rig = new Rig();
            AirworthinessSecurityAggregation aggregation = rig.Aggregation();
            aggregation.Register(mode, rig.Model).IsOk.Should().BeTrue();
            aggregation.LinearDensity.LinearDensitySymbol(0).Should().NotBeNull("every mode registers the density symbols");
            aggregation.SurfaceDensity.SurfaceDensitySymbol(0).Should().NotBeNull();
            aggregation.MinLowPayload.MinLowPayloadSymbol.Should().NotBeNull();
        }
    }

    /// <summary>
    /// **给一条包络就多注册它的符号** / Supplying one envelope registers its symbols too.
    /// </summary>
    /// <remarks>
    /// 成对：给一个包络时它的两个符号都被填上，而空列表时没有包络可填。**包络只注册符号、不加约束行**——所以不能比
    /// `Constraints.Count`（两者相同，判据会假绿）。这与 `EnvelopeLimit` 分工：符号在这里注册，约束在那里加。
    ///
    /// Paired: with one envelope both of its symbols get filled; with the empty list there is nothing to fill. **The envelope registers symbols only and adds no constraint
    /// rows**, so `Constraints.Count` cannot be compared — it is the same either way and the criterion would be falsely green. That is the split with `EnvelopeLimit`: symbols
    /// here, rows there.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldRegisterTheEnvelopesItIsGiven() {
        var emptyRig = new Rig();
        AirworthinessSecurityAggregation empty = emptyRig.Aggregation();
        empty.Register(StowageMode.FullLoad, emptyRig.Model).IsOk.Should().BeTrue();
        empty.Envelopes.Should().BeEmpty();

        var withEnvelopeRig = new Rig();
        var lhs = new EnvelopeSide("lhs", EnvelopeSideType.Left, new[] {
            new EnvelopePoint(40000.0, 10.0),
            new EnvelopePoint(60000.0, 20.0),
        });
        var rhs = new EnvelopeSide("rhs", EnvelopeSideType.Right, new[] {
            new EnvelopePoint(40000.0, 50.0),
            new EnvelopePoint(60000.0, 70.0),
        });
        var envelope = new Envelope(FlightPhase.TakeOff, "takeoff", lhs, rhs, withEnvelopeRig.TotalWeight);
        var envelopes = new Dictionary<FlightPhase, IReadOnlyList<IEnvelope>> {
            [FlightPhase.TakeOff] = new[] { envelope },
        };
        withEnvelopeRig.Aggregation(envelopes: envelopes).Register(StowageMode.FullLoad, withEnvelopeRig.Model).IsOk.Should().BeTrue();
        envelope.MinIndexSymbol.Should().NotBeNull("the envelope's own registration filled it");
        envelope.MaxIndexSymbol.Should().NotBeNull();
        envelope.MinIndexSymbol!.Name.Should().Be("min_index_takeoff_takeoff");
        envelope.MaxIndexSymbol!.Name.Should().Be("max_index_takeoff_takeoff");
    }
}
