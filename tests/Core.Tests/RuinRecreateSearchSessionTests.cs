// 破坏—重建搜索会话测试 / Tests for the ruin-and-recreate search session.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Neighborhood;
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
/// 破坏—重建搜索会话 / The ruin-and-recreate search session.
/// </summary>
public sealed class RuinRecreateSearchSessionTests {
    /// <summary>**非排列编码被拒绝** / A non-permutation encoding is rejected.</summary>
    /// <remarks>
    /// 放到连续编码上，"移走若干元素再插回去"会变成"改几个坐标"，**而算法名字与调用方式都还成立**。
    /// Under a continuous encoding, "remove some elements and insert them back" degenerates into "edit a few coordinates", **while the
    /// algorithm's name and call signature still hold**.
    /// </remarks>
    [Fact]
    public async Task Step_ShouldRejectNonPermutationEncoding() {
        RuinRecreateSearchSession<Flt64, Flt64> session = BuildSession(
            SearchEncoding.Continuous);

        Result<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result =
            await session.Step(Slice(4));

        result.Should().BeOfType<Failed<SearchStepResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**预算步数为 0 时一步都不走** / A budget of zero steps performs no work.</summary>
    [Fact]
    public async Task Step_ShouldRespectStepBudget() {
        RuinRecreateSearchSession<Flt64, Flt64> session = BuildSession(SearchEncoding.Permutation);

        SearchStepResult<Flt64, Flt64> result = Unwrap(await session.Step(Slice(0)));

        result.Steps.Should().Be(0);
        result.Candidates.Should().BeEmpty();
        result.Termination.Should().Be(SearchTermination.SliceComplete);
    }

    /// <summary>**第一次步进用模型的初始解，之后走破坏—重建** / The first step uses the model's initial solution, later ones ruin and recreate.</summary>
    [Fact]
    public async Task Step_ShouldProduceCandidates() {
        RuinRecreateSearchSession<Flt64, Flt64> session = BuildSession(SearchEncoding.Permutation);

        SearchStepResult<Flt64, Flt64> result = Unwrap(await session.Step(Slice(3)));

        result.Steps.Should().Be(3);
        result.Candidates.Should().NotBeEmpty();
        result.Evaluations.Should().Be(result.Candidates.Count,
            "每个候选恰好消耗一次评估 / every candidate consumes exactly one evaluation");
        session.Current.Should().NotBeNull();
        session.Iterations.Should().Be(3);
    }

    /// <summary>**导入的解同样被评估并推进当前解** / Imported solutions are evaluated and advance the current one.</summary>
    [Fact]
    public async Task Import_ShouldEvaluateAndAdvance() {
        RuinRecreateSearchSession<Flt64, Flt64> session = BuildSession(SearchEncoding.Permutation);

        SearchStepResult<Flt64, Flt64> result = Unwrap(await session.ImportSolutions(
            new[] {
                new Solution<Flt64>(new[] { new Flt64(4.0), new Flt64(3.0) }),
                new Solution<Flt64>(new[] { new Flt64(0.5), new Flt64(0.5) }),
            },
            Slice(4)));

        result.Candidates.Should().HaveCount(2);
        result.Steps.Should().Be(0, "导入不推进步数 / an import does not advance the step count");
        session.Current.Should().NotBeNull();
        session.Current!.Objective.ToDouble().Should().Be(1.0,
            "第二个解更优（1 对 7），必须替换当前解 / the second solution is better (1 against 7) and must replace the current one");
    }

    /// <summary>**没有声明域估计状态时拒绝快照** / The snapshot refuses without a declared estimate state.</summary>
    [Fact]
    public void Snapshot_ShouldRequireEstimateDeclaration() {
        RuinRecreateSearchSession<Flt64, Flt64> session = BuildSession(SearchEncoding.Permutation);

        session.Snapshot()
            .Should().BeOfType<Failed<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**快照与恢复能往返** / The snapshot and restore round-trip.</summary>
    [Fact]
    public async Task Snapshot_ShouldRoundTrip() {
        RuinRecreateSearchSession<Flt64, Flt64> first = BuildSession(
            SearchEncoding.Permutation, statelessEstimates: true);
        await first.Step(Slice(2));

        SearchSessionSnapshot snapshot = Unwrap(first.Snapshot());
        RuinRecreateSearchSession<Flt64, Flt64> second = BuildSession(
            SearchEncoding.Permutation, statelessEstimates: true);
        Unwrap(second.Restore(snapshot));

        second.Current.Should().NotBeNull();
        second.Iterations.Should().Be(first.Iterations);
        second.Current!.Objective.ToDouble().Should().Be(first.Current!.Objective.ToDouble());
        second.Current.Solution.Should().BeEquivalentTo(first.Current.Solution);
    }

    /// <summary>**初始化标志与解不一致时拒绝恢复** / Restore rejects a mismatched initialisation flag.</summary>
    [Fact]
    public async Task Restore_ShouldRejectInconsistentFlag() {
        RuinRecreateSearchSession<Flt64, Flt64> first = BuildSession(
            SearchEncoding.Permutation, statelessEstimates: true);
        await first.Step(Slice(1));
        SearchSessionSnapshot snapshot = Unwrap(first.Snapshot());
        SearchSessionSnapshot tampered = snapshot with { Initialized = false };

        RuinRecreateSearchSession<Flt64, Flt64> second = BuildSession(
            SearchEncoding.Permutation, statelessEstimates: true);

        second.Restore(tampered)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**解无效时拒绝恢复** / Restore rejects an invalid candidate.</summary>
    [Fact]
    public async Task Restore_ShouldRejectInvalidCandidate() {
        RuinRecreateSearchSession<Flt64, Flt64> first = BuildSession(
            SearchEncoding.Permutation, statelessEstimates: true);
        await first.Step(Slice(1));
        SearchSessionSnapshot snapshot = Unwrap(first.Snapshot());
        RuinRecreateSearchSession<Flt64, Flt64> second = BuildSession(
            SearchEncoding.Permutation, statelessEstimates: true, validSolution: _ => false);

        second.Restore(snapshot)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造一个预算片段 / Build a budget slice.</summary>
    /// <param name="steps">步数 / The step count.</param>
    /// <returns>预算片段 / The slice.</returns>
    private static SearchSlice Slice(long steps) =>
        new(new SearchBudget(1000), 1000, steps);

    /// <summary>构造会话 / Build a session.</summary>
    /// <param name="encoding">解编码 / The solution encoding.</param>
    /// <param name="statelessEstimates">域估计是否无状态 / Whether the domain estimate is stateless.</param>
    /// <param name="validSolution">解有效性判据 / The solution-validity predicate.</param>
    /// <returns>会话 / The session.</returns>
    private static RuinRecreateSearchSession<Flt64, Flt64> BuildSession(
        SearchEncoding encoding,
        bool statelessEstimates = false,
        Func<IReadOnlyList<Flt64>, bool>? validSolution = null) {
        var problem = new SearchProblem<Flt64, Flt64>(
            "ruin", "ruin-fingerprint", new RuinModel(),
            SearchCodecs.Flt64, SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            encoding: encoding,
            validSolution: validSolution ?? (_ => true),
            admissible: _ => true);
        var ruin = new RuinRecreateOperator<Flt64>(
            new RandomRuin<Flt64>(),
            new GreedyInsertRepair<Flt64>(new SumCost()));
        return new RuinRecreateSearchSession<Flt64, Flt64>(
            problem, ruin, 2, new SearchRandom(17L),
            statelessEstimates: statelessEstimates);
    }

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new InvalidOperationException("expected a successful result");

    /// <summary>求和插入代价 / A summed insertion cost.</summary>
    private sealed class SumCost : IInsertionCost<Flt64> {
        /// <inheritdoc/>
        public Result<Flt64?, ErrorCode, Error<ErrorCode>> Cost(
            IReadOnlyList<Flt64> partial, Flt64 item, int position) {
            double total = item.ToDouble();
            foreach (Flt64 value in partial) {
                total += value.ToDouble();
            }
            return Results.Ok<Flt64?>(new Flt64(total));
        }
    }

    /// <summary>会话测试模型 / A session test model.</summary>
    private sealed class RuinModel : IAbstractCallBackModelInterface<object?, Flt64, Flt64> {
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
        public IReadOnlyList<IReadOnlyList<Flt64>> InitialSolutions(
            ulong initialSolutionAmount = 1) =>
            new[] {
                (IReadOnlyList<Flt64>)new[] { new Flt64(1.0), new Flt64(2.0) },
            };

        /// <inheritdoc/>
        public Flt64 Operation(Flt64 lhs, Flt64 rhs) => lhs;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue() => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 ObjectiveValue(object? obj) => DefaultObjective;

        /// <inheritdoc/>
        public Flt64 Objective(IReadOnlyList<Flt64> solution) {
            double total = 0.0;
            foreach (Flt64 value in solution) {
                total += value.ToDouble();
            }
            return new Flt64(total);
        }

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
