// 算子池与绑定 / Operator pools and bindings.
// 与固定 Kotlin 基线 `selection/OperatorBindings.kt`（19823 B）的 `RecoverableComponent`、`OperatorPool`、`SlotSnapshot`、
// `OperatorBindings` 四部分对应；同文件的 `ControlledSearchSession` 依赖尚未移植的 `SearchMemory`/`RewardPolicy`/
// `ProgressFeatures`（属于 `memory/` 与 `learning/` 族），留待那些族完成后单独成批。
// Mirrors `RecoverableComponent`, `OperatorPool`, `SlotSnapshot` and `OperatorBindings` from the pinned baseline's
// `OperatorBindings.kt` (19823 B); the same file's `ControlledSearchSession` depends on the not-yet-ported `SearchMemory`,
// `RewardPolicy` and `ProgressFeatures` (from the `memory/` and `learning/` families) and will be handled in its own batch.

using System;
using System.Collections.Generic;
using System.Text.Json;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Mutation;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Rl;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 可保存/恢复的组件 / A component that can be saved and restored.
/// </summary>
/// <remarks>
/// **有状态算子必须显式声明自己是可恢复的**。靠"运行时探测是否实现了接口"来决定存什么，会让新增一个无状态算子时快照结构
/// 悄悄变化，而旧快照仍然能加载、只是少了那个算子的状态。
/// **A stateful operator must explicitly declare that it is recoverable.** Deciding what to store by probing the interface at runtime
/// makes the snapshot shape change silently when a stateless operator is added, while old snapshots still load — just without that
/// operator's state.
/// </remarks>
public interface IRecoverableComponent {
    /// <summary>
    /// 保存状态 / Save the state.
    /// </summary>
    /// <returns>状态载荷 / The state payload.</returns>
    Result<JsonElement, ErrorCode, Error<ErrorCode>> Save();

    /// <summary>
    /// 加载状态 / Load the state.
    /// </summary>
    /// <param name="state">状态载荷 / The state payload.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> Load(JsonElement state);
}

/// <summary>
/// 算子池槽位快照 / A snapshot of one operator pool slot.
/// </summary>
/// <param name="Policy">策略快照 / The policy snapshot.</param>
/// <param name="Random">随机流状态 / The random-stream state.</param>
/// <param name="Components">各组件状态 / The per-component states.</param>
public sealed record SlotSnapshot(
    PolicySnapshot Policy,
    long Random,
    IReadOnlyDictionary<string, JsonElement> Components);

/// <summary>
/// 算子池的非泛型视图 / A non-generic view of an operator pool.
/// </summary>
/// <remarks>
/// **必须是同一批池对象的视图，不能每次调用都新建包装**。`choose` 之后的状态（`usedAction`）保存在池实例上；
/// 若每次取 `Pools()` 都新建包装，`choose` 与随后 `observe` 作用的会是不同对象，**决策被静默丢弃而每一步都返回成功**。
/// **It must be a view over the same pool objects, never a fresh wrapper per call.** The post-`choose` state (`usedAction`) lives on
/// the pool instance; building a new wrapper each time `Pools()` is called makes `choose` and the following `observe` act on different
/// objects, **silently dropping the decision while every step returns success**.
/// </remarks>
public interface IOperatorPool {
    /// <summary>池标识 / The pool identity.</summary>
    string Id { get; }

    /// <summary>已使用过的动作；未使用时为 `null` / The used action, or `null` when unused.</summary>
    string? UsedAction { get; }

    /// <summary>选择一个算子 / Select an operator.</summary>
    /// <param name="context">选择上下文 / The selection context.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> Choose(SelectionContext context);

    /// <summary>撤销当前决策 / Discard the current decision.</summary>
    void Discard();

