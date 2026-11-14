// 目标视图与计费评估测试 / Tests for the objective view and the metered evaluation.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 目标视图 / The objective view.
/// </summary>
public sealed class SearchModelsTests {
    /// <summary>**视图的 `Objective` 走替换后的口径** / The view's `Objective` uses the replaced convention.</summary>
    [Fact]
    public void ObjectiveModel_ShouldReplaceObjective() {
        var source = new StubModel(value => value[0].Times(new Flt64(2.0)));
        IAbstractCallBackModelInterface<object?, Flt64, Flt64> view = SearchModels.ObjectiveModel<object?, Flt64, Flt64, Flt64>(
            source,
            new Flt64(0.0),
            solution => solution[0].Plus(new Flt64(100.0)),
            (lhs, rhs) => lhs!.CompareTo(rhs!) is int order ? OrderHelpers.OrderOf(order) : null);

        view.Objective(new[] { new Flt64(1.0) }).Should().Be(new Flt64(101.0), "视图必须用自己的口径 / the view uses its own convention");
        source.Objective(new[] { new Flt64(1.0) }).Should().Be(new Flt64(2.0));
    }

    /// <summary>**视图沿用原模型的变量表** / The view reuses the source token table.</summary>
    [Fact]
    public void ObjectiveModel_ShouldReuseTokens() {
        var source = new StubModel(value => value[0]);
        IAbstractCallBackModelInterface<object?, Flt64, Flt64> view = SearchModels.ObjectiveModel<object?, Flt64, Flt64, Flt64>(
            source, new Flt64(0.0), solution => solution[0], (lhs, rhs) => OrderHelpers.OrderOf(lhs.CompareTo(rhs)));

        view.Tokens.Should().BeSameAs(source.Tokens, "变量仍来自原模型 / the variables still come from the source");
    }

    /// <summary>**视图清空目标函数提取器** / The view empties the objective-function extractors.</summary>
    [Fact]
    public void ObjectiveModel_ShouldEmptyObjectiveFunctions() {
        var source = new StubModel(value => value[0]);
        IAbstractCallBackModelInterface<object?, Flt64, Flt64> view = SearchModels.ObjectiveModel<object?, Flt64, Flt64, Flt64>(
            source, new Flt64(0.0), solution => solution[0], (lhs, rhs) => OrderHelpers.OrderOf(lhs.CompareTo(rhs)));

        view.ObjectiveFunctions.Should().BeEmpty("旧目标的提取器必须清空 / the old objective's extractors must be cleared");
        source.ObjectiveFunctions.Should().NotBeEmpty();
    }

    /// <summary>**`Operation` 返回左操作数** / `Operation` returns the left operand.</summary>
    [Fact]
    public void ObjectiveModel_ShouldReturnLeftOperandOnOperation() {
        var source = new StubModel(value => value[0]);
        IAbstractCallBackModelInterface<object?, Flt64, Flt64> view = SearchModels.ObjectiveModel<object?, Flt64, Flt64, Flt64>(
            source, new Flt64(0.0), solution => solution[0], (lhs, rhs) => OrderHelpers.OrderOf(lhs.CompareTo(rhs)));

        view.Operation(new Flt64(3.0), new Flt64(7.0)).Should().Be(new Flt64(3.0));
    }

    /// <summary>**视图沿用原模型的可行性判定** / The view reuses the source feasibility verdict.</summary>
    [Fact]
    public void ObjectiveModel_ShouldReuseConstraintCheck() {
        var source = new StubModel(value => value[0]) { Feasible = true };
        IAbstractCallBackModelInterface<object?, Flt64, Flt64> view = SearchModels.ObjectiveModel<object?, Flt64, Flt64, Flt64>(
            source, new Flt64(0.0), solution => solution[0], (lhs, rhs) => OrderHelpers.OrderOf(lhs.CompareTo(rhs)));

        view.ConstraintSatisfied(new[] { new Flt64(1.0) }).Should().BeTrue();
    }

    /// <summary>构造要求原模型非空 / Construction requires a non-null source.</summary>
    [Fact]
    public void ObjectiveModel_ShouldRejectNullSource() =>
        ((Func<IAbstractCallBackModelInterface<object?, Flt64, Flt64>>)(() =>
            SearchModels.ObjectiveModel<object?, Flt64, Flt64, Flt64>(
                null!, new Flt64(0.0), solution => new Flt64(0.0), (lhs, rhs) => OrderHelpers.OrderOf(lhs.CompareTo(rhs)))))
            .Should().Throw<ArgumentNullException>();
}

