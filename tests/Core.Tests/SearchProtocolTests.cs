// 搜索协议测试 / Tests for the search protocol.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 搜索协议 / The search protocol.
/// </summary>
public sealed class SearchProtocolTests {
    /// <summary>**多目标下 `Best` 必须为 null** / `Best` is null under multiobjective search.</summary>
    [Fact]
    public void SearchResult_ShouldNotFabricateBestForMultiObjective() {
        var result = new SearchResult<Flt64, Flt64>(
            new IIndividual<Flt64, Flt64>[] { Individual(1.0), Individual(2.0) },
            MultiObjective: true,
            SearchTermination.SliceComplete,
            10, 3, 10, 1000);

        result.Best.Should().BeNull("多目标没有唯一最优 / multiobjective has no unique optimum");
    }

    /// <summary>单目标下 `Best` 取档案首个 / `Best` is the first archive entry in single-objective search.</summary>
    [Fact]
    public void SearchResult_ShouldReturnFirstArchiveEntryForSingleObjective() {
        IIndividual<Flt64, Flt64> first = Individual(1.0);
        var result = new SearchResult<Flt64, Flt64>(
            new IIndividual<Flt64, Flt64>[] { first, Individual(2.0) },
            MultiObjective: false,
            SearchTermination.SliceComplete,
            10, 3, 10, 1000);

        result.Best.Should().BeSameAs(first);
    }

    /// <summary>空档案的单目标结果不伪造最优 / An empty archive yields no optimum.</summary>
    [Fact]
    public void SearchResult_ShouldReturnNullForEmptyArchive() {
        var result = new SearchResult<Flt64, Flt64>(
            Array.Empty<IIndividual<Flt64, Flt64>>(),
            MultiObjective: false,
            SearchTermination.NoCandidate,
            0, 0, 0, 0);

        result.Best.Should().BeNull();
    }

    /// <summary>诊断列表默认不为 null / The diagnostics list defaults to empty, never null.</summary>
    [Fact]
    public void SearchResult_ShouldDefaultDiagnosticsToEmpty() {
        var result = new SearchResult<Flt64, Flt64>(
            Array.Empty<IIndividual<Flt64, Flt64>>(),
            MultiObjective: false,
            SearchTermination.SliceComplete,
            0, 0, 0, 0);

        result.Diagnostics.Should().NotBeNull().And.BeEmpty();
    }

    /// <summary>**边界把异常转换为失败结果** / The boundary converts exceptions into failures.</summary>
    [Fact]
    public void Boundary_ShouldConvertExceptionToFailure() {
        Result<int, ErrorCode, Error<ErrorCode>> result =
            SearchBoundary.Execute<int>(() => throw new InvalidOperationException("boom"));

        result.IsOk.Should().BeFalse("异常必须变成可判断的失败 / an exception must become a judgeable failure");
        ((Failed<int, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("boom");
    }

    /// <summary>边界原样透传成功值 / The boundary passes successful values through.</summary>
    [Fact]
    public void Boundary_ShouldPassThroughSuccess() =>
        ((Ok<int, ErrorCode, Error<ErrorCode>>)SearchBoundary.Execute(() => 42)).Value.Should().Be(42);

    /// <summary>边界拒绝空委托 / The boundary rejects a null delegate.</summary>
    [Fact]
    public void Boundary_ShouldRejectNullBlock() =>
        ((Func<object>)(() => SearchBoundary.Execute<int>(null!)))
            .Should().Throw<ArgumentNullException>();

    /// <summary>**会话默认不支持导入** / A session does not support import by default.</summary>
    [Fact]
    public void Session_ShouldNotSupportImportByDefault() =>
        ((ISearchSession<Flt64, Flt64>)new MinimalSession()).SupportsImport.Should().BeFalse();

    /// <summary>默认关闭是成功 / The default `Close` succeeds.</summary>
    [Fact]
    public void Session_ShouldCloseSuccessfullyByDefault() =>
        ((ISearchSession<Flt64, Flt64>)new MinimalSession()).Close().IsOk.Should().BeTrue();

    /// <summary>快照默认版本为 1 / A snapshot defaults to protocol version 1.</summary>
    [Fact]
    public void Snapshot_ShouldDefaultToVersionOne() {
        var snapshot = new SearchSessionSnapshot(
            Initialized: false, Iteration: 0, Stagnation: 0,
            State: JsonDocument.Parse("{}").RootElement,
            Archive: JsonDocument.Parse("[]").RootElement);

        snapshot.Version.Should().Be(1);
        snapshot.ActiveNanos.Should().Be(0);
        snapshot.Constraints.Should().BeNull();
    }

    /// <summary>构造一个个体 / Build an individual.</summary>
    private static IIndividual<Flt64, Flt64> Individual(double objective) =>
        new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(objective) }, new Flt64(objective), objective);

    /// <summary>最小会话：只实现四个必需成员 / A minimal session implementing only the four required members.</summary>
    private sealed class MinimalSession : ISearchSession<Flt64, Flt64> {
        /// <inheritdoc/>
        public Task<Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>> Step(SearchSlice budget) =>
            Task.FromResult(Results.Failed<SearchStepResult<Flt64, Flt64>>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "未实现 / not implemented")));

        /// <inheritdoc/>
        public Task<Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>> ImportSolutions(
            IReadOnlyList<Solution<Flt64>> solutions, SearchSlice budget) =>
            Task.FromResult(Results.Failed<SearchStepResult<Flt64, Flt64>>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "未实现 / not implemented")));

        /// <inheritdoc/>
        public Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> Snapshot() =>
            Results.Failed<SearchSessionSnapshot>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "未实现 / not implemented"));

        /// <inheritdoc/>
        public Try Restore(SearchSessionSnapshot snapshot) =>
            Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationError, "未实现 / not implemented"));
    }
}
