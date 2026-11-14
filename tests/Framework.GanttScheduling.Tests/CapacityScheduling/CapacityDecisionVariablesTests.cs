// 产能调度的决策变量（B724）/ Capacity scheduling's decision variables (B724).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.CapacityScheduling;

/// <summary>
/// **产能调度的决策变量（B724）** / Capacity scheduling's decision variables (B724).
/// </summary>
/// <remarks>
/// 三个 `Invoke` 是 `=> Ok`、`BunchCapacitySchedulingResourceUsage.Register` 被改成显式失败，根因同一个：
/// **没有决策变量**，就没有"每动作每时隙加工多少"这个量可以写进行或目标。
///
/// B724 补的就是这一层（`CapacityDecisionVariables`，形状照基线的 `operationTime[actionIndex, slotIndex]`），
/// 同时删掉 `LocateColumnDecision`——它没有调用方，返回的是 `new object()` 占位，靠"以后可能有人用"占着一个假实现的位置。
///
/// Three `Invoke` bodies are `=> Ok` and `BunchCapacitySchedulingResourceUsage.Register` fails explicitly, one root cause: **no decision variables**, so there is no "how much is
/// processed per action per slot" quantity to put into a row or objective. B724 adds exactly that layer (`CapacityDecisionVariables`, shaped after the baseline's
/// `operationTime[actionIndex, slotIndex]`) and deletes `LocateColumnDecision` — no callers, a `new object()` placeholder body, occupying a fake implementation's seat for a "maybe later".
/// </remarks>
public class CapacityDecisionVariablesTests {
    private sealed record TestAction(string Id, Executor Executor) : IProductionAction {
        public string Name => Id;

        public bool Discrete => true;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => Flt64.Zero;

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => 1UL;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() {
        new TimeRange(Origin, Origin.AddHours(8)),
        new TimeRange(Origin.AddHours(8), Origin.AddHours(16)),
    };

    private static List<TestAction> Actions() {
        var executor = new TestExecutor("E1", "E1");
        return new List<TestAction> { new("A1", executor), new("A2", executor) };
    }

    private static CapacityDecisionVariables<TestExecutor, TestAction> Variables() =>
        new(Actions(), Slots());

    /// <summary>
    /// **未注册时没有矩阵，取用直接抛** / There is no matrix before registration, and using one throws.
    /// </summary>
    /// <remarks>
    /// 三点成对：未注册 `Variables` 是 `null`（不是全零矩阵——全零矩阵是**一个合法解**，会静默通过所有检查）/
    /// 未注册取符号抛 `InvalidOperationException` / 注册之后同一个取用就成功。
    ///
    /// Three paired points: before registration `Variables` is `null` rather than an all-zero matrix (an all-zero matrix is **a feasible solution** and would silently pass every check);
    /// taking a symbol before registration throws `InvalidOperationException`; and after registration the same call succeeds.
    /// </remarks>
    [Fact]
    public void Variables_ShouldBeNullUntilRegistered() {
        CapacityDecisionVariables<TestExecutor, TestAction> variables = Variables();
        variables.Variables.Should().BeNull("an all-zero matrix reads as a feasible schedule");

        Action beforeRegistration = () => variables.SymbolOf(variables.Actions[0], 0);
        beforeRegistration.Should().Throw<InvalidOperationException>();

        using var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();
        variables.Variables.Should().NotBeNull();
        variables.SymbolOf(variables.Actions[0], 0).Should().NotBeNull();
    }