/// <summary>
/// 计费评估 / The metered evaluation.
/// </summary>
public sealed class SearchEvaluationTests {
    /// <summary>**没拿到额度时不消耗目标调用** / A denied admission records no objective call.</summary>
    [Fact]
    public void Evaluate_ShouldNotCallObjectiveWhenDenied() {
        var budget = new SearchBudget(1);
        var slice = new SearchSlice(budget, evaluations: 1);
        slice.Acquire();
        SearchProblem<Flt64, Flt64> problem = Problem();

        SearchEvaluationResult<Flt64, Flt64> result = Unwrap(problem.Evaluate(Solution(1.0), slice));

        result.Admitted.Should().BeFalse();
        result.Candidate.Should().BeNull();
        budget.ObjectiveCalls.Should().Be(0, "被拒绝的评估不该调用目标 / a denied evaluation must not call the objective");
    }

    /// <summary>**有效候选被返回并记录目标调用** / A valid candidate is returned and the objective call recorded.</summary>
    [Fact]
    public void Evaluate_ShouldReturnCandidateForValidSolution() {
        var budget = new SearchBudget(10);
        var slice = new SearchSlice(budget, evaluations: 5);
        SearchProblem<Flt64, Flt64> problem = Problem();

        SearchEvaluationResult<Flt64, Flt64> result = Unwrap(problem.Evaluate(Solution(2.0), slice));

        result.Admitted.Should().BeTrue();
        result.Candidate.Should().NotBeNull();
        result.Candidate!.Objective.Should().Be(new Flt64(2.0));
        budget.ObjectiveCalls.Should().Be(1);
    }

    /// <summary>**不可行的候选仍算消耗额度，但不产出候选** / An infeasible candidate still consumes the token but yields no candidate.</summary>
    [Fact]
    public void Evaluate_ShouldConsumeTokenForInfeasibleCandidate() {
        var budget = new SearchBudget(10);
        var slice = new SearchSlice(budget, evaluations: 5);
        SearchProblem<Flt64, Flt64> problem = Problem(admissible: _ => false);

        SearchEvaluationResult<Flt64, Flt64> result = Unwrap(problem.Evaluate(Solution(2.0), slice));

        result.Admitted.Should().BeTrue("额度已经花掉 / the token is already spent");
        result.Candidate.Should().BeNull();
        slice.Used.Should().Be(1);
        budget.ObjectiveCalls.Should().Be(0, "没有调用目标就没有调用计数 / no objective call, no call count");
    }

    /// <summary>**维度不符的解不产出候选** / A dimension-mismatched solution yields no candidate.</summary>
    [Fact]
    public void Evaluate_ShouldRejectInvalidDimension() {
        var budget = new SearchBudget(10);
        var slice = new SearchSlice(budget, evaluations: 5);
        SearchProblem<Flt64, Flt64> problem = Problem();

        SearchEvaluationResult<Flt64, Flt64> result = Unwrap(problem.Evaluate(new Solution<Flt64>(Array.Empty<Flt64>()), slice));

        result.Admitted.Should().BeTrue();
        result.Candidate.Should().BeNull();
    }

    /// <summary>**目标无效时不产出候选** / An invalid objective yields no candidate.</summary>
    [Fact]
    public void Evaluate_ShouldRejectInvalidObjective() {
        var budget = new SearchBudget(10);
        var slice = new SearchSlice(budget, evaluations: 5);
        SearchProblem<Flt64, Flt64> problem = Problem(valid: _ => false);

        SearchEvaluationResult<Flt64, Flt64> result = Unwrap(problem.Evaluate(Solution(2.0), slice));

        result.Admitted.Should().BeTrue();
        result.Candidate.Should().BeNull();
    }

