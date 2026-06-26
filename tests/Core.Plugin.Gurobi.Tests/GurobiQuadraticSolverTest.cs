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
/// Gurobi 二次求解器集成测试 / Gurobi quadratic solver integration tests.
///
/// 需要 Gurobi 10 许可证，无许可证时跳过。
/// Requires Gurobi 10 license; skipped when unavailable.
/// </summary>
[Trait("Category", "Gurobi")]
public class GurobiQuadraticSolverTest {
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
    public void Quadratic_Solver_Should_Solve_Simple_QP() {
        if (!IsGurobiAvailable()) {
            return;
        }

        // min x^2 + y^2 subject to x + y >= 1, x >= 0, y >= 0
        // Optimal: x = y = 0.5, obj = 0.5
        var variables = new List<ModelViewVariable> {
            new(0, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x"),
            new(1, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "y"),
        };

        // Constraint: x + y >= 1 (linear term in quadratic format, Col2 = -1)
        var conEntries = new List<SparseQuadraticEntry> {
            new(0, -1, Flt64.One),  // x
            new(1, -1, Flt64.One),  // y
        };
        var sparseRows = new List<SparseQuadraticVector> {
            new(conEntries),
        };
        var constraints = new QuadraticConstraintBatch(
            new SparseQuadraticMatrix(sparseRows),
            new List<ConstraintRelation> { ConstraintRelation.GreaterEqual },
            new List<Flt64> { Flt64.One },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });

        // Objective: x^2 + y^2 (quadratic diagonal terms)
        var objCells = new List<QuadraticObjectiveCell> {
            new(0, 0, Flt64.One),   // x^2
            new(1, 1, Flt64.One),   // y^2
        };
        var objective = new Objective<QuadraticObjectiveCell>(
            ObjectCategory.Minimum, objCells, Flt64.Zero);

        var model = new QuadraticTetradModel(variables, constraints, objective, "qp_test");

        var solver = new GurobiQuadraticSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            solver.InvokeAsync((IQuadraticTetradModelView)model).GetAwaiter().GetResult();

        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();

        var ok = (Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result;
        FeasibleSolverOutput<Flt64> output = ok.Value;

        // Verify optimal solution: x = y = 0.5, obj = 0.5
        output.Solution.Values[0].ToDouble().Should().BeApproximately(0.5, 1e-6, "x should be 0.5");
        output.Solution.Values[1].ToDouble().Should().BeApproximately(0.5, 1e-6, "y should be 0.5");
        output.Obj.ToDouble().Should().BeApproximately(0.5, 1e-6, "objective should be 0.5");
    }

    [Fact]
    public void Quadratic_Solver_Should_Solve_QP_With_Cross_Terms() {
        if (!IsGurobiAvailable()) {
            return;
        }

        // min x^2 + 2xy + y^2 subject to x + y <= 2, x >= 0, y >= 0
        // This is (x+y)^2, minimized at x+y as large as possible but bounded by constraint.
        // Since objective = (x+y)^2 and constraint is x+y <= 2, x >= 0, y >= 0,
        // the minimum is at x+y = 0, obj = 0 (x=0, y=0).
        // But let's use: min x^2 + xy + y^2 - x - y, subject to x + y <= 3
        // Actually let's keep it simple: min x^2 + y^2 + xy, x + y >= 1, x >= 0, y >= 0
        // Optimal at x = y = 0.5, obj = 0.25 + 0.25 + 0.25 = 0.75
        var variables = new List<ModelViewVariable> {
            new(0, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x"),
            new(1, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "y"),
        };

        // Constraint: x + y >= 1
        var conEntries = new List<SparseQuadraticEntry> {
            new(0, -1, Flt64.One),
            new(1, -1, Flt64.One),
        };
        var sparseRows = new List<SparseQuadraticVector> {
            new(conEntries),
        };
        var constraints = new QuadraticConstraintBatch(
            new SparseQuadraticMatrix(sparseRows),
            new List<ConstraintRelation> { ConstraintRelation.GreaterEqual },
            new List<Flt64> { Flt64.One },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });

        // Objective: x^2 + y^2 + xy
        var objCells = new List<QuadraticObjectiveCell> {
            new(0, 0, Flt64.One),   // x^2
            new(1, 1, Flt64.One),   // y^2
            new(0, 1, Flt64.One),   // xy
        };
        var objective = new Objective<QuadraticObjectiveCell>(
            ObjectCategory.Minimum, objCells, Flt64.Zero);

        var model = new QuadraticTetradModel(variables, constraints, objective, "qp_cross_test");

        var solver = new GurobiQuadraticSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            solver.InvokeAsync((IQuadraticTetradModelView)model).GetAwaiter().GetResult();

        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();

        var ok = (Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result;
        FeasibleSolverOutput<Flt64> output = ok.Value;

        // Symmetry: x = y = 0.5
        // obj = 0.25 + 0.25 + 0.25 = 0.75
        output.Solution.Values[0].ToDouble().Should().BeApproximately(0.5, 1e-5, "x should be 0.5");
        output.Solution.Values[1].ToDouble().Should().BeApproximately(0.5, 1e-5, "y should be 0.5");
        output.Obj.ToDouble().Should().BeApproximately(0.75, 1e-5, "objective should be 0.75");
    }

    [Fact]
    public void Quadratic_Solver_Should_Solve_Maximization_QP() {
        if (!IsGurobiAvailable()) {
            return;
        }

        // max -x^2 - y^2 + 2x + 2y subject to x + y <= 2, x >= 0, y >= 0
        // Rewrite as: max -(x-1)^2 - (y-1)^2 + 2
        // Optimal at x = y = 1, obj = 2
        var variables = new List<ModelViewVariable> {
            new(0, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x"),
            new(1, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "y"),
        };

        // Constraint: x + y <= 2
        var conEntries = new List<SparseQuadraticEntry> {
            new(0, -1, Flt64.One),
            new(1, -1, Flt64.One),
        };
        var sparseRows = new List<SparseQuadraticVector> {
            new(conEntries),
        };
        var constraints = new QuadraticConstraintBatch(
            new SparseQuadraticMatrix(sparseRows),
            new List<ConstraintRelation> { ConstraintRelation.LessEqual },
            new List<Flt64> { new Flt64(2.0) },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });

        // Objective: -x^2 - y^2 + 2x + 2y
        var objCells = new List<QuadraticObjectiveCell> {
            new(0, 0, new Flt64(-1.0)),   // -x^2
            new(1, 1, new Flt64(-1.0)),   // -y^2
            new(0, -1, new Flt64(2.0)),   // 2x (linear)
            new(1, -1, new Flt64(2.0)),   // 2y (linear)
        };
        var objective = new Objective<QuadraticObjectiveCell>(
            ObjectCategory.Maximum, objCells, Flt64.Zero);

        var model = new QuadraticTetradModel(variables, constraints, objective, "qp_max_test");

        var solver = new GurobiQuadraticSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            solver.InvokeAsync((IQuadraticTetradModelView)model).GetAwaiter().GetResult();

        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();

        var ok = (Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result;
        FeasibleSolverOutput<Flt64> output = ok.Value;

        // x = 1, y = 1, obj = 2
        output.Solution.Values[0].ToDouble().Should().BeApproximately(1.0, 1e-3, "x should be 1.0");
        output.Solution.Values[1].ToDouble().Should().BeApproximately(1.0, 1e-3, "y should be 1.0");
        output.Obj.ToDouble().Should().BeApproximately(2.0, 1e-3, "objective should be 2.0");
    }

    [Fact]
    public void Quadratic_Solver_Callback_Should_Fire() {
        if (!IsGurobiAvailable()) {
            return;
        }

        // min x^2 + y^2 subject to x + y >= 1
        var variables = new List<ModelViewVariable> {
            new(0, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x"),
            new(1, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "y"),
        };
        var conEntries = new List<SparseQuadraticEntry> {
            new(0, -1, Flt64.One),
            new(1, -1, Flt64.One),
        };
        var sparseRows = new List<SparseQuadraticVector> { new(conEntries) };
        var constraints = new QuadraticConstraintBatch(
            new SparseQuadraticMatrix(sparseRows),
            new List<ConstraintRelation> { ConstraintRelation.GreaterEqual },
            new List<Flt64> { Flt64.One },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });

        var objCells = new List<QuadraticObjectiveCell> {
            new(0, 0, Flt64.One),
            new(1, 1, Flt64.One),
        };
        var objective = new Objective<QuadraticObjectiveCell>(
            ObjectCategory.Minimum, objCells, Flt64.Zero);

        var model = new QuadraticTetradModel(variables, constraints, objective, "qp_callback_test");

        bool nativeCallbackInvoked = false;
        NativeCallBack nativeCallBack = (cb) => {
            nativeCallbackInvoked = true;
        };

        var callBackManager = new GurobiQuadraticSolverCallBack(nativeCallback: nativeCallBack);
        var solver = new GurobiQuadraticSolver(callBack: callBackManager);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            solver.InvokeAsync((IQuadraticTetradModelView)model).GetAwaiter().GetResult();

        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();
        nativeCallbackInvoked.Should().BeTrue("native callback should be invoked during QP solve");
    }
}
