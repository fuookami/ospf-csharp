// 强化学习算子 / Reinforcement-learning operators.
// 与`operator/rl/` 下的、
//、 对应（README 与基线测试不移植）。
// ,,,,,
// and (the READMEs and the code test file are not implemented).

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Operator.Rl;

/// <summary>
/// 奖励算子 / A reward operator.
/// </summary>
/// <remarks>
/// **奖励是 Q 学习与具体算法之间唯一的耦合点**：换奖励不必改 Q 表与选择器。把它做成策略对象而不是硬编码的 `±1`，
/// 是为了让"变好多少算好"成为可配置的判据，而不是埋在选择器里的假设。
/// **The reward is the only coupling between Q-learning and a concrete algorithm**: swapping rewards never touches the table or the
/// selector. Making it a strategy object instead of a hard-coded `±1` turns "how much improvement counts as good" into a configurable
/// criterion rather than an assumption buried in the selector.
/// </remarks>
public interface IReward {
    /// <summary>
    /// 计算奖励 / Compute the reward.
    /// </summary>
    /// <param name="improved">档案最优是否被改善 / Whether the archive best improved.</param>
    /// <returns>奖励值 / The reward value.</returns>
    Flt64 Invoke(bool improved);
}

/// <summary>
/// 改进奖励：变好给正奖励，变差给负奖励 / Improvement reward: positive when improved, negative otherwise.
/// </summary>
/// <param name="Positive">改进时的奖励 / The reward when improved.</param>
/// <param name="Negative">未改进时的奖励 / The reward when not improved.</param>
public sealed record ImprovementReward(Flt64 Positive, Flt64 Negative) : IReward {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public ImprovementReward() : this(Flt64.One, new Flt64(-0.5)) { }

    /// <inheritdoc/>
    public Flt64 Invoke(bool improved) => improved ? Positive : Negative;
}

/// <summary>
/// 对称奖励：改进给 `magnitude`，未改进给它的相反数 / Symmetric reward: `magnitude` when improved, its negation otherwise.
/// </summary>
/// <param name="Magnitude">奖励幅度 / The reward magnitude.</param>
public sealed record SymmetricReward(Flt64 Magnitude) : IReward {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public SymmetricReward() : this(Flt64.One) { }

    /// <inheritdoc/>
    public Flt64 Invoke(bool improved) => improved ? Magnitude : -Magnitude;
}

/// <summary>
/// Q 表 / The Q table.
/// </summary>
/// <remarks>
/// **下一状态的最大动作价值，而非当前状态自身的最大值**。写成"当前状态的最大值"会让自举项退化为常量，
/// **Q 值单调增长但策略完全不更新**——而每次迭代的奖励与更新调用都正常执行，看不出任何异常。这是 Q 学习最经典的实现错误。
/// **The maximum over the next state, not over the current one.** Taking the current state's maximum degenerates the bootstrap term
/// into a constant: **Q values grow monotonically while the policy never updates** — and every reward and update call still runs
/// normally, so nothing looks wrong. This is the classic Q-learning implementation error.
/// **`terminal` 时自举项必须取 0**。终局状态没有后继，仍取 `max(nextState)` 会让终局奖励被错误地放大。
/// **The bootstrap term must be 0 for `terminal`.** A terminal state has no successor; still taking `max(nextState)` inflates terminal
/// rewards.
/// **动作掩码同时作用于自举项与合法性校验**。只在采样时过滤掩码、却让自举项看全部动作，会让被屏蔽的高价值动作
/// **通过自举项重新进入策略**，掩码形同虚设。
/// **The action mask applies to both the bootstrap term and validation.** Filtering the mask only at sampling time while letting the
/// bootstrap term see all actions lets masked-out high-value actions **re-enter the policy through the bootstrap term**, making the
/// mask useless.
/// **形状与数值校验必须在写入前完成**。先写入再校验会让非法快照留下一半已污染的表，而调用方拿到的只是一个失败结果。
/// **Shape and value validation must complete before writing.** Writing first and validating later leaves a half-corrupted table
/// behind an illegal snapshot while the caller only sees a failure.
/// </remarks>
public sealed class QTable {
    private readonly Flt64[][] _entries;

    /// <summary>
    /// 构造 Q 表 / Construct the Q table.
    /// </summary>
    /// <param name="stateAmount">状态数量 / The state amount.</param>
    /// <param name="actionAmount">动作数量 / The action amount.</param>
    public QTable(int stateAmount, int actionAmount) {
        StateAmount = stateAmount;
        ActionAmount = actionAmount;
        int states = global::System.Math.Max(stateAmount, 0);
        int actions = global::System.Math.Max(actionAmount, 0);
        _entries = new Flt64[states][];
        for (int state = 0; state < states; state++) {
            _entries[state] = new Flt64[actions];
            for (int action = 0; action < actions; action++) {
                _entries[state][action] = Flt64.Zero;
            }
        }
    }

    /// <summary>状态数量 / The state amount.</summary>
    public int StateAmount { get; }

