// 决策的归并与观测 / Merging and observing one decision.
// 对应固定 Kotlin 基线 `HyperHeuristicSolver.kt`（47241 B）里 `HyperHeuristicRun.commitOutcome(outcome)`：
// 归并候选、算奖励、构造 `SelectionTransition`、交给选择策略观测。
// Mirrors `HyperHeuristicRun.commitOutcome(outcome)` in the pinned baseline's `HyperHeuristicSolver.kt` (47241 B): merge the candidates, compute the
// reward, build the `SelectionTransition` and hand it to the selection policy.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 一次决策归并所需的全部输入 / Everything one decision needs in order to be merged.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Episode">回合标识 / The episode identifier.</param>
/// <param name="Decision">决策序号 / The decision index.</param>
/// <param name="Action">被派发的算法 / The dispatched algorithm.</param>
/// <param name="Before">派发时的上下文 / The context at dispatch time.</param>
/// <param name="Available">派发时的可选动作 / The actions available at dispatch time.</param>
/// <param name="Reference">派发前的记忆内容 / The memory contents before the dispatch.</param>
/// <param name="Report">步进报告 / The step report.</param>
/// <param name="Consumed">已消耗评估 / The consumed evaluations.</param>
/// <param name="ElapsedNanos">耗时纳秒 / The elapsed nanoseconds.</param>
/// <param name="FeatureVersion">特征版本 / The feature version.</param>
/// <param name="PolicyVersion">策略版本 / The policy version.</param>
public sealed record HyperHeuristicDecision<O, V>(
    string Episode,
    long Decision,
    string Action,
    SelectionContext Before,
    IReadOnlyList<string> Available,
    IReadOnlyList<IIndividual<O, V>> Reference,
    SearchStepResult<O, V> Report,
    long Consumed,
    long ElapsedNanos,
    string FeatureVersion,
    long PolicyVersion)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 一次决策的归并结果 / The merged outcome of one decision.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Improved">是否有所改进 / Whether it improved anything.</param>
/// <param name="Reward">奖励 / The reward.</param>
/// <param name="Disable">是否应禁用该算法 / Whether the algorithm should be disabled.</param>
/// <param name="Diagnostics">诊断 / The diagnostics.</param>
/// <param name="Transition">转移；<c>null</c> 表示不观测 / The transition, or <c>null</c> when not observed.</param>
public sealed record HyperHeuristicMerge<O, V>(
    bool Improved,
    Flt64 Reward,
    bool Disable,
    IReadOnlyList<string> Diagnostics,
    SelectionTransition? Transition)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 决策的归并与观测 / Merging and observing a decision.
