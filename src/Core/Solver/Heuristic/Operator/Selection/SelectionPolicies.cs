// 选择策略 / Selection policies.
// 与固定 Kotlin 基线 `selection/SelectionPolicy.kt`（13691 B）对应。
// Mirrors the pinned baseline's `SelectionPolicy.kt` (13691 B).

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Rl;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 选择上下文 / A selection context.
/// </summary>
/// <param name="State">状态下标 / The state index.</param>
/// <param name="Features">特征向量 / The feature vector.</param>
/// <param name="Decision">决策序号 / The decision index.</param>
/// <param name="Stagnation">停滞计数 / The stagnation count.</param>
public sealed record SelectionContext(
    [property: JsonPropertyName("state")] int State = 0,
    [property: JsonPropertyName("features")] IReadOnlyList<Flt64>? Features = null,
    [property: JsonPropertyName("decision")] long Decision = 0,
    [property: JsonPropertyName("stagnation")] long Stagnation = 0);

/// <summary>
/// 选择转移 / A selection transition.
/// </summary>
/// <param name="Episode">回合标识 / The episode identity.</param>
/// <param name="Decision">决策序号 / The decision index.</param>
/// <param name="Action">动作名 / The action name.</param>
/// <param name="Before">动作前上下文 / The context before the action.</param>
/// <param name="After">动作后上下文 / The context after the action.</param>
/// <param name="Available">可选动作 / The available actions.</param>
/// <param name="NextAvailable">下一轮可选动作 / the next round's available actions.</param>
/// <param name="Reward">奖励 / The reward.</param>
/// <param name="Evaluations">评估次数 / The evaluation count.</param>
/// <param name="Terminal">是否终局 / Whether it is terminal.</param>
/// <param name="PolicyVersion">策略版本 / The policy version.</param>
/// <param name="FeatureVersion">特征版本 / The feature version.</param>
/// <param name="Scope">作用域 / The scope.</param>
/// <param name="Steps">步数 / The step count.</param>
/// <param name="Failed">是否失败 / Whether it failed.</param>
/// <param name="MergedImprovement">是否合并改进 / Whether the improvement was merged.</param>
/// <param name="ElapsedNanos">耗时（纳秒）/ The elapsed nanoseconds.</param>
/// <param name="PreviousAction">上一个动作 / The previous action.</param>
public sealed record SelectionTransition(
    [property: JsonPropertyName("episode")] string Episode,
    [property: JsonPropertyName("decision")] long Decision,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("before")] SelectionContext Before,
    [property: JsonPropertyName("after")] SelectionContext After,
    [property: JsonPropertyName("available")] IReadOnlyList<string> Available,
    [property: JsonPropertyName("nextAvailable")] IReadOnlyList<string> NextAvailable,
    [property: JsonPropertyName("reward")] Flt64 Reward,
    [property: JsonPropertyName("evaluations")] long Evaluations,
    [property: JsonPropertyName("terminal")] bool Terminal,
    [property: JsonPropertyName("policyVersion")] long PolicyVersion,
    [property: JsonPropertyName("featureVersion")] string FeatureVersion = "progress:1",
    [property: JsonPropertyName("scope")] string Scope = "algorithm",
    [property: JsonPropertyName("steps")] long Steps = 0,
    [property: JsonPropertyName("failed")] bool Failed = false,
    [property: JsonPropertyName("mergedImprovement")] bool MergedImprovement = false,
    [property: JsonPropertyName("elapsedNanos")] long ElapsedNanos = 0,
    [property: JsonPropertyName("previousAction")] string? PreviousAction = null);

/// <summary>
/// 策略快照 / A policy snapshot.
/// </summary>
/// <param name="Version">版本 / The version.</param>
/// <param name="Type">策略类型 / The policy type.</param>
/// <param name="Actions">动作列表 / The action list.</param>
/// <param name="Revision">修订号 / The revision.</param>
/// <param name="Cursor">游标 / The cursor.</param>
/// <param name="Counts">各动作计数 / The per-action counts.</param>
/// <param name="Sums">各动作奖励和 / The per-action reward sums.</param>
/// <param name="Table">Q 表 / The Q table.</param>
/// <param name="Extension">扩展载荷 / The extension payload.</param>
public sealed record PolicySnapshot(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("actions")] IReadOnlyList<string> Actions,
    [property: JsonPropertyName("revision")] long Revision = 0,
    [property: JsonPropertyName("cursor")] long Cursor = 0,
    [property: JsonPropertyName("counts")] IReadOnlyList<long>? Counts = null,
    [property: JsonPropertyName("sums")] IReadOnlyList<double>? Sums = null,
    [property: JsonPropertyName("table")] IReadOnlyList<IReadOnlyList<double>>? Table = null,
    [property: JsonPropertyName("extension")] JsonElement Extension = default);

