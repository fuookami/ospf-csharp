// 纵向力矩与横向不平衡的判据 / Criteria for the longitudinal moment and lateral imbalance limits.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **纵向力矩区间与横向不平衡** / The longitudinal moment interval and lateral imbalance.
/// </summary>
/// <remarks>
/// 两个限制都只有两行/阶段，风险全在**命名与顺序**上：
///
/// | 限制 | 顺序 | 后缀 |
/// | --- | --- | --- |
/// | `LongitudinalMomentLimit` | 每阶段先 `_minimum` 后 `_maximum` | 两个都带 |
/// | `LateralImbalanceLimit` | 先 `_maximum` 后 `_minimum` | 两个都带 |
///
/// 两者都与 `PayloadLimit`（先上限、**上限不带后缀**）不同。三个文件的对称性各不相同，谁也不能照着谁写。
///
/// Both limits are two rows per phase and the risk is entirely in **naming and order**, tabulated above. Neither matches `PayloadLimit` (upper first, **no suffix on the upper
/// row**). The three files are not symmetric with each other and none can be written from another's shape.
/// </remarks>
public class Demo2MomentImbalanceTests {
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
        public Rig(bool registerTorque = true) {
            var items = new List<Item> { MakeItem("I1") };
            var positions = new List<StowagePosition> { MakePosition("P1", 1UL) };
            Positions = positions;
            var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
            AircraftType type = AircraftType.B747;
            Aircraft = new AircraftModel(type, type.ToString(), new AircraftMinorModel(type.ToString()));
            var formula = new Formula(Aircraft, 100.0, 20.0, 120.0, 5000.0, 0.0);
            Load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: Aircraft, formula: formula);
            var payload = new Payload(positions, Load, plannedPayload: 5000.0, maxPayload: 20000.0);
            var fuselage = new Fuselage(null, Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);
            // 燃油表**必须让阶段间有差别**：空表时三个阶段都只有干使用力矩，多项式完全相同，
            // "三个阶段各绑自己的力矩"这条判据就无法区分（实测报出 `not to be... TakeOff's` 失败）。
            // 给起飞与着陆不同的燃油指数，三者的常数项互不相同。
            // The fuel map **must make the phases differ**: with an empty map all three phases carry only the dry-operating torque and their polynomials are identical, so "each phase
            // binds its own moment" cannot be distinguished (it failed with `not to be... TakeOff's`). Distinct indices for take-off and landing give three distinct constants.
            var fuel = new Dictionary<FlightPhase, FuelConstant> {
                [FlightPhase.TakeOff] = new FuelConstant(Density: 0.8, Weight: 1000.0, Index: 1000.0),
                [FlightPhase.Landing] = new FuelConstant(Density: 0.8, Weight: 2000.0, Index: 2000.0),
            };
            TotalWeight = new TotalWeight(fuselage, fuel, payload);
            Torque = new Torque(
                fuselage,
                fuel,
                formula,
                positions,
                Load,
                wideBody: true);

