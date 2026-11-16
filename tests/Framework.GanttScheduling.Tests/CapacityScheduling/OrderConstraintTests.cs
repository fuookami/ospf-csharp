// 顺序约束真的建行/ The order constraint really builds rows.

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
/// **顺序约束真的建行** / The order constraint really builds rows.
/// </summary>
/// <remarks>
/// 第一版 `Invoke` 是 `=&gt; Ok`：三族约束一条都没建。现在按基线建：
///
/// 1. 每顺序位至多一个动作：`Σ_a b[a, t, o] ≤ 1`；
/// 2. 下界关联：`x[a, t, o] ≥ b[a, t, o]`；
/// 3. 上界关联：`x[a, t, o] ≤ M · b[a, t, o]`，`M` 取动作自己的上界。
///
/// 判据钉的是**三条都真的存在**，而不只是"行数变多了"——2 与 3 各缺一条都会让 `b` 与 `x` 脱钩：
/// 只留 2，`b` 为 1 而 `x` 可以无限大；只留 3，`x` 为 0 而 `b` 为 1，白占一个顺序位。
///
/// The first `Invoke` was `=&gt; Ok`: none of the three families existed. It now builds the baseline's three (see the list above). The criteria pin that **all three really exist**, not merely
/// that the row count grew — dropping either 2 or 3 decouples `b` from `x`: with only 2, `b` is 1 while `x` may be unbounded; with only 3, `x` is 0 while `b` is 1, wasting a position.
/// </remarks>
public class OrderConstraintTests {
    private sealed record TestOrderAction(string Id, Executor Executor, ulong Bound) : IProductionAction {
        public string Name => Id;

        public bool Discrete => true;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => Flt64.Zero;

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => Bound;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() { new TimeRange(Origin, Origin.AddHours(8)) };

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static List<TestOrderAction> Actions() {
        var executor = new TestExecutor("E1", "E1");
        return new List<TestOrderAction> { new("A1", executor, 5UL), new("A2", executor, 7UL) };
    }

    private static (CapacityOrderVariables<TestOrderAction>, LinearMetaModel<Flt64>) Registered() {
        var variables = new CapacityOrderVariables<TestOrderAction>(Actions(), Slots(), Window(), 2UL);
        var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();
        return (variables, model);
    }

    /// <summary>
    /// **三族行都在，且行名能定位到具体那一对** / All three families exist and their names locate the exact pair.
    /// </summary>
    /// <remarks>
    /// 四点成对：每顺序位一条唯一性行（`order_unique_{t}_{o}`）/ 每个 (动作, 时隙, 顺序位) 一条下界行 /
    /// 同样的一对上界行也在 / 总行数等于 `位置数 × (1 + 2 × 动作数)`（2 × (1 + 4) = 10）——
    /// 少了任何一族，这个数就对不上。
    ///
    /// Four paired points: one uniqueness row per position (`order_unique_{t}_{o}`); one lower-link row per (action, slot, position); the upper-link row on the same pairs; and the total
    /// equals `positions x (1 + 2 x actions)` (2 x (1 + 4) = 10) — dropping any family breaks that count.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldBuildAllThreeFamilies() {
        (CapacityOrderVariables<TestOrderAction> variables, LinearMetaModel<Flt64> model) = Registered();
        var constraint = new OrderConstraint<TestOrderAction>(variables);

        constraint.Invoke(model).IsOk.Should().BeTrue();

        constraint.Name.Should().Be("order");
        constraint.Lazy.Should().BeFalse();
        var names = model.RelationConstraints.Select(row => row.Name).ToList();
        names.Should().Contain("order_unique_0_0");
        names.Should().Contain("order_unique_0_1");
        names.Should().Contain("order_link_lb_0_0_0");
        names.Should().Contain("order_link_ub_1_0_1");
        names.Should().HaveCount(2 * (1 + 2 * variables.Actions.Count));
    }

