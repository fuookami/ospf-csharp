// 配载聚合初始化器与上下文的判据 / Criteria for the stowage aggregation initializer and the contexts.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;
using StowageInitializer = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.AggregationInitializer;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **配载初始化器与两个上下文的 Init** / The stowage initializer and both contexts' `Init`.
/// </summary>
/// <remarks>
/// 这里把两个"永远返回 `Ok`"的上下文壳（`StowageContext` / `MacContext`）换成真的 `Init`，并补上 `Stowage/Service/AggregationInitializer`
/// 与 `LoadingContextFactory`。判据集中在三处：
///
/// 1. **校验真的挡住**（六条各试一次，且成对给出好输入）。
/// 2. **翻译真的落地**（货重、优先级档位、舱位最大重量、内核真的建起来）。
/// 3. **上下文不再静默成功**（上游未初始化时明确失败）。
///
/// replaces two context shells that **always returned `Ok`** (`StowageContext` / `MacContext`) with real `Init` methods and adds the stowage
/// `AggregationInitializer` and the `LoadingContextFactory`. The criteria target three things: **the validations actually reject** (each of six tried once, paired with a good
/// input); **the translation actually lands** (weights, priority buckets, position capacity, the kernel really built); and **the contexts no longer succeed silently**
/// (an uninitialised upstream fails explicitly).
/// </remarks>
public class Demo2StowageInitializerTests {
    private sealed class Rig
    {
        public Rig(RequestDTO? input = null, int? bulkPositionIndex = null) {
            Input = input ?? AircraftLoadingDemo.CreateSampleRequest();
            AircraftModel = new AircraftModel(AircraftType.B737, "B737", new AircraftMinorModel("B737"));
            // **飞机舱位来自固定的样例请求，不是 `Input`**：否则"请求舱位数量与飞机不一致"这条校验永远走不到（两边一起变）。
            // **The aircraft positions come from the fixed sample request, not from `Input`**: otherwise the count-mismatch validation can never fire (both sides move together).
            IReadOnlyList<PositionInput> aircraftInputs = AircraftLoadingDemo.CreateSampleRequest().Positions;
            var bulkLocation = new PositionLocation(new HashSet<PositionLocationTag> { PositionLocationTag.Bulk });
            var positions = aircraftInputs
                .Select((position, index) => new AircraftPosition(
                    id: (ulong)(index + 1),
                    spaceName: position.Name,
                    sizeCode: "A",
                    linearLoadingOrder: (byte)index,
                    coordinate: new PositionCoordinate(position.LongitudinalArm - 10.0, position.LongitudinalArm + 10.0, position.LateralArm - 5.0, position.LateralArm + 5.0),
                    shape: new PositionShape(20.0, 10.0, 20.0 * 10.0 * 30.0),
                    location: index == bulkPositionIndex ? bulkLocation : PositionLocation.NormalMainLocation))
                .ToList();
            var deck = new Deck(DeckLocation.Main, new List<HatchDoor>(), positions, new Dictionary<HatchDoor, IReadOnlyList<DoorUbiety>>());
            Aircraft = new AircraftAggregation(
                new RegNo("B-0001"),
                AircraftModel,
                new Formula(AircraftModel, lip: 100.0, chord: 20.0, standardDatum: 120.0, forceDistanceCoefficient: 5000.0, doiCorrection: 7.0),
                new Fuselage(null, Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0),
                new List<FuelTank>(),
                new Dictionary<FlightPhase, FuelConstant> {
                    [FlightPhase.TakeOff] = new FuelConstant(Density: 0.8, Weight: 1000.0, Index: 1000.0),
                    [FlightPhase.Landing] = new FuelConstant(Density: 0.8, Weight: 2000.0, Index: 2000.0),
                },
                new List<Deck> { deck },
                new Dictionary<NeighbourType, IReadOnlyList<Neighbour>>());
        }

        public RequestDTO Input { get; }

        public AircraftModel AircraftModel { get; }

        public AircraftAggregation Aircraft { get; }

        public Result<StowageAggregation, ErrorCode, Error<ErrorCode>> Run() =>
            StowageInitializer.Initialize(Aircraft, Input);
    }

    private static StowageAggregation AggregationOf(Rig rig) {
        Result<StowageAggregation, ErrorCode, Error<ErrorCode>> result = rig.Run();
        result.IsOk.Should().BeTrue();
        return ((Ok<StowageAggregation, ErrorCode, Error<ErrorCode>>)result).Value!;
    }

