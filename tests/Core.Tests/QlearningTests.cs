// 强化学习算子测试 / Tests for the reinforcement-learning operators.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Rl;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 奖励算子 / The reward operators.
/// </summary>
public sealed class RewardTests {
    /// <summary>**改进奖励按改进与否给出正负值** / The improvement reward yields its signed values.</summary>
    [Fact]
    public void Improvement_ShouldYieldSignedValues() {
        var reward = new ImprovementReward();

        reward.Invoke(true).ToDouble().Should().Be(1.0);
        reward.Invoke(false).ToDouble().Should().Be(-0.5);
    }

    /// <summary>**对称奖励的两个方向幅度相同** / The symmetric reward has equal magnitude in both directions.</summary>
    [Fact]
    public void Symmetric_ShouldMirrorMagnitude() {
        var reward = new SymmetricReward(new Flt64(3.0));

        reward.Invoke(true).ToDouble().Should().Be(3.0);
        reward.Invoke(false).ToDouble().Should().Be(-3.0);
    }
}

/// <summary>
/// Q 表 / The Q table.
/// </summary>
public sealed class QTableTests {
    /// <summary>**自举项取"下一状态"的最大值，而不是"当前状态"的最大值** / The bootstrap uses the next state's maximum.</summary>
    /// <remarks>
    /// 这是 Q 学习最经典的实现错误：写成"当前状态的最大值"会让自举项退化为常量，**Q 值单调增长但策略完全不更新**，
    /// 而每次迭代的奖励与更新调用都正常执行，看不出任何异常。
    ///
    /// 构造：2 状态 1 动作。先给状态 1 写入 Q = 10（通过一次 `reward=10, terminal=true` 的更新），状态 0 的 Q 仍为 0。
    /// 然后从状态 0 更新，`reward=0, alpha=0.5, gamma=1.0`：
    /// - 正确（取下一状态的最大值 = 10）：`target = 0 + 1.0*10 = 10`，`Q = 0 + 0.5*(10-0) = 5`
    /// - 错误（取当前状态的最大值 = 0）：`target = 0`，`Q = 0`
    ///
    /// This is the classic Q-learning implementation error: taking the current state's maximum degenerates the bootstrap into a
    /// constant, so **Q values grow monotonically while the policy never updates** — and every reward and update call still runs
    /// normally.
    ///
    /// Setup: 2 states, 1 action. First write Q = 10 into state 1 (via an update with `reward=10, terminal=true`); state 0 stays at 0.
    /// Then update from state 0 with `reward=0, alpha=0.5, gamma=1.0`: correct (next-state max = 10) gives `target = 10` and
    /// `Q = 0.5*10 = 5`; wrong (current-state max = 0) gives `Q = 0`.
    /// </remarks>
    [Fact]
    public void QTable_ShouldBootstrapFromNextState() {
        var table = new QTable(2, 1);
        table.Update(1, 0, new Flt64(10.0), 1, Flt64.One, Flt64.One, terminal: true)
            .Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();

        Result<Flt64, ErrorCode, Error<ErrorCode>> result = table.Update(
            0, 0, Flt64.Zero, 1, new Flt64(0.5), Flt64.One);

        result.Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)result).Value.ToDouble().Should().BeApproximately(5.0, 1e-12);
    }

    /// <summary>**终局状态的自举项为 0** / A terminal state's bootstrap term is zero.</summary>
    [Fact]
    public void QTable_ShouldIgnoreBootstrapOnTerminal() {
        var table = new QTable(2, 1);
        table.Update(1, 0, new Flt64(10.0), 1, Flt64.One, Flt64.One, terminal: true)
            .Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();

        // terminal = true → target = reward = 0，Q 保持 0。
        // terminal = true gives target = reward = 0, so Q stays 0.
        Result<Flt64, ErrorCode, Error<ErrorCode>> result = table.Update(
            0, 0, Flt64.Zero, 1, new Flt64(0.5), Flt64.One, terminal: true);

        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)result).Value.ToDouble().Should().Be(0.0);
    }

    /// <summary>**动作掩码同时约束自举项** / The action mask also constrains the bootstrap term.</summary>
    /// <remarks>
    /// 只在采样时过滤掩码、却让自举项看全部动作，会让被屏蔽的高价值动作**通过自举项重新进入策略**，掩码形同虚设。
    /// 构造：状态 1 的动作 0 为 10、动作 1 为 100；掩码只允许动作 0，则自举项必须是 10 而不是 100。
    /// Filtering the mask only at sampling time while letting the bootstrap see all actions lets masked-out high-value actions
    /// **re-enter the policy through the bootstrap term**, making the mask useless. Setup: state 1 has action 0 = 10 and action 1 = 100;
    /// with a mask allowing only action 0 the bootstrap must be 10, not 100.
    /// </remarks>
    [Fact]
    public void QTable_ShouldRespectMaskInBootstrap() {
        var table = new QTable(2, 2);
        table.Update(1, 0, new Flt64(10.0), 1, Flt64.One, Flt64.One, terminal: true)
            .Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();
        table.Update(1, 1, new Flt64(100.0), 1, Flt64.One, Flt64.One, terminal: true)
            .Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();

        Result<Flt64, ErrorCode, Error<ErrorCode>> result = table.Update(
            0, 0, Flt64.Zero, 1, new Flt64(0.5), Flt64.One, terminal: false, availableActions: new[] { 0 });

        // 自举项 = 10 → Q = 0 + 0.5*10 = 5；若忽略掩码则 Q = 50。
        // Bootstrap = 10 gives Q = 5; ignoring the mask would give 50.
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)result).Value.ToDouble().Should().BeApproximately(5.0, 1e-12);
    }

    /// <summary>**非法参数被拒绝且不写入** / Illegal arguments are rejected without writing.</summary>
    [Fact]
    public void QTable_ShouldRejectIllegalArguments() {
        var table = new QTable(2, 2);

        table.Update(5, 0, Flt64.Zero, 0, Flt64.One, Flt64.One)
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
        table.Update(0, 5, Flt64.Zero, 0, Flt64.One, Flt64.One)
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
        table.Update(0, 0, Flt64.Zero, 5, Flt64.One, Flt64.One)
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
        table.Update(0, 0, Flt64.Zero, 0, new Flt64(1.5), Flt64.One)
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
        table.Update(0, 0, Flt64.Zero, 0, Flt64.One, new Flt64(-0.5))
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();
        table.Update(0, 0, Flt64.Zero, 0, Flt64.One, Flt64.One, availableActions: new[] { 9 })
            .Should().BeOfType<Failed<Flt64, ErrorCode, Error<ErrorCode>>>();

        table.ValueOf(0, 0).ToDouble().Should().Be(0.0, "非法更新不得写入 / illegal updates must not write");
    }

    /// <summary>**快照与恢复往返一致，非法快照不写入** / Snapshots round-trip and illegal ones do not write.</summary>
    [Fact]
    public void QTable_ShouldRoundTripSnapshot() {
        var table = new QTable(2, 2);
        table.Update(0, 0, new Flt64(4.0), 0, Flt64.One, Flt64.Zero, terminal: true)
            .Should().BeOfType<Ok<Flt64, ErrorCode, Error<ErrorCode>>>();

        IReadOnlyList<IReadOnlyList<Flt64>> snapshot = table.Snapshot();
        table.Clear();
        table.ValueOf(0, 0).ToDouble().Should().Be(0.0);

        table.Restore(snapshot).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        table.ValueOf(0, 0).ToDouble().Should().BeApproximately(4.0, 1e-12);

        table.Restore(new[] { new[] { Flt64.Zero } })
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        table.ValueOf(0, 0).ToDouble().Should().BeApproximately(4.0, 1e-12,
            "非法快照不得覆盖已有数据 / an illegal snapshot must not overwrite data");
    }

    /// <summary>**越界读取返回 0 而不是抛异常** / Out-of-range reads return 0 instead of throwing.</summary>
    [Fact]
    public void QTable_ShouldReturnZeroOutOfRange() {
        var table = new QTable(2, 2);

        table.ValueOf(-1, 0).ToDouble().Should().Be(0.0);
        table.ValueOf(9, 0).ToDouble().Should().Be(0.0);
        table.ValuesOf(9).Should().BeEmpty();
        table.BestAction(9).Should().Be(0);
    }
}