    /// <summary>动作数量 / The action amount.</summary>
    public int ActionAmount { get; }

    /// <summary>
    /// 取某状态的全部动作价值 / Read every action value of a state.
    /// </summary>
    /// <param name="state">状态下标 / The state index.</param>
    /// <returns>动作价值 / The action values.</returns>
    /// <remarks>
    /// **返回的是拷贝而不是活视图**。基线写 `entries[state].toList`；返回活视图会让调用方持有的"价值快照"在后续更新后
    /// 静默变化，而选择器读到的值与它决策时的值不再一致。
    /// **A copy is returned, not a live view.** The code writes `entries[state].toList`; a live view makes the caller's "value
    /// snapshot" change silently after later updates, so the selector reads values that no longer match what it decided on.
    /// </remarks>
    public IReadOnlyList<Flt64> ValuesOf(int state) {
        if (state < 0 || state >= StateAmount) {
            return Array.Empty<Flt64>();
        }
        var cloned = new Flt64[_entries[state].Length];
        Array.Copy(_entries[state], cloned, cloned.Length);
        return Array.AsReadOnly(cloned);
    }

    /// <summary>
    /// 取单个动作价值 / Read a single action value.
    /// </summary>
    /// <param name="state">状态下标 / The state index.</param>
    /// <param name="action">动作下标 / The action index.</param>
    /// <returns>动作价值 / The action value.</returns>
    public Flt64 ValueOf(int state, int action) =>
        state >= 0 && state < StateAmount && action >= 0 && action < ActionAmount
            ? _entries[state][action]
            : Flt64.Zero;

    /// <summary>
    /// 取该状态的最优动作 / The best action of a state.
    /// </summary>
    /// <param name="state">状态下标 / The state index.</param>
    /// <returns>最优动作 / The best action.</returns>
    public int BestAction(int state) {
        if (state < 0 || state >= StateAmount || ActionAmount <= 0) {
            return 0;
        }
        int best = 0;
        Flt64 bestValue = _entries[state][0];
        for (int action = 1; action < ActionAmount; action++) {
            if (_entries[state][action].CompareTo(bestValue) > 0) {
                bestValue = _entries[state][action];
                best = action;
            }
        }
        return best;
    }

