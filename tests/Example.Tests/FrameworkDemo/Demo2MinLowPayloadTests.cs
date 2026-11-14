// 最小低甲板载荷与低载荷下界的判据 / Criteria for the minimum low-deck payload and its lower bound.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **最小低甲板载荷与低载荷下界（B588）** / The minimum low-deck payload and its lower bound (B588).
/// </summary>
/// <remarks>
/// 这是 `AirworthinessSecurity` 域的第一块实际逻辑（此前整个域只有三个 `Max*` 是真代码，其余全是"返回 `Ok` 什么也不做"的空壳）。
///
/// 两条容易做反的事：
///
/// 1. **这是下界**。`lower_estimate_payload ≥ min_low_payload`——下舱装得太少会让重心太靠前。写成上界能编译、能求解，而且大多数算例
///    在最优解上恰好看不出差别；它不报错，只是把一条安全约束变成了一条容量约束。
/// 2. **这里是插值，B584 的 `MaxLoadWeightLimit` 是阶跃**。两者都是基线的做法，差异来自数据：最大装载重量是按图表给出的阶梯规格，
///    最小低载荷是一条连续的边界曲线。不要因为"看着不一致"就改掉其中一个。
///
/// This is the first piece of real logic in the `AirworthinessSecurity` domain (until now only three `Max*` classes were real; the rest were shells returning `Ok` while doing
/// nothing). Two things are easy to get backwards: (1) it is a **lower** bound — too little in the lower hold puts the centre of gravity too far forward; an upper bound compiles,
/// solves, and usually does not bind at the optimum, it just turns a safety constraint into a capacity one. (2) this one **interpolates** while B584's `MaxLoadWeightLimit`
/// **steps**; both are the baseline's behaviour, the difference comes from the data.
/// </remarks>
public class Demo2MinLowPayloadTests {
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
            shape: new PositionShape(1.0, 1.0, 1.0),
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

    private static readonly MinLowPayloadPoint[] Table = {
        new(1000.0, 40000.0),
        new(2000.0, 50000.0),
        new(4000.0, 60000.0),
    };

    private static (Payload Payload, MinLowPayload MinLowPayload, TotalWeight TotalWeight, LinearMetaModel<Flt64> Model) Prepare(
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
        var minLowPayload = new MinLowPayload(Table, totalWeight);

        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();
        payload.Register(StowageMode.Predistribution, model).IsOk.Should().BeTrue();
        totalWeight.Register(model).IsOk.Should().BeTrue();
        return (payload, minLowPayload, totalWeight, model);
    }

    /// <summary>
    /// **中间是线性插值，两端是常数** / Linear in between, constant at the ends.
    /// </summary>
    /// <remarks>
    /// 四点成对：不高于首点用首点（1000），中间 45000 落在 40000—50000 之间线性插值得 1500（不是 1000 也不是 2000，
    /// 所以"阶跃"会被检出），55000 落在 50000—60000 之间得 3000，不低于末点用末点（4000）。
    ///
    /// Four points paired: at or below the first returns 1000; 45000 between 40000 and 50000 interpolates to 1500 (neither 1000 nor 2000, so a step is detected); 55000 between
    /// 50000 and 60000 gives 3000; at or above the last returns 4000.
    /// </remarks>
    [Fact]
    public void Interpolate_ShouldBeLinearInBetweenAndConstantAtTheEnds() {
        (Payload _, MinLowPayload model, TotalWeight _, LinearMetaModel<Flt64> _) = Prepare(computedTotalWeight: null);

        model.Interpolate(30000.0).Should().BeApproximately(1000.0, 1e-9, "below the first point");
        model.Interpolate(45000.0).Should().BeApproximately(1500.0, 1e-9, "linear between 40000 and 50000");
        model.Interpolate(45000.0).Should().NotBeApproximately(1000.0, 1e-9, "a step would stay at the lower point");
        model.Interpolate(55000.0).Should().BeApproximately(3000.0, 1e-9, "linear between 50000 and 60000");
        model.Interpolate(90000.0).Should().BeApproximately(4000.0, 1e-9, "above the last point");
    }

