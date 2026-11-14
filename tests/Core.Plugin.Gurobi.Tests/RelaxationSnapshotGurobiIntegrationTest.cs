#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gurobi;
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
using BendersSolver = Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver;
using ColumnSolver = Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class RelaxationSnapshotGurobiIntegrationTest {
    [Fact]
    public async Task ColumnGeneration_ShouldReturnRelaxedSolutionAndRowDual() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("relaxation-dual-x");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(
            variable: x,
            sign: Comparison.LE,
            rhs: new Flt64(0.5)
        ), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>([new LinearMonomial<Flt64>(-Flt64.One, x)], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);

        Result<ColumnSolver.LpResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiColumnGenerationSolver().SolveLPAsync("relaxation-dual", parent);

        Assert.True(result.IsOk, result.ToString());
        Assert.InRange(
            actual: result.Value.Solution[0].ToDouble(),
            low: 0.5 - 1e-9,
            high: 0.5 + 1e-9
        );
        Assert.InRange(
            actual: result.Value.DualSolution[parent.RelationConstraints[0]].ToDouble(),
            low: -1.0 - 1e-9,
            high: -1.0 + 1e-9
        );
        Assert.InRange(
            actual: parent.Tokens.Find(x)!.Result!.Value.ToDouble(),
            low: 0.5 - 1e-9,
            high: 0.5 + 1e-9
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrameworkRelaxation_ShouldRejectChangesDuringDualExtraction(bool benders) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("relaxation-dual-stale-x");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        Assert.True(parent.AddConstraint(relation: Row(
            variable: x,
            sign: Comparison.LE,
            rhs: Flt64.One
        ), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>([new LinearMonomial<Flt64>(-Flt64.One, x)], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AnalyzingSolution((_, _, _, _) => {
            called = true;
            Assert.True(parent.AddConstraint(relation: Row(
                variable: x,
                sign: Comparison.LE,
                rhs: Flt64.Zero
            ), group: null).IsOk);
            return Task.FromResult(Results.OkInstance);
        });

        if (benders) {
            Result<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
                await new GurobiBendersDecompositionSolver(callBack: callback).SolveSubAsync(
                    name: "relaxation-dual-stale",
                    metaModel: parent,
                    objectVariable: x,
                    fixedVariables: new Dictionary<IVariableItem, Flt64>()
                );
            Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<BendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        else {
            Result<ColumnSolver.LpResult, ErrorCode, Error<ErrorCode>> result =
                await new GurobiColumnGenerationSolver(callBack: callback).SolveLPAsync("relaxation-dual-stale", parent);
            Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<ColumnSolver.LpResult, ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        Assert.True(called);
        Assert.Equal(Flt64.Zero, parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task FeasibleRelaxation_ShouldRejectChangedSourceOrSolvedRows(int change) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("relaxation-feasible-x");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        Assert.True(parent.AddConstraint(relation: Row(
            variable: x,
            sign: Comparison.LE,
            rhs: Flt64.One
        ), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>([new LinearMonomial<Flt64>(-Flt64.One, x)], Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel original = mechanism.ToLinearTriadModel().Value;
        using LinearTriadModel relaxed = original.RelaxToContinuous().Value;
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            called = true;
            if (change != 0) {
                ChangeAndRestore(change == 1 ? original : relaxed);
            }
            return Task.FromResult(Results.OkInstance);
        });

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver(callBack: callback).InvokeAsync((ILinearTriadModelView)relaxed);

        Assert.True(called);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.Equal(Flt64.One, solved.Value.Solution.Values[0]);
        Assert.Equal(change == 0, mechanism.ApplySolverSolution(
            matrix: original,
            solvedMatrix: relaxed,
            solution: solved.Value.Solution.Values
        ).IsOk);
        Assert.Equal(change == 0 ? Flt64.One : Flt64.Zero, parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task InfeasibleRelaxation_ShouldRejectStaleFarkasRows(int change) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("relaxation-farkas-x");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(
            variable: x,
            sign: Comparison.LE,
            rhs: Flt64.Zero
        ), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Row(
            variable: x,
            sign: Comparison.GE,
            rhs: Flt64.One
        ), group: null).IsOk);
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel original = mechanism.ToLinearTriadModel().Value;
        using LinearTriadModel relaxed = original.RelaxToContinuous().Value;
        bool captured = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack()
            .Configuration((_, native, _, _) => {
                native.Set(GRB.IntParam.InfUnbdInfo, 1);
                native.Set(GRB.IntParam.DualReductions, 0);
                return Task.FromResult(Results.OkInstance);
            })
            .AfterFailure((status, _, _, constraints) => {
                Assert.Equal(SolverStatus.Infeasible, status);
                captured = System.Math.Abs(constraints[0].Get(GRB.DoubleAttr.FarkasDual)) > 0;
                if (change != 0) {
                    ChangeAndRestore(change == 1 ? original : relaxed);
                }
                return Task.FromResult(Results.OkInstance);
            });

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver(callBack: callback).InvokeAsync((ILinearTriadModelView)relaxed);

        Assert.True(captured);
        Assert.Equal(ErrorCode.ORModelInfeasible,
            Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(solved).Code);
        Assert.Equal(change == 0, mechanism.ValidateSolverTokenSnapshot(original, relaxed).IsOk);
        Assert.Null(parent.Tokens.Find(x)!.Result);
    }

    private static void ChangeAndRestore(LinearTriadModel matrix) {
        SparseVector row = ((LinearConstraintBatch)matrix.Constraints).SparseLhs.Rows[0];
        SparseVectorEntry original = row.Entries[0];
        row.Entries[0] = original with { Value = new Flt64(2) };
        row.Entries[0] = original;
    }

    private static LinearInequality<Flt64> Row(BinVar variable, Comparison sign, Flt64 rhs) => new(
        Lhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(Flt64.One, variable)], Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>([], rhs),
        Comparison: sign
    );
}
#endif