/// <summary>
/// 动作选择器 / The action selectors.
/// </summary>
public sealed class ActionSelectorTests {
    /// <summary>**ε 为 0 时恒选最优动作** / A zero epsilon always picks the best action.</summary>
    [Fact]
    public void EpsilonGreedy_ShouldBeGreedyAtZero() {
        var selector = new EpsilonGreedyActionSelector(Flt64.Zero);
        var random = new SearchRandom(1);
        IReadOnlyList<Flt64> values = new[] { new Flt64(1.0), new Flt64(9.0), new Flt64(3.0) };

        for (int attempt = 0; attempt < 20; attempt++) {
            selector.Invoke(values, () => new Flt64(random.NextDouble())).Should().Be(1);
        }
    }

    /// <summary>**ε 为 1 时覆盖全部动作** / An epsilon of 1 covers every action.</summary>
    [Fact]
    public void EpsilonGreedy_ShouldExploreAllAtOne() {
        var selector = new EpsilonGreedyActionSelector(Flt64.One);
        var random = new SearchRandom(2);
        IReadOnlyList<Flt64> values = new[] { new Flt64(1.0), new Flt64(9.0), new Flt64(3.0) };
        var seen = new HashSet<int>();

        for (int attempt = 0; attempt < 60; attempt++) {
            seen.Add(selector.Invoke(values, () => new Flt64(random.NextDouble())));
        }
        seen.Should().BeEquivalentTo(new[] { 0, 1, 2 });
    }

