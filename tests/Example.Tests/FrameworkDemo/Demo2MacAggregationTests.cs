// MAC 域聚合与 MAC 模型的判据 / Criteria for the MAC aggregation and the MAC model.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
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
/// **MAC 域聚合（B604）** / The MAC domain aggregation (B604).
/// </summary>
/// <remarks>
/// 这个域的 Benders 语义与配载域**相反**：这里 MP 等于 `Register`、SP 是空操作；配载域 MP 少两族、SP 换一套。
/// 两个聚合名字一样、语义相反，按名字类推是最容易犯的错，判据因此把两面都钉住。
///
/// This domain's Benders semantics **oppose** the stowage domain's: here the master equals `Register` and the sub-problem is a no-op, while there the master registers less and the
/// sub-problem a different set. Same names, opposite semantics — generalising from one to the other is the easiest mistake, so the criteria pin both sides.
/// </remarks>
public class Demo2MacAggregationTests {
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

    private static readonly HorizontalStabilizer.Key StabilizerKey =
        new(new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure.HorizontalStabilizerAngle("2.0"), null);

    private sealed class Rig
    {
        public Rig(bool wideBody = false, bool withComputedTotalWeight = true) {
            var items = new List<Item> { MakeItem("I1") };
            var positions = new List<StowagePosition> { MakePosition("P1", 1UL) };
            var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
            AircraftType type = wideBody ? AircraftType.B747 : AircraftType.B737;
            Aircraft = new AircraftModel(type, type.ToString(), new AircraftMinorModel(type.ToString()));
            // `doiCorrection` 必须非零，否则"多项式里漏掉 DoiCorrection"是无操作、变异必然存活（B604 实测）。
            // `doiCorrection` must be non-zero, or "dropping DoiCorrection from the polynomial" is a no-op and the mutation always survives (measured in B604).
            Formula = new Formula(Aircraft, lip: 100.0, chord: 20.0, standardDatum: 120.0, forceDistanceCoefficient: 5000.0, doiCorrection: 7.0);
            Load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: Aircraft, formula: Formula);
            var payload = new Payload(positions, Load, plannedPayload: 5000.0, maxPayload: 20000.0);
            var fuselage = new Fuselage(null, Dow: 40000.0, Doi: 10.0, BalancedArm: 900.0);
            // 燃油表**必须让阶段间有差别**，否则三个阶段的指数多项式完全相同，"取起飞而不是着陆"就无法区分。
            // The fuel map **must make the phases differ**, or all three index polynomials are identical and "take-off rather than landing" cannot be told apart.
            var fuel = new Dictionary<FlightPhase, FuelConstant> {
                [FlightPhase.TakeOff] = new FuelConstant(Density: 0.8, Weight: 1000.0, Index: 1000.0),
                [FlightPhase.Landing] = new FuelConstant(Density: 0.8, Weight: 2000.0, Index: 2000.0),
            };
            TotalWeight = withComputedTotalWeight
                ? new TotalWeight(fuselage, fuel, payload, computedTotalWeight: new Dictionary<FlightPhase, double> { [FlightPhase.TakeOff] = 50000.0 })
                : new TotalWeight(fuselage, fuel, payload);

            Model = new LinearMetaModel<Flt64>();
            stowage.Register(Model).IsOk.Should().BeTrue();
            Load.Register(Model).IsOk.Should().BeTrue();
            payload.Register(StowageMode.FullLoad, Model).IsOk.Should().BeTrue();
            TotalWeight.Register(Model).IsOk.Should().BeTrue();

            Aggregation = new MacAggregation(
                fuselage,
                fuel,
                Formula,
                positions,
                Load,
                TotalWeight,
                new Dictionary<HorizontalStabilizer.Key, (IReadOnlyList<HorizontalStabilizer.Point>, HorizontalStabilizer.Limit)> {
                    [StabilizerKey] = (
                        new List<HorizontalStabilizer.Point> { new(Tow: 40000.0, MacPercent: 20.0, Trim: 1.5) },
                        new HorizontalStabilizer.Limit(MinTrim: -1.0, MaxTrim: 3.0)),
                },
                wideBody);
        }

