// 粒子群运行器测试 / Tests for the particle-swarm runner.

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
/// 粒子群运行器 / The particle-swarm runner.
/// </summary>
public sealed class ParticleSwarmRunnerTests {
    /// <summary>**初始化产出 `size` 个粒子，速度为初始值、个人最优即自身** / Initialization yields `size` particles with the initial velocity and self-best.</summary>
    [Fact]
    public async Task Initialize_ShouldCreateParticles() {
        ParticleSwarmRunner<Flt64, Flt64> runner = BuildRunner(size: 4);

        List<ParticleState<Flt64, Flt64>> state =
            await runner.InitializeStep(Model(), new Iteration());

        state.Should().HaveCount(4);
        state[0].Velocity.Should().HaveCount(1);
        state[0].Velocity[0].ToDouble().Should().Be(0.0);
        state[0].Best.Should().BeSameAs(state[0].Individual,
            "初始个人最优就是自身 / the initial personal best is the individual itself");
    }

    /// <summary>**初始速度可定制** / The initial velocity is configurable.</summary>
    [Fact]
    public async Task Initialize_ShouldUseInitialVelocity() {
        ParticleSwarmRunner<Flt64, Flt64> runner = BuildRunner(size: 2, initialVelocity: _ => new Flt64(3.0));

        List<ParticleState<Flt64, Flt64>> state =
            await runner.InitializeStep(Model(), new Iteration());

        state[0].Velocity[0].ToDouble().Should().Be(3.0);
    }

    /// <summary>**单粒子时社会项为零、个人项把它拉向个人最优** / With one particle the social term vanishes and the personal term pulls toward the personal best.</summary>
    /// <remarks>
    /// 手工算出的期望值：`inertia=0.4, personal=2, social=2`，速度 0，当前 10、个人最优 0，两个随机数都取 1。
    /// 速度 = 0.4×0 + 2×1×(0−10) + 2×1×(10−10) = −20，位置 = 10 + (−20) = −10。
    /// Hand-computed expectation: `inertia=0.4, personal=2, social=2`, velocity 0, current 10, personal best 0, both draws 1.
    /// velocity = 0.4×0 + 2×1×(0−10) + 2×1×(10−10) = −20, position = 10 + (−20) = −10.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldMoveTowardPersonalBest() {
        ParticleSwarmRunner<Flt64, Flt64> runner = BuildRunner(size: 1, entropy: () => new Flt64(1.0));
        var state = new List<ParticleState<Flt64, Flt64>> {
            new(Individual(10.0), new[] { new Flt64(0.0) }, Individual(0.0)),
        };