    /// <summary>
    /// 执行一次 Q 学习更新 / Perform one Q-learning update.
    /// </summary>
    /// <param name="state">当前状态 / The current state.</param>
    /// <param name="action">当前动作 / The current action.</param>
    /// <param name="reward">奖励 / The reward.</param>
    /// <param name="nextState">下一状态 / The next state.</param>
    /// <param name="alpha">学习率 / The learning rate.</param>
    /// <param name="gamma">折扣因子 / The discount factor.</param>
    /// <param name="terminal">是否终局 / Whether it is terminal.</param>
    /// <param name="availableActions">动作掩码 / The action mask.</param>
    /// <returns>更新后的 Q 值 / The updated Q value.</returns>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> Update(
        int state,
        int action,
        Flt64 reward,
        int nextState,
        Flt64 alpha,
        Flt64 gamma,
        bool terminal = false,
        IReadOnlyList<int>? availableActions = null) {
        if (state < 0 || state >= StateAmount) {
            return Results.Failed<Flt64>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, $"state out of range: {state} / 状态越界：{state}"));
        }
        if (action < 0 || action >= ActionAmount) {
            return Results.Failed<Flt64>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, $"action out of range: {action} / 动作越界：{action}"));
        }
        if (nextState < 0 || nextState >= StateAmount) {
            return Results.Failed<Flt64>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"nextState out of range: {nextState} / 下一状态越界：{nextState}"));
        }
        double alphaValue = alpha.ToDouble();
        if (!double.IsFinite(alphaValue) || alphaValue < 0.0 || alphaValue > 1.0) {
            return Results.Failed<Flt64>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"alpha must lie in [0, 1], got {alphaValue} / 学习率须位于 [0, 1]"));
        }
        double gammaValue = gamma.ToDouble();
        if (!double.IsFinite(gammaValue) || gammaValue < 0.0 || gammaValue > 1.0) {
            return Results.Failed<Flt64>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"gamma must lie in [0, 1], got {gammaValue} / 折扣因子须位于 [0, 1]"));
        }
        if (!double.IsFinite(reward.ToDouble()) || HasOutOfRangeAction(availableActions)) {
            return Results.Failed<Flt64>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "奖励或动作掩码无效 / Invalid reward or action mask"));
        }

        Flt64 nextBest = Flt64.Zero;
        if (!terminal) {
            IReadOnlyList<int> mask = availableActions ?? AllActions();
            for (int index = 0; index < mask.Count; index++) {
                Flt64 candidate = _entries[nextState][mask[index]];
                if (index == 0 || candidate.CompareTo(nextBest) > 0) {
                    nextBest = candidate;
                }
            }
        }
        Flt64 target = reward + (gamma * nextBest);
        Flt64 current = _entries[state][action];
        Flt64 updated = current + (alpha * (target - current));
        _entries[state][action] = updated;
        return Results.Ok(updated);
    }

    /// <summary>
    /// 快照当前表 / Snapshot the current table.
    /// </summary>
    /// <returns>表的副本 / A copy of the table.</returns>
    public IReadOnlyList<IReadOnlyList<Flt64>> Snapshot() {
        // **必须逐行拷贝**。`Array.AsReadOnly(row)` 只是对内部数组的活视图，`Clear` 或后续更新会同步改掉"快照"，
        // 于是恢复出来的仍是清空后的表——而快照调用本身返回成功，看不出任何异常。的是 `it.toList`，即拷贝。
        // **Each row must be copied.** `Array.AsReadOnly(row)` is only a live view over the internal array, so `Clear` or any later
        // update mutates the "snapshot" too and the restore yields the cleared table — while the snapshot call itself succeeds and
        // nothing looks wrong. The code uses `it.toList`, i.e. a copy.
        var copy = new List<IReadOnlyList<Flt64>>(_entries.Length);
        foreach (Flt64[] row in _entries) {
            var cloned = new Flt64[row.Length];
            Array.Copy(row, cloned, row.Length);
            copy.Add(Array.AsReadOnly(cloned));
        }
        return copy;
    }

    /// <summary>
    /// 从快照恢复 / Restore from a snapshot.
    /// </summary>
    /// <param name="values">快照 / The snapshot.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Restore(IReadOnlyList<IReadOnlyList<Flt64>> values) {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != StateAmount) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "Q 表形状或数值无效 / Invalid Q table shape or values"));
        }
        for (int state = 0; state < values.Count; state++) {
            if (values[state].Count != ActionAmount) {
                return Results.Failed<Success>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "Q 表形状或数值无效 / Invalid Q table shape or values"));
            }
            for (int action = 0; action < values[state].Count; action++) {
                if (!double.IsFinite(values[state][action].ToDouble())) {
                    return Results.Failed<Success>(
                        new Err<ErrorCode>(ErrorCode.IllegalArgument, "Q 表形状或数值无效 / Invalid Q table shape or values"));
                }
            }
        }
        for (int state = 0; state < _entries.Length; state++) {
            for (int action = 0; action < _entries[state].Length; action++) {
                _entries[state][action] = values[state][action];
            }
        }
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 清空表 / Clear the table.
    /// </summary>
    public void Clear() {
        for (int state = 0; state < _entries.Length; state++) {
            for (int action = 0; action < _entries[state].Length; action++) {
                _entries[state][action] = Flt64.Zero;
            }
        }
    }

    /// <summary>全部动作下标 / Every action index.</summary>
    /// <returns>动作下标序列 / The action-index sequence.</returns>
    private int[] AllActions() {
        int[] all = new int[ActionAmount];
        for (int action = 0; action < ActionAmount; action++) {
            all[action] = action;
        }
        return all;
    }

    /// <summary>
    /// 掩码是否含越界动作 / Whether the mask contains an out-of-range action.
    /// </summary>
    /// <param name="availableActions">动作掩码 / The action mask.</param>
    /// <returns>是否越界 / Whether it is out of range.</returns>
    private bool HasOutOfRangeAction(IReadOnlyList<int>? availableActions) {
        if (availableActions is null) {
            return false;
        }
        for (int index = 0; index < availableActions.Count; index++) {
            if (availableActions[index] < 0 || availableActions[index] >= ActionAmount) {
                return true;
            }
        }
        return false;
    }
}

/// <summary>
/// 状态离散化 / State discretization.
/// </summary>
/// <remarks>
/// **离散化必须同时看"多样性"与"停滞"两个维度**。只看停滞会让算法在多样性充足时也判定为"收敛中"；
/// 只看多样性则在种群均匀铺开时误判为"探索充分"，而实际上已经很久没有改进。
/// **The discretization must observe both diversity and stagnation.** Stagnation alone marks a well-spread population as
/// "converging"; diversity alone marks a uniformly spread but long-stagnant population as "exploring well".
/// </remarks>
public interface IStateDiscretizer {
    /// <summary>状态数量 / The state amount.</summary>
    int StateAmount { get; }

    /// <summary>
    /// 计算当前状态 / Compute the current state.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="members">种群成员 / The population members.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <returns>状态下标 / The state index.</returns>
    int Invoke<V>(Iteration iteration, IReadOnlyList<IIndividual<object, V>> members, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V>;
}

/// <summary>
/// 多样性与停滞离散化 / Diversity-and-stagnation discretization.
/// </summary>
/// <remarks>
/// **多样性用"平均两两距离除以最大距离"**，归一到 `[0, 1]`，与问题的坐标尺度无关。用绝对距离会让同一个算法在
/// 大尺度问题上永远判为"高多样性"、在小尺度问题上永远判为"低多样性"，而两种情况下它的实际行为是一样的。
/// **Diversity is the mean pairwise distance divided by the maximum distance**, normalized into `[0, 1]` and independent of the
/// problem's coordinate scale. Absolute distances would mark the same algorithm as "always diverse" on large-scale problems and
/// "never diverse" on small-scale ones, although its actual behaviour is identical in both.
/// **停滞是"无改进迭代数除以总迭代数"**。用绝对次数会让早期的高频改进与后期的稀疏改进不可比。
/// **Stagnation is the no-improvement iteration count divided by the total iteration count.** Absolute counts make early frequent
/// improvements incomparable with late sparse ones.
/// **`levels &lt; 2` 或成员不足 2 个时返回 0**，因为此时两个维度都无定义；返回任何其他值都是在编造状态。
/// **With `levels &lt; 2` or fewer than 2 members it returns 0**, because both dimensions are undefined; any other value would
/// fabricate a state.
/// </remarks>
/// <param name="Levels">每个维度的分级数 / The level count per dimension.</param>
public sealed record DiversityStagnationDiscretizer(int Levels) : IStateDiscretizer {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public DiversityStagnationDiscretizer() : this(3) { }

