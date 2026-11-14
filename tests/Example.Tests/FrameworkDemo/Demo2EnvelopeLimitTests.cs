// CG 包络约束的判据 / Criteria for the CG envelope constraint.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
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
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **CG 包络约束（B594）** / The CG envelope constraint (B594).
/// </summary>
/// <remarks>
/// 每个飞行阶段 × 每条包络各加**两行**：纵向力矩 ≤ 最大指数（`_ub`）与 ≥ 最小指数（`_lb`）。
///
/// 四处容易做错，本批每条都有一条判据：
///
/// | 处 | 做错了会怎样 |
/// | --- | --- |
/// | 两行都要有 | 只加上界时重心可以无限前移而求解器一声不响 |
/// | 同阶段多包络是"且" | 合并成一条要先判"谁更严"，而它随总重变化 |
/// | 力矩按阶段取 | 取错会让起飞与着陆共用同一个力矩边界 |
/// | `_ub`/`_lb` 后缀 | 两行同名时"第 3 条约束不可行"无法区分上下界 |
///
/// Both rows per phase per envelope, with one criterion for each of the four mistakes tabulated above.
/// </remarks>
public class Demo2EnvelopeLimitTests {
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

    private static (Torque Torque, LinearMetaModel<Flt64> Model) PrepareTorque() {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation),
            MakePosition("P2", 2UL, PositionLocation.LowForwardNotBulkLocation),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        var fuselage = new Fuselage(new Liferaft(50.0, 12.0), Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);
        var fuel = new Dictionary<FlightPhase, FuelConstant> {
            [FlightPhase.TakeOff] = new FuelConstant(0.8, 8000.0, 5.0),
            [FlightPhase.Landing] = new FuelConstant(0.8, 2000.0, 2.0),
        };
        var torque = new Torque(fuselage, fuel, formula, positions, load, wideBody: false);
        torque.Register(model).IsOk.Should().BeTrue();
        return (torque, model);
    }

    /// <summary>
    /// 造一条已注册的包络：最小指数常数、最大指数常数。
    /// </summary>
    private static IEnvelope MakeEnvelope(string name, FlightPhase phase, double minimum, double maximum) {
        var lhs = new EnvelopeSide(name + "_lhs", EnvelopeSideType.Left, new[] {
            new EnvelopePoint(40000.0, minimum),
            new EnvelopePoint(60000.0, minimum),
        });
        var rhs = new EnvelopeSide(name + "_rhs", EnvelopeSideType.Right, new[] {
            new EnvelopePoint(40000.0, maximum),
            new EnvelopePoint(60000.0, maximum),
        });
        TotalWeight? unused = null;
        _ = unused;
        return new ConstantEnvelope(phase, name, minimum, maximum, lhs, rhs);
    }

    /// <summary>
    /// **每个包络两行，方向相反** / Two rows per envelope, in opposite directions.
    /// </summary>
    /// <remarks>
    /// 一条包络 → 两行：`_ub` 是 `LE`、`_lb` 是 `GE`，右端分别是最大与最小指数。只加上界时重心可以无限前移而求解器一声不响。
    ///
    /// One envelope → two rows: `_ub` is `LE` and `_lb` is `GE`, with the maxim-um and minimum index respectively. With only the upper bound the centre of gravity drifts
    /// arbitrarily forward and nothing complains.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddBothBounds() {
        (Torque torque, LinearMetaModel<Flt64> model) = PrepareTorque();
        var envelopes = new Dictionary<FlightPhase, IReadOnlyList<IEnvelope>> {
            [FlightPhase.TakeOff] = new[] { MakeEnvelope("main", FlightPhase.TakeOff, minimum: 10.0, maximum: 50.0) },
        };
        var limit = new EnvelopeLimit(torque, envelopes);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().BeEquivalentTo(new[] {
            "envelope_limit_main_ub",
            "envelope_limit_main_lb",
        });

        LinearInequalityConstraint<Flt64> upper = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "envelope_limit_main_ub");
        upper.Inequality.Comparison.Should().Be(Comparison.LE);
        upper.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(50.0, 1e-9);

        LinearInequalityConstraint<Flt64> lower = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "envelope_limit_main_lb");
        lower.Inequality.Comparison.Should().Be(Comparison.GE);
        lower.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(10.0, 1e-9);
    }

    /// <summary>
    /// **左端是该阶段的纵向力矩** / The left side is that phase's longitudinal torque.
    /// </summary>
    /// <remarks>
    /// 与"两个阶段各用各的力矩"成对：起飞行的左端单项式集合等于 `longitudinal_torque_takeoff` 的，着陆行的等于 `longitudinal_torque_landing` 的，且两者不同。
    /// 取错阶段会让起飞与着陆共用一个力矩边界——而它俩的燃油量差 6000 kg。
    ///
    /// Paired with "the two phases use their own torques": the take-off row's monomials equal `longitudinal_torque_takeoff`'s and the landing row's equal
    /// `longitudinal_torque_landing`'s, and the two differ. Taking the wrong phase makes take-off and landing share one boundary — and their fuel loads differ by 6000 kg.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldUseThatPhaseLongitudinalTorque() {
        (Torque torque, LinearMetaModel<Flt64> model) = PrepareTorque();
        var envelopes = new Dictionary<FlightPhase, IReadOnlyList<IEnvelope>> {
            [FlightPhase.TakeOff] = new[] { MakeEnvelope("to", FlightPhase.TakeOff, 10.0, 50.0) },
            [FlightPhase.Landing] = new[] { MakeEnvelope("ldg", FlightPhase.Landing, 15.0, 55.0) },
        };
        var limit = new EnvelopeLimit(torque, envelopes);
        limit.Invoke(model).IsOk.Should().BeTrue();

        LinearInequalityConstraint<Flt64> takeOffUpper = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "envelope_limit_to_ub");
        LinearInequalityConstraint<Flt64> landingUpper = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "envelope_limit_ldg_ub");

        takeOffUpper.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
            .Should().BeEquivalentTo(torque.LongitudinalTorque(FlightPhase.TakeOff)!.Polynomial.Monomials.Select(monomial => monomial.Symbol));
        landingUpper.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
            .Should().BeEquivalentTo(torque.LongitudinalTorque(FlightPhase.Landing)!.Polynomial.Monomials.Select(monomial => monomial.Symbol));

        takeOffUpper.Inequality.Rhs.Constant.ToDouble()
            .Should().NotBeApproximately(landingUpper.Inequality.Rhs.Constant.ToDouble(), 1e-9);
        takeOffUpper.Inequality.Lhs.Constant.ToDouble()
            .Should().NotBeApproximately(landingUpper.Inequality.Lhs.Constant.ToDouble(), 1e-9,
                "the two phases carry different fuel indices");
    }

    /// <summary>
    /// **同一阶段的多条包络是"且"不是"或"** / Several envelopes for one phase mean AND.
    /// </summary>
    /// <remarks>
    /// 一个阶段两条包络 → **四行**（每条两行），且两条的上界都必须在场。合并成一条（取更严的）会少一半约束，而合并时"谁更严"要随总重重新判定。
    ///
    /// One phase with two envelopes → **four rows**, both upper bounds present. Folding them into one (taking the tighter) halves the constraints, and deciding which is tighter
    /// depends on the total weight.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddBothEnvelopesOfTheSamePhase() {
        (Torque torque, LinearMetaModel<Flt64> model) = PrepareTorque();
        var envelopes = new Dictionary<FlightPhase, IReadOnlyList<IEnvelope>> {
            [FlightPhase.TakeOff] = new[] {
                MakeEnvelope("narrow", FlightPhase.TakeOff, 10.0, 50.0),
                MakeEnvelope("wide", FlightPhase.TakeOff, 12.0, 45.0),
            },
        };
        var limit = new EnvelopeLimit(torque, envelopes);
        limit.Invoke(model).IsOk.Should().BeTrue();

        limit.LastAddedNames.Should().HaveCount(4);
        limit.LastAddedNames.Should().BeEquivalentTo(new[] {
            "envelope_limit_narrow_ub",
            "envelope_limit_narrow_lb",
            "envelope_limit_wide_ub",
            "envelope_limit_wide_lb",
        });
        LinearInequalityConstraint<Flt64> wideUpper = model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(constraint => constraint.Name == "envelope_limit_wide_ub");
        wideUpper.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(45.0, 1e-9,
            "each envelope keeps its own bound");
    }

    /// <summary>
    /// **缺包络符号时明确失败** / Missing envelope symbols fail loudly.
    /// </summary>
    /// <remarks>
    /// 空壳版本对任何输入都返回 `Ok`。缺符号时报成功会让调用方以为重心约束加上了，而模型里一行都没有——整条安全要求凭空消失。
    ///
    /// The shell returned `Ok` for any input. Reporting success while a symbol is missing tells the caller the CG constraint is in place when the model holds none — a whole
    /// safety requirement gone.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailWhenAnEnvelopeSymbolIsMissing() {
        (Torque torque, LinearMetaModel<Flt64> model) = PrepareTorque();
        var envelopes = new Dictionary<FlightPhase, IReadOnlyList<IEnvelope>> {
            [FlightPhase.TakeOff] = new[] { (IEnvelope)new UnregisteredEnvelope(FlightPhase.TakeOff, "broken") },
        };
        var limit = new EnvelopeLimit(torque, envelopes);

        limit.Invoke(model).IsOk.Should().BeFalse();
        limit.LastAddedNames.Should().BeEmpty();
    }

    /// <summary>
    /// **缺力矩符号时明确失败** / A missing torque symbol fails loudly.
    /// </summary>
    /// <remarks>
    /// 与"缺包络符号"成对：两个失败分支各测一次，否则其中一个可以是 `return Ok` 而没人发现。
    ///
    /// Paired with the missing-envelope case: each failure branch gets its own criterion, or one of them can be `return Ok` undetected.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailWhenTheTorqueSymbolIsMissing() {
        using var model = new LinearMetaModel<Flt64>();
        var emptyTorque = new Torque(
            new Fuselage(null, 40000.0, 10.0, 900.0),
            new Dictionary<FlightPhase, FuelConstant>(),
            new Formula(new AircraftModel(AircraftType.B767, "B767", new AircraftMinorModel("B767")), 100.0, 20.0, 120.0, 5000.0, 0.0),
            new List<StowagePosition>(),
            null!,
            wideBody: false);
        var envelopes = new Dictionary<FlightPhase, IReadOnlyList<IEnvelope>> {
            [FlightPhase.TakeOff] = new[] { MakeEnvelope("main", FlightPhase.TakeOff, 10.0, 50.0) },
        };
        var limit = new EnvelopeLimit(emptyTorque, envelopes);

        limit.Invoke(model).IsOk.Should().BeFalse("the torque symbols were never registered");
        limit.LastAddedNames.Should().BeEmpty();
    }

    /// <summary>一条已注册好的常数包络 / A pre-registered constant envelope.</summary>
    private sealed class ConstantEnvelope : IEnvelope
    {
        public ConstantEnvelope(
            FlightPhase phase,
            string name,
            double minimum,
            double maximum,
            EnvelopeSide lhs,
            EnvelopeSide rhs) {
            Phase = phase;
            Name = name;
            LhsSide = lhs;
            RhsSide = rhs;
            MinIndexSymbol = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: new Flt64(minimum)),
                name: "min_index_" + name);
            MaxIndexSymbol = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: new Flt64(maximum)),
                name: "max_index_" + name);
        }

        public FlightPhase Phase { get; }

        public string Name { get; }

        public EnvelopeSide LhsSide { get; }

        public EnvelopeSide RhsSide { get; }

        public ILinearIntermediateSymbol<Flt64>? MinIndexSymbol { get; }

        public ILinearIntermediateSymbol<Flt64>? MaxIndexSymbol { get; }

        public Fuookami.Ospf.Utils.Functional.Result<
            Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> Register(
            LinearMetaModel<Flt64> model) =>
            Fuookami.Ospf.Utils.Functional.Results.Ok<Fuookami.Ospf.Utils.Functional.Success>(
                Fuookami.Ospf.Utils.Functional.Results.SuccessInstance);
    }

    /// <summary>一条从未注册符号的包络 / An envelope whose symbols were never registered.</summary>
    private sealed class UnregisteredEnvelope : IEnvelope
    {
        public UnregisteredEnvelope(FlightPhase phase, string name) {
            Phase = phase;
            Name = name;
        }

        public FlightPhase Phase { get; }

        public string Name { get; }

        public ILinearIntermediateSymbol<Flt64>? MinIndexSymbol => null;

        public ILinearIntermediateSymbol<Flt64>? MaxIndexSymbol => null;

        public Fuookami.Ospf.Utils.Functional.Result<
            Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode,
            Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> Register(
            LinearMetaModel<Flt64> model) =>
            Fuookami.Ospf.Utils.Functional.Results.Ok<Fuookami.Ospf.Utils.Functional.Success>(
                Fuookami.Ospf.Utils.Functional.Results.SuccessInstance);
    }
}
