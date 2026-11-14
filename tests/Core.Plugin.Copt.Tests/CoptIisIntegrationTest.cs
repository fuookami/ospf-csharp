#nullable enable
#if COPT_INTEGRATION

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Copt.Tests;

/// <summary>COPT 原生 IIS 验收 / Native COPT IIS acceptance.</summary>
public class CoptIisIntegrationTest {
    [Fact]
    [Trait("Solver", "COPT")]
    public async Task ComputeIisReturnsNativeEvidenceAndInfeasibleProjection() {
        using LinearTriadModel model = InfeasibleModel();
        var solver = new CoptLinearSolver();

        Result<LinearIisResult, ErrorCode, Error<ErrorCode>> result =
            await solver.ComputeIISAsync(model, new IisConfig());

        Assert.True(result.IsOk, result.ToString());
        LinearIisResult iis = result.Value;
        Assert.Equal(InfeasibilityEvidenceSource.NativeIis, iis.Evidence.Source);
        Assert.Equal("copt.computeIIS", iis.Evidence.Reference);
        Assert.Contains(model.DiagnosticConstraintId(0), iis.Evidence.ConstraintIds);
        Assert.Contains(
            iis.Evidence.VariableBoundRefs,
            reference => reference.VariableId == model.Variables[0].DiagnosticVariableId()
                && reference.Side == BoundSide.Upper);
        Assert.Single(iis.Model.OriginRows);
        Assert.Equal(0, iis.Model.OriginRows[0]);

        using var replayModel = new LinearTriadModel(
            variables: iis.Model.Variables,
            constraints: iis.Model.Rows,
            objective: new Objective<LinearObjectiveCell>(
                ObjectCategory.Minimum,
                [],
                Flt64.Zero),
            name: "copt-iis-replay");
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> replay =
            await solver.InvokeReportAsync(replayModel);
        Assert.True(replay.IsOk, replay.ToString());
        Assert.Equal(ProblemStatus.Infeasible, replay.Value.ProblemStatus);
    }

    [Fact]
    [Trait("Solver", "COPT")]
    public async Task LegacyIisEntryAttachesNativeEvidence() {
        using LinearTriadModel model = InfeasibleModel();

        Result<SolverOutput, ErrorCode, Error<ErrorCode>> result =
            await new CoptLinearSolver().InvokeAsync(
                model: model,
                iisConfig: new IisConfig());

        Assert.True(result.IsOk, result.ToString());
        LinearInfeasibleSolverOutput output = Assert.IsType<LinearInfeasibleSolverOutput>(result.Value);
        Assert.True(output.IisAvailable);
        Assert.Equal(InfeasibilityEvidenceSource.NativeIis, output.Diagnostics!.InfeasibilityEvidence!.Source);
    }

    [Fact]
    [Trait("Solver", "COPT")]
    public async Task ComputeIisSupportsMipModels() {
        using LinearTriadModel model = InfeasibleMipModel();

        Result<LinearIisResult, ErrorCode, Error<ErrorCode>> result =
            await new CoptLinearSolver().ComputeIISAsync(model, new IisConfig());

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(InfeasibilityEvidenceSource.NativeIis, result.Value.Evidence.Source);
        Assert.True(result.Value.Evidence.Exactness is EvidenceExactness.Exact or EvidenceExactness.Irreducible);
        Assert.Contains(result.Value.Model.Variables, variable => variable.Type == Binary.Instance);
        Assert.NotEmpty(result.Value.Evidence.ConstraintIds);
    }

    private static LinearTriadModel InfeasibleModel() {
        var x = new ModelViewVariable(
            index: 0,
            lowerBound: Flt64.Zero,
            upperBound: Flt64.One,
            type: Continuous.Instance,
            name: "x");
        SparseVector row = new([new SparseVectorEntry(0, Flt64.One)]);
        return new LinearTriadModel(
            variables: [x],
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix([row, row]),
                signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
                rhs: [new Flt64(2), Flt64.One],
                names: ["r0", "r1"],
                sources: [ConstraintSource.Origin, ConstraintSource.Origin]),
            objective: new Objective<LinearObjectiveCell>(
                ObjectCategory.Minimum,
                [new LinearObjectiveCell(0, Flt64.One)],
                Flt64.Zero),
            name: "copt-iis-integration");
    }

    private static LinearTriadModel InfeasibleMipModel() {
        var x = new ModelViewVariable(
            index: 0,
            lowerBound: Flt64.Zero,
            upperBound: Flt64.One,
            type: Binary.Instance,
            name: "x");
        SparseVector row = new([new SparseVectorEntry(0, Flt64.One)]);
        return new LinearTriadModel(
            variables: [x],
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix([row, row]),
                signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
                rhs: [new Flt64(2), Flt64.One],
                names: ["r0", "r1"],
                sources: [ConstraintSource.Origin, ConstraintSource.Origin]),
            objective: new Objective<LinearObjectiveCell>(
                ObjectCategory.Minimum,
                [new LinearObjectiveCell(0, Flt64.One)],
                Flt64.Zero),
            name: "copt-iis-mip-integration");
    }
}
#endif