            Model = new LinearMetaModel<Flt64>();
            stowage.Register(Model).IsOk.Should().BeTrue();
            Load.Register(Model).IsOk.Should().BeTrue();
            payload.Register(StowageMode.FullLoad, Model).IsOk.Should().BeTrue();
            TotalWeight.Register(Model).IsOk.Should().BeTrue();
            if (registerTorque) {
                Torque.Register(Model).IsOk.Should().BeTrue();
            }
        }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public AircraftModel Aircraft { get; }

        public Load Load { get; }

        public TotalWeight TotalWeight { get; }

        public Torque Torque { get; }

        public LinearMetaModel<Flt64> Model { get; }
    }

    /// <summary>
    /// **每阶段先 `_minimum` 后 `_maximum`，两个都带后缀** / Per phase `_minimum` comes first and both carry the suffix.
    /// </summary>
    /// <remarks>
    /// 三阶段 × 两行 = 6 个名字，逐字断言。**顺序也断言**：`PayloadLimit` 是先上限且上限不带后缀，
    /// 照着它写会得到 3 个名字（上限无后缀）加 3 个 `_minimum`，这条会红。
    ///
    /// Three phases times two rows is six names, pinned verbatim, **order included**. Writing from `PayloadLimit`'s shape yields three unsuffixed names plus three `_minimum`
    /// rows and turns this red.
    /// </remarks>
    [Fact]
    public void LongitudinalMomentLimit_ShouldNameBothRowsForEveryPhase() {
        var rig = new Rig();
        var limit = new LongitudinalMomentLimit(rig.Torque, 4000.0, 6000.0, "target_longitudinal_moment_limit");

        limit.Invoke(rig.Model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().Equal(new[] {
            "target_longitudinal_moment_limit_zerofuel_minimum",
            "target_longitudinal_moment_limit_zerofuel_maximum",
            "target_longitudinal_moment_limit_takeoff_minimum",
            "target_longitudinal_moment_limit_takeoff_maximum",
            "target_longitudinal_moment_limit_landing_minimum",
            "target_longitudinal_moment_limit_landing_maximum",
        }, "the phase name is lowercased without separators and both rows are suffixed, lower first");
    }

    /// <summary>
    /// **区间的两端真的绑在该阶段的力矩上** / Both ends are bound to that phase's moment.
    /// </summary>
    /// <remarks>
    /// 名字对不代表约束对。**行里的左端是展开后的基础符号**（`stowage_0_0`、`y_P1`…），不是 `longitudinal_torque_*` 本身，
    /// 所以判据比的是**多项式**：逐符号系数之和 + 常数项。再加一条反向断言——它不等于**别的阶段**的多项式，
    /// 否则三个阶段都绑到同一个力矩上也能通过。
    ///
    /// Names being right does not make the rows right. **The row's left-hand side is the expanded base symbols** (`stowage_0_0`, `y_P1`,...), not `longitudinal_torque_*`
    /// itself, so the criterion compares **polynomials** — summed coefficients per symbol plus the constant — and adds a reverse assertion that it does not equal **another
    /// phase's** polynomial; otherwise binding all three phases to one moment would still pass.
    /// </remarks>
    [Fact]
    public void LongitudinalMomentLimit_ShouldBindEachPhaseToItsOwnMoment() {
        var rig = new Rig();
        var limit = new LongitudinalMomentLimit(rig.Torque, 4000.0, 6000.0, "target_longitudinal_moment_limit");
        limit.Invoke(rig.Model).IsOk.Should().BeTrue();

        FlightPhase[] phases = { FlightPhase.ZeroFuel, FlightPhase.TakeOff, FlightPhase.Landing };
        for (int i = 0; i < phases.Length; ++i) {
            LinearExpressionSymbol moment = rig.Torque.LongitudinalTorque(phases[i])!;
            LinearInequalityConstraint<Flt64> lower = RowByName(rig.Model, limit.LastAddedNames[i * 2]);
            LinearInequalityConstraint<Flt64> upper = RowByName(rig.Model, limit.LastAddedNames[(i * 2) + 1]);

            AssertPolynomial(lower.Inequality.Lhs, moment.Polynomial);
            lower.Inequality.Comparison.Should().Be(Comparison.GE);
            lower.Inequality.Rhs.Constant.ToDouble().Should().Be(4000.0);

            AssertPolynomial(upper.Inequality.Lhs, moment.Polynomial);
            upper.Inequality.Comparison.Should().Be(Comparison.LE);
            upper.Inequality.Rhs.Constant.ToDouble().Should().Be(6000.0);

            foreach (FlightPhase other in phases.Where(phase => phase != phases[i])) {
                LinearExpressionSymbol otherMoment = rig.Torque.LongitudinalTorque(other)!;
                CoeffKey(lower.Inequality.Lhs).Should().NotBe(CoeffKey(otherMoment.Polynomial),
                    "the row must carry this phase's moment, not " + other + "'s");
            }
        }
    }

    /// <summary>
    /// **缺纵向力矩符号时明确失败** / A missing longitudinal torque symbol fails explicitly.
    /// </summary>
    /// <remarks>
    /// 成对：同一个限制在注册过力矩的模型上成功，在没注册过的模型上失败并说明是**哪个阶段**。基线这里是 `!!`，
    /// 会抛空指针；C# 侧改成返回失败，错误消息带阶段名。
    ///
    /// Paired: the same limit succeeds on a model with the torque registered and fails on one without, naming the **phase**. The baseline uses `!!` and throws; this returns a
    /// failure whose message carries the phase.
    /// </remarks>
    [Fact]
    public void LongitudinalMomentLimit_ShouldFailWhenTheMomentIsMissing() {
        var rig = new Rig(registerTorque: false);
        var limit = new LongitudinalMomentLimit(rig.Torque, 4000.0, 6000.0, "target_longitudinal_moment_limit");

        Result<Success, ErrorCode, Error<ErrorCode>> result = limit.Invoke(rig.Model);
        result.IsOk.Should().BeFalse();
        var failed = result as Failed<Success, ErrorCode, Error<ErrorCode>>;
        failed!.Error.Message.Should().Contain("ZeroFuel", "the message names the first phase it could not bind");
        limit.LastAddedNames.Should().BeEmpty("nothing was added");

        var okRig = new Rig();
        new LongitudinalMomentLimit(okRig.Torque, 4000.0, 6000.0, "target_longitudinal_moment_limit")
            .Invoke(okRig.Model).IsOk.Should().BeTrue("the same limit succeeds once the torque is registered");
    }

    /// <summary>
    /// **先 `_maximum` 后 `_minimum`，下限是对负数取上限** / `_maximum` first, and the lower bound is an upper bound on the negation.
    /// </summary>
    /// <remarks>
    /// 三处断言成对：
    ///
    /// 1. 名字顺序是 `_maximum` → `_minimum`（与 `LongitudinalMomentLimit` 相反）。
    /// 2. 上限行的左端系数是 **+1**，下限行是 **−1**。
    /// 3. 两行的右端都是 **`maxLateralImbalance`**（下限不是 `−max`）。若把下限写成 `lateral ≥ −max`，右端会是负值，这条会红。
    ///
    /// Three paired assertions: the name order is `_maximum` → `_minimum` (opposite to `LongitudinalMomentLimit`); the upper row's leading coefficient is **+1** and the lower
    /// row's is **−1**; and **both** right-hand sides equal `maxLateralImbalance` (the lower is not `−max`). Writing the lower bound as `lateral ≥ −max` makes the right-hand side
    /// negative and turns this red.
    /// </remarks>
    [Fact]
    public void LateralImbalanceLimit_ShouldUseSymmetricBoundsOnTheNegation() {
        var rig = new Rig();
        var limit = new LateralImbalanceLimit(rig.Torque, 2500.0);
        limit.Name.Should().Be("lateral_imbalance_limit", "the default name matches the baseline");
        limit.Invoke(rig.Model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().Equal(new[] {
            "lateral_imbalance_limit_maximum",
            "lateral_imbalance_limit_minimum",
        }, "maximum first, both suffixed");

        LinearExpressionSymbol lateral = rig.Torque.LateralTorqueSymbol!;
        LinearInequalityConstraint<Flt64> upper = RowByName(rig.Model, limit.LastAddedNames[0]);
        LinearInequalityConstraint<Flt64> lower = RowByName(rig.Model, limit.LastAddedNames[1]);

        upper.Inequality.Comparison.Should().Be(Comparison.LE);
        lower.Inequality.Comparison.Should().Be(Comparison.LE, "the lower bound is an upper bound on the negation");

        AssertPolynomial(upper.Inequality.Lhs, lateral.Polynomial);
        AssertPolynomial(lower.Inequality.Lhs, -lateral.Polynomial, "the lower row is the negated polynomial, constant included");
        CoeffKey(lower.Inequality.Lhs).Should().NotBe(CoeffKey(upper.Inequality.Lhs), "the two rows are not the same polynomial");

        upper.Inequality.Rhs.Constant.ToDouble().Should().Be(2500.0);
        lower.Inequality.Rhs.Constant.ToDouble().Should().Be(2500.0, "both bounds are the positive maximum; only the left-hand sign differs");
    }

    /// <summary>
    /// **缺横向力矩符号时明确失败** / A missing lateral torque symbol fails explicitly.
    /// </summary>
    /// <remarks>
    /// 成对：注册过力矩时成功，没注册时失败且不加任何行。
    ///
    /// Paired: succeeds with the torque registered, fails without it and adds no row.
    /// </remarks>
    [Fact]
    public void LateralImbalanceLimit_ShouldFailWhenTheLateralTorqueIsMissing() {
        var rig = new Rig(registerTorque: false);
        var limit = new LateralImbalanceLimit(rig.Torque, 2500.0);

        Result<Success, ErrorCode, Error<ErrorCode>> result = limit.Invoke(rig.Model);
        result.IsOk.Should().BeFalse();
        (result as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("横向力矩符号未注册");
        limit.LastAddedNames.Should().BeEmpty("nothing was added");

        var okRig = new Rig();
        new LateralImbalanceLimit(okRig.Torque, 2500.0).Invoke(okRig.Model).IsOk.Should().BeTrue();
    }

    /// <summary>把多项式化成「符号 → 系数之和」的键，用于比较两个多项式 / Collapse a polynomial into symbol-to-summed-coefficient for comparison.</summary>
    /// <param name="polynomial">多项式 / The polynomial.</param>
    /// <returns>可比较的键 / A comparable key.</returns>
    private static string CoeffKey(LinearPolynomial<Flt64> polynomial) {
        var entries = polynomial.Monomials
            .GroupBy(monomial => monomial.Symbol.Name, System.StringComparer.Ordinal)
            .Select(group => group.Key + "=" + group.Sum(monomial => monomial.Coefficient.ToDouble()).ToString("R", System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(text => text, System.StringComparer.Ordinal)
            .ToList();
        return string.Join(";", entries)
            + "|c=" + polynomial.Constant.ToDouble().ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>断言两个多项式的逐符号系数与常数项都相等 / Assert both polynomials agree per symbol and in the constant.</summary>
    /// <param name="actual">实际左端 / The actual left-hand side.</param>
    /// <param name="expected">期望多项式 / The expected polynomial.</param>
    /// <param name="because">原因 / Why.</param>
    private static void AssertPolynomial(LinearPolynomial<Flt64> actual, LinearPolynomial<Flt64> expected, string because = "the row carries the whole polynomial") =>
        CoeffKey(actual).Should().Be(CoeffKey(expected), because);

    private static LinearInequalityConstraint<Flt64> RowByName(LinearMetaModel<Flt64> model, string name) =>
        model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == name);
}
