// 综合学习粒子群运行器测试 / Tests for the CLPSO runner.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 综合学习粒子群运行器 / The CLPSO runner.
/// </summary>
public sealed class ClpsoRunnerTests {
    /// <summary>**成员取个人最优，而不是当前个体** / Members are the personal bests, not the current individuals.</summary>
    /// <remarks>
    /// 返回当前个体等于把 CLPSO 的"学习记忆"从档案里抹掉，**而档案看起来只是"收敛得更慢"**。
    /// Returning the current individuals erases CLPSO's learning memory from the archive, **and the archive merely looks like it
    /// "converges more slowly"**. This is the opposite of `ParticleSwarmRunner`.
    /// </remarks>
    [Fact]
    public void MembersOf_ShouldReturnPersonalBests() {
        ClpsoRunner<Flt64, Flt64> runner = BuildRunner(size: 2);
        var state = new List<ClpsoParticle<Flt64, Flt64>> {
            new(Individual(5.0), new[] { Flt64.Zero }, Individual(1.0), new[] { 0 }, 0),
        };

        IReadOnlyList<IIndividual<Flt64, Flt64>> members = runner.StateMembers(state);

        members.Should().HaveCount(1);
        members[0].Objective.ToDouble().Should().Be(1.0,
            "成员必须是个人最优 1，而不是当前个体 5 / the member must be the personal best 1, not the current 5");
    }

    /// <summary>**初始化给出零速度、零停滞与逐维学习对象** / Initialization yields zero velocity, zero stagnation and per-dimension exemplars.</summary>
    [Fact]
    public async Task Initialize_ShouldCreateParticles() {
        ClpsoRunner<Flt64, Flt64> runner = BuildRunner(size: 3);

        List<ClpsoParticle<Flt64, Flt64>> state =
            await runner.InitializeStep(Model(), new Iteration());

        state.Should().HaveCount(3);
        state[0].Velocity[0].ToDouble().Should().Be(0.0);
        state[0].Stagnation.Should().Be(0);
        state[0].Exemplars.Should().HaveCount(1);
        state[0].Best.Should().BeSameAs(state[0].Individual);
    }

    /// <summary>**学习对象来自个人最优，改进时停滞清零** / The learning target is the personal best and an improvement zeroes stagnation.</summary>
    /// <remarks>
    /// 手算：`weight=0.4, coefficient=1.0`，学习对象指向粒子 B（个人最优 0），当前 5，随机数取 1。
    /// 速度 = 0.4×0 + 1×1×(0−5) = −5，位置 = 0；候选 0 < 个人最优 5 → 改进，停滞清零。
    /// Hand-computed: `weight=0.4, coefficient=1.0`, the exemplar points at particle B (personal best 0), current 5, draw 1.
    /// velocity = 0.4×0 + 1×1×(0−5) = −5, position 0; candidate 0 < personal best 5 → improved, stagnation zeroed.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldLearnFromExemplarAndResetStagnation() {
        ClpsoRunner<Flt64, Flt64> runner = BuildRunner(
            size: 2, entropy: () => new Flt64(1.0), refreshInterval: 100);
        var state = new List<ClpsoParticle<Flt64, Flt64>> {
            new(Individual(5.0), new[] { Flt64.Zero }, Individual(5.0), new[] { 1 }, 3),
            new(Individual(0.0), new[] { Flt64.Zero }, Individual(0.0), new[] { 1 }, 0),
        };

