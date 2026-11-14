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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 单约束删除测试 / The single-constraint removal test.
/// </summary>
/// <remarks>
/// **预算耗尽与删除无效果看起来一样，结论完全不同。**
///
/// **A spent budget and an ineffective removal look the same and mean completely different things.**
/// </remarks>
public class RemovalTestTests {
    private static ConstraintProgrammingModelSnapshot Fixture(out ConstraintId constraintId) {
        var model = new ConstraintProgrammingModel("removal", ObjectCategory.Maximum, identityNamespace: "removal");
        var y = new IntVar("y");
        var yId = new VariableId("source:y");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(100)).Value;
        model.RegisterVariable(id: yId, variable: y, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/y")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(y, domain, yId);
        model.RegisterExpression("y", expression).Value.Should().NotBeNull();
        constraintId = new ConstraintId("constraint:cap");
        model.AddConstraint(
                ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(8)).Value,
                id: constraintId, name: "cap")
            .Value.Should().NotBeNull();
        model.Maximize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        return model.Snapshot().Value!;
    }

    private static ConstraintPerturbationPolicy Policy() => new(RemovalTest: true);

    private static ConstraintProgrammingSolution Witness(long value) =>
        new(new Dictionary<VariableId, Int64> { [new VariableId("source:y")] = new(value) });

    /// <summary>**交给求解器的模型必须真的少了那条约束** / The model handed to the solver must really lack the constraint.</summary>
    /// <remarks>
    /// **这是这条判据的全部意义**：如果派生是个静默的空操作，解出来的是一个没被扰动的模型，
    /// 改善量为 0，**报告会写"删除无效"——而这条约束可能极其关键。**
    ///
    /// **This is the whole point of the criterion**: if the derivation were a silent no-op, the solve would run on an unperturbed model, the improvement would come out zero, and
    /// **the report would say "removal ineffective" — about a constraint that may be critical.**
    /// </remarks>
    [Fact]
    public async Task TheSolvedModel_ShouldNotContainTheRemovedConstraint() {
        ConstraintProgrammingModelSnapshot snapshot = Fixture(out ConstraintId id);
        var solver = new CapturingSolver(10.0);

        ConstraintPerturbationObservation observation = await ConstraintPerturbationSupport.RunRemovalAsync(
            snapshot, id, new Flt64(8.0), Witness(8), Policy(), solver, budgetExhausted: false);

        solver.SubmittedConstraints.Should().NotContain(id.Value,
            "the whole test rests on the model actually losing that constraint");
        solver.SubmittedConstraints.Should().BeEmpty("it was the only constraint");
        observation.Kind.Should().Be(ConstraintPerturbationKind.Removal);
    }

    /// <summary>**删除后目标变好就是有效** / A better objective after removal is effective.</summary>
    /// <remarks>
    /// 8 → 10 在最大化下是 +2。**方向由 `ObjectCategory` 决定**，这批通过删除测试把它接上了。
    ///
    /// 8 to 10 is +2 under maximisation. **The direction comes from `ObjectCategory`**, and this batch wires it through the removal test.
    /// </remarks>
    [Fact]
    public async Task TheImprovingRemoval_ShouldBeEffective() {
        ConstraintProgrammingModelSnapshot snapshot = Fixture(out ConstraintId id);

        ConstraintPerturbationObservation observation = await ConstraintPerturbationSupport.RunRemovalAsync(
            snapshot, id, new Flt64(8.0), Witness(8), Policy(), new CapturingSolver(10.0), budgetExhausted: false);

        observation.Status.Should().Be(AnalysisStatus.Reachable);
        observation.ObjectiveImprovement!.Value.ToDouble().Should().Be(2.0);
        observation.Outcome.Should().Be(PerturbationOutcome.Effective);
        observation.RemovalEffective.Should().BeTrue();
    }

    /// <summary>**预算耗尽必须报 `Unknown`，不能报"删除无效"** / A spent budget is `Unknown`, not "ineffective".</summary>
    /// <remarks>
    /// 一次没跑成的测试和一次跑成了、发现没效果的测试，**结论完全不同，而它们看起来一样。**
    ///
    /// A test that never ran and a test that ran and found nothing **mean completely different things and look identical.**
    /// </remarks>
    [Fact]
    public async Task TheExhaustedBudget_ShouldBeUnknown() {
        ConstraintProgrammingModelSnapshot snapshot = Fixture(out ConstraintId id);
        var solver = new CapturingSolver(10.0);

        ConstraintPerturbationObservation observation = await ConstraintPerturbationSupport.RunRemovalAsync(
            snapshot, id, new Flt64(8.0), Witness(8), Policy(), solver, budgetExhausted: true);

        observation.Status.Should().Be(AnalysisStatus.Unknown);
        observation.Outcome.Should().Be(PerturbationOutcome.Unknown);
        observation.RemovalEffective.Should().BeNull("nothing was measured, so there is no verdict");
        observation.Message.Should().Contain("预算");
        solver.SubmittedConstraints.Should().BeNull("the solver must not even be called");
    }

    /// <summary>**约束不存在也是 `Unknown`** / A missing constraint is `Unknown` too.</summary>
    /// <remarks>这不是"删了没用"，是"根本没删"。</remarks>
    [Fact]
    public async Task TheMissingConstraint_ShouldBeUnknown() {
        ConstraintProgrammingModelSnapshot snapshot = Fixture(out _);
        var solver = new CapturingSolver(10.0);

        ConstraintPerturbationObservation observation = await ConstraintPerturbationSupport.RunRemovalAsync(
            snapshot, new ConstraintId("constraint:absent"), new Flt64(8.0), Witness(8), Policy(), solver,
            budgetExhausted: false);

        observation.Status.Should().Be(AnalysisStatus.Unknown);
        observation.Message.Should().Contain("不存在");
        solver.SubmittedConstraints.Should().BeNull("nothing was removed, so nothing should be solved");
    }

    /// <summary>**求解失败也是 `Unknown`** / A failed solve is `Unknown`.</summary>
    [Fact]
    public async Task TheFailedSolve_ShouldBeUnknown() {
        ConstraintProgrammingModelSnapshot snapshot = Fixture(out ConstraintId id);

        ConstraintPerturbationObservation observation = await ConstraintPerturbationSupport.RunRemovalAsync(
            snapshot, id, new Flt64(8.0), Witness(8), Policy(),
            new FailingSolver(new Err<ErrorCode>(ErrorCode.Other, "backend said no")), budgetExhausted: false);

        observation.Status.Should().Be(AnalysisStatus.Unknown);
        observation.Outcome.Should().Be(PerturbationOutcome.Unknown);
    }

    /// <summary>记录提交给求解器的约束 / Captures the constraints handed to the solver.</summary>
    private sealed class CapturingSolver(double objective) : IConstraintProgrammingSolver {
        public IReadOnlyList<string>? SubmittedConstraints { get; private set; }

        public SolverDescriptor Descriptor => new FakeConstraintProgrammingSolver().Descriptor;

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) {
            SubmittedConstraints = model.Snapshot().Value!.Constraints
                .Select(entry => entry.Id.Value)
                .ToArray();
            ConstraintProgrammingSolverOutput output = new ConstraintProgrammingFeasibleOutput(
                Witness(8), objective: new Flt64(objective), proofStatus: ProofStatus.Verified);
            return Task.FromResult(Results.Ok(output));
        }

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            Results.Failed<IConstraintProgrammingSession>(new Err<ErrorCode>(ErrorCode.Other, "not used"));
    }

    private sealed class FailingSolver(Err<ErrorCode> error) : IConstraintProgrammingSolver {
        public SolverDescriptor Descriptor => new FakeConstraintProgrammingSolver().Descriptor;

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<ConstraintProgrammingSolverOutput>(error));

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            Results.Failed<IConstraintProgrammingSession>(new Err<ErrorCode>(ErrorCode.Other, "not used"));
    }
}
