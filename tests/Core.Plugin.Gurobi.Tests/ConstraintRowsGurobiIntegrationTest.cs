#nullable enable
#if GUROBI_INTEGRATION

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class ConstraintRowsGurobiIntegrationTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinearSolve_ShouldUseCurrentSparseCoefficients(bool mutate) {
        using LinearTriadModel model = LinearModel();
        if (mutate) {
            ((LinearConstraintBatch)model.Constraints).SparseLhs.Rows[0].Entries[0] = new SparseVectorEntry(0, new Flt64(2));
        }
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(mutate ? 0.5 : 1.0, Assert.Single(result.Value.Solution.Values).ToDouble(), 8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuadraticSolve_ShouldUseCurrentSparseCoefficients(bool mutate) {
        using QuadraticTetradModel model = QuadraticModel();
        if (mutate) {
            ((QuadraticConstraintBatch)model.Constraints).SparseLhs.Rows[0].Entries[0] = new SparseQuadraticEntry(
                Col1: 0,
                Col2: -1,
                Value: new Flt64(2)
            );
        }
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new GurobiQuadraticSolver().InvokeAsync((IQuadraticTetradModelView)model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(mutate ? 0.5 : 1.0, Assert.Single(result.Value.Solution.Values).ToDouble(), 8);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LinearSolve_ShouldRejectExtraRowsBeforeOrDuringEnvironment(bool pool, bool duringEnvironment) {
        using LinearTriadModel model = LinearModel(binary: pool);
        SparseMatrix rows = ((LinearConstraintBatch)model.Constraints).SparseLhs;
        bool called = false;
        var callback = new GurobiLinearSolverCallBack(creatingEnvironmentFunction: _ => {
            called = true;
            if (duringEnvironment) {
                rows.AddRow(new SparseVector());
            }
            return Results.OkInstance;
        });
        if (!duringEnvironment) {
            rows.AddRow(new SparseVector());
        }
        var solver = new GurobiLinearSolver(callBack: callback);
        if (pool) {
            AssertIllegalArgument(await solver.InvokeAsync((ILinearTriadModelView)model, 2));
        }
        else {
            AssertIllegalArgument(await solver.InvokeAsync((ILinearTriadModelView)model));
        }
        Assert.Equal(duringEnvironment, called);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task QuadraticSolve_ShouldRejectExtraRowsBeforeOrDuringEnvironment(bool pool, bool duringEnvironment) {
        using QuadraticTetradModel model = QuadraticModel(binary: pool);
        SparseQuadraticMatrix rows = ((QuadraticConstraintBatch)model.Constraints).SparseLhs;
        bool called = false;
        var callback = new GurobiQuadraticSolverCallBack(creatingEnvironmentFunction: _ => {
            called = true;
            if (duringEnvironment) {
                rows.AddRow(new SparseQuadraticVector());
            }
            return Results.OkInstance;
        });
        if (!duringEnvironment) {
            rows.AddRow(new SparseQuadraticVector());
        }
        var solver = new GurobiQuadraticSolver(callBack: callback);
        if (pool) {
            AssertIllegalArgument(await solver.InvokeAsync((IQuadraticTetradModelView)model, 2));
        }
        else {
            AssertIllegalArgument(await solver.InvokeAsync((IQuadraticTetradModelView)model));
        }
        Assert.Equal(duringEnvironment, called);
    }

    [Fact]
    public async Task LinearPool_ShouldAcceptValidRows() {
        using LinearTriadModel model = LinearModel(binary: true);
        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> result =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)model, 2);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(2, result.Value.Solutions.Count);
    }

    [Fact]
    public async Task QuadraticPool_ShouldAcceptValidRows() {
        using QuadraticTetradModel model = QuadraticModel(binary: true);
        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> result =
            await new GurobiQuadraticSolver().InvokeAsync((IQuadraticTetradModelView)model, 2);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(2, result.Value.Solutions.Count);
    }

    private static void AssertIllegalArgument<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<T, ErrorCode, Error<ErrorCode>>>(result).Code);

    private static LinearTriadModel LinearModel(bool binary = false) => new(
        variables: Variables(binary),
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([new SparseVector([new SparseVectorEntry(0, Flt64.One)])]),
            signs: [ConstraintRelation.LessEqual],
            rhs: [Flt64.One],
            names: ["row"],
            sources: [ConstraintSource.Origin]
        ),
        objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, [new LinearObjectiveCell(0, -Flt64.One)]),
        name: "row-linear"
    );

    private static QuadraticTetradModel QuadraticModel(bool binary = false) => new(
        variables: Variables(binary),
        constraints: new QuadraticConstraintBatch(
            lhs: new SparseQuadraticMatrix([new SparseQuadraticVector([new SparseQuadraticEntry(
                Col1: 0,
                Col2: -1,
                Value: Flt64.One
            )])]),
            signs: [ConstraintRelation.LessEqual],
            rhs: [Flt64.One],
            names: ["row"],
            sources: [ConstraintSource.Origin]
        ),
        objective: new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, [new QuadraticObjectiveCell(
            Col1: 0,
            Col2: -1,
            Coefficient: -Flt64.One
        )]),
        name: "row-quadratic"
    );

    private static IReadOnlyList<ModelViewVariable> Variables(bool binary) => [new ModelViewVariable(
        index: 0,
        lowerBound: Flt64.Zero,
        upperBound: Flt64.One,
        type: binary ? Binary.Instance : Continuous.Instance,
        name: "x"
    )];
}
#endif