        public AircraftModel Aircraft { get; }

        public Formula Formula { get; }

        public Load Load { get; }

        public TotalWeight TotalWeight { get; }

        public LinearMetaModel<Flt64> Model { get; }

        public MacAggregation Aggregation { get; }
    }

    /// <summary>
    /// **`mac` 是起飞指数经公式换算的线性多项式** / `mac` is the take-off index mapped through the formula into a linear polynomial.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 符号名是 `mac`，且**不是常数**（有单式）——总重已知时它必须保持线性。
    /// 2. 它的逐符号系数与常数项**等于** `Formula.MacPolynomial(index_takeoff, tow)` 直接算出的结果。
    /// 3. 与标量版一致：用标量 `MacFromIndex` 在任意一组指数取值上验证同一点，两条路径给同一个数。
    ///
    /// 只断言"符号存在"无法区分"换算对了"与"换算成常数零"——第 1 点正是为此。
    ///
    /// Three paired points: the symbol is named `mac` and **is not constant** (it has monomials) — with a known total weight it must stay linear; its per-symbol coefficients and
    /// constant **equal** what `Formula.MacPolynomial(index_takeoff, tow)` computes directly; and it agrees with the scalar `MacFromIndex` at a sample index, so the two paths give
    /// the same number. Asserting only "the symbol exists" cannot distinguish a correct mapping from a constant zero, which is what point one is for.
    /// </remarks>
    [Fact]
    public void Mac_ShouldMapTheTakeOffIndexThroughTheFormula() {
        var rig = new Rig();
        rig.Aggregation.Register(StowageMode.FullLoad, rig.Model).IsOk.Should().BeTrue();

        LinearExpressionSymbol mac = rig.Aggregation.Mac.MacSymbol!;
        mac.Name.Should().Be("mac");
        mac.Polynomial.Monomials.Should().NotBeEmpty("with a known take-off total weight the MAC stays linear");

        // **不能拿 `Formula.MacPolynomial` 当期望值**：公式被变异时两边一起变，判据假绿（实测"去掉 DoiCorrection"与"去掉 Lip"两个变异
        // 都因此存活）。这里改用**标量路径** `MacFromIndex` 做交叉验证——它是另一份实现，只有一边被改才会不一致。
        // **`Formula.MacPolynomial` cannot be the expectation**: mutating the formula changes both sides and the criterion goes falsely green (measured — "drop DoiCorrection" and
        // "drop Lip" both survived this way). The **scalar path** `MacFromIndex` is used as the cross-check: it is a separate implementation, so a change to either one shows up.
        var constantIndex = new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
            new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), new Flt64(1000.0));
        rig.Formula.MacPolynomial(constantIndex, 50000.0).Constant.ToDouble()
            .Should().BeApproximately(rig.Formula.MacFromIndex(1000.0, 50000.0), 1e-9,
                "the polynomial path and the scalar path must agree on a constant index");

        LinearExpressionSymbol index = rig.Aggregation.Torque.Index(FlightPhase.TakeOff)!;
        mac.Polynomial.Monomials.Select(monomial => monomial.Symbol.Name).Distinct()
            .Should().BeEquivalentTo(index.Polynomial.Monomials.Select(monomial => monomial.Symbol.Name).Distinct(),
                "the MAC keeps exactly the index's symbols, only rescaled");

        // 逐符号系数必须是"指数系数 × k / weight / chord"这一个**手算**比例，而不是另一个函数算出来的数。
        // Every per-symbol coefficient must be the **hand-computed** ratio "index coefficient × k / weight / chord", not a number another function produced.
        // **常数项也要钉**：阶段间的差别只体现在常数项（燃油是常数），只比系数无法区分"取起飞"与"取着陆"。
        // **The constant is pinned too**: phases differ only in the constant term (the fuel is a constant), so comparing coefficients alone cannot tell take-off from landing.
        double expectedConstant =
            (((index.Polynomial.Constant.ToDouble() - rig.Formula.DoiCorrection) * rig.Formula.ForceDistanceCoefficient / 50000.0)
                + rig.Formula.StandardDatum - rig.Formula.Lip) / rig.Formula.Chord;
        mac.Polynomial.Constant.ToDouble().Should().BeApproximately(expectedConstant, 1e-9,
            "the constant is the take-off index's constant carried through arm and MAC by hand");

        double scale = rig.Formula.ForceDistanceCoefficient / 50000.0 / rig.Formula.Chord;
        foreach (string symbol in index.Polynomial.Monomials.Select(monomial => monomial.Symbol.Name).Distinct()) {
            double indexCoefficient = index.Polynomial.Monomials
                .Where(monomial => monomial.Symbol.Name == symbol)
                .Sum(monomial => monomial.Coefficient.ToDouble());
            double macCoefficient = mac.Polynomial.Monomials
                .Where(monomial => monomial.Symbol.Name == symbol)
                .Sum(monomial => monomial.Coefficient.ToDouble());
            macCoefficient.Should().BeApproximately(indexCoefficient * scale, 1e-9, "symbol " + symbol + " is rescaled by k / weight / chord");
        }
    }

    /// <summary>
    /// **起飞总重未知时 `mac` 是常数零，而不是缺席** / Without a take-off total weight `mac` is the constant zero, not absent.
    /// </summary>
    /// <remarks>
    /// 成对：总重已知时 `mac` 有单式；未知时符号**仍然存在**且是常数零。若把这条写成"跳过注册"，下游水平安定面会找不到 `mac`——
    /// 而这在求解日志里只看得到"符号缺失"，看不出根因。
    ///
    /// Paired: with a known total weight `mac` has monomials; without one the symbol **still exists** and is the constant zero. Skipping the registration would leave the
    /// downstream horizontal stabilizer without a `mac`, which shows up only as "symbol missing" in a solve log with no hint of the cause.
    /// </remarks>
    [Fact]
    public void Mac_ShouldFallBackToAConstantZeroWithoutTheTotalWeight() {
        var known = new Rig(withComputedTotalWeight: true);
        known.Aggregation.Register(StowageMode.FullLoad, known.Model).IsOk.Should().BeTrue();
        known.Aggregation.Mac.MacSymbol!.Polynomial.Monomials.Should().NotBeEmpty();

        var unknown = new Rig(withComputedTotalWeight: false);
        unknown.Aggregation.Register(StowageMode.FullLoad, unknown.Model).IsOk.Should().BeTrue();
        LinearExpressionSymbol mac = unknown.Aggregation.Mac.MacSymbol!;
        mac.Name.Should().Be("mac", "the symbol must exist even without a total weight");
        mac.Polynomial.Monomials.Should().BeEmpty();
        mac.Polynomial.Constant.ToDouble().Should().Be(0.0);
    }

    /// <summary>
    /// **指数符号缺失时 MAC 明确失败** / The MAC fails explicitly when the index symbol is missing.
    /// </summary>
    /// <remarks>
    /// 用一个**没有注册过力矩**的模型：`Torque.Index(TakeOff)` 为 `null`，`MacModel.Register` 必须失败并说明缺的是什么，
    /// 而不是拿 `!` 造一个空引用让下游在别处炸。成对——同一套构造在注册过力矩的模型上成功。
    ///
    /// Uses a model with **no torque registered**: `Torque.Index(TakeOff)` is `null`, so `MacModel.Register` must fail naming what is missing instead of taking a `!` and letting
    /// the null surface elsewhere. Paired — the same setup succeeds once the torque is registered.
    /// </remarks>
    [Fact]
    public void Mac_ShouldFailWhenTheIndexSymbolIsMissing() {
        var rig = new Rig();
        using var bareModel = new LinearMetaModel<Flt64>();
        Result<Success, ErrorCode, Error<ErrorCode>> result = rig.Aggregation.Mac.Register(bareModel);
        result.IsOk.Should().BeFalse("the torque was never registered");
        (result as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("指数");

        var okRig = new Rig();
        okRig.Aggregation.Register(StowageMode.FullLoad, okRig.Model).IsOk.Should().BeTrue();
        okRig.Aggregation.Mac.MacSymbol.Should().NotBeNull();
    }

    /// <summary>
    /// **三条路径：MP 等于 `Register`，SP 是空操作** / Three paths: the master equals `Register` and the sub-problem is a no-op.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `RegisterForBendersMP` 之后力矩、MAC、水平安定面的符号**都在**（与 `Register` 相同）。
    /// 2. `RegisterForBendersSP` 之后**一个符号都没有**。
    /// 3. 两条路径都成功。
    ///
    /// **这与配载域相反**（那边 MP 少两族、SP 换一套）。若按配载域的样子写，第 2 点会红。
    ///
    /// Three paired points: after the master registration the torque, MAC and horizontal stabilizer symbols are **all present** (as in `Register`); after the sub-problem
    /// registration **none of them is**; and both paths succeed. **This opposes the stowage domain** (master registers less, sub-problem a different set); copying that shape turns
    /// point two red.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldKeepTheBendersSemanticsOfThisDomain() {
        var master = new Rig();
        using var masterModel = new LinearMetaModel<Flt64>();
        master.Aggregation.RegisterForBendersMP(StowageMode.FullLoad, masterModel).IsOk.Should().BeTrue();
        master.Aggregation.Torque.LateralTorqueSymbol.Should().NotBeNull("the master registers the torque");
        master.Aggregation.Mac.MacSymbol.Should().NotBeNull("and the MAC");
        master.Aggregation.HorizontalStabilizers[StabilizerKey].TrimSymbol.Should().NotBeNull("and the horizontal stabilizers");

        var sub = new Rig();
        using var subModel = new LinearMetaModel<Flt64>();
        sub.Aggregation.RegisterForBendersSP(StowageMode.FullLoad, subModel).IsOk.Should().BeTrue();
        sub.Aggregation.Torque.LateralTorqueSymbol.Should().BeNull("the sub-problem registers nothing at all");
        sub.Aggregation.Mac.MacSymbol.Should().BeNull();
        sub.Aggregation.HorizontalStabilizers[StabilizerKey].TrimSymbol.Should().BeNull();
        subModel.Constraints.Count.Should().Be(0, "a no-op adds no row either");
    }

    /// <summary>
    /// **`Register` 注册力矩、MAC 与每个水平安定面** / `Register` covers the torque, the MAC and every horizontal stabilizer.
    /// </summary>
    /// <remarks>
    /// 成对：`clim` 只在宽体机上出现（窄体机没有），而力矩、MAC、`trim` 两种机型都有。把"水平安定面循环"漏掉时 `trim` 会是 `null`。
    ///
    /// Paired: `clim` appears only on a wide body while the torque, the MAC and `trim` appear on both. Dropping the stabilizer loop leaves `trim` `null`.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldRegisterEveryPart() {
        var narrow = new Rig(wideBody: false);
        narrow.Aggregation.Register(StowageMode.FullLoad, narrow.Model).IsOk.Should().BeTrue();
        narrow.Aggregation.Torque.ClimSymbol.Should().BeNull("a narrow body registers no CLIM");
        narrow.Aggregation.Torque.LateralTorqueSymbol.Should().NotBeNull();
        narrow.Aggregation.Mac.MacSymbol.Should().NotBeNull();
        narrow.Aggregation.HorizontalStabilizers[StabilizerKey].TrimSymbol.Should().NotBeNull();

        var wide = new Rig(wideBody: true);
        wide.Aggregation.Register(StowageMode.FullLoad, wide.Model).IsOk.Should().BeTrue();
        wide.Aggregation.Torque.ClimSymbol.Should().NotBeNull("a wide body registers the CLIM");
    }
}