    /// <inheritdoc/>
    public int StateAmount => Levels < 1 ? 0 : Levels * Levels;

    /// <inheritdoc/>
    public int Invoke<V>(Iteration iteration, IReadOnlyList<IIndividual<object, V>> members,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(iteration);
        ArgumentNullException.ThrowIfNull(members);
        if (Levels < 2 || members.Count < 2) {
            return 0;
        }
        int dimension = members[0].Solution.Count;
        if (dimension == 0) {
            return 0;
        }

        double accumulated = 0.0;
        double maximum = 0.0;
        long pairs = 0L;
        for (int first = 0; first < members.Count; first++) {
            for (int second = first + 1; second < members.Count; second++) {
                double squared = 0.0;
                for (int axis = 0; axis < dimension; axis++) {
                    double difference = converter.FromValue(members[first].Solution[axis]).ToDouble()
                        - converter.FromValue(members[second].Solution[axis]).ToDouble();
                    squared += difference * difference;
                }
                double distance = global::System.Math.Sqrt(squared);
                accumulated += distance;
                if (distance > maximum) {
                    maximum = distance;
                }
                pairs++;
            }
        }
        double diversity = pairs == 0L || maximum <= 0.0
            ? 0.0
            : (accumulated / pairs) / maximum;

        double total = (double)iteration.Current;
        double stagnation = total <= 0.0
            ? 0.0
            : iteration.NotBetterIteration / total;

        return (LevelOf(diversity) * Levels) + LevelOf(stagnation);
    }

    /// <summary>
    /// 把 `[0, 1]` 的取值映射到分级 / Map a `[0, 1]` value onto a level.
    /// </summary>
    /// <param name="value">取值 / The value.</param>
    /// <returns>分级下标 / The level index.</returns>
    private int LevelOf(double value) {
        double width = 1.0 / Levels;
        return global::System.Math.Clamp((int)(value / width), 0, Levels - 1);
    }
}

/// <summary>
/// 动作选择器 / An action selector.
/// </summary>
public interface IActionSelector {
    /// <summary>
    /// 选择动作 / Select an action.
    /// </summary>
    /// <param name="values">动作价值 / The action values.</param>
    /// <param name="entropy">均匀熵源 / The uniform entropy source.</param>
    /// <returns>动作下标 / The action index.</returns>
    int Invoke(IReadOnlyList<Flt64> values, Func<Flt64> entropy);
}

/// <summary>
/// 贪心动作选择器 / Greedy action selector.
/// </summary>
public sealed class GreedyActionSelector : IActionSelector {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static GreedyActionSelector Instance { get; } = new();

    /// <inheritdoc/>
    public int Invoke(IReadOnlyList<Flt64> values, Func<Flt64> entropy) => ActionSelectors.Argmax(values);
}

/// <summary>
/// ε-贪心动作选择器 / ε-greedy action selector.
/// </summary>
/// <remarks>
/// **探索时取一个独立的随机数，不能复用"是否探索"的那一次**。复用会让"探索位置"与"探索判据"强相关，
/// 于是探索总是落在低位动作上，而探索率看起来完全正确。
/// **Exploration draws an independent sample instead of reusing the "did we explore" roll.** Reusing it correlates the position with
/// the decision, so exploration always lands on low-index actions while the exploration rate looks perfectly correct.
/// </remarks>
/// <param name="Epsilon">探索率 / The exploration rate.</param>
public sealed record EpsilonGreedyActionSelector(Flt64 Epsilon) : IActionSelector {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public EpsilonGreedyActionSelector() : this(new Flt64(0.1)) { }

    /// <inheritdoc/>
    public int Invoke(IReadOnlyList<Flt64> values, Func<Flt64> entropy) {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) {
            return 0;
        }
        double epsilon = global::System.Math.Clamp(Epsilon.ToDouble(), 0.0, 1.0);
        double roll = NumericSupport.Unit(entropy);
        if (roll < epsilon) {
            double pick = NumericSupport.Unit(entropy);
            return global::System.Math.Clamp(
                (int)(pick * values.Count), 0, values.Count - 1);
        }
        return ActionSelectors.Argmax(values);
    }
}