    /// <summary>**回调抛异常时返回失败而非崩溃** / A throwing callback returns a failure instead of crashing.</summary>
    [Fact]
    public void Evaluate_ShouldConvertCallbackExceptionToFailure() {
        var budget = new SearchBudget(10);
        var slice = new SearchSlice(budget, evaluations: 5);
        var model = new StubModel(_ => throw new InvalidOperationException("boom"));
        SearchProblem<Flt64, Flt64> problem = new(
            "p", "fp", model, new Flt64Codec(), new Flt64Codec(),
            (lhs, rhs) => lhs.ToDouble() > rhs.ToDouble(),
            validSolution: _ => true,
            admissible: _ => true);

        Result<SearchEvaluationResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result =
            problem.Evaluate(Solution(2.0), slice);

        result.IsOk.Should().BeFalse("异常必须变成失败结果 / the exception must become a failure");
        ((Failed<SearchEvaluationResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("boom");
    }

    /// <summary>取出成功值 / Unwrap the successful value.</summary>
    private static SearchEvaluationResult<Flt64, Flt64> Unwrap(
        Result<SearchEvaluationResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>> result) =>
        ((Ok<SearchEvaluationResult<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;

    /// <summary>构造解 / Build a solution.</summary>
    private static Solution<Flt64> Solution(double value) => new(new[] { new Flt64(value) });

    /// <summary>构造一个单目标问题 / Build a single-objective problem.</summary>
    private static SearchProblem<Flt64, Flt64> Problem(
        Func<Flt64, bool>? valid = null,
        Func<IReadOnlyList<Flt64>, bool>? admissible = null) =>
        new("p", "fp", new StubModel(value => value[0]), new Flt64Codec(), new Flt64Codec(),
            (lhs, rhs) => lhs.ToDouble() > rhs.ToDouble(),
            valid: valid,
            validSolution: solution => solution.Count == 1,
            admissible: admissible ?? (_ => true));
}

/// <summary>
/// 最小回调模型桩 / A minimal call-back model stub.
/// </summary>
/// <remarks>
/// 这里的桩不是"为了一条断言编出来的假实现"——`SearchEvaluation` 与 `ObjectiveModelView` 的唯一输入就是一个回调模型，这个类是
/// 验证它们必须跨过的真实接缝。
/// The stub here is not a fake invented to support one assertion: a call-back model is the only input `SearchEvaluation` and
/// `ObjectiveModelView` take, so this is the real seam those tests must cross.
/// </remarks>
internal class StubModelCore<TObj> : IAbstractCallBackModelInterface<TObj, Flt64, Flt64> {
    private readonly Func<IReadOnlyList<Flt64>, Flt64> _objective;

    public StubModelCore(Func<IReadOnlyList<Flt64>, Flt64> objective) => _objective = objective;

    public bool Feasible { get; init; }

    /// <summary>可选的自定义目标比较 / An optional custom objective comparison.</summary>
    public Func<Flt64, Flt64, Order>? Comparer { get; init; }

    public Flt64 DefaultObjective => new(0.0);

    public ObjectCategory ObjectCategory => ObjectCategory.Minimum;

    public IAbstractMutableTokenTable<Flt64> Tokens { get; } =
        new ConcurrentAutoTokenTable<Flt64>(
            new Fuookami.Ospf.Math.Symbol.LinearCategory(),
            Array.Empty<Fuookami.Ospf.Core.Symbol.IIntermediateSymbol>());

    public IReadOnlyList<(Func<IReadOnlyList<Flt64>, bool?> Extractor, string Name)> Constraints =>
        new (Func<IReadOnlyList<Flt64>, bool?>, string)[] { (_ => true, "c1") };

    public IReadOnlyList<(Func<IReadOnlyList<Flt64>, TObj?> Extractor, string Name)> ObjectiveFunctions =>
        new (Func<IReadOnlyList<Flt64>, TObj?>, string)[] { (_ => default, "f1") };

    public Flt64 Operation(Flt64 lhs, Flt64 rhs) => lhs;

    public Flt64 ObjectiveValue() => DefaultObjective;

    public Flt64 ObjectiveValue(TObj obj) => DefaultObjective;

    public Flt64 Objective(IReadOnlyList<Flt64> solution) => _objective(solution);

    public Order CompareObjective(Flt64 lhs, Flt64 rhs) =>
        Comparer is null ? OrderHelpers.OrderOf(lhs.CompareTo(rhs)) : Comparer(lhs, rhs);

    public bool? ConstraintSatisfied(IReadOnlyList<Flt64> solution) => Feasible;

    public void Flush() { }

    public void Dispose() { }
}

/// <summary>
/// 最小 `Flt64` 编解码器 / A minimal `Flt64` codec.
/// </summary>
internal sealed class Flt64Codec : ISearchCodec<Flt64> {
    public Result<System.Text.Json.JsonElement, ErrorCode, Error<ErrorCode>> Encode(Flt64 value) =>
        Results.Ok(System.Text.Json.JsonSerializer.SerializeToElement(value.ToDouble()));

    public Result<Flt64, ErrorCode, Error<ErrorCode>> Decode(System.Text.Json.JsonElement value) =>
        Results.Ok(new Flt64(value.GetDouble()));
}

/// <summary>
/// 目标类型为 `object?` 的回调模型桩 / The call-back model stub with an `object?` objective type.
/// </summary>
internal sealed class StubModel : StubModelCore<object?> {
    public StubModel(Func<IReadOnlyList<Flt64>, Flt64> objective) : base(objective) { }
}
