#nullable enable
#if GUROBI_INTEGRATION

using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class MatrixIdentityGurobiIntegrationTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SolveCarriesStableIdentityAndRejectsChangedRegistry(bool change) {
        var registry = new ModelElementIdentityRegistry();
        var x = new BinVar("identity-solve-x");
        using var parent = new LinearMetaModel<Flt64>(identityRegistry: registry);
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new LinearFlattenData<Flt64>([new LinearMonomial<Flt64>(Flt64.One, x)], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);
        Assert.True(registry.RegisterVariable(
            element: x,
            id: new VariableId("variable:x"),
            origin: new ModelElementOrigin("variable", "x")
        ).IsOk);
        Assert.True(registry.RegisterObjective(
            element: parent.MetaSubObjects[0],
            id: new ObjectiveId("objective:x"),
            origin: new ModelElementOrigin("objective", "x")
        ).IsOk);
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        Assert.Equal("variable:x", Assert.Single(matrix.Variables).Id?.Value);
        Assert.Equal("objective:x", matrix.Objective.Id?.Value);
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            if (change) {
                Assert.True(registry.RegisterVariable(
                    element: new object(),
                    id: new VariableId("variable:other"),
                    origin: new ModelElementOrigin("variable", "other")
                ).IsOk);
            }
            return Task.FromResult(Results.OkInstance);
        });
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver(callBack: callback).InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.Equal(1.0, Assert.Single(solved.Value.Solution.Values).ToDouble(), 8);
        Result<Success, ErrorCode, Error<ErrorCode>> applied = mechanism.ApplySolverSolution(
            matrix: matrix,
            solution: solved.Value.Solution.Values
        );
        Assert.Equal(!change, applied.IsOk);
        Assert.Equal(change ? Flt64.Zero : Flt64.One, parent.Tokens.Find(x)!.Result);
    }
}
#endif
