// 原生状态信封编解码器测试 / Tests for the native-state envelope codecs.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Running;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 原生状态信封编解码器 / The native-state envelope codecs.
/// </summary>
public sealed class RunnerNativeStateCodecTests {
    /// <summary>**粒子状态的往返保留速度与个人最优** / The particle-state round-trip preserves velocity and personal best.</summary>
    /// <remarks>
    /// 只存当前个体会让"个人最优"在恢复后回退成当前位置，**于是整群在被恢复的那一刻同时忘记了自己的历史最优，
    /// 而算法看起来只是"退步了一次"**。
    /// Storing only the current individuals makes every personal best fall back to the current position on restore, **so the whole swarm
    /// forgets its own history at that moment while the algorithm merely looks like it "stepped back once"**.
    /// </remarks>
    [Fact]
    public void Particles_ShouldRoundTrip() {
        ISearchCodec<List<ParticleState<Flt64, Flt64>>> codec = RunnerStateCodecs.Particles(Problem());
        var state = new List<ParticleState<Flt64, Flt64>> {
            new(Individual(1.0), new[] { new Flt64(0.25) }, Individual(0.5)),
            new(Individual(2.0), new[] { new Flt64(-0.5) }, Individual(2.0)),
        };

        List<ParticleState<Flt64, Flt64>> restored = Unwrap(codec.Decode(Unwrap(codec.Encode(state))));

        restored.Should().HaveCount(2);
        restored[0].Individual.Objective.ToDouble().Should().Be(1.0);
        restored[0].Best.Objective.ToDouble().Should().Be(0.5);
        restored[0].Velocity[0].ToDouble().Should().Be(0.25);
        restored[1].Velocity[0].ToDouble().Should().Be(-0.5);
    }