    /// <summary>**探索位置用独立随机数，不复用"是否探索"的那一次** / Exploration uses an independent draw.</summary>
    /// <remarks>
    /// 复用会让"探索位置"与"探索判据"强相关，于是探索总是落在低位动作上，而探索率看起来完全正确。
    /// 构造：熵源恒返回 0.5、ε = 0.6。第一次抽样 0.5 &lt; 0.6 → 探索；若复用同一个 0.5 选位置，会得到
    /// `0.5 * 3 = 1`（永远动作 1）；独立抽样（仍为 0.5）也得到 1——**所以必须用两个不同的值**才能区分。
    /// 用一个"第一次 0.5、第二次 0.95"的序列：复用会得到 1，独立抽样会得到 `0.95*3 = 2`。
    /// Reusing the roll correlates the position with the decision, so exploration always lands on low-index actions while the
    /// exploration rate looks correct. Setup: epsilon 0.6 with a first draw of 0.5 (so it explores); reusing 0.5 for the position
    /// gives action 1, while an independent second draw of 0.95 gives action 2.
    /// </remarks>
    [Fact]
    public void EpsilonGreedy_ShouldDrawIndependentPosition() {
        var selector = new EpsilonGreedyActionSelector(new Flt64(0.6));
        double[] sequence = { 0.5, 0.95 };
        int index = 0;

        int action = selector.Invoke(
            new[] { new Flt64(1.0), new Flt64(2.0), new Flt64(3.0) },
            () => new Flt64(sequence[index++]));

        action.Should().Be(2, "第二次抽样 0.95 → 0.95*3 = 2 / the second draw gives 0.95*3 = 2");
        index.Should().Be(2, "必须消耗两次抽样 / two draws must be consumed");
    }

    /// <summary>**温度趋近 0 时退化为贪心** / A near-zero temperature degenerates into greedy.</summary>
    [Fact]
    public void Softmax_ShouldDegenerateAtZeroTemperature() {
        var selector = new SoftmaxActionSelector(Flt64.Zero);
        var random = new SearchRandom(3);

        selector.Invoke(new[] { new Flt64(1.0), new Flt64(9.0) }, () => new Flt64(random.NextDouble()))
            .Should().Be(1);
    }

    /// <summary>**高温下选择分布由价值主导但不止选最优** / At high temperature the choice spreads out.</summary>
    [Fact]
    public void Softmax_ShouldSpreadAtHighTemperature() {
        var selector = new SoftmaxActionSelector(new Flt64(10.0));
        var random = new SearchRandom(4);
        var seen = new HashSet<int>();

        for (int attempt = 0; attempt < 80; attempt++) {
            seen.Add(selector.Invoke(
                new[] { new Flt64(0.0), new Flt64(1.0) },
                () => new Flt64(random.NextDouble())));
        }
        seen.Should().Contain(0, "高温下不应总是选最优 / a high temperature must not always pick the best");
    }

