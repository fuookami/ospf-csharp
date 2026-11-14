#nullable enable
#if GUROBI_AVAILABLE

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class MatrixIdentityGurobiTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinearRejectsInvalidIdentityBeforeEnvironment(bool pool) {
        bool called = false;
        var callback = new GurobiLinearSolverCallBack(creatingEnvironmentFunction: _ => {
            called = true;
            return Results.OkInstance;
        });
        using var model = new LinearTriadModel(
            variables: [InvalidVariable()],
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(),
                signs: [],
                rhs: [],
                names: [],
                sources: []
            ),
            objective: new Objective<LinearObjectiveCell>(ObjectCategory.Minimum, []),
            name: "invalid-identity"
        );
        var solver = new GurobiLinearSolver(callBack: callback);
        if (pool) {
            Result<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>> result =
                await solver.InvokeAsync((ILinearTriadModelView)model, 2);
            Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        else {
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
                await solver.InvokeAsync((ILinearTriadModelView)model);
            Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuadraticRejectsInvalidIdentityBeforeEnvironment(bool pool) {
        bool called = false;
        var callback = new GurobiQuadraticSolverCallBack(creatingEnvironmentFunction: _ => {
            called = true;
            return Results.OkInstance;
        });
        using var model = new QuadraticTetradModel(
            variables: [InvalidVariable()],
            constraints: new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix(),
                signs: [],
                rhs: [],
                names: [],
                sources: []
            ),
            objective: new Objective<QuadraticObjectiveCell>(ObjectCategory.Minimum, []),
            name: "invalid-identity"
        );
        var solver = new GurobiQuadraticSolver(callBack: callback);
        if (pool) {
            Result<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>> result =
                await solver.InvokeAsync((IQuadraticTetradModelView)model, 2);
            Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        else {
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
                await solver.InvokeAsync((IQuadraticTetradModelView)model);
            Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        Assert.False(called);
    }

    private static ModelViewVariable InvalidVariable() => new(
        index: 0,
        lowerBound: Flt64.Zero,
        upperBound: Flt64.One,
        type: Continuous.Instance,
        name: "x",
        identity: new ModelElementIdentity(
            Kind: ModelElementKind.Variable,
            Id: new ModelElementId("variable:x"),
            Scope: ModelElementScope.Stable
        ),
        identityNamespace: "model",
        identitySchemaVersion: "1"
    );
}
#endif