    /// <summary>
    /// **上界关联的系数就是动作上界，不是拍脑袋的大 M** / The upper-link coefficient is the action's bound, not a guessed big-M.
    /// </summary>
    /// <remarks>
    /// 三点成对：动作为 A1（上界 5）时 `x ≤ 5 · b`，右端单项式系数是 5 / 动作换成 A2（上界 7）时系数是 7 /
    /// 唯一性行的右端常数是 1、比较符是 `≤`——不是 `=`，也不是没有上界。
    ///
    /// Three paired points: with action A1 (bound 5) the row reads `x &lt;= 5. b` and the right monomial coefficient is 5; with A2 (bound 7) it is 7; and the uniqueness row has constant
    /// 1 on the right with comparison `&lt;=`, neither `=` nor unbounded.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldUseTheActionBoundAsBigM() {
        (CapacityOrderVariables<TestOrderAction> variables, LinearMetaModel<Flt64> model) = Registered();
        new OrderConstraint<TestOrderAction>(variables).Invoke(model).IsOk.Should().BeTrue();

        LinearInequalityConstraint<Flt64> first = model.RelationConstraints.First(row => row.Name == "order_link_ub_0_0_0");
        first.Inequality.Rhs.Monomials.Should().ContainSingle();
        first.Inequality.Rhs.Monomials[0].Coefficient.ToDouble().Should().Be(5.0, "A1's bound is 5");

        LinearInequalityConstraint<Flt64> second = model.RelationConstraints.First(row => row.Name == "order_link_ub_1_0_0");
        second.Inequality.Rhs.Monomials[0].Coefficient.ToDouble().Should().Be(7.0, "A2's bound is 7");

        LinearInequalityConstraint<Flt64> unique = model.RelationConstraints.First(row => row.Name == "order_unique_0_0");
        unique.Sign.Should().Be(Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE);
        unique.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0, "at most one action per position, not at most N");
        unique.Inequality.Lhs.Monomials.Should().HaveCount(variables.Actions.Count,
            "the uniqueness row sums over every action");

        // **两条关联式的方向也要钉住**：`x ≥ b` 与 `x ≤ M · b` 方向反了，行数、行名、系数全都还是对的。
        // **The direction of both linking rows must be pinned too**: reversing `x >= b` or `x <= M. b` leaves the count, the names and the coefficients all looking right.
        model.RelationConstraints.First(row => row.Name == "order_link_lb_0_0_0").Sign
            .Should().Be(Fuookami.Ospf.Math.Symbol.Inequality.Comparison.GE, "x >= b, not x <= b");
        model.RelationConstraints.First(row => row.Name == "order_link_ub_0_0_0").Sign
            .Should().Be(Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE, "x <= M . b, not x >= M . b");
    }

    /// <summary>
    /// **未注册与非法入参显式失败** / Unregistered variables and invalid inputs fail explicitly.
    /// </summary>
    /// <remarks>
    /// 三点成对：未注册 → `Failed` 且一行都没建（第一版这里返回 `Ok`）/ 传入 `new object` → `Failed` /
    /// 注册之后同一个调用 → `Ok` 且行数非空。
    ///
    /// Three paired points: unregistered yields `Failed` with no rows written (the first version returned `Ok` here); `new object` yields `Failed`; and after registration the same call
    /// yields `Ok` with rows present.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailInsteadOfSkipping() {
        var unregistered = new CapacityOrderVariables<TestOrderAction>(Actions(), Slots(), Window(), 2UL);
        using var model = new LinearMetaModel<Flt64>();
        new OrderConstraint<TestOrderAction>(unregistered).Invoke(model).IsFailed.Should().BeTrue();
        model.RelationConstraints.Should().BeEmpty();

        (CapacityOrderVariables<TestOrderAction> variables, LinearMetaModel<Flt64> registered) = Registered();
        var constraint = new OrderConstraint<TestOrderAction>(variables);
        constraint.Invoke(new object()).IsFailed.Should().BeTrue("a non-model argument must not be reported as success");
        constraint.Invoke(registered).IsOk.Should().BeTrue();
        registered.RelationConstraints.Should().NotBeEmpty();
    }
}