    /// <summary>
    /// **点按零油重排序，输入顺序不影响结果** / Points are sorted; input order does not change the answer.
    /// </summary>
    /// <remarks>
    /// 与"插值结果正确"成对：乱序传入后每一个采样点都给出与升序表相同的结果。不排序时 `Points[0]`／`Points[^1]` 这两条边界分支
    /// 会拿错点，而中间那段的相邻关系也会错位。
    ///
    /// Paired with "the interpolation is correct": shuffled input gives the same answer at every sample. Without sorting, the two boundary branches use the wrong points, and the
    /// neighbour pairing in the middle is off too.
    /// </remarks>
    [Fact]
    public void Points_ShouldBeSortedAndOrderIndependent() {
        (Payload payload, MinLowPayload sortedModel, TotalWeight totalWeight, LinearMetaModel<Flt64> model) = Prepare(computedTotalWeight: null);
        sortedModel.Points.Select(point => point.Zfw).Should().BeInAscendingOrder();

        var shuffled = new MinLowPayload(new[] { Table[2], Table[0], Table[1] }, totalWeight);
        shuffled.Points.Select(point => point.Zfw).Should().BeInAscendingOrder();
        foreach (double zfw in new[] { 30000.0, 40000.0, 45000.0, 50000.0, 55000.0, 60000.0, 90000.0 }) {
            shuffled.Interpolate(zfw).Should().BeApproximately(sortedModel.Interpolate(zfw), 1e-9,
                "the result must not depend on the order the table was given in");
        }
        _ = payload;
        _ = model;
    }

    /// <summary>
    /// **已知零油重时产一个常数符号** / A known zero-fuel weight yields a constant symbol.
    /// </summary>
    /// <remarks>
    /// 成对：给了 `computedTotalWeight[ZeroFuel] = 45000` 时符号是常数 1500 且没有单项式；不给时符号**含单项式**（因为它关于零油重估计
    /// 线性）。写成"永远是常数"会让第二条路上的约束降级成一个猜测。
    ///
    /// Paired: with `computedTotalWeight[ZeroFuel] = 45000` the symbol is the constant 1500 with no monomials; without it the symbol **carries monomials** (it is linear in the
    /// zero-fuel weight estimate). Always producing a constant would downgrade the constraint into a guess on the second path.
    /// </remarks>
    [Fact]
    public void Register_ShouldProduceAConstantWhenTheZeroFuelWeightIsKnown() {
        (Payload _, MinLowPayload known, TotalWeight _, LinearMetaModel<Flt64> knownModel) = Prepare(
            new Dictionary<FlightPhase, double> { [FlightPhase.ZeroFuel] = 45000.0 });
        known.Register(knownModel).IsOk.Should().BeTrue();
        LinearExpressionSymbol symbol = known.MinLowPayloadSymbol!;
        symbol.Name.Should().Be("min_low_payload");
        symbol.Polynomial.Monomials.Should().BeEmpty("the interpolated value is a number");
        symbol.Polynomial.Constant.ToDouble().Should().BeApproximately(1500.0, 1e-9);

        (Payload _, MinLowPayload unknown, TotalWeight _, LinearMetaModel<Flt64> unknownModel) = Prepare(computedTotalWeight: null);
        unknown.Register(unknownModel).IsOk.Should().BeTrue();
        unknown.MinLowPayloadSymbol!.Polynomial.Monomials.Should().NotBeEmpty(
            "without a known zero-fuel weight the bound follows the zero-fuel estimate");
    }

