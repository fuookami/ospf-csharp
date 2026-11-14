// 容量调度的解真的读得回来（B731）/ Capacity scheduling's solution is really read back (B731).

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
/// **容量调度的解真的读得回来（B731）** / Capacity scheduling's solution is really read back (B731).
/// </summary>
/// <remarks>
/// 三个 `ExtractSolution` 第一版都返回 `Ok` 加一个**分配表为空**的 `CapacitySchedulingSolution`。调用方读到的是
/// 「排好了，什么都没排」——**空解和真解无法区分**，这是事后对账时最难发现的一类缺陷。
///
/// B731 之后：`CapacityOrderCompilation` 真的从模型读回 `x[动作, 时隙, 顺序位]`，按 (动作, 时隙) 汇总；
/// 另外两个编译对象**没有决策变量**，所以显式失败，而不是给一份空计划。
///
/// All three `ExtractSolution` bodies used to return `Ok` with an **empty allocation table**. The caller read "solved, nothing scheduled" — **an empty solution and a real one cannot be
/// told apart**, the hardest defect to find afterwards. After B731, `CapacityOrderCompilation` really reads `x[action, slot, order position]` back and sums per (action, slot), while the
/// other two compilations **have no decision variables** and therefore fail explicitly instead of handing back an empty plan.
/// </remarks>
public class CapacityOrderSolutionExtractionTests {
    private sealed record TestExtractAction(string Id, Executor Executor, ulong Bound) : IProductionAction {
        public string Name => Id;

        public bool Discrete => true;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => Flt64.Zero;

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => Bound;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() {
        new TimeRange(Origin, Origin.AddHours(8)),
    };

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static List<TestExtractAction> Actions() {
        var executor = new TestExecutor("E1", "E1");
        return new List<TestExtractAction> { new("A1", executor, 5UL), new("A2", executor, 5UL) };
    }

    /// <summary>把所有格子的取值清零 / Zero every cell.</summary>
    /// <param name="model">模型 / The model.</param>
    /// <param name="variables">排序变量层 / The order variable layer.</param>
    private static void ZeroAll(LinearMetaModel<Flt64> model, CapacityOrderVariables<TestExtractAction> variables) {
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

    private static void SetSelection(LinearMetaModel<Flt64> model, CapacityOrderVariables<TestExtractAction> variables, int action, int slot, int position, double value) {
        Fuookami.Ospf.Core.Token.Token<Flt64>? token = model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)variables.SelectionOf(action, slot, position));
        token.Should().NotBeNull("the selection must be in the model");
        token!.SetResult(new Flt64(value));
    }

    private static (CapacityOrderVariables<TestExtractAction> Variables, LinearMetaModel<Flt64> Model, CapacityOrderCompilation<TestExtractAction> Compilation) Ready() {
        var variables = new CapacityOrderVariables<TestExtractAction>(Actions(), Slots(), Window(), 2UL);
        var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();
        var compilation = new CapacityOrderCompilation<TestExtractAction>(Actions(), Slots(), Window(), 2UL, variables);
        return (variables, model, compilation);
    }

