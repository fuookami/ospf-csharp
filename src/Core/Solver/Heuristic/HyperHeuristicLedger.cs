// 超启发式运行的计数器与终止判定 / The hyper-heuristic run's ledger and termination decision.
// 对应固定 Kotlin 基线 `HyperHeuristicSolver.kt`（47241 B）里 `HyperHeuristicRun` 的 `termination` 与 `state`：
// 两个纯函数，输入是计数器，输出是终止原因与特征输入。
// Mirrors `termination` and `state` of `HyperHeuristicRun` in the pinned baseline's `HyperHeuristicSolver.kt` (47241 B): two pure functions
// turning the counters into a termination reason and the feature input.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 一次超启发式运行的计数器与终止判定 / The ledger and termination decision of one hyper-heuristic run.
/// </summary>
/// <remarks>
/// **终止判定的顺序就是语义**。它是"多条互不相同的停止条件同时成立时报哪一个"的唯一约定，而**报哪一个会改变调用方看到的东西**：
/// 超时与预算耗尽在报告里是两件事，前者说明环境慢、后者说明搜索用完了额度。
///
/// 基线的顺序（本类照抄）：
/// 1. 诊断非空 → `Failed`（有错误就不该报"跑完了"）；
/// 2. 外部停止 → `Stopped`（人叫停的优先级高于预算）；
/// 3. 评估额度用尽 → `EvaluationLimit`；
/// 4. 步数上限 → `StepLimit`；
/// 5. 决策上限 → `StepLimit`（**决策与步数共用同一个终止原因**，因为对调用方而言它们是同一类"次数用完"）；
/// 6. 时间上限 → `TimeLimit`；
/// 7. 全部工作单元被禁用 → `NoCandidate`；
/// 8. 都不成立 → `null`（本次切片正常结束）。
///
/// **The order of the termination checks is the semantics.** It is the only convention for "which reason wins when several stop conditions hold at once",
/// and **which one wins changes what the caller sees**: a timeout and an exhausted budget are different reports — the first says the environment was
/// slow, the second says the search spent its allowance.
///
/// The baseline's order, copied here: diagnostics → `Failed`, stopped → `Stopped`, evaluation allowance → `EvaluationLimit`, steps → `StepLimit`,
/// decisions → `StepLimit` (**decisions and steps share a reason, because to the caller both mean "ran out of turns"**), time → `TimeLimit`,
/// every worker disabled → `NoCandidate`, otherwise `null` (this slice finished normally).
/// </remarks>
public sealed class HyperHeuristicLedger {
    /// <summary>已发出的决策 / The dispatched decisions.</summary>
    public long Dispatched { get; set; }

    /// <summary>已完成的决策 / The completed decisions.</summary>
    public long Completed { get; set; }

    /// <summary>已提交的步数 / The committed steps.</summary>
    public long Steps { get; set; }

    /// <summary>已计账的评估 / The accounted evaluations.</summary>
    public long Accounted { get; set; }

    /// <summary>成功的决策 / The successful decisions.</summary>
    public long Successes { get; set; }

    /// <summary>无进展的决策数 / The decisions without progress.</summary>
    public long Stagnation { get; set; }

    /// <summary>最近一次决策的消耗 / The cost of the latest decision.</summary>
    public long LastCost { get; set; }

    /// <summary>外部是否请求停止 / Whether a stop was requested from outside.</summary>
    public bool Stopped { get; set; }

    /// <summary>目标调用数 / The objective-call count.</summary>
    public long ObjectiveCalls { get; set; }

    /// <summary>诊断 / The diagnostics.</summary>
    public List<string> Diagnostics { get; } = new();

