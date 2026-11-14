#nullable enable
#if GUROBI_INTEGRATION

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
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

public class FixedVariableSolutionGurobiIntegrationTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllFixedLinearColumns_ShouldRespectConstantFeasibility(bool feasible) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("all-fixed-x");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(
                [new LinearMonomial<Flt64>(new Flt64(2), x)],
                new Flt64(3)
            ),
            name: "all-fixed-objective",
            displayName: null
        ).IsOk);
        Assert.True(parent.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(Flt64.One, x)], Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>([], Flt64.One),
                Comparison: Comparison.GE
            ),
            group: null
        ).IsOk);
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel(
            new Dictionary<IVariableItem, Flt64> { [x] = feasible ? Flt64.One : Flt64.Zero }
        ).Value;
        Assert.Empty(matrix.Variables);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        if (!feasible) {
            Assert.True(solved.IsFailed);
            Assert.Null(parent.Tokens.Find(x)!.Result);
            return;
        }
        Assert.True(solved.IsOk, solved.ToString());
        Assert.Equal(new Flt64(5), solved.Value.Obj);
        Assert.Empty(solved.Value.Solution.Values);
        Assert.True(mechanism.ApplySolverSolution(matrix, solved.Value.Solution.Values).IsOk);
        Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllFixedQuadraticColumns_ShouldRespectConstantFeasibility(bool feasible) {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new BinVar("quadratic-all-fixed-x");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new QuadraticFlattenData<Flt64>(
                [new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(2),
                    Symbol1: x,
                    Symbol2: x
                )],
                new Flt64(3)
            ),
            name: "quadratic-all-fixed-objective",
            displayName: null
        ).IsOk);
        Assert.True(parent.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(Flt64.One, x)], Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>([], Flt64.One),
                Comparison: Comparison.GE
            ),
            group: null
        ).IsOk);
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel(
            new Dictionary<IVariableItem, Flt64> { [x] = feasible ? Flt64.One : Flt64.Zero }
        ).Value;
        Assert.Empty(matrix.Variables);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiQuadraticSolver().InvokeAsync((IQuadraticTetradModelView)matrix);
        if (!feasible) {
            Assert.True(solved.IsFailed);
            Assert.Null(parent.Tokens.Find(x)!.Result);
            return;
        }
        Assert.True(solved.IsOk, solved.ToString());
        Assert.Equal(new Flt64(5), solved.Value.Obj);
        Assert.Empty(solved.Value.Solution.Values);
        Assert.True(mechanism.ApplySolverSolution(matrix, solved.Value.Solution.Values).IsOk);
        Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinearFixedColumns_ShouldRestoreAndRejectStaleSource(bool replace) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("linear-fixed-x");
        var y = new BinVar("linear-free-y");
        var z = new BinVar("linear-fixed-z");
        var w = new BinVar("linear-free-w");
        Assert.True(parent.Add(new IVariableItem[] { x, y, z, w }).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] {
                new LinearMonomial<Flt64>(new Flt64(-2), x),
                new LinearMonomial<Flt64>(new Flt64(-3), y),
                new LinearMonomial<Flt64>(new Flt64(4), z),
                new LinearMonomial<Flt64>(new Flt64(-1), w),
            }, Flt64.Zero),
            name: "fixed-linear-objective",
            displayName: null
        ).IsOk);
        Assert.True(parent.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(new[] {
                    new LinearMonomial<Flt64>(Flt64.One, y),
                    new LinearMonomial<Flt64>(Flt64.One, w),
                }, Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>([], Flt64.One),
                Comparison: Comparison.LE
            ),
            group: null
        ).IsOk);
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel(
            new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [z] = Flt64.Zero }
        ).Value;
        Assert.Equal(new[] { y, w }, matrix.Variables.Select(column => column.Origin));
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            if (replace) {
                Assert.True(parent.Add(z).IsOk);
            }
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiLinearSolver(callBack: callback);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.Equal(new Flt64(-5), solved.Value.Obj);
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> restored =
            mechanism.RestoreSolverSolution(matrix, solved.Value.Solution.Values);
        if (replace) {
            Assert.True(restored.IsFailed);
            Assert.True(mechanism.ApplySolverSolution(matrix, solved.Value.Solution.Values).IsFailed);
            Assert.All(parent.Tokens.TokensInSolver, token => Assert.Null(token.Result));
        }
        else {
            Assert.True(restored.IsOk, restored.ToString());
            Assert.Equal(new[] { Flt64.One, Flt64.One, Flt64.Zero, Flt64.Zero }, restored.Value);
            Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> pool =
                await solver.InvokeAsync((ILinearTriadModelView)matrix, solutionAmount: 2);
            Assert.True(pool.IsOk, pool.ToString());
            Assert.Equal(2, pool.Value.Solutions.Count);
            foreach (IReadOnlyList<Flt64> reduced in pool.Value.Solutions) {
                Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> expanded =
                    mechanism.RestoreSolverSolution(matrix, reduced);
                Assert.True(expanded.IsOk, expanded.ToString());
                Assert.Equal(Flt64.One, expanded.Value[0]);
                Assert.Equal(Flt64.Zero, expanded.Value[2]);
                Assert.True(expanded.Value[1].ToDouble() + expanded.Value[3].ToDouble() <= 1.0);
            }
            Assert.True(mechanism.ApplySolverSolution(matrix, solved.Value.Solution.Values).IsOk);
            Assert.Equal(restored.Value, parent.Tokens.TokensInSolver.Select(token => token.Result!.Value));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuadraticFixedColumns_ShouldRestoreAndRejectStaleSource(bool replace) {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new BinVar("quadratic-fixed-x");
        var y = new BinVar("quadratic-free-y");
        var z = new BinVar("quadratic-fixed-z");
        var w = new BinVar("quadratic-free-w");
        Assert.True(parent.Add(new IVariableItem[] { x, y, z, w }).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new QuadraticFlattenData<Flt64>(new[] {
                new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(2),
                    Symbol1: x,
                    Symbol2: y
                ),
                new QuadraticMonomial<Flt64>(
                    Coefficient: Flt64.One,
                    Symbol1: y,
                    Symbol2: y
                ),
                new QuadraticMonomial<Flt64>(new Flt64(-4), y),
                new QuadraticMonomial<Flt64>(new Flt64(5), z),
                new QuadraticMonomial<Flt64>(new Flt64(3), w),
            }, Flt64.Zero),
            name: "fixed-quadratic-objective",
            displayName: null
        ).IsOk);
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel(
            new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [z] = Flt64.Zero }
        ).Value;
        Assert.Equal(new[] { y, w }, matrix.Variables.Select(column => column.Origin));
        GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack().AfterModeling((_, _, _, _) => {
            if (replace) {
                Assert.True(parent.Add(z).IsOk);
            }
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiQuadraticSolver(callBack: callback);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeAsync((IQuadraticTetradModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.Equal(new Flt64(-1), solved.Value.Obj);
        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> restored =
            mechanism.RestoreSolverSolution(matrix, solved.Value.Solution.Values);
        if (replace) {
            Assert.True(restored.IsFailed);
            Assert.True(mechanism.ApplySolverSolution(matrix, solved.Value.Solution.Values).IsFailed);
            Assert.All(parent.Tokens.TokensInSolver, token => Assert.Null(token.Result));
        }
        else {
            Assert.True(restored.IsOk, restored.ToString());
            Assert.Equal(new[] { Flt64.One, Flt64.One, Flt64.Zero, Flt64.Zero }, restored.Value);
            Assert.True(mechanism.ApplySolverSolution(matrix, solved.Value.Solution.Values).IsOk);
            Assert.Equal(restored.Value, parent.Tokens.TokensInSolver.Select(token => token.Result!.Value));
        }
    }
}
#endif
