#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 目标可行性分类 / Classifying target feasibility.
/// </summary>
/// <remarks>
/// 三条分支各有一个反直觉的地方，**每条都要一条判据**：可行要复核、不可行要证明、无结论可能仍可用。
///
/// Each of the three branches has a counter-intuitive point and **each needs a criterion**: feasibility is re-checked, infeasibility needs a proof, and an unknown result may
/// still be usable.
/// </remarks>
public class TargetFeasibilityAnalyzerTests {
    private static (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveTarget AtLeast8) Fixture() {
        var model = new ConstraintProgrammingModel("feasibility", ObjectCategory.Maximum, identityNamespace: "feasibility");
        var y = new IntVar("y");
        var yId = new VariableId("source:y");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: yId, variable: y, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/y")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(y, domain, yId);
        model.RegisterExpression("y", expression).Value.Should().NotBeNull();
        model.AddConstraint(
                ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(8)).Value,
                id: new ConstraintId("constraint:cap"), name: "cap")
            .Value.Should().NotBeNull();
        var objective = new ObjectiveId("objective:main");
        model.Maximize(expression: expression, id: objective).Value.Should().NotBeNull();
        return (model.Snapshot().Value!, ObjectiveTarget.CreateAtLeast(objective, new Flt64(8.0)));
    }

    private static ConstraintProgrammingSolution Witness(long value) =>
        new(new Dictionary<VariableId, Int64> { [new VariableId("source:y")] = new(value) });

    private static Task<Result<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>>> Analyze(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target,
        ConstraintProgrammingSolverOutput output) =>
        TargetFeasibilityAnalyzer.AnalyzeAsync(snapshot, target, new FixedOutputSolver(output));

    /// <summary>**合格见证 → `Reachable`** / A valid witness yields `Reachable`.</summary>
    [Fact]
    public async Task TheValidWitness_ShouldBeReachable() {
        (ConstraintProgrammingModelSnapshot snapshot, ObjectiveTarget target) = Fixture();
        var output = new ConstraintProgrammingFeasibleOutput(Witness(8), proofStatus: ProofStatus.Verified);

        Result<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>> report = await Analyze(snapshot, target, output);
        report.IsOk.Should().BeTrue(report.ToString());
        report.Value!.Status.Should().Be(AnalysisStatus.Reachable);
        report.Value.Message.Should().BeNull();
    }

    /// <summary>**可行但见证不合格 → `Unknown`，不是可达** / Feasible with a bad witness is `Unknown`.</summary>
    /// <remarks>
    /// 后端说可行、解却不满足目标边界。**照抄"可行"就是把一个错的结论写进报告。**
    ///
    /// The backend says feasible while the solution misses the bound. **Copying "feasible" through would write a wrong conclusion into the report.**
    /// </remarks>
    [Fact]
    public async Task TheFeasibleWithABadWitness_ShouldNotBeReachable() {
        (ConstraintProgrammingModelSnapshot snapshot, ObjectiveTarget target) = Fixture();
        var output = new ConstraintProgrammingFeasibleOutput(Witness(5), proofStatus: ProofStatus.Verified);

        Result<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>> report = await Analyze(snapshot, target, output);
        report.Value!.Status.Should().Be(AnalysisStatus.Unknown, "5 does not reach the bound of 8");
        report.Value.Status.Should().NotBe(AnalysisStatus.Reachable);
        report.Value.Message.Should().Contain("见证无效", "the reason has to travel with the report");
        report.Value.Solution.Should().BeNull("an invalid witness must not be handed on");
    }

    /// <summary>**不可行但没证明 → `Unknown`，不是不可达** / Unproven infeasibility is `Unknown`.</summary>
    /// <remarks>
    /// **这一条是这批最重要的**：把"后端没证出来"报成"不可达"，会让一个其实可达的目标被当成死路。
    ///
    /// **The most important one in**: reporting "not proven" as "unreachable" would turn a reachable target into a dead end.
    /// </remarks>
    [Fact]
    public async Task TheUnprovenInfeasibility_ShouldNotBeUnreachable() {
        (ConstraintProgrammingModelSnapshot snapshot, ObjectiveTarget target) = Fixture();

        Result<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>> unproven = await Analyze(snapshot, target,
            new ConstraintProgrammingInfeasibleOutput(proofStatus: ProofStatus.Claimed));
        unproven.Value!.Status.Should().Be(AnalysisStatus.Unknown,
            "Claimed is not Verified, so nothing was proven");
        unproven.Value.Message.Should().Contain("未提供不可达证明");

        Result<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>> proven = await Analyze(snapshot, target,
            new ConstraintProgrammingInfeasibleOutput(proofStatus: ProofStatus.Verified));
        proven.Value!.Status.Should().Be(AnalysisStatus.Unreachable,
            "a Verified proof is what licenses the word unreachable");
        proven.Value.ProofStatus.Should().Be(ProofStatus.Verified);
    }

    /// <summary>**降阶不支持 → `Unsupported`，不是失败** / Unsupported lowering is `Unsupported`, not a failure.</summary>
    [Fact]
    public async Task TheUnsupportedLowering_ShouldBeUnsupported() {
        (ConstraintProgrammingModelSnapshot snapshot, ObjectiveTarget target) = Fixture();
        var solver = new FixedOutputSolver(
            new Err<ErrorCode>(ErrorCode.Other, "lowering not supported"));

        Result<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>> report = await TargetFeasibilityAnalyzer.AnalyzeAsync(snapshot, target, solver);
        report.IsOk.Should().BeTrue("an unsupported lowering is an answer, not an error");
        report.Value!.Status.Should().Be(AnalysisStatus.Unsupported);
        report.Value.TargetFixed.Should().BeFalse("nothing was fixed, so the target is still open");
    }

    /// <summary>**求解器抛异常 → `Unknown`，不向上抛** / A throwing solver yields `Unknown`.</summary>
    [Fact]
    public async Task TheThrowingSolver_ShouldBeUnknown() {
        (ConstraintProgrammingModelSnapshot snapshot, ObjectiveTarget target) = Fixture();
        var solver = new ThrowingSolver(new InvalidOperationException("backend exploded"));

        Result<TargetFeasibilityReport, ErrorCode, Error<ErrorCode>> report = await TargetFeasibilityAnalyzer.AnalyzeAsync(snapshot, target, solver);
        report.IsOk.Should().BeTrue("a backend crash is a result to report, not an exception to propagate");
        report.Value!.Status.Should().Be(AnalysisStatus.Unknown);
        report.Value.Message.Should().Contain("backend exploded");
    }

    /// <summary>固定返回一份输出的求解器 / A solver that always returns one fixed output.</summary>
    private sealed class FixedOutputSolver(
        object payload) : IConstraintProgrammingSolver {
        public SolverDescriptor Descriptor => new FakeConstraintProgrammingSolver().Descriptor;

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(payload is ConstraintProgrammingSolverOutput output
                ? Results.Ok(output)
                : Results.Failed<ConstraintProgrammingSolverOutput>((Err<ErrorCode>)payload));

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            Results.Failed<IConstraintProgrammingSession>(
                new Err<ErrorCode>(ErrorCode.Other, "not used"));
    }

    /// <summary>固定抛异常的求解器 / A solver that always throws.</summary>
    private sealed class ThrowingSolver(Exception error) : IConstraintProgrammingSolver {
        public SolverDescriptor Descriptor => new FakeConstraintProgrammingSolver().Descriptor;

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) => throw error;

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            Results.Failed<IConstraintProgrammingSession>(
                new Err<ErrorCode>(ErrorCode.Other, "not used"));
    }
}
