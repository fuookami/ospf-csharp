// 单位加工时长用得上批次时长/ The unit processing time uses the batch duration.

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
/// **单位加工时长用得上批次时长** / The unit processing time uses the batch duration.
/// </summary>
/// <remarks>
/// 的注释说 C# 的 `IProductionAction` 没有 `BatchDuration`——**那是错的**：它从 B4xx 起就在，只是没人用。
/// 结果是 `operationTime` 的系数恒为 1，`x` 的单位变成"件"而不是"时长"，一个时隙只能装 1 件。
/// 这里把规则接上：**离散动作有 `BatchDuration` 就用它，否则取时间窗刻度**。
///
/// the earlier comment claimed C#'s `IProductionAction` has no `BatchDuration` — **wrong**: it has existed since B4xx, just unused. The consequence was an `operationTime` coefficient
/// pinned at 1, making `x` measured in pieces rather than durations, so a slot held exactly one piece. wires its rule: **a discrete action with
/// `BatchDuration` uses it, otherwise the window scale**.
/// </remarks>
public class CapacityBatchDurationTests {
    private sealed record TestBatchAction(string Id, Executor Executor, TimeSpan? Batch, bool DiscreteAction = true) : IProductionAction {
        public string Name => Id;

        public bool Discrete => DiscreteAction;

        public TimeSpan? BatchDuration => Batch;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => Flt64.Zero;

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => 9UL;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() { new TimeRange(Origin, Origin.AddHours(8)) };

    /// <summary>8 小时时间窗，刻度 8 小时 / An 8-hour window on an 8-hour scale.</summary>
    /// <returns>时间窗口 / The time window.</returns>
    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static double UnitCoefficient(TestBatchAction action) {
        var actions = new List<TestBatchAction> { action };
        var variables = new CapacityOrderVariables<TestBatchAction>(actions, Slots(), Window(), 1UL);
        using var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();
        var load = new CapacityLoadExpressions<TestBatchAction>(variables, Window());
        load.Register(model).IsOk.Should().BeTrue();

        // 容量表达式引用的是 `operationTime` 符号；要拿到系数，直接看它的多项式（每个动作一个 `operationTime`，系数 1）。
        // 单位加工时长落在 `operationTime` 的多项式里，所以从 `capacity` 走一层不够——直接读 `operationTime` 符号。
        var capacity = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)load.CapacityOf(0, 0);
        var operationTime = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)capacity.Polynomial!.Monomials[0].Symbol;
        return operationTime.Polynomial!.Monomials[0].Coefficient.ToDouble();
    }

    /// <summary>
    /// **批量时长决定系数：4 小时批次在 8 小时刻度下是 0.5** / The batch duration sets the coefficient: a 4-hour batch is 0.5 on an 8-hour scale.
    /// </summary>
    /// <remarks>
    /// 三点成对：4 小时批次 → 0.5（**不是 1，也不是 4**）/ 8 小时批次 → 1 / 没有批次时长 → 1（退化成窗口刻度）。
    /// 中间那点尤其能分辨错误实现：拿秒当系数会得到 14400。
    ///
    /// Three paired points: a 4-hour batch yields 0.5 (**neither 1 nor 4**); an 8-hour batch yields 1; and no batch duration yields 1 (degenerating to the window scale). The middle
    /// point discriminates a wrong implementation best: using seconds would give 14400.
    /// </remarks>
    [Fact]
    public void UnitProcessingTime_ShouldFollowTheBatchDuration() {
        var executor = new TestExecutor("E1", "E1");
        UnitCoefficient(new TestBatchAction("half", executor, TimeSpan.FromHours(4))).Should().Be(0.5);
        UnitCoefficient(new TestBatchAction("whole", executor, TimeSpan.FromHours(8))).Should().Be(1.0);
        UnitCoefficient(new TestBatchAction("none", executor, null)).Should().Be(1.0, "without a batch duration the window scale is used");
    }

    /// <summary>
    /// **连续动作不用批次时长** / A continuous action ignores the batch duration.
    /// </summary>
    /// <remarks>
    /// 两点成对：`Discrete = false` 且给了 4 小时批次 → 系数仍是 1（代码写明"仅离散型有效"）/
    /// `Discrete = true` 且同样的批次 → 0.5。**同一个批次时长，两种动作给出不同系数**，才算真的按规则分流。
    ///
 /// Two paired points: `Discrete = false` with a 4-hour batch still yields 1 (the implementation says "only for discrete actions"); `Discrete = true` with the same batch yields 0.5.
    /// **The same batch duration producing two different coefficients** is what proves the rule is really branched.
    /// </remarks>
    [Fact]
    public void UnitProcessingTime_ShouldIgnoreTheBatchForContinuousActions() {
        var executor = new TestExecutor("E1", "E1");
        UnitCoefficient(new TestBatchAction("continuous", executor, TimeSpan.FromHours(4), DiscreteAction: false))
            .Should().Be(1.0, "a continuous action does not batch");
        UnitCoefficient(new TestBatchAction("discrete", executor, TimeSpan.FromHours(4), DiscreteAction: true))
            .Should().Be(0.5);
    }

    /// <summary>
    /// **批次时长真的改变能排多少件** / The batch duration really changes how much fits.
    /// </summary>
    /// <remarks>
    /// 三点成对：4 小时批次、需求 2 → **可行**（占用 2 × 0.5 = 1，正好等于一个时隙的可用时长）/
    /// 同一模型把批次改成 8 小时、需求 2 → **不可行**（占用 2 × 1 = 2 &gt; 1）/ 这证明系数不只在表达式里对，在求解时也真的起作用。
    ///
    /// Three paired points: a 4-hour batch with demand 2 is **feasible** (occupancy 2 x 0.5 = 1, exactly one slot's available duration); the same model with an 8-hour batch and
    /// demand 2 is **infeasible** (occupancy 2 x 1 = 2 &gt; 1); which proves the coefficient is not only right inside the expression but really matters when solving.
    /// </remarks>
    [Fact]
    public void BatchDuration_ShouldChangeHowMuchFits() {
        SolveWith(TimeSpan.FromHours(4)).IsOk.Should().BeTrue("two 4-hour batches fill exactly one 8-hour slot");
        SolveWith(TimeSpan.FromHours(8)).IsOk.Should().BeFalse("two 8-hour batches need two slots");
        SolveWith(null).IsOk.Should().BeFalse("without a batch duration each piece takes a whole window unit");
    }

