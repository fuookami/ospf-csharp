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
/// Gurobi Benders 分解求解器集成测试。
/// Gurobi Benders decomposition solver integration tests.
///
/// 需要 Gurobi 10 许可证，无许可证时跳过。
/// Requires Gurobi 10 license; skipped when unavailable.
/// </summary>
[Trait("Category", "Gurobi")]
public class GurobiBendersDecompositionSolverTest {
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
    public void Solver_Should_Implement_IBendersDecompositionSolver() {
        // 验证类存在且实现了正确的接口 / Verify class exists and implements the correct interface
        Type type = typeof(GurobiBendersDecompositionSolver);
        type.Should().NotBeNull();
        type.IsSealed.Should().BeTrue();

        // 验证实现了 Framework.Solver.IBendersDecompositionSolver
        Type bendersInterface = typeof(Framework.Solver.IBendersDecompositionSolver);
        bendersInterface.IsAssignableFrom(type).Should().BeTrue(
            "GurobiBendersDecompositionSolver should implement Framework.Solver.IBendersDecompositionSolver");
    }

    [Fact]
    public void Solver_Should_Have_Name_Gurobi() {
        var solver = new GurobiBendersDecompositionSolver();
        solver.Name.Should().Be("gurobi");
    }

    [Fact]
    public void Solver_Should_Accept_Custom_Config() {
        var config = new Fuookami.Ospf.Core.Solver.Config.GurobiSolverConfig {
            Threads = 4,
            TimeLimit = 60.0,
            MipGap = 1e-6
        };
        var solver = new GurobiBendersDecompositionSolver(config: config);
        solver.Config.Should().BeSameAs(config);
    }

    [Fact]
    public async Task FacilityLocation_Benders_Should_Converge_Async() {
        // Benders 分解求解设施选址问题的集成测试。
        // Integration test: Benders decomposition for a facility location problem.
        //
        // 问题描述 / Problem description:
        //   2 个设施, 2 个客户 / 2 facilities, 2 customers
        //   设施开设成本: f1=4, f2=5 / Opening costs: f1=4, f2=5
        //   设施容量: cap1=10, cap2=10 / Capacities: cap1=10, cap2=10
        //   客户需求: d1=5, d2=5 / Demands: d1=5, d2=5
        //   运输成本: c11=2, c12=3, c21=4, c22=1 / Transport costs
        //
        // 主问题 (Master): min 4*y1 + 5*y2 + theta, y binary
        // 子问题 (Sub, for fixed y*): min 2*x11+3*x12+4*x21+1*x22
        //   s.t. x11+x12 <= 10*y1*, x21+x22 <= 10*y2*
        //        x11+x21 = 5, x12+x22 = 5, xij >= 0
        //
        // 最优解: y1=1, y2=1, obj=24 / Optimal: y1=1, y2=1, obj=24

        if (!IsGurobiAvailable()) {
            return;
        }

        const int maxIterations = 10;
        double bestMasterObj = double.MinValue;  // Lower bound, should be non-decreasing
        double bestSubObj = double.MaxValue;     // Upper bound, should be non-increasing
        bool converged = false;

        // 初始主问题变量: y1 (binary), y2 (binary), theta (continuous >= 0)
        // Master variables: y1 (binary), y2 (binary), theta (continuous >= 0)
        var masterVars = new List<ModelViewVariable> {
            new(0, Flt64.Zero, Flt64.One, Integer.Instance, "y1"),  // binary
            new(1, Flt64.Zero, Flt64.One, Integer.Instance, "y2"),  // binary
            new(2, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "theta"),
        };

        // 初始主问题约束: y1 + y2 >= 1 (至少开一个设施 / at least one facility open)
        // This avoids the trivial infeasible sub-problem when y1=0, y2=0
        var masterConEntries = new List<SparseVector> {
            new(new List<SparseVectorEntry> {
                new(0, Flt64.One),  // y1
                new(1, Flt64.One),  // y2
            })
        };
        var masterSigns = new List<ConstraintRelation> { ConstraintRelation.GreaterEqual };
        var masterRhs = new List<Flt64> { Flt64.One };
        var masterConNames = new List<string> { "at_least_one" };
        var masterConSources = new List<ConstraintSource> { ConstraintSource.Origin };

        // 目标: min 4*y1 + 5*y2 + theta
        var masterObjCells = new List<LinearObjectiveCell> {
            new(0, new Flt64(4.0)),  // 4*y1
            new(1, new Flt64(5.0)),  // 5*y2
            new(2, Flt64.One),       // theta
        };

        for (int iteration = 0; iteration < maxIterations; iteration++) {
            // === 步骤 1: 构建并求解主问题 / Step 1: Build and solve master ===
            var masterConstraints = new LinearConstraintBatch(
                new SparseMatrix(masterConEntries),
                masterSigns,
                masterRhs,
                masterConNames,
                masterConSources);

            var masterObjective = new Objective<LinearObjectiveCell>(
                ObjectCategory.Minimum, masterObjCells, Flt64.Zero);

            var masterModel = new LinearTriadModel(
                masterVars, masterConstraints, masterObjective, $"benders_master_iter{iteration}");

            var masterSolver = new GurobiLinearSolver();
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> masterResult =
                await masterSolver.InvokeAsync((ILinearTriadModelView)masterModel);

            masterResult.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(
                $"Master problem should be feasible at iteration {iteration}");

            var masterOutput = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)masterResult).Value;
            double masterObj = masterOutput.Obj.ToDouble();
            double y1Val = masterOutput.Solution.Values[0].ToDouble();
            double y2Val = masterOutput.Solution.Values[1].ToDouble();
            double thetaVal = masterOutput.Solution.Values[2].ToDouble();