/// <remarks>
/// **不要给这个记录加第二个构造函数**。`System.Text.Json` 在存在多个构造函数且无一标注 `JsonConstructor` 时无法反序列化，
/// 报"类型没有无参构造/单一参数化构造"，而错误只在**恢复快照**时出现——写入快照一直正常。
/// **Never add a second constructor to this record.** With several constructors and none annotated `JsonConstructor`,
/// `System.Text.Json` cannot deserialize and reports "no parameterless or singular parameterized constructor" — and the error only shows up
/// **when restoring a snapshot**, while writing one keeps working.
/// </remarks>

/// <summary>
/// 选择策略 / A selection policy.
/// </summary>
/// <remarks>
/// **`snapshot`/`restore` 必须按动作名对齐，不能按下标**。快照里的 `actions` 顺序是"当时配置的顺序"，恢复时的 `actions` 可能
/// 已经被重新排序或插入；按下标恢复会把 A 动作的计数搬到 B 动作上，**每个动作的数值都仍然合理**，只是整体错位。
/// **`snapshot`/`restore` must align by action name, not by index.** The snapshot's `actions` order is "the order configured then",
/// while the restoring side's `actions` may have been reordered or extended; restoring by index moves A's counts onto B, and **every
/// action's numbers still look plausible**, only misattributed.
/// **`discard`/`predecessor` 有默认实现**：不是每个策略都需要"撤销上一次决策"或"查询前驱"，强制它们实现会让无状态策略
/// 不得不写一堆空方法。
/// **`discard`/`predecessor` have default implementations**: not every policy needs to undo a decision or query a predecessor, and
/// forcing them would make stateless policies implement a pile of empty methods.
/// </remarks>
public interface ISelectionPolicy {
    /// <summary>修订号 / The revision.</summary>
    long Revision { get; }

    /// <summary>状态数量 / The state count.</summary>
    int? StateCount => null;

    /// <summary>
    /// 选择一个动作 / Select an action.
    /// </summary>
    /// <param name="context">上下文 / The context.</param>
    /// <param name="available">可选动作 / The available actions.</param>
    /// <param name="random">随机数源 / The randomness source.</param>
    /// <returns>动作名 / The action name.</returns>
    Result<string, ErrorCode, Error<ErrorCode>> Select(
        SelectionContext context, IReadOnlyList<string> available, SearchRandom random);

    /// <summary>
    /// 观察一次转移 / Observe a transition.
    /// </summary>
    /// <param name="transition">转移 / The transition.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> Observe(SelectionTransition transition);

    /// <summary>
    /// 撤销一次决策 / Discard one decision.
    /// </summary>
    /// <param name="decision">决策序号 / The decision index.</param>
    void Discard(long decision) { }

    /// <summary>
    /// 查询某个决策的前驱动作 / Query the predecessor action of a decision.
    /// </summary>
    /// <param name="decision">决策序号 / The decision index.</param>
    /// <returns>前驱动作 / The predecessor action.</returns>
    string? Predecessor(long decision) => null;

    /// <summary>
    /// 生成快照 / Produce a snapshot.
    /// </summary>
    /// <returns>快照 / The snapshot.</returns>
    PolicySnapshot Snapshot();

    /// <summary>
    /// 从快照恢复 / Restore from a snapshot.
    /// </summary>
    /// <param name="snapshot">快照 / The snapshot.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> Restore(PolicySnapshot snapshot);
}

/// <summary>
/// 策略载体 / A policy carrier.
/// </summary>
public interface IPolicyCarrier {
    /// <summary>
    /// 全部策略快照 / Every policy snapshot.
    /// </summary>
    /// <returns>策略快照映射 / The policy-snapshot map.</returns>
    IReadOnlyDictionary<string, PolicySnapshot> Policies();

    /// <summary>
    /// 取走已积累的转移 / Drain the accumulated transitions.
    /// </summary>
    /// <returns>转移列表 / The transition list.</returns>
    IReadOnlyList<SelectionTransition> DrainTransitions() => Array.Empty<SelectionTransition>();

