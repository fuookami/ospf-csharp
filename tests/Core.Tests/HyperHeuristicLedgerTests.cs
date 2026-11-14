// 超启发式计数器与终止判定测试 / Tests for the run ledger and its termination ladder.

using System;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 超启发式运行的终止判定 / The hyper-heuristic run's termination decision.
/// </summary>
public sealed class HyperHeuristicLedgerTests {
    /// <summary>**诊断非空时一律报 `Failed`，哪怕预算也用完了** / Diagnostics always win, even when the budget is exhausted.</summary>
    /// <remarks>
    /// 有错误时若报"预算用尽"，调用方会以为搜索正常结束——**而实际上它中途出过错**。
    /// With an error, reporting "budget exhausted" makes the caller believe the search finished normally — **while it actually failed partway**.
    /// </remarks>
    [Fact]
    public void Termination_ShouldPreferFailedOverEverything() {
        var ledger = new HyperHeuristicLedger {
            Stopped = true, Accounted = 1000L, Steps = 1000L, Dispatched = 1000L,
        };
        ledger.Diagnostics.Add("boom");

        ledger.Termination(new BudgetConfig(Evaluations: 10L, Steps: 10L, Decisions: 10L), 0L, true)
            .Should().Be(SearchTermination.Failed);
    }

    /// <summary>**外部停止优先于预算耗尽** / An external stop outranks an exhausted budget.</summary>
    [Fact]
    public void Termination_ShouldPreferStoppedOverLimits() {
        var ledger = new HyperHeuristicLedger { Stopped = true, Accounted = 1000L };

        ledger.Termination(new BudgetConfig(Evaluations: 10L), 0L, false)
            .Should().Be(SearchTermination.Stopped);
    }

    /// <summary>**评估额度用尽报 `EvaluationLimit`，与步数、决策区分开** / An exhausted evaluation allowance reports `EvaluationLimit`, distinct from steps and decisions.</summary>
    [Fact]
    public void Termination_ShouldReportEvaluationLimit() {
        var ledger = new HyperHeuristicLedger { Accounted = 10L };

        ledger.Termination(new BudgetConfig(Evaluations: 10L), 0L, false)
            .Should().Be(SearchTermination.EvaluationLimit);
    }

    /// <summary>**步数与决策共用 `StepLimit`** / Steps and decisions share `StepLimit`.</summary>
    [Fact]
    public void Termination_ShouldReportStepLimitForStepsAndDecisions() {
        var bySteps = new HyperHeuristicLedger { Steps = 5L };
        bySteps.Termination(new BudgetConfig(Steps: 5L), 0L, false)
            .Should().Be(SearchTermination.StepLimit);

        var byDecisions = new HyperHeuristicLedger { Dispatched = 3L };
        byDecisions.Termination(new BudgetConfig(Decisions: 3L), 0L, false)
            .Should().Be(SearchTermination.StepLimit);
    }

    /// <summary>**超时报 `TimeLimit`，且只在毫秒额度真的用完时** / A timeout reports `TimeLimit`, and only once the millisecond allowance is spent.</summary>
    [Fact]
    public void Termination_ShouldReportTimeLimit() {
        var budget = new BudgetConfig(TimeMillis: 10L);
        var ledger = new HyperHeuristicLedger();

        ledger.Termination(budget, 9_999_999L, false).Should().BeNull();
        ledger.Termination(budget, 10_000_000L, false).Should().Be(SearchTermination.TimeLimit);
    }

    /// <summary>**全部工作单元禁用时报 `NoCandidate`；否则返回 `null` 表示切片正常结束** / All workers disabled reports `NoCandidate`; otherwise `null` means the slice simply finished.</summary>
    [Fact]
    public void Termination_ShouldReportNoCandidateAndOtherwiseNull() {
        var ledger = new HyperHeuristicLedger();
        var budget = new BudgetConfig(Evaluations: 100L);

        ledger.Termination(budget, 0L, true).Should().Be(SearchTermination.NoCandidate);
        ledger.Termination(budget, 0L, false).Should().BeNull();
    }

    /// <summary>**分母为 0 时特征不许出现 `NaN`** / No `NaN` may appear in the features when a denominator is zero.</summary>
    /// <remarks>
    /// 第一次决策时工作集为空、成功次数为 0。直接除会得到 `NaN`，而 `NaN` 会顺着特征进到选择策略里，
    /// **让所有动作的比较都变成"不更好"**——看起来就像"算法不收敛"。
    ///
    /// On the first decision the working set is empty and the success count is zero. Dividing directly yields `NaN`, which travels through the features
    /// into the selection policy, **making every action comparison say "not better"** — which looks exactly like "the algorithm does not converge".
    /// </remarks>
    [Fact]
    public void Progress_ShouldNeverProduceNaN() {
        var ledger = new HyperHeuristicLedger();

        SearchProgress progress = ledger.Progress(evaluationLimit: 100L,
            workingCount: 0, distinctWorking: 0, sliceEvaluations: 0L);

        double.IsNaN(progress.Diversity).Should().BeFalse();
        double.IsNaN(progress.SuccessRate.ToDouble()).Should().BeFalse();
        double.IsNaN(progress.Cost.ToDouble()).Should().BeFalse();
        progress.Diversity.Should().Be(0.0);
    }

    /// <summary>**除数正常时特征按公式给出** / With sane denominators the features follow the formula.</summary>
    [Fact]
    public void Progress_ShouldComputeDiversityAndSuccessRate() {
        var ledger = new HyperHeuristicLedger { Completed = 4L, Successes = 1L, LastCost = 50L };

        SearchProgress progress = ledger.Progress(
            evaluationLimit: 1000L, workingCount: 4, distinctWorking: 3, sliceEvaluations: 100L);

        progress.Diversity.Should().BeApproximately(0.75, 1e-12);
        progress.SuccessRate.ToDouble().Should().BeApproximately(0.25, 1e-12);
        progress.Cost.ToDouble().Should().BeApproximately(0.5, 1e-12);
    }
}
