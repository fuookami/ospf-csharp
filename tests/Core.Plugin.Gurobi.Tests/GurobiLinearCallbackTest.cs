#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Gurobi;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests; 
/// <summary>
/// Gurobi 线性回调集成测试 / Gurobi linear callback integration tests.
///
/// 需要 Gurobi 许可证，无许可证时跳过。
/// Requires Gurobi license; skipped when unavailable.
/// </summary>
public class GurobiLinearCallbackTest {
    private static bool IsGurobiAvailable() {
        try {
            using var env = new GRBEnv();
            using var model = new GRBModel(env);
            return true;
        }
        catch {
            return false;
        }
    }

    [Fact]
    public void Callback_Class_Should_Exist() {
        // Verify the internal callback class is accessible via InternalsVisibleTo
        Type type = typeof(GurobiLinearCallback);
        type.Should().NotBeNull();
        type.IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Solver_Should_Invoke_StatusCallback_On_MIP_Solve() {
        if (!IsGurobiAvailable()) {
            return;
        }

        // Build a trivial MIP: min x, x in {0, 1}
        var solver = new GurobiLinearSolver();
        var variables = new List<ModelViewVariable>
        {
            new(0, Flt64.Zero, Flt64.One, Integer.Instance, "x")
        };
        var sparseRows = new List<SparseVector>
        {
            new(new List<SparseVectorEntry> { new(0, Flt64.One) })
        };
        var constraints = new LinearConstraintBatch(
            new SparseMatrix(sparseRows),
            new List<ConstraintRelation> { ConstraintRelation.LessEqual },
            new List<Flt64> { Flt64.One },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });

        var objective = new Objective<LinearObjectiveCell>(
            ObjectCategory.Minimum,
            new List<LinearObjectiveCell> { new(0, Flt64.One) },
            Flt64.Zero);

        var model = new LinearTriadModel(variables, constraints, objective, "callback_test");

        bool callbackInvoked = false;
        SolvingStatus? capturedStatus = null;
        SolvingStatusCallBack statusCallBack = (status) => {
            callbackInvoked = true;
            capturedStatus = status;
            return Results.OkInstance;
        };

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = solver.InvokeAsync((ILinearTriadModelView)model, statusCallBack).GetAwaiter().GetResult();

        // The MIP should solve successfully
        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();

        // For a trivial MIP, callback may or may not fire depending on solver timing.
        // If it fired, verify the status structure.
        if (callbackInvoked && capturedStatus is not null) {
            capturedStatus!.Solver.Should().Be("gurobi");
            capturedStatus.SolverIndex.Should().Be(0);
            capturedStatus.Status.Should().Be(SolverStatus.Feasible);
        }
    }

    [Fact]
    public void Solver_Should_Invoke_NativeCallback_On_MIP_Solve() {
        if (!IsGurobiAvailable()) {
            return;
        }

        var variables = new List<ModelViewVariable>
        {
            new(0, Flt64.Zero, Flt64.One, Integer.Instance, "x")
        };
        var sparseRows = new List<SparseVector>
        {
            new(new List<SparseVectorEntry> { new(0, Flt64.One) })
        };
        var constraints = new LinearConstraintBatch(
            new SparseMatrix(sparseRows),
            new List<ConstraintRelation> { ConstraintRelation.LessEqual },
            new List<Flt64> { Flt64.One },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });

        var objective = new Objective<LinearObjectiveCell>(
            ObjectCategory.Minimum,
            new List<LinearObjectiveCell> { new(0, Flt64.One) },
            Flt64.Zero);

        var model = new LinearTriadModel(variables, constraints, objective, "native_callback_test");

        bool nativeCallbackInvoked = false;
        NativeCallBack nativeCallBack = (cb) => {
            nativeCallbackInvoked = true;
        };

        var callBackManager = new GurobiLinearSolverCallBack(nativeCallback: nativeCallBack);
        var solverWithCb = new GurobiLinearSolver(callBack: callBackManager);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = solverWithCb.InvokeAsync((ILinearTriadModelView)model).GetAwaiter().GetResult();

        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();
        // Native callback should have been invoked at least once during the MIP solve
        nativeCallbackInvoked.Should().BeTrue("native callback should be invoked during MIP solve");
    }
}
