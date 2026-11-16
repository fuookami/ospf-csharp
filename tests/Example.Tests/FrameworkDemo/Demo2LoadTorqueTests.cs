// 装载模型此前的判据：扭矩、CLIM、指数与容量联动 / Criteria for the third load slice: torque, CLIM, index and the capacity link.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **扭矩、CLIM、指数与容量联动** / Torque, CLIM, index and the capacity link.
/// </summary>
/// <remarks>
/// 这里的符号全部**建在已有符号之上**，所以最容易出的错不是"算错"而是"接错"：
///
/// - 纵向力矩乘了**另一个**重量的多项式（推荐力矩乘实际重量）——数值看着正常，只是"推荐"和"实际"永远相等；
/// - `load_index` 的系数写成货物重量而不是"该舱位力臂下的指数"，量纲对、数错位；
/// - `y`/`z` **没接进容量行**——松弛重量对容量不可见，模型可以凭空多装几百公斤，且没有任何求解错误。
///
/// Every symbol here is built **on top of** existing ones, so the likely failure is not "wrong arithmetic" but "wrong wiring": a longitudinal torque multiplying the *other*
/// weight's polynomial (values look fine, "estimate" just always equals "actual"); the `load_index` coefficient being the cargo weight instead of the index at that arm
/// (right dimension, wrong number); or `y`/`z` **never linked into the capacity row** — slack weight invisible to capacity, hundreds of kilograms appearing from nowhere with no
/// solver complaint.
/// </remarks>
public class Demo2LoadTorqueTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(string name, double weight) =>
        new(
            name: name,
            weight: weight,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromCode(CargoCode.BAL),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(
        string name,
        ulong id,
        ulong mla,
        double longitudinalArm,
        double lateralArm,
        double mlw = 1000.0) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(
                frontArm: longitudinalArm,
                backArm: longitudinalArm,
                leftArm: lateralArm,
                rightArm: lateralArm),
            shape: new PositionShape(1.0, 1.0, 1.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(
                stowageNeeded: true,
                adjustmentNeeded: false,
                predicateWeightNeeded: true,
                recommendedWeightNeeded: true),
            mlw: new MaxLoadWeightLimit(mlw),
            mla: mla,
            // 起"需要谓词重量"就必须带三元组规格：`Load` 的松弛右端取 `min`，缺规格是**错误而不是跳过**
            // （跳过会让 `y` 无下界参照，模型照解）。这批既有 fixture 默认给了 `mlw` 那一口径的值。
            // Since "needs the predicate weight" requires the triple: `Load`'s slack takes `min` and a missing specification is an **error, not a skip** (skipping leaves
            // `y` with no reference bound and the model still solves). These pre-existing fixtures default the bounds to the `mlw` figure.
            plw: new PositionPredicateLoadWeight(Plw: 1000.0, Min: 0.0, Max: 1000.0));
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

    private static AircraftModel MakeModel(AircraftType type) =>
        new(type, type.ToString(), new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure.AircraftMinorModel(type.ToString()));

    private static (List<Item> Items, List<StowagePosition> Positions, Stowage Stowage, Load Load) Prepare(
        AircraftType type = AircraftType.B767,
        bool multiSchema = true,
        Formula? formula = null) {
        var items = new List<Item> { MakeItem("I1", 100.0) };
        var positions = new List<StowagePosition> { MakePosition("P1", 1UL, 3UL, 500.0, 60.0) };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        AircraftModel model = MakeModel(type);
        Formula balance = formula ?? new Formula(model, lip: 100.0, chord: 20.0, standardDatum: 120.0, forceDistanceCoefficient: 5000.0, doiCorrection: 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: multiSchema, aircraftModel: model, formula: balance);
        return (items, positions, stowage, load);
    }

    /// <summary>
    /// **推荐纵向力矩乘推荐重量，实际乘实际重量** / The estimated torque uses the estimated weight; the actual uses the actual.
    /// </summary>
    /// <remarks>
    /// 两者成对断言系数：推荐力矩的 `stowage` 系数应当等于 "力矩/重量 × 100"（货物重量），再加 `y`/`z` 两项；
    /// 实际力矩**只有** `stowage` 项、系数相同、**没有** `y`/`z`。共用同一个重量多项式时，两者系数一样、项数一样，
    /// 但"推荐"从此等于"实际"。
    ///
    /// Paired on the coefficients: the estimated torque's `stowage` coefficient is torque-per-weight times 100 (the cargo weight) plus the `y`/`z` terms; the actual torque has
    /// **only** the `stowage` term with the same coefficient and **no** `y`/`z`. Sharing one weight polynomial makes both identical, so "estimate" equals "actual" forever.
    /// </remarks>
    [Fact]
    public void LongitudinalTorque_ShouldPairEstimateWithEstimateAndActualWithActual() {
        (List<Item> _, List<StowagePosition> positions, Stowage stowage, Load load) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        double torquePerWeight = TorquePerWeight(500.0);

        LinearExpressionSymbol estimate = load.EstimateLongitudinalTorqueSymbol(0)!;
        estimate.Name.Should().Be("load_longitudinal_torque_P1");
        estimate.Polynomial.Monomials.Should().HaveCount(3, "one cargo term plus y and z");
        estimate.Polynomial.Monomials[0].Symbol.Should().BeSameAs(stowage.StowageSymbol(0, 0));
        estimate.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(torquePerWeight * 100.0, 1e-6);

        LinearExpressionSymbol actual = load.ActualLongitudinalTorqueSymbol(0)!;
        actual.Name.Should().Be("actual_load_longitudinal_torque_P1");
        actual.Polynomial.Monomials.Should().HaveCount(1, "the actual torque carries no y/z");
        actual.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(torquePerWeight * 100.0, 1e-6);
        _ = positions;
    }

    /// <summary>
    /// **横向力矩用横向力臂** / The lateral torque uses the lateral arm.
    /// </summary>
    /// <remarks>
    /// 与纵向成对：同一个舱位的两个力臂不同（500 与 60），横向力矩的系数必须是 60 那一支。
    /// 复制纵向的实现会让横向力矩变成纵向力矩的复制品——量纲对、数值错位。
    ///
    /// Paired with the longitudinal one: the two arms differ (500 vs 60) and the lateral coefficient must be the 60 branch. Copying the longitudinal implementation makes the
    /// lateral torque a duplicate of it — right dimension, wrong number.
    /// </remarks>
    [Fact]
    public void LateralTorque_ShouldUseTheLateralArm() {
        (List<Item> _, List<StowagePosition> _, Stowage stowage, Load load) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        double perWeight = TorquePerWeight(1.0);

        LinearExpressionSymbol lateral = load.LateralTorqueSymbol(0)!;
        lateral.Name.Should().Be("load_lateral_torque_P1");
        lateral.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(perWeight * 60.0 * 100.0, 1e-6);

        double longitudinalCoefficient = load.EstimateLongitudinalTorqueSymbol(0)!.Polynomial.Monomials[0].Coefficient.ToDouble();
        lateral.Polynomial.Monomials[0].Coefficient.ToDouble().Should().NotBeApproximately(longitudinalCoefficient, 1e-3,
            "the two arms differ, so the two torques must differ");
    }

    /// <summary>
    /// **CLIM 只在宽体上存在** / CLIM exists only on wide bodies.
    /// </summary>
    /// <remarks>
    /// 成对：宽体有、窄体没有。窄体也建出来会多一族无人约束的力臂符号，而报告里会显示一个不该存在的 CLIM。
    ///
    /// Paired: present on a wide body, absent on a narrow one. Building it on a narrow body adds an unconstrained arm family and a CLIM in the report that should not exist.
    /// </remarks>
    [Fact]
    public void Clim_ShouldExistOnlyOnWideBodies() {
        (List<Item> _, List<StowagePosition> _, Stowage wideStowage, Load wide) = Prepare(AircraftType.B767);
        using var wideModel = new LinearMetaModel<Flt64>();
        wideStowage.Register(wideModel).IsOk.Should().BeTrue();
        wide.Register(wideModel).IsOk.Should().BeTrue();

        (List<Item> _, List<StowagePosition> _, Stowage narrowStowage, Load narrow) = Prepare(AircraftType.B737);
        using var narrowModel = new LinearMetaModel<Flt64>();
        narrowStowage.Register(narrowModel).IsOk.Should().BeTrue();
        narrow.Register(narrowModel).IsOk.Should().BeTrue();

        wide.ClimSymbol(0).Should().NotBeNull("B767 is a wide body");
        narrow.ClimSymbol(0).Should().BeNull("B737 is a narrow body");
    }

    /// <summary>
    /// **指数的系数是"该力臂下的指数"，不是货物重量** / The index coefficient is the index at that arm, not the cargo weight.
    /// </summary>
    /// <remarks>
    /// 成对断言：货物项的系数等于 `IndexFromWeight(100, 500)`，且**不等于** 100。写成货物重量时量纲看着对（都是数），
    /// 只有把公式算一遍才知道错位——而平衡图上的读数会整体偏移一个常数倍。
    ///
    /// Paired: the cargo coefficient equals `IndexFromWeight(100, 500)` and **does not** equal 100. Using the cargo weight looks dimensionally fine (both are numbers); only
    /// evaluating the formula reveals the mismatch, and every reading on the balance chart then scales by a constant.
    /// </remarks>
    [Fact]
    public void Index_ShouldUseTheFormulaAtIndexNotTheRawWeight() {
        (List<Item> _, List<StowagePosition> _, Stowage stowage, Load load) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        var formula = new Formula(MakeModel(AircraftType.B767), 100.0, 20.0, 120.0, 5000.0, 0.0);
        double expected = formula.IndexFromWeight(100.0, 500.0);
        expected.Should().NotBe(100.0);

        LinearExpressionSymbol index = load.IndexSymbol(0)!;
        index.Name.Should().Be("load_index_P1");
        index.Polynomial.Monomials[0].Symbol.Should().BeSameAs(stowage.StowageSymbol(0, 0));
        index.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(expected, 1e-9);

        double perWeight = formula.IndexFromWeight(1.0, 500.0);
        index.Polynomial.Monomials.Should().Contain(
            monomial => monomial.Symbol == load.Y![0] && System.Math.Abs(monomial.Coefficient.ToDouble() - perWeight) < 1e-9,
            "y carries the per-kilogram index");
        index.Polynomial.Monomials.Should().Contain(
            monomial => monomial.Symbol == load.Z![0] && System.Math.Abs(monomial.Coefficient.ToDouble() - perWeight) < 1e-9,
            "z carries the same per-kilogram index");
    }

    /// <summary>
    /// **`y`/`z` 必须接进内核容量行** / `y`/`z` must be linked into the kernel's capacity row.
    /// </summary>
    /// <remarks>
    /// 这是这里最危险的一处：不接容量行，模型能解、可行、报告里重量加起来对得上，只是**松弛重量对容量不可见**——
    /// 实际超载的配载会被判为合法。判据查约束名，因为"有没有接上"在数值上只有求解以后才看得出来。
    ///
    /// This is the dangerous one: without the capacity link the model solves, the solution is "feasible", the report adds up — only the **slack weight is invisible to
    /// capacity**, so an actually overloaded plan is certified legal. The criterion inspects the constraint names, because "is it wired" is otherwise only visible after solving.
    /// </remarks>
    [Fact]
    public void Register_ShouldLinkTheSlackWeightIntoTheCapacityRow() {
        (List<Item> _, List<StowagePosition> _, Stowage stowage, Load load) = Prepare();
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        IReadOnlyList<string> names = ConstraintNames(model);
        names.Should().Contain("additional_weight_nonnegative_P1",
            "the slack must be non-negative or it cancels real cargo weight");
        names.Should().Contain("additional_weight_capacity_P1",
            "the slack must consume capacity, otherwise it is weight from nowhere");
    }

    /// <summary>
    /// **单装载方案下容量联动同样生效** / The capacity link applies under the single schema too.
    /// </summary>
    /// <remarks>
    /// 基线里 `linkAdditionalWeight` 那段**不在任何 `if` 里**，所以单方案下也要接。 把它包进 `if (WithMultiLoadingSchema)`，
    /// 于是单方案下整段容量联动消失——而单方案恰恰是最常用的那种。**这条判据是那一版错误理解的留档改写**：
    /// 现在钉住"单方案下也有那两行"，与多方案成对。
    ///
    /// The baseline's `linkAdditionalWeight` block sits inside no `if`, so it applies under the single schema too. wrapped it in `if (WithMultiLoadingSchema)` and the whole
    /// link vanished under the single schema — the common case. **This criterion is the rewritten record of that misunderstanding**: it now pins "the two rows are there under the
    /// single schema as well", paired with the multi-schema case.
    /// </remarks>
    [Fact]
    public void Register_ShouldLinkTheSlackUnderTheSingleLoadingSchemaToo() {
        (List<Item> _, List<StowagePosition> _, Stowage stowage, Load load) = Prepare(multiSchema: false);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        IReadOnlyList<string> names = ConstraintNames(model);
        names.Should().Contain("additional_weight_nonnegative_P1",
            "the baseline's link is unconditional");
        names.Should().Contain("additional_weight_capacity_P1");
        load.EstimateLoadWeightSymbol(0).Should().BeNull(
            "only the estimated load weight is gated by the schema");
    }

    private static double TorquePerWeight(double arm) {
        Quantity<Flt64> force = AircraftModel.Gravity(new Quantity<Flt64>(Flt64.One, AircraftModel.WeightUnit));
        Quantity<Flt64> moment = force.Multiply(new Quantity<Flt64>(new Flt64(arm), AircraftModel.LengthUnit));
        return moment.To(AircraftModel.TorqueUnit)!.Value.Value.ToDouble();
    }

    private static IReadOnlyList<string> ConstraintNames(LinearMetaModel<Flt64> model) =>
        model.Constraints.ToList().Select(constraint => constraint.Name).ToList();
}
