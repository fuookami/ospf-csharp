#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gurobi;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class TokenOrderingGurobiIntegrationTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RemovedOrReplacedToken_ShouldKeepWarmStartAndSolutionInColumnOrder(bool manual, bool replace) {
        using var parent = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: manual)
        );
        var x = new BinVar("ordering-x");
        var removed = new BinVar("ordering-removed");
        var y = new BinVar("ordering-y");
        var z = new BinVar("ordering-z");
        Assert.True(parent.Add(new IVariableItem[] { x, removed, y }).IsOk);
        parent.Remove(removed);
        Assert.True(parent.Add(z).IsOk);
        if (replace) {
            parent.Tokens.Find(y)!.SetResult(Flt64.One);
            Assert.True(parent.Add(y).IsOk);
            Assert.Null(parent.Tokens.Find(y)!.Result);
        }
        parent.Tokens.SetSolverSolution(replace
            ? new[] { Flt64.Zero, Flt64.Zero, Flt64.One }
            : new[] { Flt64.Zero, Flt64.One, Flt64.Zero });
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] {
                new LinearMonomial<Flt64>(new Flt64(-2), x),
                new LinearMonomial<Flt64>(new Flt64(3), y),
                new LinearMonomial<Flt64>(new Flt64(-4), z),
            }, Flt64.Zero),
            name: "ordering-objective",
            displayName: null
        ).IsOk);
        var row = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(new[] {
                new LinearMonomial<Flt64>(Flt64.One, x),
                new LinearMonomial<Flt64>(Flt64.One, y),
            }, Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
            Comparison: Comparison.LE
        );
        Assert.True(parent.AddConstraint(relation: row, group: null).IsOk);

        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        IReadOnlyList<string>? names = null;
        IReadOnlyList<double>? starts = null;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, native, variables, _) => {
            native.Update();
            names = variables.Select(variable => variable.Get(GRB.StringAttr.VarName)).ToArray();
            starts = variables.Select(variable => variable.Get(GRB.DoubleAttr.Start)).ToArray();
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiLinearSolver(callBack: callback);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved = await solver.InvokeAsync((ILinearTriadModelView)matrix);

        Assert.True(solved.IsOk, solved.ToString());
        Assert.Equal(replace ? new[] { x.Name, z.Name, y.Name } : new[] { x.Name, y.Name, z.Name }, names);
        Assert.Equal(replace ? new[] { 0.0, 0.0, 1.0 } : new[] { 0.0, 1.0, 0.0 }, starts);
        Assert.Equal(replace ? new[] { 0, 2 } : new[] { 0, 1 }, matrix.Constraints.Lhs[0].Select(cell => cell.ColIndex));
        Assert.Equal(new Flt64(-6), solved.Value.Obj);
        Assert.Equal(replace ? new[] { Flt64.One, Flt64.One, Flt64.Zero } : new[] { Flt64.One, Flt64.Zero, Flt64.One }, solved.Value.Solution.Values);
        parent.Tokens.SetSolverSolution(solved.Value.Solution.Values);
        Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);
        Assert.Equal(Flt64.Zero, parent.Tokens.Find(y)!.Result);
        Assert.Equal(Flt64.One, parent.Tokens.Find(z)!.Result);
    }
}
#endif
