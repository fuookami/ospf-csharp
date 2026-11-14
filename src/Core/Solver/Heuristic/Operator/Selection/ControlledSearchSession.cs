// 受控搜索会话 / A controlled search session.
// 与固定 Kotlin 基线 `selection/OperatorBindings.kt`（19823 B）的 `ControlledSearchSession` 与 `ControlledSnapshot` 对应。
// Mirrors `ControlledSearchSession` and `ControlledSnapshot` from the pinned baseline's `OperatorBindings.kt` (19823 B).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 受控快照载荷 / The controlled-snapshot payload.
/// </summary>
/// <param name="Session">下层会话快照 / The delegate session snapshot.</param>
/// <param name="Slots">各算子池槽位 / The per-pool slots.</param>
/// <param name="LocalArchive">本地档案 / The local archive.</param>
/// <param name="Initialized">是否已初始化 / Whether it is initialized.</param>
/// <param name="Decisions">决策数 / The decision count.</param>
/// <param name="Stagnation">停滞计数 / The stagnation count.</param>
/// <param name="RewardState">奖励状态 / The reward state.</param>
/// <param name="RewardStateVersion">奖励状态版本 / The reward-state version.</param>
public sealed record ControlledSnapshot(
    SearchSessionSnapshot Session,
    IReadOnlyDictionary<string, SlotSnapshot> Slots,
    JsonElement LocalArchive,
    bool Initialized,
    long Decisions,
    long Stagnation,
    JsonElement RewardState,
    int RewardStateVersion = 0);

