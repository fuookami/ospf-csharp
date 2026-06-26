#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Plugin.Mosek;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using mosek;
using System;
using System.Collections.Generic;
using Xunit;
using MosekTask = mosek.Task;

namespace Fuookami.Ospf.Core.Plugin.Mosek.Tests;

/// <summary>
/// MOSEK 线性求解器冒烟测试 / MOSEK linear solver smoke tests.
///
/// 需要 MOSEK 许可证，无许可证时跳过。
/// Requires MOSEK license; skipped when unavailable.
/// </summary>
public class MosekLinearSolverSmokeTest {
    private static bool IsMosekAvailable() {
        try {
            string dllPath = @"D:\environment\solvers\Mosek\10.2\tools\platform\win64x86\bin\mosekdotnet10_2.dll";
            if (!System.IO.File.Exists(dllPath)) {
                return false;
            }
            return TryInitMosek();
        }
        catch {
            return false;
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool TryInitMosek() {
        try {
            using var env = new Env();
            using var task = new MosekTask(env, 0, 0);
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
    public async System.Threading.Tasks.Task LP_Should_Solve_To_Optimal() {
        if (!IsMosekAvailable()) {
            return;
        }

        MosekLinearSolver solver = new();
        LinearTriadModel model = BuildSimpleLP();

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync((ILinearTriadModelView)model);

        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();
        var ok = (Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result;
        ok.Value.Obj.Should().Be(new Flt64(1.0));
        ok.Value.Solution.Values.Should().HaveCount(2);
    }

    [Fact]
    public void Solver_Name_Should_Be_Mosek() {
        MosekLinearSolver solver = new();
        solver.Name.Should().Be("mosek");
    }

    [Fact]
    public void SolverCallBack_Should_Support_AfterModeling() {
        MosekSolverCallBack callBack = new();
        callBack.AfterModeling((SolverStatus? status, MosekTask mosekTask) => Results.OkInstance);
        callBack.Contains(CallBackPoint.AfterModeling).Should().BeTrue();
    }
}
