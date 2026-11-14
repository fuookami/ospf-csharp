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
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 扰动分析的编排 / The orchestration of the perturbation analysis.
/// </summary>
/// <remarks>
/// **前置门在任何求解之前。** 一个做不了的分析不该先花预算再说做不了。
///
/// **The pre-flight gate comes before any solve.** An impossible analysis must not spend budget and refuse afterwards.
/// </remarks>
public class ConstraintPerturbationPipelineTests {
    private static (ConstraintProgrammingModelSnapshot Snapshot, ConstraintId Id) Fixture() {
        var model = new ConstraintProgrammingModel("perturb", ObjectCategory.Maximum, identityNamespace: "perturb");
        var x = new IntVar("x");
        var xId = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(new Int64(0), new Int64(10)).Value;
        model.RegisterVariable(id: xId, variable: x, domain: domain, scope: ModelElementScope.Stable, origin: "fixture/x")
            .Value.Should().NotBeNull();
        var expression = new ConstraintProgrammingExpression.Variable(x, domain, xId);
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        var id = new ConstraintId("c:le");
        model.AddConstraint(ConstraintProgrammingConstraint.CreateLessOrEqual(expression, new Int64(4)).Value,
            id: id, name: "le").Value.Should().NotBeNull();
        model.Maximize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();
        return (model.Snapshot().Value!, id);
    }

    private static CapabilityMatrix NoRhs() {
        var supports = new Dictionary<AnalysisCapability, CapabilitySupport> {
            [AnalysisCapability.RhsPerturbation] = CapabilitySupport.Unsupported,
        };
        return new CapabilityMatrix(ModelTypes: new HashSet<SolverModelType>(), AnalysisCapabilities: supports);
    }

    /// <summary>**两项都不允许** / Neither stage is allowed.</summary>
    /// <remarks>
    /// **第一次我只禁了删除测试，RHS 仍可用**，于是前置门判"继续"，判据红。
    /// **这是本会话第五次"夹具与判据不符"。**
    ///
    /// **The first version disabled only the removal test while RHS stayed available**, so the gate said "proceed" and the criterion went red. **This is the fifth time the fixture did not
    /// match what the criterion claimed.**
    /// </remarks>
    private static CapabilityMatrix BothUnavailable() {
        var supports = new Dictionary<AnalysisCapability, CapabilitySupport> {
            [AnalysisCapability.RhsPerturbation] = CapabilitySupport.Unsupported,
            [AnalysisCapability.RemovalTest] = CapabilitySupport.Unsupported,
        };
        return new CapabilityMatrix(ModelTypes: new HashSet<SolverModelType>(), AnalysisCapabilities: supports);
    }

    private static CapabilityMatrix NoRemoval() {
        var supports = new Dictionary<AnalysisCapability, CapabilitySupport> {
            [AnalysisCapability.RemovalTest] = CapabilitySupport.Unsupported,
        };
        return new CapabilityMatrix(ModelTypes: new HashSet<SolverModelType>(), AnalysisCapabilities: supports);
    }

    /// <summary>**前置门在任何求解之前** / The pre-flight gate runs before any solve.</summary>
    /// <remarks>
    /// **反向对照**：先求解再拒的话，`SolveCalls` 会大于 0。只查报告状态，
    /// 一个"先求解再拒"的实现照样给出 Unsupported，**而它已经白花了一次预算。**
    ///
    /// **The reverse control**: solving first would make `SolveCalls` greater than zero. Checking only the report status lets a "solve then refuse" implementation pass, **while it has
    /// already burnt a solve for nothing.**
    /// </remarks>
    [Fact]
    public async Task TheRefusedBackend_ShouldNotSpendASingleSolve() {
        (ConstraintProgrammingModelSnapshot snapshot, ConstraintId id) = Fixture();
        var solver = new CountingSolver();

        Result<ConstraintPerturbationReport, ErrorCode, Error<ErrorCode>> result =
            await ConstraintPerturbationPipeline.AnalyzeAsync(
                snapshot, id, null, new Flt64(4.0),
                // RHS 与删除都不允许 → 整份 Unsupported。
                BothUnavailable(), new ConstraintPerturbationPolicy(Deltas: [new Flt64(1.0)], RemovalTest: true),
                solver);

        result.Value!.Status.Should().Be(AnalysisStatus.Unsupported,
            "both requested stages are unavailable, so there is nothing to report");
        solver.SolveCalls.Should().Be(0, "the gate came before the budget was touched");

        // **这一条才是把两条路径分开的**：跳过 gate 时，RHS 不被允许也会为每条 delta
        // 记一条 `Unsupported` 观察——**状态和求解次数看着都对，只有观察列表不同。**
        //
        // **This is what separates the two paths.** With the gate skipped, a disallowed RHS still records one `Unsupported` observation per delta — **status and solve count look
        // right, and only the observation list differs.**
        result.Value.ObservationList.Should().BeEmpty("the gate refused before a single observation was made");
        result.Value.Message.Should().Contain("未声明 RHS 扰动能力", "the gate names the missing capability");
    }

