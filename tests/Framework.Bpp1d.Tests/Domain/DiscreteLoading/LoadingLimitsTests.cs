// 离散装载限制判据 / Criteria for the discrete-loading limits.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Service.Limits;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using PositionConflict = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.DiscreteLoading;

/// <summary>
/// **离散装载的限制类** / The discrete-loading limit classes.
/// </summary>
/// <remarks>
/// 对应的两个限制类。**判据不跑求解器，直接读回约束的不等号方向与右端系数**——
/// 因为最容易写错的恰恰是这两处：
///
/// | 点 | 错法 | 后果 |
/// | --- | --- | --- |
/// | 必装货物用 `≤` 而非 `==` | 与普通货物同一分支 | 求解器**一件都不装**，还认为那是更好的解 |
/// | 件数上界只取 `maxCount` | 不取 `min(maxCount, 可用分配数)` | 上界**永远达不到**，等于没有约束 |
/// | 件数/重量不乘 `occupied` | 右端是常数 | 可以在**未占用**的舱位堆东西 |
///
/// **The criteria read the comparison direction and the right-hand side coefficient straight off the model instead of running a solver**, because those two are exactly
/// what goes wrong (table above): a mandatory cargo written as `&lt;=` is indistinguishable from an optional one, so the solver **loads none** and may even prefer that;
/// a count bound of `maxCount` alone is **never reachable**, i.e. no constraint at all; and dropping the `occupied` factor lets cargo stack on a **never-occupied**
/// position.
/// </remarks>
public class LoadingLimitsTests {
    /// <summary>**必装货物用 `==`，其余用 `≤`** / A mandatory cargo uses `==`, everything else `&lt;=`.</summary>
    /// <remarks>
    /// **这是这里最重要的一条判据。** 两者约束名、列数、右端常数**完全一样**，只有**不等号方向**不同。
    /// 任何"数约束条数"的判据都发现不了。
    ///
    /// **The most important criterion in.** The two rows have identical names, identical term counts and identical right-hand constants — only the
    /// **comparison direction** differs. No constraint-counting assertion can find it.
    /// </remarks>
    [Fact]
    public void CapacityLimit_ShouldUseEqualityForMandatoryCargoAndInequalityOtherwise() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, true, null), ("I2", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);

        LinearMetaModel<Flt64> model = BuildModel(problem, out LoadingAggregation aggregation, out _);
        Try result = new LoadingCapacityLimit<Flt64>(problem, aggregation, Capacities(problem)).Invoke(model);

        result.IsOk.Should().BeTrue(result.ToString());
        SignOf(model, "loading_capacity_item_I1").Should().Be(
            Comparison.EQ, "I1 is required, so it must be loaded exactly once, not at most once");
        SignOf(model, "loading_capacity_item_I2").Should().Be(
            Comparison.LE, "I2 is optional, so it is only bounded above");
    }

    /// <summary>**件数上界是 `min(最大件数, 可用分配数)`** / The count bound is `min(maxCount, available assignments)`.</summary>
    /// <remarks>
    /// 舱位最大 5 件，但只有 **2** 条合法分配能落到它上面 ⇒ 正确上界是 **2**。
    /// 若只取 `maxCount`，上界 5 **永远达不到**——约束存在但无效。
    ///
    /// The position allows 5, but only **2** legal assignments can land on it, so the bound must be **2**. Using `maxCount` alone gives 5, **never reachable** — a
    /// constraint that exists and does nothing.
    /// </remarks>
    [Fact]
    public void CapacityLimit_ShouldBoundTheCountByTheAvailableAssignments() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null), ("I2", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);

        LinearMetaModel<Flt64> model = BuildModel(problem, out LoadingAggregation aggregation, out _);
        new LoadingCapacityLimit<Flt64>(problem, aggregation, Capacities(problem)).Invoke(model)
            .IsOk.Should().BeTrue();

        RhsCoefficientOf(model, "loading_capacity_count_P1").Should().BeApproximately(
            2.0, 1e-12, "only two assignments reach P1, so 5 is unreachable");
    }

    /// <summary>**件数与重量都乘 `occupied`，右端不是常数** / Both the count and the weight are multiplied by `occupied`, so neither right-hand side is a constant.</summary>
    /// <remarks>
    /// 反向对照：若右端是纯常数，`Weights ≤ capacity` 就成了"舱位无论是否占用都不能超重"，
    /// **未占用的舱位也能装东西**——占用与装载脱节。
    ///
    /// Reverse control: a constant right-hand side turns `Weights &lt;= capacity` into "a position may hold weight whether or not it is occupied", which decouples
    /// occupancy from loading.
    /// </remarks>
    [Fact]
    public void CapacityLimit_ShouldTieBothBoundsToTheOccupancyVariable() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);

        LinearMetaModel<Flt64> model = BuildModel(problem, out LoadingAggregation aggregation, out _);
        new LoadingCapacityLimit<Flt64>(problem, aggregation, Capacities(problem)).Invoke(model)
            .IsOk.Should().BeTrue();

        LinearInequality<Flt64> countRow = RowOf(model, "loading_capacity_count_P1");
        countRow.Rhs.Monomials.Should().HaveCount(1, "the count bound must carry the occupancy variable");
        LinearInequality<Flt64> weightRow = RowOf(model, "loading_capacity_weight_P1");
        weightRow.Rhs.Monomials.Should().HaveCount(1, "the weight bound must carry the occupancy variable");
        weightRow.Rhs.Constant.ToDouble().Should().BeApproximately(0.0, 1e-12);
    }

    /// <summary>**重量上界等于归一化后的容量** / The weight bound equals the normalised capacity.</summary>
    [Fact]
    public void CapacityLimit_ShouldUseTheNormalisedCapacityAsTheWeightBound() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);

        LinearMetaModel<Flt64> model = BuildModel(problem, out LoadingAggregation aggregation, out _);
        new LoadingCapacityLimit<Flt64>(problem, aggregation, Capacities(problem)).Invoke(model)
            .IsOk.Should().BeTrue();

        CoefficientOfMonomial(RowOf(model, "loading_capacity_weight_P1").Rhs.Monomials[0])
            .Should().BeApproximately(5000.0, 1e-9, "the problem's weight unit is grams");
    }

    /// <summary>**团约束是 `Σ occupied ≤ 1`** / A clique row is `Σ occupied &lt;= 1`.</summary>
    [Fact]
    public void ConflictLimit_ShouldEmitOneAtMostOneRowPerClique() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)],
            conflicts: [("P1", "P2")]);

        LinearMetaModel<Flt64> model = BuildModel(problem, out LoadingAggregation aggregation, out ConflictIndex index);
        Try result = new LoadingConflictLimit<Flt64>(problem, aggregation, index).Invoke(model);

        result.IsOk.Should().BeTrue(result.ToString());
        LinearInequality<Flt64> row = RowOf(model, "loading_conflict_clique_0");
        row.Comparison.Should().Be(Comparison.LE);
        row.Lhs.Monomials.Should().HaveCount(2, "the clique holds P1 and P2");
        row.Rhs.Constant.ToDouble().Should().BeApproximately(1.0, 1e-12);
    }

    /// <summary>**布局组恰选一种，且每个受约束舱位需有覆盖它的布局** / A layout group picks exactly one layout, and each governed position needs a covering layout.</summary>
    /// <remarks>
    /// 两条约束缺一不可：只有 `Σ layout == 1` 时，**布局与占用可以互相矛盾**——
    /// 选了一个不含 `P1` 的布局却仍然占用 `P1`。第二条把两者绑在一起。
    ///
    /// Both rows are required. With only `Σ layout == 1`, **the layout and the occupancy can contradict each other** — a layout excluding `P1` chosen while `P1` stays
    /// occupied. The second row ties them together.
    /// </remarks>
    [Fact]
    public void ConflictLimit_ShouldTieEveryGovernedPositionToTheChosenLayout() {
        var group = new LayoutSelectionGroup(
            "G1",
            new HashSet<string> { "P1", "P2" },
            new List<LoadingLayout> {
                new("L1", new HashSet<string> { "P1" }),
                new("L2", new HashSet<string> { "P2" }),
            });
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)],
            layoutGroups: [group]);

        LinearMetaModel<Flt64> model = BuildModel(problem, out LoadingAggregation aggregation, out ConflictIndex index);
        Try result = new LoadingConflictLimit<Flt64>(problem, aggregation, index).Invoke(model);

        result.IsOk.Should().BeTrue(result.ToString());
        LinearInequality<Flt64> chosen = RowOf(model, "loading_conflict_layout_G1");
        chosen.Comparison.Should().Be(Comparison.EQ, "exactly one layout must be selected");
        chosen.Lhs.Monomials.Should().HaveCount(2, "the group offers two layouts");
        chosen.Rhs.Constant.ToDouble().Should().BeApproximately(1.0, 1e-12);

        LinearInequality<Flt64> governed = RowOf(model, "loading_conflict_layout_G1_P1");
        governed.Comparison.Should().Be(Comparison.LE, "occupancy is implied by a covering layout");
        governed.Lhs.Monomials.Should().HaveCount(1, "the left side is occupied_P1 alone");
        governed.Rhs.Monomials.Should().HaveCount(
            1, "only L1 covers P1, so the right side carries exactly one layout variable");
    }

    /// <summary>**禁用舱位上的固定货物仍然建变量** / A fixed cargo on a disabled position still gets its variable.</summary>
    /// <remarks>
    /// **第一版判据用了"单件超重的固定货物"，跑不起来**：基线 `validate` 的第 11 条
    /// "固定装载不超容量"**先把它拒了**——所以那种输入**根本到不了建变量这一步**。
    ///
    /// 改用**禁用舱位**：`validate` 允许（不是固定装载超容量），而 `register` 会给该舱位
    /// `range.eq(true)`（因为它是固定舱位之一）。此时跳过规则里的 `且` 决定一切：
    ///
    /// | 连接词 | 结果 |
    /// | --- | --- |
    /// | **`且`（正确）** | 固定 ⇒ 不跳过 ⇒ 建变量 ⇒ 模型可满足 |
    /// | `或`（错） | 禁用 ⇒ 跳过 ⇒ **不建变量**，但占位变量被钉为 1 ⇒ **模型恒不可行** |
    ///
    /// **The first version of this criterion used a fixed **overweight** cargo and could not run**: baseline `validate`'s check 11, "fixed load within capacity",
    /// **rejects it first**, so that input **never reaches variable creation**. A **disabled position** is used instead: `validate` allows it (it is not an
    /// over-capacity fixed load) while `register` pins that position to `range.eq(true)` because it holds a fixed cargo. The `and` in the skip rule now decides
    /// everything (table above): with `or`, the assignment is skipped, **no variable exists**, yet the occupancy variable is pinned to 1 — **the model becomes
    /// trivially infeasible**.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldStillCreateTheVariableForAFixedCargoOnADisabledPosition() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, "P1")],
            positions: [("P1", 5UL, 5000.0)],
            disabled: ["P1"]);

        _ = BuildModel(problem, out LoadingAggregation aggregation, out _);

        aggregation.Assignments.Should().ContainKey(
            new AssignmentKey("I1", "P1"),
            "I1 is fixed to the disabled P1: the assignment variable must exist so the pinned occupancy stays satisfiable");
    }

    /// <summary>**不可用且未固定的分配不建变量** / An unusable, unfixed assignment gets no variable.</summary>
    /// <remarks>
    /// 反向对照：若跳过规则被删掉，一个单件就超重的分配也会建变量——
    /// 那条分配**永远不可能取 1**，白白扩大模型。
    ///
    /// Reverse control: without the skip rule, an assignment whose single piece already exceeds capacity still gets a variable — it **can never take value 1** and
    /// merely inflates the model.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldSkipAnUnusableUnfixedAssignment() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 9000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);

        _ = BuildModel(problem, out LoadingAggregation aggregation, out _);

        aggregation.Assignments.Should().NotContainKey(
            new AssignmentKey("I1", "P1"),
            "a single piece of 9000 g can never fit a 5000 g position");
    }

    /// <summary>**固定货物只与自己固定的舱位之间有变量** / A fixed cargo only keeps the variable for its own fixed position.</summary>
    [Fact]
    public void Aggregation_ShouldKeepOnlyTheFixedPositionForAFixedCargo() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, "P1")],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)]);

        _ = BuildModel(problem, out LoadingAggregation aggregation, out _);

        aggregation.Assignments.Should().ContainKey(new AssignmentKey("I1", "P1"));
        aggregation.Assignments.Should().NotContainKey(
            new AssignmentKey("I1", "P2"), "I1 is fixed to P1, so the P2 pairing must not exist");
    }

    /// <summary>构造模型并建变量 / Build the model and create the variables.</summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="aggregation">建出的聚合 / The resulting aggregation.</param>
    /// <param name="index">团覆盖 / The clique cover.</param>
    /// <returns>元模型 / The meta model.</returns>
    private static LinearMetaModel<Flt64> BuildModel(
        DiscreteLoadingProblem<Flt64> problem,
        out LoadingAggregation aggregation,
        out ConflictIndex index) {
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        validator.Validate().IsOk.Should().BeTrue("the fixtures must be legal problems");
        var model = new LinearMetaModel<Flt64>();
        aggregation = new LoadingAggregation();
        aggregation.Build(problem, validator.Capacities, validator.ItemWeights, model).IsOk.Should().BeTrue();
        index = ConflictIndex.Build(problem.Conflicts);
        return model;
    }

    /// <summary>归一化容量 / The normalised capacities.</summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <returns>容量表 / The capacity map.</returns>
    private static IReadOnlyDictionary<string, Flt64> Capacities(DiscreteLoadingProblem<Flt64> problem) {
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        validator.Validate().IsOk.Should().BeTrue();
        return validator.Capacities;
    }

    /// <summary>按名称取出行 / Find a row by name.</summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="name">约束名 / Constraint name.</param>
    /// <returns>线性不等式 / The linear inequality.</returns>
    private static LinearInequality<Flt64> RowOf(LinearMetaModel<Flt64> model, string name) {
        foreach (LinearInequalityConstraint<Flt64> row in model.RelationConstraints) {
            if (row.Name == name) {
                return row.Inequality;
            }
        }
        throw new global::System.InvalidOperationException($"no constraint named {name}");
    }

    /// <summary>按名称取出不等号方向 / Read the comparison direction of a named row.</summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="name">约束名 / Constraint name.</param>
    /// <returns>比较方向 / The comparison.</returns>
    private static Comparison SignOf(LinearMetaModel<Flt64> model, string name) {
        foreach (LinearInequalityConstraint<Flt64> row in model.RelationConstraints) {
            if (row.Name == name) {
                return row.Sign;
            }
        }
        throw new global::System.InvalidOperationException($"no constraint named {name}");
    }

    /// <summary>右端单项式的系数 / The coefficient of the right-hand monomial.</summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="name">约束名 / Constraint name.</param>
    /// <returns>系数 / The coefficient.</returns>
    private static double RhsCoefficientOf(LinearMetaModel<Flt64> model, string name) =>
        CoefficientOfMonomial(RowOf(model, name).Rhs.Monomials[0]);

    /// <summary>取单项式系数 / Read a monomial's coefficient.</summary>
    /// <param name="monomial">单项式 / The monomial.</param>
    /// <returns>系数的双精度值 / The coefficient as a double.</returns>
    private static double CoefficientOfMonomial(Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial) =>
        monomial.Coefficient.ToDouble();

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="items">货物：(标识, 克重, 必装, 固定舱位) / Items: (identity, grams, required, fixed position).</param>
    /// <param name="positions">舱位：(标识, 最大件数, 克容量) / Positions: (identity, max count, gram capacity).</param>
    /// <param name="conflicts">互斥对 / Conflicting pairs.</param>
    /// <param name="layoutGroups">布局选择组 / Layout selection groups.</param>
    /// <returns>问题 / The problem.</returns>
    private static DiscreteLoadingProblem<Flt64> MakeProblem(
        (string Id, double Weight, bool Required, string? Fixed)[] items,
        (string Id, ulong MaxCount, double Capacity)[] positions,
        (string First, string Second)[]? conflicts = null,
        IReadOnlyList<LayoutSelectionGroup>? layoutGroups = null,
        string[]? disabled = null) {
        var disabledIds = new HashSet<string>(disabled ?? []);
        var itemList = new List<LoadingItem<Flt64>>(items.Length);
        foreach ((string id, double weight, bool required, string? fixedPosition) in items) {
            itemList.Add(new LoadingItem<Flt64>(id, Grams(weight), Required: required, FixedPositionId: fixedPosition));
        }
        var positionList = new List<LoadingPosition<Flt64>>(positions.Length);
        foreach ((string id, ulong maxCount, double capacity) in positions) {
            positionList.Add(new LoadingPosition<Flt64>(id, maxCount, Grams(capacity), Enabled: !disabledIds.Contains(id)));
        }

        var assignments = new HashSet<AssignmentKey>();
        foreach (LoadingItem<Flt64> item in itemList) {
            foreach (LoadingPosition<Flt64> position in positionList) {
                assignments.Add(new AssignmentKey(item.Id, position.Id));
            }
        }

        var conflictSet = new HashSet<PositionConflict>();
        if (conflicts is not null) {
            foreach ((string first, string second) in conflicts) {
                conflictSet.Add(new PositionConflict(first, second));
            }
        }

        return new DiscreteLoadingProblem<Flt64>(
            Items: itemList,
            Positions: positionList,
            AllowedAssignments: assignments,
            Conflicts: conflictSet,
            LayoutGroups: layoutGroups ?? new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
    }

    /// <summary>构造克量 / Build a gram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Grams(double value) =>
        new(new Flt64(value), MassUnits.Gram);
}