    /// <summary>
    /// 从快照恢复全部策略 / Restore every policy from snapshots.
    /// </summary>
    /// <param name="policies">策略快照映射 / The policy-snapshot map.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> RestorePolicies(
        IReadOnlyDictionary<string, PolicySnapshot> policies);
}

/// <summary>
/// 表格型选择策略 / A tabular selection policy.
/// </summary>
/// <remarks>
/// **`ucb1` 显式优先选择"从未试过"的动作，但这条分支在本实现里是冗余的**。若去掉它，`mean + sqrt(2*ln(total+1)/counts)`
/// 在 `counts == 0` 时得到 `0 + ∞ = ∞`，`Argmax` 仍然会选中未试过的动作——变异测试证实：删掉该分支后测试**仍然通过**。
///
/// 保留它的理由是**把"除零得到无穷大"这个隐式行为变成显式判断**：依赖 `∞` 比较需要读代码的人先相信 `Argmax` 的 `>` 语义，
/// 而显式分支让意图直接可读。这是**等价变异**（代码无行为差异），不是"这条测试守住了关键逻辑"。
/// **`ucb1` explicitly prefers never-tried actions, but that branch is redundant in this implementation.** Without it,
/// `mean + sqrt(2*ln(total+1)/counts)` yields `0 + ∞ = ∞` at `counts == 0` and `Argmax` still selects the untried action — the mutation
/// test confirmed that removing the branch **still passes**.
///
/// It is kept to turn the implicit "division by zero yields infinity" into an explicit check: relying on the `∞` comparison requires the
/// reader to trust `Argmax`'s `>` semantics, whereas the explicit branch states the intent. This is an **equivalent mutant** (no
/// behavioural difference), not "the test guards the critical logic".
/// **`round-robin` 的游标是"上一个被选中的下标 + 1"**，而不是"调用次数 + 1"。用调用次数会让掩码变化后的轮转从错误的位置开始，
/// 被屏蔽的动作反复"跳过"，而每次调用本身看起来完全正常。
/// **`round-robin`'s cursor is "the last chosen index + 1"**, not "call count + 1". Using the call count makes rotation restart from the
/// wrong position after the mask changes, repeatedly skipping masked actions while every call looks perfectly normal.
/// **`stagnation` 只在停滞计数恰好是 `stagnationLimit` 的整数倍时推进游标**。每步都推进会让它退化成 `round-robin`，
/// 停滞信号完全失效。
/// **`stagnation` advances the cursor only when the stagnation count is an exact multiple of `stagnationLimit`.** Advancing every step
/// degenerates it into `round-robin` and the stagnation signal is lost.
/// **`observe` 在 `learning == false` 时直接返回成功**，不累加计数、不更新 Q 表。若仍然累加，`ucb1` 的探索项会随观察次数增长，
/// **"关闭学习"实际上没有关闭**。
/// **`observe` returns success immediately when `learning == false`**, without accumulating counts or updating the Q table. Accumulating
/// anyway makes `ucb1`'s exploration term grow with observations, so **"learning off" is not actually off**.
/// **Q 表只在 `type == "q-learning"` 时参与快照**。其他类型也写入快照会让快照体积无谓膨胀，且恢复时必须额外校验一张
/// 永远不用的表。
/// **The Q table participates in snapshots only when `type == "q-learning"`.** Writing it for other types bloats snapshots and forces
/// restoring to validate a table that is never used.
/// </remarks>
public sealed class TabularSelectionPolicy : ISelectionPolicy {
    private readonly List<string> _actions;
    private readonly Dictionary<string, int> _actionIndices;
    private readonly List<long> _counts;
    private readonly List<double> _sums;
    private readonly QTable _table;