/// <summary>
/// 动作选择辅助 / Action-selection helpers.
/// </summary>
public static class ActionSelectors {
    /// <summary>
    /// 取最大价值的动作下标 / The action index with the maximum value.
    /// </summary>
    /// <param name="values">动作价值 / The action values.</param>
    /// <returns>动作下标 / The action index.</returns>
    public static int Argmax(IReadOnlyList<Flt64> values) {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) {
            return 0;
        }
        int best = 0;
        Flt64 bestValue = values[0];
        for (int index = 1; index < values.Count; index++) {
            if (values[index].CompareTo(bestValue) > 0) {
                bestValue = values[index];
                best = index;
            }
        }
        return best;
    }
}

/// <summary>
/// 玻尔兹曼动作选择器 / Softmax action selector.
/// </summary>
/// <remarks>
/// **必须减去最大值再取指数**。不减会让 `exp` 溢出成 `∞`，而"总权重非有限就退回贪心"这一兜底会让高温下的选择
/// 静默退化成贪心——**温度参数看起来仍然生效**。
/// **Subtract the maximum before exponentiating.** Skipping it overflows `exp` to `∞`, and the "fall back to greedy when the total
/// weight is not finite" guard silently degrades high-temperature selection into greedy — **the temperature still looks active**.
/// **温度 `&lt;= 0` 直接退回贪心**，而不是产生除零或全零权重。
/// **A temperature `&lt;= 0` falls back to greedy directly** instead of dividing by zero or producing all-zero weights.
/// </remarks>
/// <param name="Temperature">温度 / The temperature.</param>
public sealed record SoftmaxActionSelector(Flt64 Temperature) : IActionSelector {
    /// <summary>以默认参数构造 / Construct with the default parameters.</summary>
    public SoftmaxActionSelector() : this(Flt64.One) { }

    /// <inheritdoc/>
    public int Invoke(IReadOnlyList<Flt64> values, Func<Flt64> entropy) {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) {
            return 0;
        }
        double temperature = Temperature.ToDouble();
        if (temperature <= 0.0) {
            return ActionSelectors.Argmax(values);
        }

        double maximum = double.NegativeInfinity;
        for (int index = 0; index < values.Count; index++) {
            double value = values[index].ToDouble();
            if (value > maximum) {
                maximum = value;
            }
        }
        double[] weights = new double[values.Count];
        double total = 0.0;
        for (int index = 0; index < values.Count; index++) {
            double shifted = values[index].ToDouble() - maximum;
            weights[index] = double.IsFinite(shifted) ? global::System.Math.Exp(shifted / temperature) : 0.0;
            total += weights[index];
        }
        if (total <= 0.0 || !double.IsFinite(total)) {
            return ActionSelectors.Argmax(values);
        }

        double roll = global::System.Math.Clamp(NumericSupport.Unit(entropy), 0.0, 1.0) * total;
        double accumulated = 0.0;
        for (int index = 0; index < weights.Length; index++) {
            accumulated += weights[index];
            if (roll <= accumulated) {
                return index;
            }
        }
        return values.Count - 1;
    }
}

/// <summary>
/// Q 学习智能体 / The Q-learning agent.
/// </summary>
/// <remarks>
/// **`step` 在首次调用时只记录状态、不更新表**（`started` 由假转真）。首次调用没有"上一状态-动作-奖励"三元组可用，
/// 强行更新会用未初始化的 `state = 0, action = 0` 污染第 0 行第 0 列。
/// **The first `step` only records the state and does not update the table** (`started` flips from false to true). The first call has
/// no previous state-action-reward triple, and updating anyway would pollute row 0 column 0 with an uninitialized `state = 0,
/// action = 0`.
/// **`alpha` 与 `gamma` 在构造时夹到 `[0, 1]`**，而 `QTable.Update` 仍会再校验一次。构造期夹取是为了让不可达分支保持不可达；
/// 表侧校验是为了防止绕过智能体直接调用表的调用方。
/// **`alpha` and `gamma` are clamped into `[0, 1]` at construction**, while `QTable.Update` validates them again. The constructor clamp
/// keeps the unreachable branch unreachable; the table-side validation protects callers that bypass the agent.
/// **表尺寸取 `max(1, ...)`**：`QTable(0, n)` 的任何写入都会越界，而智能体在这种情况下本应直接返回 `null`。
/// **The table size uses `max(1, ...)`**: any write to `QTable(0, n)` is out of range, and the agent should return `null` in that case
/// anyway.
/// </remarks>
public sealed class QlearningAgent {
    private readonly Func<Flt64> _randomGenerator;

