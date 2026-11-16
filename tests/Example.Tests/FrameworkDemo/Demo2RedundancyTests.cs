// 冗余域判据 / Criteria for the redundancy domain.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Service.Limits;
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
/// **冗余域：备用容量、纵向平衡与两条目标** / The redundancy domain: spare capacity, longitudinal balance and two objectives.
/// </summary>
/// <remarks>
/// 冗余域只有 4 个 源文件，做两件事：
///
/// 1. **备用容量** = 主甲板总容量 − 主甲板装载重量（实际与推荐各一份）+ 松弛；
/// 2. **纵向平衡** = 主甲板纵向力矩的**实际与推荐之差** + 松弛。
///
/// 两条目标都只是 `minimize(系数 × 松弛)`，形状一模一样。这类代码最典型的错法是"两件事写成一件"：
/// 推荐与实际共用一个多项式（备用容量在规划阶段就等于实际值），或者两条目标共用一个松弛（纵向平衡不再被优化）。
/// 都能编译、能求解，报告里数字也都"合理"。
///
/// The implementation domain is four sources doing two things: spare capacity (main-deck capacity minus main-deck load weight, once actual and once estimated, plus a slack) and
/// longitudinal balance (the actual-minus-estimated main-deck torque, plus a slack). Both objectives are `minimize(coefficient × slack)` with an identical shape. The classic
/// mistake is collapsing the two: sharing one polynomial between actual and estimated (spare capacity then equals the actual during planning), or sharing one slack between the two
/// objectives (the balance stops being optimised). Both compile, both solve, and the report looks reasonable either way.
/// </remarks>
public class Demo2RedundancyTests {
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
        PositionLocation location,
        double mlw,
        ulong mla) {
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

    private static (Stowage Stowage, Load Load, Redundancy Redundancy, ExperimentalLongitudinalBalance Balance) Prepare(
        bool multiSchema = true) {
        var items = new List<Item> { MakeItem("I1", 100.0) };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, PositionLocation.NormalMainLocation, 500.0, 3UL),
            MakePosition("P2", 2UL, PositionLocation.LowForwardNotBulkLocation, 900.0, 3UL),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var model = new AircraftModel(
            AircraftType.B767,
            "B767",
            new AircraftMinorModel("B767"));
        var formula = new Formula(model, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: multiSchema, aircraftModel: model, formula: formula);
        var redundancy = new Redundancy(items, positions, load);
        var balance = new ExperimentalLongitudinalBalance(positions, load);
        return (stowage, load, redundancy, balance);
    }

    private static (Load Load, Redundancy Redundancy, ExperimentalLongitudinalBalance Balance, LinearMetaModel<Flt64> Model) Register(bool multiSchema = true) {
        (Stowage stowage, Load load, Redundancy redundancy, ExperimentalLongitudinalBalance balance) = Prepare(multiSchema);
        var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();
        redundancy.Register(model).IsOk.Should().BeTrue();
        balance.Register(model).IsOk.Should().BeTrue();
        return (load, redundancy, balance, model);
    }

    /// <summary>
    /// **主甲板容量只算主甲板** / The main-deck capacity counts the main deck only.
    /// </summary>
    /// <remarks>
    /// 成对：`P1`（主，500）计入、`P2`（下，900）不计。把下货舱也算进去会让"备用容量"凭空多 900——
    /// 那不是备用容量，那是另一块甲板的容量。
    ///
    /// Paired: `P1` (main, 500) counts and `P2` (lower, 900) does not. Counting the lower hold invents 900 of "spare capacity" — which is not spare capacity on this deck at all.
    /// </remarks>
    [Fact]
    public void MainDeckCapacity_ShouldCountOnlyTheMainDeck() {
        (Stowage _, Load _, Redundancy redundancy, ExperimentalLongitudinalBalance _) = Prepare();

        redundancy.MainDeckCapacity.ToDouble().Should().BeApproximately(500.0, 1e-9);
        redundancy.MaxRedundancy.ToDouble().Should().BeApproximately(500.0, 1e-9);
        redundancy.MinRedundancy.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>
    /// **实际与推荐冗余是两份多项式** / The actual and estimated redundancy are two polynomials.
    /// </summary>
    /// <remarks>
    /// 成对：两者都以主甲板容量为常数项，但**推荐那份含 `y`/`z` 两项**（推荐装载重量的松弛与数量），实际那份不含。
    /// 共用一份时"备用容量"在规划阶段就等于实际值，目标函数对决策变量不再敏感——这正是本域存在的理由。
    ///
    /// Paired: both carry the main-deck capacity as the constant term, but the **estimated one carries the `y`/`z` terms** and the actual one does not. Sharing one polynomial makes
    /// spare capacity equal the actual value during planning, so the objective stops reacting to the decisions — which is the entire reason this domain exists.
    /// </remarks>
    [Fact]
    public void Redundancy_ShouldKeepActualAndEstimatedApart() {
        (Load load, Redundancy redundancy, ExperimentalLongitudinalBalance _, LinearMetaModel<Flt64> _) = Register();

        LinearExpressionSymbolAlias actual = new(redundancy.RedundancySymbol!);
        LinearExpressionSymbolAlias estimated = new(redundancy.PredicateRedundancySymbol!);

        actual.Name.Should().Be("redundancy");
        estimated.Name.Should().Be("predicate_redundancy");

        actual.Polynomial.Constant.ToDouble().Should().BeApproximately(500.0, 1e-9);
        estimated.Polynomial.Constant.ToDouble().Should().BeApproximately(500.0, 1e-9);

        actual.Monomials.Should().HaveCount(1, "the actual redundancy only subtracts the cargo term");
        estimated.Monomials.Should().HaveCount(3, "the estimated redundancy also subtracts y and z");
        estimated.Symbols.Should().Contain(load.Y![0]);
        estimated.Symbols.Should().Contain(load.Z![0]);
        actual.Symbols.Should().NotContain(load.Y![0]);
    }

    /// <summary>
    /// **冗余松弛对的是常数零** / The redundancy slack is taken against constant zero.
    /// </summary>
    /// <remarks>
    /// 与"下界是主甲板容量"成对：写成容量就成了"冗余不得小于总容量"，等于要求主甲板一件都不装，模型立刻不可行。
    ///
    /// Paired with "the bound is the capacity": using the capacity would mean "spare capacity must be at least the total capacity", i.e. the main deck must stay empty, and the
    /// model goes infeasible immediately.
    /// </remarks>
    [Fact]
    public void RedundancySlack_ShouldBeTakenAgainstZero() {
        (Load _, Redundancy redundancy, ExperimentalLongitudinalBalance _, LinearMetaModel<Flt64> _) = Register();

        redundancy.RedundancySlackSymbol.Should().NotBeNull();
        Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> slack = redundancy.RedundancySlackSymbol!.Polynomial;
        slack.Monomials.Should().HaveCount(2, "both the negative and the positive slack enter the result");
        slack.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);

        // 松弛的**右端**才是"下界"那一处：只断言松弛自身的多项式常数是 0 是**空判据**——右端无论取零还是取容量，
        // 结果多项式里都只有两个松弛变量、常数都是零。要真的钉住下界，得看 `SlackFunction.Y`。
        // The **right-hand side** is where the bound lives: asserting the slack's own constant is zero is a **vacuous criterion** — whether the right side is zero or the
        // capacity, the result polynomial holds two slack variables and a zero constant either way. Pinning the bound means reading `SlackFunction.Y`.
        var slackFunction = (Fuookami.Ospf.Core.Symbol.Function.SlackFunction<Flt64>)redundancy.RedundancySlackFunction!;
        slackFunction.Y.Monomials.Should().BeEmpty("the bound is a constant");
        slackFunction.Y.Constant.ToDouble().Should().BeApproximately(redundancy.MinRedundancy.ToDouble(), 1e-9,
            "the bound is the minimum redundancy, not the main-deck capacity");
        slackFunction.Y.Constant.ToDouble().Should().NotBeApproximately(redundancy.MaxRedundancy.ToDouble(), 1e-9);
    }

    /// <summary>
    /// **纵向平衡只合计主甲板，且实际对推荐** / The balance totals the main deck only, actual against estimated.
    /// </summary>
    /// <remarks>
    /// 成对断言：两份力矩的**常数都是零**（力矩是纯线性的，没有截距），但系数不同——实际那份只有货物项，
    /// 推荐那份还含 `y`/`z`。让两者相等（比如都取推荐）会让松弛恒零、目标退化成常数。
    ///
    /// Paired: both torque totals have a **zero constant** (torque is purely linear) but differ in their terms — the actual has only cargo terms while the estimated also has `y`/`z`.
    /// Making them equal (say both estimated) pins the slack at zero and the objective degenerates to a constant.
    /// </remarks>
    [Fact]
    public void LongitudinalBalance_ShouldCompareActualAgainstEstimated() {
        (Load load, Redundancy _, ExperimentalLongitudinalBalance balance, LinearMetaModel<Flt64> _) = Register();

        LinearExpressionSymbolAlias actual = new(balance.MainActualLongitudinalTorqueSymbol!);
        LinearExpressionSymbolAlias estimated = new(balance.PredicateLongitudinalTorqueSymbol!);

        actual.Name.Should().Be("main_actual_longitudinal_torque");
        estimated.Name.Should().Be("predicate_longitudinal_torque");
        actual.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
        estimated.Polynomial.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);

        actual.Monomials.Should().HaveCount(1, "one main-deck cargo term, the lower hold is excluded");
        estimated.Monomials.Should().HaveCount(3, "the estimated torque also carries the y/z weight terms");
        estimated.Symbols.Should().Contain(load.Y![0]);
        actual.Symbols.Should().NotContain(load.Y![0]);
        balance.LongitudinalTorqueSlackSymbol.Should().NotBeNull();
    }

    /// <summary>
    /// **两条目标各罚自己的松弛** / Each objective penalises its own slack.
    /// </summary>
    /// <remarks>
    /// 最容易安静的错法：两条管线共用同一个松弛，于是只有一条真正被优化，另一条的目标是前者的复制品。
    /// 判据比较两条目标各自引用到的**变量集合**，它们必须不相交。
    ///
    /// The quiet failure: both pipelines use the same slack, so only one is really optimised and the other's objective is a copy. The criterion compares the **variable sets** each
    /// objective references; they must be disjoint.
    /// </remarks>
    [Fact]
    public void Objectives_ShouldPenaliseTheirOwnSlack() {
        (Load _, Redundancy redundancy, ExperimentalLongitudinalBalance balance, LinearMetaModel<Flt64> model) = Register();

        var redundancyLimit = new RedundancyLimit(redundancy, () => new Flt64(2.0));
        var balanceLimit = new ExperimentalLongitudinalBalanceLimit(balance, () => new Flt64(3.0));

        redundancyLimit.Invoke(model).IsOk.Should().BeTrue();
        balanceLimit.Invoke(model).IsOk.Should().BeTrue();

        HashSet<IVariableItem> redundancyVars = VariablesOf(model, "redundancy");
        HashSet<IVariableItem> balanceVars = VariablesOf(model, "experimental longitudinal balance");

        redundancyVars.Should().NotBeEmpty("the redundancy objective must reference its slack variables");
        balanceVars.Should().NotBeEmpty("the balance objective must reference its slack variables");
        redundancyVars.Overlaps(balanceVars).Should().BeFalse(
            "sharing one slack would optimise only one of the two objectives");

        // 还要钉住"罚的是松弛，不是原始力矩"：罚原始力矩也能让两份变量集不相交，只是目标变成了"让力矩最小"——
        // 一个完全不同、而且方向错的优化问题。所以比较的是**变量的身份集合**，不是"非空且不相交"。
        // Pin "it penalises the slack, not the raw torque": penalising the torque also keeps the two variable sets disjoint, it just turns the objective into "minimise the torque"
        // — a different and wrongly-directed problem. So compare the **identity sets**, not merely "non-empty and disjoint".
        var expectedBalanceVars = new HashSet<IVariableItem>(
            balance.LongitudinalTorqueSlackSymbol!.Polynomial.Monomials
                .Where(monomial => monomial.Symbol is IVariableItem)
                .Select(monomial => (IVariableItem)monomial.Symbol));
        balanceVars.Should().BeEquivalentTo(expectedBalanceVars,
            "the balance objective must penalise exactly the slack's own variables");
    }

    /// <summary>
    /// **系数是调用时求值且真的乘进去了** / The coefficient is evaluated at invoke time and actually multiplies.
    /// </summary>
    /// <remarks>
    /// 成对：先用系数 1 建一次，再用系数 5 建一次，第二份目标里每个变量的系数都应当是第一份的 5 倍。
    /// 系数被忽略时两份目标完全相同——编译通过、求解通过，只是权重参数再也没有作用。
    ///
    /// Paired: build once with coefficient 1 and once with coefficient 5; every coefficient in the second objective should be five times the first. Ignoring the coefficient makes
    /// both objectives identical — it compiles, it solves, and the weight parameter simply never does anything.
    /// </remarks>
    [Fact]
    public void Objectives_ShouldScaleByTheCoefficient() {
        (Load _, Redundancy redundancy, ExperimentalLongitudinalBalance _, LinearMetaModel<Flt64> model) = Register();

        new RedundancyLimit(redundancy, () => Flt64.One).Invoke(model).IsOk.Should().BeTrue();
        new RedundancyLimit(redundancy, () => new Flt64(5.0)).Invoke(model).IsOk.Should().BeTrue();

        var coefficients = model.MetaSubObjects
            .Where(objective => objective.Name == "redundancy")
            .SelectMany(objective => objective.Polynomial.Monomials.Select(monomial => monomial.Coefficient))
            .ToList();
        coefficients.Should().HaveCount(4, "two objectives of two monomials each");
        coefficients.Count(coefficient => System.Math.Abs(coefficient.ToDouble() - 1.0) < 1e-9)
            .Should().Be(2, "the first objective keeps coefficient 1");
        coefficients.Count(coefficient => System.Math.Abs(coefficient.ToDouble() - 5.0) < 1e-9)
            .Should().Be(2, "the second objective is scaled by 5");
    }

    /// <summary>
    /// **未注册松弛时目标明确失败** / The objective fails loudly when the slack is not registered.
    /// </summary>
    /// <remarks>
    /// 返回 `Ok` 会让调用方以为目标加上了，而实际上一条目标都没有——模型照样求解，只是这个域被静默关掉。
    ///
    /// Returning `Ok` would let the caller believe the objective exists when there is none: the model still solves, the domain silently switched off.
    /// </remarks>
    [Fact]
    public void Objectives_ShouldFailWithoutARegisteredSlack() {
        (Stowage _, Load _, Redundancy redundancy, ExperimentalLongitudinalBalance balance) = Prepare();
        using var model = new LinearMetaModel<Flt64>();

        new RedundancyLimit(redundancy, () => Flt64.One).Invoke(model).IsOk.Should().BeFalse();
        new ExperimentalLongitudinalBalanceLimit(balance, () => Flt64.One).Invoke(model).IsOk.Should().BeFalse();
    }

    private static HashSet<IVariableItem> VariablesOf(LinearMetaModel<Flt64> model, string name) {
        var variables = new HashSet<IVariableItem>();
        foreach (MetaSubObject<Flt64> objective in model.MetaSubObjects.Where(objective => objective.Name == name)) {
            foreach (Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in objective.Polynomial.Monomials) {
                if (monomial.Symbol is IVariableItem variable) {
                    variables.Add(variable);
                }
            }
        }
        return variables;
    }

    /// <summary>
    /// 只读地看一个线性中间符号 / A read-only view of a linear intermediate symbol.
    /// </summary>
    /// <remarks>
    /// 只为让判据少写一层 `Polynomial.` 前缀，不承担别的职责。
    ///
    /// Only there so the criteria avoid a `Polynomial.` prefix; no other responsibility.
    /// </remarks>
    private readonly struct LinearExpressionSymbolAlias
    {
        private readonly Core.Symbol.LinearExpressionSymbol _inner;

        public LinearExpressionSymbolAlias(Core.Symbol.LinearExpressionSymbol inner) => _inner = inner;

        public string Name => _inner.Name;

        public Math.Symbol.Polynomial.LinearPolynomial<Flt64> Polynomial => _inner.Polynomial;

        public IReadOnlyList<Math.Symbol.Monomial.LinearMonomial<Flt64>> Monomials => _inner.Polynomial.Monomials;

        public IEnumerable<Math.Symbol.ISymbol> Symbols =>
            _inner.Polynomial.Monomials.Select(monomial => monomial.Symbol);
    }
}
