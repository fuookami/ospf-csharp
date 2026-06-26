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
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// Gurobi 列生成求解器集成测试。
/// Gurobi column generation solver integration tests.
///
/// 需要 Gurobi 10 许可证，无许可证时跳过。
/// Requires Gurobi 10 license; skipped when unavailable.
/// </summary>
[Trait("Category", "Gurobi")]
public class GurobiColumnGenerationSolverTest {
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
    public void Solver_Should_Implement_IColumnGenerationSolver() {
        // 验证类存在且实现了正确的接口 / Verify class exists and implements the correct interface
        var type = typeof(GurobiColumnGenerationSolver);
        type.Should().NotBeNull();
        type.IsSealed.Should().BeTrue();

        // 验证实现了 Framework.Solver.IColumnGenerationSolver / Verify implements Framework.Solver.IColumnGenerationSolver
        var cgInterface = typeof(Framework.Solver.IColumnGenerationSolver);
        cgInterface.IsAssignableFrom(type).Should().BeTrue(
            "GurobiColumnGenerationSolver should implement Framework.Solver.IColumnGenerationSolver");
    }

    [Fact]
    public void Solver_Should_Have_Name_Gurobi() {
        var solver = new GurobiColumnGenerationSolver();
        solver.Name.Should().Be("gurobi");
    }

    [Fact]
    public void Solver_Should_Accept_Custom_Config() {
        var config = new Fuookami.Ospf.Core.Solver.Config.GurobiSolverConfig {
            Threads = 4,
            TimeLimit = 60.0,
            MipGap = 1e-6
        };
        var solver = new GurobiColumnGenerationSolver(config: config);
        solver.Config.Should().BeSameAs(config);
    }

