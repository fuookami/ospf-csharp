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
/// 原生 IIS 冲突种子 / The native-IIS conflict seed.
/// </summary>
/// <remarks>
/// **只传约束类 activation id。** 边界的 activation 后端可以省略，
/// 强行传进去会把一次合法请求变成"未知 id"的建模错误。
///
/// **Only constraint activation ids are passed.** A backend may omit a bound activation, and passing one turns a legitimate request into an unknown-id modelling error.
/// </remarks>
public class NativeConflictSeedTests {
    private static (ConstraintProgrammingModelSnapshot Snapshot, ObjectiveTarget Target) Fixture() {
        var model = new ConstraintProgrammingModel("native", ObjectCategory.Maximum, identityNamespace: "native");
        var x = new IntVar("x");
        var xId = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: xId, variable: x, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/x")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(x, domain, xId);
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        model.AddConstraint(ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(4)).Value,
            id: new ConstraintId("c:le"), name: "le").Value.Should().NotBeNull();
        var objective = new ObjectiveId("objective:main");
        model.Maximize(expression: expression, id: objective).Value.Should().NotBeNull();
        return (model.Snapshot().Value!, ObjectiveTarget.CreateAtLeast(objective, new Flt64(9.0)));
    }

    /// <summary>**只传约束类 activation id，边界一律不传** / Only constraint activation ids, never bounds.</summary>
    ///
    /// <remarks>
    /// **这是这批唯一真正容易写错的地方。** "顺手补全"会把边界也传进去，
    /// 而**后端可能根本没为那条边界建 activation**。
    ///
    /// **This is the one thing in that is really easy to get wrong.** A helpful completion would pass the bounds too, and **the backend may never have created an
    /// activation for that bound.**
    /// </remarks>
    [Fact]
    public void TheActivationIds_ShouldOnlyCarryConstraints() {
        var evidence = new List<DiagnosticSource> {
            new DiagnosticSource.Constraint(new ConstraintId("c:le")),
            new DiagnosticSource.VariableLowerBound(new VariableId("source:x")),
            new DiagnosticSource.VariableUpperBound(new VariableId("source:x")),
            new DiagnosticSource.SparseDomain(new VariableId("source:x")),
        };

        IReadOnlySet<string>? ids = NativeConflictSeed.ActivationIds(evidence);

        ids.Should().NotBeNull();
        ids.Should().BeEquivalentTo(["constraint:c:le"],
            "bounds and domains are not guaranteed to have activations, so passing them breaks a legitimate request");
    }

    /// <summary>**没有约束类证据时不指定** / No constraint evidence means unspecified.</summary>
    ///
    /// <remarks>
    /// 返回 `null` 而不是空集：**"不指定"和"指定零个"在部分后端上是两种不同的请求。**
    ///
    /// `null` rather than an empty set: **"unspecified" and "zero specified" are two different requests to some backends.**
    /// </remarks>
    [Fact]
    public void TheActivationIds_ShouldBeUnspecifiedWithoutConstraints() {
        NativeConflictSeed.ActivationIds([
            new DiagnosticSource.VariableLowerBound(new VariableId("source:x")),
        ]).Should().BeNull("there is no constraint to activate, so nothing should be specified");
    }

    /// <summary>**后端不做冲突分析就没有种子** / A backend that does no conflict analysis yields no seed.</summary>
    [Fact]
    public async Task TheInfeasibleWithoutProof_ShouldGiveNoSeed() {
        (ConstraintProgrammingModelSnapshot snapshot, ObjectiveTarget target) = Fixture();
        var conflict = new ConstraintProgrammingConflict(constraintIds: [new ConstraintId("c:le")]);

        IReadOnlyList<DiagnosticSource>? seed = await NativeConflictSeed.TrySeedAsync(
            snapshot, target, [new DiagnosticSource.Constraint(new ConstraintId("c:le"))],
            new CapturingSolver(new ConstraintProgrammingInfeasibleOutput(
                conflict: conflict, proofStatus: ProofStatus.Claimed)));

        seed.Should().BeNull("nothing was proven, so this path produced no seed");
    }

    /// <summary>**请求里带上了 conflict 收集开关** / The request turns conflict collection on.</summary>
    ///
    /// <remarks>
    /// **不加 `CollectConflict`，后端会把这次求解当成普通求解**，返回的冲突永远是 `null`——
    /// 而"没有冲突"和"没请求冲突"看起来一样。
    ///
    /// **Without `CollectConflict` the backend treats this as an ordinary solve** and never reports a conflict — and "no conflict" looks just like "conflict was not requested".
    /// </remarks>
    [Fact]
    public async Task TheRequest_ShouldAskForConflicts() {
        (ConstraintProgrammingModelSnapshot snapshot, ObjectiveTarget target) = Fixture();
        var solver = new CapturingSolver(new ConstraintProgrammingInfeasibleOutput(
            conflict: new ConstraintProgrammingConflict(constraintIds: [new ConstraintId("c:le")]),
            proofStatus: ProofStatus.Verified));

        await NativeConflictSeed.TrySeedAsync(
            snapshot, target,
            [new DiagnosticSource.Constraint(new ConstraintId("c:le")),
             new DiagnosticSource.VariableLowerBound(new VariableId("source:x"))],
            solver);

        solver.Options.Should().NotBeNull();
        solver.Options!.CollectConflict.Should().BeTrue("without it the backend never reports a conflict");
        solver.Options.ShrinkConflict.Should().BeFalse("the native seed is not shrunk here");
        solver.Options.ConflictActivationIds.Should().BeEquivalentTo(["constraint:c:le"]);
    }

    /// <summary>**已证明的冲突给出种子** / A proven conflict yields a seed.</summary>
    [Fact]
    public async Task TheProvenConflict_ShouldGiveASeed() {
        (ConstraintProgrammingModelSnapshot snapshot, ObjectiveTarget target) = Fixture();
        var asked = new DiagnosticSource.Constraint(new ConstraintId("c:le"));

        IReadOnlyList<DiagnosticSource>? seed = await NativeConflictSeed.TrySeedAsync(
            snapshot, target, [asked],
            new CapturingSolver(new ConstraintProgrammingInfeasibleOutput(
                conflict: new ConstraintProgrammingConflict(constraintIds: [new ConstraintId("c:le")]),
                proofStatus: ProofStatus.Verified)));

        seed.Should().NotBeNull();
        seed.Should().Contain(asked);
    }

    /// <summary>记录求解选项的求解器 / A solver that records the options it was given.</summary>
    private sealed class CapturingSolver(ConstraintProgrammingSolverOutput output) : IConstraintProgrammingSolver {
        public ConstraintProgrammingSolveOptions? Options { get; private set; }

        public SolverDescriptor Descriptor => new FakeConstraintProgrammingSolver().Descriptor;

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) {
            Options = options;
            return Task.FromResult(Results.Ok(output));
        }

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            Results.Failed<IConstraintProgrammingSession>(new Err<ErrorCode>(ErrorCode.Other, "not used"));
    }
}
