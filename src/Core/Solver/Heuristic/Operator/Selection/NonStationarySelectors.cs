// 非平稳选择器 / Non-stationary selectors.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator;

/// <summary>
/// 一条信用观测 / One credit observation.
/// </summary>
/// <param name="Action">动作名 / The action name.</param>
/// <param name="Reward">奖励 / The reward.</param>
public sealed record CreditObservation(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("reward")] double Reward);

/// <summary>
/// 一次待反馈的派发 / A dispatch awaiting feedback.
/// </summary>
/// <param name="Action">动作名 / The action name.</param>
/// <param name="Previous">前驱动作 / The predecessor action.</param>
public sealed record CreditDispatch(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("previous")] string? Previous);

/// <summary>
/// 信用状态快照 / A credit-state snapshot.
/// </summary>
/// <param name="Definition">定义向量 / The definition vector.</param>
/// <param name="Window">滑动窗口 / The sliding window.</param>
/// <param name="Quality">各动作质量 / The per-action quality.</param>
/// <param name="Pairs">前驱-动作对信用 / The predecessor-action pair credits.</param>
/// <param name="Probabilities">各动作概率 / The per-action probabilities.</param>
/// <param name="LastUsed">各动作最近使用时刻 / The per-action last-used clocks.</param>
/// <param name="Previous">前驱动作 / The predecessor action.</param>
/// <param name="Pending">待反馈派发 / The pending dispatches.</param>
/// <param name="Clock">时钟 / The clock.</param>
public sealed record CreditState(
    [property: JsonPropertyName("definition")] IReadOnlyList<double> Definition,
    [property: JsonPropertyName("window")] IReadOnlyList<CreditObservation> Window,
    [property: JsonPropertyName("quality")] IReadOnlyDictionary<string, double> Quality,
    [property: JsonPropertyName("pairs")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> Pairs,
    [property: JsonPropertyName("probabilities")] IReadOnlyDictionary<string, double> Probabilities,
    [property: JsonPropertyName("lastUsed")] IReadOnlyDictionary<string, long> LastUsed,
    [property: JsonPropertyName("previous")] string? Previous,
    [property: JsonPropertyName("pending")] IReadOnlyDictionary<long, CreditDispatch> Pending,
    [property: JsonPropertyName("clock")] long Clock);

/// <summary>
/// 非平稳选择器 / A non-stationary selector.
/// </summary>
/// <remarks>
/// **奖励必须夹到非负后再进窗口**。`frr-mab` 的分数按窗口内奖励归一化求和；负奖励会让"某动作的分数"变成负数，
/// 而 `pow(decay, rank) * (size - rank)` 的权重仍然为正——**分数越低反而权重越高**，选择方向整体反转。
/// **Rewards must be clamped to non-negative before entering the window.** `frr-mab` normalizes and sums window rewards; a negative
/// reward makes an action's score negative while the `pow(decay, rank) * (size - rank)` weight stays positive — **a lower score gets a
/// higher weight**, inverting the selection.
/// **`frr-mab` 的探索项用"窗口内该动作的计数"，不是"全历史计数"**。用全历史会让窗口滚动失去意义，选择退化成 UCB1。
/// **"未试过"的显式分支是等价变异**：删掉它之后，`counts == 0` 让探索项成为 `∞`，`>` 比较仍会选中该动作——变异测试证实
/// 删掉后测试仍然通过。保留它只为把"依赖 `∞` 比较"变成显式判断，**没有行为差异**（与 earlier 的 `ucb1` 同理）。
/// **`frr-mab`'s exploration term uses the action's count inside the window, not its all-time count.** An all-time count makes the
/// sliding window meaningless and degenerates the selector into UCB1.
/// **`choice-function` 的归一化按"该映射内的绝对值最大者"缩放**，不是全局常数。用全局常数会让质量项与配对项的量级不可比，
/// 配对项被系统性淹没。
/// **`choice-function` normalization scales by the largest absolute value within that map**, not a global constant. A global constant
/// makes the quality and pair terms incomparable and systematically drowns the pair term.
/// **`pending` 用于在 `select` 与 `observe` 之间传递"前驱动作"**。不记录它，`observe` 只能从转移里读 `previousAction`，
/// 而那个字段是"上一次 observe 的动作"，与"本次选择时的前驱"在交错场景下不同。
/// **`pending` carries the predecessor from `select` to `observe`.** Without it, `observe` can only read the transition's
/// `previousAction`, which is "the action of the previous observation" — different from "the predecessor at selection time" whenever
/// selections and observations interleave.
/// **同一决策号重复 `select` 必须报错**。允许覆盖会让前一次派发的前驱被静默丢弃，而后续 `observe` 仍能成功、只是记错了配对。
/// **Selecting twice for the same decision must fail.** Allowing overwrite silently drops the earlier dispatch's predecessor, and the
/// Following
/// **`adaptive-pursuit` 在全更新率（`rate == 1`）时直接取目标值**。理由写的是"避免消去误差跌破概率下界"，但在双精度下
/// `p + 1.0*(target - p)` 与 `target` 算术上完全相同——变异测试删掉这条捷径后测试**仍然通过**，是**等价变异**。
/// 保留它是因为它与原式在双精度下等价；把它说成"防止了数值下溢"会是假的。
/// **`adaptive-pursuit` assigns the target directly at full rate (`rate == 1`).** Going through `p + rate*(target - p)` can dip below
/// the probability floor through cancellation error while the probabilities still sum to roughly 1, so it goes undetected.
/// **恢复时的校验比常规实现更严**：概率和必须在 `1e-9` 内、每个概率必须落在 `[minimumProbability, 1]`、
/// `lastUsed` 不得超过 `clock`。放宽任何一条都会让一份"看起来合理"的损坏快照被接受。
/// **Restore validation is stricter than usual**: the probability sum must be within `1e-9`, each probability must lie in
/// `[minimumProbability, 1]`, and `lastUsed` must not exceed `clock`. Loosening any one of these accepts a plausible-looking corrupt
/// snapshot.
/// </remarks>
public class NonStationarySelector : ISelectionPolicy {
    private readonly List<string> _actions;
    private readonly List<CreditObservation> _window = new();
    private Dictionary<string, double> _quality;
    private Dictionary<string, Dictionary<string, double>> _pairs;
    private Dictionary<string, double> _probabilities;
    private Dictionary<string, long> _lastUsed;
    private string? _previous;
    private Dictionary<long, CreditDispatch> _pending = new();
    private long _clock;

    /// <summary>
    /// 构造选择器 / Construct the selector.
    /// </summary>
    /// <param name="type">选择器类型 / The selector type.</param>
    /// <param name="actions">动作列表 / The action list.</param>
    /// <param name="learning">是否学习 / Whether it learns.</param>
    /// <param name="windowSize">窗口大小 / The window size.</param>
    /// <param name="exploration">探索系数 / The exploration coefficient.</param>
    /// <param name="decay">衰减系数 / The decay coefficient.</param>
    /// <param name="rate">更新率 / The update rate.</param>
    /// <param name="minimumProbability">概率下界 / The probability floor.</param>
    /// <param name="recencyWeight">新近权重 / The recency weight.</param>
    public NonStationarySelector(
        string type,
        IReadOnlyList<string> actions,
        bool learning = true,
        int windowSize = 50,
        double exploration = 0.5,
        double decay = 0.8,
        double rate = 0.2,
        double minimumProbability = 0.01,
        double recencyWeight = 0.1) {
        ArgumentNullException.ThrowIfNull(actions);
        Type = type;
        _actions = new List<string>(actions);
        Learning = learning;
        WindowSize = windowSize;
        Exploration = exploration;
        Decay = decay;
        Rate = rate;
        MinimumProbability = minimumProbability;
        RecencyWeight = recencyWeight;

        double initial = 1.0 / global::System.Math.Max(_actions.Count, 1);
        _quality = new Dictionary<string, double>(StringComparer.Ordinal);
        _probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
        _lastUsed = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string action in _actions) {
            _quality[action] = 0.0;
            _probabilities[action] = initial;
            _lastUsed[action] = 0L;
        }
        _pairs = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
    }

    /// <summary>选择器类型 / The selector type.</summary>
    public string Type { get; }

    /// <summary>动作列表 / The action list.</summary>
    public IReadOnlyList<string> Actions => _actions;

    /// <summary>是否学习 / Whether it learns.</summary>
    public bool Learning { get; }

    /// <summary>窗口大小 / The window size.</summary>
    public int WindowSize { get; }

    /// <summary>探索系数 / The exploration coefficient.</summary>
    public double Exploration { get; }

    /// <summary>衰减系数 / The decay coefficient.</summary>
    public double Decay { get; }

    /// <summary>更新率 / The update rate.</summary>
    public double Rate { get; }

    /// <summary>概率下界 / The probability floor.</summary>
    public double MinimumProbability { get; }

    /// <summary>新近权重 / The recency weight.</summary>
    public double RecencyWeight { get; }

    /// <inheritdoc/>
    public long Revision { get; private set; }

    /// <inheritdoc/>
    public int? StateCount => null;

    /// <summary>
    /// 校验配置 / Validate the configuration.
    /// </summary>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Validate() {
        bool knownType = Type == "frr-mab" || Type == "choice-function" || Type == "adaptive-pursuit";
        if (!knownType || _actions.Count == 0 || WindowSize < 1 || WindowSize > 1_000_000) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary selector parameters");
        }
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        foreach (string action in _actions) {
            if (string.IsNullOrWhiteSpace(action) || !distinct.Add(action)) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary selector parameters");
            }
        }
        if (!double.IsFinite(Exploration) || !double.IsFinite(Decay) || !double.IsFinite(Rate)
            || !double.IsFinite(MinimumProbability) || !double.IsFinite(RecencyWeight)
            || Exploration < 0.0 || Decay < 0.0 || Decay > 1.0
            || Rate <= 0.0 || Rate > 1.0 || MinimumProbability < 0.0
            || MinimumProbability * _actions.Count > 1.0 || RecencyWeight < 0.0
            || (Type == "frr-mab" && WindowSize < _actions.Count)) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary selector parameters");
        }
        return Results.Ok(new Success());
    }

    /// <inheritdoc/>
    public Result<string, ErrorCode, Error<ErrorCode>> Select(
        SelectionContext context, IReadOnlyList<string> available, SearchRandom random) {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(available);
        ArgumentNullException.ThrowIfNull(random);
        Result<Success, ErrorCode, Error<ErrorCode>> valid = Validate();
        if (valid is Failed<Success, ErrorCode, Error<ErrorCode>> invalid) {
            return Results.Failed<string>(invalid.Error);
        }
        if (available.Count == 0 || context.Decision < 0) {
            return SearchCodecSupport.SearchInvalid<string>("invalid non-stationary action mask/context");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var mask = new List<string>(available.Count);
        foreach (string action in available) {
            if (!seen.Add(action) || !Contains(_actions, action)) {
                return SearchCodecSupport.SearchInvalid<string>("invalid non-stationary action mask/context");
            }
            mask.Add(action);
        }
        // **按 `actions` 的原序过滤掩码**，而不是按 `available` 的顺序，即 `actions.filter { it in available }`；
        // 用 `available` 的顺序会让 `maxBy` 在并列时选出不同动作，而两种顺序下的分数完全相同。
        // **The mask is filtered in `actions` order**, not `available` order. The code writes `actions.filter { it in available }`;
        // using `available` order makes `maxBy` pick a different action on ties while both orders have identical scores.
        var filtered = new List<string>(_actions.Count);
        foreach (string action in _actions) {
            if (seen.Contains(action)) {
                filtered.Add(action);
            }
        }

        string chosen;
        if (Type == "frr-mab") {
            chosen = SelectFrrMab(filtered, random);
        } else if (Type == "choice-function") {
            chosen = SelectChoiceFunction(filtered);
        } else {
            chosen = SelectAdaptivePursuit(filtered, random);
        }

        if (Learning && Type == "choice-function") {
            if (_pending.ContainsKey(context.Decision)) {
                return SearchCodecSupport.SearchInvalid<string>(
                    $"decision already awaiting feedback: {context.Decision}");
            }
            _pending[context.Decision] = new CreditDispatch(chosen, _previous);
        }
        return Results.Ok(chosen);
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Observe(SelectionTransition transition) {
        ArgumentNullException.ThrowIfNull(transition);
        if (!Contains(_actions, transition.Action) || !Contains(transition.Available, transition.Action)) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid credit observation");
        }
        foreach (string candidate in transition.Available) {
            if (!Contains(_actions, candidate)) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid credit observation");
            }
        }
        foreach (string candidate in transition.NextAvailable) {
            if (!Contains(_actions, candidate)) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid credit observation");
            }
        }
        if (!double.IsFinite(transition.Reward.ToDouble())
            || transition.Evaluations < 0 || transition.ElapsedNanos < 0) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid credit observation");
        }
        if (transition.PreviousAction is not null && !Contains(_actions, transition.PreviousAction)) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid credit observation");
        }
        if (_pending.TryGetValue(transition.Decision, out CreditDispatch? dispatch)
            && !string.Equals(dispatch.Action, transition.Action, StringComparison.Ordinal)) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid credit observation");
        }

        if (!Learning) {
            return Results.Ok(new Success());
        }
        string action = transition.Action;
        double value = transition.Reward.ToDouble();
        string? predecessor = _pending.TryGetValue(transition.Decision, out CreditDispatch? recorded)
            ? recorded.Previous
            : transition.PreviousAction;
        _pending.Remove(transition.Decision);

        _window.Add(new CreditObservation(action, value));
        while (_window.Count > WindowSize) {
            _window.RemoveAt(0);
        }
        _quality[action] = ((1.0 - Rate) * _quality[action]) + (Rate * value);
        if (predecessor is not null) {
            if (!_pairs.TryGetValue(predecessor, out Dictionary<string, double>? row)) {
                row = new Dictionary<string, double>(StringComparer.Ordinal);
                _pairs[predecessor] = row;
            }
            double old = row.TryGetValue(action, out double previous) ? previous : 0.0;
            row[action] = ((1.0 - Rate) * old) + (Rate * value);
        }
        if (Type == "adaptive-pursuit") {
            string winner = _actions[0];
            foreach (string candidate in _actions) {
                if (_quality[candidate] > _quality[winner]) {
                    winner = candidate;
                }
            }
            foreach (string id in _actions) {
                double target = string.Equals(id, winner, StringComparison.Ordinal)
                    ? 1.0 - (MinimumProbability * (_actions.Count - 1))
                    : MinimumProbability;
                // 全更新率直接取目标，避免消去误差跌破概率下界。
                // Full-rate updates take the target directly to avoid cancellation below the probability floor.
                _probabilities[id] = Rate == 1.0
                    ? target
                    : _probabilities[id] + (Rate * (target - _probabilities[id]));
            }
        }
        _clock++;
        _lastUsed[action] = _clock;
        _previous = transition.Terminal ? null : action;
        Revision++;
        return Results.Ok(new Success());
    }

    /// <inheritdoc/>
    public void Discard(long decision) => _pending.Remove(decision);

    /// <inheritdoc/>
    public string? Predecessor(long decision) =>
        _pending.TryGetValue(decision, out CreditDispatch? dispatch) ? dispatch.Previous : null;

    /// <inheritdoc/>
    public PolicySnapshot Snapshot() {
        var definition = new List<double> {
            WindowSize, Exploration, Decay, Rate, MinimumProbability, RecencyWeight,
        };
        var pairs = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, Dictionary<string, double>> pair in _pairs) {
            pairs[pair.Key] = new Dictionary<string, double>(pair.Value, StringComparer.Ordinal);
        }
        var state = new CreditState(
            definition, new List<CreditObservation>(_window),
            new Dictionary<string, double>(_quality, StringComparer.Ordinal),
            pairs,
            new Dictionary<string, double>(_probabilities, StringComparer.Ordinal),
            new Dictionary<string, long>(_lastUsed, StringComparer.Ordinal),
            _previous, new Dictionary<long, CreditDispatch>(_pending), _clock);
        JsonElement extension = JsonSerializer.SerializeToElement(state);
        return new PolicySnapshot(1, Type, new List<string>(_actions), Revision, 0L, null, null, null, extension);
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Restore(PolicySnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version != 1 || !string.Equals(snapshot.Type, Type, StringComparison.Ordinal)
            || snapshot.Actions.Count != _actions.Count || snapshot.Revision < 0) {
            return SearchCodecSupport.SearchInvalid<Success>("incompatible non-stationary policy snapshot");
        }
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (string action in snapshot.Actions) {
            if (!declared.Add(action)) {
                return SearchCodecSupport.SearchInvalid<Success>("incompatible non-stationary policy snapshot");
            }
        }
        if (!declared.SetEquals(new HashSet<string>(_actions, StringComparer.Ordinal))) {
            return SearchCodecSupport.SearchInvalid<Success>("incompatible non-stationary policy snapshot");
        }

        Result<CreditState, ErrorCode, Error<ErrorCode>> decoded =
            SearchBoundary.Execute(() => JsonSerializer.Deserialize<CreditState>(snapshot.Extension.GetRawText())!);
        if (decoded is Failed<CreditState, ErrorCode, Error<ErrorCode>> decodeFailed) {
            return Results.Failed<Success>(decodeFailed.Error);
        }
        CreditState saved = ((Ok<CreditState, ErrorCode, Error<ErrorCode>>)decoded).Value;

        var definition = new List<double> {
            WindowSize, Exploration, Decay, Rate, MinimumProbability, RecencyWeight,
        };
        if (!DefinitionsEqual(saved.Definition, definition)) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
        }
        if (saved.Clock < 0 || saved.Window.Count > WindowSize) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
        }
        foreach (CreditObservation observation in saved.Window) {
            if (!Contains(_actions, observation.Action) || !double.IsFinite(observation.Reward)) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
            }
        }
        if (!KeysMatch(saved.Quality) || !KeysMatch(saved.Probabilities)
            || !KeysMatch(saved.LastUsed)) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
        }
        foreach (double quality in saved.Quality.Values) {
            if (!double.IsFinite(quality)) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
            }
        }
        foreach (long used in saved.LastUsed.Values) {
            if (used < 0 || used > saved.Clock) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
            }
        }
        double sum = 0.0;
        foreach (double probability in saved.Probabilities.Values) {
            if (!double.IsFinite(probability)
                || probability < MinimumProbability || probability > 1.0) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
            }
            sum += probability;
        }
        if (global::System.Math.Abs(sum - 1.0) > 1.0e-9) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
        }
        foreach (KeyValuePair<string, IReadOnlyDictionary<string, double>> pair in saved.Pairs) {
            if (!Contains(_actions, pair.Key)) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
            }
            foreach (KeyValuePair<string, double> row in pair.Value) {
                if (!Contains(_actions, row.Key) || !double.IsFinite(row.Value)) {
                    return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
                }
            }
        }
        if (saved.Previous is not null && !Contains(_actions, saved.Previous)) {
            return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
        }
        foreach (KeyValuePair<long, CreditDispatch> pair in saved.Pending) {
            if (pair.Key < 0 || !Contains(_actions, pair.Value.Action)
                || (pair.Value.Previous is not null && !Contains(_actions, pair.Value.Previous))) {
                return SearchCodecSupport.SearchInvalid<Success>("invalid non-stationary credit state");
            }
        }

        _window.Clear();
        _window.AddRange(saved.Window);
        _quality = new Dictionary<string, double>(saved.Quality, StringComparer.Ordinal);
        _pairs = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, IReadOnlyDictionary<string, double>> pair in saved.Pairs) {
            _pairs[pair.Key] = new Dictionary<string, double>(pair.Value, StringComparer.Ordinal);
        }
        _probabilities = new Dictionary<string, double>(saved.Probabilities, StringComparer.Ordinal);
        _lastUsed = new Dictionary<string, long>(saved.LastUsed, StringComparer.Ordinal);
        _previous = saved.Previous;
        _pending = new Dictionary<long, CreditDispatch>(saved.Pending);
        _clock = saved.Clock;
        Revision = snapshot.Revision;
        return Results.Ok(new Success());
    }

    /// <summary>
    /// `frr-mab` 的选择 / The `frr-mab` selection.
    /// </summary>
    /// <param name="mask">掩码 / The mask.</param>
    /// <param name="random">随机数源 / The randomness source.</param>
    /// <returns>动作名 / The action name.</returns>
    private string SelectFrrMab(IReadOnlyList<string> mask, SearchRandom random) {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string action in _actions) {
            counts[action] = 0L;
        }
        double maximum = 0.0;
        foreach (CreditObservation observation in _window) {
            counts[observation.Action]++;
            double clamped = global::System.Math.Max(observation.Reward, 0.0);
            if (clamped > maximum) {
                maximum = clamped;
            }
        }
        double scale = maximum == 0.0 ? 1.0 : maximum;
        var totals = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string action in _actions) {
            totals[action] = 0.0;
        }
        foreach (CreditObservation observation in _window) {
            totals[observation.Action] += global::System.Math.Max(observation.Reward, 0.0) / scale;
        }

        var ranking = new List<string>(_actions);
        ranking.Sort((left, right) => totals[right].CompareTo(totals[left]));
        var credits = new Dictionary<string, double>(StringComparer.Ordinal);
        for (int rank = 0; rank < ranking.Count; rank++) {
            string action = ranking[rank];
            credits[action] = totals[action] > 0.0
                ? global::System.Math.Pow(Decay, rank) * (_actions.Count - rank)
                : 0.0;
        }
        double total = 0.0;
        foreach (double credit in credits.Values) {
            total += credit;
        }
        if (total < 1.0) {
            total = 1.0;
        }

        if (Learning) {
            foreach (string action in mask) {
                if (counts[action] == 0L) {
                    return action;
                }
            }
            string best = mask[0];
            double bestScore = double.NegativeInfinity;
            double windowScale = global::System.Math.Log(
                global::System.Math.Max(_window.Count, 1) + 1.0);
            foreach (string action in mask) {
                double score = (credits[action] / total)
                    + (Exploration * global::System.Math.Sqrt(
                        2.0 * windowScale / counts[action]));
                if (score > bestScore) {
                    bestScore = score;
                    best = action;
                }
            }
            return best;
        }
        string plainBest = mask[0];
        foreach (string action in mask) {
            if (credits[action] > credits[plainBest]) {
                plainBest = action;
            }
        }
        return plainBest;
    }

    /// <summary>
    /// `choice-function` 的选择 / The `choice-function` selection.
    /// </summary>
    /// <param name="mask">掩码 / The mask.</param>
    /// <returns>动作名 / The action name.</returns>
    private string SelectChoiceFunction(IReadOnlyList<string> mask) {
        Dictionary<string, double> pair = _previous is not null && _pairs.TryGetValue(_previous, out Dictionary<string, double>? row)
            ? row
            : new Dictionary<string, double>(StringComparer.Ordinal);
        string best = mask[0];
        double bestScore = double.NegativeInfinity;
        foreach (string action in mask) {
            double score = Normalized(_quality, action) + Normalized(pair, action);
            if (Learning) {
                score += RecencyWeight * (_clock - _lastUsed[action]);
            }
            if (score > bestScore) {
                bestScore = score;
                best = action;
            }
        }
        return best;
    }

    /// <summary>
    /// `adaptive-pursuit` 的选择 / The `adaptive-pursuit` selection.
    /// </summary>
    /// <param name="mask">掩码 / The mask.</param>
    /// <param name="random">随机数源 / The randomness source.</param>
    /// <returns>动作名 / The action name.</returns>
    private string SelectAdaptivePursuit(IReadOnlyList<string> mask, SearchRandom random) {
        double total = 0.0;
        foreach (string action in mask) {
            total += _probabilities[action];
        }
        double draw = random.NextDouble() * total;
        double cumulative = 0.0;
        foreach (string action in mask) {
            cumulative += _probabilities[action];
            if (cumulative > draw) {
                return action;
            }
        }
        return mask[^1];
    }

    /// <summary>
    /// 按映射内的绝对值最大者归一 / Normalize by the map's largest absolute value.
    /// </summary>
    /// <param name="values">映射 / The map.</param>
    /// <param name="action">动作 / The action.</param>
    /// <returns>归一值 / The normalized value.</returns>
    private static double Normalized(IReadOnlyDictionary<string, double> values, string action) {
        double scale = 0.0;
        foreach (double value in values.Values) {
            double magnitude = global::System.Math.Abs(value);
            if (magnitude > scale) {
                scale = magnitude;
            }
        }
        if (scale == 0.0) {
            return 0.0;
        }
        return values.TryGetValue(action, out double found) ? found / scale : 0.0;
    }

    /// <summary>
    /// 两个定义向量是否逐项相等 / Whether two definition vectors match item by item.
    /// </summary>
    /// <param name="left">左向量 / The left vector.</param>
    /// <param name="right">右向量 / The right vector.</param>
    /// <returns>是否相等 / Whether they are equal.</returns>
    private static bool DefinitionsEqual(IReadOnlyList<double> left, IReadOnlyList<double> right) {
        if (left.Count != right.Count) {
            return false;
        }
        for (int index = 0; index < left.Count; index++) {
            if (left[index] != right[index]) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 映射的键集是否与动作集一致 / Whether a map's keys match the action set.
    /// </summary>
    /// <param name="map">映射 / The map.</param>
    /// <returns>是否一致 / Whether they match.</returns>
    private bool KeysMatch<TValue>(IReadOnlyDictionary<string, TValue> map) {
        if (map.Count != _actions.Count) {
            return false;
        }
        foreach (string action in _actions) {
            if (!map.ContainsKey(action)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 列表是否包含某字符串 / Whether a list contains a string.
    /// </summary>
    /// <param name="list">列表 / The list.</param>
    /// <param name="value">字符串 / The string.</param>
    /// <returns>是否包含 / Whether it is contained.</returns>
    private static bool Contains(IReadOnlyList<string> list, string value) {
        for (int index = 0; index < list.Count; index++) {
            if (string.Equals(list[index], value, StringComparison.Ordinal)) {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// 滑动窗口 UCB 选择器 / The sliding-window UCB selector.
/// </summary>
/// <param name="actions">动作列表 / The action list.</param>
/// <param name="learning">是否学习 / Whether it learns.</param>
/// <param name="windowSize">窗口大小 / The window size.</param>
public sealed class FrrMabSelector(
    IReadOnlyList<string> actions, bool learning = true, int windowSize = 50)
    : NonStationarySelector("frr-mab", actions, learning, windowSize);

/// <summary>
/// 选择函数选择器 / The choice-function selector.
/// </summary>
/// <param name="actions">动作列表 / The action list.</param>
/// <param name="learning">是否学习 / Whether it learns.</param>
public sealed class ChoiceFunctionSelector(IReadOnlyList<string> actions, bool learning = true)
    : NonStationarySelector("choice-function", actions, learning);

/// <summary>
/// 自适应追踪选择器 / The adaptive-pursuit selector.
/// </summary>
/// <param name="actions">动作列表 / The action list.</param>
/// <param name="learning">是否学习 / Whether it learns.</param>
public sealed class AdaptivePursuitSelector(IReadOnlyList<string> actions, bool learning = true)
    : NonStationarySelector("adaptive-pursuit", actions, learning);