    [Fact]
    public async Task CuttingStock_RMP_And_Pricing_Cycle_Should_Converge_Async() {
        // 列生成求解切割库存问题的集成测试。
        // Integration test: column generation for a cutting stock problem.
        //
        // 问题描述 / Problem description:
        //   原料卷宽度 W = 10 / Raw roll width W = 10
        //   需求: 宽度 4 的件 3 个, 宽度 3 的件 2 个 / Demands: 3 pieces of width 4, 2 pieces of width 3
        //
        // 初始切割模式 / Initial cutting patterns:
        //   p1 = (2, 0) → 2 个宽度 4 的件 / 2 pieces of width 4
        //   p2 = (0, 3) → 3 个宽度 3 的件 / 3 pieces of width 3
        //   p3 = (1, 2) → 1 个宽度 4 + 2 个宽度 3 / 1 of width 4 + 2 of width 3
        //
        // RMP (LP 松弛) / RMP (LP relaxation):
        //   min x1 + x2 + x3
        //   s.t. 2*x1 + 0*x2 + 1*x3 >= 3  (需求约束 1 / demand constraint 1)
        //        0*x1 + 3*x2 + 2*x3 >= 2  (需求约束 2 / demand constraint 2)
        //        x1, x2, x3 >= 0
        //
        // 定价子问题 (背包) / Pricing subproblem (knapsack):
        //   max y1*a1 + y2*a2
        //   s.t. 4*a1 + 3*a2 <= 10
        //        a1, a2 >= 0, integer
        //   若最优值 > 1, 则添加新列 / If optimal > 1, add new column

        if (!IsGurobiAvailable()) {
            return;
        }

        // 需求向量 / Demand vector
        double[] demand = new double[] { 3.0, 2.0 };

        // 初始模式矩阵 (列 = 模式, 行 = 物品) / Initial pattern matrix (cols = patterns, rows = items)
        // p1 = (2, 0), p2 = (0, 3), p3 = (1, 2)
        var patterns = new List<double[]> {
            new double[] { 2.0, 0.0 },
            new double[] { 0.0, 3.0 },
            new double[] { 1.0, 2.0 }
        };

        const int maxIterations = 10;
        double bestObj = double.MaxValue;
        int iteration = 0;

        for (iteration = 0; iteration < maxIterations; iteration++) {
            // === 步骤 1: 构建 RMP (LP 松弛) / Step 1: Build RMP (LP relaxation) ===
            int numPatterns = patterns.Count;
            int numItems = demand.Length;

            // 变量: 每个模式一个连续变量 / Variables: one continuous variable per pattern
            var variables = new List<ModelViewVariable>(numPatterns);
            for (int j = 0; j < numPatterns; j++) {
                variables.Add(new ModelViewVariable(
                    j,
                    Flt64.Zero,
                    new Flt64(GRB.INFINITY),
                    Continuous.Instance,
                    $"x{j}"));
            }

            // 约束: 每个物品一个 >= 约束 / Constraints: one >= constraint per item
            var conEntriesList = new List<SparseVector>(numItems);
            var signs = new List<ConstraintRelation>(numItems);
            var rhsValues = new List<Flt64>(numItems);
            var conNames = new List<string>(numItems);
            var conSources = new List<ConstraintSource>(numItems);

            for (int i = 0; i < numItems; i++) {
                var entries = new List<SparseVectorEntry>();
                for (int j = 0; j < numPatterns; j++) {
                    double coeff = patterns[j][i];
                    if (coeff > 0) {
                        entries.Add(new SparseVectorEntry(j, new Flt64(coeff)));
                    }
                }
                conEntriesList.Add(new SparseVector(entries));
                signs.Add(ConstraintRelation.GreaterEqual);
                rhsValues.Add(new Flt64(demand[i]));
                conNames.Add($"demand_{i}");
                conSources.Add(ConstraintSource.Origin);
            }

            var constraints = new LinearConstraintBatch(
                new SparseMatrix(conEntriesList),
                signs,
                rhsValues,
                conNames,
                conSources);

            // 目标函数: min sum(x_j) / Objective: min sum(x_j)
            var objCells = new List<LinearObjectiveCell>(numPatterns);
            for (int j = 0; j < numPatterns; j++) {
                objCells.Add(new LinearObjectiveCell(j, Flt64.One));
            }
            var objective = new Objective<LinearObjectiveCell>(
                ObjectCategory.Minimum, objCells, Flt64.Zero);

            var rmpModel = new LinearTriadModel(variables, constraints, objective, $"cg_rmp_iter{iteration}");

            // === 步骤 2: 求解 RMP LP / Step 2: Solve RMP LP ===
            var dualValues = new List<double>(numItems);
            var rmpSolver = CreateSolverWithDualExtraction(dualValues);

            var rmpResult =
                await rmpSolver.InvokeAsync((ILinearTriadModelView)rmpModel);
            rmpResult.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(
                $"RMP LP should be feasible at iteration {iteration}");

            var rmpOutput = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)rmpResult).Value;
            double currentObj = rmpOutput.Obj.ToDouble();

            // 目标值应非增 / Objective should be non-increasing
            currentObj.Should().BeLessThanOrEqualTo(bestObj + 1e-6,
                $"RMP objective should not increase at iteration {iteration}");
            bestObj = currentObj;

            // 对偶值应已提取 / Dual values should have been extracted
            dualValues.Should().HaveCount(numItems,
                $"Should have {numItems} dual values at iteration {iteration}");

            // === 步骤 3: 定价子问题 (背包) / Step 3: Pricing subproblem (knapsack) ===
            double y1 = dualValues[0];
            double y2 = dualValues[1];

            var priceEnv = new GRBEnv();
            var priceModel = new GRBModel(priceEnv);
            priceModel.ModelName = $"cg_pricing_iter{iteration}";

            var a1 = priceModel.AddVar(0, GRB.INFINITY, 0, GRB.INTEGER, "a1");
            var a2 = priceModel.AddVar(0, GRB.INFINITY, 0, GRB.INTEGER, "a2");

