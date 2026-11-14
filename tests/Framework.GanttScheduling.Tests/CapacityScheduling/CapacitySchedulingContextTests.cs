// 产能调度上下文真的装得起来（B732）/ The capacity scheduling context really assembles (B732).

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
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.CapacityScheduling;

/// <summary>
/// **产能调度上下文真的装得起来（B732）** / The capacity scheduling context really assembles (B732).
/// </summary>
/// <remarks>
/// `ICapacitySchedulingContext` 从 B4xx 起就在，但**没有任何实现**——B724 的决策变量、B726 的排序变量、
/// B727 的三族约束、B728 的成本目标全是"零件齐了、没装起来"。**一个没有实现的接口比没有接口更糟**：
/// 它让人以为整条链路已经能跑。
///
/// 这一批给出唯一实现 `CapacitySchedulingContext`，并用一条端到端判据证明零件真的能装在一起：
/// 注册 → 模型里同时有变量、三族约束和一个目标 → 写回取值 → 解读得回来。
///
/// `ICapacitySchedulingContext` has existed since B4xx with **no implementation at all** — B724's decision variables, B726's order variables, B727's three constraint families and B728's cost
/// objective were all "parts present, never assembled". **An interface with no implementation is worse than no interface**: it makes readers believe the chain already runs. This batch adds
/// the single implementation and one end-to-end criterion proving the parts really go together: register, see variables plus three constraint families plus one objective in the model, write
/// values back, and read the solution out.
/// </remarks>
public class CapacitySchedulingContextTests {
    private sealed record TestContextAction(string Id, Executor Executor, ulong Bound, double Cost) : IProductionAction {
        public string Name => Id;

        public bool Discrete => true;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => fromDouble(Cost);

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => Bound;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() { new TimeRange(Origin, Origin.AddHours(8)) };

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static List<TestContextAction> Actions() {
        var executor = new TestExecutor("E1", "E1");
        return new List<TestContextAction> { new("A1", executor, 5UL, 2.0), new("A2", executor, 5UL, 3.0) };
    }

    private static CapacitySchedulingContext<TestContextAction> Context() {
        List<TestContextAction> actions = Actions();
        var variables = new CapacityOrderVariables<TestContextAction>(actions, Slots(), Window(), 2UL);
        var compilation = new CapacityOrderCompilation<TestContextAction>(actions, Slots(), Window(), 2UL, variables);
        return new CapacitySchedulingContext<TestContextAction>(actions, Slots(), Window(), compilation, 2UL);
    }