    /// <summary>记录一次观察 / Record one observation.</summary>
    /// <param name="after">动作后上下文 / The context after the action.</param>
    /// <param name="reward">奖励 / The reward.</param>
    /// <param name="evaluations">评估次数 / The evaluation count.</param>
    /// <param name="terminal">是否终局 / Whether it is terminal.</param>
    /// <param name="elapsedNanos">耗时（纳秒）/ The elapsed nanoseconds.</param>
    /// <param name="steps">步数 / The step count.</param>
    /// <param name="failed">是否失败 / Whether it failed.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> Observe(
        SelectionContext after, Flt64 reward, long evaluations, bool terminal,
        long elapsedNanos, long steps, bool failed = false);

    /// <summary>取走已积累的转移 / Drain the accumulated transitions.</summary>
    /// <returns>转移列表 / The transition list.</returns>
    IReadOnlyList<SelectionTransition> DrainTransitions();

    /// <summary>保存槽位状态 / Save the slot state.</summary>
    /// <returns>槽位快照 / The slot snapshot.</returns>
    Result<SlotSnapshot, ErrorCode, Error<ErrorCode>> Save();

    /// <summary>加载槽位状态 / Load the slot state.</summary>
    /// <param name="snapshot">槽位快照 / The slot snapshot.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> Load(SlotSnapshot snapshot);
}

/// <summary>
/// 算子池 / An operator pool.
/// </summary>
/// <remarks>
/// **`choose` 之后必须 `current()` 才算"用过"**。`observe` 只在 `used` 为真时才产生转移——否则"选了但没用"的决策会被记成
/// 一次真实转移，策略会从一次并未发生的执行里学习。
/// **`choose` must be followed by `current()` for the decision to count as used.** `observe` produces a transition only when `used`
/// is true — otherwise a "selected but never used" decision is recorded as a real transition and the policy learns from an execution
/// that never happened.
/// **`save` 跳过 `stateless` 集合里的候选**。无状态算子没有可保存的东西，把它们写进快照会让快照体积随候选数量增长，
/// 而恢复时必须额外核对一堆空载荷。
/// **`save` skips candidates listed in `stateless`.** Stateless operators have nothing to save; writing them inflates snapshots with
/// candidate count and forces restoring to check a pile of empty payloads.
/// **`load` 先校验组件集合再逐个加载**。先加载再校验会让一次失败的恢复把部分算子改到新状态、其余留在旧状态，
/// 而调用方只拿到一个失败结果。
/// **`load` validates the component set before loading any of them.** Loading first would leave a failed restore with some operators in
/// the new state and the rest in the old one, while the caller only sees a failure.
/// **恢复顺序是"策略 → 组件 → 随机流"**。随机流最后恢复，是因为组件的 `load` 可能自身消耗随机数；先恢复随机流会让这些消耗
/// 把流推进到错误位置，而快照里的随机数看起来完全正常。
/// **The restore order is policy, then components, then the random stream.** The random stream goes last because a component's `load`
/// may itself consume randomness; restoring it first would leave those draws advancing the stream from the wrong position while the
/// snapshot's random value looks perfectly normal.
/// </remarks>
/// <typeparam name="T">算子类型 / The operator type.</typeparam>
public sealed class OperatorPool<T> : IOperatorPool where T : class {
    private readonly List<SelectionTransition> _transitions = new();
    private string _action = "";
    private bool _used;
    private SelectionContext _before = new();
    private long _revision;

    /// <summary>
    /// 构造算子池 / Construct the operator pool.
    /// </summary>
    /// <param name="id">池标识 / The pool identity.</param>
    /// <param name="candidates">候选算子 / The candidate operators.</param>
    /// <param name="policy">选择策略 / The selection policy.</param>
    /// <param name="random">随机流 / The random stream.</param>
    /// <param name="stateless">无状态候选名 / The stateless candidate names.</param>
    public OperatorPool(
        string id,
        IReadOnlyDictionary<string, T> candidates,
        ISelectionPolicy policy,
        SearchRandom random,
        IReadOnlyCollection<string> stateless) {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(stateless);
        if (candidates.Count == 0) {
            throw new ArgumentException("an operator pool needs at least one candidate", nameof(candidates));
        }
        Id = id;
        Candidates = candidates;
        Policy = policy;
        Random = random;
        Stateless = stateless;
        string? first = null;
        foreach (string key in candidates.Keys) {
            first = key;
            break;
        }
        _action = first!;
    }