    /// <summary>**个体数不是速度行数的两倍时被拒绝** / A count that is not twice the velocity rows is rejected.</summary>
    [Fact]
    public void Particles_ShouldRejectCountMismatch() {
        ISearchCodec<List<ParticleState<Flt64, Flt64>>> codec = RunnerStateCodecs.Particles(Problem());
        var state = new List<ParticleState<Flt64, Flt64>> {
            new(Individual(1.0), new[] { new Flt64(0.25) }, Individual(0.5)),
        };
        JsonNode node = JsonNode.Parse(Unwrap(codec.Encode(state)).GetRawText())!;
        node["vectors"]!.AsArray().Add(new JsonArray(0.0));

        codec.Decode(JsonDocument.Parse(node.ToJsonString()).RootElement)
            .Should().BeOfType<Failed<List<ParticleState<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**速度行长度与维度不符时被拒绝** / A velocity row that does not match the dimension is rejected.</summary>
    [Fact]
    public void Particles_ShouldRejectRowLengthMismatch() {
        ISearchCodec<List<ParticleState<Flt64, Flt64>>> codec = RunnerStateCodecs.Particles(Problem());
        var state = new List<ParticleState<Flt64, Flt64>> {
            new(Individual(1.0), new[] { new Flt64(0.25) }, Individual(0.5)),
        };
        JsonNode node = JsonNode.Parse(Unwrap(codec.Encode(state)).GetRawText())!;
        node["vectors"]!.AsArray()[0]!.AsArray().Add(9.0);

        codec.Decode(JsonDocument.Parse(node.ToJsonString()).RootElement)
            .Should().BeOfType<Failed<List<ParticleState<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**CLPSO 状态的往返保留学习对象与停滞计数** / The CLPSO round-trip preserves exemplars and stagnation.</summary>
    [Fact]
    public void Clpso_ShouldRoundTrip() {
        ISearchCodec<List<ClpsoParticle<Flt64, Flt64>>> codec = RunnerStateCodecs.Clpso(Problem());
        var state = new List<ClpsoParticle<Flt64, Flt64>> {
            new(Individual(1.0), new[] { new Flt64(0.5) }, Individual(0.5), new[] { 1 }, 3),
            new(Individual(2.0), new[] { new Flt64(0.0) }, Individual(2.0), new[] { 0 }, 0),
        };

        List<ClpsoParticle<Flt64, Flt64>> restored =
            Unwrap(codec.Decode(Unwrap(codec.Encode(state))));

        restored[0].Stagnation.Should().Be(3);
        restored[0].Exemplars[0].Should().Be(1);
        restored[1].Exemplars[0].Should().Be(0);
        restored[0].Velocity[0].ToDouble().Should().Be(0.5);
    }

    /// <summary>**学习对象下标越界时被拒绝** / An out-of-range exemplar index is rejected.</summary>
    /// <remarks>
    /// 不检查下标落在群内会让恢复后取到越界个体，**而错误发生在若干步之后**。
    /// Not checking that the indices fall inside the swarm yields out-of-range individuals a few steps after the restore.
    /// </remarks>
    [Fact]
    public void Clpso_ShouldRejectOutOfRangeExemplar() {
        ISearchCodec<List<ClpsoParticle<Flt64, Flt64>>> codec = RunnerStateCodecs.Clpso(Problem());
        var state = new List<ClpsoParticle<Flt64, Flt64>> {
            new(Individual(1.0), new[] { new Flt64(0.5) }, Individual(0.5), new[] { 0 }, 0),
        };
        JsonNode node = JsonNode.Parse(Unwrap(codec.Encode(state)).GetRawText())!;
        node["children"]!.AsArray()[0]!["counters"]!.AsArray()[1] = 7;

        codec.Decode(JsonDocument.Parse(node.ToJsonString()).RootElement)
            .Should().BeOfType<Failed<List<ClpsoParticle<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**SHADE 状态的往返保留记忆与档案** / The SHADE round-trip preserves the memory and the archive.</summary>
    [Fact]
    public void Shade_ShouldRoundTrip() {
        ISearchCodec<ShadeState<Flt64, Flt64>> codec = RunnerStateCodecs.Shade(Problem());
        var state = new ShadeState<Flt64, Flt64>(
            new[] { Individual(1.0) },
            new ShadeMemory(new[] { 0.4, 0.6 }, new[] { 0.2, 0.8 }, 1),
            new[] { new Solution<Flt64>(new[] { new Flt64(7.0) }) });

        ShadeState<Flt64, Flt64> restored = Unwrap(codec.Decode(Unwrap(codec.Encode(state))));

        restored.Memory.MemoryF[0].Should().Be(0.4);
        restored.Memory.MemoryCr[1].Should().Be(0.8);
        restored.Memory.Cursor.Should().Be(1);
        restored.Population.Should().HaveCount(1);
        restored.Archive.Should().HaveCount(1);
        restored.Archive[0].Values[0].ToDouble().Should().Be(7.0);
    }

    /// <summary>**记忆分量超出 0 到 1 时被拒绝** / A memory component outside 0 to 1 is rejected.</summary>
    /// <remarks>
    /// 越界的 `F` 或 `Cr` 不会让采样失败——它会安静地产出一个不可能的权重，**而下一次采样仍然"成功"**。
    /// An out-of-range `F` or `Cr` does not make sampling fail — it quietly produces an impossible weight, **and the next sample still
    /// "succeeds"**.
    /// </remarks>
    [Fact]
    public void Shade_ShouldRejectOutOfRangeMemory() {
        ISearchCodec<ShadeState<Flt64, Flt64>> codec = RunnerStateCodecs.Shade(Problem());
        var state = new ShadeState<Flt64, Flt64>(
            new[] { Individual(1.0) },
            new ShadeMemory(new[] { 0.4, 0.6 }, new[] { 0.2, 0.8 }, 0),
            Array.Empty<Solution<Flt64>>());
        JsonNode node = JsonNode.Parse(Unwrap(codec.Encode(state)).GetRawText())!;
        node["vectors"]!.AsArray()[0]!.AsArray()[0] = 2.0;

        codec.Decode(JsonDocument.Parse(node.ToJsonString()).RootElement)
            .Should().BeOfType<Failed<ShadeState<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**CMA-ES 状态的往返保留均值、步长、协方差与代数** / The CMA-ES round-trip preserves the mean, step size, covariance and generation.</summary>
    [Fact]
    public void CmaEs_ShouldRoundTrip() {
        ISearchCodec<CmaEsState<Flt64, Flt64>> codec = RunnerStateCodecs.CmaEs(CmaProblem());
        var state = new CmaEsState<Flt64, Flt64>(
            new[] { 0.5 }, 0.3, new[] { new[] { 1.5 } },
            new[] { 0.1 }, new[] { 0.2 }, 4, Individual(1.0));

        CmaEsState<Flt64, Flt64> restored = Unwrap(codec.Decode(Unwrap(codec.Encode(state))));

        restored.Mean[0].Should().Be(0.5);
        restored.Sigma.Should().Be(0.3);
        restored.Covariance[0][0].Should().Be(1.5);
        restored.PathSigma[0].Should().Be(0.1);
        restored.PathCovariance[0].Should().Be(0.2);
        restored.Generation.Should().Be(4);
        restored.Best.Should().NotBeNull();
        restored.Best!.Objective.ToDouble().Should().Be(1.0);
    }

    /// <summary>**均值长度与变量数不符时被拒绝** / A mean whose length differs from the variable count is rejected.</summary>
    /// <remarks>
    /// 不检查会让一份"维度对不上"的检查点恢复成功，**而第一次采样才会以越界或错位的方式失败**。
    /// Skipping it lets a checkpoint with the wrong dimension restore successfully, **and only the first sample then fails, by overrun or by
    /// misalignment**.
    ///
    /// **这条 fixture 必须让"协方差形状"自洽、只让"维度"不自洽**：问题注册了 2 个变量，而状态里的均值只有 1 维、协方差也只有 1 行。
    /// 若改成"给 1 维问题的均值塞两个分量"，那么"协方差行数必须等于均值长度"这条检查会先一步失败，
    /// **于是"维度检查"是否有效就无从判断了**——第一版正是这么写的，变异测试因此通过。
    ///
    /// **This fixture must make the covariance shape self-consistent and only the dimension inconsistent**: the problem registers two
    /// variables while the state's mean and covariance are both one-dimensional. Tampering "add a second component to a one-dimensional
    /// problem's mean" instead trips the "covariance rows must match the mean length" check first, **leaving the dimension check untested** —
    /// the first version did exactly that and the mutation survived.
    /// </remarks>
    [Fact]
    public void CmaEs_ShouldRejectDimensionMismatch() {
        ISearchCodec<CmaEsState<Flt64, Flt64>> codec = RunnerStateCodecs.CmaEs(CmaProblem(2));
        var state = new CmaEsState<Flt64, Flt64>(
            new[] { 0.5 }, 0.3, new[] { new[] { 1.5 } },
            new[] { 0.1 }, new[] { 0.2 }, 4, Individual(1.0));

        codec.Decode(Unwrap(codec.Encode(state)))
            .Should().BeOfType<Failed<CmaEsState<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("expected a successful result");

    /// <summary>构造一个个体 / Build one individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<Flt64, Flt64> Individual(double objective) =>
        new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(objective) }, new Flt64(objective), 0.0);

    /// <summary>构造问题边界 / Build a problem boundary.</summary>
    /// <returns>问题边界 / The problem boundary.</returns>
    private static SearchProblem<Flt64, Flt64> Problem() =>
        new("native", "native", new CodecModel(),
            SearchCodecs.Flt64, SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            validSolution: _ => true,
            admissible: _ => true);

    /// <summary>构造注册了若干 token 的问题边界 / Build a problem boundary that registers some tokens.</summary>
    /// <param name="dimension">token 数量 / The token count.</param>
    /// <returns>问题边界 / The problem boundary.</returns>
    private static SearchProblem<Flt64, Flt64> CmaProblem(int dimension = 1) =>
        new("native-cma", "native-cma", new BoundedStubModel<object?>(dimension, -10.0, 10.0),
            SearchCodecs.Flt64, SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            validSolution: _ => true,
            admissible: _ => true);

    /// <summary>编解码器测试模型 / A codec test model.</summary>
    private sealed class CodecModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
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