        List<ParticleState<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced[0].Velocity[0].ToDouble().Should().Be(-20.0);
        advanced[0].Individual.Solution[0].ToDouble().Should().Be(-10.0);
    }

    /// <summary>**速度先按 `maxVelocity` 截断再用于位置更新** / The velocity is clamped to `maxVelocity` before moving the position.</summary>
    /// <remarks>
    /// 只截断位置会让速度无限累积，**于是粒子在若干步之后"瞬移"，而轨迹上看不出是哪一步出的问题**。
    /// Clamping only the position lets the velocity accumulate without bound, **so the particle teleports after a few steps and the
    /// trajectory gives no hint which step went wrong**.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldClampVelocityBeforeMoving() {
        ParticleSwarmRunner<Flt64, Flt64> runner = BuildRunner(
            size: 1, entropy: () => new Flt64(1.0), maxVelocity: new Flt64(5.0));
        var state = new List<ParticleState<Flt64, Flt64>> {
            new(Individual(10.0), new[] { new Flt64(0.0) }, Individual(0.0)),
        };

        List<ParticleState<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced[0].Velocity[0].ToDouble().Should().Be(-5.0,
            "未截断的 −20 必须被压到 −5 / the unclamped −20 must be pressed to −5");
        advanced[0].Individual.Solution[0].ToDouble().Should().Be(5.0,
            "位置用的是截断后的速度 / the position uses the clamped velocity");
    }

    /// <summary>**严格改进时个人最优被更新** / The personal best updates on strict improvement.</summary>
    [Fact]
    public async Task Advance_ShouldUpdatePersonalBestOnImprovement() {
        ParticleSwarmRunner<Flt64, Flt64> runner = BuildRunner(size: 1, entropy: () => new Flt64(0.0));
        // 速度为 −5、随机数取 0：速度 = 0.4×(−5) = −2，位置 10 − 2 = 8。
        // Velocity −5 with draws 0: velocity = 0.4×(−5) = −2, position 10 − 2 = 8.
        var state = new List<ParticleState<Flt64, Flt64>> {
            new(Individual(10.0), new[] { new Flt64(-5.0) }, Individual(10.0)),
        };

        List<ParticleState<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced[0].Individual.Objective.ToDouble().Should().Be(8.0);
        advanced[0].Best.Objective.ToDouble().Should().Be(8.0,
            "改进时个人最优必须跟进 / the personal best must follow an improvement");
    }

    /// <summary>**变差时个人最优保持不变** / The personal best is kept when the candidate is worse.</summary>
    [Fact]
    public async Task Advance_ShouldKeepPersonalBestWhenWorse() {
        ParticleSwarmRunner<Flt64, Flt64> runner = BuildRunner(size: 1, entropy: () => new Flt64(0.0));
        // 速度为 +5、随机数取 0：速度 = 2，位置 12。
        // Velocity +5 with draws 0: velocity = 2, position 12.
        var state = new List<ParticleState<Flt64, Flt64>> {
            new(Individual(10.0), new[] { new Flt64(5.0) }, Individual(10.0)),
        };

        List<ParticleState<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced[0].Individual.Objective.ToDouble().Should().Be(12.0);
        advanced[0].Best.Objective.ToDouble().Should().Be(10.0,
            "变差不得覆盖个人最优 / a worse candidate must not overwrite the personal best");
    }

    /// <summary>**全局最优取整群最优、个人项与社会项各用各的权重** / The global best comes from the whole swarm and the two weights stay distinct.</summary>
    /// <remarks>
    /// 手工算出的期望值：`inertia=0.4, personal=2, social=3`，两个随机数都取 1。粒子 A 当前 10、个人最优 10，粒子 B 当前 0。
    /// 全局最优是 B（目标 0），所以 A 的速度 = 0.4×0 + 2×1×(10−10) + 3×1×(0−10) = −30，位置 = 10 − 30 = −20。
    ///
    /// **这条测试专门用来杀掉两个存活变异**：
    /// 若个人项与社会项权重对调，速度变成 3×0 + 2×(−10) = −20，与 −30 不符；
    /// 若全局最优退化成"取第一个粒子"，社会项的目标变成 A 自己（10），速度变成 0，也与 −30 不符。
    ///
    /// Hand-computed expectation: `inertia=0.4, personal=2, social=3`, both draws 1. Particle A is at 10 with personal best 10, particle B
    /// is at 0. The global best is B (objective 0), so A's velocity = 0.4×0 + 2×1×(10−10) + 3×1×(0−10) = −30 and its position is
    /// 10 − 30 = −20.
    ///
    /// **This test exists to kill two surviving mutants**: swapping the personal and social weights yields 3×0 + 2×(−10) = −20, not −30;
    /// degrading the global best to "the first particle" makes the social target A itself (10) and yields 0, also not −30.
    /// </remarks>
    [Fact]
    public async Task Advance_ShouldUseSwarmBestAndDistinctWeights() {
        ParticleSwarmRunner<Flt64, Flt64> runner = BuildRunner(
            size: 2,
            entropy: () => new Flt64(1.0),
            personalWeight: new ConstantSource(new Flt64(2.0)),
            socialWeight: new ConstantSource(new Flt64(3.0)));
        var state = new List<ParticleState<Flt64, Flt64>> {
            new(Individual(10.0), new[] { new Flt64(0.0) }, Individual(10.0)),
            new(Individual(0.0), new[] { new Flt64(0.0) }, Individual(0.0)),
        };

        List<ParticleState<Flt64, Flt64>> advanced =
            await runner.AdvanceStep(Model(), new Iteration(), state);

        advanced[0].Velocity[0].ToDouble().Should().Be(-30.0,
            "个人项 2、社会项 3、社会目标是整群最优 / personal 2, social 3, the social target is the swarm best");
        advanced[0].Individual.Solution[0].ToDouble().Should().Be(-20.0);
    }

    /// <summary>**空粒子群原样返回** / An empty swarm is returned unchanged.</summary>
    [Fact]
    public async Task Advance_ShouldReturnEmptySwarmUnchanged() {
        ParticleSwarmRunner<Flt64, Flt64> runner = BuildRunner(size: 3);

        List<ParticleState<Flt64, Flt64>> advanced = await runner.AdvanceStep(
            Model(), new Iteration(), new List<ParticleState<Flt64, Flt64>>());

        advanced.Should().BeEmpty();
    }

    /// <summary>**成员取当前个体，不取个人最优** / Members are the current individuals, not the personal bests.</summary>
    [Fact]
    public void MembersOf_ShouldReturnCurrentIndividuals() {
        ParticleSwarmRunner<Flt64, Flt64> runner = BuildRunner(size: 2);
        var state = new List<ParticleState<Flt64, Flt64>> {
            new(Individual(5.0), new[] { new Flt64(0.0) }, Individual(1.0)),
        };

        IReadOnlyList<IIndividual<Flt64, Flt64>> members = runner.StateMembers(state);

        members.Should().HaveCount(1);
        members[0].Objective.ToDouble().Should().Be(5.0);
    }

    /// <summary>**非正粒子数被拒绝** / A non-positive swarm size is rejected.</summary>
    [Fact]
    public void Constructor_ShouldRejectNonPositiveSize() {
        Action act = () => BuildRunner(size: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>构造运行器 / Build a runner.</summary>
    /// <param name="size">粒子数 / The particle count.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="maxVelocity">速度上限 / The velocity limit.</param>
    /// <param name="initialVelocity">初始速度 / The initial velocity.</param>
    /// <returns>运行器 / The runner.</returns>
    private static ParticleSwarmRunner<Flt64, Flt64> BuildRunner(
        int size,
        Func<Flt64>? entropy = null,
        Flt64? maxVelocity = null,
        Func<int, Flt64>? initialVelocity = null,
        IParameterSource? personalWeight = null,
        IParameterSource? socialWeight = null) =>
        new(entropy ?? (() => new Flt64(0.5)),
            new BestArchive<Flt64, Flt64>(20),
            NeverStop.Instance,
            IIntoValue<Flt64>.Identity,
            size: size,
            personalWeight: personalWeight,
            socialWeight: socialWeight,
            maxVelocity: maxVelocity,
            initialVelocity: initialVelocity,
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