    /// <summary>池标识 / The pool identity.</summary>
    public string Id { get; }

    /// <summary>候选算子 / The candidate operators.</summary>
    public IReadOnlyDictionary<string, T> Candidates { get; }

    /// <summary>选择策略 / The selection policy.</summary>
    public ISelectionPolicy Policy { get; }

    /// <summary>随机流 / The random stream.</summary>
    public SearchRandom Random { get; }

    /// <summary>无状态候选名 / The stateless candidate names.</summary>
    public IReadOnlyCollection<string> Stateless { get; }

    /// <summary>已使用过的动作；未使用时为 `null` / The used action, or `null` when unused.</summary>
    public string? UsedAction => _used ? _action : null;

    /// <summary>
    /// 选择一个算子 / Select an operator.
    /// </summary>
    /// <param name="context">选择上下文 / The selection context.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Choose(SelectionContext context) {
        ArgumentNullException.ThrowIfNull(context);
        var available = new List<string>(Candidates.Count);
        foreach (string key in Candidates.Keys) {
            available.Add(key);
        }
        Result<string, ErrorCode, Error<ErrorCode>> selected = Policy.Select(context, available, Random);
        if (selected is Failed<string, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        _action = ((Ok<string, ErrorCode, Error<ErrorCode>>)selected).Value;
        _before = context;
        _revision = Policy.Revision;
        _used = false;
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 撤销当前决策 / Discard the current decision.
    /// </summary>
    public void Discard() => Policy.Discard(_before.Decision);

    /// <summary>
    /// 取当前算子并标记为已使用 / Take the current operator and mark it used.
    /// </summary>
    /// <returns>当前算子 / The current operator.</returns>
    public T Current() {
        _used = true;
        return Candidates[_action];
    }

    /// <summary>
    /// 记录一次观察 / Record one observation.
    /// </summary>
    /// <param name="after">动作后上下文 / The context after the action.</param>
    /// <param name="reward">奖励 / The reward.</param>
    /// <param name="evaluations">评估次数 / The evaluation count.</param>
    /// <param name="terminal">是否终局 / Whether it is terminal.</param>
    /// <param name="elapsedNanos">耗时（纳秒）/ The elapsed nanoseconds.</param>
    /// <param name="steps">步数 / The step count.</param>
    /// <param name="failed">是否失败 / Whether it failed.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Observe(
        SelectionContext after,
        Flt64 reward,
        long evaluations,
        bool terminal,
        long elapsedNanos,
        long steps,
        bool failed = false) {
        ArgumentNullException.ThrowIfNull(after);
        if (!_used) {
            return Results.Ok(new Success());
        }
        var available = new List<string>(Candidates.Count);
        foreach (string key in Candidates.Keys) {
            available.Add(key);
        }
        var transition = new SelectionTransition(
            Id, _before.Decision, _action, _before, after, available, available,
            reward, evaluations, terminal, _revision,
            Scope: Id, Steps: steps, Failed: failed, ElapsedNanos: elapsedNanos,
            PreviousAction: Policy.Predecessor(_before.Decision));
        _transitions.Add(transition);
        return Policy.Observe(transition);
    }

    /// <summary>
    /// 取走已积累的转移 / Drain the accumulated transitions.
    /// </summary>
    /// <returns>转移列表 / The transition list.</returns>
    public IReadOnlyList<SelectionTransition> DrainTransitions() {
        var result = new List<SelectionTransition>(_transitions);
        _transitions.Clear();
        return result;
    }

