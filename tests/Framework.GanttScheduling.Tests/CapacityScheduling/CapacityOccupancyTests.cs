// 占用是算出来的，不是"每个分配占满一个时隙"/ Occupancy is computed, not "each allocation fills a slot".

using System;
using System.Collections.Generic;
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
/// **占用是算出来的，不是"每个分配占满一个时隙"** / Occupancy is computed, not "each allocation fills a slot".
/// </summary>
/// <remarks>
/// 起 `ExecutorCapacityResult.TotalDuration` 是把每个分配的 `Duration`（= 时隙长度）加起来。同一时隙里两个动作就会报
/// "两个时隙的占用"——**数字看着像产能，其实只是动作个数乘以时隙长度**。基线不是这么算的：
/// `capacity[e, s]` 求值后经 `timeWindow.durationOf(...)` 换回时长。
///
/// 现在从数量直接算：`占用 = Σ 单位加工时长 × 数量`，再换回时长。
///
/// Since，`ExecutorCapacityResult.TotalDuration` summed every allocation's `Duration` (the slot length), so two actions in one slot reported "two slots of occupancy" —
/// **a number that looks like capacity but is really the action count times the slot length**. The baseline evaluates `capacity[e, s]` and converts back through
/// `timeWindow.durationOf(...)`. It is now computed from the amounts: `occupancy = sum of unit processing time x amount`, converted back to a duration.
/// </remarks>
public class CapacityOccupancyTests {
    private sealed record TestOccupancyAction(string Id, Executor Executor, TimeSpan? Batch) : IProductionAction {
        public string Name => Id;

        public bool Discrete => true;

        public TimeSpan? BatchDuration => Batch;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => Flt64.Zero;

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => 9UL;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() { new TimeRange(Origin, Origin.AddHours(8)) };

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static (CapacityOrderCompilation<TestOccupancyAction> Compilation, CapacityOrderVariables<TestOccupancyAction> Variables, LinearMetaModel<Flt64> Model) Ready(TimeSpan? batch) {
        var executor = new TestExecutor("E1", "E1");
        var actions = new List<TestOccupancyAction> { new("A1", executor, batch) };
        var variables = new CapacityOrderVariables<TestOccupancyAction>(actions, Slots(), Window(), 2UL);
        var compilation = new CapacityOrderCompilation<TestOccupancyAction>(actions, Slots(), Window(), 2UL, variables);
        var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();
        for (int position = 0; position < 2; ++position) {
            model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)variables.AmountOf(0, 0, position))!
                .SetResult(new Flt64(position == 0 ? 2.0 : 0.0));
            model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)variables.SelectionOf(0, 0, position))!
                .SetResult(new Flt64(position == 0 ? 1.0 : 0.0));
        }
        return (compilation, variables, model);
    }

    /// <summary>
    /// **占用 = 单位加工时长 × 数量，而不是时隙长度** / Occupancy is unit time x amount, not the slot length.
    /// </summary>
    /// <remarks>
    /// 三点成对：4 小时批次、数量 2 → 占用 `2 × 0.5 = 1` 个窗口单位 = **8 小时**（不是 16 小时、也不是"每个分配 8 小时"）/
    /// 无批次、数量 2 → 占用 `2 × 1 = 2` 个窗口单位 = **16 小时** / 两个结果不同，说明它真的按单位和数量算。
    ///
    /// 第一点的 8 小时恰好等于时隙长度，容易和旧写法混起来——所以第二点必须留着：**没有批次时长时两个数量就是一整个时隙加一个时隙**。
    ///
    /// Three paired points: a 4-hour batch with amount 2 gives `2 x 0.5 = 1` window unit = **8 hours** (neither 16 nor "8 hours per allocation"); no batch with amount 2 gives
    /// `2 x 1 = 2` window units = **16 hours**; and the two differ, proving it really uses the unit and the amount. The first point's 8 hours coincides with the slot length and could
    /// be confused with the old form, which is why the second must stay: **without a batch duration two pieces are one full slot plus another**.
    /// </remarks>
    [Fact]
    public void TotalDuration_ShouldFollowTheUnitAndTheAmount() {
        (CapacityOrderCompilation<TestOccupancyAction> batched, CapacityOrderVariables<TestOccupancyAction> _, LinearMetaModel<Flt64> batchedModel) = Ready(TimeSpan.FromHours(4));
        using LinearMetaModel<Flt64> first = batchedModel;
        CapacitySchedulingSolution<TestOccupancyAction> batchedSolution = Extract(batched, batchedModel);
        batchedSolution.ExecutorCapacities.Should().ContainSingle();
        batchedSolution.ExecutorCapacities[0].TotalDuration.Should().Be(TimeSpan.FromHours(8));

        (CapacityOrderCompilation<TestOccupancyAction> unbatched, CapacityOrderVariables<TestOccupancyAction> _, LinearMetaModel<Flt64> unbatchedModel) = Ready(null);
        using LinearMetaModel<Flt64> second = unbatchedModel;
        CapacitySchedulingSolution<TestOccupancyAction> unbatchedSolution = Extract(unbatched, unbatchedModel);
        unbatchedSolution.ExecutorCapacities.Should().ContainSingle();
        unbatchedSolution.ExecutorCapacities[0].TotalDuration.Should().Be(TimeSpan.FromHours(16),
            "two pieces without a batch duration take two window units");
    }

    /// <summary>
    /// **数量为 0 就没有占用行** / Zero amounts leave no occupancy row.
    /// </summary>
    /// <remarks>
    /// 两点成对：数量为 0 → `ExecutorCapacities` 为空 / 数量非 0 → 有一行。旧写法里"每个分配都算"会让前一点也产生行（一个 0 数量的分配）。
    ///
    /// Two paired points: zero amounts leave `ExecutorCapacities` empty; non-zero amounts leave one row. The old form produced a row for every allocation, including zero-amount ones.
    /// </remarks>
    [Fact]
    public void ZeroAmount_ShouldLeaveNoOccupancy() {
        var executor = new TestExecutor("E1", "E1");
        var actions = new List<TestOccupancyAction> { new("A1", executor, null) };
        var variables = new CapacityOrderVariables<TestOccupancyAction>(actions, Slots(), Window(), 2UL);
        var compilation = new CapacityOrderCompilation<TestOccupancyAction>(actions, Slots(), Window(), 2UL, variables);
        using var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();
        for (int position = 0; position < 2; ++position) {
            model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)variables.AmountOf(0, 0, position))!
                .SetResult(Flt64.Zero);
            model.Tokens.Find((Fuookami.Ospf.Core.Variable.IVariableItem)variables.SelectionOf(0, 0, position))!
                .SetResult(Flt64.Zero);
        }

        CapacitySchedulingSolution<TestOccupancyAction> solution = Extract(compilation, model);

        solution.ActionAllocations.Should().BeEmpty();
        solution.ExecutorCapacities.Should().BeEmpty("nothing productive happened");
    }

    private static CapacitySchedulingSolution<TestOccupancyAction> Extract(
        CapacityOrderCompilation<TestOccupancyAction> compilation,
        LinearMetaModel<Flt64> model) {
        Result<CapacitySchedulingSolution<TestOccupancyAction>, ErrorCode, Error<ErrorCode>> result = compilation.ExtractSolution(model);
        result.IsOk.Should().BeTrue(result.ToString());
        return ((Ok<CapacitySchedulingSolution<TestOccupancyAction>, ErrorCode, Error<ErrorCode>>)result).Value;
    }
}
