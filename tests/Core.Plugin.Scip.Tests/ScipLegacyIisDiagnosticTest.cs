#nullable enable
#if SCIP_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

/// <summary>
/// SCIP legacy IIS 验收：后端没有原生 IIS 时，统一入口用弹性与删除过滤把不可行结论归约成子系统模型。
/// Acceptance tests for the SCIP legacy IIS path: without a native IIS the unified entry reduces the infeasible
/// conclusion into a subsystem model through elastic and deletion filtering.
/// </summary>
public class ScipLegacyIisDiagnosticTest {
    private static readonly Flt64 Neg = NumericConstantsRegistry.For<Flt64>().NegativeInfinity!.Value;
    private static readonly Flt64 Pos = NumericConstantsRegistry.For<Flt64>().PositiveInfinity!.Value;

    /// <summary>能力声明如实标注：有 Farkas、无原生 IIS，因此子系统必然来自 legacy 过滤。 / The capability declaration is honest: Farkas without native IIS, so the subsystem must come from legacy filtering.</summary>
    [Fact]
    public void DescriptorAdmitsThatNativeIisIsUnavailable() {
        SolverDescriptor descriptor = Assert.IsType<SolverDescriptor>(new ScipLinearSolver().Descriptor);

        Assert.Equal("scip", descriptor.SolverId);
        Assert.False(descriptor.Capabilities.NativeIis);
        Assert.True(descriptor.Capabilities.Farkas);
        Assert.Contains(SolverModelType.Mip, descriptor.Capabilities.ModelTypes);
    }

    /// <summary>
    /// 两条矛盾行加一条无害行的模型经 legacy 过滤后只保留被违反的行，证据按启发式定级且明确声明未经独立复验。
    /// A model with two contradictory rows plus one idle row keeps only the violated rows after legacy filtering, and the
    /// evidence is graded as heuristic while stating explicitly that no independent IIS verification happened.
    /// </summary>
    [Fact]
    public async Task InfeasibleEntryReturnsReducedSubsystemInsteadOfSnapshot() {
        using LinearTriadModel model = Triad();
        var solver = new ScipLinearSolver();

        Result<SolverOutput, ErrorCode, Error<ErrorCode>> result = await solver.InvokeAsync(
            model: model,
            iisConfig: new IisConfig { Time = TimeSpan.FromSeconds(10) });

        Assert.True(result.IsOk, result.ToString());
        LinearInfeasibleSolverOutput output = Assert.IsType<LinearInfeasibleSolverOutput>(result.Value);
        Assert.Equal(SolverStatus.Infeasible, output.Status);
        Assert.True(output.IisAvailable);
        LinearIisModel iis = Assert.IsType<LinearIisModel>(output.Iis);
        using (iis) {
            Assert.Equal("iis-fixture_iis", iis.Name);
            Assert.Same(model, iis.Origin);
            Assert.True(iis.RelaxedFeasible);
            Assert.Null(iis.GuardConstraints);
            // 无害行不会被违反，因此不可能进入子系统；矛盾行至少有一条被保留。
            // The idle row is never violated so it cannot enter the subsystem, while at least one contradictory row stays.
            Assert.NotEmpty(iis.OriginRows);
            Assert.DoesNotContain(2, iis.OriginRows);
            Assert.All(iis.OriginRows, row => Assert.InRange(row, 0, 1));
            Assert.All(iis.Rows.Names, name => Assert.NotEqual("idle-row", name));
            // 命中的松弛都指回行，原变量都是自由列，因此子系统列不保留任何一侧边界。
            // Every hit slack traces back to a row and both origin columns are free, so no bound side survives.
            Assert.Equal(new[] { "x", "y" }, iis.Variables.Select(variable => variable.Name));
            Assert.True(iis.Variables.All(variable => variable.LowerBound == Neg));
            Assert.True(iis.Variables.All(variable => variable.UpperBound == Pos));
        }

        InfeasibilityEvidence? evidence = output.Diagnostics?.InfeasibilityEvidence;
        Assert.NotNull(evidence);
        Assert.Equal(InfeasibilityEvidenceSource.ElasticFilter, evidence!.Source);
        Assert.Equal(EvidenceExactness.Heuristic, evidence.Exactness);
        Assert.Equal(EvidenceValidity.Heuristic, evidence.Validity);
        Assert.Equal(EvidenceMinimality.NotChecked, evidence.Minimality);
        Assert.Equal(EvidenceCompleteness.Complete, evidence.Completeness);
        Assert.Equal("legacy-iis-heuristic", evidence.UnavailableReason?.Code);
        Assert.NotNull(evidence.Elapsed);

        var report = output.ToSolveReport();
        Assert.Equal(ProblemStatus.Infeasible, report.ProblemStatus);
        Assert.Equal(
            InfeasibilityEvidenceSource.ElasticFilter,
            report.Diagnostics.InfeasibilityEvidence?.Source);
    }

    /// <summary>两条矛盾行加一条永不违反的行，用于检查归约只保留被证据命中的行。 / Two contradictory rows plus a never-violated row, which checks that only evidenced rows survive the reduction.</summary>
    private static LinearTriadModel Triad() => new(
        variables: [
            Column(0, "x", double.NegativeInfinity, double.PositiveInfinity),
            Column(1, "y", double.NegativeInfinity, double.PositiveInfinity),
        ],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                Row(),
                Row(),
                Row(),
            ]),
            signs: [
                ConstraintRelation.GreaterEqual,
                ConstraintRelation.LessEqual,
                ConstraintRelation.LessEqual,
            ],
            rhs: [new Flt64(2), new Flt64(0), new Flt64(10)],
            names: ["lower-row", "upper-row", "idle-row"],
            sources: [ConstraintSource.Origin, ConstraintSource.Origin, ConstraintSource.Origin]),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: []),
        name: "iis-fixture");

    private static SparseVector Row() => new([
        new SparseVectorEntry(0, Flt64.One),
        new SparseVectorEntry(1, Flt64.One),
    ]);

    private static ModelViewVariable Column(int index, string name, double lower, double upper) => new(
        index: index,
        lowerBound: new Flt64(lower),
        upperBound: new Flt64(upper),
        type: Continuous.Instance,
        name: name);
}
#endif