    /// <summary>
    /// 构造智能体 / Construct the agent.
    /// </summary>
    /// <param name="discretizer">状态离散化 / The state discretizer.</param>
    /// <param name="actionAmount">动作数量 / The action amount.</param>
    /// <param name="selector">动作选择器 / The action selector.</param>
    /// <param name="reward">奖励算子 / The reward operator.</param>
    /// <param name="alpha">学习率 / The learning rate.</param>
    /// <param name="gamma">折扣因子 / The discount factor.</param>
    /// <param name="randomGenerator">均匀熵源 / The uniform entropy source.</param>
    public QlearningAgent(
        IStateDiscretizer discretizer,
        int actionAmount,
        IActionSelector selector,
        IReward reward,
        Flt64 alpha,
        Flt64 gamma,
        Func<Flt64> randomGenerator) {
        ArgumentNullException.ThrowIfNull(discretizer);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(reward);
        ArgumentNullException.ThrowIfNull(randomGenerator);
        Discretizer = discretizer;
        ActionAmount = actionAmount;
        Selector = selector;
        Reward = reward;
        Alpha = new Flt64(global::System.Math.Clamp(alpha.ToDouble(), 0.0, 1.0));
        Gamma = new Flt64(global::System.Math.Clamp(gamma.ToDouble(), 0.0, 1.0));
        Table = new QTable(
            global::System.Math.Max(discretizer.StateAmount, 1),
            global::System.Math.Max(actionAmount, 1));
        _randomGenerator = randomGenerator;
    }

    /// <summary>状态离散化 / The state discretizer.</summary>
    public IStateDiscretizer Discretizer { get; }

    /// <summary>动作数量 / The action amount.</summary>
    public int ActionAmount { get; }

    /// <summary>动作选择器 / The action selector.</summary>
    public IActionSelector Selector { get; }

    /// <summary>奖励算子 / The reward operator.</summary>
    public IReward Reward { get; }

    /// <summary>学习率 / The learning rate.</summary>
    public Flt64 Alpha { get; }

    /// <summary>折扣因子 / The discount factor.</summary>
    public Flt64 Gamma { get; }

    /// <summary>Q 表 / The Q table.</summary>
    public QTable Table { get; }

    /// <summary>当前动作 / The current action.</summary>
    public int Action { get; private set; }

    /// <summary>当前状态 / The current state.</summary>
    public int State { get; private set; }

    /// <summary>当前状态的最优动作 / The best action of the current state.</summary>
    public int BestAction => Table.BestAction(State);

    /// <summary>是否已开始 / Whether it has started.</summary>
    public bool Started { get; private set; }

    /// <summary>
    /// 重置全部状态 / Reset every state.
    /// </summary>
    public void Reset() {
        Table.Clear();
        Action = 0;
        State = 0;
        Started = false;
    }

    /// <summary>
    /// 推进一步 / Advance one step.
    /// </summary>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="members">种群成员 / The population members.</param>
    /// <param name="improved">是否有所改进 / Whether it improved.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <returns>更新后的 Q 值；未更新时为 `null` / The updated Q value, or `null` when nothing was updated.</returns>
    public Flt64? Step<V>(Iteration iteration, IReadOnlyList<IIndividual<object, V>> members, bool improved,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(iteration);
        ArgumentNullException.ThrowIfNull(members);
        if (ActionAmount <= 0 || Table.StateAmount <= 0) {
            return null;
        }

        int nextState = global::System.Math.Clamp(
            Discretizer.Invoke(iteration, members, converter), 0, Table.StateAmount - 1);
        Flt64? updated = null;
        if (Started) {
            Flt64 rewardValue = Reward.Invoke(improved);
            Result<Flt64, ErrorCode, Error<ErrorCode>> result = Table.Update(
                State, Action, rewardValue, nextState, Alpha, Gamma);
            // 参数已在构造时夹取、下标已在上下方夹取，此分支不可达；保留显式分支以避免吞掉未来改动引入的错误。
            // Unreachable today (arguments are clamped at construction and indices above); kept explicit so future changes cannot
            // silently swallow an error.
            if (result is Ok<Flt64, ErrorCode, Error<ErrorCode>> ok) {
                updated = ok.Value;
            }
        } else {
            Started = true;
        }

        State = nextState;
        Action = global::System.Math.Clamp(
            Selector.Invoke(Table.ValuesOf(State), _randomGenerator), 0, ActionAmount - 1);
        return updated;
    }
}

/// <summary>
/// 迭代观察者 / An iteration observer.
/// </summary>
public interface IIterationObserver {
    /// <summary>
    /// 观察一次迭代 / Observe one iteration.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="members">种群成员 / The population members.</param>
    /// <param name="improved">是否有所改进 / Whether it improved.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    void Observe<ObjValue, V>(Iteration iteration, IReadOnlyList<IIndividual<ObjValue, V>> members,
        bool improved, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V>;
}

/// <summary>
/// Q 学习参数源 / A Q-learning parameter source.
/// </summary>
/// <remarks>
/// **候选为空时返回 0 而不是崩溃**。空候选意味着调用方配置有误，但把错误变成异常会让整个搜索中断；
/// 返回 0 至少让轨迹可复现，错误可以在候选列表构造处暴露。
/// **An empty candidate list returns 0 instead of crashing.** Empty candidates mean the caller misconfigured something, but turning
/// that into an exception aborts the whole search; returning 0 keeps the trajectory reproducible and the error surfaces where the list
/// is built.
/// </remarks>
public sealed class QlearningSource : IParameterSource, IIterationObserver {
    /// <summary>
    /// 构造参数源 / Construct the parameter source.
    /// </summary>
    /// <param name="candidates">候选参数源 / The candidate parameter sources.</param>
    /// <param name="agent">智能体 / The agent.</param>
    public QlearningSource(IReadOnlyList<IParameterSource> candidates, QlearningAgent agent) {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(agent);
        Candidates = candidates;
        Agent = agent;
    }

