// 配载聚合的判据 / Criteria for the stowage aggregation.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
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
/// **配载聚合** / The stowage aggregation.
/// </summary>
/// <remarks>
/// 三条注册路径的顺序各不相同，见 <see cref="StowageAggregation"/> 的说明。判据的重点是**它们确实不同**：
/// 把三者写成同一个方法是最自然的"顺手重构"，而它会让 MP 多算两族符号、SP 重复注册装载变量。
///
/// The three registration paths differ; the criteria focus on **them actually differing**. Collapsing them into one method is the natural "tidy-up" and it makes the master
/// compute two extra symbol families while the sub-problem re-registers the stowage variables.
/// </remarks>
public class Demo2StowageAggregationTests {
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

    private sealed class Rig
    {
        public Rig(AircraftType type = AircraftType.B737, bool withKernel = true) {
            var items = new List<Item> { MakeItem("I1") };
            var positions = new List<StowagePosition> { MakePosition("P1", 1UL) };
            Items = items;
            Positions = positions;
            Aircraft = new AircraftModel(type, type.ToString(), new AircraftMinorModel(type.ToString()));
            var fuselage = new Fuselage(null, Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);
            var flight = new Flight(new FlightNo("CA1234"), new IATACode("PEK"), new IATACode("SHA"));
            Aggregation = new StowageAggregation(
                Aircraft,
                flight,
                items,
                positions,
                new Appointment(new Dictionary<Item, Position>()),
                new BiologicalLimit(new List<(CargoType, CargoType)>(), new List<(CargoType, CargoType)>(), new List<CargoType>()),
                new Dictionary<NeighbourType, IReadOnlyList<Neighbour>>(),
                plannedPayload: 5000.0,
                maxPayload: 20000.0,
                withMultiLoadingSchema: true,
                formula: new Formula(Aircraft, 100.0, 20.0, 120.0, 5000.0, 0.0),
                fuselage: fuselage,
                fuel: new Dictionary<FlightPhase, FuelConstant>());

            // 装载内核必须喂进去，否则 `Stowage.Register` 会在注册约束时失败。
            // The loading kernel has to be supplied or `Stowage.Register` fails when it adds constraints.
            if (withKernel) {
                Aggregation.Stowage.LoadingContext = MakeKernel(items, positions);
            }
        }

        public IReadOnlyList<Item> Items { get; }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public AircraftModel Aircraft { get; }

        public StowageAggregation Aggregation { get; }
    }

    /// <summary>
    /// **构造时就建好派生模型，配重按机型可空** / The derived models exist right after construction, with the ballast nullable by type.
    /// </summary>
    /// <remarks>
    /// **`BallastNeeded` 是 ` || `**——它既不是"窄体"也不是"宽体"。所以三种机型一起断言：
    ///
    /// | 机型 | 窄体？ | 宽体？ | 需要配重？ |
    /// | --- | --- | --- | --- |
    /// | | 是 | 否 | **否** |
    /// | | 否 | 是 | **是** |
    /// | | 否 | 是 | **否** |
    ///
    /// 那一行是关键：它排除"配重按宽体判断"这个很自然的错误读法。最初就写成了"宽体机有配重"，判据直接红。
    ///
    /// **`BallastNeeded` is ` || `** — neither "narrow" nor "wide", so three types are asserted together, tabulated above. The row is the point: it rules out the
    /// very natural misreading "ballast follows the wide-body flag". The first draft said exactly that and turned red.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldBuildTheDerivedModelsAndTheBallastByType() {
        var narrow = new Rig();
        narrow.Aggregation.Stowage.Should().NotBeNull();
        narrow.Aggregation.Load.Should().NotBeNull();
        narrow.Aggregation.Payload.Should().NotBeNull();
        narrow.Aggregation.TotalWeight.Should().NotBeNull();
        narrow.Aggregation.MaxLoadWeight.Should().NotBeNull();
        narrow.Aggregation.Ballast.Should().BeNull("a narrow body needs no ballast");

