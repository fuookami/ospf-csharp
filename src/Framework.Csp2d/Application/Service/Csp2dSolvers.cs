// 二维套裁的求解入口：MILP 路径与列生成路径 / The 2D cutting stock entries: the MILP path and the column generation path.
// 与固定 Kotlin 的求解器部分对应。
// The solver part of the pinned Kotlin: .

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.Csp2d.Application.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.PatternGeneration;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
using IColumnGenerationSolver = Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Application.Service;

/// <summary>使用量与解的读取 / Reading usages and solutions from a model.</summary>
public static class Csp2dSolverReading {
    /// <summary>
    /// 读取变量的解值 / Read the solution value of a variable.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>解值，未注册或未求解时为零 / The value, zero when unregistered or unsolved.</returns>
    public static double TokenValue(LinearMetaModel<Flt64> model, Fuookami.Ospf.Core.Variable.IVariableItem variable) =>
        model.Tokens.Find(variable)?.DoubleResult ?? 0.0;

    /// <summary>
    /// 读取第 i 个方案的使用次数 / Read the usage count of the i-th plan.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="assignment">使用量变量 / The usage variables.</param>
    /// <param name="index">方案下标 / The plan index.</param>
    /// <returns>使用次数 / The usage count.</returns>
    public static int UsageAt(LinearMetaModel<Flt64> model, Csp2dPatternAssignment assignment, int index) {
        double value = TokenValue(model, assignment.Usage[index]);
        double floored = System.Math.Floor(double.IsFinite(value) ? value : 0.0);
        return floored > 0.0 ? (int)floored : 0;
    }

    /// <summary>
    /// 读取方案使用次数 / Read the plan usage counts.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="assignment">使用量变量 / The usage variables.</param>
    /// <returns>使用次数，向下取整且不低于零 / The usage counts, floored and floored at zero.</returns>
    /// <remarks>
    /// **向下取整不是"四舍五入的随手选择"**：LP 松弛会给出 2.9999 这类值，向上取整会把方案开得比可行域允许多一张，
    /// 而少开一张只影响覆盖、能立刻被覆盖判定发现。**负数一律归零**——负使用次数在物理上不存在。
    ///
    /// **Flooring is not a rounding whim**: an LP relaxation yields values like 2.9999, and rounding up would open one more plan than the feasible region allows, while
    /// rounding down only reduces coverage, which the coverage check catches immediately. **Negatives clamp to zero** — a negative usage does not exist physically.
    /// </remarks>
    public static IReadOnlyList<int> ReadUsages(LinearMetaModel<Flt64> model, Csp2dPatternAssignment assignment) {
        var usages = new List<int>();
        for (int index = 0; index < assignment.Plans.Count; index++) {
            usages.Add(UsageAt(model, assignment, index));
        }
        return usages;
    }
}

/// <summary>
/// MILP 求解入口：一次生成全部候选图案，解一次整数主问题 / The MILP entry: generate every candidate once and solve one integer master problem.
/// </summary>
/// <remarks>
/// **`MaxCandidatesPerIteration` 决定候选规模**：候选越多主问题越大，但一次求解；列生成路径正相反。
///
/// **`MaxCandidatesPerIteration` sets the candidate scale**: more candidates mean a bigger master solved once, while the column generation path trades the other way.
/// </remarks>
public sealed class Csp2dMilp {
    private readonly IColumnGenerationSolver _solver;

    /// <summary>构造 MILP 求解入口 / Build the MILP entry.</summary>
    /// <param name="solver">求解器 / The solver.</param>
    public Csp2dMilp(IColumnGenerationSolver solver) => _solver = solver;