    /// <summary>
    /// 保存槽位状态 / Save the slot state.
    /// </summary>
    /// <returns>槽位快照 / The slot snapshot.</returns>
    public Result<SlotSnapshot, ErrorCode, Error<ErrorCode>> Save() {
        var states = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, T> pair in Candidates) {
            if (IsStateless(pair.Key)) {
                continue;
            }
            if (pair.Value is not IRecoverableComponent component) {
                return SearchCodecSupport.SearchInvalid<SlotSnapshot>(
                    $"slot {pair.Key} is not recoverable");
            }
            Result<JsonElement, ErrorCode, Error<ErrorCode>> saved = component.Save();
            if (saved is Failed<JsonElement, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<SlotSnapshot>(failed.Error);
            }
            states[pair.Key] = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)saved).Value;
        }
        return Results.Ok(new SlotSnapshot(Policy.Snapshot(), Random.State, states));
    }

    /// <summary>
    /// 加载槽位状态 / Load the slot state.
    /// </summary>
    /// <param name="snapshot">槽位快照 / The slot snapshot.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Load(SlotSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in Candidates.Keys) {
            if (!IsStateless(key)) {
                expected.Add(key);
            }
        }
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in snapshot.Components.Keys) {
            actual.Add(key);
        }
        if (!expected.SetEquals(actual)) {
            return SearchCodecSupport.SearchInvalid<Success>(
                $"incompatible slot components: {Id}");
        }

        Result<Success, ErrorCode, Error<ErrorCode>> restored = Policy.Restore(snapshot.Policy);
        if (restored is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<Success>(failed.Error);
        }
        foreach (KeyValuePair<string, JsonElement> pair in snapshot.Components) {
            if (Candidates[pair.Key] is not IRecoverableComponent component) {
                return SearchCodecSupport.SearchInvalid<Success>($"slot {pair.Key} is not recoverable");
            }
            Result<Success, ErrorCode, Error<ErrorCode>> loaded = component.Load(pair.Value);
            if (loaded is Failed<Success, ErrorCode, Error<ErrorCode>> loadFailed) {
                return Results.Failed<Success>(loadFailed.Error);
            }
        }
        Random.Restore(snapshot.Random);
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 名称是否属于无状态集合 / Whether a name belongs to the stateless set.
    /// </summary>
    /// <param name="name">名称 / The name.</param>
    /// <returns>是否无状态 / Whether it is stateless.</returns>
    private bool IsStateless(string name) {
        foreach (string candidate in Stateless) {
            if (string.Equals(candidate, name, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// 算子绑定 / Operator bindings.
/// </summary>
/// <remarks>
/// **`pools` 的顺序必须确定**。参数池按名称排序，其余按"更新 → 变异 → 邻域"固定顺序；顺序不稳定会让同一份种子的
/// 随机流消耗顺序随字典遍历顺序变化，**轨迹不可重放而每次运行本身看起来都正常**。
/// **The `pools` order must be deterministic.** Parameter pools are sorted by name and the rest follow a fixed
/// update → mutation → neighborhood order; an unstable order makes random-stream consumption follow dictionary iteration order, so
/// **the trajectory is not replayable while each run looks normal**.
/// **`Parameter` 在缺少对应池时返回默认源，而不是失败**。参数是可选的，强制每个算法都配置全部参数的池会让"用默认值"
/// 这件事无处表达。
/// **`Parameter` returns the default source when no pool exists rather than failing.** Parameters are optional, and forcing every
/// algorithm to configure a pool for each parameter leaves "use the default" unexpressible.
/// **`SolutionUpdate`/`MutationOperator`/`NeighborhoodOperator` 是每次取值都新建的适配器**。它们持有池的引用，`Current()` 在被调用时
/// 才取算子——把算子本身缓存进适配器会让后续 `Choose` 选出的新算子被忽略，**策略看起来仍在工作但实际一直执行第一个算子**。
/// **The `SolutionUpdate`/`MutationOperator`/`NeighborhoodOperator` adapters are created fresh on every access.** They hold a reference
/// to the pool and only call `Current()` when invoked; caching the operator itself inside the adapter makes later `Choose` results
/// ignored, so **the policy appears to work while the first operator always executes**.
/// </remarks>
public sealed class OperatorBindings {
    /// <summary>
    /// 构造绑定 / Construct the bindings.
    /// </summary>
    /// <param name="update">更新算子池 / The update-operator pool.</param>
    /// <param name="mutation">变异算子池 / The mutation-operator pool.</param>
    /// <param name="neighborhood">邻域算子池 / The neighborhood-operator pool.</param>
    /// <param name="parameters">参数源池 / The parameter-source pools.</param>
    public OperatorBindings(
        OperatorPool<ISolutionUpdate>? update,
        OperatorPool<IMutationOperator>? mutation,
        OperatorPool<INeighborhood>? neighborhood,
        IReadOnlyDictionary<string, OperatorPool<IParameterSource>> parameters) {
        ArgumentNullException.ThrowIfNull(parameters);
        Update = update;
        Mutation = mutation;
        Neighborhood = neighborhood;
        Parameters = parameters;

        // **在构造函数里一次性建好视图列表**，保证整个会话期间 `choose`/`observe` 作用在同一批池对象上。
        // **The view list is built once in the constructor** so that `choose`/`observe` act on the same pool objects for the whole
        // session.
        var pools = new List<IOperatorPool>();
        if (update is not null) {
            pools.Add(update);
        }
        if (mutation is not null) {
            pools.Add(mutation);
        }
        if (neighborhood is not null) {
            pools.Add(neighborhood);
        }
        foreach (OperatorPool<IParameterSource> pool in ParameterPools()) {
            pools.Add(pool);
        }
        _pools = pools;
    }

    /// <summary>缓存的算子池视图 / The cached operator-pool view.</summary>
    private readonly IReadOnlyList<IOperatorPool> _pools;

    /// <summary>更新算子池 / The update-operator pool.</summary>
    public OperatorPool<ISolutionUpdate>? Update { get; }

    /// <summary>变异算子池 / The mutation-operator pool.</summary>
    public OperatorPool<IMutationOperator>? Mutation { get; }

    /// <summary>邻域算子池 / The neighborhood-operator pool.</summary>
    public OperatorPool<INeighborhood>? Neighborhood { get; }

    /// <summary>参数源池 / The parameter-source pools.</summary>
    public IReadOnlyDictionary<string, OperatorPool<IParameterSource>> Parameters { get; }

    /// <summary>
    /// 全部算子池（顺序确定，且是同一批池对象）/ Every operator pool in a deterministic order, over the same pool objects.
    /// </summary>
    /// <returns>算子池列表 / The pool list.</returns>
    public IReadOnlyList<IOperatorPool> Pools() => _pools;

    /// <summary>
    /// 取全部参数源池（顺序确定）/ Every parameter-source pool in a deterministic order.
    /// </summary>
    /// <returns>参数源池列表 / The parameter-source pool list.</returns>
    public IReadOnlyList<OperatorPool<IParameterSource>> ParameterPools() {
        var names = new List<string>(Parameters.Keys);
        names.Sort(StringComparer.Ordinal);
        var pools = new List<OperatorPool<IParameterSource>>(names.Count);
        foreach (string name in names) {
            pools.Add(Parameters[name]);
        }
        return pools;
    }

    /// <summary>
    /// 按名称取参数源 / Get a parameter source by name.
    /// </summary>
    /// <param name="name">参数名 / The parameter name.</param>
    /// <param name="fallback">默认源 / The fallback source.</param>
    /// <returns>参数源 / The parameter source.</returns>
    public IParameterSource Parameter(string name, IParameterSource fallback) {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fallback);
        if (!Parameters.TryGetValue(name, out OperatorPool<IParameterSource>? pool)) {
            return fallback;
        }
        return new PooledParameterSource(pool);
    }
}

/// <summary>
/// 池化的参数源适配器 / A pooled parameter-source adapter.
/// </summary>
/// <remarks>
/// **每次取值都通过池的 `Current()` 取算子**，而不是把某个具体源缓存下来。
/// **Every read goes through the pool's `Current()`** instead of caching one concrete source.
/// </remarks>
internal sealed class PooledParameterSource : IParameterSource {
    private readonly OperatorPool<IParameterSource> _pool;

    /// <summary>
    /// 构造适配器 / Construct the adapter.
    /// </summary>
    /// <param name="pool">参数源池 / The parameter-source pool.</param>
    public PooledParameterSource(OperatorPool<IParameterSource> pool) => _pool = pool;

    /// <inheritdoc/>
    public Flt64 Value(Iteration iteration) => _pool.Current().Value(iteration);
}