    /// <summary>**候选不存在是调用方的错，不是"做不了"** / An absent candidate is a caller mistake.</summary>
    [Fact]
    public async Task TheAbsentConstraint_ShouldBeAnError() {
        (ConstraintProgrammingModelSnapshot snapshot, _) = Fixture();

        Result<ConstraintPerturbationReport, ErrorCode, Error<ErrorCode>> result =
            await ConstraintPerturbationPipeline.AnalyzeAsync(
                snapshot, new ConstraintId("c:absent"), null, new Flt64(4.0),
                new CapabilityMatrix(ModelTypes: new HashSet<SolverModelType>()),
                new ConstraintPerturbationPolicy(Deltas: [new Flt64(1.0)]),
                new CountingSolver());

        result.Should().BeOfType<Failed<ConstraintPerturbationReport, ErrorCode, Error<ErrorCode>>>(
            "a wrong id is not an inability of the backend");
    }

    /// <summary>**非有限的基线值被拒** / A non-finite baseline is refused.</summary>
    [Fact]
    public async Task TheNonFiniteBaseline_ShouldBeAnError() {
        (ConstraintProgrammingModelSnapshot snapshot, ConstraintId id) = Fixture();

        Result<ConstraintPerturbationReport, ErrorCode, Error<ErrorCode>> result =
            await ConstraintPerturbationPipeline.AnalyzeAsync(
                snapshot, id, null, new Flt64(double.PositiveInfinity),
                new CapabilityMatrix(ModelTypes: new HashSet<SolverModelType>()),
                new ConstraintPerturbationPolicy(Deltas: [new Flt64(1.0)]),
                new CountingSolver());

        result.Should().BeOfType<Failed<ConstraintPerturbationReport, ErrorCode, Error<ErrorCode>>>(
            "an infinite baseline makes every improvement comparison meaningless");
    }

    /// <summary>**求解次数受策略上限约束** / The solve count honours the policy cap.</summary>
    [Fact]
    public async Task TheSolveCap_ShouldBeHonoured() {
        (ConstraintProgrammingModelSnapshot snapshot, ConstraintId id) = Fixture();
        var solver = new CountingSolver();

        await ConstraintPerturbationPipeline.AnalyzeAsync(
            snapshot, id, null, new Flt64(4.0),
            new CapabilityMatrix(ModelTypes: new HashSet<SolverModelType>()),
            new ConstraintPerturbationPolicy(
                Deltas: [new Flt64(1.0), new Flt64(2.0), new Flt64(4.0), new Flt64(8.0)],
                MaxSolves: 2),
            solver);

        solver.SolveCalls.Should().BeLessThanOrEqualTo(2, "the policy capped the budget at two solves");
    }

    /// <summary>总返回未知解的求解器 / A solver that always reports an unknown result.</summary>
    private sealed class CountingSolver : IConstraintProgrammingSolver {
        public int SolveCalls { get; private set; }

        public SolverDescriptor Descriptor => new FakeConstraintProgrammingSolver().Descriptor;

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) {
            SolveCalls++;
            ConstraintProgrammingSolverOutput output = new ConstraintProgrammingUnknownOutput(
                TerminationReason.TimeLimit);
            return Task.FromResult(Results.Ok(output));
        }

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            Results.Failed<IConstraintProgrammingSession>(new Err<ErrorCode>(ErrorCode.Other, "not used"));
    }
}