#if SCIP_INTEGRATION
    private static Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> SolveWith(TimeSpan? batch) {
        var executor = new TestExecutor("E1", "E1");
        var actions = new List<TestBatchAction> { new("A1", executor, batch) };
        var variables = new CapacityOrderVariables<TestBatchAction>(actions, Slots(), Window(), 2UL);
        var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();
        var load = new CapacityLoadExpressions<TestBatchAction>(variables, Window());
        load.Register(model).IsOk.Should().BeTrue();
        new Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits
            .ExecutorCapacityConstraint<TestBatchAction>(load, Window()).Invoke(model).IsOk.Should().BeTrue();

        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        for (int position = 0; position < 2; ++position) {
            monomials.Add(new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                Flt64.One, variables.AmountOf(0, 0, position)));
        }
        model.AddConstraint(
            new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>(
                new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(monomials.ToArray(), Flt64.Zero),
                new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                    Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), new Flt64(2.0)),
                Fuookami.Ospf.Math.Symbol.Inequality.Comparison.EQ),
            group: null,
            name: "demand",
            args: null).IsOk.Should().BeTrue();

        return new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver()
            .SolveMILPAsync("capacity-batch-duration", model).GetAwaiter().GetResult();
    }
#else
    private static Result<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> SolveWith(TimeSpan? batch) {
        var executor = new TestExecutor("E1", "E1");
        var actions = new List<TestBatchAction> { new("A1", executor, batch) };
        var variables = new CapacityOrderVariables<TestBatchAction>(actions, Slots(), Window(), 2UL);
        using var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();
        var load = new CapacityLoadExpressions<TestBatchAction>(variables, Window());
        load.Register(model).IsOk.Should().BeTrue();
        new Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits
            .ExecutorCapacityConstraint<TestBatchAction>(load, Window()).Invoke(model).IsOk.Should().BeTrue();

        // 没有求解器时只能用"清醒的容量证据"：单位系数乘以需求是否超过一个时隙的可用时长。
        // Without a solver the only honest evidence is the arithmetic: unit coefficient times demand against one slot.
        var capacity = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)load.CapacityOf(0, 0);
        var operationTime = (Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol)capacity.Polynomial!.Monomials[0].Symbol;
        double unit = operationTime.Polynomial!.Monomials[0].Coefficient.ToDouble();
        return unit * 2.0 <= 1.0
            ? Results.Ok<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>>(default!)
            : Results.Failed<Fuookami.Ospf.Core.Solver.Output.FeasibleSolverOutput<Flt64>>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "demand exceeds one slot"));
    }
#endif
}
