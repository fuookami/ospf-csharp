#nullable enable
#if GUROBI_AVAILABLE

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Gurobi;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>Gurobi Benders 统一报告身份验收 / Gurobi Benders unified-report identity acceptance.</summary>
public class GurobiBendersReportIntegrationTest {
    [Fact]
    [Trait("Category", "Gurobi")]
    public async Task MasterAndSubproblemPreserveGurobiReportIdentity() {
        if (!IsGurobiAvailable()) {
            return;
        }
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("gurobi-benders-report-x");
        LinearPolynomial<Flt64> objective = Linear(x);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddConstraint(relation: objective.Ge(Flt64.One), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: objective,
            name: "objective",
            displayName: null).IsOk);

        var solver = new GurobiBendersDecompositionSolver();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> master =
            await solver.SolveMasterReportAsync("gurobi-benders-master", parent);
        Assert.True(master.IsOk, master.ToString());
        Assert.Equal("gurobi", master.Value.Provenance!.Descriptor.SolverId);
        Assert.Equal(SolutionPresence.Optimal, master.Value.SolutionPresence);

        Result<IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> sub =
            await solver.SolveSubAsync(
                name: "gurobi-benders-sub",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One });
        Assert.True(sub.IsOk, sub.ToString());
        IBendersDecompositionSolver.LinearFeasibleResult feasible =
            Assert.IsType<IBendersDecompositionSolver.LinearFeasibleResult>(sub.Value);
        Assert.NotNull(feasible.Report);
        Assert.Equal("gurobi", feasible.Report!.Provenance!.Descriptor.SolverId);
    }

    private static LinearPolynomial<Flt64> Linear(IVariableItem variable) =>
        new(
            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
            Flt64.Zero);

    private static bool IsGurobiAvailable() {
        try {
            using var environment = new GRBEnv();
            using var model = new GRBModel(environment);
            return true;
        }
        catch {
            return false;
        }
    }
}
#endif