    /// <summary>
    /// **未知零油重时用首尾两点定斜率与截距** / Without a known weight, the first and last points fix slope and intercept.
    /// </summary>
    /// <remarks>
    /// 斜率 = (4000 − 1000) / (60000 − 40000) = 0.15；截距 = 1000 − 0.15 × 40000 = −5000。代入零油重估计得到一个与估计同形的表达式，
    /// 所以它的**常数项**必须等于 `0.15 × 零油重估计的常数 − 5000`。用两端点而不是最小二乘，也不重跑整张表——这条钉住的是"用哪两点"。
    ///
    /// Slope = (4000 − 1000) / (60000 − 40000) = 0.15; intercept = 1000 − 0.15 × 40000 = −5000. Substituted into the estimate this gives an expression shaped like the estimate,
    /// so its **constant term** must equal `0.15 × estimate constant − 5000`. The first and last points rather than a least-squares fit, and no full-table interpolation — this
    /// pins "which two points".
    /// </remarks>
    [Fact]
    public void Register_ShouldUseTheFirstAndLastPointsForTheLinearForm() {
        (Payload _, MinLowPayload model, TotalWeight totalWeight, LinearMetaModel<Flt64> meta) = Prepare(computedTotalWeight: null);
        model.Register(meta).IsOk.Should().BeTrue();

        LinearExpressionSymbol symbol = model.MinLowPayloadSymbol!;
        double estimateConstant = totalWeight.EstimateTotalWeight(FlightPhase.ZeroFuel)!.Polynomial.Constant.ToDouble();
        symbol.Polynomial.Constant.ToDouble().Should().BeApproximately(0.15 * estimateConstant - 5000.0, 1e-6,
            "slope 0.15 and intercept −5000 come from the first and last points");

        // 每个单项式的系数都必须是零油重估计里对应系数的 0.15 倍。
        // Every monomial coefficient must be 0.15 times the matching coefficient of the zero-fuel estimate.
        symbol.Polynomial.Monomials.Should().HaveCount(
            totalWeight.EstimateTotalWeight(FlightPhase.ZeroFuel)!.Polynomial.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in symbol.Polynomial.Monomials) {
            LinearMonomial<Flt64> source = totalWeight.EstimateTotalWeight(FlightPhase.ZeroFuel)!.Polynomial.Monomials
                .Single(candidate => ReferenceEquals(candidate.Symbol, monomial.Symbol));
            monomial.Coefficient.ToDouble().Should().BeApproximately(0.15 * source.Coefficient.ToDouble(), 1e-6);
        }
    }

