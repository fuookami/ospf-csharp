// CG 包络线的判据 / Criteria for the CG envelope.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;
using ErrorCode = Fuookami.Ospf.Utils.Error.ErrorCode;
using Success = Fuookami.Ospf.Utils.Functional.Success;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **CG 包络线（B592）** / The CG envelope (B592).
/// </summary>
/// <remarks>
/// 包络线是一条**折线**：每种总重对应一个允许的 CG 指数边界，两侧分别给下界与上界。两支取值——该阶段总重已知时产**常数**，
/// 未知时产**关于总重估计的分段线性符号**。
///
/// 第二支是本批的核心：总重本身是决策变量的表达式，所以"允许的 CG 指数"也是表达式。写成常数要先把总重估成某个数，而那个猜测会被当成硬边界——
/// 重心被限制在一个伪造的包络里，而且不会有任何报错。
///
/// An envelope is a **polyline**: each total weight has an allowed CG index bound, with the two sides giving the lower and upper bounds. Two branches — a known phase total
/// gives a **constant**, an unknown one gives a **piecewise-linear symbol over the total-weight estimate**. The second is the point: the total weight is itself an expression
/// over decisions, so the allowed CG index is too. Forcing a constant means guessing a weight and then treating the guess as a hard boundary — the centre of gravity confined to a
/// fabricated envelope with no error anywhere.
/// </remarks>
public class Demo2EnvelopeTests {
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
            coordinate: new PositionCoordinate(500.0, 500.0, 60.0, 60.0),
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

    private static (Envelope Envelope, TotalWeight TotalWeight, LinearMetaModel<Flt64> Model) Prepare(
        IReadOnlyDictionary<FlightPhase, double>? computedTotalWeight) {
        var items = new List<Item> { MakeItem("I1") };
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
        };
        var totalWeight = new TotalWeight(fuselage, fuel, payload, computedTotalWeight: computedTotalWeight);

        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();
        payload.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        totalWeight.Register(model).IsOk.Should().BeTrue();

