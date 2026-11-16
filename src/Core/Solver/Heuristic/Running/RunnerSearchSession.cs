// 运行器会话 / The runner session.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// 计费评估模型 / A budget-accounting evaluation model.
/// </summary>
/// <remarks>
/// **每次目标评估都要先申请额度**，申请失败才置 `Exhausted`。先评估再"事后发现超额"会让一次评估的结果被丢弃，
/// **而调用方看到的评估计数与实际发生的模型调用数不符**。
/// **Every objective evaluation must acquire allowance first**, and only a failed acquisition sets `Exhausted`. Evaluating first and
/// discovering the overrun afterwards discards the result of an evaluation, **so the evaluation count the caller sees disagrees with the
/// number of model calls that actually happened**.
/// **维度校验、可行性与约束读数都在模型调用之前**。把它们放到之后会让一次"非法解"仍消耗额度，
/// **而评估上限被用来支付不可能产生候选的调用**。
/// **Dimension validation, feasibility and the constraint reading all precede the model call.** Putting them after lets an "illegal
/// solution" still consume allowance, **so the evaluation limit pays for calls that cannot produce a candidate**.
/// **只有全部检查通过才落入候选集合**。任何一条失败路径都返回 `null` 并保持"已拒绝"，于是上层看到的失败与真实原因一致。
/// **Only a fully valid evaluation enters the candidate set.** Every failure path returns `null` and stays "rejected", so the failure the
/// caller observes matches the real cause.
/// **两个视图有消费者，本实现没有——这是既定形状差异，不是缺口。** `FitnessEvaluator` 读 `rejected` 丢掉被拒的评估、
/// `LocalSearchUpdates` 读 `remaining &lt;= 2` 提前退出；本实现在 earlier 把约束与预算从模型上剥离（`SearchConstraints` 取代模型装饰器，
/// 预算走 `SearchBudget`），算子拿不到模型侧的这两个读数，**调用方用 `SearchBudget` 包住整次调用**（见 `LocalSearchComposites`）。
/// 补一个没人读的属性只会变成下一个人的困惑来源，所以**不补**。
/// **The code's two views have consumers; this implementation has none — an established shape difference, not a gap.** The code's `FitnessEvaluator` reads `rejected` to drop a
/// rejected evaluation, and `LocalSearchUpdates` reads `remaining &lt;= 2` to exit early; this implementation detached constraints and budgets from the model in earlier
/// (`SearchConstraints` replaced the model decorator, budgets go through `SearchBudget`), so operators cannot reach either reading and **the caller wraps the whole call in a
/// `SearchBudget`** (see `LocalSearchComposites`). Adding an unconsumed property would only become the next person's confusion, so **it stays out**.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
internal sealed class EvaluationModel<ObjValue, V>
    : IAbstractCallBackModelInterface<object?, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IAbstractCallBackModelInterface<object?, ObjValue, V> _inner;
    private readonly SearchProblem<ObjValue, V> _problem;
    private readonly SearchSlice _budget;
    private readonly SearchConstraintHandling<ObjValue, V>? _constraints;
    private readonly List<IIndividual<ObjValue, V>> _candidates = new();

    /// <summary>
    /// 构造评估模型 / Construct the evaluation model.
    /// </summary>
    /// <param name="inner">下层模型 / The delegate model.</param>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <param name="budget">片段预算 / The slice budget.</param>
    /// <param name="constraints">约束处理器 / The constraint handler.</param>
    public EvaluationModel(
        IAbstractCallBackModelInterface<object?, ObjValue, V> inner,
        SearchProblem<ObjValue, V> problem,
        SearchSlice budget,
        SearchConstraintHandling<ObjValue, V>? constraints) {
        _inner = inner;
        _problem = problem;
        _budget = budget;
        _constraints = constraints;
    }

    /// <summary>额度是否已用尽 / Whether the allowance ran out.</summary>
    public bool Exhausted { get; private set; }

    /// <summary>通过全部检查的候选 / The candidates that passed every check.</summary>
    public IReadOnlyList<IIndividual<ObjValue, V>> Candidates => _candidates;

    /// <inheritdoc/>
    public ObjValue DefaultObjective => _inner.DefaultObjective;

    /// <inheritdoc/>
    public ObjectCategory ObjectCategory => _inner.ObjectCategory;

    /// <inheritdoc/>
    public IAbstractMutableTokenTable<V> Tokens => _inner.Tokens;

    /// <inheritdoc/>
    public IReadOnlyList<(Func<IReadOnlyList<V>, bool?> Extractor, string Name)> Constraints =>
        _inner.Constraints;

    /// <inheritdoc/>
    public IReadOnlyList<(Func<IReadOnlyList<V>, object?> Extractor, string Name)> ObjectiveFunctions =>
        _inner.ObjectiveFunctions;

    /// <inheritdoc/>
    public ObjValue Operation(ObjValue lhs, ObjValue rhs) => _inner.Operation(lhs, rhs);

    /// <inheritdoc/>
    public ObjValue ObjectiveValue() => _inner.ObjectiveValue();

    /// <inheritdoc/>
    public ObjValue ObjectiveValue(object? obj) => _inner.ObjectiveValue(obj);

    /// <inheritdoc/>
    public ObjValue? Objective(IReadOnlyList<V> solution) {
        ArgumentNullException.ThrowIfNull(solution);
        if (!_budget.Acquire()) {
            Exhausted = true;
            return default;
        }
        if (!_problem.ValidSolution(solution) || !_problem.Admissible(solution)) {
            return default;
        }
        _constraints?.Observe(solution);
        if (_constraints?.Failure is not null) {
            return default;
        }
        _budget.Global.ObjectiveCalled();
        ObjValue? objective = _inner.Objective(solution);
        if (objective is null || !_problem.Valid(objective)) {
            return default;
        }
        // 适应度由目标值本身决定：这里没有独立的适应度函数，而档案的排序只看 `Objective`。
        // The fitness derives from the objective itself: there is no separate fitness function here, and the archive orders by
        // `Objective` alone.
        _candidates.Add(new SolutionWithFitness<ObjValue, V>(solution, objective, 0.0));
        return objective;
    }

    /// <inheritdoc/>
    public Order? CompareObjective(ObjValue? lhs, ObjValue? rhs) =>
        _inner.CompareObjective(lhs, rhs);

    /// <summary>
    /// 转发初始解 / Forward the initial solutions.
    /// </summary>
    /// <remarks>
    /// **这个转发不是可选优化，而是"经会话驱动的运行器能否启动"的唯一前提**。接口上的 `InitialSolutions` 有默认实现（返回空），
    /// 一个漏掉转发的包装器会让**每一个**经 `AbstractRunner.Run` 驱动的运行器都从空种群开始：
    /// 初始化拿到空集、`Advance` 原样返回空集、进程正常结束，**而结果是一个"成功的空结果"**。
 /// 这个缺陷在 earlier 的 NSGA-II 上一直存在，直到 earlier 第一次从求解器入口调用 `Run` 才暴露——
    /// **所有此前的测试都直接调 `InitializeStep`/`AdvanceStep`，恰好绕开了会话。**
    /// **This forwarding is not an optional optimisation: it is the only precondition for "a session-driven runner can start at all".**
    /// `InitialSolutions` has a default implementation on the interface (returning empty), so a wrapper that forgets to forward makes **every**
    /// runner driven through `AbstractRunner.Run` start from an empty population: initialisation gets nothing, `Advance` returns it unchanged,
    /// the process ends normally, **and the result is a "successful empty result"**.
 /// The defect lived in earlier's NSGA-II the whole time and only surfaced in earlier, when the solver entry point first called `Run` —
    /// **every earlier test called `InitializeStep`/`AdvanceStep` directly and happened to bypass the session.**
    /// </remarks>
    /// <param name="initialSolutionAmount">初始解数量 / The initial-solution amount.</param>
    /// <returns>初始解 / The initial solutions.</returns>
    public IReadOnlyList<IReadOnlyList<V>> InitialSolutions(ulong initialSolutionAmount = 1UL) =>
        _inner.InitialSolutions(initialSolutionAmount);

    /// <inheritdoc/>
    public bool? ConstraintSatisfied(IReadOnlyList<V> solution) => _inner.ConstraintSatisfied(solution);

    /// <inheritdoc/>
    public void Flush() => _inner.Flush();

    /// <inheritdoc/>
    public void Dispose() { }
}