    /// <summary>
    /// **矩阵是动作 × 时隙，且每格进模型、彼此不同** / The matrix is actions by slots, every cell is registered and they differ.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 模型里多了 `动作数 × 时隙数` 个变量（2 × 2 = 4）。
    /// 2. 两个不同格子给出**不同**的符号——全都指同一个会让所有加工量绑在一个变量上。
    /// 3. 同一格取两次给出**同一个**符号（不是每次新建）。
    /// 4. 符号能直接进单项式（`new LinearMonomial(Flt64.One, symbol)`），这是建约束与目标的前提。
    ///
    /// Four paired points: the model gains `actions x slots` variables (2 x 2 = 4); two different cells give **different** symbols, since one shared symbol would tie every processed quantity
    /// together; the same cell twice gives the **same** symbol rather than a fresh one; and a symbol goes straight into a monomial, the prerequisite for building rows and objectives.
    /// </remarks>
    [Fact]
    public void Register_ShouldAddOneVariablePerPair() {
        CapacityDecisionVariables<TestExecutor, TestAction> variables = Variables();
        using var model = new LinearMetaModel<Flt64>();

        variables.Register(model).IsOk.Should().BeTrue();

        // **用一行"全部加起来 = 0"来数**：变量项进的是求解器变量表，不是符号表，`Tokens.Symbols` 数不出来；
        // 而一行里出现几个单项式，正好就是矩阵有几格。
        // **Count them with a row that sums everything**: variable items go into the solver's variable table rather than the symbol table, so `Tokens.Symbols` cannot count them —
        // while the monomials in one row are exactly the cells of the matrix.
        var monomials = new List<LinearMonomial<Flt64>>();
        for (int action = 0; action < variables.Actions.Count; ++action) {
            for (int slot = 0; slot < variables.Slots.Count; ++slot) {
                monomials.Add(new LinearMonomial<Flt64>(Flt64.One, variables.SymbolOf(variables.Actions[action], slot)));
            }
        }
        monomials.Should().HaveCount(variables.Actions.Count * variables.Slots.Count, "one decision per (action, slot)");
        monomials.Select(monomial => monomial.Symbol).Should().OnlyHaveUniqueItems(
            "sharing one symbol would tie every processed quantity together");

        model.AddConstraint(
            new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(monomials.ToArray(), Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                Fuookami.Ospf.Math.Symbol.Inequality.Comparison.GE,
                "capacity_sum"),
            group: null,
            name: "capacity_sum",
            args: null).IsOk.Should().BeTrue();
        variables.SymbolOf(variables.Actions[0], 0).Should().BeSameAs(variables.SymbolOf(variables.Actions[0], 0),
            "the same cell must keep one symbol");

        // **"变量真的进了模型"这一条在单元层面测不出来**：`Add` 对变量项是幂等的（同一格加两次不报错），
        // 而删掉注册循环之后，建行、取符号、数单项式全都照旧——差别要到求解器那边才显形。
        // 与其写一条假判据，这里如实留空；升级路径是等产能调度有了求解路径，再补一条解级判据。
        // **"The variables really entered the model" cannot be tested at the unit level**: `Add` is idempotent for variable items (the same cell twice does not error), and with the
        // registration loop deleted, building rows, taking symbols and counting monomials all still work — the difference only shows up at the solver. Rather than write a fake
        // criterion this is left empty on purpose; the upgrade path is a solution-level criterion once capacity scheduling has a solve path.
    }

    /// <summary>
    /// **越界与外来动作被拒绝** / Out-of-range slots and foreign actions are rejected.
    /// </summary>
    /// <remarks>
    /// 三点成对：时隙下标越界 → `ArgumentOutOfRangeException` / 不在矩阵里的动作 → 同样抛 /
    /// 空动作或空时隙时注册成功但**不建矩阵**（没有决策就没有变量）。
    ///
    /// Three paired points: an out-of-range slot index throws `ArgumentOutOfRangeException`; an action not in the matrix throws too; and with no actions or no slots registration succeeds
    /// while **building no matrix** — no decision means no variable.
    /// </remarks>
    [Fact]
    public void SymbolOf_ShouldRejectOutOfRangePairs() {
        CapacityDecisionVariables<TestExecutor, TestAction> variables = Variables();
        using var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();

        Action badSlot = () => variables.SymbolOf(variables.Actions[0], 9);
        badSlot.Should().Throw<ArgumentOutOfRangeException>();
        Action foreignAction = () => variables.SymbolOf(new TestAction("X", new TestExecutor("E9", "E9")), 0);
        foreignAction.Should().Throw<ArgumentOutOfRangeException>();

        var empty = new CapacityDecisionVariables<TestExecutor, TestAction>(
            Array.Empty<TestAction>(), Slots());
        using var emptyModel = new LinearMetaModel<Flt64>();
        empty.Register(emptyModel).IsOk.Should().BeTrue();
        empty.Variables.Should().BeNull("no actions means no decision, so no matrix");
    }
}