        var lhs = new EnvelopeSide("lhs", EnvelopeSideType.Left, new[] {
            new EnvelopePoint(40000.0, 10.0),
            new EnvelopePoint(60000.0, 20.0),
        });
        var rhs = new EnvelopeSide("rhs", EnvelopeSideType.Right, new[] {
            new EnvelopePoint(40000.0, 50.0),
            new EnvelopePoint(60000.0, 70.0),
        });
        var envelope = new Envelope(FlightPhase.TakeOff, "takeoff", lhs, rhs, totalWeight);
        return (envelope, totalWeight, model);
    }

    /// <summary>
    /// **折线：中间插值，两端夹住** / A polyline: interpolate in between, clamp at the ends.
    /// </summary>
    /// <remarks>
    /// 四条分支成对：低于首点 10、中点 15（**不是 10 也不是 20**，所以阶跃会被检出）、高于末点 20。左右两侧各测一次，取值不同。
    ///
    /// Four paired branches: 10 below the first point, 15 in the middle (**neither 10 nor 20**, so a step is detected), 20 above the last. Both sides are checked and their values
    /// differ.
    /// </remarks>
    [Fact]
    public void Side_ShouldInterpolateInBetweenAndClampAtTheEnds() {
        (Envelope envelope, TotalWeight _, LinearMetaModel<Flt64> _) = Prepare(computedTotalWeight: null);

        envelope.MinIndexOf(30000.0).Should().BeApproximately(10.0, 1e-9, "below the first point");
        envelope.MinIndexOf(50000.0).Should().BeApproximately(15.0, 1e-9, "the midpoint of 10 and 20");
        envelope.MinIndexOf(50000.0).Should().NotBeApproximately(10.0, 1e-9, "a step would stay at the lower point");
        envelope.MinIndexOf(90000.0).Should().BeApproximately(20.0, 1e-9, "above the last point");

        envelope.MaxIndexOf(50000.0).Should().BeApproximately(60.0, 1e-9, "the right side interpolates its own points");
        envelope.MaxIndexOf(50000.0).Should().NotBeApproximately(envelope.MinIndexOf(50000.0), 1e-9,
            "the two sides are distinct bounds");
    }

    /// <summary>
    /// **点按总重排序，输入顺序不影响结果** / Points are sorted; input order does not change the answer.
    /// </summary>
    /// <remarks>
    /// 与"插值正确"成对：乱序点表在四个采样点上给出与升序表相同的结果。不排序时两端分支与相邻配对都会错。
    ///
    /// Paired with "interpolation is correct": a shuffled table gives the same answers as a sorted one at four samples. Without sorting, both boundary branches and the neighbour
    /// pairing go wrong.
    /// </remarks>
    [Fact]
    public void Side_ShouldSortItsPoints() {
        var shuffled = new EnvelopeSide("lhs", EnvelopeSideType.Left, new[] {
            new EnvelopePoint(60000.0, 20.0),
            new EnvelopePoint(40000.0, 10.0),
        });
        shuffled.Points.Select(point => point.TotalWeight).Should().BeInAscendingOrder();
        // 五点的期望值**逐个写出来**，不要用一条公式覆盖全部采样点：公式在两端不会自动夹住，而"夹住"正是这里要被钉住的行为。
        // The five expectations are **spelled out** rather than derived from one formula: a formula would not clamp at the ends, and clamping is exactly what is being pinned here.
        shuffled.At(30000.0).Should().BeApproximately(10.0, 1e-9, "clamped to the first point");
        shuffled.At(40000.0).Should().BeApproximately(10.0, 1e-9);
        shuffled.At(50000.0).Should().BeApproximately(15.0, 1e-9);
        shuffled.At(60000.0).Should().BeApproximately(20.0, 1e-9);
        shuffled.At(90000.0).Should().BeApproximately(20.0, 1e-9, "clamped to the last point");
    }

    /// <summary>
    /// **已知总重时产常数符号** / A known total weight yields a constant symbol.
    /// </summary>
    /// <remarks>
    /// 成对：给 `computedTotalWeight[TakeOff] = 50000` 时最小指数是常数 15 且**无单项式**；不给时最小指数**有单项式或至少不是那个常数**
    /// （它关于总重估计是折线）。写成"永远是常数"会让第二条路上的约束降级成一个猜测。
    ///
    /// Paired: with `computedTotalWeight[TakeOff] = 50000` the minimum index is the constant 15 with **no monomials**; without it the symbol follows the total-weight estimate.
    /// Always producing a constant would downgrade the constraint into a guess on the second path.
    /// </remarks>
    [Fact]
    public void Register_ShouldProduceAConstantWhenTheTotalWeightIsKnown() {
        (Envelope known, TotalWeight _, LinearMetaModel<Flt64> knownModel) = Prepare(
            new Dictionary<FlightPhase, double> { [FlightPhase.TakeOff] = 50000.0 });
        known.Register(knownModel).IsOk.Should().BeTrue();

        ILinearIntermediateSymbol<Flt64> minimum = known.MinIndexSymbol!;
        minimum.Name.Should().Be("min_index_takeoff_takeoff");
        minimum.Polynomial.Monomials.Should().BeEmpty("the interpolated value is a number");
        minimum.Polynomial.Constant.ToDouble().Should().BeApproximately(15.0, 1e-9);

        ILinearIntermediateSymbol<Flt64> maximum = known.MaxIndexSymbol!;
        maximum.Name.Should().Be("max_index_takeoff_takeoff");
        maximum.Polynomial.Constant.ToDouble().Should().BeApproximately(60.0, 1e-9);

        (Envelope unknown, TotalWeight _, LinearMetaModel<Flt64> unknownModel) = Prepare(computedTotalWeight: null);
        unknown.Register(unknownModel).IsOk.Should().BeTrue();
        unknown.MinIndexSymbol!.Name.Should().Be("min_index_takeoff_takeoff");
        unknown.MinIndexSymbol!.Polynomial.Constant.ToDouble().Should().NotBeApproximately(15.0, 1e-9,
            "without a known total the bound is a function, not that number");
    }

    /// <summary>
    /// **两侧分别注册，符号不同** / The two sides register separately as distinct symbols.
    /// </summary>
    /// <remarks>
    /// 成对：`min_index_*` 与 `max_index_*` 是两个不同的对象、名字不同、**数值也不同**（左侧 10/20，右侧 50/70）。只注册一侧时重心只受
    /// 单向约束——飞机可以重心偏出包络的另一边而求解器一声不响。
    ///
    /// Paired: `min_index_*` and `max_index_*` are distinct objects with distinct names and **distinct values** (left 10/20, right 50/70). Registering one side leaves the centre of
    /// gravity constrained in one direction only — it can leave the envelope on the other side with no complaint from the solver.
    /// </remarks>
    [Fact]
    public void Register_ShouldRegisterBothSidesSeparately() {
        (Envelope envelope, TotalWeight _, LinearMetaModel<Flt64> model) = Prepare(
            new Dictionary<FlightPhase, double> { [FlightPhase.TakeOff] = 50000.0 });
        envelope.Register(model).IsOk.Should().BeTrue();

        envelope.MinIndexSymbol.Should().NotBeNull();
        envelope.MaxIndexSymbol.Should().NotBeNull();
        envelope.MinIndexSymbol.Should().NotBeSameAs(envelope.MaxIndexSymbol);
        envelope.MinIndexSymbol!.Name.Should().StartWith("min_index_");
        envelope.MaxIndexSymbol!.Name.Should().StartWith("max_index_");
        envelope.MaxIndexSymbol!.Polynomial.Constant.ToDouble()
            .Should().BeGreaterThan(envelope.MinIndexSymbol!.Polynomial.Constant.ToDouble(),
                "the maximum index must lie above the minimum at the same weight");

        // 分段那一支也要分左右。上面走的是"已知总重 → 常数"的分支，它直接调 `MinIndexOf`/`MaxIndexOf`，绕过了内部选边的那一行；
        // 只有"未知总重 → 分段"的路径才会真的用 `isMinimum ? LhsSide : RhsSide`。少了这一段，把两侧都取左侧的写法检不出来。
        // The piecewise path needs its own left/right check. The case above takes the "known total → constant" branch, which calls `MinIndexOf`/`MaxIndexOf` directly and
        // bypasses the side selection; only the "unknown total → piecewise" path actually uses `isMinimum ? LhsSide : RhsSide`. Without this, taking the left side for both is
        // undetectable.
        (Envelope unknown, TotalWeight _, LinearMetaModel<Flt64> unknownModel) = Prepare(computedTotalWeight: null);
        unknown.Register(unknownModel).IsOk.Should().BeTrue();
        // 分段适配器的 `Polynomial` 是**结果多项式**，常数为 0 且两侧相同——比它区分不出来。能区分的是它包裹的那个分段函数：
        // 函数名来自被切分的那一侧（`lhs_left` / `rhs_right`）。
        // The piecewise adapter's `Polynomial` is its **result polynomial**, constant 0 and identical on both sides, so comparing it proves nothing. What distinguishes them is the
        // piecewise function it wraps, whose name comes from the side that was cut (`lhs_left` / `rhs_right`).
        var minimumAdapter = (Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>)unknown.MinIndexSymbol!;
        var maximumAdapter = (Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64>)unknown.MaxIndexSymbol!;
        UnivariateLinearPiecewiseFunction<Flt64> minimumPiecewise = minimumAdapter.Function
            .Should().BeOfType<Fuookami.Ospf.Core.Symbol.Function.UnivariateLinearPiecewiseFunction<Flt64>>().Subject;
        UnivariateLinearPiecewiseFunction<Flt64> maximumPiecewise = maximumAdapter.Function
            .Should().BeOfType<Fuookami.Ospf.Core.Symbol.Function.UnivariateLinearPiecewiseFunction<Flt64>>().Subject;
        // 左侧两点 (40000,10)-(60000,20) → 斜率 0.0005；右侧两点 (40000,50)-(60000,70) → 斜率 0.001。
        // 斜率是**分段函数本身**的可观测量，比适配器的结果多项式更能说明"这条线是从哪一侧切出来的"。
        // The left side's points (40000,10)-(60000,20) give slope 0.0005; the right side's (40000,50)-(60000,70) give 0.001. The slope is an observable of the **piecewise function
        // itself** and says which side was cut far better than the adapter's result polynomial.
        minimumPiecewise.Slopes[0].ToDouble().Should().BeApproximately(0.0005, 1e-12);
        maximumPiecewise.Slopes[0].ToDouble().Should().BeApproximately(0.001, 1e-12,
            "the maximum must be cut from the right side, not the left");
        unknown.MinIndexSymbol!.Name.Should().StartWith("min_index_");
        unknown.MaxIndexSymbol!.Name.Should().StartWith("max_index_");
    }

    /// <summary>
    /// **点数不足时明确失败** / Too few points fails loudly.
    /// </summary>
    /// <remarks>
    /// 分段函数至少要两点；一点时"折线"退化成常数而构造器会拒绝。静默产一个常数会把包络变成一个固定边界——比没有约束更危险，
    /// 因为它看起来像一个约束。
    ///
    /// A piecewise function needs two points; with one the polyline degenerates and the constructor rejects it. Silently producing a constant turns the envelope into a fixed
    /// boundary — more dangerous than no constraint, because it looks like one.
    /// </remarks>
    [Fact]
    public void Register_ShouldFailWithFewerThanTwoPoints() {
        (Envelope _, TotalWeight totalWeight, LinearMetaModel<Flt64> model) = Prepare(computedTotalWeight: null);
        var single = new EnvelopeSide("lhs", EnvelopeSideType.Left, new[] { new EnvelopePoint(40000.0, 10.0) });
        var rhs = new EnvelopeSide("rhs", EnvelopeSideType.Right, new[] {
            new EnvelopePoint(40000.0, 50.0),
            new EnvelopePoint(60000.0, 70.0),
        });
        var envelope = new Envelope(FlightPhase.TakeOff, "takeoff", single, rhs, totalWeight);

        // 断言**具体的报错**而不只是"失败了"：点数不足时应当由这里拦住并说明原因，而不是让分段函数构造器在下一层抛出一个笼统的
        // "构造失败"。两者都是 `IllegalArgument`，只有消息能把它们分开——而这条判据钉的正是"在这里拦住、并说清楚为什么"。
        // Assert the **specific message**, not merely "it failed": too few points should be caught here with a reason, rather than left to the piecewise constructor's generic
        // failure. Both are `IllegalArgument`, so only the message separates them — and "caught here, with the reason" is exactly what this criterion pins.
        Fuookami.Ospf.Utils.Functional.Result<Success, ErrorCode, Fuookami.Ospf.Utils.Error.Error<ErrorCode>> result = envelope.Register(model);
        result.IsOk.Should().BeFalse("a one-point side cannot be a polyline");
        envelope.MinIndexSymbol.Should().BeNull();
        envelope.MaxIndexSymbol.Should().BeNull();
        result.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Failed<
            Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>>()
            .Which.Error.Message.Should().Contain("两点");
    }
}