    /// <summary>**极大的价值不会溢出成 `∞` 而静默退化为贪心** / Huge values never overflow and silently fall back.</summary>
    /// <remarks>
    /// 不减最大值再取指数会让 `exp` 溢出成 `∞`，而"总权重非有限就退回贪心"这一兜底会让高温下的选择静默退化成贪心——
    /// **温度参数看起来仍然生效**。
    /// Skipping the max subtraction overflows `exp` to `∞`, and the "fall back to greedy when the total is not finite" guard silently
    /// degrades high-temperature selection into greedy — **the temperature still looks active**.
    /// </remarks>
    [Fact]
    public void Softmax_ShouldNotOverflowOnHugeValues() {
        var selector = new SoftmaxActionSelector(new Flt64(1000.0));
        var random = new SearchRandom(5);
        var seen = new HashSet<int>();

        for (int attempt = 0; attempt < 60; attempt++) {
            seen.Add(selector.Invoke(
                new[] { new Flt64(1.0e300), new Flt64(1.0e300) },
                () => new Flt64(random.NextDouble())));
        }
        seen.Should().BeEquivalentTo(new[] { 0, 1 },
            "两个动作价值相同时必须能选到两个 / equal values must reach both actions");
    }
}

/// <summary>
/// 状态离散化 / State discretization.
/// </summary>
public sealed class StateDiscretizerTests {
    /// <summary>**状态数量是 `levels²`** / The state amount is `levels` squared.</summary>
    [Fact]
    public void Discretizer_ShouldSquareLevels() {
        new DiversityStagnationDiscretizer(3).StateAmount.Should().Be(9);
        new DiversityStagnationDiscretizer(1).StateAmount.Should().Be(1);
        new DiversityStagnationDiscretizer(0).StateAmount.Should().Be(0);
    }

    /// <summary>**成员不足两个时返回 0** / Fewer than two members yields state 0.</summary>
    [Fact]
    public void Discretizer_ShouldReturnZeroForSmallPopulation() {
        var iteration = new Iteration();

        new DiversityStagnationDiscretizer(3)
            .Invoke(iteration, Array.Empty<IIndividual<object, Flt64>>(), IIntoValue<Flt64>.Identity)
            .Should().Be(0);
    }

    /// <summary>**完全相同的种群多样性为 0，完全铺开时为最大值** / Identical members score zero diversity.</summary>
    /// <remarks>
    /// 多样性是"平均两两距离除以最大距离"。全部成员重合时 `maximum == 0`，函数必须返回 0 而不是 `0/0 = NaN`。
    /// Diversity is the mean pairwise distance divided by the maximum. With all members coincident `maximum == 0` and the function must
    /// return 0 rather than `0/0 = NaN`.
    /// </remarks>
    [Fact]
    public void Discretizer_ShouldHandleCoincidentMembers() {
        var iteration = new Iteration();
        var members = new IIndividual<object, Flt64>[] {
            new SolutionWithFitness<object, Flt64>(new[] { new Flt64(5.0) }, new object(), 0.0),
            new SolutionWithFitness<object, Flt64>(new[] { new Flt64(5.0) }, new object(), 0.0),
        };

        int state = new DiversityStagnationDiscretizer(3)
            .Invoke(iteration, members, IIntoValue<Flt64>.Identity);

        // 多样性 0 → 等级 0；停滞 0（total == 0）→ 等级 0；状态 0。
        // Diversity 0 gives level 0; stagnation 0 (total == 0) gives level 0; state 0.
        state.Should().Be(0);
    }

    /// <summary>**停滞维度随无改进迭代数上升** / The stagnation dimension rises with no-improvement iterations.</summary>
    [Fact]
    public void Discretizer_ShouldTrackStagnation() {
        var iteration = new Iteration();
        var members = new IIndividual<object, Flt64>[] {
            new SolutionWithFitness<object, Flt64>(new[] { new Flt64(0.0) }, new object(), 0.0),
            new SolutionWithFitness<object, Flt64>(new[] { new Flt64(10.0) }, new object(), 0.0),
        };
        int early = new DiversityStagnationDiscretizer(3)
            .Invoke(iteration, members, IIntoValue<Flt64>.Identity);

        for (int step = 0; step < 10; step++) {
            iteration.Next(better: false);
        }
        int late = new DiversityStagnationDiscretizer(3)
            .Invoke(iteration, members, IIntoValue<Flt64>.Identity);

        late.Should().BeGreaterThan(early, "停滞越久状态值越大 / longer stagnation means a larger state");
    }
}