            // 背包约束: 4*a1 + 3*a2 <= 10 / Knapsack constraint
            var priceExpr = new GRBLinExpr();
            priceExpr.AddTerm(4.0, a1);
            priceExpr.AddTerm(3.0, a2);
            priceModel.AddConstr(priceExpr, GRB.LESS_EQUAL, 10.0, "capacity");

            // 目标: max y1*a1 + y2*a2 / Objective
            var priceObj = new GRBLinExpr();
            priceObj.AddTerm(y1, a1);
            priceObj.AddTerm(y2, a2);
            priceModel.SetObjective(priceObj, GRB.MAXIMIZE);

            priceModel.Optimize();

            int priceStatus = priceModel.Get(GRB.IntAttr.Status);
            priceStatus.Should().Be(GRB.Status.OPTIMAL,
                $"Pricing subproblem should be optimal at iteration {iteration}");

            double pricingObj = priceModel.Get(GRB.DoubleAttr.ObjVal);
            int newA1 = (int)global::System.Math.Round(a1.Get(GRB.DoubleAttr.X));
            int newA2 = (int)global::System.Math.Round(a2.Get(GRB.DoubleAttr.X));

            priceModel.Dispose();
            priceEnv.Dispose();

            // === 步骤 4: 检查收敛 / Step 4: Check convergence ===
            if (pricingObj <= 1.0 + 1e-6) {
                // 已收敛 / Converged
                break;
            }

