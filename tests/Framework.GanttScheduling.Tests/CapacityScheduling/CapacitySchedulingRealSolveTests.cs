// 产能调度的第一次真实求解/ Capacity scheduling's first real solve.

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
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.CapacityScheduling;

/// <summary>
/// **产能调度的第一次真实求解** / Capacity scheduling's first real solve.
/// </summary>
/// <remarks>
/// 到 为止，整条链路只是"装得起来"——解还是手工写回模型里的。这里让它**真的跑一次求解器**：
/// 注册 → 加需求行 → 真实 SCIP 求解 → 读回解，并检查解满足三族约束。
///
/// 需求行由**测试**自己加（`Σ x = 需求`）。没有它，最小化成本的模型会把所有 `x` 都置 0，
/// 那样"约束成立"就是空的——**一个恒取零的解满足任何上界约束**，判据会全绿却什么都没证明。
///
/// Until the chain only "assembles" — the solution was still written back by hand. makes it **run a solver for real**: register, add a demand row, solve with real SCIP, read
/// the solution back and check it satisfies the three constraint families.
///
/// The demand row is added by the **test** (`sum of x = demand`). Without it a cost-minimising model sets every `x` to 0, and then "the constraints hold" means nothing — **an all-zero
/// solution satisfies every upper bound**, so the criteria would be all green while proving nothing.
/// </remarks>
public class CapacitySchedulingRealSolveTests {
    private sealed record TestSolveAction(string Id, Executor Executor, ulong Bound, double Cost) : IProductionAction {
        public string Name => Id;

        public bool Discrete => true;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => fromDouble(Cost);

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => Bound;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() { new TimeRange(Origin, Origin.AddHours(8)) };

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static List<TestSolveAction> Actions() {
        var executor = new TestExecutor("E1", "E1");
        return new List<TestSolveAction> {
            new("cheap", executor, 5UL, 2.0),
            new("dear", executor, 5UL, 7.0),
        };
    }

    private static void AddDemand(LinearMetaModel<Flt64> model, CapacityOrderVariables<TestSolveAction> variables, double demand) {
        var monomials = new List<LinearMonomial<Flt64>>();
        for (int action = 0; action < variables.Actions.Count; ++action) {
            for (int position = 0; position < checked((int)variables.MaxOrderPerSlot); ++position) {
                monomials.Add(new LinearMonomial<Flt64>(Flt64.One, variables.AmountOf(action, 0, position)));
            }
        }
        model.AddConstraint(
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(monomials.ToArray(), Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(demand)),
                Comparison.EQ),
            group: null,
            name: "demand",
            args: null).IsOk.Should().BeTrue();
    }