/// <summary>
/// Q 学习智能体 / The Q-learning agent.
/// </summary>
public sealed class QlearningAgentTests {
    /// <summary>**首次调用只记录状态、不更新表** / The first step only records the state.</summary>
    /// <remarks>
    /// 首次调用没有"上一状态-动作-奖励"三元组可用，强行更新会用未初始化的 `state = 0, action = 0` 污染第 0 行第 0 列。
    /// The first call has no previous state-action-reward triple; updating anyway would pollute row 0 column 0 with an uninitialized
    /// `state = 0, action = 0`.
    /// </remarks>
    [Fact]
    public void Agent_ShouldNotUpdateOnFirstStep() {
        QlearningAgent agent = BuildAgent();
        var iteration = new Iteration();

        Flt64? first = agent.Step(iteration, Members(), improved: true, IIntoValue<Flt64>.Identity);
        first.Should().BeNull();
        agent.Started.Should().BeTrue();
        agent.Table.ValueOf(0, 0).ToDouble().Should().Be(0.0);

        Flt64? second = agent.Step(iteration, Members(), improved: true, IIntoValue<Flt64>.Identity);
        second.Should().NotBeNull("第二次起才真正更新 / updates begin on the second step");
    }

    /// <summary>**重置清空表与全部状态** / Reset clears the table and every state.</summary>
    [Fact]
    public void Agent_ShouldResetEverything() {
        QlearningAgent agent = BuildAgent();
        var iteration = new Iteration();
        agent.Step(iteration, Members(), improved: true, IIntoValue<Flt64>.Identity);
        agent.Step(iteration, Members(), improved: true, IIntoValue<Flt64>.Identity);
        agent.Started.Should().BeTrue();

        agent.Reset();

        agent.Started.Should().BeFalse();
        agent.Action.Should().Be(0);
        agent.State.Should().Be(0);
        agent.Table.ValueOf(0, 0).ToDouble().Should().Be(0.0);
    }

    /// <summary>**动作数量为 0 时不更新** / A zero action amount never updates.</summary>
    [Fact]
    public void Agent_ShouldReturnNullWhenActionAmountIsZero() {
        QlearningAgent agent = new(
            new DiversityStagnationDiscretizer(3), 0, new EpsilonGreedyActionSelector(),
            new ImprovementReward(), new Flt64(0.5), new Flt64(0.9),
            () => new Flt64(0.5));

        agent.Step(new Iteration(), Members(), improved: true, IIntoValue<Flt64>.Identity)
            .Should().BeNull();
    }

    /// <summary>**学习率与折扣因子在构造时被夹到 `[0, 1]`** / Alpha and gamma are clamped at construction.</summary>
    [Fact]
    public void Agent_ShouldClampHyperparameters() {
        QlearningAgent agent = new(
            new DiversityStagnationDiscretizer(3), 2, new EpsilonGreedyActionSelector(),
            new ImprovementReward(), new Flt64(5.0), new Flt64(-1.0),
            () => new Flt64(0.5));

        agent.Alpha.ToDouble().Should().Be(1.0);
        agent.Gamma.ToDouble().Should().Be(0.0);
    }

    /// <summary>构造智能体 / Build the agent.</summary>
    /// <returns>智能体 / The agent.</returns>
    private static QlearningAgent BuildAgent() {
        var random = new SearchRandom(1);
        return new QlearningAgent(
            new DiversityStagnationDiscretizer(3), 2, new EpsilonGreedyActionSelector(),
            new ImprovementReward(), new Flt64(0.5), new Flt64(0.9),
            () => new Flt64(random.NextDouble()));
    }

    /// <summary>构造种群成员 / Build population members.</summary>
    /// <returns>种群成员 / The population members.</returns>
    private static IIndividual<object, Flt64>[] Members() => new IIndividual<object, Flt64>[] {
        new SolutionWithFitness<object, Flt64>(new[] { new Flt64(0.0) }, new object(), 0.0),
        new SolutionWithFitness<object, Flt64>(new[] { new Flt64(10.0) }, new object(), 0.0),
    };
}

/// <summary>
/// Q 学习参数源与更新算子 / The Q-learning parameter source and update operator.
/// </summary>
public sealed class QlearningDispatchTests {
    /// <summary>**参数源只按当前动作派发一次** / The parameter source dispatches once by the current action.</summary>
    /// <remarks>
    /// 把全部候选都跑一遍再挑结果会让未被选中的算子也消耗随机数，同一份种子完全不同。
    /// Running every candidate and picking a result would let unselected operators consume randomness and change the trajectory.
    /// </remarks>
    [Fact]
    public void Source_ShouldDispatchOnlySelectedCandidate() {
        var iteration = new Iteration();
        var first = new CountingSource(new Flt64(1.0));
        var second = new CountingSource(new Flt64(2.0));
        var random = new SearchRandom(1);
        var source = QlearningSource.Create(
            new IParameterSource[] { first, second }, () => new Flt64(random.NextDouble()));

        source.Value(iteration).ToDouble().Should().Be(1.0);
        first.Calls.Should().Be(1);
        second.Calls.Should().Be(0, "未选中的候选不得被调用 / unselected candidates must not run");
    }

