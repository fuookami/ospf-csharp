// 无损编解码测试 / Tests for the lossless codecs.

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 无损编解码 / The lossless codecs.
/// </summary>
public sealed class SearchCodecsTests {
    /// <summary>**`Flt64` 往返无损** / `Flt64` round-trips losslessly.</summary>
    [Fact]
    public void Flt64_ShouldRoundTrip() {
        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = SearchCodecs.Flt64.Encode(new Flt64(3.25));
        Result<Flt64, ErrorCode, Error<ErrorCode>> decoded =
            SearchCodecs.Flt64.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);

        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)decoded).Value.Should().Be(new Flt64(3.25));
    }

    /// <summary>**非有限值编码被拒绝** / Non-finite values are rejected on encode.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Flt64_ShouldRejectNonFiniteOnEncode(double value) =>
        SearchCodecs.Flt64.Encode(new Flt64(value)).IsOk.Should().BeFalse(
            "非有限值会毒化目标比较 / non-finite values poison objective comparison");

    /// <summary>**非有限值解码被拒绝** / Non-finite text is rejected on decode.</summary>
    [Fact]
    public void Flt64_ShouldRejectNonFiniteOnDecode() {
        JsonElement nan = JsonSerializer.SerializeToElement("NaN");

        SearchCodecs.Flt64.Decode(nan).IsOk.Should().BeFalse();
    }

    /// <summary>非数值解码被拒绝 / Non-numeric text is rejected on decode.</summary>
    [Fact]
    public void Flt64_ShouldRejectNonNumeric() {
        JsonElement text = JsonSerializer.SerializeToElement("not-a-number");

        SearchCodecs.Flt64.Decode(text).IsOk.Should().BeFalse();
    }

    /// <summary>**列表往返保序** / A list round-trips in order.</summary>
    [Fact]
    public void List_ShouldRoundTripInOrder() {
        ISearchCodec<List<Flt64>> codec = SearchCodecs.List(SearchCodecs.Flt64);
        var input = new List<Flt64> { new(1.0), new(2.0), new(3.0) };

        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = codec.Encode(input);
        Result<List<Flt64>, ErrorCode, Error<ErrorCode>> decoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);

        ((Ok<List<Flt64>, ErrorCode, Error<ErrorCode>>)decoded).Value.Should().Equal(input);
    }

    /// <summary>**列表遇到首个失败整体失败** / A list fails as a whole on the first element failure.</summary>
    [Fact]
    public void List_ShouldFailWholeListOnFirstElementFailure() {
        ISearchCodec<List<Flt64>> codec = SearchCodecs.List(SearchCodecs.Flt64);
        JsonElement array = JsonSerializer.SerializeToElement(new[] { 1.0, 2.0 });

        // 构造一个含非法元素的数组：第二个元素是文本。
        JsonElement broken = JsonSerializer.SerializeToElement(new object[] { 1.0, "NaN", 3.0 });

        codec.Decode(broken).IsOk.Should().BeFalse("坏元素必须让整个列表失败 / a bad element must fail the whole list");
        codec.Decode(array).IsOk.Should().BeTrue();
    }

    /// <summary>**列表拒绝非数组** / A list rejects non-array input.</summary>
    [Fact]
    public void List_ShouldRejectNonArray() {
        ISearchCodec<List<Flt64>> codec = SearchCodecs.List(SearchCodecs.Flt64);

        codec.Decode(JsonSerializer.SerializeToElement(42)).IsOk.Should().BeFalse();
    }

    /// <summary>**空列表往返** / An empty list round-trips.</summary>
    [Fact]
    public void List_ShouldRoundTripEmpty() {
        ISearchCodec<List<Flt64>> codec = SearchCodecs.List(SearchCodecs.Flt64);
        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = codec.Encode(new List<Flt64>());
        Result<List<Flt64>, ErrorCode, Error<ErrorCode>> decoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);

        ((Ok<List<Flt64>, ErrorCode, Error<ErrorCode>>)decoded).Value.Should().BeEmpty();
    }

    /// <summary>**可空编码把 null 映射为 JSON null** / The nullable codec maps null to JSON null.</summary>
    [Fact]
    public void Nullable_ShouldRoundTripNull() {
        ISearchCodec<string?> codec = SearchCodecs.Nullable(SearchCodecs.Serialized<string>());

        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = codec.Encode(null);
        ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value.ValueKind.Should().Be(JsonValueKind.Null);

        Result<string?, ErrorCode, Error<ErrorCode>> decoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);
        ((Ok<string?, ErrorCode, Error<ErrorCode>>)decoded).Value.Should().BeNull();
    }

    /// <summary>**可空编码透传非 null 值** / The nullable codec passes non-null values through.</summary>
    [Fact]
    public void Nullable_ShouldRoundTripValue() {
        ISearchCodec<string?> codec = SearchCodecs.Nullable(SearchCodecs.Serialized<string>());
        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = codec.Encode("hello");
        Result<string?, ErrorCode, Error<ErrorCode>> decoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);

        ((Ok<string?, ErrorCode, Error<ErrorCode>>)decoded).Value.Should().Be("hello");
    }

    /// <summary>
    /// **个体解码重新校验维度** / Individual decoding revalidates dimensions.
    /// </summary>
    /// <remarks>
    /// 只校验 JSON 形状会让"形状正确但维度不符"的状态恢复成功，随后在搜索里以越界或错位的形式暴露。
    /// Checking only the JSON shape would let a correctly shaped but dimensionally wrong state restore, then fail inside the search.
    /// </remarks>
    [Fact]
    public void Individual_ShouldRejectWrongDimension() {
        ISearchCodec<IIndividual<Flt64, Flt64>> codec =
            SearchCodecs.Individual(Problem(validDimension: 2));
        ISearchCodec<IIndividual<Flt64, Flt64>> writer =
            SearchCodecs.Individual(Problem(validDimension: 1));
        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded =
            writer.Encode(new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(1.0) }, new Flt64(1.0)));

        codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value).IsOk
            .Should().BeFalse("维度不符必须拒绝 / a dimension mismatch must be rejected");
    }

    /// <summary>**个体往返保留解与目标** / An individual round-trips solution and objective.</summary>
    [Fact]
    public void Individual_ShouldRoundTrip() {
        ISearchCodec<IIndividual<Flt64, Flt64>> codec = SearchCodecs.Individual(Problem(validDimension: 2));
        var input = new EvaluatedIndividual<Flt64, Flt64>(
            new[] { new Flt64(1.5), new Flt64(2.5) }, new Flt64(4.0));

        Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = codec.Encode(input);
        Result<IIndividual<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> decoded =
            codec.Decode(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value);

        IIndividual<Flt64, Flt64> value = ((Ok<IIndividual<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)decoded).Value;
        value.Solution.Should().Equal(input.Solution);
        value.Objective.Should().Be(input.Objective);
    }

    /// <summary>**个体编码拒绝非对象** / Individual decoding rejects non-object input.</summary>
    [Fact]
    public void Individual_ShouldRejectNonObject() {
        ISearchCodec<IIndividual<Flt64, Flt64>> codec = SearchCodecs.Individual(Problem(validDimension: 1));

        codec.Decode(JsonSerializer.SerializeToElement(7)).IsOk.Should().BeFalse();
    }

    /// <summary>**个体编码拒绝缺字段** / Individual decoding rejects missing fields.</summary>
    [Fact]
    public void Individual_ShouldRejectMissingFitness() {
        ISearchCodec<IIndividual<Flt64, Flt64>> codec = SearchCodecs.Individual(Problem(validDimension: 1));
        JsonElement onlySolution = JsonSerializer.SerializeToElement(
            new Dictionary<string, object> { ["solution"] = new[] { 1.0 } });

        codec.Decode(onlySolution).IsOk.Should().BeFalse();
    }

    /// <summary>构造一个简化问题 / Build a simplified problem.</summary>
    private static SearchProblem<Flt64, Flt64> Problem(int validDimension) =>
        new("p", "fp", new StubModel(value => value[0]), SearchCodecs.Flt64, SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() > rhs.ToDouble(),
            validSolution: solution => solution.Count == validDimension,
            admissible: _ => true);
}