    /// <summary>把所有格子的取值清零 / Zero every cell.</summary>
    /// <param name="model">模型 / The model.</param>
    /// <param name="variables">排序变量层 / The order variable layer.</param>
    private static void ZeroAll(LinearMetaModel<Flt64> model, CapacityOrderVariables<TestContextAction> variables) {
        for (int action = 0; action < variables.Actions.Count; ++action) {
            for (int slot = 0; slot < variables.Slots.Count; ++slot) {
                for (int position = 0; position < checked((int)variables.MaxOrderPerSlot); ++position) {
                    model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)variables.AmountOf(action, slot, position))!
                        .SetResult(new Flt64(0.0));
                    model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)variables.SelectionOf(action, slot, position))!
                        .SetResult(new Flt64(0.0));
                }
            }
        }
    }

    /// <summary>
    /// **注册之后三样东西都在同一个模型里** / After registration all three live in the same model.
    /// </summary>
    /// <remarks>
    /// 四点成对：排序变量建起来了（`Amounts` 非空）/ 顺序约束的三族行都在（唯一性 + 上下界）/
    /// 成本目标挂上去了（方向是最小化）/ 变量层与编译对象指向的是**同一个**实例（各建一份就会读不到解）。
    ///
    /// Four paired points: the order variables are built (`Amounts` is non-null); all three constraint families are present (uniqueness plus lower and upper links); the cost objective is
    /// attached and minimises; and the variable layer and the compilation are the **same** instance (a second copy would make the solution unreadable).
    /// </remarks>
    [Fact]
    public void Register_ShouldWireVariablesConstraintsAndObjective() {
        CapacitySchedulingContext<TestContextAction> context = Context();
        using var model = new LinearMetaModel<Flt64>();

        context.Register(model).IsOk.Should().BeTrue();

        context.Variables.Amounts.Should().NotBeNull("registration must build the amount cube");
        var names = model.RelationConstraints.Select(row => row.Name).ToList();
        names.Should().Contain("order_unique_0_0");
        names.Should().Contain("order_link_lb_0_0_0");
        names.Should().Contain("order_link_ub_0_0_0");
        model.MetaSubObjects.Should().ContainSingle("the cost objective is attached once");
        model.MetaSubObjects[0].Category.Should().Be(Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
    }

    /// <summary>
    /// **端到端：写回取值之后解读得回来** / End to end: after writing values back the solution reads out.
    /// </summary>
    /// <remarks>
    /// 三点成对：注入的变量层与上下文用的是**同一个**实例（`AmountOf` 能取到注册后的符号）/
    /// 写回取值后 `ExtractSolution` 给出对应数量的分配 / 数量为 0 的动作不出现。
    ///
    /// Three paired points: the injected variable layer is the **same** instance the context uses (`AmountOf` reaches the registered symbols); after writing values back
    /// `ExtractSolution` returns an allocation with the matching amount; and an action with amount 0 does not appear.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldReadWhatWasWritten() {
        List<TestContextAction> actions = Actions();
        var variables = new CapacityOrderVariables<TestContextAction>(actions, Slots(), Window(), 2UL);
        var compilation = new CapacityOrderCompilation<TestContextAction>(actions, Slots(), Window(), 2UL, variables);
        var context = new CapacitySchedulingContext<TestContextAction>(actions, Slots(), Window(), compilation, 2UL);
        using var model = new LinearMetaModel<Flt64>();
        context.Register(model).IsOk.Should().BeTrue();

        ZeroAll(model, variables);
        model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)variables.AmountOf(0, 0, 0))!.SetResult(new Flt64(4.0));
        model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)variables.SelectionOf(0, 0, 0))!.SetResult(new Flt64(1.0));

        Result<CapacitySchedulingSolution<TestContextAction>, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsOk.Should().BeTrue(result.ToString());
        CapacitySchedulingSolution<TestContextAction> solution =
            ((Ok<CapacitySchedulingSolution<TestContextAction>, ErrorCode, Error<ErrorCode>>)result).Value;
        solution.ActionAllocations.Should().ContainSingle();
        solution.ActionAllocations[0].Action.Id.Should().Be("A1");
        solution.ActionAllocations[0].Amount.Should().Be(4UL);
    }

    /// <summary>
    /// **关掉成本目标就不挂目标** / Turning the cost objective off attaches nothing.
    /// </summary>
    /// <remarks>
    /// 两点成对：`withCostObjective: false` 时模型里没有目标 / 约束照样都在——**"不要目标"不等于"什么都不要"**。
    ///
    /// Two paired points: with `withCostObjective: false` the model holds no objective; the constraints are still there — **"no objective" is not "nothing at all"**.
    /// </remarks>
    [Fact]
    public void Register_ShouldRespectTheObjectiveSwitch() {
        List<TestContextAction> actions = Actions();
        var variables = new CapacityOrderVariables<TestContextAction>(actions, Slots(), Window(), 2UL);
        var compilation = new CapacityOrderCompilation<TestContextAction>(actions, Slots(), Window(), 2UL, variables);
        var context = new CapacitySchedulingContext<TestContextAction>(actions, Slots(), Window(), compilation, 2UL, withCostObjective: false);
        using var model = new LinearMetaModel<Flt64>();

        context.Register(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Should().BeEmpty("the objective was switched off");
        model.RelationConstraints.Should().NotBeEmpty("the constraints are not affected by that switch");
    }
}
