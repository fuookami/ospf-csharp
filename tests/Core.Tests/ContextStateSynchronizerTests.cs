// 上下文状态同步器测试 / Tests for the cross-context state synchroniser.

using System;
using System.Collections.Generic;
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
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 上下文状态同步器 / The cross-context state synchroniser.
/// </summary>
public sealed class ContextStateSynchronizerTests {
    /// <summary>**速度按上限截断** / The velocity is clamped to the limit.</summary>
    [Fact]
    public void Velocity_ShouldClampToLimit() {
        var synchronizer = new ContextStateSynchronizer<Flt64, Flt64>(
            BuildProblem(), maxVelocity: 2.0);

        IReadOnlyList<Flt64> forward = Unwrap(synchronizer.Velocity(
            new[] { new Flt64(0.0) }, new[] { new Flt64(5.0) }));
        IReadOnlyList<Flt64> backward = Unwrap(synchronizer.Velocity(
            new[] { new Flt64(5.0) }, new[] { new Flt64(0.0) }));

        forward[0].ToDouble().Should().Be(2.0);
        backward[0].ToDouble().Should().Be(-2.0);
    }

    /// <summary>**维度不一致或位置非有限时失败** / Mismatched dimensions or non-finite positions fail.</summary>
    [Fact]
    public void Velocity_ShouldRejectInvalidInput() {
        var synchronizer = new ContextStateSynchronizer<Flt64, Flt64>(BuildProblem());

        Action mismatched = () => synchronizer.Velocity(
            new[] { new Flt64(0.0) }, new[] { new Flt64(1.0), new Flt64(2.0) });
        Action notFinite = () => synchronizer.Velocity(
            new[] { new Flt64(0.0) }, new[] { new Flt64(double.NaN) });

        mismatched.Should().NotThrow();
        synchronizer.Velocity(new[] { new Flt64(0.0) }, new[] { new Flt64(1.0), new Flt64(2.0) })
            .Should().BeOfType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>();
        notFinite.Should().NotThrow();
        synchronizer.Velocity(new[] { new Flt64(0.0) }, new[] { new Flt64(double.NaN) })
            .Should().BeOfType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**外来个体写进最差的槽位** / Incoming individuals go into the worst slots.</summary>
    /// <remarks>
    /// 替换列表按"个人最优"升序排好，从末尾写等于"最差的先被替换"。从头部写会让最好的粒子被顶掉，
    /// **而种群规模、速度、最优值看起来全部正常**。
    /// The replacement list is sorted by personal best ascending, so writing from the end means "the worst is replaced first". Writing from
    /// the head would evict the best particle, **while the population size, velocities and best values all still look normal**.
    ///
    /// 手算：状态三个粒子的最优值分别是 9、5、0，外来个体在 5.5。距离最近的是 5 那个粒子，因此速度 = 5.5 − 5 = 0.5；
    /// 个人最优取 5（比 5.5 好）。写入槽位是排序后的第 3 个（下标 2）。
    /// Hand-computed: the three particles' bests are 9, 5 and 0, and the incoming individual sits at 5.5. The nearest particle is the one at
    /// 5, so the velocity is 5.5 − 5 = 0.5; the personal best stays 5 (better than 5.5). The target slot is the third one after sorting
    /// (index 2).
    /// </remarks>
    [Fact]
    public void Particles_ShouldWriteIntoWorstSlots() {
        var synchronizer = new ContextStateSynchronizer<Flt64, Flt64>(BuildProblem());
        var state = new List<ParticleState<Flt64, Flt64>> {
            Particle(9.0, 9.0), Particle(5.0, 5.0), Particle(0.0, 0.0),
        };

        IReadOnlyList<ParticleState<Flt64, Flt64>> merged = Unwrap(
            synchronizer.Particles(state, new[] { Individual(5.5) }));

        merged.Should().HaveCount(3);
        merged[2].Individual.Solution[0].ToDouble().Should().Be(5.5,
            "外来个体必须落在最差的槽位 / the incoming individual must land in the worst slot");
        merged[2].Velocity[0].ToDouble().Should().Be(0.5,
            "速度来自最近的粒子（5），而不是对应槽位 / the velocity comes from the nearest particle (5), not the matching slot");
        merged[2].Best.Objective.ToDouble().Should().Be(5.0,
            "个人最优保留更优的那个 / the better personal best is kept");
        merged[0].Individual.Solution[0].ToDouble().Should().Be(0.0,
            "最好的粒子保持不变 / the best particle stays put");
    }

    /// <summary>**外来个体无效时整体失败** / Invalid incoming individuals fail the whole merge.</summary>
    [Fact]
    public void Particles_ShouldFailOnInvalidIncoming() {
        var synchronizer = new ContextStateSynchronizer<Flt64, Flt64>(BuildProblem());
        var state = new List<ParticleState<Flt64, Flt64>> { Particle(1.0, 1.0) };

        Result<IReadOnlyList<ParticleState<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> result =
            synchronizer.Particles(state, new[] { Individual(double.NaN) });

        result.Should().BeOfType<Failed<IReadOnlyList<ParticleState<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**没有外来个体时状态原样返回** / An empty incoming set returns the state unchanged.</summary>
    [Fact]
    public void Particles_ShouldReturnStateWhenNothingArrives() {
        var synchronizer = new ContextStateSynchronizer<Flt64, Flt64>(BuildProblem());
        var state = new List<ParticleState<Flt64, Flt64>> { Particle(1.0, 1.0) };

        IReadOnlyList<ParticleState<Flt64, Flt64>> merged = Unwrap(
            synchronizer.Particles(state, Array.Empty<IIndividual<Flt64, Flt64>>()));

        merged.Should().BeSameAs(state);
    }

    /// <summary>**被挤出去的旧个体进入 SHADE 外部档案** / Displaced old individuals enter the SHADE archive.</summary>
    /// <remarks>
    /// 放"被接受的个体"会让档案变成第二个种群，**而收敛曲线只是"覆盖度更好"**。
    /// Storing accepted individuals would turn the archive into a second population, **while the convergence curve merely looks "better
    /// spread"**.
    /// </remarks>
    [Fact]
    public void Shade_ShouldArchiveDisplacedIndividuals() {
        var synchronizer = new ContextStateSynchronizer<Flt64, Flt64>(BuildProblem());
        var state = new ShadeState<Flt64, Flt64>(
            new[] { Individual(1.0), Individual(2.0) },
            ShadeMemory.Create(5),
            Array.Empty<Solution<Flt64>>());

        ShadeState<Flt64, Flt64> merged = Unwrap(
            synchronizer.Shade(state, new[] { Individual(0.0) }));

        merged.Population.Should().HaveCount(2);
        merged.Population[0].Objective.ToDouble().Should().Be(0.0);
        merged.Population[1].Objective.ToDouble().Should().Be(1.0);
        merged.Archive.Should().HaveCount(1, "被挤出去的 2 必须进档案 / the displaced 2 must enter the archive");
        merged.Archive[0].Values[0].ToDouble().Should().Be(2.0);
    }

    /// <summary>**只有一个外来个体时保留原协方差** / A single incoming individual keeps the previous covariance.</summary>
    /// <remarks>
    /// 用 1 个样本算样本协方差会得到全零矩阵，**于是下一次采样退化成"在原地采一个点"，而代数仍在推进**。
    /// Sample covariance over one sample is all zeros, **so the next sample degenerates to "one point at the mean" while the generation
    /// counter keeps advancing**.
    /// </remarks>
    [Fact]
    public void CmaEs_ShouldKeepCovarianceForSingleImport() {
        var synchronizer = new ContextStateSynchronizer<Flt64, Flt64>(BuildProblem());
        var state = new CmaEsState<Flt64, Flt64>(
            new[] { 0.0 }, 0.3, new[] { new[] { 1.0 } },
            new[] { 0.0 }, new[] { 0.0 }, 4, null);

        CmaEsState<Flt64, Flt64> merged = Unwrap(
            synchronizer.CmaEs(state, new[] { Individual(7.0) }));

        merged.Covariance[0][0].Should().Be(1.0,
            "单个样本的样本协方差毫无意义，必须保留原值 / sample covariance over one sample is meaningless, so the old value must be kept");
        merged.Mean[0].Should().Be(7.0);
        merged.Sigma.Should().Be(0.3);
        merged.Generation.Should().Be(0, "同步后重新计数 / the generation restarts after a sync");
    }

    /// <summary>**两个外来个体给出手算均值与协方差** / Two incoming individuals give the hand-computed mean and covariance.</summary>
    /// <remarks>
    /// 手算：x = 1 与 x = 3 → 均值 2；样本方差 = ((1−2)² + (3−2)²)/1 = 2；除以 σ² = 0.09 → 22.2222…；对角再加 1e-10。
    /// Hand-computed: x = 1 and x = 3 → mean 2; sample variance = ((1−2)² + (3−2)²)/1 = 2; divided by σ² = 0.09 → 22.2222…; plus 1e-10 on
    /// the diagonal.
    /// </remarks>
    [Fact]
    public void CmaEs_ShouldComputeMeanAndCovariance() {
        var synchronizer = new ContextStateSynchronizer<Flt64, Flt64>(BuildProblem());
        var state = new CmaEsState<Flt64, Flt64>(
            new[] { 0.0 }, 0.3, new[] { new[] { 1.0 } },
            new[] { 0.0 }, new[] { 0.0 }, 4, null);

        CmaEsState<Flt64, Flt64> merged = Unwrap(
            synchronizer.CmaEs(state, new[] { Individual(1.0), Individual(3.0) }));

        merged.Mean[0].Should().Be(2.0);
        merged.Covariance[0][0].Should().BeApproximately(22.22222222, 1.0e-6);
    }

    /// <summary>**CLPSO 同步后学习对象是本粒子的下标、停滞清零** / After a CLPSO sync the exemplars are the particle's own index and stagnation is zero.</summary>
    /// <remarks>
    /// 改成随机下标会让同步这一步额外消耗随机数，**而两个上下文的随机流就此错位**。
    /// Using random indices would make the sync step consume entropy, **desynchronising the two contexts' random streams**.
    /// </remarks>
    [Fact]
    public void Clpso_ShouldResetExemplarsToOwnIndex() {
        var synchronizer = new ContextStateSynchronizer<Flt64, Flt64>(BuildProblem());
        var state = new List<ClpsoParticle<Flt64, Flt64>> {
            new(Individual(0.0), new[] { new Flt64(0.0) }, Individual(0.0), new[] { 3 }, 5),
            new(Individual(9.0), new[] { new Flt64(0.0) }, Individual(9.0), new[] { 2 }, 4),
        };

        IReadOnlyList<ClpsoParticle<Flt64, Flt64>> merged = Unwrap(
            synchronizer.Clpso(state, new[] { Individual(8.0) }));

        merged.Should().HaveCount(2);
        merged[1].Exemplars[0].Should().Be(1,
            "学习对象取该粒子自己的下标 / the exemplar takes the particle's own index");
        merged[1].Stagnation.Should().Be(0);
    }

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("expected a successful result");

    /// <summary>构造一个粒子 / Build one particle.</summary>
    /// <param name="position">位置 / The position.</param>
    /// <param name="objective">个人最优的目标值 / The personal best's objective.</param>
    /// <returns>粒子 / The particle.</returns>
    private static ParticleState<Flt64, Flt64> Particle(double position, double objective) =>
        new(Individual(position), new[] { Flt64.Zero }, Individual(objective));

    /// <summary>构造一个个体 / Build one individual.</summary>
    /// <param name="value">位置与目标值 / The position and objective.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<Flt64, Flt64> Individual(double value) =>
        new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(value) }, new Flt64(value), 0.0);

    /// <summary>构造搜索问题 / Build the search problem.</summary>
    /// <returns>搜索问题 / The search problem.</returns>
    private static SearchProblem<Flt64, Flt64> BuildProblem() =>
        new("bridge", "bridge-fingerprint", new BridgeModel(),
            SearchCodecs.Flt64, SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            valid: objective => double.IsFinite(objective.ToDouble()),
            validSolution: _ => true,
            admissible: _ => true);

    /// <summary>同步器测试模型 / A synchroniser test model.</summary>
    private sealed class BridgeModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
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
