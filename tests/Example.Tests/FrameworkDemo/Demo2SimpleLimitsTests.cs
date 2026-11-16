// 三个简单上限（业载、总重、CLIM）的判据 / Criteria for the three simple bounds (payload, total weight, CLIM).

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;
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
/// **三个简单上限** / The three simple bounds.
/// </summary>
/// <remarks>
/// 业载、总重、CLIM 三条限制都是"一行或两行不等式"，没有插值也没有分支表。它们的风险全在**细节**上：
///
/// | 限制 | 容易做错的地方 |
/// | --- | --- |
/// | `PayloadLimit` | 只有下限带 `_minimum` 后缀；`minPayload` 是构造参数不是 `Payload` 字段 |
/// | `TotalWeightLimit` | 缺表达式是失败、缺上限是跳过（故意不同）；遍历三个阶段不只起飞 |
/// | `CLIMLimit` | 右端是 0 不是 `maxCLIM`；下界是 `+` 不是 `−` |
///
/// Three one-or-two-row inequalities, no interpolation, no branch table — all the risk is in the details tabulated above.
/// </remarks>
public class Demo2SimpleLimitsTests {
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
            coordinate: new PositionCoordinate(500.0, 900.0, 40.0, 60.0),
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

    private sealed class Rig
    {
        public Rig(
            bool wideBody = false,
            bool registerTotalWeight = true,
            bool registerPayload = true,
            IReadOnlyDictionary<FlightPhase, double>? computedTotalWeight = null) {
            var items = new List<Item> { MakeItem("I1") };
            var positions = new List<StowagePosition> {
                MakePosition("P1", 1UL, PositionLocation.NormalMainLocation),
                MakePosition("P2", 2UL, PositionLocation.LowForwardNotBulkLocation),
            };
            Positions = positions;
            var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
            Aircraft = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
            Formula = new Formula(Aircraft, 100.0, 20.0, 120.0, 5000.0, 0.0);
            Load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: Aircraft, formula: Formula);
            Model = new LinearMetaModel<Flt64>();
            stowage.Register(Model).IsOk.Should().BeTrue();
            Load.Register(Model).IsOk.Should().BeTrue();

            Payload = new Payload(positions, Load, plannedPayload: 5000.0, maxPayload: 20000.0);
            if (registerPayload) {
                Payload.Register(StowageMode.FullLoad, Model).IsOk.Should().BeTrue();
            }

            var fuselage = new Fuselage(new Liferaft(50.0, 12.0), Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);
            var fuel = new Dictionary<FlightPhase, FuelConstant> {
                [FlightPhase.TakeOff] = new FuelConstant(0.8, 8000.0, 5.0),
                [FlightPhase.Landing] = new FuelConstant(0.8, 2000.0, 2.0),
            };
            TotalWeight = new TotalWeight(
                fuselage,
                fuel,
                Payload,
                maxTotalWeight: new Dictionary<FlightPhase, double> {
                    [FlightPhase.TakeOff] = 70000.0,
                    [FlightPhase.Landing] = 65000.0,
                },
                computedTotalWeight: computedTotalWeight);
            if (registerTotalWeight) {
                TotalWeight.Register(Model).IsOk.Should().BeTrue();
            }
            Torque = new Torque(fuselage, fuel, Formula, positions, Load, wideBody);
        }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public AircraftModel Aircraft { get; }

        public Formula Formula { get; }

        public Load Load { get; }

        public LinearMetaModel<Flt64> Model { get; }

        public Payload Payload { get; }

        public TotalWeight TotalWeight { get; }