    /// <summary>求解问题 / Solve the problem.</summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>解 / The solution.</returns>
    public async Task<Result<Csp2dSolution, ErrorCode, Error<ErrorCode>>> SolveAsync(
        Csp2dProblem problem,
        CancellationToken cancellationToken = default) {
        Result<Success, ErrorCode, Error<ErrorCode>> validated = problem.Validate();
        if (validated is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Csp2dSolverSupport.ReShapePublic<Csp2dSolution, Success>(validated);
        }

        Result<IReadOnlyList<CuttingPlan2<Flt64>>, ErrorCode, Error<ErrorCode>> built = Csp2dSolverSupport
            .BuildPlans(problem, RequiredProducts(problem));
        if (built is not Ok<IReadOnlyList<CuttingPlan2<Flt64>>, ErrorCode, Error<ErrorCode>> builtOk) {
            return Csp2dSolverSupport.ReShapePublic<Csp2dSolution, IReadOnlyList<CuttingPlan2<Flt64>>>(built);
        }

        Result<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), ErrorCode, Error<ErrorCode>>
            assembly = Csp2dSolverSupport.AssembleMaster("csp2d-milp", problem, builtOk.Value);
        if (assembly is not Ok<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), ErrorCode,
                Error<ErrorCode>> assemblyOk) {
            return Csp2dSolverSupport
                .ReShapePublic<Csp2dSolution, (LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment)>(assembly);
        }
        (LinearMetaModel<Flt64> model, Csp2dPatternAssignment assignment) = assemblyOk.Value;
        try {
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
                await _solver.SolveMILPAsync("csp2d-milp", model).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (solved is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solvedOk) {
                return Csp2dSolverSupport.ReShapePublic<Csp2dSolution, FeasibleSolverOutput<Flt64>>(solved);
            }
            model.SetSolution(solvedOk.Value.Solution.Values);
            return Csp2dSolverSupport.SolutionOf(
                problem, builtOk.Value, Csp2dSolverReading.ReadUsages(model, assignment), 1);
        } finally {
            model.Dispose();
        }
    }

    /// <summary>
    /// 参与生成的产品与件数 / The products and counts taking part in the generation.
    /// </summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <returns>产品与件数 / Products and counts.</returns>
    /// <remarks>
    /// **件数口径缺失时按 1 件生成**：面积/重量口径的需求没有件数，按 0 生成会让该产品根本不进候选，
    /// 而按 1 件至少能拿到"这个产品的单件图案"。
    ///
    /// **A missing amount counts as one piece**: the area and weight units carry no piece count, and using 0 would keep the product out of the candidates entirely, while
    /// one piece at least yields "the single-piece pattern of that product".
    /// </remarks>
    internal static IReadOnlyList<(Product2<Flt64> Product, ulong Amount)> RequiredProducts(Csp2dProblem problem) {
        var products = new List<(Product2<Flt64>, ulong)>();
        foreach (ProductDemand2<Flt64> demand in problem.Demands) {
            products.Add((demand.Product, demand.PiecesOrNull() ?? 1UL));
        }
        return products;
    }
}

/// <summary>
/// 列生成求解入口：按剩余需求逐轮追加图案，最后解一次整数主问题 / The column generation entry.
/// </summary>
/// <remarks>
/// **本阶段由剩余需求驱动，而不是由检验数驱动**——两者都属于列生成思想，但后者需要从 LP 对偶里取出需求约束的影子价格。
/// 对偶提取留作后续，**这里不声称已实现**。`LpResult.DualSolution` 已经在手边，接上是下一步而不是这里。
///
/// **Driven by the residual demand, not by reduced costs** at this stage — both belong to column generation, but the latter needs the demand shadow prices from the LP
/// duals. Dual extraction is left for a later stage and **is not claimed here**; `LpResult.DualSolution` is already within reach, so wiring it is a next step rather
/// than this change.
/// </remarks>
public sealed class Csp2dColumnGeneration {
    private readonly IColumnGenerationSolver _solver;

    /// <summary>构造列生成求解入口 / Build the column generation entry.</summary>
    /// <param name="solver">求解器 / The solver.</param>
    public Csp2dColumnGeneration(IColumnGenerationSolver solver) => _solver = solver;