    /// <summary>
    /// 构造策略 / Construct the policy.
    /// </summary>
    /// <param name="type">策略类型 / The policy type.</param>
    /// <param name="actions">动作列表 / The action list.</param>
    /// <param name="learning">是否学习 / Whether it learns.</param>
    /// <param name="epsilon">探索率 / The exploration rate.</param>
    /// <param name="alpha">学习率 / The learning rate.</param>
    /// <param name="gamma">折扣因子 / The discount factor.</param>
    /// <param name="states">状态数量 / The state count.</param>
    /// <param name="stagnationLimit">停滞阈值 / The stagnation threshold.</param>
    public TabularSelectionPolicy(
        string type,
        IReadOnlyList<string> actions,
        bool learning = true,
        double epsilon = 0.1,
        double alpha = 0.5,
        double gamma = 0.9,
        int states = 64,
        long stagnationLimit = 10) {
        ArgumentNullException.ThrowIfNull(actions);
        Type = type;
        Learning = learning;
        Epsilon = epsilon;
        Alpha = alpha;
        Gamma = gamma;
        States = states;
        StagnationLimit = stagnationLimit;
        _actions = new List<string>(actions);
        _actionIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < _actions.Count; index++) {
            _actionIndices.TryAdd(_actions[index], index);
        }
        _counts = new List<long>(_actions.Count);
        _sums = new List<double>(_actions.Count);
        for (int index = 0; index < _actions.Count; index++) {
            _counts.Add(0L);
            _sums.Add(0.0);
        }
        _table = new QTable(States, _actions.Count);
    }

    /// <summary>策略类型 / The policy type.</summary>
    public string Type { get; }

    /// <summary>动作列表 / The action list.</summary>
    public IReadOnlyList<string> Actions => _actions;

    /// <summary>是否学习 / Whether it learns.</summary>
    public bool Learning { get; }

    /// <summary>探索率 / The exploration rate.</summary>
    public double Epsilon { get; }

    /// <summary>学习率 / The learning rate.</summary>
    public double Alpha { get; }

    /// <summary>折扣因子 / The discount factor.</summary>
    public double Gamma { get; }

    /// <summary>状态数量 / The state count.</summary>
    public int States { get; }

    /// <summary>停滞阈值 / The stagnation threshold.</summary>
    public long StagnationLimit { get; }

    /// <inheritdoc/>
    public int? StateCount => States;

    /// <inheritdoc/>
    public long Revision { get; private set; }

    /// <summary>游标 / The cursor.</summary>
    private long Cursor { get; set; }

    /// <summary>
    /// 校验配置 / Validate the configuration.
    /// </summary>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        string[] known = {
            "random", "round-robin", "fixed", "stagnation",
            "epsilon-greedy", "ucb1", "q-learning",
        };
        bool knownType = false;
        foreach (string candidate in known) {
            if (string.Equals(candidate, Type, StringComparison.Ordinal)) {
                knownType = true;
                break;
            }
        }
        if (!knownType) {
            return SearchCodecSupport.SearchInvalid<Success>($"unknown selection: {Type}");
        }
        if (_actions.Count == 0 || States <= 0) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid action/state definitions");
        }
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (string action in _actions) {
            if (string.IsNullOrWhiteSpace(action) || !distinct.Add(action)) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid action/state definitions");
            }
        }
        if (!IsUnit(Epsilon) || !IsUnit(Alpha) || !IsUnit(Gamma) || StagnationLimit <= 0) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid learning parameters");
        }
        return Results.Ok(new Success());
    }

    /// <inheritdoc/>
    public Result<string, ErrorCode, Error<ErrorCode>> Select(
        SelectionContext context, IReadOnlyList<string> available, SearchRandom random) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(random);
        if (available.Count == 0) {
            return SearchCodecSupport.SearchInvalid<string>("empty or invalid action mask");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var indices = new List<int>(available.Count);
        foreach (string action in available) {
            if (!seen.Add(action) || !_actionIndices.ContainsKey(action)) {
                return SearchCodecSupport.SearchInvalid<string>("empty or invalid action mask");
            }
            indices.Add(_actionIndices[action]);
        }
        if (context.State < 0 || context.State >= States) {
            return SearchCodecSupport.SearchInvalid<string>("invalid selection state");
        }
        if (context.Features is not null) {
            foreach (Flt64 feature in context.Features) {
                if (!double.IsFinite(feature.ToDouble())) {
                    return SearchCodecSupport.SearchInvalid<string>("invalid selection state");
                }
            }
        }

        int RandomIndex() =>
            indices[global::System.Math.Clamp((int)(random.NextDouble() * indices.Count), 0, indices.Count - 1)];

        int index;
        switch (Type) {
            case "random":
                index = RandomIndex();
                break;
            case "fixed":
            case "round-robin": {
                int start = (int)(Cursor % _actions.Count);
                var mask = new HashSet<int>(indices);
                int chosen = start;
                for (int offset = 0; offset < _actions.Count; offset++) {
                    int candidate = (start + offset) % _actions.Count;
                    if (mask.Contains(candidate)) {
                        chosen = candidate;
                        break;
                    }
                }
                Cursor = chosen + 1L;
                index = chosen;
                break;
            }
            case "stagnation": {
                if (context.Stagnation > 0 && context.Stagnation % StagnationLimit == 0L) {
                    Cursor++;
                }
                int start = (int)(Cursor % _actions.Count);
                int? chosen = null;
                foreach (int candidate in indices) {
                    if (candidate >= start) {
                        chosen = candidate;
                        break;
                    }
                }
                index = chosen ?? indices[0];
                break;
            }
            case "q-learning":
                index = Learning && random.NextDouble() < Epsilon
                    ? RandomIndex()
                    : Argmax(indices, candidate => _table.ValueOf(context.State, candidate).ToDouble());
                break;
            case "epsilon-greedy":
                index = Learning && random.NextDouble() < Epsilon
                    ? RandomIndex()
                    : Argmax(indices, Mean);
                break;
            case "ucb1": {
                if (!Learning) {
                    index = Argmax(indices, Mean);
                    break;
                }
                int? untried = null;
                foreach (int candidate in indices) {
                    if (_counts[candidate] == 0L) {
                        untried = candidate;
                        break;
                    }
                }
                if (untried is not null) {
                    index = untried.Value;
                    break;
                }
                long total = 0L;
                foreach (long count in _counts) {
                    total += count;
                }
                double exploration = global::System.Math.Log(total + 1.0);
                index = Argmax(indices, candidate =>
                    Mean(candidate) + global::System.Math.Sqrt(2.0 * exploration / _counts[candidate]));
                break;
            }
            default:
                return SearchCodecSupport.SearchInvalid<string>($"unknown selection: {Type}");
        }
        return Results.Ok(_actions[index]);
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Observe(SelectionTransition transition) {
        ArgumentNullException.ThrowIfNull(transition);
        if (!_actionIndices.TryGetValue(transition.Action, out int index)) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid observed transition");
        }
        if (!Contains(transition.Available, transition.Action)
            || !double.IsFinite(transition.Reward.ToDouble())
            || transition.Evaluations < 0
            || transition.Before.State < 0 || transition.Before.State >= States
            || transition.After.State < 0 || transition.After.State >= States) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid observed transition");
        }
        foreach (string action in transition.Available) {
            if (!_actionIndices.ContainsKey(action)) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid observed transition");
            }
        }
        var nextIndices = new List<int>(transition.NextAvailable.Count);
        foreach (string action in transition.NextAvailable) {
            if (!_actionIndices.TryGetValue(action, out int next)) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid observed transition");
            }
            nextIndices.Add(next);
        }

        if (!Learning) {
            return Results.Ok(new Success());
        }
        if (Type == "q-learning") {
            Result<Flt64, ErrorCode, Error<ErrorCode>> updated = _table.Update(
                transition.Before.State, index, transition.Reward, transition.After.State,
                new Flt64(Alpha), new Flt64(Gamma), transition.Terminal, nextIndices);
            if (updated is Failed<Flt64, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<Success>(failed.Error);
            }
        }
        if (Type == "q-learning" || Type == "epsilon-greedy" || Type == "ucb1") {
            _counts[index]++;
            _sums[index] += transition.Reward.ToDouble();
            Revision++;
        }
        return Results.Ok(new Success());
    }

    /// <inheritdoc/>
    public PolicySnapshot Snapshot() {
        var table = new List<IReadOnlyList<double>>();
        if (Type == "q-learning") {
            foreach (IReadOnlyList<Flt64> row in _table.Snapshot()) {
                var converted = new List<double>(row.Count);
                foreach (Flt64 value in row) {
                    converted.Add(value.ToDouble());
                }
                table.Add(converted);
            }
        }
        // **`Extension` 必须是真正的 JSON `null`**：`default(JsonElement)` 的 `ValueKind` 是 `Undefined`，
        // 任何把它写进快照的序列化都会抛出 `InvalidOperationException`。
        // **`Extension` must be a real JSON `null`**: `default(JsonElement)` has `ValueKind == Undefined`, and any serialization that
        // writes it into a snapshot throws `InvalidOperationException`.
        return new PolicySnapshot(
            1, Type, new List<string>(_actions), Revision, Cursor,
            new List<long>(_counts), new List<double>(_sums), table,
            JsonDocument.Parse("null").RootElement.Clone());
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Restore(PolicySnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version != 1 || !string.Equals(snapshot.Type, Type, StringComparison.Ordinal)
            || snapshot.Revision < 0 || snapshot.Cursor < 0) {
            return SearchCodecSupport.SearchInvalid<Success>("incompatible policy snapshot");
        }
        if (snapshot.Actions.Count != _actions.Count) {
            return SearchCodecSupport.SearchInvalid<Success>("incompatible policy snapshot");
        }
        var snapshotIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < snapshot.Actions.Count; index++) {
            if (!snapshotIndices.TryAdd(snapshot.Actions[index], index)) {
                return SearchCodecSupport.SearchInvalid<Success>("incompatible policy snapshot");
            }
        }
        var permutation = new List<int>(_actions.Count);
        foreach (string action in _actions) {
            if (!snapshotIndices.TryGetValue(action, out int position)) {
                return SearchCodecSupport.SearchInvalid<Success>("incompatible policy snapshot");
            }
            permutation.Add(position);
        }

        IReadOnlyList<long>? counts = snapshot.Counts;
        IReadOnlyList<double>? sums = snapshot.Sums;
        if (counts is null || sums is null
            || counts.Count != _actions.Count || sums.Count != _actions.Count) {
            return SearchCodecSupport.SearchInvalid<Success>("incompatible policy snapshot");
        }
        foreach (long count in counts) {
            if (count < 0) {
                return SearchCodecSupport.SearchInvalid<Success>("incompatible policy snapshot");
            }
        }
        foreach (double sum in sums) {
            if (!double.IsFinite(sum)) {
                return SearchCodecSupport.SearchInvalid<Success>("incompatible policy snapshot");
            }
        }

        if (Type == "q-learning") {
            IReadOnlyList<IReadOnlyList<double>>? table = snapshot.Table;
            if (table is null || table.Count != States) {
                return SearchCodecSupport.SearchInvalid<Success>("incompatible Q table");
            }
            var restored = new List<IReadOnlyList<Flt64>>(table.Count);
            foreach (IReadOnlyList<double> row in table) {
                if (row.Count != _actions.Count) {
                    return SearchCodecSupport.SearchInvalid<Success>("incompatible Q table");
                }
                var converted = new List<Flt64>(permutation.Count);
                foreach (int position in permutation) {
                    converted.Add(new Flt64(row[position]));
                }
                restored.Add(converted);
            }
            Result<Success, ErrorCode, Error<ErrorCode>> result = _table.Restore(restored);
            if (result is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<Success>(failed.Error);
            }
        }

        for (int index = 0; index < _actions.Count; index++) {
            _counts[index] = counts[permutation[index]];
            _sums[index] = sums[permutation[index]];
        }
        Cursor = snapshot.Cursor;
        Revision = snapshot.Revision;
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 动作的平均奖励 / The mean reward of an action.
    /// </summary>
    /// <param name="index">动作下标 / The action index.</param>
    /// <returns>平均奖励 / The mean reward.</returns>
    private double Mean(int index) => _counts[index] == 0L ? 0.0 : _sums[index] / _counts[index];

    /// <summary>
    /// 取使指标最大的动作 / The action maximizing a score.
    /// </summary>
    /// <param name="indices">候选下标 / The candidate indices.</param>
    /// <param name="score">指标函数 / The score function.</param>
    /// <returns>动作下标 / The action index.</returns>
    private static int Argmax(IReadOnlyList<int> indices, Func<int, double> score) {
        int best = indices[0];
        double bestValue = score(best);
        for (int position = 1; position < indices.Count; position++) {
            double value = score(indices[position]);
            if (value > bestValue) {
                bestValue = value;
                best = indices[position];
            }
        }
        return best;
    }

    /// <summary>
    /// 值是否位于 `[0, 1]` 且有限 / Whether a value is finite and inside `[0, 1]`.
    /// </summary>
    /// <param name="value">取值 / The value.</param>
    /// <returns>是否合法 / Whether it is legal.</returns>
    private static bool IsUnit(double value) => double.IsFinite(value) && value >= 0.0 && value <= 1.0;

    /// <summary>
    /// 动作是否在列表中 / Whether an action appears in a list.
    /// </summary>
    /// <param name="available">列表 / The list.</param>
    /// <param name="action">动作 / The action.</param>
    /// <returns>是否包含 / Whether it is contained.</returns>
    private static bool Contains(IReadOnlyList<string> available, string action) {
        for (int index = 0; index < available.Count; index++) {
            if (string.Equals(available[index], action, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }
}