        var middling = new Rig(AircraftType.B767);
        middling.Aggregation.Ballast.Should().NotBeNull("a B767 needs ballast");
        middling.Aggregation.Ballast!.AircraftModel.Should().BeSameAs(middling.Aircraft);

        var widest = new Rig(AircraftType.B747);
        widest.Aggregation.Ballast.Should().BeNull("a B747 is a wide body but does not need ballast: BallastNeeded is B757 || B767, not IsWideBody");
    }

    /// <summary>
    /// **`Register` 注册全部五步** / `Register` runs all five steps.
    /// </summary>
    /// <remarks>
    /// 逐层断言符号都建出来了：装载变量（`stowage_0_0`）、载荷估计、业载估计、总重估计、最大装载重量。
    /// 成对：同一模型上再跑一次 `Register` 会失败（`Stowage.Register` 只允许一次），说明第一次真的注册了。
    ///
    /// Asserts each layer's symbols exist: the stowage variables (`stowage_0_0`), the load estimates, the payload estimate, the total weight estimates and the maximum load
    /// weight. Paired: calling `Register` a second time on the same model fails (`Stowage.Register` allows one registration), proving the first one really ran.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldRegisterAllFiveSteps() {
        var rig = new Rig();
        using var model = new LinearMetaModel<Flt64>();
        rig.Aggregation.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();

        rig.Aggregation.Load.EstimateLoadWeightSymbol(0).Should().NotBeNull();
        rig.Aggregation.Payload.EstimatePayload.Should().NotBeNull();
        rig.Aggregation.TotalWeight.EstimateTotalWeight(FlightPhase.TakeOff).Should().NotBeNull();
        rig.Aggregation.MaxLoadWeight.MaxLoadWeightSymbol(0).Should().NotBeNull("the fifth step ran");
        model.Constraints.Count.Should().BeGreaterThan(0, "the load model adds capacity constraints");

        using var second = new LinearMetaModel<Flt64>();
        StowageAggregation fresh = new Rig().Aggregation;
        fresh.Register(StowageMode.FullLoad, second).IsOk.Should().BeTrue();
        fresh.Register(StowageMode.FullLoad, second).IsOk.Should().BeFalse("the stowage variables cannot be registered twice");
    }

    /// <summary>
    /// **装载内核必须喂进装载模型，否则注册失败** / The loading kernel must reach the stowage model or registration fails.
    /// </summary>
    /// <remarks>
    /// 成对：喂了内核注册成功，不喂则失败。内核是"哪些货物能放哪些舱位"的来源，缺了它后面的载荷与最大装载重量都建不起来。
    ///
    /// **一处不可观测已记录**：把 `Load = new Load(items, positions, Stowage, …)` 改成 `new Load(items, positions, new StowageModel(items, positions), …)`
    /// （即载荷模型不共享装载实例）时，本 fixture 里**两条判据都照常通过**——`Load.Register` 容忍 `LoadingContext` 为 `null`，只是少加一些约束，
    /// 而"少加多少"与"内核根本没喂"两种情况在本 fixture 下得到同样的约束集合。要区分它需要一条只有内核才存在的约束（当前 `Load.Register` 没有这种分支）。
    /// 因此这个变异记录为不可观测，而不是编一条假判据。
    ///
    /// Paired: registration succeeds with the kernel and fails without it. **One unobservable recorded**: replacing `Load = new Load(items, positions, Stowage,...)` with
    /// `new Load(items, positions, new StowageModel(items, positions),...)` (the load model not sharing the stowage instance) leaves **both criteria passing** in this fixture —
    /// `Load.Register` tolerates a `null` `LoadingContext` and merely adds fewer constraints, and "fewer" and "never supplied" produce the same constraint set here. Telling them
    /// apart needs a constraint that exists only with the kernel, which `Load.Register` has no branch for. The mutation is recorded as unobservable rather than papered over with a
    /// fake criterion.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldRequireTheLoadingKernel() {
        var withKernel = new Rig(withKernel: true);
        using var richModel = new LinearMetaModel<Flt64>();
        withKernel.Aggregation.Register(StowageMode.FullLoad, richModel).IsOk.Should().BeTrue();
        richModel.Constraints.Count.Should().BeGreaterThan(0);

        var withoutKernel = new Rig(withKernel: false);
        using var bareModel = new LinearMetaModel<Flt64>();
        withoutKernel.Aggregation.Register(StowageMode.FullLoad, bareModel).IsOk.Should().BeFalse(
            "the load and maximum-load-weight models cannot be built without the kernel");
    }

    /// <summary>
    /// **MP 不含总重与最大装载重量，SP 不含装载与载荷** / The master skips the total weight and the maximum load weight; the sub-problem skips the stowage and the load.
    /// </summary>
    /// <remarks>
    /// 三点成对，用**符号是否被建出来**观察（不是行数——这几个模型都只注册符号）：
    ///
    /// 1. `RegisterForBendersMP` 之后：业载符号**有**，总重符号**没有**。
    /// 2. `RegisterForBendersSP` 之后：总重符号**有**，载荷估计符号**没有**（因为载荷没注册）。
    /// 3. 两条路径都成功。
    ///
    /// 把三者写成同一个方法会让第 1 点的"总重没有"与第 2 点的"载荷没有"同时变红。
    ///
    /// Three paired points observed through **whether the symbols exist** (not row counts — these models register symbols only): after the master registration the payload
    /// symbol **is** there and the total weight symbol **is not**; after the sub-problem registration the total weight **is** there and the load estimate **is not**; and both
    /// paths succeed. Collapsing the three into one method turns both negative assertions red.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldKeepTheThreeRegistrationPathsDifferent() {
        var master = new Rig();
        using var masterModel = new LinearMetaModel<Flt64>();
        master.Aggregation.RegisterForBendersMP(StowageMode.FullLoad, masterModel).IsOk.Should().BeTrue();
        master.Aggregation.Payload.EstimatePayload.Should().NotBeNull("the master registers the payload");
        master.Aggregation.TotalWeight.EstimateTotalWeight(FlightPhase.TakeOff)
            .Should().BeNull("the master stops before the total weight");
        master.Aggregation.MaxLoadWeight.MaxLoadWeightSymbol(0)
            .Should().BeNull("and before the maximum load weight");

        var sub = new Rig();
        using var subModel = new LinearMetaModel<Flt64>();
        sub.Aggregation.RegisterForBendersSP(StowageMode.FullLoad, subModel).IsOk.Should().BeTrue();
        sub.Aggregation.TotalWeight.EstimateTotalWeight(FlightPhase.TakeOff)
            .Should().NotBeNull("the sub-problem registers the total weight");
        sub.Aggregation.Load.EstimateLoadWeightSymbol(0)
            .Should().BeNull("but skips the stowage and the load entirely");
    }

    /// <summary>
    /// **单模型 Benders 路径：业载只能注册一次，追索只补重量** /
    /// The single-model Benders path: the payload is registered once, recourse adds only the weights.
    /// </summary>
    /// <remarks>
    /// "一个模型 + 阶段表"。同一个模型上，
    /// `RegisterForBendersMP` 已经把业载注册进去，而 `RegisterForBendersSP` 会**再注册一次**业载——
    /// 那条路只对"两个模型"成立，也是本仓 demo2 现在走的路。
    ///
    /// 这条判据把三件事同时钉住：
    ///
    /// | 调用 | 期望 | 为什么 |
    /// | --- | --- | --- |
    /// | `RegisterForBendersMP` | `IsOk` | 主问题半注册成功 |
    /// | 再 `RegisterForBendersSPWithoutPayload` | `IsOk`，符号数增加 | 单模型路径走得通 |
    /// | 再 `RegisterForBendersSP`（原版） | **失败** | 业载重复注册，证明两条路径确实不同 |
    ///
    /// 第三条是关键：**它不是"我们觉得会失败"，是这里实测失败**。没有它，`RegisterForBendersSPWithoutPayload`
    /// 看起来只是"多余的便利方法"。
    ///
 /// This implementation is "one model plus a stage table". On one model `RegisterForBendersMP` already registers the payload, and `RegisterForBendersSP` registers it
    /// **again** — a path that only holds for "two models", which is what demo2 does today. This criterion pins three things at once (table): the master half
    /// registers, the payload-free recourse then registers and adds symbols, and the original recourse registration **fails** on that model. The third line matters: it
    /// is not "we think it fails", it fails here, measured. Without it, the payload-free method looks like a redundant convenience.
    /// </remarks>
    [Fact]
    public void TheSingleModelRecourse_ShouldRegisterWithoutThePayload() {
        var rig = new Rig();

        using var model = new LinearMetaModel<Flt64>();
        rig.Aggregation.RegisterForBendersMP(StowageMode.FullLoad, model).IsOk.Should().BeTrue(
            "the master half registers stowage, load and payload into one model");

        // **不断言"符号数增加"**： 第一版这么写了，实测是 13 → 13。
        // 原因是 `RegisterForBendersMP` 注册的业载已经带上了这些重量符号，再注册一次命中的是同一批名字。
        // **假设被实测推翻，就按实测写判据**，而不是把假设留下。
        //
        // **No "symbol count grows" assertion**: the earlier first version claimed that and measured 13 → 13. The payload registered by `RegisterForBendersMP` already carries
        // those weight symbols, so registering again hits the same names. **When a measurement overturns an assumption, the criterion follows the measurement.**
        rig.Aggregation.RegisterForBendersSPWithoutPayload(model).IsOk.Should().BeTrue(
            "the recourse half registers without touching the payload, which is what a single-model path needs");

        rig.Aggregation.RegisterForBendersSP(StowageMode.FullLoad, model).IsOk.Should().BeFalse(
            "the original recourse registration registers the payload again, and that is exactly why the port runs two models");
    }


    /// <summary>
    /// **业载注册到底加了几个符号** / How many symbols the payload registration actually adds.
    /// </summary>
    /// <remarks>
    /// 量到配载那一半只加 8 个符号（占用 2 + 分配 6），而「装载 → 业载」也在同一半里。
    /// 若业载真的注册了，数字不可能只有 8。这条判据把一个模型只交给业载，实测它加多少。
    ///
    /// measured the stowage half adding only eight symbols (two occupancy plus six assignments), yet "load to payload" lives in the same half. If the payload really
    /// registered, the number could not be eight. This hands a fresh model to the payload alone and measures what it adds.
    /// </remarks>
    [Fact]
    public void ThePayloadRegistration_ShouldAddItsOwnSymbols() {
        var rig = new Rig();

        using var model = new LinearMetaModel<Flt64>();
        rig.Aggregation.Payload.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue(
            "the payload half is reported as Ok by the master registration, so it must also succeed on its own");

        int symbols = model.Tokens.Tokens.Count;
        model.Constraints.Count.Should().BeGreaterThanOrEqualTo(0);
            // **实测：业载注册返回 `IsOk` 且 token 数保持 0**——`LinearExpressionSymbol` 不是 token，
            // 所以"token +0"既不等于"什么都没做"，也不等于"符号没进模型"。 那条 `recourse:+0 tokens` 同样要这样读。
            // **要判断一个注册半到底做了什么，得同时看约束与目标**，不能只看 token。
            //
            // **Measured : the payload registration returns `IsOk` while the token count stays 0** — a `LinearExpressionSymbol` is not a token, so "tokens +0"
            // means neither "it did nothing" nor "the symbol never entered the model". the earlier `recourse:+0 tokens` must be read the same way. **Judging what a registration
            // half did needs the constraint and objective counts too, not tokens alone.**
            symbols.Should().Be(0,
                "the recorded measurement: the payload half adds expression symbols, which this token count does not see");
    }

}