    /// <summary>
    /// **合法请求真的翻译成聚合** / A valid request really translates into an aggregation.
    /// </summary>
    /// <remarks>
    /// 逐项断言：货物数量与顺序、优先级档位（10 → 高、6 → 普通、4 → 普通）、舱位最大重量、计划业载是货重之和、最大业载取 DTO 上限、
    /// 且**装载内核真的建起来了**（`Stowage.LoadingContext` 非 `null`）。
    ///
    /// 只断言 `IsOk` 无法区分"真的翻译了"与"返回一个空聚合"——后者正是 之前两个上下文的形态。
    ///
    /// Asserts item by item: the count and order, the priority buckets (10 → high, 6 → normal, 4 → normal), the position capacity, the planned payload as the sum of weights,
    /// the maximum payload from the DTO, and that **the loading kernel really exists** (`Stowage.LoadingContext` non-null). Asserting only `IsOk` cannot distinguish a real
    /// translation from an empty aggregation — the shape both contexts had before.
    /// </remarks>
    [Fact]
    public void Initialize_ShouldTranslateTheRequestIntoTheAggregation() {
        var rig = new Rig();
        StowageAggregation aggregation = AggregationOf(rig);

        aggregation.Items.Should().HaveCount(3);
        aggregation.Items.Select(item => item.Name).Should().ContainInOrder("C1", "C2", "C3");
        aggregation.Items[0].Priority.Category.Should().Be(CargoPriorityCategory.High, "priority 10 is >= 8");
        aggregation.Items[0].Priority.Name.Should().Be("P10");
        aggregation.Items[1].Priority.Category.Should().Be(CargoPriorityCategory.Normal, "priority 6 sits between 2 and 8");
        aggregation.Items[2].Priority.Category.Should().Be(CargoPriorityCategory.Normal, "priority 4 sits between 2 and 8");
        aggregation.Items.Should().OnlyContain(item => item.Status == ItemStatus.Optional);
        aggregation.Items.Should().OnlyContain(item => item.Location == ItemLocation.MainDeck, "this DTO carries no deck location");

        aggregation.Positions.Should().HaveCount(2);
        aggregation.Positions.Select(position => position.Name).Should().BeEquivalentTo(new[] { "P1", "P2" });
        aggregation.Positions[0].Mlw.Mlw.Should().BeApproximately(10.0, 1e-9, "the capacity comes from the request");
        aggregation.Positions.Should().OnlyContain(position => position.Mla == 1UL, "the DTO has no maximum count, so the conservative 1 applies");

        aggregation.Payload.PlannedPayload.Should().BeApproximately(18.0, 1e-9, "8 + 6 + 4");
        aggregation.Payload.MaxPayload.Should().BeApproximately(20.0, 1e-9);
        aggregation.Stowage.LoadingContext.Should().NotBeNull("the kernel is built by the factory");
        // 舱位容量必须原样进入内核：写成常数会让装载约束全部按错误的上限生成。
        // The position capacity must reach the kernel unchanged; a constant would generate every loading constraint against a wrong limit.
        aggregation.Stowage.LoadingContext!.Problem.Positions[0].MaxWeight.Value.ToDouble()
            .Should().BeApproximately(10.0, 1e-9, "the kernel takes the request's capacity");
        aggregation.Load.Should().NotBeNull();
        aggregation.TotalWeight.Should().NotBeNull();
    }

