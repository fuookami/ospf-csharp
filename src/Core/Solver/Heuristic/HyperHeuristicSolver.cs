// 超启发式求解器的门面 / The hyper-heuristic solver facade.
// 预检 → 装配工作单元 → 解析全局组件 → 构造运行 → 可选恢复策略档案 → 跑完 → 汇总结果。
// Mirrors `HyperHeuristicSolver`'s `open` / `invoke`'s (47241 B): preflight, assemble the workers,
// resolve the global components, construct the run, optionally restore a policy archive, run it, summarise.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 打开后的一次运行 / A run once opened.
/// </summary>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
/// <param name="Run">运行 / The run.</param>
/// <param name="Definition">策略定义摘要 / The policy-definition digest.</param>
/// <param name="Problem">问题 / The problem.</param>
/// <param name="Configuration">配置摘要 / The configuration digest.</param>
public sealed record HyperHeuristicSession<O, V>(
    HyperHeuristicRun<O, V> Run,
    string Definition,
    SearchProblem<O, V> Problem,
    string Configuration)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 超启发式求解器 / The hyper-heuristic solver.
/// </summary>
/// <remarks>
/// **`Open` 必须先跑预检，再装配**。顺序反过来会在配置非法时先构造出一批运行器、分配一批模型，
/// 然后在半路失败——**调用方拿到的错误指向某个构造函数，而不是配置里那一行**，而且那些已分配的资源没人回收。
///
/// **`Open` must run the preflight before assembling.** The other order constructs runners and allocates models on an illegal configuration and fails
/// midway — **the caller sees an error from some constructor rather than the offending configuration line**, and nothing reclaims what was allocated.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public static class HyperHeuristicSolver<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 打开一次运行 / Open a run.
    /// </summary>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="registry">搜索注册表 / The search registry.</param>
    /// <param name="policy">待恢复的策略档案；可为 <c>null</c> / The policy archive to restore, possibly <c>null</c>.</param>
    /// <param name="definition">策略定义摘要 / The policy-definition digest.</param>
    /// <param name="configuration">配置摘要 / The configuration digest.</param>
    /// <param name="schedule">调度录制；可为 <c>null</c> / The schedule recording, possibly <c>null</c>.</param>
    /// <returns>会话或失败 / The session or a failure.</returns>
    public static Result<HyperHeuristicSession<O, V>, ErrorCode, Error<ErrorCode>> Open(
        HyperHeuristicConfig config,
        SearchRegistry<O, V> registry,
        PolicyArchive? policy = null,
        string definition = "policy:default",
        string configuration = "configuration:default",
        ScheduleControl? schedule = null) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(registry);
        long seed = (config.Execution ?? new ExecutionConfig()).Seed;
        var random = new SearchRandom(seed);
        Result<Success, ErrorCode, Error<ErrorCode>> checkedConfig =
            HyperHeuristicPreflight<O, V>.Check(config, registry, new SearchRandom(seed));
        if (checkedConfig is Failed<Success, ErrorCode, Error<ErrorCode>> preflightFailure) {
            return Results.Failed<HyperHeuristicSession<O, V>>(preflightFailure.Error);
        }
        Result<IReadOnlyList<HyperHeuristicWorker<O, V>>, ErrorCode, Error<ErrorCode>> assembled =
            HyperHeuristicAssembly<O, V>.Build(config, registry, random);
        if (assembled is Failed<IReadOnlyList<HyperHeuristicWorker<O, V>>, ErrorCode, Error<ErrorCode>>
            assemblyFailure) {
            return Results.Failed<HyperHeuristicSession<O, V>>(assemblyFailure.Error);
        }
        IReadOnlyList<HyperHeuristicWorker<O, V>> workers =
            ((Ok<IReadOnlyList<HyperHeuristicWorker<O, V>>, ErrorCode, Error<ErrorCode>>)assembled).Value;
        Result<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>> problemResult =
            registry.Problem(config.Problem, random);
        if (problemResult is Failed<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>> problemFailure) {
            return Results.Failed<HyperHeuristicSession<O, V>>(problemFailure.Error);
        }
        SearchProblem<O, V> problem =
            ((Ok<SearchProblem<O, V>, ErrorCode, Error<ErrorCode>>)problemResult).Value;

        LearningConfig learning = config.Learning ?? new LearningConfig();
        MemoryConfig memoryConfig = config.Memory ?? new MemoryConfig();
        var ids = new List<string>(workers.Count);
        foreach (HyperHeuristicWorker<O, V> worker in workers) {
            ids.Add(worker.Id);
        }
        Result<ISelectionPolicy, ErrorCode, Error<ErrorCode>> selector = registry.Selection(
            config.Selection ?? HyperHeuristicDefaults.Selection, ids,
            learning.Mode != LearningMode.Frozen);
        if (selector is Failed<ISelectionPolicy, ErrorCode, Error<ErrorCode>> selectorFailure) {
            return Results.Failed<HyperHeuristicSession<O, V>>(selectorFailure.Error);
        }
        Result<IFeatureExtractor, ErrorCode, Error<ErrorCode>> features = registry.Features(
            learning.Features ?? HyperHeuristicDefaults.Features);
        if (features is Failed<IFeatureExtractor, ErrorCode, Error<ErrorCode>> featuresFailure) {
            return Results.Failed<HyperHeuristicSession<O, V>>(featuresFailure.Error);
        }
        Result<IRewardPolicy, ErrorCode, Error<ErrorCode>> reward = registry.Reward(
            learning.Reward ?? HyperHeuristicDefaults.Reward);
        if (reward is Failed<IRewardPolicy, ErrorCode, Error<ErrorCode>> rewardFailure) {
            return Results.Failed<HyperHeuristicSession<O, V>>(rewardFailure.Error);
        }
        long horizon = config.Budget.Decisions ?? config.Budget.Steps ?? config.Budget.Evaluations
            ?? long.MaxValue;
        Result<IMemoryAcceptance<O, V>, ErrorCode, Error<ErrorCode>> acceptance = registry.Acceptance(
            memoryConfig.Acceptance ?? HyperHeuristicDefaults.Acceptance, problem, horizon);
        if (acceptance is Failed<IMemoryAcceptance<O, V>, ErrorCode, Error<ErrorCode>> acceptanceFailure) {
            return Results.Failed<HyperHeuristicSession<O, V>>(acceptanceFailure.Error);
        }
        Result<IBudgetAllocator, ErrorCode, Error<ErrorCode>> allocator = registry.Allocator(
            config.Budget.Allocator ?? HyperHeuristicDefaults.Allocator);
        if (allocator is Failed<IBudgetAllocator, ErrorCode, Error<ErrorCode>> allocatorFailure) {
            return Results.Failed<HyperHeuristicSession<O, V>>(allocatorFailure.Error);
        }
        if (problem.ObjectiveCoordinates is not Func<O, IReadOnlyList<Flt64>> coordinates) {
            return Results.Failed<HyperHeuristicSession<O, V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "run requires explicit normalized minimization coordinates"));
        }

        var memory = new SearchMemory<O, V>(
            memoryConfig.ArchiveCapacity, memoryConfig.WorkingCapacity,
            ((Ok<IMemoryAcceptance<O, V>, ErrorCode, Error<ErrorCode>>)acceptance).Value,
            problem.MultiObjective,
            new Operator.MultiObjective.AnyArityMinimizationDominance(),
            coordinates,
            individual => problem.ValidSolution?.Invoke(individual.Solution) ?? true);
        long limit = config.Budget.Evaluations ?? long.MaxValue;
        var run = new HyperHeuristicRun<O, V>(
            config, problem, workers,
            ((Ok<ISelectionPolicy, ErrorCode, Error<ErrorCode>>)selector).Value,
            ((Ok<IFeatureExtractor, ErrorCode, Error<ErrorCode>>)features).Value,
            ((Ok<IMemoryAcceptance<O, V>, ErrorCode, Error<ErrorCode>>)acceptance).Value,
            ((Ok<IBudgetAllocator, ErrorCode, Error<ErrorCode>>)allocator).Value,
            memory,
            new SearchBudget(limit),
            random,
            ((Ok<IRewardPolicy, ErrorCode, Error<ErrorCode>>)reward).Value,
            schedule);
        if (policy is not null) {
            Result<Success, ErrorCode, Error<ErrorCode>> restored =
                HyperHeuristicPolicyArchive<O, V>.Restore(run, policy, definition);
            if (restored is Failed<Success, ErrorCode, Error<ErrorCode>> restoreFailure) {
                run.Close();
                return Results.Failed<HyperHeuristicSession<O, V>>(restoreFailure.Error);
            }
        }
        return Results.Ok(new HyperHeuristicSession<O, V>(run, definition, problem, configuration));
    }

    /// <summary>
    /// 打开、跑完、关闭 / Open, run to completion, close.
    /// </summary>
    /// <remarks>
    /// **关闭必须发生在 `finally` 里**：`Run` 抛异常时若不关闭，工作单元的模型与令牌表会一直挂着，
    /// 而调用方只看到一个异常，**看不出还有资源没释放**。
    ///
    /// **The close must happen in `finally`**: without it, an exception from `Run` leaves the workers' models and token tables held, and the caller
    /// only sees an exception, **with no sign of the unreleased resources**.
    /// </remarks>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="registry">搜索注册表 / The search registry.</param>
    /// <param name="policy">待恢复的策略档案；可为 <c>null</c> / The policy archive to restore, possibly <c>null</c>.</param>
    /// <param name="definition">策略定义摘要 / The policy-definition digest.</param>
    /// <returns>搜索结果或失败 / The search result or a failure.</returns>
    public static Result<SearchResult<O, V>, ErrorCode, Error<ErrorCode>> Invoke(
        HyperHeuristicConfig config,
        SearchRegistry<O, V> registry,
        PolicyArchive? policy = null,
        string definition = "policy:default") {
        Result<HyperHeuristicSession<O, V>, ErrorCode, Error<ErrorCode>> opened =
            Open(config, registry, policy, definition);
        if (opened is Failed<HyperHeuristicSession<O, V>, ErrorCode, Error<ErrorCode>> openFailure) {
            return Results.Failed<SearchResult<O, V>>(openFailure.Error);
        }
        HyperHeuristicRun<O, V> run =
            ((Ok<HyperHeuristicSession<O, V>, ErrorCode, Error<ErrorCode>>)opened).Value.Run;
        try {
            return Results.Ok(run.Run());
        } finally {
            run.Close();
        }
    }
}