    /// <summary>
    /// **低载荷约束是下界，不是上界** / The low-payload row is a lower bound, not an upper one.
    /// </summary>
    /// <remarks>
    /// 断言左端是**下舱推荐业载**、右端是**最小低载荷符号本身**（`BeSameAs`），且比较符是 `GE`。写成 `LE` 能编译能求解，大多数算例在最优解上
    /// 也看不出差别——它不报错，只是把一条安全约束变成了一条容量约束。
    ///
    /// Asserts the left side is the **lower-hold recommended payload**, the right side is the **minimum symbol itself** (`BeSameAs`), and the operator is `GE`. An `LE` compiles,
    /// solves, and usually does not bind at the optimum — nothing reports it, a safety constraint just became a capacity one.
    /// </remarks>
    [Fact]
    public void LowPayloadLimit_ShouldBeALowerBoundOnTheLowerHold() {
        (Payload payload, MinLowPayload minLowPayload, TotalWeight _, LinearMetaModel<Flt64> model) = Prepare(
            new Dictionary<FlightPhase, double> { [FlightPhase.ZeroFuel] = 45000.0 });
        minLowPayload.Register(model).IsOk.Should().BeTrue();
        var limit = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits.LowPayloadLimit(
            payload, minLowPayload);
        limit.Invoke(model).IsOk.Should().BeTrue();

        LinearInequalityConstraint<Flt64> row = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "low_payload_limit");
        row.Inequality.Comparison.Should().Be(Fuookami.Ospf.Math.Symbol.Inequality.Comparison.GE,
            "too little in the lower hold puts the centre of gravity too far forward");
        row.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
            .Should().BeEquivalentTo(payload.LowEstimatePayload!.Polynomial.Monomials.Select(monomial => monomial.Symbol),
                "the left side is the lower-hold recommended payload, not the main-deck one");
        row.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(1500.0, 1e-9);
    }

    /// <summary>
    /// **缺符号时明确失败** / Missing symbols fail loudly.
    /// </summary>
    /// <remarks>
    /// 空壳版本对任何输入都返回 `Ok`。那种失败方式最坏：调用方以为约束加上了，而模型里一行都没有，求解"成功"地忽略了整条安全要求。
    ///
    /// The shell returned `Ok` for any input. That failure mode is the worst: the caller believes the row is there while the model holds none, and the solve "successfully"
    /// ignores a whole safety requirement.
    /// </remarks>
    [Fact]
    public void LowPayloadLimit_ShouldFailWithoutRegisteredSymbols() {
        (Payload payload, MinLowPayload minLowPayload, TotalWeight _, LinearMetaModel<Flt64> model) = Prepare(
            new Dictionary<FlightPhase, double> { [FlightPhase.ZeroFuel] = 45000.0 });
        var limit = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits.LowPayloadLimit(
            payload, minLowPayload);

        limit.Invoke(model).IsOk.Should().BeFalse("the minimum symbol was never registered");
        model.Constraints.Select(constraint => constraint.Name).Should().NotContain("low_payload_limit");

        // 第二条分支：业载**从未注册**时 `LowEstimatePayload` 为 `null`。上面那条路里 `Prepare` 已经注册了业载，所以它只覆盖了
        // "最小低载荷缺"，没覆盖"业载缺"——两个失败分支必须各测一次，否则其中一个可以是 `return Ok` 而没人发现。
        // The second branch: when the payload was **never registered**, `LowEstimatePayload` is `null`. The path above goes through `Prepare`, which does register the payload,
        // so it only covers "minimum missing" and not "payload missing" — both failure branches need their own case, or one can be `return Ok` undetected.
        (Payload barePayload, MinLowPayload bareMinimum, TotalWeight bareTotalWeight, LinearMetaModel<Flt64> bareModel) = Prepare(
            new Dictionary<FlightPhase, double> { [FlightPhase.ZeroFuel] = 45000.0 });
        bareMinimum.Register(bareModel).IsOk.Should().BeTrue();
        barePayload.LowEstimatePayload.Should().NotBeNull("Prepare does register the payload");
        var payloadOnlyMissing = new Payload(
            new[] { MakePosition("P1", 1UL, PositionLocation.NormalMainLocation) },
            new Load(
                new[] { MakeItem("I1") },
                new[] { MakePosition("P1", 1UL, PositionLocation.NormalMainLocation) },
                new Stowage(
                    new[] { MakeItem("I1") },
                    new[] { MakePosition("P1", 1UL, PositionLocation.NormalMainLocation) }),
                withMultiLoadingSchema: true),
            5000.0,
            20000.0);
        payloadOnlyMissing.LowEstimatePayload.Should().BeNull();
        var bareLimit = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits.LowPayloadLimit(
            payloadOnlyMissing, bareMinimum);
        bareLimit.Invoke(bareModel).IsOk.Should().BeFalse("the lower-hold payload symbol was never registered");
        bareModel.Constraints.Select(constraint => constraint.Name).Should().NotContain("low_payload_limit");
        _ = bareTotalWeight;
    }

    /// <summary>
    /// **空插值表是错误** / An empty table is an error.
    /// </summary>
    /// <remarks>
    /// 空表会让 `Points[0]` 越界或让插值永远返回 0。静默接受会得到一个"最小低载荷恒为 0"的规格，它不会崩，只是让这条安全约束完全不起作用。
    ///
    /// An empty table would index past `Points[0]` or interpolate to zero forever. Accepting it silently yields "the minimum low payload is always zero" — nothing crashes, the
    /// safety constraint simply stops doing anything.
    /// </remarks>
    [Fact]
    public void Constructor_ShouldRejectAnEmptyTable() {
        (Payload _, MinLowPayload _, TotalWeight totalWeight, LinearMetaModel<Flt64> _) = Prepare(computedTotalWeight: null);
        Action act = () => _ = new MinLowPayload(Array.Empty<MinLowPayloadPoint>(), totalWeight);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
