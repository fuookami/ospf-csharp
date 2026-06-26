#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Plugin.Hexaly;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Hexaly.Optimizer;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Hexaly.Tests;

/// <summary>
/// Hexaly 线性求解器冒烟测试 / Hexaly linear solver smoke tests.
///
/// 需要 Hexaly 许可证，无许可证时跳过。
/// Requires Hexaly license; skipped when unavailable.
/// </summary>
public class HexalyLinearSolverSmokeTest {
    private static bool IsHexalyAvailable() {
        try {
            string dllPath = @"D:\environment\solvers\hexaly_13_0\bin\Hexaly.NET.dll";
            if (!System.IO.File.Exists(dllPath)) {
                return false;
            }
            return TryInitHexaly();
        }
        catch {
            return false;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool TryInitHexaly() {
        try {
            using var optimizer = new HexalyOptimizer();
            return true;
        }
        catch {
            return false;
        }
    }

    /// <summary>
    /// 构建简单 LP 模型: min x1+x2, s.t. x1+x2>=1, x1>=0, x2>=0.
    /// Build simple LP model: min x1+x2, s.t. x1+x2>=1, x1>=0, x2>=0.
    /// </summary>
    private static LinearTriadModel BuildSimpleLP() {
        List<ModelViewVariable> variables = [
            new(0, Flt64.Zero, new Flt64(double.PositiveInfinity), Continuous.Instance, "x1"),
            new(1, Flt64.Zero, new Flt64(double.PositiveInfinity), Continuous.Instance, "x2")
        ];

        // Constraint: x1 + x2 >= 1
        List<SparseVector> sparseRows = [
            new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)])
        ];
        var constraints = new LinearConstraintBatch(
            new SparseMatrix(sparseRows),
            [ConstraintRelation.GreaterEqual],
            [Flt64.One],
            ["c0"],
            [ConstraintSource.Origin]);

        // Objective: min x1 + x2
        var objective = new Objective<LinearObjectiveCell>(
            ObjectCategory.Minimum,
            [new LinearObjectiveCell(0, Flt64.One), new LinearObjectiveCell(1, Flt64.One)],
            Flt64.Zero);

        return new LinearTriadModel(variables, constraints, objective, "smoke_lp");
    }

    [Fact]
    public async Task LP_Should_Solve_To_Optimal() {
        if (!IsHexalyAvailable()) {
            return;
        }

        HexalyLinearSolver solver = new();
        LinearTriadModel model = BuildSimpleLP();

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync((ILinearTriadModelView)model);

        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();
        var ok = (Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result;
        ok.Value.Obj.ToDouble().Should().BeApproximately(1.0, 1e-6);
        ok.Value.Solution.Values.Should().HaveCount(2);
    }

    [Fact]
    public void Solver_Name_Should_Be_Hexaly() {
        HexalyLinearSolver solver = new();
        solver.Name.Should().Be("hexaly");
    }

    [Fact]
    public void SolverCallBack_Should_Support_AfterModeling() {
        if (!IsHexalyAvailable()) {
            return;
        }

        HexalySolverCallBack callBack = new();
        callBack.AfterModeling((SolverStatus? status, HexalyOptimizer optimizer, IReadOnlyList<HxExpression> vars, IReadOnlyList<HxExpression> constrs) => Results.OkInstance);
        callBack.Contains(CallBackPoint.AfterModeling).Should().BeTrue();
    }
}