        List<ClpsoParticle<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced[0].Individual.Objective.ToDouble().Should().Be(0.0);
        advanced[0].Best.Objective.ToDouble().Should().Be(0.0);
        advanced[0].Stagnation.Should().Be(0, "改进后停滞必须清零 / an improvement must zero stagnation");
    }

    /// <summary>**速度先按上限截断** / The velocity is clamped to the limit first.</summary>
    [Fact]
    public async Task Advance_ShouldClampVelocity() {
        ClpsoRunner<Flt64, Flt64> runner = BuildRunner(
            size: 2, entropy: () => new Flt64(1.0), refreshInterval: 100,
            maxVelocity: new Flt64(2.0));
        var state = new List<ClpsoParticle<Flt64, Flt64>> {
            new(Individual(5.0), new[] { Flt64.Zero }, Individual(5.0), new[] { 1 }, 3),
            new(Individual(0.0), new[] { Flt64.Zero }, Individual(0.0), new[] { 1 }, 0),
        };

        List<ClpsoParticle<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced[0].Velocity[0].ToDouble().Should().Be(-2.0);
        advanced[0].Individual.Solution[0].ToDouble().Should().Be(3.0,
            "位置用的是截断后的速度 / the position uses the clamped velocity");
    }

    /// <summary>**停滞达到刷新间隔时重新抽取学习对象** / Reaching the refresh interval re-draws the exemplars.</summary>
    /// <remarks>
    /// 判据读的是**更新前**的停滞计数。把它挪到更新之后再判断，会让一次刚刚成功的改进立刻触发刷新。
    /// The predicate reads the **pre-update** stagnation counter; moving it after the update makes a just-succeeded improvement trigger a
    /// refresh immediately.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldRefreshExemplarsAtInterval() {
        // 第一次抽样取 1.0 → 下标 int(1.0×2)=2 → 截断到 1；学习项的随机数取 0 → 速度 0，候选停在 5。
        // 候选不改进，且停滞 8 ≤ 上限 100，于是走"保持个人最优、写入刷新后的学习对象"分支。
        // The first draw is 1.0 → index int(1.0×2) = 2 → clamped to 1; the learning draw is 0 → zero speed, candidate stays at 5.
        // The candidate does not improve and stagnation 8 ≤ limit 100, so the branch that keeps the personal best and stores the
        // refreshed exemplars is taken.
        // 粒子 0 用两次（抽样、学习），粒子 1 用一次（学习）。
        // Particle 0 consumes two draws (sampling, learning); particle 1 consumes one (learning).
        var draws = new Queue<double>(new[] { 1.0, 0.0, 0.0 });
        ClpsoRunner<Flt64, Flt64> runner = BuildRunner(
            size: 2, entropy: () => new Flt64(draws.Dequeue()), refreshInterval: 7, stagnationLimit: 100);
        var state = new List<ClpsoParticle<Flt64, Flt64>> {
            new(Individual(5.0), new[] { Flt64.Zero }, Individual(0.0), new[] { 0 }, 7),
            new(Individual(0.0), new[] { Flt64.Zero }, Individual(0.0), new[] { 0 }, 0),
        };

        List<ClpsoParticle<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced[0].Stagnation.Should().Be(8);
        advanced[0].Exemplars[0].Should().Be(1,
            "刷新后学习对象必须被写入 / the refreshed exemplar must be stored");
        advanced[0].Best.Objective.ToDouble().Should().Be(0.0,
            "未改进时个人最优不变 / the personal best is unchanged without an improvement");
    }

    /// <summary>**长期停滞把个人最优重置到当前位置** / Prolonged stagnation resets the personal best to the current position.</summary>
    /// <remarks>
    /// 不重置会让一个"卡住的"粒子永远朝同一个方向学习，**而停滞计数继续增长却没有任何行为变化**。
    /// Skipping the reset leaves a stuck particle learning toward the same target forever, **while the stagnation counter keeps growing
    /// with no behavioural change**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldResetBestAfterStagnationLimit() {
        // 停滞 5，上限 5 → 6 > 5 进入重置分支；学习项随机数取 0 → 速度 0，候选停在 5；重置时抽样取 1.0 → 下标 1。
        // Stagnation 5 with limit 5 → 6 > 5 enters the reset branch; the learning draw is 0 → zero speed, candidate stays at 5; the
        // reset draw is 1.0 → index 1.
        // 粒子 0 用两次（学习、重置抽样），粒子 1 用一次（学习）。
        // Particle 0 consumes two draws (learning, reset sampling); particle 1 consumes one (learning).
        var draws = new Queue<double>(new[] { 0.0, 1.0, 0.0 });
        ClpsoRunner<Flt64, Flt64> runner = BuildRunner(
            size: 2, entropy: () => new Flt64(draws.Dequeue()), refreshInterval: 100, stagnationLimit: 5);
        var state = new List<ClpsoParticle<Flt64, Flt64>> {
            new(Individual(5.0), new[] { Flt64.Zero }, Individual(0.0), new[] { 0 }, 5),
            new(Individual(0.0), new[] { Flt64.Zero }, Individual(0.0), new[] { 0 }, 0),
        };

        List<ClpsoParticle<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced[0].Best.Objective.ToDouble().Should().Be(5.0,
            "个人最优必须被重置到当前位置 / the personal best must be reset to the current position");
        advanced[0].Stagnation.Should().Be(0);
        advanced[0].Exemplars[0].Should().Be(1,
            "重置后学习对象必须重新抽取 / the exemplars must be re-drawn after the reset");
    }

    /// <summary>**规模为 1 时抽样不消耗随机数** / Sampling a size of one consumes no entropy.</summary>
    /// <remarks>
    /// 消耗随机数会让单粒子情形下的随机流位置与多粒子情形不同，**而两种情形本该只在"抽样"上不同**。
    /// Consuming it would put the single-particle case at a different position in the random stream than the multi-particle case, **and
    /// the two should differ only in the sampling**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldNotConsumeEntropyForSingleParticle() {
        int draws = 0;
        ClpsoRunner<Flt64, Flt64> runner = BuildRunner(
            size: 1, entropy: () => { draws++; return new Flt64(0.5); }, refreshInterval: 7);
        var state = new List<ClpsoParticle<Flt64, Flt64>> {
            new(Individual(5.0), new[] { Flt64.Zero }, Individual(5.0), new[] { 0 }, 7),
        };

        await runner.AdvanceStep(Model(), new Iteration(), state);

        draws.Should().Be(1,
            "只有学习项抽取一次；刷新抽样因规模为 1 而不消耗熵 / only the learning term draws; the refresh draw is skipped for a size of 1");
    }

    /// <summary>**空粒子群原样返回** / An empty swarm is returned unchanged.</summary>
    [Fact]
    public async Task Advance_ShouldReturnEmptySwarmUnchanged() {
        ClpsoRunner<Flt64, Flt64> runner = BuildRunner(size: 3);

        List<ClpsoParticle<Flt64, Flt64>> advanced = await runner.AdvanceStep(
            Model(), new Iteration(), new List<ClpsoParticle<Flt64, Flt64>>());

        advanced.Should().BeEmpty();
    }

    /// <summary>**非正粒子数与负间隔被拒绝** / A non-positive size or a negative interval is rejected.</summary>
    [Fact]
    public void Constructor_ShouldRejectInvalidConfiguration() {
        Action zeroSize = () => BuildRunner(size: 0);
        Action negativeInterval = () => BuildRunner(size: 2, refreshInterval: -1);

        zeroSize.Should().Throw<ArgumentOutOfRangeException>();
        negativeInterval.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>构造运行器 / Build a runner.</summary>
    /// <param name="size">粒子数 / The particle count.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="refreshInterval">刷新间隔 / The refresh interval.</param>
    /// <param name="stagnationLimit">停滞阈值 / The stagnation limit.</param>
    /// <param name="maxVelocity">速度上限 / The velocity limit.</param>
    /// <returns>运行器 / The runner.</returns>
    private static ClpsoRunner<Flt64, Flt64> BuildRunner(
        int size,
        Func<Flt64>? entropy = null,
        int refreshInterval = 7,
        int stagnationLimit = 5,
        Flt64? maxVelocity = null) =>
        new(entropy ?? (() => new Flt64(0.5)),
            new BestArchive<Flt64, Flt64>(20),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            size: size,
            inertia: new ConstantSource(new Flt64(0.4)),
            learningWeight: new Flt64(1.0),
            maxVelocity: maxVelocity,
            refreshInterval: refreshInterval,
            stagnationLimit: stagnationLimit,
            initializer: new RampInitializer());

    /// <summary>构造一个个体 / Build one individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<Flt64, Flt64> Individual(double objective) =>
        new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(objective) }, new Flt64(objective), 0.0);

    /// <summary>构造模型 / Build the model.</summary>
    /// <returns>模型 / The model.</returns>
    private static IAbstractCallBackModelInterface<object?, Flt64, Flt64> Model() => new SwarmModel();

    /// <summary>递增初始化器 / A ramp initializer.</summary>
    private sealed class RampInitializer : IInitializer {
        /// <inheritdoc/>
        public IReadOnlyList<Solution<V>> Generate<V, TObj, TObjValue>(
            Iteration iteration,
            IAbstractCallBackModelInterface<TObj, TObjValue, V> model,
            ulong amount,
            IIntoValue<V> converter)
            where V : struct, IRealNumber<V>, INumberField<V> {
            var result = new List<Solution<V>>((int)amount);
            for (int index = 0; index < (int)amount; index++) {
                result.Add(new Solution<V>(new[] { converter.IntoValue(new Flt64(index)) }));
            }
            return result;
        }
    }

    /// <summary>粒子测试模型 / A swarm test model.</summary>
    private sealed class SwarmModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
        /// <inheritdoc/>
        public Flt64 DefaultObjective => new(double.PositiveInfinity);

        /// <inheritdoc/>
        public ObjectCategory ObjectCategory => ObjectCategory.Minimum;

        /// <inheritdoc/>
        public IAbstractMutableTokenTable<Flt64> Tokens { get; } =
            new ConcurrentAutoTokenTable<Flt64>(
                new Fuookami.Ospf.Math.Symbol.LinearCategory(),
                Array.Empty<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints =>
            new (Func<IReadOnlyList<Flt64>, bool?>, string)[] { (values => true, "c1") };

        /// <inheritdoc/>
        public IReadOnlyList<(Func<IReadOnlyList<Flt64>, object?> Extractor, string Name)> ObjectiveFunctions =>
            new (Func<IReadOnlyList<Flt64>, object?>, string)[] { (values => null, "f1") };

        /// <inheritdoc/>
        public Flt64 Operation(Flt64 lhs, Flt64 rhs) => lhs;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue() => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue(object? obj) => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 Objective(IReadOnlyList<Flt64> solution) =>
            solution.Count == 0 ? DefaultObjective : solution[0];

        /// <inheritdoc/>
        public Order CompareObjective(Flt64 lhs, Flt64 rhs) =>
            OrderHelpers.OrderOf(lhs.ToDouble().CompareTo(rhs.ToDouble()));

        /// <inheritdoc/>
        public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => true;

        /// <inheritdoc/>
        public void Flush() { }

        /// <inheritdoc/>
        public void Dispose() { }
    }
}