    /// <summary>求解问题 / Solve the problem.</summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>解 / The solution.</returns>
    public async Task<Result<Csp2dSolution, ErrorCode, Error<ErrorCode>>> SolveAsync(
        Csp2dProblem problem,
        CancellationToken cancellationToken = default) {
        Result<Success, ErrorCode, Error<ErrorCode>> validated = problem.Validate();
        if (validated is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Csp2dSolverSupport.ReShapePublic<Csp2dSolution, Success>(validated);
        }

        var required = new Dictionary<string, int>();
        foreach (ProductDemand2<Flt64> demand in problem.Demands) {
            required[demand.Product.Id.Value] = (int)(demand.PiecesOrNull() ?? 0UL);
        }

        Result<IReadOnlyList<CuttingPlan2<Flt64>>, ErrorCode, Error<ErrorCode>> built =
            Csp2dSolverSupport.BuildPlans(problem, Csp2dMilp.RequiredProducts(problem));
        if (built is not Ok<IReadOnlyList<CuttingPlan2<Flt64>>, ErrorCode, Error<ErrorCode>> builtOk) {
            return Csp2dSolverSupport.ReShapePublic<Csp2dSolution, IReadOnlyList<CuttingPlan2<Flt64>>>(built);
        }
        IReadOnlyList<CuttingPlan2<Flt64>> plans = builtOk.Value;
        var known = new HashSet<string>();
        foreach (CuttingPlan2<Flt64> plan in plans) {
            known.Add(Csp2dPatternGenerationSupport.PatternKey(plan.Pattern));
        }
        int iteration = 1;

        while (iteration < problem.Settings.IterationLimit) {
            Result<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), ErrorCode, Error<ErrorCode>>
                assembly = Csp2dSolverSupport.AssembleMaster("csp2d-rmp-" + iteration, problem, plans);
            if (assembly is not Ok<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), ErrorCode,
                    Error<ErrorCode>> assemblyOk) {
                return Csp2dSolverSupport
                    .ReShapePublic<Csp2dSolution, (LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment)>(assembly);
            }
            (LinearMetaModel<Flt64> rmpModel, Csp2dPatternAssignment rmpAssignment) = assemblyOk.Value;
            try {
                Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> relaxed = await _solver
                    .SolveLPAsync("csp2d-rmp-" + iteration, rmpModel)
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                if (relaxed is not Ok<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> relaxedOk) {
                    return Csp2dSolverSupport.ReShapePublic<Csp2dSolution, IColumnGenerationSolver.LpResult>(relaxed);
                }
                rmpModel.SetSolution(relaxedOk.Value.Solution);
                IReadOnlyList<int> usages = Csp2dSolverReading.ReadUsages(rmpModel, rmpAssignment);

                Dictionary<string, int> produced = CoverageOf(plans, usages);
                var residualProducts = new List<(Product2<Flt64> Product, ulong Amount)>();
                bool covered = true;
                foreach (KeyValuePair<string, int> entry in required) {
                    int producedCount = produced.TryGetValue(entry.Key, out int known2) ? known2 : 0;
                    int residual = entry.Value - producedCount;
                    if (residual > 0) {
                        covered = false;
                        foreach (ProductDemand2<Flt64> demand in problem.Demands) {
                            if (demand.Product.Id.Value == entry.Key) {
                                residualProducts.Add((demand.Product, (ulong)residual));
                                break;
                            }
                        }
                    }
                }
                if (covered || residualProducts.Count == 0) {
                    break;
                }

                Result<IReadOnlyList<CuttingPlan2<Flt64>>, ErrorCode, Error<ErrorCode>> added = Csp2dSolverSupport
                    .BuildPlans(problem, residualProducts, iteration);
                if (added is not Ok<IReadOnlyList<CuttingPlan2<Flt64>>, ErrorCode, Error<ErrorCode>> addedOk) {
                    return Csp2dSolverSupport
                        .ReShapePublic<Csp2dSolution, IReadOnlyList<CuttingPlan2<Flt64>>>(added);
                }
                var merged = new List<CuttingPlan2<Flt64>>(plans);
                int appended = 0;
                foreach (CuttingPlan2<Flt64> plan in addedOk.Value) {
                    if (known.Add(Csp2dPatternGenerationSupport.PatternKey(plan.Pattern))) {
                        merged.Add(plan);
                        appended++;
                    }
                }
                if (appended == 0) {
                    // 没有新列，收敛。 / No new column, so the loop has converged.
                    break;
                }
                plans = merged;
                iteration++;
            } finally {
                rmpModel.Dispose();
            }
        }

        Result<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), ErrorCode, Error<ErrorCode>>
            finalAssembly = Csp2dSolverSupport.AssembleMaster("csp2d-final", problem, plans);
        if (finalAssembly is not Ok<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), ErrorCode,
                Error<ErrorCode>> finalOk) {
            return Csp2dSolverSupport
                .ReShapePublic<Csp2dSolution, (LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment)>(
                    finalAssembly);
        }
        (LinearMetaModel<Flt64> model, Csp2dPatternAssignment assignment) = finalOk.Value;
        try {
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
                await _solver.SolveMILPAsync("csp2d-final", model).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (solved is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solvedOk) {
                return Csp2dSolverSupport.ReShapePublic<Csp2dSolution, FeasibleSolverOutput<Flt64>>(solved);
            }
            model.SetSolution(solvedOk.Value.Solution.Values);
            return Csp2dSolverSupport.SolutionOf(
                problem, plans, Csp2dSolverReading.ReadUsages(model, assignment), iteration);
        } finally {
            model.Dispose();
        }
    }

    /// <summary>统计当前方案集合的产出件数 / Count the produced pieces of the current plan set.</summary>
    /// <param name="plans">方案 / The plans.</param>
    /// <param name="usages">使用次数 / The usage counts.</param>
    /// <returns>产品编号到件数 / Product identifier to piece count.</returns>
    private static Dictionary<string, int> CoverageOf(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        IReadOnlyList<int> usages) {
        var coverage = new Dictionary<string, int>();
        for (int index = 0; index < plans.Count; index++) {
            int usage = usages[index];
            if (usage <= 0) {
                continue;
            }
            foreach (CuttingPlanDemandContribution2<Flt64> contribution in plans[index].Contributions
                ?? new List<CuttingPlanDemandContribution2<Flt64>>()) {
                string productId = contribution.Product.Id.Value;
                coverage[productId] = (coverage.TryGetValue(productId, out int known) ? known : 0)
                    + (int)contribution.Amount * usage;
            }
        }
        return coverage;
    }
}