            // 添加新列 / Add new column
            patterns.Add(new double[] { newA1, newA2 });
        }

        // 验证收敛 / Verify convergence
        iteration.Should().BeLessThan(maxIterations,
            "Column generation should converge within max iterations");

        // 最终目标值应合理 / Final objective should be reasonable
        bestObj.Should().BeGreaterThan(0, "Final objective should be positive");
    }

    [Fact]
    public async Task CuttingStock_Dual_Extraction_Should_Return_Valid_Prices_Async() {
        // 验证 LP 对偶值提取的正确性。
        // Verify LP dual value extraction correctness.
        //
        // 简单 LP: min x1 + x2
        //   s.t. x1 + x2 >= 1  (对偶 y1)
        //        x1 >= 0, x2 >= 0
        // 最优解: obj = 1, 对偶: y1 = 1

        if (!IsGurobiAvailable()) {
            return;
        }

        var variables = new List<ModelViewVariable> {
            new ModelViewVariable(0, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x1"),
            new ModelViewVariable(1, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x2"),
        };

        var sparseRows = new List<SparseVector> {
            new SparseVector(new List<SparseVectorEntry> {
                new SparseVectorEntry(0, Flt64.One),
                new SparseVectorEntry(1, Flt64.One)
            })
        };
        var constraints = new LinearConstraintBatch(
            new SparseMatrix(sparseRows),
            new List<ConstraintRelation> { ConstraintRelation.GreaterEqual },
            new List<Flt64> { Flt64.One },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });

        var objective = new Objective<LinearObjectiveCell>(
            ObjectCategory.Minimum,
            new List<LinearObjectiveCell> {
                new LinearObjectiveCell(0, Flt64.One),
                new LinearObjectiveCell(1, Flt64.One)
            },
            Flt64.Zero);

        var model = new LinearTriadModel(variables, constraints, objective, "dual_test");

        var dualValues = new List<double>();
        var solver = CreateSolverWithDualExtraction(dualValues);

        var result =
            await solver.InvokeAsync((ILinearTriadModelView)model);
        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();

        var output = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;

        // 目标值应为 1 / Objective should be 1
        output.Obj.ToDouble().Should().BeApproximately(1.0, 1e-6, "objective should be 1.0");

        // 对偶值应已提取 / Dual should have been extracted
        dualValues.Should().HaveCount(1);
        dualValues[0].Should().BeApproximately(1.0, 1e-6, "dual price should be 1.0");
    }

    [Fact]
    public async Task CuttingStock_Column_Addition_Should_Improve_Objective_Async() {
        // 验证添加列后目标值改善。
        // Verify objective improves after adding a column.

        if (!IsGurobiAvailable()) {
            return;
        }

        var solver = new GurobiLinearSolver();

        // 第一次求解: 只有一个变量 / First solve: single variable
        var vars1 = new List<ModelViewVariable> {
            new ModelViewVariable(0, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x1"),
        };
        var rows1 = new List<SparseVector> {
            new SparseVector(new List<SparseVectorEntry> { new SparseVectorEntry(0, Flt64.One) })
        };
        var cons1 = new LinearConstraintBatch(
            new SparseMatrix(rows1),
            new List<ConstraintRelation> { ConstraintRelation.GreaterEqual },
            new List<Flt64> { new Flt64(2.0) },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });
        var obj1 = new Objective<LinearObjectiveCell>(
            ObjectCategory.Minimum,
            new List<LinearObjectiveCell> { new LinearObjectiveCell(0, Flt64.One) },
            Flt64.Zero);
        var model1 = new LinearTriadModel(vars1, cons1, obj1, "col_add_1");

        var result1 =
            await solver.InvokeAsync((ILinearTriadModelView)model1);
        result1.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();

        double obj1Val = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result1).Value.Obj.ToDouble();
        obj1Val.Should().BeApproximately(2.0, 1e-6, "initial objective should be 2.0");

        // 第二次求解: 添加一个列 (两个变量) / Second solve: add a column (two variables)
        var vars2 = new List<ModelViewVariable> {
            new ModelViewVariable(0, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x1"),
            new ModelViewVariable(1, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x2"),
        };
        var rows2 = new List<SparseVector> {
            new SparseVector(new List<SparseVectorEntry> {
                new SparseVectorEntry(0, Flt64.One),
                new SparseVectorEntry(1, new Flt64(2.0))
            })
        };
        var cons2 = new LinearConstraintBatch(
            new SparseMatrix(rows2),
            new List<ConstraintRelation> { ConstraintRelation.GreaterEqual },
            new List<Flt64> { new Flt64(2.0) },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });
        var obj2 = new Objective<LinearObjectiveCell>(
            ObjectCategory.Minimum,
            new List<LinearObjectiveCell> {
                new LinearObjectiveCell(0, Flt64.One),
                new LinearObjectiveCell(1, Flt64.One)
            },
            Flt64.Zero);
        var model2 = new LinearTriadModel(vars2, cons2, obj2, "col_add_2");

        var result2 =
            await solver.InvokeAsync((ILinearTriadModelView)model2);
        result2.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();

        double obj2Val = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result2).Value.Obj.ToDouble();

        // 添加列后目标值应改善或相等 / Objective should improve or stay equal after adding column
        obj2Val.Should().BeLessThanOrEqualTo(obj1Val + 1e-6,
            "objective should not increase after adding a column");
        obj2Val.Should().BeApproximately(1.0, 1e-6,
            "with new column, optimal should be 1.0 (x2=1, x1=0)");
    }

    /// <summary>
    /// 创建带有对偶提取功能的 Gurobi 线性求解器。
    /// Create a Gurobi linear solver with dual extraction capability.
    /// </summary>
    private static GurobiLinearSolver CreateSolverWithDualExtraction(List<double> dualValues) {
        var callBack = new GurobiLinearSolverCallBack()
            .AnalyzingSolution(async (_, _, _, grbConstrs) => {
                dualValues.Clear();
                for (int i = 0; i < grbConstrs.Count; i++) {
                    double pi = grbConstrs[i].Get(GRB.DoubleAttr.Pi);
                    dualValues.Add(pi);
                }
                return Results.OkInstance;
            });

        return new GurobiLinearSolver(callBack: callBack);
    }
}
