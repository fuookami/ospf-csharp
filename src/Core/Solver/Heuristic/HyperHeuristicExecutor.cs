// 一次派发的执行 / Executing one dispatch.
// 对应固定 Kotlin 基线 `HyperHeuristicSolver.kt`（47241 B）里 `HyperHeuristicRun.execute(dispatch)`。
// Mirrors `HyperHeuristicRun.execute(dispatch)` in the pinned baseline's `HyperHeuristicSolver.kt` (47241 B).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 一次派发 / One dispatch.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Decision">决策序号 / The decision index.</param>
/// <param name="Allowance">评估额度 / The evaluation allowance.</param>
/// <param name="StepAllowance">步数额度 / The step allowance.</param>
/// <param name="Seeds">待导入的种子解 / The seed solutions to import.</param>
public sealed record HyperHeuristicDispatch<O, V>(
    long Decision,
    long Allowance,
    long StepAllowance,
    IReadOnlyList<Solution<V>> Seeds)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 一次派发的执行结果 / The result of executing one dispatch.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Report">步进报告 / The step report.</param>
/// <param name="Consumed">已消耗评估 / The consumed evaluations.</param>
/// <param name="Initialized">工作单元是否已初始化 / Whether the worker is now initialised.</param>
/// <param name="ElapsedNanos">耗时纳秒 / The elapsed nanoseconds.</param>
public sealed record HyperHeuristicExecution<O, V>(
    Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> Report,
    long Consumed,
    bool Initialized,
    long ElapsedNanos)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 派发的执行器 / The dispatch executor.
/// </summary>
/// <remarks>
/// **种子的导入时机取决于工作单元是否已初始化**，这不是风格问题：
///
/// - **已初始化**：先导入再步进。种子立刻参与本步的搜索。
/// - **未初始化**：先步进、后导入。未初始化的会话在第一次步进时**建立种群**，此时导入的种子会被这次建立覆盖掉，
///   于是"导入了"与"没导入"没有任何区别——**而调用方以为共享已经发生**。
///
/// **The seed import point depends on whether the worker is already initialised**, and this is not a matter of style:
///
/// - **Initialised**: import first, then step. The seeds take part in this very step.
/// - **Not initialised**: step first, import after. An uninitialised session **builds its population** on the first step, which overwrites anything
///   imported beforehand — so "imported" and "not imported" are indistinguishable, **while the caller believes sharing took place**.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public static class HyperHeuristicExecutor<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 执行一次派发 / Execute one dispatch.
    /// </summary>
    /// <param name="session">会话 / The session.</param>
    /// <param name="budget">全局预算 / The global budget.</param>
    /// <param name="dispatch">派发 / The dispatch.</param>
    /// <param name="initialized">工作单元是否已初始化 / Whether the worker is initialised.</param>
    /// <param name="evaluationLimit">全局评估额度 / The global evaluation allowance.</param>
    /// <returns>执行结果 / The execution result.</returns>
    public static HyperHeuristicExecution<O, V> Execute(
        ISearchSession<O, V> session,
        SearchBudget budget,
        HyperHeuristicDispatch<O, V> dispatch,
        bool initialized,
        long evaluationLimit) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(dispatch);
        long started = Stopwatch.GetTimestamp();
        var candidates = new List<IIndividual<O, V>>();
        var slice = new SearchSlice(budget, dispatch.Allowance, dispatch.StepAllowance);
        try {
            if (initialized && dispatch.Seeds.Count > 0) {
                Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> imported =
                    session.ImportSolutions(dispatch.Seeds, slice.Child(dispatch.Allowance, 0L))
                        .GetAwaiter().GetResult();
                if (imported is Failed<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> importFailure) {
                    return new HyperHeuristicExecution<O, V>(
                        Results.Failed<SearchStepResult<O, V>>(importFailure.Error), slice.Used,
                        initialized, Elapsed(started));
                }
                candidates.AddRange(
                    ((Ok<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>>)imported).Value.Candidates);
            }

            long remaining = dispatch.Allowance - slice.Used;
            if (remaining < 0L) {
                remaining = 0L;
            }
            Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> stepped =
                session.Step(slice.Child(remaining, dispatch.StepAllowance)).GetAwaiter().GetResult();
            if (stepped is Failed<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> stepFailure) {
                return new HyperHeuristicExecution<O, V>(
                    Results.Failed<SearchStepResult<O, V>>(stepFailure.Error), slice.Used,
                    initialized, Elapsed(started));
            }
            SearchStepResult<O, V> report =
                ((Ok<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>>)stepped).Value;

            var importedCandidates = new List<IIndividual<O, V>>();
            if (!initialized && report.Steps > 0L && dispatch.Seeds.Count > 0) {
                long afterStep = dispatch.Allowance - slice.Used;
                if (afterStep < 0L) {
                    afterStep = 0L;
                }
                Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> imported =
                    session.ImportSolutions(dispatch.Seeds, slice.Child(afterStep, 0L))
                        .GetAwaiter().GetResult();
                if (imported is Failed<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> importFailure) {
                    return new HyperHeuristicExecution<O, V>(
                        Results.Failed<SearchStepResult<O, V>>(importFailure.Error), slice.Used,
                        initialized, Elapsed(started));
                }
                importedCandidates.AddRange(
                    ((Ok<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>>)imported).Value.Candidates);
            }

            bool nowInitialized = initialized || report.Steps > 0L;
            candidates.AddRange(report.Candidates);
            candidates.AddRange(importedCandidates);
            return new HyperHeuristicExecution<O, V>(
                Results.Ok(new SearchStepResult<O, V>(
                    candidates, slice.Used, report.Steps, report.Termination, report.Diagnostics)),
                slice.Used, nowInitialized, Elapsed(started));
        } finally {
            // 额度上限只用于诊断：本执行器不在这里强制收口，收口由运行器按自身预算控制。
            // The allowance is diagnostic only here: this executor does not enforce it; the run controls that through its own budget.
            _ = evaluationLimit;
        }
    }

    /// <summary>已耗时纳秒 / The elapsed nanoseconds.</summary>
    /// <param name="started">起始时间戳 / The starting timestamp.</param>
    /// <returns>纳秒 / Nanoseconds.</returns>
    private static long Elapsed(long started) =>
        (long)Stopwatch.GetElapsedTime(started).TotalNanoseconds;
}