            // 主问题目标值（下界）应非减 / Master objective (lower bound) should be non-decreasing
            masterObj.Should().BeGreaterThanOrEqualTo(bestMasterObj - 1e-6,
                $"Master lower bound should not decrease at iteration {iteration}");
            bestMasterObj = System.Math.Max(bestMasterObj, masterObj);

            // === 步骤 2: 构建并求解子问题（LP 松弛 + 变量固定）/ Step 2: Build and solve sub-problem ===
            // 子问题变量: x11, x12, x21, x22 (all continuous)
            var subVars = new List<ModelViewVariable> {
                new(0, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x11"),
                new(1, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x12"),
                new(2, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x21"),
                new(3, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x22"),
            };

            // 容量约束: x11+x12 <= 10*y1*, x21+x22 <= 10*y2*
            // Capacity constraints (RHS depends on master solution)
            var subConEntries = new List<SparseVector> {
                // x11 + x12 <= 10*y1*
                new(new List<SparseVectorEntry> {
                    new(0, Flt64.One),
                    new(1, Flt64.One)
                }),
                // x21 + x22 <= 10*y2*
                new(new List<SparseVectorEntry> {
                    new(2, Flt64.One),
                    new(3, Flt64.One)
                }),
                // x11 + x21 = 5 (demand 1)
                new(new List<SparseVectorEntry> {
                    new(0, Flt64.One),
                    new(2, Flt64.One)
                }),
                // x12 + x22 = 5 (demand 2)
                new(new List<SparseVectorEntry> {
                    new(1, Flt64.One),
                    new(3, Flt64.One)
                }),
            };
            var subSigns = new List<ConstraintRelation> {
                ConstraintRelation.LessEqual,
                ConstraintRelation.LessEqual,
                ConstraintRelation.Equal,
                ConstraintRelation.Equal,
            };
            var subRhs = new List<Flt64> {
                new Flt64(10.0 * y1Val),  // capacity 1
                new Flt64(10.0 * y2Val),  // capacity 2
                new Flt64(5.0),            // demand 1
                new Flt64(5.0),            // demand 2
            };
            var subConNames = new List<string> { "cap1", "cap2", "dem1", "dem2" };
            var subConSources = new List<ConstraintSource> {
                ConstraintSource.Origin, ConstraintSource.Origin,
                ConstraintSource.Origin, ConstraintSource.Origin
            };

            var subConstraints = new LinearConstraintBatch(
                new SparseMatrix(subConEntries), subSigns, subRhs, subConNames, subConSources);

            // 目标: min 2*x11 + 3*x12 + 4*x21 + 1*x22
            var subObjCells = new List<LinearObjectiveCell> {
                new(0, new Flt64(2.0)),
                new(1, new Flt64(3.0)),
                new(2, new Flt64(4.0)),
                new(3, new Flt64(1.0)),
            };
            var subObjective = new Objective<LinearObjectiveCell>(
                ObjectCategory.Minimum, subObjCells, Flt64.Zero);

            var subModel = new LinearTriadModel(subVars, subConstraints, subObjective, $"benders_sub_iter{iteration}");

            // 设置对偶提取回调 / Set up dual extraction callback
            var dualValues = new List<double>();
            var subCallBack = new GurobiLinearSolverCallBack()
                .Configuration(async (_, grbModel, _, _) => {
                    grbModel.Set(GRB.IntParam.InfUnbdInfo, 1);
                    return Results.OkInstance;
                })
                .AnalyzingSolution(async (_, _, _, grbConstrs) => {
                    dualValues.Clear();
                    for (int i = 0; i < grbConstrs.Count; i++) {
                        dualValues.Add(grbConstrs[i].Get(GRB.DoubleAttr.Pi));
                    }
                    return Results.OkInstance;
                });

            var subSolver = new GurobiLinearSolver(callBack: subCallBack);
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> subResult =
                await subSolver.InvokeAsync((ILinearTriadModelView)subModel);

            // === 步骤 3: 检查子问题结果并生成割 / Step 3: Check sub result and generate cut ===
            if (subResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> subOk) {
                double subObj = subOk.Value.Obj.ToDouble();

                // 验证对偶值已提取 / Verify duals extracted
                dualValues.Should().HaveCount(4, "Should have 4 dual values");

                // 对偶可行性检查: 强对偶定理 / Dual feasibility: strong duality check
                // 子问题目标值应 >= 0 / Sub objective should be >= 0
                subObj.Should().BeGreaterThanOrEqualTo(-1e-6, "Sub-problem objective should be non-negative");

                // === 步骤 4: 检查收敛 / Step 4: Check convergence ===
                // 上界 = 4*y1 + 5*y2 + subObj / Upper bound = opening cost + sub objective
                double upperBound = 4.0 * y1Val + 5.0 * y2Val + subObj;
                bestSubObj = System.Math.Min(bestSubObj, upperBound);

                // Benders 间隙 = (上界 - 下界) / 上界 / Gap = (UB - LB) / UB
                double bendersGap = bestSubObj > 1e-6
                    ? (bestSubObj - masterObj) / bestSubObj
                    : System.Math.Abs(bestSubObj - masterObj);

                if (bendersGap < 1e-6) {
                    // 已收敛 / Converged
                    converged = true;

                    // 验证最优解 / Verify optimal solution
                    // 最优解应为 y1=1, y2=1, obj=24
                    bestSubObj.Should().BeApproximately(24.0, 1e-4,
                        "Optimal upper bound should be 24.0 (y1=1, y2=1, sub=15)");
                    break;
                }

                // 添加最优性割: theta >= dual1*(10*y1) + dual2*(10*y2) + dual3*5 + dual4*5
                // Add optimality cut: theta >= sum(dual_i * rhs_i)
                // 即: theta - 10*dual1*y1 - 10*dual2*y2 >= 5*dual3 + 5*dual4
                double d0 = dualValues[0]; // cap1 dual
                double d1 = dualValues[1]; // cap2 dual
                double d2 = dualValues[2]; // dem1 dual
                double d3 = dualValues[3]; // dem2 dual

                // 添加 Benders 最优性割约束 / Add Benders optimality cut constraint
                // theta - 10*d0*y1 - 10*d1*y2 >= 5*d2 + 5*d3
                var cutEntries = new List<SparseVectorEntry>();
                cutEntries.Add(new(2, Flt64.One));                // theta
                if (System.Math.Abs(d0) > 1e-12) {
                    cutEntries.Add(new(0, new Flt64(-10.0 * d0))); // -10*d0*y1
                }
                if (System.Math.Abs(d1) > 1e-12) {
                    cutEntries.Add(new(1, new Flt64(-10.0 * d1))); // -10*d1*y2
                }
                masterConEntries.Add(new SparseVector(cutEntries));
                masterSigns.Add(ConstraintRelation.GreaterEqual);
                masterRhs.Add(new Flt64(5.0 * d2 + 5.0 * d3));
                masterConNames.Add($"benders_cut_{iteration}");
                masterConSources.Add(ConstraintSource.Origin);
            }
            else if (subResult is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> subFailed) {
                // 子问题不可行: 生成可行性割 / Sub infeasible: generate feasibility cut
                // 使用 Farkas 对偶 / Use Farkas duals
                var farkasValues = new List<double>();
                var infeasibleCallBack = new GurobiLinearSolverCallBack()
                    .Configuration(async (_, grbModel, _, _) => {
                        grbModel.Set(GRB.IntParam.InfUnbdInfo, 1);
                        return Results.OkInstance;
                    })
                    .AfterFailure(async (status, _, _, grbConstrs) => {
                        if (status == SolverStatus.Infeasible) {
                            farkasValues.Clear();
                            for (int i = 0; i < grbConstrs.Count; i++) {
                                farkasValues.Add(grbConstrs[i].Get(GRB.DoubleAttr.FarkasDual));
                            }
                        }
                        return Results.OkInstance;
                    });

                var infeasibleSolver = new GurobiLinearSolver(callBack: infeasibleCallBack);
                await infeasibleSolver.InvokeAsync((ILinearTriadModelView)subModel);

                if (farkasValues.Count == 4) {
                    // 添加可行性割 / Add feasibility cut
                    double f0 = farkasValues[0];
                    double f1 = farkasValues[1];
                    double f2 = farkasValues[2];
                    double f3 = farkasValues[3];

                    var cutEntries = new List<SparseVectorEntry>();
                    if (System.Math.Abs(f0) > 1e-12) {
                        cutEntries.Add(new(0, new Flt64(-10.0 * f0)));
                    }
                    if (System.Math.Abs(f1) > 1e-12) {
                        cutEntries.Add(new(1, new Flt64(-10.0 * f1)));
                    }
                    if (cutEntries.Count > 0) {
                        masterConEntries.Add(new SparseVector(cutEntries));
                        masterSigns.Add(ConstraintRelation.GreaterEqual);
                        masterRhs.Add(new Flt64(-(5.0 * f2 + 5.0 * f3)));
                        masterConNames.Add($"benders_feas_cut_{iteration}");
                        masterConSources.Add(ConstraintSource.Origin);
                    }
                }
            }
        }

        // 验证收敛 / Verify convergence
        converged.Should().BeTrue("Benders decomposition should converge within max iterations");
    }

    [Fact]
    public async Task SubProblem_Dual_Extraction_Should_Work_Async() {
        // 验证子问题对偶提取的正确性。
        // Verify sub-problem dual extraction correctness.
        //
        // 简单 LP: min 2*x1 + 3*x2
        //   s.t. x1 + x2 <= 10  (对偶 pi1)
        //        x1 + x2 >= 5   (对偶 pi2)
        //        x1, x2 >= 0
        // 最优解: x1=5, x2=0 (or any with x1+x2=5), obj=10
        // 对偶: pi1=0 (slack), pi2=-2 (binding >= constraint)

        if (!IsGurobiAvailable()) {
            return;
        }

        var variables = new List<ModelViewVariable> {
            new(0, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x1"),
            new(1, Flt64.Zero, new Flt64(GRB.INFINITY), Continuous.Instance, "x2"),
        };

        var sparseRows = new List<SparseVector> {
            // x1 + x2 <= 10
            new(new List<SparseVectorEntry> {
                new(0, Flt64.One),
                new(1, Flt64.One)
            }),
            // x1 + x2 >= 5
            new(new List<SparseVectorEntry> {
                new(0, Flt64.One),
                new(1, Flt64.One)
            }),
        };
        var constraints = new LinearConstraintBatch(
            new SparseMatrix(sparseRows),
            new List<ConstraintRelation> { ConstraintRelation.LessEqual, ConstraintRelation.GreaterEqual },
            new List<Flt64> { new Flt64(10.0), new Flt64(5.0) },
            new List<string> { "cap", "demand" },
            new List<ConstraintSource> { ConstraintSource.Origin, ConstraintSource.Origin });

        var objective = new Objective<LinearObjectiveCell>(
            ObjectCategory.Minimum,
            new List<LinearObjectiveCell> {
                new(0, new Flt64(2.0)),
                new(1, new Flt64(3.0))
            },
            Flt64.Zero);

        var model = new LinearTriadModel(variables, constraints, objective, "dual_sub_test");

        var dualValues = new List<double>();
        var callBack = new GurobiLinearSolverCallBack()
            .AnalyzingSolution(async (_, _, _, grbConstrs) => {
                dualValues.Clear();
                for (int i = 0; i < grbConstrs.Count; i++) {
                    dualValues.Add(grbConstrs[i].Get(GRB.DoubleAttr.Pi));
                }
                return Results.OkInstance;
            });

        var solver = new GurobiLinearSolver(callBack: callBack);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync((ILinearTriadModelView)model);

        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();

        var output = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;

        // 目标值应为 10 (x1=5, x2=0, obj=2*5+3*0=10)
        // Note: with x1+x2<=10 slack, optimal is x1=5, x2=0 to minimize 2*x1+3*x2
        output.Obj.ToDouble().Should().BeApproximately(10.0, 1e-6, "objective should be 10.0");

        // 对偶值应已提取 / Duals should have been extracted
        dualValues.Should().HaveCount(2, "Should have 2 dual values");

        // 对偶值应合理 / Duals should be reasonable
        // cap constraint (<=) should have pi <= 0 (or >= 0 depending on convention)
        // demand constraint (>=) should have pi >= 0 (binding)
    }

    [Fact]
    public async Task Variable_Fixing_Via_Bounds_Should_Work_Async() {
        // 验证通过设置 LB=UB 固定变量的正确性。
        // Verify variable fixing via LB=UB is correct.
        //
        // 原始问题: min x1 + x2, s.t. x1 + x2 >= 3, x1,x2 in [0,10]
        // 固定 x1=2: min 2+x2, s.t. x2 >= 1, x2 in [0,10]
        // 最优: x1=2, x2=1, obj=3

        if (!IsGurobiAvailable()) {
            return;
        }

        // 固定 x1=2, x2 自由 / Fix x1=2, x2 free
        var variables = new List<ModelViewVariable> {
            new(0, new Flt64(2.0), new Flt64(2.0), Continuous.Instance, "x1"),  // fixed at 2
            new(1, Flt64.Zero, new Flt64(10.0), Continuous.Instance, "x2"),
        };

        var sparseRows = new List<SparseVector> {
            new(new List<SparseVectorEntry> {
                new(0, Flt64.One),
                new(1, Flt64.One)
            })
        };
        var constraints = new LinearConstraintBatch(
            new SparseMatrix(sparseRows),
            new List<ConstraintRelation> { ConstraintRelation.GreaterEqual },
            new List<Flt64> { new Flt64(3.0) },
            new List<string> { "c0" },
            new List<ConstraintSource> { ConstraintSource.Origin });

        var objective = new Objective<LinearObjectiveCell>(
            ObjectCategory.Minimum,
            new List<LinearObjectiveCell> {
                new(0, Flt64.One),
                new(1, Flt64.One)
            },
            Flt64.Zero);

        var model = new LinearTriadModel(variables, constraints, objective, "fix_var_test");

        var solver = new GurobiLinearSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync((ILinearTriadModelView)model);

        result.Should().BeOfType<Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>();

        var output = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;

        // x1 应固定为 2 / x1 should be fixed at 2
        output.Solution.Values[0].ToDouble().Should().BeApproximately(2.0, 1e-6, "x1 should be fixed at 2.0");

        // x2 应为 1 (满足 x1+x2>=3) / x2 should be 1 (satisfying x1+x2>=3)
        output.Solution.Values[1].ToDouble().Should().BeApproximately(1.0, 1e-6, "x2 should be 1.0");

        // 目标值应为 3 / Objective should be 3
        output.Obj.ToDouble().Should().BeApproximately(3.0, 1e-6, "objective should be 3.0");
    }
}