/// <summary>
/// 运行器会话 / A runner session.
/// </summary>
/// <remarks>
/// **约束处理器是显式参数，不从模型上探测**。`constraintHandling(model)` 从模型引用找回处理器；本实现沿用
/// `ConstrainedSearch` 的形状，把处理器作为构造参数（与既有约束处理器一致，是既定的形状差异）。
/// **The constraint handler is an explicit parameter, never probed off the model.** The code recovers it from the model reference
/// via `constraintHandling(model)`; this implementation follows the `ConstrainedSearch` shape and takes it as a constructor argument (an
/// established shape difference shared with the existing constraint handler).
/// **每一步都要先保存约束状态，在失败路径上恢复**。不恢复会让一次失败的步进把 ε 与排序流推进到"这一步从未发生过"之外的状态，
/// **而后续步进在一个被污染的基础上继续，且没有任何地方记录这一点**。
/// **Every step saves the constraint state first and restores it on every failure path.** Without the restore, a failed step leaves
/// epsilon and the ranking stream advanced past "this step never happened", **and later steps continue from a contaminated basis with
/// nothing recording it**.
/// **步数上限与额度上限都为负时直接拒绝**。负数上限在循环条件里表现为"永不推进"，**而一次 `Step` 会返回一个成功的空报告**。
/// **Negative step or allowance limits are rejected outright.** A negative limit manifests in the loop condition as "never advance",
/// **and a `Step` returns a successful empty report**.
/// **恢复时拒绝计数的负值，并拒绝"快照有约束而会话没有"（或反之）**。静默接受会让一次恢复把会话带到与自己能力不匹配的状态上。
/// **A restore rejects negative counters and rejects "the snapshot carries constraints while the session has none" (or the reverse).**
/// Accepting either silently puts the session into a state its own capabilities do not match.
/// **恢复时先解码档案再解码状态**。反过来的话，一次档案解码失败会留下一份已被替换的运行状态，
/// **而调用方以为恢复"没有生效"**。
/// **A restore decodes the archive before the state.** Reversed, an archive decode failure leaves an already-replaced run state behind,
/// **while the caller believes the restore "did not take effect"**.
/// </remarks>
/// <typeparam name="TState">运行状态类型 / The run-state type.</typeparam>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class RunnerSearchSession<TState, ObjValue, V> : ISearchSession<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly AbstractRunner<TState, ObjValue, V> _runner;
    private readonly SearchProblem<ObjValue, V> _problem;
    private readonly ISearchCodec<TState>? _codec;
    private readonly Func<TState, IReadOnlyList<IIndividual<ObjValue, V>>,
        Result<TState, ErrorCode, Error<ErrorCode>>>? _importer;
    private readonly SearchConstraintHandling<ObjValue, V>? _constraints;
    private readonly ISearchCodec<List<IIndividual<ObjValue, V>>> _individuals;

    private TState? _state;
    private bool _initialized;

    /// <summary>
    /// 构造会话 / Construct the session.
    /// </summary>
    /// <param name="runner">运行器 / The runner.</param>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    /// <param name="codec">状态编解码器 / The state codec.</param>
    /// <param name="importer">导入器 / The importer.</param>
    /// <param name="constraints">约束处理器 / The constraint handler.</param>
    /// <param name="iteration">起始迭代 / The starting iteration.</param>
    public RunnerSearchSession(
        AbstractRunner<TState, ObjValue, V> runner,
        SearchProblem<ObjValue, V> problem,
        ISearchCodec<TState>? codec,
        Func<TState, IReadOnlyList<IIndividual<ObjValue, V>>,
            Result<TState, ErrorCode, Error<ErrorCode>>>? importer = null,
        SearchConstraintHandling<ObjValue, V>? constraints = null,
        Iteration? iteration = null) {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(problem);
        _runner = runner;
        _problem = problem;
        _codec = codec;
        _importer = importer;
        _constraints = constraints;
        _individuals = SearchCodecs.List(SearchCodecs.Individual(problem));
        Iteration = iteration ?? new Iteration();
        Iteration.Pause();
    }

    /// <summary>迭代计数器 / The iteration counter.</summary>
    public Iteration Iteration { get; }

    /// <summary>档案个体 / The archive individuals.</summary>
    public IReadOnlyList<IIndividual<ObjValue, V>> Archive => _runner.Archive.Individuals;

    /// <summary>是否已初始化 / Whether the session is initialized.</summary>
    public bool Initialized => _initialized;

    /// <inheritdoc/>
    public bool SupportsImport => _importer is not null;

    /// <inheritdoc/>
    public async Task<Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> Step(
        SearchSlice budget) {
        ArgumentNullException.ThrowIfNull(budget);
        if (budget.Steps < 0L || budget.Evaluations < 0L) {
            return SearchCodecSupport.SearchInvalid<SearchStepResult<ObjValue, V>>("negative slice");
        }

        var evaluated = new EvaluationModel<ObjValue, V>(
            _problem.Model, _problem, budget, _constraints);
        long completed = 0L;
        SearchTermination reason = SearchTermination.SliceComplete;
        Iteration.Resume();
        try {
            while (completed < budget.Steps && !budget.Expired) {
                ConstraintSearchState? saved = _constraints?.Save();
                _constraints?.Begin();

                TState result;
                try {
                    // **必须把计费模型传给运行器**，而不是原始模型。传原始模型会让额度从不被消耗、候选集合恒为空，
                    // 而每一次 `Step` 都"成功"——**报告里的评估数恒为 0，却看不出任何异常**。
                    // **The accounting model must be handed to the runner**, not the raw model. Passing the raw model leaves the
                    // allowance untouched and the candidate set empty, while every `Step` "succeeds" — **the report always shows zero
                    // evaluations and nothing looks wrong**.
                    result = _initialized
                        ? await _runner.AdvanceStep(evaluated, Iteration, _state!).ConfigureAwait(false)
                        : await _runner.InitializeStep(evaluated, Iteration).ConfigureAwait(false);
                } catch (Exception exception) {
                    RestoreConstraints(saved);
                    return Results.Ok(new SearchStepResult<ObjValue, V>(
                        new List<IIndividual<ObjValue, V>>(evaluated.Candidates),
                        budget.Used, completed, SearchTermination.Failed,
                        new[] { exception.Message }));
                }

                if (_constraints?.Failure is string constraintFailure) {
                    RestoreConstraints(saved);
                    return Results.Ok(new SearchStepResult<ObjValue, V>(
                        new List<IIndividual<ObjValue, V>>(evaluated.Candidates),
                        budget.Used, completed, SearchTermination.Failed,
                        new[] { constraintFailure }));
                }

                if (evaluated.Exhausted) {
                    RestoreConstraints(saved);
                    reason = budget.Expired
                        ? SearchTermination.TimeLimit
                        : SearchTermination.EvaluationLimit;
                    break;
                }

                IReadOnlyList<IIndividual<ObjValue, V>> members = _runner.StateMembers(result);
                Observe(members);
                if (_constraints?.Failure is string observeFailure) {
                    RestoreConstraints(saved);
                    return Results.Ok(new SearchStepResult<ObjValue, V>(
                        new List<IIndividual<ObjValue, V>>(evaluated.Candidates),
                        budget.Used, completed, SearchTermination.Failed,
                        new[] { observeFailure }));
                }

                bool improved = _runner.Archive.Update(_problem.Model, members);
                _state = result;
                _initialized = true;
                _constraints?.Commit();
                foreach (IIterationObserverContract observer in _runner.Observers) {
                    observer.Observe(Iteration, members, improved, _runner.Converter);
                }
                Iteration.Next(improved);
                _problem.Model.Flush();
                completed++;
                if (members.Count == 0) {
                    reason = SearchTermination.NoCandidate;
                    break;
                }
            }
            if (budget.Expired) {
                reason = SearchTermination.TimeLimit;
            }
            return Results.Ok(new SearchStepResult<ObjValue, V>(
                new List<IIndividual<ObjValue, V>>(evaluated.Candidates),
                budget.Used, completed, reason));
        } finally {
            Iteration.Pause();
        }
    }

    /// <inheritdoc/>
    public Task<Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> ImportSolutions(
        IReadOnlyList<Solution<V>> solutions, SearchSlice budget) {
        ArgumentNullException.ThrowIfNull(solutions);
        ArgumentNullException.ThrowIfNull(budget);
        if (!_initialized) {
            return Task.FromResult(
                SearchCodecSupport.SearchInvalid<SearchStepResult<ObjValue, V>>(
                    "initialize before importing"));
        }
        if (_importer is null) {
            return Task.FromResult(
                SearchCodecSupport.SearchInvalid<SearchStepResult<ObjValue, V>>(
                    "session does not support imports"));
        }

        ConstraintSearchState? saved = _constraints?.Save();
        _constraints?.Begin();
        var evaluated = new EvaluationModel<ObjValue, V>(
            _problem.Model, _problem, budget, _constraints);
        foreach (Solution<V> solution in solutions) {
            try {
                _ = evaluated.Objective(solution.Values);
            } catch (Exception exception) {
                RestoreConstraints(saved);
                return Task.FromResult(Results.Failed<SearchStepResult<ObjValue, V>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, exception.Message)));
            }
            if (evaluated.Exhausted) {
                break;
            }
        }

        Result<TState, ErrorCode, Error<ErrorCode>> imported;
        try {
            imported = _importer(_state!, evaluated.Candidates);
        } catch (Exception exception) {
            RestoreConstraints(saved);
            return Task.FromResult(Results.Failed<SearchStepResult<ObjValue, V>>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, exception.Message)));
        }

        Observe(evaluated.Candidates);
        if (imported is Failed<TState, ErrorCode, Error<ErrorCode>> importFailed) {
            RestoreConstraints(saved);
            return Task.FromResult(Results.Failed<SearchStepResult<ObjValue, V>>(importFailed.Error));
        }
        if (_constraints?.Failure is string constraintFailure) {
            RestoreConstraints(saved);
            return Task.FromResult(
                SearchCodecSupport.SearchInvalid<SearchStepResult<ObjValue, V>>(
                    "导入约束评估失败 / Import constraint assessment failed: " + constraintFailure));
        }

        _runner.Archive.Update(_problem.Model, evaluated.Candidates);
        _state = ((Ok<TState, ErrorCode, Error<ErrorCode>>)imported).Value;
        _initialized = true;
        SearchTermination reason = evaluated.Exhausted
            ? (budget.Expired ? SearchTermination.TimeLimit : SearchTermination.EvaluationLimit)
            : SearchTermination.SliceComplete;
        return Task.FromResult(Results.Ok(new SearchStepResult<ObjValue, V>(
            new List<IIndividual<ObjValue, V>>(evaluated.Candidates),
            budget.Used, 0L, reason)));
    }

    /// <inheritdoc/>
    public Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> Snapshot() {
        ISearchCodec<TState>? codec = _codec;
        if (codec is null) {
            return SearchCodecSupport.SearchInvalid<SearchSessionSnapshot>(
                "missing native state codec");
        }

        JsonElement encoded;
        if (_initialized) {
            Result<JsonElement, ErrorCode, Error<ErrorCode>> encodeResult = codec.Encode(_state!);
            if (encodeResult is Failed<JsonElement, ErrorCode, Error<ErrorCode>> encodeFailed) {
                return Results.Failed<SearchSessionSnapshot>(encodeFailed.Error);
            }
            encoded = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encodeResult).Value;
        } else {
            encoded = NullElement();
        }

        Result<JsonElement, ErrorCode, Error<ErrorCode>> archiveResult =
            _individuals.Encode(new List<IIndividual<ObjValue, V>>(_runner.Archive.Individuals));
        if (archiveResult is Failed<JsonElement, ErrorCode, Error<ErrorCode>> archiveFailed) {
            return Results.Failed<SearchSessionSnapshot>(archiveFailed.Error);
        }
        JsonElement archive = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)archiveResult).Value;

        JsonElement? constraints = null;
        if (_constraints is not null) {
            Result<JsonElement, ErrorCode, Error<ErrorCode>> saved =
                SearchCodecs.Serialized<ConstraintSearchState>().Encode(_constraints.Save());
            if (saved is Failed<JsonElement, ErrorCode, Error<ErrorCode>> constraintFailed) {
                return Results.Failed<SearchSessionSnapshot>(constraintFailed.Error);
            }
            constraints = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)saved).Value;
        }

        return Results.Ok(new SearchSessionSnapshot(
            _initialized,
            Iteration.Current,
            Iteration.NotBetterIteration,
            encoded,
            archive,
            Version: 1,
            ActiveNanos: Iteration.Time.Ticks * 100L,
            Constraints: constraints));
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Restore(SearchSessionSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ISearchCodec<TState> codec = _codec
            ?? throw new InvalidOperationException("missing native state codec");
        if (snapshot.Version != 1
            || snapshot.Iteration < 0L
            || snapshot.Stagnation < 0L
            || snapshot.ActiveNanos < 0L) {
            return SearchCodecSupport.SearchInvalid<Success>("session version/counters");
        }
        if (snapshot.Constraints.HasValue != (_constraints is not null)) {
            return SearchCodecSupport.SearchInvalid<Success>(
                "约束恢复能力不匹配 / Constraint recovery capabilities mismatch");
        }
        if (_constraints is not null) {
            Result<ConstraintSearchState, ErrorCode, Error<ErrorCode>> saved =
                SearchCodecs.Serialized<ConstraintSearchState>().Decode(snapshot.Constraints!.Value);
            if (saved is Failed<ConstraintSearchState, ErrorCode, Error<ErrorCode>> savedFailed) {
                return Results.Failed<Success>(savedFailed.Error);
            }
            Result<Success, ErrorCode, Error<ErrorCode>> loaded = _constraints.Load(
                ((Ok<ConstraintSearchState, ErrorCode, Error<ErrorCode>>)saved).Value);
            if (loaded is Failed<Success, ErrorCode, Error<ErrorCode>> loadFailed) {
                return loadFailed;
            }
        }

        Result<List<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>> archiveResult =
            _individuals.Decode(snapshot.Archive);
        if (archiveResult is Failed<List<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>>
            archiveFailed) {
            return Results.Failed<Success>(archiveFailed.Error);
        }
        List<IIndividual<ObjValue, V>> entries =
            ((Ok<List<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>>)archiveResult).Value;

        if (snapshot.Initialized) {
            Result<TState, ErrorCode, Error<ErrorCode>> decoded = codec.Decode(snapshot.State);
            if (decoded is Failed<TState, ErrorCode, Error<ErrorCode>> decodeFailed) {
                return Results.Failed<Success>(decodeFailed.Error);
            }
            _state = ((Ok<TState, ErrorCode, Error<ErrorCode>>)decoded).Value;
            _initialized = true;
        } else {
            _state = default;
            _initialized = false;
        }

        Result<Success, ErrorCode, Error<ErrorCode>> restored = Iteration.Restore(
            (int)snapshot.Iteration,
            (int)snapshot.Stagnation,
            TimeSpan.FromTicks(snapshot.ActiveNanos / 100L));
        if (restored is Failed<Success, ErrorCode, Error<ErrorCode>> restoreFailed) {
            return restoreFailed;
        }
        Iteration.Pause();

        _runner.Archive.Clear();
        _runner.Archive.Update(_problem.Model, entries);
        return Results.Ok(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public void Close() => Iteration.Pause();

    /// <summary>
    /// 观察成员与档案 / Observe the members and the archive.
    /// </summary>
    /// <param name="members">成员 / The members.</param>
    private void Observe(IReadOnlyList<IIndividual<ObjValue, V>> members) {
        if (_constraints is null) {
            return;
        }
        foreach (IIndividual<ObjValue, V> member in members) {
            _constraints.Observe(member.Solution);
        }
        foreach (IIndividual<ObjValue, V> archived in _runner.Archive.Individuals) {
            _constraints.Observe(archived.Solution);
        }
    }

    /// <summary>
    /// 一个真实的 JSON `null` / A real JSON `null`.
    /// </summary>
    /// <remarks>
    /// **不能用 `default(JsonElement)`**：它的 `ValueKind` 是 `Undefined`，序列化时会抛 `InvalidOperationException`。
    /// **`default(JsonElement)` must not be used**: its `ValueKind` is `Undefined` and serializing it throws
    /// `InvalidOperationException`.
    /// </remarks>
    /// <returns>JSON `null` 元素 / A JSON `null` element.</returns>
    private static JsonElement NullElement() =>
        JsonDocument.Parse("null").RootElement.Clone();

    /// <summary>
    /// 恢复约束状态 / Restore the constraint state.
    /// </summary>
    /// <param name="saved">已保存状态 / The saved state.</param>
    private void RestoreConstraints(ConstraintSearchState? saved) {
        if (_constraints is not null && saved is not null) {
            _constraints.Load(saved);
        }
    }
}
