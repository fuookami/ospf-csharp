#nullable enable
#if MOSEK_INTEGRATION

using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Plugin.Mosek;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Mosek.Tests;

/// <summary>
/// 显式启用的 MOSEK 真机集成测试 / Opt-in MOSEK native integration tests.
/// </summary>
public class MosekIntegrationTest {
    [Fact]
    [Trait("Solver", "MOSEK")]
    public async Task SolvesLpWithStandardConstraintDirection() {
        using LinearTriadModel model = MosekTestModels.SimpleLp();

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MosekLinearSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Feasible, result.Value.ProblemStatus);
        Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
        Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
        Assert.Equal(1, result.Value.Solution!.Objective!.Value.ToDouble(), 6);
    }

    [Fact]
    [Trait("Solver", "MOSEK")]
    public async Task SolvesMipWithObjectiveOffsetAndWarmStart() {
        using LinearTriadModel model = MosekTestModels.IntegerModel();

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MosekLinearSolver().InvokeReportAsync(
                model: model,
                solvingStatusCallBack: _ => Results.OkInstance);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Feasible, result.Value.ProblemStatus);
        Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
        Assert.Equal(14, result.Value.Solution!.Objective!.Value.ToDouble(), 6);
        Assert.Equal(3, result.Value.Solution.Values[0].ToDouble(), 6);
        Assert.Equal(1, result.Value.Solution.Values[1].ToDouble(), 6);
        Assert.Equal("mosek", result.Value.Provenance!.Descriptor.SolverId);
        Assert.StartsWith("10.2", result.Value.Provenance.NativeVersion);
    }

    [Fact]
    [Trait("Solver", "MOSEK")]
    public async Task InfeasibleModelReturnsAReportRatherThanASolution() {
        using LinearTriadModel model = InfeasibleModel();

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new MosekLinearSolver().InvokeReportAsync(model);

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Infeasible, result.Value.ProblemStatus);
        Assert.Equal(SolutionPresence.None, result.Value.SolutionPresence);
    }

    private static LinearTriadModel InfeasibleModel() => new(
        variables: [
            new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: new Flt64(10),
                type: Continuous.Instance,
                name: "x"),
        ],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([new SparseVectorEntry(0, Flt64.One)]),
                new SparseVector([new SparseVectorEntry(0, Flt64.One)]),
            ]),
            signs: [
                ConstraintRelation.GreaterEqual,
                ConstraintRelation.LessEqual,
            ],
            rhs: [new Flt64(2), new Flt64(1)],
            names: ["lower", "upper"],
            sources: [ConstraintSource.Origin, ConstraintSource.Origin]),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [],
            constant: Flt64.Zero),
        name: "mosek-infeasible");
}
#endif
