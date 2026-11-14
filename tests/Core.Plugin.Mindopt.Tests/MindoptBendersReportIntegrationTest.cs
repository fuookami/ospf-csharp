#nullable enable
#if MINDOPT_INTEGRATION

using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Mindopt;
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
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Mindopt.Tests;

/// <summary>MindOPT Benders 统一报告身份验收 / MindOPT Benders unified-report identity acceptance.</summary>
public class MindoptBendersReportIntegrationTest {
    [Fact]
    [Trait("Solver", "MindOPT")]
    public async Task MasterAndSubproblemPreserveMindoptReportIdentity() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("mindopt-benders-report-x");
        LinearPolynomial<Flt64> objective = Linear(x);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddConstraint(relation: objective.Ge(Flt64.One), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: objective,
            name: "objective",
            displayName: null).IsOk);

        var solver = new MindoptBendersDecompositionSolver();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> master =
            await solver.SolveMasterReportAsync("mindopt-benders-master", parent);
        Assert.True(master.IsOk, master.ToString());
        Assert.Equal("mindopt", master.Value.Provenance!.Descriptor.SolverId);
        Assert.Equal(SolutionPresence.Optimal, master.Value.SolutionPresence);

        Result<IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> sub =
            await solver.SolveSubAsync(
                name: "mindopt-benders-sub",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One });
        Assert.True(sub.IsOk, sub.ToString());
        IBendersDecompositionSolver.LinearFeasibleResult feasible =
            Assert.IsType<IBendersDecompositionSolver.LinearFeasibleResult>(sub.Value);
        Assert.NotNull(feasible.Report);
        Assert.Equal("mindopt", feasible.Report!.Provenance!.Descriptor.SolverId);
    }

    private static LinearPolynomial<Flt64> Linear(IVariableItem variable) =>
        new(
            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
            Flt64.Zero);
}
#endif
