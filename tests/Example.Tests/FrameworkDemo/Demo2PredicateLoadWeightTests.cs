// 谓词装载重量三元组、松弛与上界约束的判据 / Criteria for the predicate load weight triple, slack and upper bound row.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **谓词装载重量：三元组、松弛与上界** / The predicate load weight: triple, slack and upper bound.
/// </summary>
/// <remarks>
/// C# 之前把`PositionPredicateLoadWeight`（`plw`/`min`/`max` 三元组）压成一个 `double?`，于是两处用量都丢了。
/// 这里补回三元组，并把两处接上：
///
/// | 用处 | 取哪一端 | 落在哪 |
/// | --- | --- | --- |
/// | `y_j ≤ plw.max` | `max` | `PredicateLoadWeightLimit` |
/// | 松弛右端 | `min` | `Load.predicateLoadWeightSlack` |
///
/// 取错一端的后果不同但都安静：取错上界只让 `y` 顶得更高（求解器不抱怨）；取错下界让松弛量整体偏移一个常数。
///
/// C# had flattened its triple into a single `double?`, losing both uses. restores the triple and wires both: `y_j ≤ plw.max` in
/// `PredicateLoadWeightLimit` and the slack's right-hand side using `min`. Taking the wrong end fails quietly in both cases: a wrong upper bound simply lets `y` climb higher (the
/// solver never complains) and a wrong lower bound shifts the slack by a constant.
/// </remarks>
public class Demo2PredicateLoadWeightTests {
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