        public Torque Torque { get; }
    }

    private static LinearInequalityConstraint<Flt64> Row(LinearMetaModel<Flt64> model, string name) =>
        model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == name);

    /// <summary>
    /// **业载：上限无后缀、下限带 `_minimum`，方向相反** / Payload: the upper row has no suffix, the lower carries `_minimum`, and the directions differ.
    /// </summary>
    /// <remarks>
    /// 上限 20000（`LE`）、下限 3000（`GE`），左端都是 `estimate_payload`。上下限写反会让求解器要求业载既 ≤20000 又 ≥3000——顺序错时它照样可行，
    /// 只是约束的是别的东西。
    ///
    /// Upper 20000 (`LE`), lower 3000 (`GE`), both on `estimate_payload`. Swapping them still yields a feasible model, it just constrains something else.
    /// </remarks>
    [Fact]
    public void PayloadLimit_ShouldBoundBothSidesWithTheBaselineNames() {
        var rig = new Rig();
        var limit = new PayloadLimit(rig.Payload, minPayload: 3000.0);
        limit.Invoke(rig.Model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().Equal("payload_limit", "payload_limit_minimum");
        Row(rig.Model, "payload_limit").Inequality.Comparison.Should().Be(Comparison.LE);
        Row(rig.Model, "payload_limit").Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(20000.0, 1e-9);
        Row(rig.Model, "payload_limit_minimum").Inequality.Comparison.Should().Be(Comparison.GE);
        Row(rig.Model, "payload_limit_minimum").Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(3000.0, 1e-9);

        Row(rig.Model, "payload_limit").Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
            .Should().BeEquivalentTo(rig.Payload.EstimatePayload!.Polynomial.Monomials.Select(monomial => monomial.Symbol));
    }

    /// <summary>
    /// **业载：下限用的是构造参数，不是计划业载** / Payload: the lower bound is the constructor argument, not the planned payload.
    /// </summary>
    /// <remarks>
    /// `Payload.PlannedPayload` 是 5000，构造参数给 3000。若误用计划业载，下限会变成 5000——数值合理，判据才看得出。
    ///
    /// `PlannedPayload` is 5000 while the argument is 3000. Using the planned payload would make the bound 5000 — a plausible number only a criterion can catch.
    /// </remarks>
    [Fact]
    public void PayloadLimit_ShouldUseTheArgumentNotThePlannedPayload() {
        var rig = new Rig();
        var limit = new PayloadLimit(rig.Payload, minPayload: 3000.0);
        limit.Invoke(rig.Model).IsOk.Should().BeTrue();

        rig.Payload.PlannedPayload.Should().BeApproximately(5000.0, 1e-9, "the fixture's planned payload");
        Row(rig.Model, "payload_limit_minimum").Inequality.Rhs.Constant.ToDouble()
            .Should().BeApproximately(3000.0, 1e-9)
            .And.NotBeApproximately(rig.Payload.PlannedPayload, 1e-9,
                "the lower bound comes from the argument, not from the planned payload");
    }

    /// <summary>
    /// **业载：缺符号时明确失败** / Payload: a missing symbol fails loudly.
    /// </summary>
    /// <remarks>
    /// 空壳对任何输入都返回 `Ok`。缺符号时报成功会让调用方以为业载受约束，而模型里一行都没有。
    ///
    /// The shell returned `Ok` for any input. Reporting success while the symbol is missing tells the caller payload is bounded when no row exists.
    /// </remarks>
    [Fact]
    public void PayloadLimit_ShouldFailWithoutTheEstimateSymbol() {
        var rig = new Rig(registerPayload: false, registerTotalWeight: false);
        var limit = new PayloadLimit(rig.Payload, minPayload: 3000.0);

        limit.Invoke(rig.Model).IsOk.Should().BeFalse();
        limit.LastAddedNames.Should().BeEmpty();
    }

    /// <summary>
    /// **总重：有上限的阶段各一行，没上限的跳过** / Total weight: one row per phase that has a maximum, skipped otherwise.
    /// </summary>
    /// <remarks>
    /// 起飞 70000、着陆 65000、零油重**没有**上限 → 恰好两行，名字带阶段。零油重没有上限是合法配置，不能当成错误。
    ///
    /// Take-off 70000, landing 65000, zero fuel **without** a maximum → exactly two rows with the phase in the name. A phase without a maximum is legitimate.
    /// </remarks>
    [Fact]
    public void TotalWeightLimit_ShouldAddARowPerPhaseWithAMaximum() {
        var rig = new Rig();
        var limit = new TotalWeightLimit(rig.TotalWeight);
        limit.Invoke(rig.Model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().Equal("total_weight_limit_takeoff", "total_weight_limit_landing");
        Row(rig.Model, "total_weight_limit_takeoff").Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(70000.0, 1e-9);
        Row(rig.Model, "total_weight_limit_landing").Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(65000.0, 1e-9);
        Row(rig.Model, "total_weight_limit_takeoff").Inequality.Comparison.Should().Be(Comparison.LE);
    }

    /// <summary>
    /// **总重：缺表达式是失败，缺上限是跳过** / Total weight: a missing expression fails, a missing maximum is skipped.
    /// </summary>
    /// <remarks>
    /// 成对的两条：`registerTotalWeight: false` 时两阶段都缺表达式 → **失败**；有表达式但零油重没上限 → 上一判据已钉住"跳过"。
    /// 若把两者都写成跳过，集成错误会静默；若都写成失败，"这个阶段不设限"就无法表达。
    ///
    /// The pair: with `registerTotalWeight: false` both phases lack an expression → **failure**; with expressions but no zero-fuel maximum the previous criterion pins the skip.
    /// Skipping both hides integration errors; failing both makes "no limit for this phase" inexpressible.
    /// </remarks>
    [Fact]
    public void TotalWeightLimit_ShouldFailWhenTheExpressionIsMissing() {
        var rig = new Rig(registerTotalWeight: false);
        var limit = new TotalWeightLimit(rig.TotalWeight);

        limit.Invoke(rig.Model).IsOk.Should().BeFalse();
        limit.LastAddedNames.Should().BeEmpty();
    }

    /// <summary>
    /// **CLIM：右端是 0，下界用加号** / CLIM: the right side is zero and the lower bound adds.
    /// </summary>
    /// <remarks>
    /// 上界 `clim − maxCLIM ≤ 0`、下界 `clim + maxCLIM ≥ 0`。三点成对：
    ///
    /// 1. 两行的右端常数都是 **0**（不是 `maxCLIM`）；
    /// 2. 上界的 `maxCLIM` 部分系数是**负**、下界是**正**；
    /// 3. 两行的 CLIM 部分系数相同（同一族符号）。
    ///
    /// 写成 `clim ≤ maxCLIM` / `clim ≥ maxCLIM` 会把可行域缩成一个点，求解器报"不可行"——看起来像数据问题。
    ///
    /// Three paired points: both right sides are **zero** (not `maxCLIM`); the `maxCLIM` part is **negative** in the upper row and **positive** in the lower; the CLIM part is
    /// identical. Writing `clim ≤ maxCLIM` / `clim ≥ maxCLIM` collapses the feasible region to a point and the solver reports "infeasible", which reads like a data problem.
    /// </remarks>
    [Fact]
    public void CLIMLimit_ShouldCompareAgainstZeroWithOppositeSigns() {
        var rig = new Rig(wideBody: true);
        rig.Torque.Register(rig.Model).IsOk.Should().BeTrue();
        var maxClim = new MaxCLIM(
            new[] { new MaxClimPoint(40000.0, 8.0), new MaxClimPoint(60000.0, 4.0) },
            rig.TotalWeight);
        maxClim.Register(rig.Model).IsOk.Should().BeTrue();

        var limit = new CLIMLimit(rig.Torque, maxClim);
        limit.Invoke(rig.Model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().Equal("max_clim_limit_ub", "max_clim_limit_lb");
        LinearInequalityConstraint<Flt64> upper = Row(rig.Model, "max_clim_limit_ub");
        LinearInequalityConstraint<Flt64> lower = Row(rig.Model, "max_clim_limit_lb");
        upper.Inequality.Comparison.Should().Be(Comparison.LE);
        lower.Inequality.Comparison.Should().Be(Comparison.GE);
        upper.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9, "the right side is zero");
        lower.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);

        // 本 fixture 的 TotalWeight 没有 computed 值，所以 `maxCLIM` 走的是**线性支**（关于起飞总重估计），不是常数。
        // 因此不能只比常数项：把两行的左端**逐项**与 `clim ∓ maxCLIM` 对照，符号才真正被钉住。
        // This fixture's TotalWeight has no computed value, so `maxCLIM` takes the **linear** branch over the take-off estimate, not the constant one. Comparing constants alone
        // would not do: the two left sides are compared term by term against `clim ∓ maxCLIM`, which is what actually pins the signs.
        maxClim.MaxClimSymbol!.Polynomial.Monomials.Should().NotBeEmpty("the estimate branch is linear");
        AssertMatches(upper.Inequality.Lhs, clim: rig.Torque.ClimSymbol!.Polynomial, maximum: maxClim.MaxClimSymbol!.Polynomial, subtract: true);
        AssertMatches(lower.Inequality.Lhs, clim: rig.Torque.ClimSymbol!.Polynomial, maximum: maxClim.MaxClimSymbol!.Polynomial, subtract: false);
        upper.Inequality.Lhs.Constant.ToDouble()
            .Should().NotBeApproximately(lower.Inequality.Lhs.Constant.ToDouble(), 1e-9,
                "the two rows differ in the maxCLIM sign");
    }

    /// <summary>
    /// **CLIM：窄体机上调用是调用方错误** / CLIM: invoking it on a narrow body is a caller error.
    /// </summary>
    /// <remarks>
    /// 窄体机不注册 CLIM。若这里静默成功，调用方会以为横向不平衡受约束，而模型里没有。
    ///
    /// **`MaxCLIM` 必须先注册成功**：否则"最大 CLIM 缺符号"那道守卫也会拦住，两条守卫里只要有一条还在，判据就绿——
    /// 这条变异（把 CLIM 符号换成横向力矩）第一次就是这样存活的。先让第二个守卫不触发，才只剩 CLIM 这一条在拦。
    ///
    /// Narrow bodies register no CLIM (pinned). Succeeding silently would tell the caller lateral imbalance is bounded when nothing is in the model.
    /// **`MaxCLIM` must register successfully first**: otherwise the "maximum CLIM missing" guard also fires and the criterion stays green while either guard survives — which is
    /// exactly how a mutation (substituting the lateral torque for the CLIM symbol) survived the first time. Disarming the second guard leaves only the CLIM one to catch it.
    /// </remarks>
    [Fact]
    public void CLIMLimit_ShouldFailOnNarrowBodyAircraft() {
        var rig = new Rig(wideBody: false);
        rig.Torque.Register(rig.Model).IsOk.Should().BeTrue();
        rig.Torque.ClimSymbol.Should().BeNull("the narrow body registers no CLIM");
        rig.Torque.LateralTorqueSymbol.Should().NotBeNull("but the lateral torque does exist");
        var maxClim = new MaxCLIM(new[] { new MaxClimPoint(40000.0, 8.0) }, rig.TotalWeight);
        maxClim.Register(rig.Model).IsOk.Should().BeTrue("the maximum CLIM must be in place so only the CLIM guard can fire");

        var limit = new CLIMLimit(rig.Torque, maxClim);
        limit.Invoke(rig.Model).IsOk.Should().BeFalse();
        limit.LastAddedNames.Should().BeEmpty();
    }

    /// <summary>
    /// **CLIM：最大 CLIM 缺符号时明确失败** / CLIM: a missing maximum symbol fails loudly.
    /// </summary>
    /// <remarks>
    /// 与"窄体机失败"成对：两个失败分支各测一次。`MaxCLIM.Register` 没跑过时它没有符号。
    ///
    /// Paired with the narrow-body case so each failure branch has its own criterion.
    /// </remarks>
    [Fact]
    public void CLIMLimit_ShouldFailWithoutTheMaximumSymbol() {
        var rig = new Rig(wideBody: true);
        rig.Torque.Register(rig.Model).IsOk.Should().BeTrue();
        var maxClim = new MaxCLIM(new[] { new MaxClimPoint(40000.0, 8.0) }, rig.TotalWeight);

        var limit = new CLIMLimit(rig.Torque, maxClim);
        limit.Invoke(rig.Model).IsOk.Should().BeFalse("MaxCLIM.Register was never called");
        limit.LastAddedNames.Should().BeEmpty();
    }

    /// <summary>
    /// **最大 CLIM 两端都夹住** / The maximum CLIM clamps at both ends.
    /// </summary>
    /// <remarks>
    /// 与 `MinLowPayload` 一致。五点成对：
    ///
    /// | 起飞重量 | 期望 | 说明 |
    /// | --- | --- | --- |
    /// | 30000 | 8 | 低于首点 → 夹住 |
    /// | 40000 | 8 | 首点 |
    /// | 50000 | 6 | 中点（8 与 4 的插值） |
    /// | 60000 | 4 | 末点 |
    /// | 80000 | **4** | 高于末点 → **夹住**，不是按斜率外推到 0 |
    ///
    /// 若上端外推，80000 会给 0；若下端不夹，30000 会给 10。**两端都夹**——循环形状看起来像上端外推，实际不会。
    ///
    /// Consistent with `MinLowPayload`. Five paired points as tabulated. Extrapolating the top would give 0 at 80000; not clamping the bottom would give 10 at 30000. **The code
    /// clamps both ends** — the loop shape merely looks like an upper extrapolation.
    /// </remarks>
    [Fact]
    public void MaxCLIM_ShouldClampAtBothEnds() {
        var rig = new Rig();
        var maxClim = new MaxCLIM(
            new[] { new MaxClimPoint(40000.0, 8.0), new MaxClimPoint(60000.0, 4.0) },
            rig.TotalWeight);

        maxClim.At(30000.0).Should().BeApproximately(8.0, 1e-9, "below the first point it clamps");
        maxClim.At(40000.0).Should().BeApproximately(8.0, 1e-9);
        maxClim.At(50000.0).Should().BeApproximately(6.0, 1e-9, "the midpoint of 8 and 4");
        maxClim.At(60000.0).Should().BeApproximately(4.0, 1e-9);
        maxClim.At(80000.0).Should().BeApproximately(4.0, 1e-9, "above the last point it clamps");
        maxClim.At(80000.0).Should().NotBeApproximately(0.0, 1e-9,
            "extrapolating the last slope would give 0");
    }

    /// <summary>
    /// 逐项比对 `clim ∓ maxCLIM` / Compare `clim ∓ maxCLIM` term by term.
    /// </summary>
    /// <param name="actual">实际左端 / The actual left side.</param>
    /// <param name="clim">CLIM 多项式 / The CLIM polynomial.</param>
    /// <param name="maximum">最大 CLIM 多项式 / The maximum CLIM polynomial.</param>
    /// <param name="subtract">是否相减 / Whether to subtract.</param>
    private static void AssertMatches(
        Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> actual,
        Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> clim,
        Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> maximum,
        bool subtract) {
        var expected = new Dictionary<object, double>();
        foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in clim.Monomials) {
            expected[monomial.Symbol] = monomial.Coefficient.ToDouble();
        }
        foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in maximum.Monomials) {
            double signed = subtract ? -monomial.Coefficient.ToDouble() : monomial.Coefficient.ToDouble();
            expected[monomial.Symbol] = expected.TryGetValue(monomial.Symbol, out double existing)
                ? existing + signed
                : signed;
        }
        expected = expected.Where(pair => pair.Value != 0.0).ToDictionary(pair => pair.Key, pair => pair.Value);

        // 两边都**按符号求和**再比：多项式不合并同符号的单项式（`AddMonomial` 只是追加），所以直接比条数会因为
        // clim 与 maxCLIM 共享装载符号而失配。求和之后比的才是"每个变量前的系数"。
        // Both sides are **summed by symbol** before comparing: the polynomial does not merge monomials of the same symbol (`AddMonomial` only appends), so comparing counts
        // fails whenever clim and maxCLIM share load symbols. Summing compares what matters — the coefficient in front of each variable.
        var actualBySymbol = new Dictionary<object, double>();
        foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in actual.Monomials) {
            double value = monomial.Coefficient.ToDouble();
            actualBySymbol[monomial.Symbol] = actualBySymbol.TryGetValue(monomial.Symbol, out double existing)
                ? existing + value
                : value;
        }
        actualBySymbol = actualBySymbol.Where(pair => pair.Value != 0.0).ToDictionary(pair => pair.Key, pair => pair.Value);
        actualBySymbol.Keys.Should().BeEquivalentTo(expected.Keys);
        foreach (KeyValuePair<object, double> pair in expected) {
            actualBySymbol[pair.Key].Should().BeApproximately(pair.Value, 1e-9);
        }
        double expectedConstant = clim.Constant.ToDouble()
            + (subtract ? -maximum.Constant.ToDouble() : maximum.Constant.ToDouble());
        actual.Constant.ToDouble().Should().BeApproximately(expectedConstant, 1e-9);
    }

    /// <summary>
    /// **最大 CLIM 已知总重时产常数，且那个数来自插值** / With a known total the maximum CLIM is a constant taken from the interpolation.
    /// </summary>
    /// <remarks>
    /// 上面几条判据的 fixture 都没有 computed 总重，所以走的全是**线性支**——"已知 → 常数"那条分支从未被执行。这条专门给
    /// `computedTotalWeight[TakeOff] = 50000`，钉住常数等于 `At(50000) = 6` 而**不是首点的 8**，并且**没有单项式**。
    ///
    /// 成对：同一条点表在线性支上**有**单项式。只钉"是常数"会让"永远返回首点"也通过。
    ///
    /// The criteria above all run without a computed total, so they only exercise the **linear** branch and the "known → constant" branch never runs. This one sets
    /// `computedTotalWeight[TakeOff] = 50000` and pins the constant to `At(50000) = 6`, **not** the first point's 8, with **no monomials**. Paired: the same table on the linear
    /// branch **does** have monomials. Pinning "it is a constant" alone would let "always the first point" pass.
    /// </remarks>
    [Fact]
    public void MaxCLIM_ShouldInterpolateAConstantWhenTheTotalWeightIsKnown() {
        var known = new Rig(computedTotalWeight: new Dictionary<FlightPhase, double> {
            [FlightPhase.TakeOff] = 50000.0,
        });
        var maxClim = new MaxCLIM(
            new[] { new MaxClimPoint(40000.0, 8.0), new MaxClimPoint(60000.0, 4.0) },
            known.TotalWeight);
        maxClim.Register(known.Model).IsOk.Should().BeTrue();

        maxClim.MaxClimSymbol!.Name.Should().Be("max_clim");
        maxClim.MaxClimSymbol!.Polynomial.Monomials.Should().BeEmpty("a known total gives a number");
        maxClim.MaxClimSymbol!.Polynomial.Constant.ToDouble().Should().BeApproximately(6.0, 1e-9,
            "50000 sits halfway between 40000 and 60000");
        maxClim.MaxClimSymbol!.Polynomial.Constant.ToDouble().Should().NotBeApproximately(8.0, 1e-9,
            "the first point's value would mean the interpolation never ran");

        var unknown = new Rig();
        var linear = new MaxCLIM(
            new[] { new MaxClimPoint(40000.0, 8.0), new MaxClimPoint(60000.0, 4.0) },
            unknown.TotalWeight);
        linear.Register(unknown.Model).IsOk.Should().BeTrue();
        linear.MaxClimSymbol!.Polynomial.Monomials.Should().NotBeEmpty("the estimate branch is linear");
    }

    /// <summary>
    /// **最大 CLIM 的点表按起飞重量排序** / The maximum CLIM sorts its points.
    /// </summary>
    /// <remarks>
    /// 乱序点表给出与升序表相同的四个结果。不排序时插值会取错相邻对。
    ///
    /// A shuffled table gives the same four answers as a sorted one; without sorting the interpolation pairs the wrong neighbours.
    /// </remarks>
    [Fact]
    public void MaxCLIM_ShouldSortItsPoints() {
        var rig = new Rig();
        var shuffled = new MaxCLIM(
            new[] { new MaxClimPoint(60000.0, 4.0), new MaxClimPoint(40000.0, 8.0) },
            rig.TotalWeight);

        shuffled.Points.Select(point => point.Tow).Should().BeInAscendingOrder();
        shuffled.At(30000.0).Should().BeApproximately(8.0, 1e-9);
        shuffled.At(50000.0).Should().BeApproximately(6.0, 1e-9);
        shuffled.At(80000.0).Should().BeApproximately(4.0, 1e-9);
    }
}