/// </summary>
/// <remarks>
/// **失败与"没有产出"是两件不同的事，必须分别处理**：
///
/// - 报告本身失败 → **禁用该算法**，并记一条失败转移。不禁用的话，下一轮还会选中它，**于是每轮都以同样的方式失败**，
///   而诊断只增不减。
/// - 报告成功但零步、且全局**完全没有**评估/决策/时间预算 → 这是"切片额度不足以提交一个完整步"的**配置错误**，
///   同样禁用。**这一条只在"没有任何别的终止手段"时才成立**：有预算时零步是正常的切片边界。
///
/// **Failure and "no output" are two different things and must be handled separately**:
///
/// - The report itself failed → **disable the algorithm** and record a failed transition. Without disabling, the next round picks it again,
///   **so every round fails the same way** while diagnostics only accumulate.
/// - The report succeeded with zero steps while the configuration has **no** evaluation, decision or time budget at all → that is a
///   configuration error ("the slice allowance cannot commit a complete step"), and it is also disabled. **This one only holds when there is no
///   other termination means**: with a budget, zero steps is an ordinary slice boundary.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public static class HyperHeuristicCommit<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 归并一次决策 / Merge one decision.
    /// </summary>
    /// <param name="decision">决策 / The decision.</param>
    /// <param name="memory">共享记忆 / The shared memory.</param>
    /// <param name="reward">回报策略 / The reward policy.</param>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="coordinates">目标坐标投影；可为 <c>null</c> / The objective coordinate projection, possibly <c>null</c>.</param>
    /// <returns>归并结果 / The merge result.</returns>
    public static HyperHeuristicMerge<O, V> Merge(
        HyperHeuristicDecision<O, V> decision,
        SearchMemory<O, V> memory,
        IRewardPolicy reward,
        HyperHeuristicConfig config,
        Func<O, IReadOnlyList<Flt64>>? coordinates) {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(reward);
        ArgumentNullException.ThrowIfNull(config);
        var diagnostics = new List<string>();
        SearchStepResult<O, V> report = decision.Report;

        if (report.Termination == SearchTermination.Failed) {
            diagnostics.Add(report.Diagnostics.Count > 0
                ? string.Join("; ", report.Diagnostics)
                : "算法执行失败 / Algorithm failed: " + decision.Action);
            return new HyperHeuristicMerge<O, V>(
                false, Flt64.Zero, true, diagnostics,
                Transition(decision, Flt64.Zero, true, new List<string>()));
        }

        bool disable = report.Termination == SearchTermination.NoCandidate
            || (report.Evaluations == 0L && report.Steps == 0L);
        BudgetConfig budget = config.Budget;
        if (report.Steps == 0L
            && report.Termination == SearchTermination.EvaluationLimit
            && budget.Evaluations is null && budget.Decisions is null && budget.TimeMillis is null) {
            disable = true;
            diagnostics.Add("片段评估额度不足以提交完整步骤 / Slice allowance cannot commit a complete step: "
                + decision.Action);
        }

        (bool improved, double ratio) = memory.Contribution(decision.Reference, report.Candidates);
        memory.Offer(report.Candidates);

        Result<Flt64, ErrorCode, Error<ErrorCode>> rewarded = reward.Evaluate(new RewardContext(
            improved,
            new Flt64(ratio),
            report.Evaluations,
            decision.ElapsedNanos,
            decision.Action,
            Project(decision.Reference, coordinates),
            Project(report.Candidates, coordinates)));
        Flt64 value = Flt64.Zero;
        if (rewarded is Failed<Flt64, ErrorCode, Error<ErrorCode>> rewardFailure) {
            // **奖励算不出来不等于这次决策失败**：记录诊断、按零奖励继续观测。
            // 丢弃整条转移会让策略少看一步，而"奖励是 NaN"通常是问题定义的问题，不该连累调度。
            // **A reward that cannot be computed is not a failed decision**: record it, observe with zero reward and move on. Dropping the whole
            // transition would hide one step from the policy, and a NaN reward is usually a problem-definition issue that must not take the schedule
            // down with it.
            diagnostics.Add("奖励计算失败 / Reward computation failed: " + rewardFailure.Error.Message);
        } else {
            value = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)rewarded).Value;
        }

        return new HyperHeuristicMerge<O, V>(
            improved, value, disable, diagnostics,
            Transition(decision, value, false, new List<string>()));
    }

    /// <summary>
    /// 构造一次转移 / Build one transition.
    /// </summary>
    /// <param name="decision">决策 / The decision.</param>
    /// <param name="reward">奖励 / The reward.</param>
    /// <param name="failed">是否失败 / Whether it failed.</param>
    /// <param name="nextAvailable">下一步可选动作 / The actions available next.</param>
    /// <returns>转移 / The transition.</returns>
    private static SelectionTransition Transition(
        HyperHeuristicDecision<O, V> decision, Flt64 reward, bool failed,
        IReadOnlyList<string> nextAvailable) =>
        new(
            decision.Episode,
            decision.Decision,
            decision.Action,
            decision.Before,
            decision.Before,
            decision.Available,
            nextAvailable,
            reward,
            decision.Consumed,
            failed,
            decision.PolicyVersion,
            decision.FeatureVersion,
            "algorithm",
            0L,
            failed,
            false,
            decision.ElapsedNanos);

    /// <summary>
    /// 把个体投影成目标坐标 / Project individuals onto objective coordinates.
    /// </summary>
    /// <param name="individuals">个体 / The individuals.</param>
    /// <param name="coordinates">投影；<c>null</c> 时给空表 / The projection, or an empty list when <c>null</c>.</param>
    /// <returns>坐标 / The coordinates.</returns>
    private static IReadOnlyList<IReadOnlyList<Flt64>> Project(
        IReadOnlyList<IIndividual<O, V>> individuals,
        Func<O, IReadOnlyList<Flt64>>? coordinates) {
        if (coordinates is null) {
            return Array.Empty<IReadOnlyList<Flt64>>();
        }
        var projected = new List<IReadOnlyList<Flt64>>(individuals.Count);
        foreach (IIndividual<O, V> individual in individuals) {
            projected.Add(coordinates(individual.Objective));
        }
        return projected;
    }
}