    /// <summary>
    /// **装载内核的允许指派只包含双向同意的组合** / The kernel's allowed assignments hold only mutually agreed pairs.
    /// </summary>
    /// <remarks>
    /// 成对：每件货物至少有一个允许舱位（内核构造要求如此），且允许集合是 `StowageNeeded` 的结果——把条件写成恒真会让
    /// 三件货 × 两个舱位 = 6 个指派，恒假会让内核构造失败。断言数量落在两者之间且等于 `StowageNeeded` 的实际计数。
    ///
    /// Paired: every item has at least one allowed position (the kernel requires it), and the allowed set is `StowageNeeded`'s result — a constant true would give
    /// three items × two positions = six assignments while a constant false would make construction fail. The count is asserted to equal `StowageNeeded`'s actual count.
    /// </remarks>
    [Fact]
    public void Initialize_ShouldBuildTheKernelFromTheStowagePredicate() {
        // **必须让一对不合法**，否则 `StowageNeeded` 恒真与真实判定给出同一个允许集合，判据无法区分。
        // 把 P2 改成**散货舱**：`Enabled` 里"散货舱禁止装载非散货装载物"会拒掉主甲板货物，允许集合因此严格小于 3 × 2。
        // **One illegal pair is required**, or a constant-true `StowageNeeded` and the real predicate give the same allowed set. P2 is turned into a **bulk** position: `Enabled`
        // rejects a non-bulk item there, so the allowed set is strictly smaller than 3 × 2.
        var rig = new Rig(bulkPositionIndex: 1);
        StowageAggregation aggregation = AggregationOf(rig);

        int expected = aggregation.Items
            .Sum(item => aggregation.Positions.Count(position => Stowage.StowageNeeded(item, position)));
        expected.Should().BeGreaterThan(0, "the kernel cannot be built with no allowed assignment");
        (aggregation.Items.Count * aggregation.Positions.Count).Should().BeGreaterThan(expected,
            "not every pair is legal, otherwise the predicate is a constant");

        // **直接读内核暴露的 `Problem`**：只断言"内核非空"无法区分允许集合被写成恒真或恒假——
        // 恒假会让构造失败（也算检出），但恒真会让构造成功而约束全错，必须比对具体集合。
        // **Read the kernel's exposed `Problem` directly**: asserting "the kernel exists" cannot tell a constant-true allowed set from a constant-false one — the latter fails
        // construction (also a detection) while the former succeeds with every constraint wrong, so the exact set is compared.
        var kernel = aggregation.Stowage.LoadingContext!;
        kernel.Problem.AllowedAssignments.Should().HaveCount(expected);
        kernel.Problem.AllowedAssignments.Should().BeEquivalentTo(
            aggregation.Items.SelectMany(item => aggregation.Positions
                .Where(position => Stowage.StowageNeeded(item, position))
                .Select(position => new Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.AssignmentKey(item.Id, position.Name))));
        kernel.Problem.Items.Should().HaveCount(aggregation.Items.Count);
        kernel.Problem.Positions.Should().HaveCount(aggregation.Positions.Count);
    }

    /// <summary>
    /// **六条校验各自挡住自己的坏输入** / Each of the six validations rejects its own bad input.
    /// </summary>
    /// <remarks>
    /// 每条成对：坏输入失败、同一条的好输入成功。六条**各自只改一处**，否则无法判断是哪条起的作用。
    ///
    /// Each is paired: the bad input fails and a good one of the same shape succeeds. The six change **exactly one thing each**, otherwise it is impossible to tell which
    /// validation acted.
    /// </remarks>
    [Fact]
    public void Initialize_ShouldRejectEachBadInputAtItsOwnBoundary() {
        RequestDTO good = AircraftLoadingDemo.CreateSampleRequest();

        // `with` 表达式必须先括起来才能接成员访问，所以每条都先赋给局部变量再 `.Run`。
        // A `with` expression needs parentheses before member access, so each is assigned to a local first and then `.Run`.
        RequestDTO noCargo = good with { Cargos = new List<CargoInput>() };
        new Rig(input: noCargo).Run().IsOk.Should().BeFalse("no cargo at all");

        RequestDTO blankSource = good with { Cargos = new List<CargoInput> { new("C1", 8.0, 10, "", "D1") } };
        new Rig(input: blankSource).Run().IsOk.Should().BeFalse("a blank source is not a source");

        RequestDTO duplicateNames = good with {
            Cargos = new List<CargoInput> { new("C1", 8.0, 10, "S1", "D1"), new("C1", 6.0, 6, "S2", "D1") },
        };
        new Rig(input: duplicateNames).Run().IsOk.Should().BeFalse("duplicate cargo names");

        RequestDTO zeroWeight = good with { Cargos = new List<CargoInput> { new("C1", 0.0, 10, "S1", "D1") } };
        new Rig(input: zeroWeight).Run().IsOk.Should().BeFalse("a zero weight is not a weight");

        RequestDTO zeroBound = good with { PayloadUpperBound = 0.0 };
        new Rig(input: zeroBound).Run().IsOk.Should().BeFalse("a zero payload bound is not a bound");

        RequestDTO onePosition = good with { Positions = new List<PositionInput> { new("P1", 10.0, -1.0, -0.5) } };
        Result<StowageAggregation, ErrorCode, Error<ErrorCode>> onePositionResult = new Rig(input: onePosition).Run();
        onePositionResult.IsOk.Should().BeFalse("the position count does not match the aircraft");
        // **断言失败的是数量分支**：后面还有一条"请求缺少舱位"也会拒同一个输入。只断言 `IsOk` 为假时，删掉数量校验仍然会红——
        // 但红在另一条分支上，等于没有钉住。数量分支用 `ApplicationFailed`，缺项分支也用同一个码，所以改看消息。
        // **Assert that the count branch is the one failing**: a later "the request has no position" check rejects the same input. Asserting only that `IsOk` is false would
        // still go red after the count check is deleted — but on the other branch, which pins nothing. Both use `ApplicationFailed`, so the message is compared.
        (onePositionResult as Failed<StowageAggregation, ErrorCode, Error<ErrorCode>>)!.Error.Message
            .Should().Contain("数量不一致", "the count check must be the one that fires, not the missing-position check");

        // 成对的好输入：只把坏字段改回合法值。
        // The paired good inputs: only the bad field is restored.
        RequestDTO oneValidCargo = good with { Cargos = new List<CargoInput> { new("C1", 8.0, 10, "S1", "D1") } };
        new Rig(input: oneValidCargo).Run().IsOk.Should().BeTrue("one valid cargo is enough");
        RequestDTO smallBound = good with { PayloadUpperBound = 0.5 };
        new Rig(input: smallBound).Run().IsOk.Should().BeTrue("a small positive bound is a bound");
    }