    /// <summary>候选参数源 / The candidate parameter sources.</summary>
    public IReadOnlyList<IParameterSource> Candidates { get; }

    /// <summary>智能体 / The agent.</summary>
    public QlearningAgent Agent { get; }

    /// <summary>
    /// 以默认组件构造 / Construct with the default components.
    /// </summary>
    /// <param name="candidates">候选参数源 / The candidate parameter sources.</param>
    /// <param name="randomGenerator">均匀熵源 / The uniform entropy source.</param>
    /// <returns>参数源 / The parameter source.</returns>
    public static QlearningSource Create(IReadOnlyList<IParameterSource> candidates, Func<Flt64> randomGenerator) =>
        Create(candidates, randomGenerator, new DiversityStagnationDiscretizer(),
            new EpsilonGreedyActionSelector(), new ImprovementReward(),
            new Flt64(0.5), new Flt64(0.9));

    /// <summary>
    /// 以指定组件构造 / Construct with the given components.
    /// </summary>
    /// <param name="candidates">候选参数源 / The candidate parameter sources.</param>
    /// <param name="randomGenerator">均匀熵源 / The uniform entropy source.</param>
    /// <param name="discretizer">状态离散化 / The state discretizer.</param>
    /// <param name="selector">动作选择器 / The action selector.</param>
    /// <param name="reward">奖励算子 / The reward operator.</param>
    /// <param name="alpha">学习率 / The learning rate.</param>
    /// <param name="gamma">折扣因子 / The discount factor.</param>
    /// <returns>参数源 / The parameter source.</returns>
    public static QlearningSource Create(
        IReadOnlyList<IParameterSource> candidates,
        Func<Flt64> randomGenerator,
        IStateDiscretizer discretizer,
        IActionSelector selector,
        IReward reward,
        Flt64 alpha,
        Flt64 gamma) {
        ArgumentNullException.ThrowIfNull(candidates);
        return new QlearningSource(candidates, new QlearningAgent(
            discretizer, candidates.Count, selector, reward, alpha, gamma, randomGenerator));
    }

    /// <summary>
    /// 重置智能体 / Reset the agent.
    /// </summary>
    public void Reset() => Agent.Reset();

    /// <inheritdoc/>
    public Flt64 Value(Iteration iteration) {
        ArgumentNullException.ThrowIfNull(iteration);
        if (Candidates.Count == 0) {
            return Flt64.Zero;
        }
        int index = global::System.Math.Clamp(Agent.Action, 0, Candidates.Count - 1);
        return Candidates[index].Value(iteration);
    }

    /// <inheritdoc/>
    public void Observe<ObjValue, V>(Iteration iteration, IReadOnlyList<IIndividual<ObjValue, V>> members,
        bool improved, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(members);
        Agent.Step(iteration, MemberViews.Of(members), improved, converter);
    }
}

/// <summary>
/// 个体视图转换 / Individual view conversion.
/// </summary>
/// <remarks>
/// **`QlearningAgent.Step` 只需要"解向量 + 目标值"，但调用方持有的是带具体目标值类型的个体**。`Individual&lt;*, V&gt;`
/// 的通配符擦除目标值类型；C# 没有通配符，只能用视图投影把 `IIndividual&lt;ObjValue, V&gt;` 转成
/// `IIndividual&lt;object?, V&gt;`。这里不做任何数据拷贝，只换一个静态类型。
/// **`QlearningAgent.Step` only needs the solution vector and the objective value, but callers hold individuals with a concrete
/// objective type.** The code erases the objective type with the `Individual&lt;*, V&gt;` wildcard; C# has no wildcards, so a view
/// projection converts `IIndividual&lt;ObjValue, V&gt;` into `IIndividual&lt;object?, V&gt;`. No data is copied, only the static type
/// changes.
/// </remarks>
public static class MemberViews {
    /// <summary>
    /// 把带具体目标值类型的个体投影为 `object?` 目标值视图 / Project individuals with a concrete objective type onto an
    /// `object?` objective view.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="members">种群成员 / The population members.</param>
    /// <returns>视图序列 / The view sequence.</returns>
    public static IReadOnlyList<IIndividual<object, V>> Of<ObjValue, V>(IReadOnlyList<IIndividual<ObjValue, V>> members)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(members);
        var views = new List<IIndividual<object, V>>(members.Count);
        foreach (IIndividual<ObjValue, V> member in members) {
            views.Add(new MemberView<ObjValue, V>(member));
        }
        return views;
    }

    /// <summary>
    /// 目标值类型擦除的个体视图 / An individual view with the objective type erased.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    private sealed class MemberView<ObjValue, V> : IIndividual<object, V>
        where V : struct, IRealNumber<V>, INumberField<V> {
        private readonly IIndividual<ObjValue, V> _inner;

        /// <summary>
        /// 构造视图 / Construct the view.
        /// </summary>
        /// <param name="inner">被包装的个体 / The wrapped individual.</param>
        public MemberView(IIndividual<ObjValue, V> inner) => _inner = inner;

        /// <inheritdoc/>
        public IReadOnlyList<V> Solution => _inner.Solution;

        /// <inheritdoc/>
        public object Objective => _inner.Objective!;
    }
}