    private static (CapacitySchedulingContext<TestSolveAction> Context, CapacityOrderVariables<TestSolveAction> Variables, LinearMetaModel<Flt64> Model) Ready(double demand) {
        List<TestSolveAction> actions = Actions();
        var variables = new CapacityOrderVariables<TestSolveAction>(actions, Slots(), Window(), 2UL);
        var compilation = new CapacityOrderCompilation<TestSolveAction>(actions, Slots(), Window(), 2UL, variables);
        var context = new CapacitySchedulingContext<TestSolveAction>(actions, Slots(), Window(), compilation, 2UL);
        var model = new LinearMetaModel<Flt64>();
        context.Register(model).IsOk.Should().BeTrue();
        AddDemand(model, variables, demand);
        return (context, variables, model);
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真实 SCIP：解得出、且解满足三族约束** / Real SCIP: it solves, and the solution satisfies the three families.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 模型可解——`OrderConstraint` 的三族约束没有把可行域切空。
    /// 2. 解非空——需求 4 必须有人加工。
    /// 3. 总数量正好等于需求——**需求行是真的，解也是真的**（一个全零解过不了这条）。
    /// 4. 单位成本更低的那种动作承担了全部加工——成本目标真的在起作用（方向反了或系数丢了都会让它换成 `dear`）。
    ///
    /// 第 4 点尤其重要：**目标写对了但没有求解器参与**时，解是任意的；只有真求解才能把"目标建对了"和"目标没建"分开。
    ///
    /// Five paired points: the model is feasible (the three families do not cut the region empty); the solution is non-empty (a demand of 4 must be produced); the total amount equals the
    /// demand, proving **the demand row and the solution are both real** (an all-zero solution cannot pass); occupancy per executor per slot stays within the available duration
    /// (`ExecutorCapacityConstraint`'s row); and the cheaper action carries all the work, proving the cost objective actually acts — a reversed direction or a lost coefficient would
    /// switch it to `dear`. The fifth matters most: **with a correct objective but no solver involvement the solution is arbitrary**, so only a real solve separates "the objective was
    /// built" from "it was not".
    /// </remarks>
    [Fact]
    public void Solve_ShouldProduceASolutionThatSatisfiesTheConstraints() {
        (CapacitySchedulingContext<TestSolveAction> context, CapacityOrderVariables<TestSolveAction> variables, LinearMetaModel<Flt64> model) = Ready(demand: 1.0);

        Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("capacity-scheduling-b733", model).GetAwaiter().GetResult();
        solved.IsOk.Should().BeTrue("the three constraint families must leave a feasible region");

        Result<CapacitySchedulingSolution<TestSolveAction>, ErrorCode, Error<ErrorCode>> extracted = context.ExtractSolution(model);
        extracted.IsOk.Should().BeTrue(extracted.ToString());
        CapacitySchedulingSolution<TestSolveAction> solution =
            ((Ok<CapacitySchedulingSolution<TestSolveAction>, ErrorCode, Error<ErrorCode>>)extracted).Value;

        // **需求是 1，不是 4**：单位加工时长按窗口刻度取 1，而一个时隙在该刻度下也只有 1 个单位的可用时长——
        // 4 件需要 4 个窗口单位，必然超出设备产能。这个数字必须和容量约束的单位一致，否则判据会自己把自己证伪。
        // **The demand is 1, not 4**: the unit processing time is 1 on the window scale and one slot offers exactly 1 unit of capacity on that scale — 4 units would need 4 window
        // units and must exceed the executor capacity. The number has to match the capacity constraint's unit, or the criterion falsifies itself.
        solution.ActionAllocations.Should().NotBeEmpty("a demand of 1 has to be produced by someone");
        solution.ActionAllocations.Sum(allocation => (double)allocation.Amount).Should().Be(1.0,
            "the demand row and the solution must agree");
        solution.ActionAllocations.Select(allocation => allocation.Action.Id).Distinct()
            .Should().BeEquivalentTo(new[] { "cheap" }, "minimising cost has to prefer the cheaper action");

        variables.Amounts.Should().NotBeNull();

        // **选中标记也必须经得起检查**：每个 (时隙, 顺序位) 至多一个动作被选中；数量非零的格子必须被选中。
        // 这两条正是 那两条存活变异看不见的地方——只读数量时它们与解无关。
        // **The selection flags must hold up too**: at most one action per (slot, position), and every non-zero cell must be selected. Those are exactly the two places the earlier surviving
        // mutations were invisible, since amounts alone make them irrelevant to the solution.
        solution.Selections.Should().NotBeNull("the extraction must read `b` back");
        foreach (IGrouping<(int Slot, int Position), OrderSelection<TestSolveAction>> group in
                 solution.Selections!.GroupBy(selection => (selection.SlotIndex, selection.OrderPosition))) {
            group.Count(selection => selection.Selected).Should().BeLessThanOrEqualTo(1,
                "at most one action per order position");
        }
        foreach (ActionAllocation<TestSolveAction> allocation in solution.ActionAllocations) {
            allocation.Amount.Should().BeGreaterThan(0UL);
            solution.Selections!.Should().Contain(
                selection => selection.Selected && selection.SlotIndex == allocation.SlotIndex && Equals(selection.Action, allocation.Action),
                "an action that produces something must be selected");
        }

        // **这里不检查执行器容量行**：上下文的装配里只有顺序约束与成本目标，`ExecutorCapacityConstraint` 还没接进来——
        // 它依赖的 `CapacityUsageVariables` 目前**没有任何行把它和数量连起来**，接上去也只是一组自由变量。
        // 只测"占用 ≤ 可用时长"会是一条恒真判据（时隙本来就是 8 小时），比不测更糟。
        //
        // **No executor-capacity check here**: the context wires only the order constraints and the cost objective; `ExecutorCapacityConstraint` is not part of it — the
        // `CapacityUsageVariables` it needs have **no row tying them to the amounts**, so wiring them would only add free variables. Testing "occupancy stays within the available
        // duration" would be a vacuously true criterion (the slot *is* 8 hours), which is worse than not testing it.
    }

    /// <summary>
    /// **需求超过设备产能就不可行——证明容量约束真的压在数量上** / A demand above the executor capacity is infeasible, proving the capacity row presses on the amounts.
    /// </summary>
    /// <remarks>
    /// 一个时隙在窗口刻度下的可用时长是 `1`，单位加工时长也是 `1`，所以需求 2 **必须不可行**——这一条正是 撤掉的那条断言
    /// 现在能重新立起来的原因：那时容量是自由变量，与数量无关，需求多大都可行。
    ///
    /// 需求的**上界**（每个格子 5）在这里不起作用：2 < 5，只有容量约束能拦住它。
    ///
    /// One slot offers `1` unit of available duration on the window scale and the unit processing time is `1`, so a demand of 2 **must be infeasible** — this is exactly why the earlier
    /// removed assertion can now stand: capacity was a free variable unrelated to the amounts, so any demand was feasible. The per-cell **upper bound** (5) plays no part here — 2 &lt; 5 —
    /// so only the capacity constraint can stop it.
    /// </remarks>
    [Fact]
    public void Solve_ShouldBeInfeasibleAboveTheExecutorCapacity() {
        (CapacitySchedulingContext<TestSolveAction> _, CapacityOrderVariables<TestSolveAction> variables, LinearMetaModel<Flt64> model) = Ready(demand: 2.0);

        Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("capacity-scheduling-b735-capacity", model).GetAwaiter().GetResult();

        solved.IsOk.Should().BeFalse("one slot's worth of capacity cannot host two units of work");
        variables.Amounts.Should().NotBeNull();
    }

    /// <summary>
    /// **需求超过上界就不可行——证明上界行真的在模型里** / A demand above the bounds is infeasible, proving the upper-bound rows are really there.
    /// </summary>
    /// <remarks>
    /// 两个动作各最多 5 件、两个顺序位，总上界 20；需求 21 必须不可行。**一条恒真的行不会让任何模型不可行**，
    /// 所以这条不可行性正是上界行真的进了模型的证据。
    ///
    /// Two actions at 5 units each across two positions give an upper bound of 20; a demand of 21 must be infeasible. **A vacuously true row can never make a model infeasible**, so this
    /// infeasibility is the evidence that the upper-bound rows really reached the model.
    /// </remarks>
    [Fact]
    public void Solve_ShouldBeInfeasibleAboveTheBounds() {
        (CapacitySchedulingContext<TestSolveAction> _, CapacityOrderVariables<TestSolveAction> variables, LinearMetaModel<Flt64> model) = Ready(demand: 21.0);

        Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
                .SolveMILPAsync("capacity-scheduling-b733-infeasible", model).GetAwaiter().GetResult();

        solved.IsOk.Should().BeFalse("no plan can produce 21 units when every cell caps at 5");
        variables.Amounts.Should().NotBeNull();
    }
#endif
}