    /// <summary>
    /// 从检查点恢复计数 / Restore the counters from a checkpoint.
    /// </summary>
    /// <remarks>
    /// **诊断与转移是追加的，恢复时必须先清空**：不清空会让恢复后的账本同时含有"恢复前"和"恢复后"两段历史，
    /// 而**终止判定只看"诊断是否非空"**——于是一份从干净状态恢复的运行会立刻报 `Failed`。
    ///
    /// **Diagnostics and transitions append, so restore must clear them first**: without clearing, the restored ledger holds both the pre-restore and
    /// post-restore history, and **the termination decision only looks at "are diagnostics non-empty"** — so a run restored from a clean state would
    /// immediately report `Failed`.
    /// </remarks>
    /// <param name="evaluations">已用评估 / The consumed evaluations.</param>
    /// <param name="dispatched">已派发 / The dispatched count.</param>
    /// <param name="completed">已完成 / The completed count.</param>
    /// <param name="steps">步数 / The step count.</param>
    /// <param name="stagnation">停滞 / The stagnation count.</param>
    /// <param name="successes">成功数 / The success count.</param>
    /// <param name="lastCost">最近代价 / The latest cost.</param>
    /// <param name="objectiveCalls">目标调用数 / The objective-call count.</param>
    /// <param name="diagnostics">诊断 / The diagnostics.</param>
    /// <param name="transitions">转移记录 / The transition log.</param>
    /// <returns>转移记录条数 / The transition-log length.</returns>
    public int Restore(
        long evaluations, long dispatched, long completed, long steps, long stagnation,
        long successes, long lastCost, long objectiveCalls,
        IReadOnlyList<string>? diagnostics, IReadOnlyList<SelectionTransition>? transitions) {
        Accounted = evaluations;
        Dispatched = dispatched;
        Completed = completed;
        Steps = steps;
        Stagnation = stagnation;
        Successes = successes;
        LastCost = lastCost;
        ObjectiveCalls = objectiveCalls;
        Diagnostics.Clear();
        if (diagnostics is not null) {
            Diagnostics.AddRange(diagnostics);
        }
        Stopped = false;
        return transitions?.Count ?? 0;
    }

    /// <summary>
    /// 判定终止原因 / Decide the termination reason.
    /// </summary>
    /// <param name="budget">预算配置 / The budget configuration.</param>
    /// <param name="elapsedNanos">已用纳秒 / The elapsed nanoseconds.</param>
    /// <param name="allDisabled">是否全部工作单元被禁用 / Whether every worker is disabled.</param>
    /// <returns>终止原因；<c>null</c> 表示未终止 / The reason, or <c>null</c> when not terminated.</returns>
    public SearchTermination? Termination(
        BudgetConfig budget, long elapsedNanos, bool allDisabled) {
        ArgumentNullException.ThrowIfNull(budget);
        if (Diagnostics.Count > 0) {
            return SearchTermination.Failed;
        }
        if (Stopped) {
            return SearchTermination.Stopped;
        }
        if (Accounted >= (budget.Evaluations ?? long.MaxValue)) {
            return SearchTermination.EvaluationLimit;
        }
        if (Steps >= (budget.Steps ?? long.MaxValue)) {
            return SearchTermination.StepLimit;
        }
        if (Dispatched >= (budget.Decisions ?? long.MaxValue)) {
            return SearchTermination.StepLimit;
        }
        if (budget.TimeMillis is long millis && elapsedNanos >= millis * 1_000_000L) {
            return SearchTermination.TimeLimit;
        }
        return allDisabled ? SearchTermination.NoCandidate : null;
    }

    /// <summary>
    /// 构造特征输入 / Build the feature input.
    /// </summary>
    /// <remarks>
    /// **`diversity` 与 `successRate` 的分母都要兜 0**：第一次决策时工作集为空、成功次数为 0，直接除会得到 `NaN`——
    /// 而 `NaN` 会顺着特征一路进到选择策略里，**让所有动作的比较都变成"不更好"**，看起来就像"算法不收敛"。
    ///
    /// **Both `diversity` and `successRate` guard a zero denominator**: on the first decision the working set is empty and the success count is zero, so
    /// dividing directly yields `NaN` — and `NaN` travels through the features into the selection policy, **making every action comparison say "not
    /// better"**, which looks exactly like "the algorithm does not converge".
    /// </remarks>
    /// <param name="evaluationLimit">评估额度 / The evaluation allowance.</param>
    /// <param name="workingCount">工作集规模 / The working-set size.</param>
    /// <param name="distinctWorking">工作集中互不相同的解数 / The distinct solutions in the working set.</param>
    /// <param name="sliceEvaluations">切片评估额度 / The slice's evaluation allowance.</param>
    /// <returns>特征输入 / The feature input.</returns>
    public SearchProgress Progress(
        long evaluationLimit, int workingCount, int distinctWorking, long sliceEvaluations) {
        double diversity = workingCount <= 0 ? 0.0 : distinctWorking / (double)workingCount;
        Flt64 successRate = new(Successes / (double)(Completed > 0L ? Completed : 1L));
        Flt64 cost = new(sliceEvaluations > 0L ? LastCost / (double)sliceEvaluations : 0.0);
        return new SearchProgress(
            Dispatched, Accounted, evaluationLimit, Stagnation, diversity, successRate, cost);
    }
}