/// <summary>
/// Q 学习更新 / A Q-learning update.
/// </summary>
/// <remarks>
/// **被选中的候选才是真正执行的算子**，其余候选只是"可选项"。因此 `Value`/`Update` 都只按 `Agent.Action` 派发一次，
/// 而不是把全部候选都跑一遍再挑结果——后者会让未被选中的算子也消耗随机数，同一份种子完全不同。
/// **Only the selected candidate actually executes**; the rest are mere options. Both `Value` and `Update` dispatch once by
/// `Agent.Action` instead of running every candidate and picking a result — the latter would let unselected operators consume randomness
/// and change the whole trajectory under the same seed.
/// </remarks>
public sealed class QlearningUpdate : ISolutionUpdate, IIterationObserver {
    /// <summary>
    /// 构造更新算子 / Construct the update operator.
    /// </summary>
    /// <param name="candidates">候选更新算子 / The candidate update operators.</param>
    /// <param name="agent">智能体 / The agent.</param>
    public QlearningUpdate(IReadOnlyList<ISolutionUpdate> candidates, QlearningAgent agent) {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(agent);
        Candidates = candidates;
        Agent = agent;
    }

    /// <summary>候选更新算子 / The candidate update operators.</summary>
    public IReadOnlyList<ISolutionUpdate> Candidates { get; }

    /// <summary>智能体 / The agent.</summary>
    public QlearningAgent Agent { get; }

    /// <summary>
    /// 以默认组件构造 / Construct with the default components.
    /// </summary>
    /// <param name="candidates">候选更新算子 / The candidate update operators.</param>
    /// <param name="randomGenerator">均匀熵源 / The uniform entropy source.</param>
    /// <returns>更新算子 / The update operator.</returns>
    public static QlearningUpdate Create(IReadOnlyList<ISolutionUpdate> candidates, Func<Flt64> randomGenerator) =>
        Create(candidates, randomGenerator, new DiversityStagnationDiscretizer(),
            new EpsilonGreedyActionSelector(), new ImprovementReward(),
            new Flt64(0.5), new Flt64(0.9));

    /// <summary>
    /// 以指定组件构造 / Construct with the given components.
    /// </summary>
    /// <param name="candidates">候选更新算子 / The candidate update operators.</param>
    /// <param name="randomGenerator">均匀熵源 / The uniform entropy source.</param>
    /// <param name="discretizer">状态离散化 / The state discretizer.</param>
    /// <param name="selector">动作选择器 / The action selector.</param>
    /// <param name="reward">奖励算子 / The reward operator.</param>
    /// <param name="alpha">学习率 / The learning rate.</param>
    /// <param name="gamma">折扣因子 / The discount factor.</param>
    /// <returns>更新算子 / The update operator.</returns>
    public static QlearningUpdate Create(
        IReadOnlyList<ISolutionUpdate> candidates,
        Func<Flt64> randomGenerator,
        IStateDiscretizer discretizer,
        IActionSelector selector,
        IReward reward,
        Flt64 alpha,
        Flt64 gamma) {
        ArgumentNullException.ThrowIfNull(candidates);
        return new QlearningUpdate(candidates, new QlearningAgent(
            discretizer, candidates.Count, selector, reward, alpha, gamma, randomGenerator));
    }

    /// <summary>
    /// 重置智能体 / Reset the agent.
    /// </summary>
    public void Reset() => Agent.Reset();

    /// <inheritdoc/>
    public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
        Iteration iteration,
        IReadOnlyList<T> parents,
        IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
        IIntoValue<V> converter,
        Func<Flt64> entropy)
        where V : struct, IRealNumber<V>, INumberField<V>
        where T : IIndividual<TObjValue, V> {
        ArgumentNullException.ThrowIfNull(parents);
        if (Candidates.Count == 0) {
            return Array.Empty<Solution<V>>();
        }
        int index = global::System.Math.Clamp(Agent.Action, 0, Candidates.Count - 1);
        return Candidates[index].Update(iteration, parents, model, converter, entropy);
    }

    /// <inheritdoc/>
    public void Observe<ObjValue, V>(Iteration iteration, IReadOnlyList<IIndividual<ObjValue, V>> members,
        bool improved, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ArgumentNullException.ThrowIfNull(members);
        Agent.Step(iteration, MemberViews.Of(members), improved, converter);
    }
}
