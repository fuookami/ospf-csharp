#nullable enable
#if GUROBI_INTEGRATION

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

public class SparseSnapshotGurobiIntegrationTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinearSolve_ShouldRejectRestoredSparseChanges(bool mutate) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("sparse-linear-x");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        Assert.True(parent.AddConstraint(
            relation: Row(
                variable: x,
                sign: Comparison.LE,
                rhs: Flt64.One
            ),
            group: null
        ).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>([new LinearMonomial<Flt64>(-Flt64.One, x)], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            called = true;
            if (mutate) {
                SparseVector row = ((LinearConstraintBatch)matrix.Constraints).SparseLhs.Rows[0];
                SparseVectorEntry original = row.Entries[0];
                row.Entries[0] = original with { Value = new Flt64(2) };
                row.Entries[0] = original;
            }
            return Task.FromResult(Results.OkInstance);
        });

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver(callBack: callback).InvokeAsync((ILinearTriadModelView)matrix);

        Assert.True(called);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.Equal(Flt64.One, solved.Value.Solution.Values[0]);
        Assert.Equal(!mutate, mechanism.ApplySolverSolution(
            matrix: matrix,
            solution: solved.Value.Solution.Values
        ).IsOk);
        Assert.Equal(mutate ? Flt64.Zero : Flt64.One, parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuadraticSolve_ShouldRejectRestoredSparseChanges(bool mutate) {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new BinVar("sparse-quadratic-x");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        Assert.True(parent.AddConstraint(
            relation: Row(
                variable: x,
                sign: Comparison.LE,
                rhs: Flt64.One
            ),
            group: null
        ).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new QuadraticFlattenData<Flt64>([new QuadraticMonomial<Flt64>(-Flt64.One, x)], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel().Value;
        bool called = false;
        GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack().AfterModeling((_, _, _, _) => {
            called = true;
            if (mutate) {
                SparseQuadraticVector row = ((QuadraticConstraintBatch)matrix.Constraints).SparseLhs.Rows[0];
                SparseQuadraticEntry original = row.Entries[0];
                row.Entries[0] = original with { Value = new Flt64(2) };
                row.Entries[0] = original;
            }
            return Task.FromResult(Results.OkInstance);
        });

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiQuadraticSolver(callBack: callback).InvokeAsync((IQuadraticTetradModelView)matrix);

        Assert.True(called);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.Equal(Flt64.One, solved.Value.Solution.Values[0]);
        Assert.Equal(!mutate, mechanism.ApplySolverSolution(
            matrix: matrix,
            solution: solved.Value.Solution.Values
        ).IsOk);
        Assert.Equal(mutate ? Flt64.Zero : Flt64.One, parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InfeasibleLinearSolve_ShouldRejectRestoredSparseChanges(bool mutate) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("sparse-infeasible-x");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddConstraint(
            relation: Row(
                variable: x,
                sign: Comparison.LE,
                rhs: Flt64.Zero
            ),
            group: null
        ).IsOk);
        Assert.True(parent.AddConstraint(
            relation: Row(
                variable: x,
                sign: Comparison.GE,
                rhs: Flt64.One
            ),
            group: null
        ).IsOk);
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterFailure((_, _, _, _) => {
            called = true;
            if (mutate) {
                SparseVector row = ((LinearConstraintBatch)matrix.Constraints).SparseLhs.Rows[0];
                SparseVectorEntry original = row.Entries[0];
                row.Entries[0] = original with { Value = new Flt64(2) };
                row.Entries[0] = original;
            }
            return Task.FromResult(Results.OkInstance);
        });

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver(callBack: callback).InvokeAsync((ILinearTriadModelView)matrix);

        Assert.True(called);
        Assert.Equal(ErrorCode.ORModelInfeasible, Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(solved).Code);
        Assert.Equal(!mutate, mechanism.ValidateSolverTokenSnapshot(matrix).IsOk);
        Assert.Null(parent.Tokens.Find(x)!.Result);
    }

    private static LinearInequality<Flt64> Row(BinVar variable, Comparison sign, Flt64 rhs) => new(
        Lhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(Flt64.One, variable)], Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>([], rhs),
        Comparison: sign
    );
}
#endif