/// <summary>
/// 受控搜索会话 / A controlled search session.
/// </summary>
/// <remarks>
/// **`initialized` 在第一次"低层真正走了步"之后才置位**。第一次决策时算子池还没被选中过任何算子，此时 `choose` 会凭空产生
/// 一条决策，而 `usedAction` 仍是 `null`——**反馈一条从未执行的决策会让策略从空执行里学习**。
/// **`initialized` flips only after the low-level session has actually taken a step.** On the first decision no operator has been
/// chosen yet; calling `choose` there fabricates a decision while `usedAction` stays `null` — **feeding back a decision that never
/// executed teaches the policy from nothing**.
/// **只对"用过"的池记录反馈，未用过的池一律 `discard`**。对未用过的池调 `observe` 会返回成功但什么也不记，看起来像正常反馈。
/// **Feedback is recorded only for pools that were used; every other pool is `discard`ed.** Calling `observe` on an unused pool returns
/// success while recording nothing, which looks like normal feedback.
/// **奖励无效时记为 0 并写诊断，而不是中止**。中止会让一次奖励函数的数值意外毁掉整个搜索；记 0 之后策略仍能继续，
/// 诊断里留下了"这次奖励是假的"的证据。
/// **An invalid reward is recorded as 0 with a diagnostic rather than aborting.** Aborting lets one numeric accident in the reward
/// function destroy the whole search; recording 0 keeps the policy going while the diagnostic preserves the evidence that this reward
/// was fake.
/// **`terminal` 与 `failed` 是"本次低层未完整走完"，不是全局终止**。把它们写成全局状态会让一次切片边界被当成算法终止，
/// 之后所有信用都被记成终局，**而搜索本身还在继续**。
/// **`terminal` and `failed` mean "this low-level step did not complete", not global termination.** Writing them as global state makes a
/// slice boundary look like algorithm termination, after which every credit is recorded as terminal **while the search itself continues**.
/// **`restore` 校验槽位集合与当前绑定完全一致**。少一个池会让它保留旧状态而搜索继续，多一个池会让它的状态无处可去；
/// 两种情况下都会返回成功。
/// **`restore` requires the slot set to match the current bindings exactly.** A missing pool keeps its old state while the search
/// continues, and an extra pool has nowhere to put its state; both would otherwise return success.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class ControlledSearchSession<ObjValue, V> : ISearchSession<ObjValue, V>, IPolicyCarrier
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly ISearchSession<ObjValue, V> _delegate;
    private readonly OperatorBindings _bindings;
    private readonly IRewardPolicy _reward;
    private readonly SearchMemory<ObjValue, V> _memory;
    private bool _initialized;
    private long _decisions;
    private long _stagnation;

    /// <summary>
    /// 构造会话 / Construct the session.
    /// </summary>
    /// <param name="session">下层会话 / The delegate session.</param>
    /// <param name="bindings">算子绑定 / The operator bindings.</param>
    /// <param name="reward">奖励策略 / The reward policy.</param>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <param name="coordinates">目标坐标投影 / The objective-coordinate projection.</param>
    /// <param name="multiObjective">是否多目标 / Whether it is multi-objective.</param>
    /// <param name="admissible">个体是否合法可行 / Whether an individual is valid and feasible.</param>
    public ControlledSearchSession(
        ISearchSession<ObjValue, V> session,
        OperatorBindings bindings,
        IRewardPolicy reward,
        IDominance dominance,
        Func<ObjValue, IReadOnlyList<Flt64>> coordinates,
        bool multiObjective = false,
        Func<IIndividual<ObjValue, V>, bool>? admissible = null) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(reward);
        ArgumentNullException.ThrowIfNull(dominance);
        ArgumentNullException.ThrowIfNull(coordinates);
        _delegate = session;
        _bindings = bindings;
        _reward = reward;
        Func<IIndividual<ObjValue, V>, bool> accept = admissible ?? (_ => true);
        _memory = new SearchMemory<ObjValue, V>(
            100, 20,
            new NotWorseAcceptancePolicy<ObjValue, V>(dominance, coordinates),
            multiObjective, dominance, coordinates, accept);
    }

    /// <inheritdoc/>
    public bool SupportsImport => _delegate.SupportsImport;

    /// <summary>决策数 / The decision count.</summary>
    public long Decisions => _decisions;

    /// <summary>停滞计数 / The stagnation count.</summary>
    public long Stagnation => _stagnation;

    /// <summary>是否已初始化 / Whether it is initialized.</summary>
    public bool Initialized => _initialized;

    /// <summary>本地档案 / The local archive.</summary>
    public IReadOnlyList<IIndividual<ObjValue, V>> Archive => _memory.Archive;

    /// <inheritdoc/>
    public async Task<Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> Step(
        SearchSlice budget) {
        ArgumentNullException.ThrowIfNull(budget);
        var candidates = new List<IIndividual<ObjValue, V>>();
        var diagnostics = new List<string>();
        long evaluations = 0L;
        long steps = 0L;
        SearchTermination reason = SearchTermination.SliceComplete;
        long iterations = global::System.Math.Min(budget.Steps, int.MaxValue);

        for (long index = 0; index < iterations; index++) {
            SelectionContext before = ProgressFeatures.Instance.Extract(new SearchProgress(
                _decisions, 0L, long.MaxValue, _stagnation, 0.0));

            if (_initialized) {
                foreach (IOperatorPool pool in _bindings.Pools()) {
                    Result<Success, ErrorCode, Error<ErrorCode>> selected = pool.Choose(before);
                    if (selected is Failed<Success, ErrorCode, Error<ErrorCode>> chooseFailed) {
                        foreach (IOperatorPool other in _bindings.Pools()) {
                            other.Discard();
                        }
                        return Results.Failed<SearchStepResult<ObjValue, V>>(chooseFailed.Error);
                    }
                }
            }

            IReadOnlyList<IIndividual<ObjValue, V>> reference = _memory.Archive;
            SearchSlice slice = budget.Child(budget.Evaluations - evaluations, 1L);
            long started = Stopwatch.GetTimestamp();
            Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>> outcome =
                await StepBoundary(slice).ConfigureAwait(false);
            long elapsedNanos = ElapsedNanos(started);

            if (outcome is Failed<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>> failed) {
                if (_initialized) {
                    foreach (IOperatorPool pool in _bindings.Pools()) {
                        if (pool.UsedAction is null) {
                            pool.Discard();
                            continue;
                        }
                        Result<Success, ErrorCode, Error<ErrorCode>> observed = pool.Observe(
                            before, Flt64.Zero, slice.Used, true, elapsedNanos, 0L, failed: true);
                        if (observed is Failed<Success, ErrorCode, Error<ErrorCode>> observeFailed) {
                            diagnostics.Add(
                                $"失败算子反馈失败 / Failed-operator feedback failed: {observeFailed.Error.Message}");
                        }
                    }
                }
                diagnostics.Add($"低层执行失败 / Low-level execution failed: {failed.Error.Message}");
                return Results.Ok(new SearchStepResult<ObjValue, V>(
                    candidates, evaluations + slice.Used, steps, SearchTermination.Failed, diagnostics));
            }

            SearchStepResult<ObjValue, V> result =
                ((Ok<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>)outcome).Value;
            (bool improved, double ratio) = _memory.Contribution(reference, result.Candidates);
            RewardContext creditContext = RewardContexts.Build(
                reference, result.Candidates, (improved, ratio), result.Evaluations, elapsedNanos,
                "operator", null, _ => true);
            _stagnation = improved ? 0L : _stagnation + 1;
            SelectionContext after = ProgressFeatures.Instance.Extract(new SearchProgress(
                _decisions + 1, 0L, long.MaxValue, _stagnation, 0.0));

            if (_initialized) {
                foreach (IOperatorPool pool in _bindings.Pools()) {
                    string? action = pool.UsedAction;
                    if (action is null) {
                        pool.Discard();
                        continue;
                    }
                    string scoped = $"{pool.Id}/{action}";
                    Result<Flt64, ErrorCode, Error<ErrorCode>> rewarded =
                        _reward.Evaluate(creditContext with {
                            Action = scoped,
                            // **奖励上下文必须带上目标坐标**：省略会让所有依赖坐标的奖励策略退化成"只用贡献度"，
                            // 而返回值仍然是一个合理的小数。
                            // **The reward context must carry the objective coordinates**: omitting them degrades every
                            // coordinate-dependent policy into "contribution only" while still returning a plausible number.
                            Reference = creditContext.Reference,
                            Candidates = creditContext.Candidates,
                        });
                    bool validReward = rewarded is Ok<Flt64, ErrorCode, Error<ErrorCode>> ok
                        && double.IsFinite(ok.Value.ToDouble());
                    if (!validReward) {
                        diagnostics.Add(
                            $"算子奖励无效 / Invalid operator reward: {scoped}");
                    }
                    Flt64 value = validReward
                        ? ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)rewarded).Value
                        : Flt64.Zero;
                    Result<Success, ErrorCode, Error<ErrorCode>> observed = pool.Observe(
                        after, value, result.Evaluations,
                        !validReward || result.Termination != SearchTermination.SliceComplete,
                        elapsedNanos, result.Steps,
                        failed: !validReward || result.Termination == SearchTermination.Failed);
                    if (observed is Failed<Success, ErrorCode, Error<ErrorCode>> observeFailed) {
                        diagnostics.Add(
                            $"算子反馈失败 / Operator feedback failed: {observeFailed.Error.Message}");
                    }
                }
            }

            _memory.Offer(result.Candidates);
            _initialized = _initialized || result.Steps > 0;
            _decisions++;
            candidates.AddRange(result.Candidates);
            evaluations += result.Evaluations;
            steps += result.Steps;
            reason = diagnostics.Count == 0 ? result.Termination : SearchTermination.Failed;
            foreach (string line in result.Diagnostics) {
                diagnostics.Add(line);
            }
            if (reason != SearchTermination.SliceComplete
                || evaluations >= budget.Evaluations || budget.Expired) {
                return Results.Ok(new SearchStepResult<ObjValue, V>(
                    candidates, evaluations, steps, reason, new List<string>(diagnostics)));
            }
        }

        return Results.Ok(new SearchStepResult<ObjValue, V>(
            candidates, evaluations, steps, reason, new List<string>(diagnostics)));
    }

    /// <inheritdoc/>
    public Task<Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> ImportSolutions(
        IReadOnlyList<Solution<V>> solutions, SearchSlice budget) =>
        _delegate.ImportSolutions(solutions, budget);

    /// <inheritdoc/>
    public Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> Snapshot() {
        var slots = new Dictionary<string, SlotSnapshot>(StringComparer.Ordinal);
        foreach (IOperatorPool pool in _bindings.Pools()) {
            Result<SlotSnapshot, ErrorCode, Error<ErrorCode>> saved = pool.Save();
            if (saved is Failed<SlotSnapshot, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<SearchSessionSnapshot>(failed.Error);
            }
            slots[pool.Id] = ((Ok<SlotSnapshot, ErrorCode, Error<ErrorCode>>)saved).Value;
        }

        // **必须用真正的 JSON `null` 而不是 `default(JsonElement)`**：后者的 `ValueKind` 是 `Undefined`，
        // 序列化会直接抛出异常，整个快照随之失败。
        // **A real JSON `null` is required, not `default(JsonElement)`**: the latter has `ValueKind == Undefined` and serialization
        // throws outright, failing the whole snapshot.
        JsonElement rewardState = JsonDocument.Parse("null").RootElement.Clone();
        int rewardVersion = 0;
        if (_reward is IRecoverableComponent component) {
            Result<JsonElement, ErrorCode, Error<ErrorCode>> saved = component.Save();
            if (saved is Failed<JsonElement, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<SearchSessionSnapshot>(failed.Error);
            }
            rewardState = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)saved).Value;
            rewardVersion = 1;
        }

        Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> native = _delegate.Snapshot();
        if (native is Failed<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> nativeFailed) {
            return Results.Failed<SearchSessionSnapshot>(nativeFailed.Error);
        }
        SearchSessionSnapshot baseline = ((Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>)native).Value;
        // **`LocalArchive` 同理必须是真正的 JSON `null`**：`default(JsonElement)` 会让 `JsonElementConverter.Write` 抛异常，
        // 而异常发生在 `Snapshot()` 内部、调用方只看到一个未处理异常。
        // **`LocalArchive` likewise must be a real JSON `null`**: `default(JsonElement)` makes `JsonElementConverter.Write` throw, and the
        // exception happens inside `Snapshot()` so the caller only sees an unhandled exception.
        var payload = new ControlledSnapshot(
            baseline, slots, JsonDocument.Parse("null").RootElement.Clone(),
            _initialized, _decisions, _stagnation, rewardState, rewardVersion);
        return Results.Ok(baseline with { State = JsonSerializer.SerializeToElement(payload) });
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Restore(SearchSessionSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        Result<ControlledSnapshot, ErrorCode, Error<ErrorCode>> decoded = SearchBoundary.Execute(
            () => JsonSerializer.Deserialize<ControlledSnapshot>(snapshot.State.GetRawText())!);
        if (decoded is Failed<ControlledSnapshot, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        ControlledSnapshot saved = ((Ok<ControlledSnapshot, ErrorCode, Error<ErrorCode>>)decoded).Value;
        if (saved.RewardStateVersion is < 0 or > 1) {
            return SearchCodecSupport.SearchInvalid<Success>(
                "不支持或冲突的算子奖励状态 / Unsupported or conflicting operator reward state");
        }
        if (saved.RewardStateVersion != 0 && saved.RewardState.ValueKind == JsonValueKind.Null) {
            return SearchCodecSupport.SearchInvalid<Success>(
                "不支持或冲突的算子奖励状态 / Unsupported or conflicting operator reward state");
        }

        if (_reward is IRecoverableComponent component) {
            Result<Success, ErrorCode, Error<ErrorCode>> loaded = component.Load(saved.RewardState);
            if (loaded is Failed<Success, ErrorCode, Error<ErrorCode>> rewardFailed) {
                return Results.Failed<Success>(rewardFailed.Error);
            }
        } else if (saved.RewardState.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) {
            return SearchCodecSupport.SearchInvalid<Success>(
                "unexpected reward state / unexpected reward state");
        }

        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (IOperatorPool pool in _bindings.Pools()) {
            expected.Add(pool.Id);
        }
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in saved.Slots.Keys) {
            actual.Add(id);
        }
        if (!expected.SetEquals(actual)) {
            return SearchCodecSupport.SearchInvalid<Success>("operator slots changed");
        }
        foreach (IOperatorPool pool in _bindings.Pools()) {
            Result<Success, ErrorCode, Error<ErrorCode>> restored = pool.Load(saved.Slots[pool.Id]);
            if (restored is Failed<Success, ErrorCode, Error<ErrorCode>> slotFailed) {
                return Results.Failed<Success>(slotFailed.Error);
            }
        }

        Result<Success, ErrorCode, Error<ErrorCode>> sessionRestore = _delegate.Restore(saved.Session);
        if (sessionRestore is Failed<Success, ErrorCode, Error<ErrorCode>> sessionFailed) {
            return Results.Failed<Success>(sessionFailed.Error);
        }
        _memory.Restore(Array.Empty<IIndividual<ObjValue, V>>(), Array.Empty<IIndividual<ObjValue, V>>());
        _initialized = saved.Initialized;
        _decisions = saved.Decisions;
        _stagnation = saved.Stagnation;
        return Results.Ok(new Success());
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Close() => _delegate.Close();

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, PolicySnapshot> Policies() {
        var policies = new Dictionary<string, PolicySnapshot>(StringComparer.Ordinal);
        foreach (IOperatorPool pool in _bindings.Pools()) {
            Result<SlotSnapshot, ErrorCode, Error<ErrorCode>> saved = pool.Save();
            if (saved is Ok<SlotSnapshot, ErrorCode, Error<ErrorCode>> ok) {
                policies[pool.Id] = ok.Value.Policy;
            }
        }
        return policies;
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> RestorePolicies(
        IReadOnlyDictionary<string, PolicySnapshot> policies) {
        ArgumentNullException.ThrowIfNull(policies);
        foreach (IOperatorPool pool in _bindings.Pools()) {
            if (!policies.TryGetValue(pool.Id, out PolicySnapshot? snapshot)) {
                continue;
            }
            Result<SlotSnapshot, ErrorCode, Error<ErrorCode>> saved = pool.Save();
            if (saved is Failed<SlotSnapshot, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<Success>(failed.Error);
            }
            SlotSnapshot slot = ((Ok<SlotSnapshot, ErrorCode, Error<ErrorCode>>)saved).Value;
            Result<Success, ErrorCode, Error<ErrorCode>> restored =
                pool.Load(slot with { Policy = snapshot });
            if (restored is Failed<Success, ErrorCode, Error<ErrorCode>> loadFailed) {
                return Results.Failed<Success>(loadFailed.Error);
            }
        }
        return Results.Ok(new Success());
    }

    /// <inheritdoc/>
    public IReadOnlyList<SelectionTransition> DrainTransitions() {
        var transitions = new List<SelectionTransition>();
        foreach (IOperatorPool pool in _bindings.Pools()) {
            transitions.AddRange(pool.DrainTransitions());
        }
        return transitions;
    }

    /// <summary>
    /// 在外部边界执行一次低层步进 / Execute one low-level step across the external boundary.
    /// </summary>
    /// <remarks>
    /// **低层会话在边界之外**：它的异常必须被转成失败结果，而不是冒泡越过整个搜索栈。
    /// **The low-level session lives outside the boundary**: its exceptions must become failed results rather than bubbling past the whole
    /// search stack.
    /// </remarks>
    /// <param name="slice">片段预算 / The slice budget.</param>
    /// <returns>步进结果 / The step result.</returns>
    private async Task<Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> StepBoundary(
        SearchSlice slice) {
        try {
            return await _delegate.Step(slice).ConfigureAwait(false);
        } catch (Exception exception) {
            return Results.Failed<SearchStepResult<ObjValue, V>>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                $"搜索边界失败 / Search boundary failed: {exception.Message}"));
        }
    }

    /// <summary>
    /// 把起止时间戳换算为纳秒 / Convert start and end timestamps into nanoseconds.
    /// </summary>
    /// <param name="started">起始时间戳 / The start timestamp.</param>
    /// <returns>纳秒数 / The nanosecond count.</returns>
    private static long ElapsedNanos(long started) {
        long ticks = Stopwatch.GetTimestamp() - started;
        double nanos = ticks * (1_000_000_000.0 / Stopwatch.Frequency);
        // **负耗时被夹到 0**：时钟回退会让奖励函数收到负耗时并返回 `NaN`，进而丢掉一条本可用的信用。
        // **A negative elapsed time is clamped to 0**: a clock regression would hand the reward function a negative duration, which
        // returns `NaN` and discards an otherwise usable credit.
        return nanos < 0.0 ? 0L : (long)nanos;
    }
}
