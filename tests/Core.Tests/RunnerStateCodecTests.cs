// 运行器状态编解码器测试 / Tests for the runner state codecs.

using System;
using System.Collections.Generic;
using System.Text.Json;
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
/// 运行器状态编解码器 / The runner state codecs.
/// </summary>
public sealed class RunnerStateCodecTests {
    /// <summary>**种群状态往返一致** / A population state round-trips.</summary>
    [Fact]
    public void Population_ShouldRoundTrip() {
        ISearchCodec<List<IIndividual<Flt64, Flt64>>> codec =
            RunnerStateCodecs.Population(Problem());
        var population = new List<IIndividual<Flt64, Flt64>> {
            Individual(1.0), Individual(2.5), Individual(-3.0),
        };

        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = codec.Encode(population);
        encoded.Should().BeOfType<Ok<JsonElement, ErrorCode, Error<ErrorCode>>>();
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> decoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);

        decoded.Should()
            .BeOfType<Ok<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
        List<IIndividual<Flt64, Flt64>> restored =
            ((Ok<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)decoded).Value;
        restored.Should().HaveCount(3);
        restored[0].Objective.ToDouble().Should().Be(1.0);
        restored[1].Solution[0].ToDouble().Should().Be(2.5);
        restored[2].Objective.ToDouble().Should().Be(-3.0);
    }

    /// <summary>**空种群可以往返** / An empty population round-trips.</summary>
    [Fact]
    public void Population_ShouldRoundTripEmpty() {
        ISearchCodec<List<IIndividual<Flt64, Flt64>>> codec =
            RunnerStateCodecs.Population(Problem());

        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded =
            codec.Encode(new List<IIndividual<Flt64, Flt64>>());
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> decoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);

        ((Ok<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>)decoded).Value
            .Should().BeEmpty();
    }

    /// <summary>**岛群的嵌套结构被保留** / The island nesting is preserved.</summary>
    [Fact]
    public void Islands_ShouldPreserveNesting() {
        ISearchCodec<List<List<IIndividual<Flt64, Flt64>>>> codec =
            RunnerStateCodecs.Islands(Problem());
        var islands = new List<List<IIndividual<Flt64, Flt64>>> {
            new() { Individual(1.0), Individual(2.0) },
            new() { Individual(3.0) },
        };

        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = codec.Encode(islands);
        Result<List<List<IIndividual<Flt64, Flt64>>>, ErrorCode, Error<ErrorCode>> decoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);

        List<List<IIndividual<Flt64, Flt64>>> restored =
            ((Ok<List<List<IIndividual<Flt64, Flt64>>>, ErrorCode, Error<ErrorCode>>)decoded).Value;
        restored.Should().HaveCount(2);
        restored[0].Should().HaveCount(2);
        restored[1].Should().HaveCount(1);
        restored[1][0].Objective.ToDouble().Should().Be(3.0);
    }

    /// <summary>**空岛群可以往返** / An empty island group round-trips.</summary>
    [Fact]
    public void Islands_ShouldRoundTripEmpty() {
        ISearchCodec<List<List<IIndividual<Flt64, Flt64>>>> codec =
            RunnerStateCodecs.Islands(Problem());

        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded =
            codec.Encode(new List<List<IIndividual<Flt64, Flt64>>>());
        Result<List<List<IIndividual<Flt64, Flt64>>>, ErrorCode, Error<ErrorCode>> decoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);

        ((Ok<List<List<IIndividual<Flt64, Flt64>>>, ErrorCode, Error<ErrorCode>>)decoded).Value
            .Should().BeEmpty();
    }

    /// <summary>**轨迹的"未初始化"与"有成员"是两种不同编码** / A trajectory's "uninitialized" and "has a member" are distinct encodings.</summary>
    /// <remarks>
    /// 用空列表表示"未初始化"会让这两种状态塌缩成同一个编码，**而恢复后的会话无法区分**：前者应当继续推进，后者应当重新初始化。
    /// Using an empty list for "uninitialized" collapses both states into the same encoding, **and a restored session cannot tell them
    /// apart**: the former should keep advancing, the latter should re-initialize.
    /// </remarks>
    [Fact]
    public void Trajectory_ShouldDistinguishNullFromMember() {
        ISearchCodec<IIndividual<Flt64, Flt64>?> codec = RunnerStateCodecs.Trajectory(Problem());

        Result<JsonElement, ErrorCode, Error<ErrorCode>> nullEncoded = codec.Encode(null);
        Result<JsonElement, ErrorCode, Error<ErrorCode>> memberEncoded = codec.Encode(Individual(4.0));

        ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)nullEncoded).Value.ValueKind
            .Should().Be(JsonValueKind.Null);
        ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)memberEncoded).Value.ValueKind
            .Should().Be(JsonValueKind.Object);

        Result<IIndividual<Flt64, Flt64>?, ErrorCode, Error<ErrorCode>> nullDecoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)nullEncoded).Value);
        Result<IIndividual<Flt64, Flt64>?, ErrorCode, Error<ErrorCode>> memberDecoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)memberEncoded).Value);

        ((Ok<IIndividual<Flt64, Flt64>?, ErrorCode, Error<ErrorCode>>)nullDecoded).Value
            .Should().BeNull("未初始化必须恢复成 null / uninitialized must restore to null");
        ((Ok<IIndividual<Flt64, Flt64>?, ErrorCode, Error<ErrorCode>>)memberDecoded).Value!
            .Objective.ToDouble().Should().Be(4.0);
    }

    /// <summary>**维度不符的快照在解码时被拒绝** / A wrongly dimensioned snapshot is rejected on decode.</summary>
    /// <remarks>
    /// 只校验 JSON 形状会让一份"形状正确、维度错误"的快照通过解码，**而错误在若干步之后才以"难以置信的目标值"的形式爆发**。
    /// Checking only the JSON shape lets a "correctly shaped, wrongly dimensioned" snapshot decode, **and the error only surfaces several
    /// steps later as an "impossible objective"**.
    /// </remarks>
    [Fact]
    public void Decode_ShouldRejectWrongDimensions() {
        // 用一个只接受单元素解的编码器写出两元素解，再用正常问题解码。
        // Write a two-element solution with a codec that accepts it, then decode it with the normal one.
        ISearchCodec<List<IIndividual<Flt64, Flt64>>> permissive =
            RunnerStateCodecs.Population(Problem(allowAnyDimension: true));
        var twoDimensional = new List<IIndividual<Flt64, Flt64>> {
            new SolutionWithFitness<Flt64, Flt64>(
                new[] { new Flt64(1.0), new Flt64(2.0) }, new Flt64(3.0), 0.0),
        };
        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = permissive.Encode(twoDimensional);
        encoded.Should().BeOfType<Ok<JsonElement, ErrorCode, Error<ErrorCode>>>();

        ISearchCodec<List<IIndividual<Flt64, Flt64>>> strict =
            RunnerStateCodecs.Population(Problem());
        Result<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>> decoded =
            strict.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);

        decoded.Should()
            .BeOfType<Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**畸形的个体编码被拒绝** / A malformed individual encoding is rejected.</summary>
    [Fact]
    public void Decode_ShouldRejectMalformedIndividual() {
        ISearchCodec<List<IIndividual<Flt64, Flt64>>> codec =
            RunnerStateCodecs.Population(Problem());

        codec.Decode(JsonDocument.Parse("{\"solution\":[1.0]}").RootElement)
            .Should().BeOfType<Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
        codec.Decode(JsonDocument.Parse("[1,2,3]").RootElement)
            .Should().BeOfType<Failed<List<IIndividual<Flt64, Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造一个个体 / Build one individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<Flt64, Flt64> Individual(double objective) =>
        new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(objective) }, new Flt64(objective), 0.0);

    /// <summary>构造问题边界 / Build a problem boundary.</summary>
    /// <param name="allowAnyDimension">是否接受任意维度 / Whether any dimension is accepted.</param>
    /// <returns>问题边界 / The problem boundary.</returns>
    private static SearchProblem<Flt64, Flt64> Problem(bool allowAnyDimension = false) =>
        new("codec", "codec", new CodecModel(),
            SearchCodecs.Flt64, SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            // 默认维度判定要求解长度等于模型的 token 数；测试模型没有注册 token，因此显式给出判定。
            // The default dimension predicate requires the solution length to equal the model's token count; the test model registers
            // no tokens, so the predicate is explicit.
            validSolution: solution => allowAnyDimension || solution.Count == 1);

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