    /// <summary>
    /// **上下文不再静默成功** / The contexts no longer succeed silently.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 未初始化的 `StowageContext.Aggregation` 是 `null`（不是 `null!` 的假非空）。
    /// 2. 上游飞机聚合缺失时 `Init` **失败**，且消息点明是飞机聚合—— 之前它返回 `Ok`。
    /// 3. 正常初始化后 `Aggregation` 非 `null`，且**第二次 `Init` 不重建**（返回同一个实例）。
    ///
    /// Three paired points: an uninitialised `StowageContext.Aggregation` is `null` (not a fake non-null); `Init` **fails** with a message naming the aircraft aggregation when
    /// that upstream is missing — before it returned `Ok`; and after a normal initialisation `Aggregation` is non-null while a **second `Init` does not rebuild**
    /// (same instance).
    /// </remarks>
    [Fact]
    public void Contexts_ShouldNoLongerSucceedSilently() {
        var stowageContext = new StowageContext();
        stowageContext.Aggregation.Should().BeNull("nothing has been initialised yet");

        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();
        Result<Success, ErrorCode, Error<ErrorCode>> withoutAircraft = stowageContext.Init(new AircraftContext(), input);
        withoutAircraft.IsOk.Should().BeFalse("the aircraft aggregation is missing");
        (withoutAircraft as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("飞机聚合");
        stowageContext.Aggregation.Should().BeNull("a failure leaves nothing behind");

        var rig = new Rig();
        var aircraftContext = new AircraftContext();
        typeof(AircraftContext).GetProperty("Aggregation")!.SetValue(aircraftContext, rig.Aircraft);

        stowageContext.Init(aircraftContext, input).IsOk.Should().BeTrue();
        stowageContext.Aggregation.Should().NotBeNull();
        StowageAggregation first = stowageContext.Aggregation!;
        stowageContext.Init(aircraftContext, input).IsOk.Should().BeTrue();
        stowageContext.Aggregation.Should().BeSameAs(first, "an existing aggregation is not rebuilt");

        var macContext = new MacContext();
        macContext.Aggregation.Should().BeNull();
        macContext.Init(aircraftContext, new StowageContext(), input).IsOk.Should().BeFalse("the stowage aggregation is missing");

        var macWithoutAircraft = new MacContext();
        macWithoutAircraft.Init(new AircraftContext(), stowageContext, input).IsOk.Should().BeFalse("the aircraft aggregation is missing");

        macContext.Init(aircraftContext, stowageContext, input).IsOk.Should().BeTrue();
        macContext.Aggregation.Should().NotBeNull();
        macContext.Aggregation!.Torque.Should().NotBeNull();
    }

    /// <summary>
    /// **MAC 初始化器拒绝空位置表** / The MAC initializer rejects an empty position list.
    /// </summary>
    /// <remarks>
    /// 成对：空位置表失败、正常配载聚合成功。空表下 `Torque` 的多项式全是常数，MAC 会退化成一个没有单式的符号——
    /// 下游水平安定面据此算出的配平毫无意义。
    ///
    /// Paired: an empty position list fails and a normal stowage aggregation succeeds. With an empty list every `Torque` polynomial is a constant and the MAC degrades to a
    /// symbol with no monomials, making any trim the horizontal stabilizer derives from it meaningless.
    /// </remarks>
    [Fact]
    public void MacInitializer_ShouldRejectAnEmptyPositionList() {
        var rig = new Rig();
        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();
        StowageAggregation stowage = AggregationOf(rig);

        Result<Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.MacAggregation, ErrorCode, Error<ErrorCode>> empty =
            Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Service.AggregationInitializer.Initialize(
                rig.Aircraft,
                new StowageAggregation(
                    rig.AircraftModel,
                    new Flight(new FlightNo("CA1"), new IATACode("PEK"), new IATACode("SHA")),
                    new List<Item>(),
                    new List<StowagePosition>(),
                    new Appointment(new Dictionary<Item, AircraftPosition>()),
                    new BiologicalLimit(new List<(CargoType, CargoType)>(), new List<(CargoType, CargoType)>(), new List<CargoType>()),
                    new Dictionary<NeighbourType, IReadOnlyList<Neighbour>>(),
                    plannedPayload: 0.0,
                    maxPayload: 20.0,
                    withMultiLoadingSchema: true,
                    formula: rig.Aircraft.Formula,
                    fuselage: rig.Aircraft.Fuselage,
                    fuel: rig.Aircraft.Fuel),
                input);
        empty.IsOk.Should().BeFalse("no position means no torque and a meaningless MAC");

        Result<Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.MacAggregation, ErrorCode, Error<ErrorCode>> ok =
            Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Service.AggregationInitializer.Initialize(rig.Aircraft, stowage, input);
        ok.IsOk.Should().BeTrue();
        Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.MacAggregation mac =
            ((Ok<Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.MacAggregation, ErrorCode, Error<ErrorCode>>)ok).Value!;
        mac.HorizontalStabilizers.Should().BeEmpty("the baseline passes an empty map");
        mac.Torque.Should().NotBeNull();
    }

    /// <summary>
    /// **端到端：三个上下文串起来后适航安全的 `Init` 真的能建出聚合** / End to end: with the three contexts chained, the airworthiness `Init` really builds an aggregation.
    /// </summary>
    /// <remarks>
    /// 让 `AirworthinessSecurityContext.Init` 可用，但当时上游两个上下文还是壳，所以它必然失败。这里把上游接上，端到端路径因此第一次走得通：
    /// 飞机 → 配载 → MAC → 适航安全，四步全部成功，且适航聚合的 `MinPayload` 是真实算出来的数。
    ///
 /// made `AirworthinessSecurityContext.Init` usable, but the two upstream contexts were still shells then, so it had to fail. wires the upstreams, making
    /// the end-to-end path work for the first time: aircraft → stowage → MAC → airworthiness, all four succeeding, with the airworthiness aggregation's `MinPayload` a real
    /// computed number.
    /// </remarks>
    [Fact]
    public void EndToEnd_ShouldChainTheFourContexts() {
        var rig = new Rig();
        RequestDTO input = AircraftLoadingDemo.CreateSampleRequest();
        var aircraftContext = new AircraftContext();
        typeof(AircraftContext).GetProperty("Aggregation")!.SetValue(aircraftContext, rig.Aircraft);

        var stowageContext = new StowageContext();
        stowageContext.Init(aircraftContext, input).IsOk.Should().BeTrue();

        var macContext = new MacContext();
        macContext.Init(aircraftContext, stowageContext, input).IsOk.Should().BeTrue();

        var securityContext = new AirworthinessSecurityContext();
        Result<Success, ErrorCode, Error<ErrorCode>> result = securityContext.Init(aircraftContext, stowageContext, macContext, input);
        result.IsOk.Should().BeTrue("every upstream aggregation now exists");
        securityContext.Aggregation.Should().NotBeNull();

        AirworthinessSecurityAggregation aggregation = securityContext.Aggregation!;
        // min(20, 18) × 0.6 = 10.8：上限与货量取小，再乘最小比例。
        // min(20, 18) × 0.6 = 10.8: the smaller of the bound and the cargo, times the minimum ratio.
        aggregation.MinPayload.Should().BeApproximately(10.8, 1e-9);
        aggregation.Positions.Should().HaveCount(2);
        aggregation.MaxCumulativeLoadWeight.LimitZones.Should().HaveCount(2);
        aggregation.Envelopes.Keys.Should().BeEquivalentTo(new[] { FlightPhase.ZeroFuel, FlightPhase.TakeOff, FlightPhase.Landing });
    }
}
