// 设备产能约束压在数量上/ The executor capacity constraint presses on the amounts.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.CapacityScheduling;

/// <summary>
/// **设备产能约束压在数量上** / The executor capacity constraint presses on the amounts.
/// </summary>
/// <remarks>
/// 两代缺陷叠在这里：第一版是 `Invoke =&gt; Ok`（什么都没建）；第二版建了行，但压的是 `CapacityUsageVariables`
/// 那组**自由变量**——它们与数量之间没有任何行相连，所以"占用 ≤ 可用时长"对数量毫无约束力。
///
/// 起容量是**表达式**：`capacity[e, s] = Σ_{执行器为 e 的动作} Σ_顺序位 x`，约束直接压到数量上。
///
/// Two generations of defects stack here: the first version was `Invoke =&gt; Ok` (building nothing); the second built rows but pressed on `CapacityUsageVariables`, **free variables**
/// with no row tying them to the amounts, so "occupancy within available duration" constrained nothing. Since capacity is an **expression** —
/// `sum of x over the executor's actions and positions` — and the constraint presses directly on the amounts.
/// </remarks>
public class ExecutorCapacityConstraintTests {
    private sealed record TestLoadAction(string Id, Executor Executor) : IProductionAction {
        public string Name => Id;

        public bool Discrete => true;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => Flt64.Zero;

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => 9UL;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() {
        new TimeRange(Origin, Origin.AddHours(8)),
    };

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static List<TestLoadAction> Actions() {
        var first = new TestExecutor("E1", "E1");
        var second = new TestExecutor("E2", "E2");
        return new List<TestLoadAction> { new("A1", first), new("A2", first), new("A3", second) };
    }

    /// <summary>把变量层与表达式层注册进给定模型 / Register the variable and expression layers into a given model.</summary>
    /// <param name="model">模型 / The model.</param>
    /// <returns>占用表达式层 / The load expression layer.</returns>
    private static CapacityLoadExpressions<TestLoadAction> Register(LinearMetaModel<Flt64> model) {
        var variables = new CapacityOrderVariables<TestLoadAction>(Actions(), Slots(), Window(), 2UL);
        variables.Register(model).IsOk.Should().BeTrue();
        var load = new CapacityLoadExpressions<TestLoadAction>(variables, Window());
        load.Register(model).IsOk.Should().BeTrue();
        return load;
    }

    /// <summary>
    /// **每个 (执行器, 时隙) 一行，右端项是窗口单位下的可用时长** / One row per (executor, slot), with the window-unit duration on the right.
    /// </summary>
    /// <remarks>
    /// 三点成对：行数 = 执行器数 × 时隙数（2 × 1 = 2）/ 行名带执行器标识 / 右端项是 `1`（8 小时时隙在 8 小时刻度下），不是 8 也不是 28800。
    ///
    /// Three paired points: the row count equals executors x slots (2 x 1 = 2); the row names carry the executor id; and the right-hand side is `1` (an 8-hour slot on an 8-hour scale),
    /// neither 8 nor 28800.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddOneRowPerExecutorAndSlot() {
        using var model = new LinearMetaModel<Flt64>();
        CapacityLoadExpressions<TestLoadAction> load = Register(model);
        ExecutorCapacityConstraint<TestLoadAction> constraint = new(load, Window());

        constraint.Invoke(model).IsOk.Should().BeTrue();

        constraint.Name.Should().Be("executor_capacity");
        constraint.Lazy.Should().BeFalse();
        model.RelationConstraints.Should().HaveCount(load.Executors.Count * load.Slots.Count);
        model.RelationConstraints.Select(row => row.Name).Should().Contain(new[] {
            "executor_capacity_E1_0",
            "executor_capacity_E2_0",
        });
        foreach (Fuookami.Ospf.Core.Model.Mechanism.LinearInequalityConstraint<Flt64> row in model.RelationConstraints) {
            row.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0, "8 hours measured in 8-hour units is 1");
            row.Sign.Should().Be(Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE);
        }
    }

    /// <summary>
    /// **左端是数量的和，不是一组自由变量** / The left-hand side is a sum of amounts, not free variables.
    /// </summary>
    /// <remarks>
    /// 三点成对：E1 的容量表达式含 2 个 `operationTime`（每个动作一个）/ E2 的含 1 个 /
    /// 两个执行器的表达式**不是同一个符号**。第一条正是 那个自由变量版做不到的：那时左端只有一个变量项，
    /// 与数量无关，所以超卖也不会被拦住。
    ///
    /// Three paired points: E1's capacity expression holds 2 `operationTime` symbols (one per action); E2's holds 1; and the two executors' expressions are
    /// **not the same symbol**. The first is exactly what the earlier free-variable version could not do: the left-hand side was a single variable item unrelated to the amounts, so
    /// oversubscription could not be stopped.
    /// </remarks>
    [Fact]
    public void CapacityExpression_ShouldSumTheAmounts() {
        using var model = new LinearMetaModel<Flt64>();
        CapacityLoadExpressions<TestLoadAction> load = Register(model);

        load.CapacityOf(0, 0).Should().NotBeSameAs(load.CapacityOf(1, 0), "each executor has its own capacity");

        var first = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)load.CapacityOf(0, 0);
        var second = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)load.CapacityOf(1, 0);
        // **容量表达式引用的是 `operationTime` 符号**（每个动作一个），不是直接铺开所有格子——两层表达式，
        // 展开成数量要经 `operationTime` 这一层。 的自由变量版连这一层都没有。
        //
        // **The capacity expression references the `operationTime` symbols** (one per action) rather than flattening every cell — two expression layers, and reaching the amounts goes
        // through `operationTime`. the earlier free-variable version had no such layer at all.
        first.Polynomial!.Monomials.Should().HaveCount(2, "one operationTime per E1 action");
        second.Polynomial!.Monomials.Should().HaveCount(1, "one operationTime for the single E2 action");
    }

    /// <summary>
    /// **非法入参显式失败，未注册的表达式层取用直接抛** / Invalid input fails explicitly and an unregistered layer throws on use.
    /// </summary>
    /// <remarks>
    /// 两点成对：`new object` → `Failed`（第一版这里返回 `Ok`）/ 未注册就取容量表达式 → `InvalidOperationException`，
    /// **不要返回一个空多项式冒充"零占用"**。
    ///
    /// Two paired points: `new object` yields `Failed` (the first version returned `Ok` here); and taking a capacity expression before registration throws
    /// `InvalidOperationException` — **no empty polynomial pretending to be "zero occupancy"**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailInsteadOfSkipping() {
        var variables = new CapacityOrderVariables<TestLoadAction>(Actions(), Slots(), Window(), 2UL);
        CapacityLoadExpressions<TestLoadAction> unregistered = new(variables, Window());
        ExecutorCapacityConstraint<TestLoadAction> constraint = new(unregistered, Window());
        using var model = new LinearMetaModel<Flt64>();

        constraint.Invoke(new object()).IsFailed.Should().BeTrue("a non-model argument must not be reported as success");
        Action taking = () => unregistered.CapacityOf(0, 0);
        taking.Should().Throw<InvalidOperationException>();
        model.RelationConstraints.Should().BeEmpty();
    }
}
