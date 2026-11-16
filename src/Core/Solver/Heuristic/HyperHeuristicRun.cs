// 超启发式运行 / The hyper-heuristic run.
// 对应固定 Kotlin （47241 B）里 `HyperHeuristicRun` 的单线程主体：
// 选动作 → 分配额度 → 执行 → 归并 → 记账 → 决策，加上 `run` 循环与 `result`。
// Single-threaded body: pick an action, allocate an
// allowance, execute, merge, account, decide — plus the `run` loop and `result`.

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
/// 一次超启发式运行 / One hyper-heuristic run.
/// </summary>
/// <remarks>
/// **本实现只做单线程顺序执行**，不支持 `ExecutionMode.Asynchronous` 下的多线程并发派发。
/// 差异的后果是明确的：并发下"同一决策序号可能有两个工作单元同时记账"，而顺序执行**每一步的账都是确定的**。
/// 因此这里的门槛证据与并发路径不可互比——**不声称性能等价，也不声称并发行为一致**。
/// `ponytail:` 并发派发留到需要时再加，届时按工作单元分片的调度是升级路径。
///
/// **This implementation runs single-threaded and sequential** and does not support multi-threaded concurrent dispatch under
/// `ExecutionMode.Asynchronous`. The consequence is explicit: under concurrency one decision index may be accounted by two workers at once, while
/// sequential execution **accounts every step deterministically**. The gate evidence here is therefore not comparable to a concurrent
/// path — **no claim of equal performance, none of identical concurrency semantics**.
/// `ponytail:` concurrent dispatch is deferred until needed; index-sharded scheduling per worker is the upgrade path.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class HyperHeuristicRun<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly HyperHeuristicConfig _config;
    private readonly SearchProblem<O, V> _problem;
    private readonly IReadOnlyList<HyperHeuristicWorker<O, V>> _workers;
    private readonly ISelectionPolicy _policy;
    private readonly IFeatureExtractor _features;
    private readonly IMemoryAcceptance<O, V> _acceptance;
    private readonly IRewardPolicy? _reward;
    private readonly List<SelectionTransition> _transitions = new();
    private readonly IBudgetAllocator _allocator;
    private readonly SearchMemory<O, V> _memory;
    private readonly SearchBudget _budget;
    private readonly SearchRandom _random;
    private readonly HyperHeuristicLedger _ledger = new();
    private readonly bool[] _initialized;
    private readonly bool[] _disabled;
    private readonly ScheduleControl? _schedule;
    private bool _closed;

    /// <summary>
    /// 构造一次运行 / Construct a run.
    /// </summary>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="workers">工作单元 / The working units.</param>
    /// <param name="policy">选择策略 / The selection policy.</param>
    /// <param name="features">特征提取器 / The feature extractor.</param>
    /// <param name="acceptance">接受策略 / The acceptance policy.</param>
    /// <param name="allocator">预算分配器 / The budget allocator.</param>
    /// <param name="memory">共享记忆 / The shared memory.</param>
    /// <param name="budget">预算 / The budget.</param>
    /// <param name="random">随机源 / The random source.</param>
    /// <param name="reward">回报策略；缺省按"有改进即 1" / The reward policy, defaulting to "1 when improved".</param>
    /// <param name="schedule">调度录制；<c>null</c> 表示不录制 / The schedule recording, or <c>null</c> for no recording.</param>
    public HyperHeuristicRun(
        HyperHeuristicConfig config,
        SearchProblem<O, V> problem,
        IReadOnlyList<HyperHeuristicWorker<O, V>> workers,
        ISelectionPolicy policy,
        IFeatureExtractor features,
        IMemoryAcceptance<O, V> acceptance,
        IBudgetAllocator allocator,
        SearchMemory<O, V> memory,
        SearchBudget budget,
        SearchRandom random,
        IRewardPolicy? reward = null,
        ScheduleControl? schedule = null) {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(workers);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(acceptance);
        ArgumentNullException.ThrowIfNull(allocator);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(random);
        _config = config;
        _problem = problem;
        _workers = workers;
        _policy = policy;
        _features = features;
        _acceptance = acceptance;
        _allocator = allocator;
        _memory = memory;
        _budget = budget;
        _random = random;
        _reward = reward;
        _schedule = schedule;
        _initialized = new bool[workers.Count];
        _disabled = new bool[workers.Count];
    }

    /// <summary>计数器与终止判定 / The ledger and termination decision.</summary>
    public HyperHeuristicLedger Ledger => _ledger;

    /// <summary>共享记忆 / The shared memory.</summary>
    public SearchMemory<O, V> Memory => _memory;

    /// <summary>工作单元 / The working units.</summary>
    public IReadOnlyList<HyperHeuristicWorker<O, V>> Workers => _workers;

    /// <summary>特征版本 / The feature version.</summary>
    public string FeatureVersion => _features.Version;

    /// <summary>随机源当前状态 / The current random state.</summary>
    public long RandomState => _random.State;

    /// <summary>配置 / The configuration.</summary>
    public HyperHeuristicConfig Configuration => _config;

    /// <summary>问题 / The problem.</summary>
    public SearchProblem<O, V> Problem => _problem;

    /// <summary>根随机源 / The root random source.</summary>
    public SearchRandom Random => _random;

    /// <summary>选择策略 / The selection policy.</summary>
    public ISelectionPolicy Policy => _policy;

    /// <summary>根策略快照 / The root policy snapshot.</summary>
    /// <returns>策略快照 / The policy snapshot.</returns>
    public PolicySnapshot PolicySnapshot() => _policy.Snapshot();

    /// <summary>某个工作单元是否已初始化 / Whether a worker is initialised.</summary>
    /// <param name="id">工作单元标识 / The worker identifier.</param>
    /// <returns>是否已初始化 / Whether it is initialised.</returns>
    public bool IsInitialized(string id) {
        int index = IndexOf(id);
        return index >= 0 && _initialized[index];
    }

    /// <summary>某个工作单元是否被禁用 / Whether a worker is disabled.</summary>
    /// <param name="id">工作单元标识 / The worker identifier.</param>
    /// <returns>是否被禁用 / Whether it is disabled.</returns>
    public bool IsDisabled(string id) {
        int index = IndexOf(id);
        return index >= 0 && _disabled[index];
    }

    /// <summary>设置初始化状态 / Set the initialised flag.</summary>
    /// <param name="id">工作单元标识 / The worker identifier.</param>
    /// <param name="value">取值 / The value.</param>
    public void SetInitialized(string id, bool value) {
        int index = IndexOf(id);
        if (index >= 0) {
            _initialized[index] = value;
        }
    }

    /// <summary>设置禁用状态 / Set the disabled flag.</summary>
    /// <param name="id">工作单元标识 / The worker identifier.</param>
    /// <param name="value">取值 / The value.</param>
    public void SetDisabled(string id, bool value) {
        int index = IndexOf(id);
        if (index >= 0) {
            _disabled[index] = value;
        }
    }

    /// <summary>追加一条转移记录 / Append a transition record.</summary>
    /// <param name="transition">转移 / The transition.</param>
    public void AddTransition(SelectionTransition transition) {
        ArgumentNullException.ThrowIfNull(transition);
        _transitions.Add(transition);
    }

    /// <summary>清空转移记录 / Clear the transition log.</summary>
    public void ClearTransitions() => _transitions.Clear();

    /// <summary>已记录的决策转移 / The recorded decision transitions.</summary>
    public IReadOnlyList<SelectionTransition> Transitions => new List<SelectionTransition>(_transitions);

    /// <summary>请求停止 / Request a stop.</summary>
    public void RequestStop() => _ledger.Stopped = true;

    /// <summary>
    /// 运行到终止 / Run until termination.
    /// </summary>
    /// <returns>运行结果 / The run result.</returns>
    public SearchResult<O, V> Run() {
        while (!_closed) {
            Result<bool, ErrorCode, Error<ErrorCode>> advanced = Advance();
            if (advanced is Failed<bool, ErrorCode, Error<ErrorCode>> failure) {
                _ledger.Diagnostics.Add(failure.Error.Message);
                break;
            }
            if (!((Ok<bool, ErrorCode, Error<ErrorCode>>)advanced).Value) {
                break;
            }
        }
        return Result();
    }

    /// <summary>
    /// 推进一轮 / Advance one round.
    /// </summary>
    /// <remarks>
    /// **一轮可以派发多个工作单元**（`parallelism > 1`），但**执行并行、归并顺序**：并行归并会让两个工作单元同时改写同一份记忆与
    /// 同一张策略表，结果是"有时改进被吞掉、有时奖励算在别人头上"，而**两者的运行都正常结束**。
    ///
    /// **A round may dispatch several workers** (`parallelism > 1`), but **execution is parallel and merging is sequential**: merging in parallel lets two
    /// workers rewrite the same memory and policy table at once, and the result is "sometimes an improvement is swallowed, sometimes a reward lands on
    /// someone else" while **both runs finish normally**.
    ///
    /// **额度必须在派发时预留**。按"剩余额度"逐个分配而不预留，一轮里每个工作单元都看到同一份剩余额，**一轮就能把全局预算超支数倍**。
    ///
    /// **The allowance must be reserved at dispatch time.** Allocating each slot from the same "remaining" figure without reserving lets every worker in a
    /// round see the same remainder, **overspending the global budget several times in a single round**.
    /// </remarks>
    /// <returns>是否还应继续 / Whether to keep going.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> Advance() {
        if (_closed) {
            return Results.Ok(false);
        }
        if (Termination() is not null) {
            return Results.Ok(false);
        }
        Result<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>, ErrorCode, Error<ErrorCode>> built =
            BuildRound();
        if (built is Failed<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>, ErrorCode, Error<ErrorCode>>
            roundFailure) {
            return Results.Failed<bool>(roundFailure.Error);
        }
        IReadOnlyList<HyperHeuristicRoundSlot<O, V>> slots =
            ((Ok<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>, ErrorCode, Error<ErrorCode>>)built).Value;
        if (slots.Count == 0) {
            return Results.Ok(false);
        }

        IReadOnlyList<HyperHeuristicRoundResult<O, V>> executed =
            HyperHeuristicParallelRound<O, V>.Execute(
                slots, _budget, _budget.Limit, Parallelism > 1 && slots.Count > 1);
        foreach (HyperHeuristicRoundResult<O, V> result in executed) {
            Result<Success, ErrorCode, Error<ErrorCode>> merged = MergeRoundResult(result);
            if (merged is Failed<Success, ErrorCode, Error<ErrorCode>> mergeFailure) {
                return Results.Failed<bool>(mergeFailure.Error);
            }
        }
        Result<Success, ErrorCode, Error<ErrorCode>>? yielded = _schedule?.Event(
            new ScheduleEvent("yield"));
        return yielded is Failed<Success, ErrorCode, Error<ErrorCode>> yieldFailure
            ? Results.Failed<bool>(yieldFailure.Error)
            : Results.Ok(true);
    }

    /// <summary>本配置的并行度 / The parallelism of this configuration.</summary>
    private int Parallelism {
        get {
            ExecutionConfig execution = _config.Execution ?? new ExecutionConfig();
            if (execution.Mode == ExecutionMode.Serial) {
                return 1;
            }
            return execution.Parallelism < 1 ? 1 : execution.Parallelism;
        }
    }

    /// <summary>
    /// 组装一轮的派发 / Assemble one round of dispatches.
    /// </summary>
    /// <returns>派发列表或失败 / The dispatch list or a failure.</returns>
    private Result<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>, ErrorCode, Error<ErrorCode>>
        BuildRound() {
        var slots = new List<HyperHeuristicRoundSlot<O, V>>();
        IReadOnlyList<string> available = Available();
        if (available.Count == 0) {
            return Results.Ok<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>>(slots);
        }
        var picked = new List<string>(available);
        long reserved = 0L;
        long decisionLimit = _config.Budget.Decisions ?? long.MaxValue;
        bool began = false;
        while (slots.Count < Parallelism
            && picked.Count > 0
            && _ledger.Dispatched < decisionLimit) {
            long remaining = _budget.Limit - _ledger.Accounted - reserved;
            if (remaining <= 0L) {
                break;
            }
            SelectionContext context = Context();
            Result<string, ErrorCode, Error<ErrorCode>> selected =
                _policy.Select(context, picked, _random);
            if (selected is Failed<string, ErrorCode, Error<ErrorCode>> selectFailure) {
                return Results.Failed<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>>(selectFailure.Error);
            }
            string action = ((Ok<string, ErrorCode, Error<ErrorCode>>)selected).Value;
            int index = IndexOf(action);
            if (index < 0 || !Contains(picked, action)) {
                // **选择器给出被屏蔽的动作必须丢弃并报错**，不能"就当它没选"。放过它会让同一轮里同一个工作单元被派发两次，
                // 而两次的额度各自预留、最后只有一次归并——**另一份额度凭空消失**。
                // **A masked action from the selector must be discarded and reported**, never treated as "it chose nothing". Letting it through
                // dispatches the same worker twice in one round, both allowances are reserved, only one is merged — **the other vanishes**.
                _policy.Discard(context.Decision);
                return Results.Failed<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "selector returned a masked action: " + action));
            }
            long configured = _config.Budget.SliceEvaluations;
            long allowance = _allocator.Allocate(action, configured, remaining);
            if (allowance <= 0L || allowance > remaining || allowance > configured) {
                _policy.Discard(context.Decision);
                return Results.Failed<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "allocator returned an invalid allowance"));
            }

            HyperHeuristicWorker<O, V> worker = _workers[index];
            IReadOnlyList<IIndividual<O, V>> reference = _memory.Working;
            var seeds = new List<Output.Solution<V>>();
            foreach (IIndividual<O, V> individual in reference) {
                if (seeds.Count >= 1) {
                    break;
                }
                seeds.Add(new Output.Solution<V>(individual.Solution));
            }
            if (!began) {
                Result<Success, ErrorCode, Error<ErrorCode>>? begin =
                    _schedule?.Event(new ScheduleEvent("begin"));
                if (begin is Failed<Success, ErrorCode, Error<ErrorCode>> beginFailure) {
                    return Results.Failed<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>>(
                        beginFailure.Error);
                }
                began = true;
            }
            var dispatch = new HyperHeuristicDispatch<O, V>(
                _ledger.Dispatched, allowance, _config.Budget.SliceSteps, seeds);
            // **派发事件带着随机流状态**：重放时只有"同一状态 + 同一动作列表"才能给出同一序列，否则重放会在选择那一步分叉，
            // 而分叉点之前的日志看上去完全一致。
            // **The dispatch event carries the random stream state**: only "same state plus same action list" reproduces the same sequence, otherwise
            // the replay diverges at the selection step while the log before that point looks identical.
            Result<Success, ErrorCode, Error<ErrorCode>>? recorded = _schedule?.Event(new ScheduleEvent(
                "dispatch", dispatch.Decision, action, allowance, dispatch.StepAllowance,
                _random.State, new List<string>(picked)));
            if (recorded is Failed<Success, ErrorCode, Error<ErrorCode>> dispatchFailure) {
                return Results.Failed<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>>(
                    dispatchFailure.Error);
            }
            _ledger.Dispatched++;
            reserved += allowance;
            picked.Remove(action);
            slots.Add(new HyperHeuristicRoundSlot<O, V>(
                index, worker.Session, dispatch, context, available,
                reference, _initialized[index]));
        }
        return Results.Ok<IReadOnlyList<HyperHeuristicRoundSlot<O, V>>>(slots);
    }

    /// <summary>
    /// 归并一个执行结果 / Merge one execution result.
    /// </summary>
    /// <param name="result">执行结果 / The execution result.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    private Result<Success, ErrorCode, Error<ErrorCode>> MergeRoundResult(
        HyperHeuristicRoundResult<O, V> result) {
        HyperHeuristicRoundSlot<O, V> slot = result.Slot;
        HyperHeuristicExecution<O, V> execution = result.Execution;
        string action = _workers[slot.Index].Id;
        if (execution.Initialized != _initialized[slot.Index]) {
            _initialized[slot.Index] = execution.Initialized;
        }
        if (execution.Report is Failed<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> stepFailure) {
            return Results.Failed<Success>(stepFailure.Error);
        }
        SearchStepResult<O, V> report =
            ((Ok<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>>)execution.Report).Value;

        // **归并、奖励与转移统一交给 `HyperHeuristicCommit`**：把这段逻辑写在运行器里，会让"奖励算不出来怎么办""零步要不要禁用"
        // 这类决定散落在两处，而两处迟早会给出不同答案。
        // **Merging, reward and the transition all go through `HyperHeuristicCommit`**: duplicating this logic inside the run would scatter decisions
        // such as "what happens when the reward cannot be computed" and "does a zero step disable the worker" across two places, and the two would
        // eventually disagree.
        HyperHeuristicMerge<O, V> merged = HyperHeuristicCommit<O, V>.Merge(
            new HyperHeuristicDecision<O, V>(
                Episode(), slot.Dispatch.Decision, action, slot.Context, slot.Available,
                slot.Reference, report, execution.Consumed, execution.ElapsedNanos,
                _features.Version, 0L),
            _memory,
            _reward ?? new ImprovementRewardPolicy(),
            _config,
            _problem.ObjectiveCoordinates);
        _ledger.Accounted += execution.Consumed;
        _ledger.Steps += report.Steps;
        _ledger.Completed++;
        _ledger.LastCost = execution.Consumed;
        _ledger.Successes += merged.Improved ? 1L : 0L;
        _ledger.Stagnation = merged.Improved ? 0L : _ledger.Stagnation + 1L;
        foreach (string diagnostic in merged.Diagnostics) {
            _ledger.Diagnostics.Add(diagnostic);
        }
        if (merged.Transition is not null) {
            _transitions.Add(merged.Transition);
            // **转移必须真的交给选择策略**。只把它记进日志、不调 `Observe`，运行照样跑完、转移日志照样完整，
            // 但**策略一次都没更新**——于是"训练出的策略"与"初始策略"逐字节相同，而报告里写着训练了 N 个回合。
            // **The transition must actually reach the selection policy.** Only logging it without calling `Observe` still completes the run and still
            // leaves a complete transition log, but **the policy never updates once** — so the "trained policy" is byte-identical to the initial one
            // while the report claims N trained episodes.
            // **冻结由策略自身的 `Learning` 标志执行**（注册表按 `LearningMode` 传 `learning: false`），
            // 运行器不再叠一层同样的判断——两层门控会让"哪一层在起作用"无从判断，而**删掉任何一层测试都照样通过**。
            // **The freeze is enforced by the policy's own `Learning` flag** (the registry passes `learning: false` for `LearningMode.Frozen`), and the
            // run does not stack a second identical check — two gates make it impossible to tell which one is doing the work, and **deleting either
            // leaves the tests green**.
            Result<Success, ErrorCode, Error<ErrorCode>> observed = _policy.Observe(merged.Transition);
            if (observed is Failed<Success, ErrorCode, Error<ErrorCode>> observeFailure) {
                _ledger.Diagnostics.Add("策略观测失败 / Policy observation failed: "
                    + observeFailure.Error.Message);
            }
        }
        if (merged.Disable) {
            _disabled[slot.Index] = true;
        }
        Result<Success, ErrorCode, Error<ErrorCode>>? completed = _schedule?.Event(new ScheduleEvent(
            "complete", slot.Dispatch.Decision, action, execution.Consumed, report.Steps,
            Stopped: _ledger.Stopped, Failed: report.Termination == SearchTermination.Failed));
        return completed is Failed<Success, ErrorCode, Error<ErrorCode>> completeFailure
            ? Results.Failed<Success>(completeFailure.Error)
            : Results.Ok(new Success());
    }

    /// <summary>动作是否在列表里 / Whether an action is in the list.</summary>
    /// <param name="list">列表 / The list.</param>
    /// <param name="action">动作 / The action.</param>
    /// <returns>是否存在 / Whether it is present.</returns>
    private static bool Contains(IReadOnlyList<string> list, string action) {
        for (int index = 0; index < list.Count; index++) {
            if (string.Equals(list[index], action, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 汇总运行结果 / Summarise the run.
    /// </summary>
    /// <returns>运行结果 / The run result.</returns>
    public SearchResult<O, V> Result() => new(
        _memory.Archive,
        _problem.MultiObjective,
        Termination() ?? SearchTermination.SliceComplete,
        _ledger.Accounted,
        _ledger.Completed,
        _ledger.Steps,
        _ledger.LastCost,
        _ledger.Diagnostics);

    /// <summary>关闭运行 / Close the run.</summary>
    /// <returns>关闭结果 / The close result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Close() {
        if (_closed) {
            return Results.Ok(new Success());
        }
        _closed = true;
        foreach (HyperHeuristicWorker<O, V> worker in _workers) {
            worker.Session.Close();
        }
        return Results.Ok(new Success());
    }

    /// <summary>回合标识 / The episode identifier.</summary>
    /// <returns>回合标识 / The episode identifier.</returns>
    private string Episode() =>
        _problem.Id + ":" + _problem.Fingerprint + ":" + (_config.Execution?.Seed ?? 0L);

    /// <summary>
    /// 记录结束事件并导出调度日志 / Record the end event and export the schedule log.
    /// </summary>
    /// <returns>日志或失败 / The log or a failure.</returns>
    public Result<ScheduleLog, ErrorCode, Error<ErrorCode>> ExportSchedule() {
        if (_schedule is null) {
            return Results.Failed<ScheduleLog>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "schedule recording is not enabled"));
        }
        // **`end` 只在不推进时记一次**，且带着累计评估与步数：`ValidatePrefix` 会核对这两个累计值，
        // 对不上说明"结束时刻的账"和"事件序列的账"已经不一致。
        // **`end` is recorded once, only while not advancing**, and carries the accumulated evaluations and steps: `ValidatePrefix` cross-checks
        // both totals, and a mismatch means "the ledger at the end" and "the ledger of the event sequence" have already disagreed.
        Result<Success, ErrorCode, Error<ErrorCode>> ended = _schedule.Event(new ScheduleEvent(
            "end", Evaluations: _ledger.Accounted, Steps: _ledger.Steps,
            Stopped: _ledger.Stopped, Failed: _ledger.Diagnostics.Count > 0));
        return ended is Failed<Success, ErrorCode, Error<ErrorCode>> endFailure
            ? Results.Failed<ScheduleLog>(endFailure.Error)
            : _schedule.Export();
    }

    /// <summary>当前终止原因 / The current termination reason.</summary>
    /// <returns>终止原因；<c>null</c> 表示未终止 / The reason, or <c>null</c>.</returns>
    private SearchTermination? Termination() => _ledger.Termination(
        _config.Budget, _ledger.LastCost, AllDisabled());

    /// <summary>是否全部工作单元被禁用 / Whether every worker is disabled.</summary>
    /// <returns>是否全部禁用 / Whether all are disabled.</returns>
    private bool AllDisabled() {
        foreach (bool disabled in _disabled) {
            if (!disabled) {
                return false;
            }
        }
        return _disabled.Length > 0;
    }

    /// <summary>构造选择上下文 / Build the selection context.</summary>
    /// <returns>选择上下文 / The selection context.</returns>
    private SelectionContext Context() {
        IReadOnlyList<IIndividual<O, V>> working = _memory.Working;
        var distinct = new List<IReadOnlyList<V>>(working.Count);
        foreach (IIndividual<O, V> individual in working) {
            bool seen = false;
            foreach (IReadOnlyList<V> existing in distinct) {
                if (existing.Count == individual.Solution.Count) {
                    bool same = true;
                    for (int index = 0; index < existing.Count && same; index++) {
                        same = existing[index].Equals(individual.Solution[index]);
                    }
                    seen = same;
                    if (seen) {
                        break;
                    }
                }
            }
            if (!seen) {
                distinct.Add(individual.Solution);
            }
        }
        return _features.Extract(_ledger.Progress(
            _budget.Limit, working.Count, distinct.Count, _config.Budget.SliceEvaluations));
    }

    /// <summary>当前可选的算法 / The currently available algorithms.</summary>
    /// <returns>算法标识 / The algorithm identifiers.</returns>
    private IReadOnlyList<string> Available() {
        var available = new List<string>(_workers.Count);
        for (int index = 0; index < _workers.Count; index++) {
            if (!_disabled[index]) {
                available.Add(_workers[index].Id);
            }
        }
        return available;
    }

    /// <summary>按标识查找工作单元 / Find a worker by identifier.</summary>
    /// <param name="id">标识 / The identifier.</param>
    /// <returns>下标；未找到为 -1 / The index, or -1.</returns>
    private int IndexOf(string id) {
        for (int index = 0; index < _workers.Count; index++) {
            if (string.Equals(_workers[index].Id, id, StringComparison.Ordinal)) {
                return index;
            }
        }
        return -1;
    }
}
