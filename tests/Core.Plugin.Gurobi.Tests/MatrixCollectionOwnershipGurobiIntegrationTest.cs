#nullable enable
#if GUROBI_INTEGRATION

using System;
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

public class MatrixCollectionOwnershipGurobiIntegrationTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinearSolve_ShouldUseOwnedCollections(bool mutate) {
        List<ModelViewVariable> variables = Variables();
        var cells = new List<LinearObjectiveCell> { new(0, -Flt64.One) };
        var rhs = new List<Flt64> { Flt64.One };
        var row = new SparseVector();
        row.Add(0, Flt64.One);
        using var model = new LinearTriadModel(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix([row]),
                signs: [ConstraintRelation.LessEqual],
                rhs: rhs,
                names: ["row"],
                sources: [ConstraintSource.Origin]
            ),
            objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, cells),
            name: "owned-linear"
        );
        if (mutate) {
            variables.Clear();
            cells.Clear();
            rhs[0] = Flt64.Zero;
        }
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            called = true;
            Assert.Throws<NotSupportedException>(() => ((IList<ModelViewVariable>)model.Variables).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<LinearObjectiveCell>)model.Objective.Cells).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<Flt64>)model.Constraints.Rhs)[0] = Flt64.Zero);
            return Task.FromResult(Results.OkInstance);
        });

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new GurobiLinearSolver(callBack: callback).InvokeAsync((ILinearTriadModelView)model);

        Assert.True(called);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(Flt64.One, Assert.Single(result.Value.Solution.Values));
        Assert.Equal(-Flt64.One, result.Value.Obj);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuadraticSolve_ShouldUseOwnedCollections(bool mutate) {
        List<ModelViewVariable> variables = Variables();
        var cells = new List<QuadraticObjectiveCell> {
            new(
                Col1: 0,
                Col2: -1,
                Coefficient: -Flt64.One
            ),
        };
        var rhs = new List<Flt64> { Flt64.One };
        var row = new SparseQuadraticVector();
        row.Add(
            col1: 0,
            col2: -1,
            value: Flt64.One
        );
        using var model = new QuadraticTetradModel(
            variables: variables,
            constraints: new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix([row]),
                signs: [ConstraintRelation.LessEqual],
                rhs: rhs,
                names: ["row"],
                sources: [ConstraintSource.Origin]
            ),
            objective: new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, cells),
            name: "owned-quadratic"
        );
        if (mutate) {
            variables.Clear();
            cells.Clear();
            rhs[0] = Flt64.Zero;
        }
        bool called = false;
        GurobiQuadraticSolverCallBack callback = new GurobiQuadraticSolverCallBack().AfterModeling((_, _, _, _) => {
            called = true;
            Assert.Throws<NotSupportedException>(() => ((IList<ModelViewVariable>)model.Variables).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<QuadraticObjectiveCell>)model.Objective.Cells).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<Flt64>)model.Constraints.Rhs)[0] = Flt64.Zero);
            return Task.FromResult(Results.OkInstance);
        });

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new GurobiQuadraticSolver(callBack: callback).InvokeAsync((IQuadraticTetradModelView)model);

        Assert.True(called);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(Flt64.One, Assert.Single(result.Value.Solution.Values));
        Assert.Equal(-Flt64.One, result.Value.Obj);
    }

    private static List<ModelViewVariable> Variables() => [new ModelViewVariable(
        index: 0,
        lowerBound: Flt64.Zero,
        upperBound: Flt64.One,
        type: Binary.Instance,
        name: "x"
    )];
}
#endif