    private static void Set(LinearMetaModel<Flt64> model, CapacityOrderVariables<TestExtractAction> variables, int action, int slot, int position, double value) {
        Fuookami.Ospf.Core.Token.Token<Flt64>? token = model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)variables.AmountOf(action, slot, position));
        token.Should().NotBeNull("the variable must be in the model");
        token!.SetResult(new Flt64(value));
    }

    /// <summary>
    /// **读回来的数量与顺序位都对得上** / The amounts and order positions read back are right.
    /// </summary>
    /// <remarks>
    /// 四点成对：一个动作在两个顺序位各 3 与 2，读回的数量是 5（**顺序位要汇总，不能只取第一个**）/
    /// 顺序位记的是最后一个非零位置（1）/ 数量为零的 (动作, 时隙) 不产生分配 / 时长取的是时隙长度。
    ///
    /// Four paired points: an action holding 3 and 2 in two positions reads back as 5 (**the positions must be summed, not just the first one**); the order position records the last
    /// non-zero one (1); an (action, slot) with zero amount produces no allocation; and the duration is the slot length.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldSumTheOrderPositions() {
        (CapacityOrderVariables<TestExtractAction> variables, LinearMetaModel<Flt64> model, CapacityOrderCompilation<TestExtractAction> compilation) = Ready();
        ZeroAll(model, variables);
        Set(model, variables, 0, 0, 0, 3.0);
        Set(model, variables, 0, 0, 1, 2.0);
        // **选中标记也要写回**：B734 起解读同时读 `b`，只写数量会让提取显式失败——这正是想要的行为。
        // **The selection flags are written back too**: since B734 the extraction reads `b` as well, and writing amounts alone makes it fail explicitly — which is the intent.
        SetSelection(model, variables, 0, 0, 0, 1.0);
        SetSelection(model, variables, 0, 0, 1, 1.0);

        Result<CapacitySchedulingSolution<TestExtractAction>, ErrorCode, Error<ErrorCode>> result = compilation.ExtractSolution(model);

        result.IsOk.Should().BeTrue(result.ToString());
        CapacitySchedulingSolution<TestExtractAction> solution =
            ((Ok<CapacitySchedulingSolution<TestExtractAction>, ErrorCode, Error<ErrorCode>>)result).Value;
        solution.ActionAllocations.Should().ContainSingle("only A1 has a non-zero amount");
        ActionAllocation<TestExtractAction> allocation = solution.ActionAllocations[0];
        allocation.Action.Id.Should().Be("A1");
        allocation.Amount.Should().Be(5UL, "two order positions of 3 and 2 must add up");
        allocation.Order.Should().Be(1, "the last non-zero position");
        allocation.Duration.Should().Be(Slots()[0].Duration);

        solution.ExecutorCapacities.Should().ContainSingle("the one executor is busy in the one slot");
        solution.ExecutorCapacities[0].Executor.Id.Should().Be("E1");
    }

    /// <summary>
    /// **读不到值就失败，不当成 0** / A missing value fails instead of counting as zero.
    /// </summary>
    /// <remarks>
    /// 三点成对：变量没注册 → `Failed`（第一版这里返回 `Ok` 加空表）/ 变量在模型里但没取值 → `Failed` /
    /// 参入非模型对象 → `Failed`。**把"读不到"当成 0 会安静地报一份空计划**，所以每一条都必须报出来。
    ///
    /// Three paired points: unregistered variables fail (the first version returned `Ok` with an empty table); a variable in the model with no value fails; and a non-model argument fails.
    /// **Treating "cannot read" as zero would quietly report an empty plan**, so every one of them must be reported.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldFailInsteadOfGuessing() {
        var bare = new CapacityOrderCompilation<TestExtractAction>(Actions(), Slots(), Window(), 2UL);
        using var model = new LinearMetaModel<Flt64>();
        bare.ExtractSolution(model).IsFailed.Should().BeTrue("without registered variables there is nothing to read");

        (CapacityOrderVariables<TestExtractAction> variables, LinearMetaModel<Flt64> registered, CapacityOrderCompilation<TestExtractAction> compilation) = Ready();
        compilation.ExtractSolution(registered).IsFailed.Should().BeTrue("no value was written back yet");
        compilation.ExtractSolution(new object()).IsFailed.Should().BeTrue("a non-model argument must not be reported as success");
        variables.Amounts.Should().NotBeNull();
    }

    /// <summary>
    /// **没有决策变量的两个编译对象显式失败** / The two compilations without decision variables fail explicitly.
    /// </summary>
    /// <remarks>
    /// 两点成对：`CapacityCompilation` 与 `IterativeCapacityCompilation` 的 `ExtractSolution` 都是 `Failed` /
    /// 错误码是 `ApplicationError`（缺的是能力，不是参数）。它们第一版都返回 `Ok` 加空表。
    ///
    /// Two paired points: `CapacityCompilation` and `IterativeCapacityCompilation` both return `Failed`; the code is `ApplicationError`, since a capability is missing rather than an
    /// argument. Both returned `Ok` with an empty table in the first version.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldFailForCompilationsWithoutVariables() {
        using var model = new LinearMetaModel<Flt64>();
        var plain = new CapacityCompilation<TestExtractAction>(Actions(), Slots(), Window());
        plain.ExtractSolution(model).IsFailed.Should().BeTrue();

        Result<CapacitySchedulingSolution<TestExtractAction>, ErrorCode, Error<ErrorCode>> iterative =
            new IterativeCapacityCompilation<TestExecutor, TestExtractAction>(
                new List<TestExecutor> { new("E1", "E1") }, Actions(), Slots(), Window())
                .ExtractSolution(model);
        iterative.IsFailed.Should().BeTrue();
        ((Failed<CapacitySchedulingSolution<TestExtractAction>, ErrorCode, Error<ErrorCode>>)iterative)
            .Error.Code.Should().Be(ErrorCode.ApplicationError);
    }
}