    private static StowagePosition MakePosition(
        string name,
        ulong id,
        double mlw,
        PositionPredicateLoadWeight? plw,
        bool predicateWeightNeeded = true,
        bool stowageNeeded = true) {
        var position = new Position(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: 0,
            coordinate: new PositionCoordinate(500.0, 500.0, 60.0, 60.0),
            shape: new PositionShape(1.0, 1.0, 1.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(
                stowageNeeded: stowageNeeded,
                adjustmentNeeded: false,
                predicateWeightNeeded: predicateWeightNeeded,
                recommendedWeightNeeded: false),
            mlw: new MaxLoadWeightLimit(mlw),
            mla: 3UL,
            plw: plw);
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

    private static (List<Item> Items, List<StowagePosition> Positions, Stowage Stowage, Load Load) Prepare(
        PositionPredicateLoadWeight? plw,
        bool predicateWeightNeeded = true,
        bool stowageNeeded = true) {
        var items = new List<Item> { MakeItem("I1") };
        var positions = new List<StowagePosition> {
            MakePosition("P1", 1UL, 1000.0, plw, predicateWeightNeeded, stowageNeeded),
        };
        var stowage = new Stowage(items, positions) { LoadingContext = MakeKernel(items, positions) };
        var aircraftModel = new AircraftModel(
            AircraftType.B767,
            "B767",
            new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure.AircraftMinorModel("B767"));
        var formula = new Formula(aircraftModel, 100.0, 20.0, 120.0, 5000.0, 0.0);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true, aircraftModel: aircraftModel, formula: formula);
        return (items, positions, stowage, load);
    }

    /// <summary>
    /// **上界约束取 `plw.max`，不取 `mlw`** / The upper bound row uses `plw.max`, not `mlw`.
    /// </summary>
    /// <remarks>
    /// 成对：`y` 的变量范围本来就到 `mlw`（1000），而谓词规格上界是 400。判据查约束的**右端常数**是 400 且不是 1000——
    /// 少了这一行 `y` 会顶到 1000，求解器不会有任何抱怨。
    ///
    /// Paired: `y`'s declared range already reaches `mlw` (1000) while the predicate bound is 400. The criterion checks the row's **right-hand constant** is 400 and not 1000 —
    /// without the row `y` climbs to 1000 and the solver never complains.
    /// </remarks>
    [Fact]
    public void Limit_ShouldBoundByThePredicateMaxNotThePositionMaximum() {
        (List<Item> items, List<StowagePosition> positions, Stowage stowage, Load load) =
            Prepare(new PositionPredicateLoadWeight(Plw: 350.0, Min: 300.0, Max: 400.0));
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        new PredicateLoadWeightLimit(positions, load).Invoke(model).IsOk.Should().BeTrue();

        LinearInequalityRow row = SingleRow(model, "predicate_load_weight_limit_P1");
        row.Right.Should().BeApproximately(400.0, 1e-9, "the row uses plw.max");
        row.Right.Should().NotBeApproximately(1000.0, 1e-9, "the row must not use the position's mlw");
        row.LeftSymbols.Should().ContainSingle().Which.Should().BeSameAs(load.Y![0]);
        _ = items;
    }

    /// <summary>
    /// **松弛右端取 `plw.min`，且乘上"该舱位装了几件"** / The slack's right side uses `plw.min` times the position's stowage count.
    /// </summary>
    /// <remarks>
    /// 需要装载决策的舱位：右端是 `min × Σ stowage`，不是常数 `min`——空舱位没有下界要求，写成常数会让空舱位也必须"装满下界"，
    /// 那是一个凭空多出来的约束。成对：不需要决策的舱位右端就是常数 `min`。
    ///
    /// A position needing a stowage decision gets `min × Σ stowage`, not the constant `min`: an empty position has no lower bound, and a constant would demand every position meet
    /// it even when empty — a constraint out of nowhere. Paired: a position without the decision gets the constant `min`.
    /// </remarks>
    [Fact]
    public void Slack_ShouldUseThePredicateMinTimesTheStowageCount() {
        (List<Item> _, List<StowagePosition> positions, Stowage stowage, Load load) =
            Prepare(new PositionPredicateLoadWeight(Plw: 350.0, Min: 300.0, Max: 400.0));
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        var slack = (SlackFunction<Flt64>)load.PredicateLoadWeightSlackFunction(0)!;
        slack.X.Monomials.Should().ContainSingle().Which.Symbol.Should().BeSameAs(load.Y![0]);
        slack.Y.Monomials.Should().NotBeEmpty("a position with a decision scales the bound by its stowage count");
        slack.Y.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
        slack.Y.Monomials.Should().OnlyContain(
            monomial => System.Math.Abs(monomial.Coefficient.ToDouble() - 300.0) < 1e-9);
        slack.Y.Monomials[0].Symbol.Should().BeSameAs(stowage.StowageSymbol(0, 0));
        _ = positions;
    }

    /// <summary>
    /// **不需要决策时右端是常数 `min`** / Without a decision the right side is the constant `min`.
    /// </summary>
    /// <remarks>
    /// 与上一条成对。写成"总是乘装载量"会让不需要决策的舱位右端恒为零——松弛恒等于 `y`，目标里那一项变成"最小化 `y`"，
    /// 与谓词重量再无关系。
    ///
    /// Paired with the previous one. Always multiplying by the count would pin a no-decision position's right side at zero — the slack becomes `y` itself and the objective degrades
    /// into "minimise `y`", unrelated to the predicate weight.
    /// </remarks>
    [Fact]
    public void Slack_ShouldUseAConstantRightSideWithoutAStowageDecision() {
        (List<Item> _, List<StowagePosition> _, Stowage stowage2, Load load2) = Prepare(
            new PositionPredicateLoadWeight(350.0, 300.0, 400.0),
            stowageNeeded: false);
        using var model2 = new LinearMetaModel<Flt64>();
        stowage2.Register(model2).IsOk.Should().BeTrue();
        load2.Register(model2).IsOk.Should().BeTrue();

        var slack = (SlackFunction<Flt64>)load2.PredicateLoadWeightSlackFunction(0)!;
        slack.Y.Monomials.Should().BeEmpty("no decision means a constant bound");
        slack.Y.Constant.ToDouble().Should().BeApproximately(300.0, 1e-9);
    }

    /// <summary>
    /// **不需要谓词重量的舱位拿常数零符号，不是空洞** / A position that needs no predicate weight gets a constant-zero symbol, not a hole.
    /// </summary>
    /// <remarks>
    /// 与"需要时是松弛函数"成对。留空会让下游按下标取用时处处判空，而判空漏一处就是空引用；常数零的语义也正确：没有要求就没有差距。
    ///
    /// Paired with "it is a slack function when needed". Leaving a hole forces a null check at every index-based lookup (miss one and it is a null reference), and the constant zero is
    /// semantically right anyway: no requirement, no gap.
    /// </remarks>
    [Fact]
    public void Slack_ShouldBeConstantZeroWhenThePredicateWeightIsNotNeeded() {
        (List<Item> _, List<StowagePosition> _, Stowage stowage, Load load) = Prepare(
            new PositionPredicateLoadWeight(350.0, 300.0, 400.0),
            predicateWeightNeeded: false);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();
        load.Register(model).IsOk.Should().BeTrue();

        ILinearIntermediateSymbolProxy symbol = new(load.PredicateLoadWeightSlackSymbol(0)!);
        symbol.Monomials.Should().BeEmpty();
        symbol.Constant.ToDouble().Should().BeApproximately(0.0, 1e-9);
        load.PredicateLoadWeightSlackSymbol(0).Should().NotBeOfType<SlackFunction<Flt64>>();
    }

    /// <summary>
    /// **缺少谓词规格是错误，不是跳过** / A missing specification is an error, not a skip.
    /// </summary>
    /// <remarks>
    /// 跳过会让该舱位的 `y` 无上界，而报告里看不出少了哪一行——这类"安静地少一行约束"是最难查的一类缺陷。
    ///
    /// Skipping leaves that position's `y` unbounded with nothing in the report to say a row is missing — the hardest class of defect to diagnose.
    /// </remarks>
    [Fact]
    public void Limit_And_Register_ShouldFailWithoutAPredicateSpecification() {
        (List<Item> _, List<StowagePosition> positions, Stowage stowage, Load load) =
            Prepare(plw: null);
        using var model = new LinearMetaModel<Flt64>();
        stowage.Register(model).IsOk.Should().BeTrue();

        load.Register(model).IsOk.Should().BeFalse("the slack cannot be built without a lower bound");
        new PredicateLoadWeightLimit(positions, load).Invoke(model).IsOk.Should().BeFalse();
    }

    private static LinearInequalityRow SingleRow(LinearMetaModel<Flt64> model, string name) {
        var match = (Fuookami.Ospf.Core.Model.Mechanism.LinearInequalityConstraint<Flt64>)model.Constraints
            .Single(constraint => constraint.Name == name);
        Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64> inequality = match.Inequality;
        return new LinearInequalityRow(
            inequality.Lhs.Constant.ToDouble(),
            inequality.Rhs.Constant.ToDouble(),
            inequality.Lhs.Monomials.Select(monomial => monomial.Symbol).ToList());
    }

    /// <summary>
    /// 行走约束的最小只读视图 / A minimal read-only view of a row-shaped constraint.
    /// </summary>
    private readonly struct LinearInequalityRow
    {
        public LinearInequalityRow(
            double left,
            double right,
            IReadOnlyList<Fuookami.Ospf.Math.Symbol.ISymbol> leftSymbols) {
            LeftConstant = left;
            Right = right;
            LeftSymbols = leftSymbols;
        }

        public double LeftConstant { get; }

        public double Right { get; }

        public IReadOnlyList<Fuookami.Ospf.Math.Symbol.ISymbol> LeftSymbols { get; }
    }

    /// <summary>
    /// 线性中间符号的最小只读视图 / A minimal read-only view of a linear intermediate symbol.
    /// </summary>
    private readonly struct ILinearIntermediateSymbolProxy
    {
        private readonly Core.Symbol.ILinearIntermediateSymbol<Flt64> _inner;

        public ILinearIntermediateSymbolProxy(Core.Symbol.ILinearIntermediateSymbol<Flt64> inner) => _inner = inner;

        public IReadOnlyList<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>> Monomials =>
            _inner.Polynomial.Monomials;

        public Flt64 Constant => _inner.Polynomial.Constant;
    }
}