    /// <summary>**候选为空时返回 0 而不是崩溃** / An empty candidate list returns 0 instead of crashing.</summary>
    [Fact]
    public void Source_ShouldReturnZeroWithoutCandidates() {
        var random = new SearchRandom(1);
        var source = QlearningSource.Create(
            Array.Empty<IParameterSource>(), () => new Flt64(random.NextDouble()));

        source.Value(new Iteration()).ToDouble().Should().Be(0.0);
    }

    /// <summary>**观察者把迭代与改进反馈交给智能体** / The observer forwards the iteration and the improvement flag.</summary>
    [Fact]
    public void Source_ShouldForwardObservation() {
        var random = new SearchRandom(1);
        var candidates = new IParameterSource[] { new CountingSource(Flt64.One) };
        var source = QlearningSource.Create(candidates, () => new Flt64(random.NextDouble()));

        source.Observe(
            new Iteration(),
            new IIndividual<object, Flt64>[] {
                new SolutionWithFitness<object, Flt64>(new[] { new Flt64(0.0) }, new object(), 0.0),
                new SolutionWithFitness<object, Flt64>(new[] { new Flt64(10.0) }, new object(), 0.0),
            },
            improved: true, IIntoValue<Flt64>.Identity);

        source.Agent.Started.Should().BeTrue();
    }

    /// <summary>**更新算子只派发被选中的候选** / The update operator dispatches only the selected candidate.</summary>
    [Fact]
    public void Update_ShouldDispatchOnlySelectedCandidate() {
        var iteration = new Iteration();
        var first = new CountingUpdate();
        var second = new CountingUpdate();
        var random = new SearchRandom(1);
        var update = QlearningUpdate.Create(
            new ISolutionUpdate[] { first, second }, () => new Flt64(random.NextDouble()));

        update.Update<object, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
            iteration, Parents(), new StubModelCore<object>(value => value[0]), IIntoValue<Flt64>.Identity,
            () => new Flt64(random.NextDouble()));

        first.Calls.Should().Be(1);
        second.Calls.Should().Be(0, "未选中的候选不得被调用 / unselected candidates must not run");
    }

    /// <summary>**候选为空时产生空结果** / An empty candidate list yields nothing.</summary>
    [Fact]
    public void Update_ShouldReturnEmptyWithoutCandidates() {
        var random = new SearchRandom(1);
        var update = QlearningUpdate.Create(
            Array.Empty<ISolutionUpdate>(), () => new Flt64(random.NextDouble()));

        update.Update<object, Flt64, Flt64, EvaluatedIndividual<Flt64, Flt64>>(
                new Iteration(), Parents(), new StubModelCore<object>(value => value[0]),
                IIntoValue<Flt64>.Identity, () => new Flt64(random.NextDouble()))
            .Should().BeEmpty();
    }

    /// <summary>构造父代组 / Build a parent group.</summary>
    /// <returns>父代组 / The parent group.</returns>
    private static EvaluatedIndividual<Flt64, Flt64>[] Parents() => new[] {
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(1.0) }, new Flt64(0.0)),
    };

    /// <summary>计数的参数源 / A counting parameter source.</summary>
    private sealed class CountingSource : IParameterSource {
        private readonly Flt64 _value;

        /// <summary>
        /// 构造计数源 / Construct the counting source.
        /// </summary>
        /// <param name="value">返回值 / The returned value.</param>
        public CountingSource(Flt64 value) => _value = value;

        /// <summary>调用次数 / The call count.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc/>
        public Flt64 Value(Iteration iteration) {
            Calls++;
            return _value;
        }
    }

    /// <summary>计数的更新算子 / A counting update operator.</summary>
    private sealed class CountingUpdate : ISolutionUpdate {
        /// <summary>调用次数 / The call count.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc/>
        public IReadOnlyList<Solution<V>> Update<TObj, TObjValue, V, T>(
            Iteration iteration,
            IReadOnlyList<T> parents,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            IIntoValue<V> converter,
            Func<Flt64> entropy)
            where V : struct, IRealNumber<V>, INumberField<V>
            where T : IIndividual<TObjValue, V> {
            Calls++;
            return parents.Select(parent => new Solution<V>(parent.Solution)).ToList();
        }
    }
}
