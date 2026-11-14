#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 固定整数 LP 敏感性的编排 / The fixed-integer LP sensitivity orchestration.
/// </summary>
/// <remarks>
/// **这套判据证明"不需要真实求解器也能跑通编排"**：只要一个假后端，
/// 校验、固定值投影、能力门、求解、敏感性组装整条链路都能走完。
///
/// **These criteria show the orchestration runs without a real solver**: with a stub backend the whole chain completes.
/// </remarks>
public class FixedIntegerLpSensitivityAnalyzerTests {
    /// <summary>一个只数调用次数的假后端 / A stub backend that only counts calls.</summary>
    private sealed class StubBackend : IFixedIntegerLpBackend {
        public int Calls { get; private set; }

        public Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>> Next { get; set; } =
            Results.Ok(new FixedIntegerLpSolveResult(AnalysisStatus.Reachable));

        public Task<Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
            FixedIntegerLpRequest request,
            CancellationToken cancellationToken = default) {
            ++Calls;
            return Task.FromResult(Next);
        }
    }

    /// <summary>**没有后端时不求解，报 `Unsupported`** / No backend means no solve and Unsupported.</summary>
    /// <remarks>
    /// **反向对照**：这条挡的是"没有后端也硬着头皮求解"——那会抛空引用，
    /// **而调用方看到的将是一个异常而不是一句"这条路走不通"。**
    ///
    /// **The reverse control**: this guards against "solve anyway without a backend", which throws a null reference — **and the caller sees an exception instead of "this route is closed".**
    /// </remarks>
    [Fact]
    public async Task TheMissingBackend_ShouldReportUnsupportedWithoutSolving() {
        var analyzer = new FixedIntegerLpSensitivityAnalyzer(backend: null);

        Result<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>> result = await analyzer.AnalyzeAsync(BuildSnapshot(), BuildSolution(value: 1));

        result.Should().BeOfType<Ok<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>>>();
        FixedIntegerLpSensitivityReport report = ((Ok<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Status.Should().Be(AnalysisStatus.Unsupported);
        report.Message.Should().Contain("对偶能力", "the caller needs to know which capability is missing");
    }

    /// <summary>**后端被调用恰好一次** / The backend is called exactly once.</summary>
    /// <remarks>
    /// **"恰好一次"而不是"至少一次"**：调用两次意味着同一个模型解了两遍，
    /// **而第二遍的代价与第一遍相同。**
    ///
    /// **"Exactly once", not "at least once"**: two calls mean the same model was solved twice, **at twice the cost.**
    /// </remarks>
    [Fact]
    public async Task ThePresentBackend_ShouldBeCalledOnce() {
        var backend = new StubBackend();
        var analyzer = new FixedIntegerLpSensitivityAnalyzer(backend);

        Result<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>> result = await analyzer.AnalyzeAsync(BuildSnapshot(), BuildSolution(value: 1));

        backend.Calls.Should().Be(1);
        result.Should().BeOfType<Ok<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**后端失败不被吞成零敏感度** / A backend failure is not swallowed into zero sensitivity.</summary>
    /// <remarks>
    /// 吞掉会让调用方**把"后端没算出来"读成"这些约束都不重要"**。
    ///
    /// Swallowing makes the caller **read "the backend did not compute it" as "none of these constraints matter".**
    /// </remarks>
    [Fact]
    public async Task TheBackendFailure_ShouldPropagate() {
        var backend = new StubBackend {
            Next = Results.Failed<FixedIntegerLpSolveResult>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "no duals")),
        };
        var analyzer = new FixedIntegerLpSensitivityAnalyzer(backend);

        Result<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>> result = await analyzer.AnalyzeAsync(BuildSnapshot(), BuildSolution(value: 1));

        result.Should().BeOfType<Failed<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>>>();
        backend.Calls.Should().Be(1, "the backend was asked and answered; the answer was a failure");
    }

    /// <summary>**缺解时是 `Unknown` 且不求解** / A missing solution is Unknown and solves nothing.</summary>
    /// <remarks>
    /// **有后端也不该求解**：没有基线解就没有固定值，**而后端要的正是固定值。**
    ///
    /// **Not even with a backend**: no baseline solution means no fixed values, **and fixed values are what the backend asks for.**
    /// </remarks>
    [Fact]
    public async Task TheMissingSolution_ShouldBeUnknownAndSolveNothing() {
        var backend = new StubBackend();
        var analyzer = new FixedIntegerLpSensitivityAnalyzer(backend);

        Result<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>> result = await analyzer.AnalyzeAsync(BuildSnapshot(), solution: null);

        FixedIntegerLpSensitivityReport report = ((Ok<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Status.Should().Be(AnalysisStatus.Unknown, "a missing solution is not a statement about the backend");
        report.Message.Should().Contain("baseline CP 解", "the caller has to know what is missing");
        backend.Calls.Should().Be(0, "there was nothing to ask the backend about");
    }

    /// <summary>**越界的解被拒绝，且消息说清是越界** / An out-of-domain solution is refused with the right reason.</summary>
    /// <remarks>
    /// 三种失败指向三个修法。**把越界报成缺变量会让人去补一个并不缺的赋值。**
    ///
    /// Three failures point at three fixes. **Reporting an out-of-range value as a missing one sends the reader to fill in an assignment that is already there.**
    /// </remarks>
    [Fact]
    public async Task TheOutOfDomainSolution_ShouldBeRefusedWithItsOwnReason() {
        var backend = new StubBackend();
        var analyzer = new FixedIntegerLpSensitivityAnalyzer(backend);

        Result<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>> result = await analyzer.AnalyzeAsync(BuildSnapshot(), BuildSolution(value: 99));

        FixedIntegerLpSensitivityReport report = ((Ok<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Status.Should().Be(AnalysisStatus.Unknown);
        report.Message.Should().Contain("值域", "an out-of-range value is its own failure");
        report.Message.Should().NotContain("缺少整数变量赋值", "the variable is there; its value is wrong");
        backend.Calls.Should().Be(0);
    }

    /// <summary>**有后端但能力未声明时仍报 `Unsupported`** / A backend without the declared capability is still Unsupported.</summary>
    /// <remarks>
    /// **第一版判据没有这一条，所以删掉能力门也没红。**
    /// 有后端 ≠ 这个后端声明了对偶能力——**中间隔着一次能力声明。**
    ///
    /// **The first version lacked this case, so removing the capability gate stayed green.**
    /// Having a backend is not the same as that backend declaring the dual capability — **there is a declaration in between.**
    /// </remarks>
    [Fact]
    public async Task TheUndeclaredCapability_ShouldReportUnsupportedEvenWithABackend() {
        var backend = new StubBackend();
        var analyzer = new FixedIntegerLpSensitivityAnalyzer(backend);

        var matrix = new CapabilityMatrix(
            ModelTypes: new HashSet<SolverModelType> { SolverModelType.Mip },
            AnalysisCapabilities: new Dictionary<AnalysisCapability, CapabilitySupport> {
                [AnalysisCapability.FixedIntegerLpSensitivity] = CapabilitySupport.Unsupported,
            });

        Result<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>> result = await analyzer.AnalyzeAsync(
            BuildSnapshot(), BuildSolution(value: 1), capabilityMatrix: matrix);

        FixedIntegerLpSensitivityReport report = ((Ok<FixedIntegerLpSensitivityReport, ErrorCode, Error<ErrorCode>>)result).Value;
        report.Status.Should().Be(AnalysisStatus.Unsupported,
            "a backend that never declared the capability cannot be used for it");
        backend.Calls.Should().Be(0, "an undeclared capability is refused before any solve");
    }

    private static ConstraintProgrammingModelSnapshot BuildSnapshot() {
        using var model = new ConstraintProgrammingModel(name: "sensitivity", objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        var id = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(Int64.Zero, new Int64(3)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, id);

        model.RegisterVariable(id: id, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull("the variable registered");
        model.RegisterExpression("x", expression).Value.Should().NotBeNull("the expression registered");
        ConstraintProgrammingConstraint.IntegerComparison constraint =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, Int64.Zero).Value;
        model.AddConstraint(constraint, "constraint:x").Value.Should().NotBeNull("the constraint registered");
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should()
            .NotBeNull("the objective registered");

        return model.Snapshot().Value;
    }

    private static ConstraintProgrammingSolution BuildSolution(long value) =>
        new(new Dictionary<VariableId, Int64> { [new VariableId("source:x")] = new Int64(value) });
}
