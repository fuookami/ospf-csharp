// 二维套裁的装配与恢复：候选生成、主问题装配、解恢复 / Assembly and recovery: candidate generation, master assembly and solution recovery.
// 与固定 Kotlin 的纯函数部分对应（求解器入口见 `Csp2dMilpSolver`/`Csp2dColumnGenerationSolver`）。
// The pure-function part of the pinned Kotlin: .

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp2d.Application.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.PatternGeneration;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp2d.Domain.SheetAssignment;
using Fuookami.Ospf.Framework.Csp2d.Domain.WastingMinimization;
using Fuookami.Ospf.Framework.Csp2d.Domain.Yield;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Application.Service;

/// <summary>
/// 二维套裁的装配与恢复 / Assembly and recovery for 2D cutting stock.
/// </summary>
/// <remarks>
/// 两条求解路径（MILP 与列生成）共用这一套：差别只在「候选方案怎么来」。**确定性部分抽在这里**，
/// 求解器入口只管"调一次 LP/MILP 再读解"。
///
/// Both solving paths (MILP and column generation) share this: they differ only in where the candidate plans come from. **The deterministic part is extracted here**, and
/// the solver entry points only call one LP/MILP and read the solution.
/// </remarks>
public static class Csp2dSolverSupport {
    /// <summary>
    /// 创建线性元模型 / Create a linear meta model.
    /// </summary>
    /// <param name="name">模型名称 / The model name.</param>
    /// <returns>元模型 / The meta model.</returns>
    public static LinearMetaModel<Flt64> NewModel(string name) =>
        new(name, ObjectCategory.Minimum);

    /// <summary>
    /// 由问题生成候选方案 / Build the candidate plans of a problem.
    /// </summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="products">参与生成的产品及件数 / The products and counts taking part.</param>
    /// <param name="iteration">迭代序号 / The iteration index.</param>
    /// <returns>候选方案 / The candidate plans.</returns>
    /// <remarks>
    /// **每个物料按各自的供给模式生成**：卷材按段高上界排样，定尺按整块板材排样。
    /// **一个都没生成时明确失败**——空方案集会让主问题成一个零变量模型，解出来的东西没有意义。
    ///
    /// **Each material is laid out by its own supply mode**: coils against the segment height bound, fixed sheets against the whole sheet. **An empty result is an
    /// explicit failure** — it would make the master a zero-variable model whose solution means nothing.
    /// </remarks>
    public static Result<IReadOnlyList<CuttingPlan2<Flt64>>, ErrorCode, Error<ErrorCode>> BuildPlans(
        Csp2dProblem problem,
        IReadOnlyList<(Product2<Flt64> Product, ulong Amount)> products,
        int iteration = 0) {
        var context = new PatternGenerationContext2();
        IReadOnlyDictionary<string, Product2<Flt64>> catalogue = problem.ProductsById();
        var plans = new List<CuttingPlan2<Flt64>>();
        for (int materialIndex = 0; materialIndex < problem.Materials.Count; materialIndex++) {
            Material2<Flt64> material = problem.Materials[materialIndex];
            var request = new PatternGenerationRequest2(
                material,
                products,
                problem.Kerf,
                problem.Process,
                problem.Costars,
                null,
                problem.Settings.MaxCandidatesPerIteration,
                iteration);
            Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> generated =
                context.Generate(request);
            if (generated is not Ok<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> ok) {
                return ReShape<IReadOnlyList<CuttingPlan2<Flt64>>, IReadOnlyList<PatternGenerationResult2>>(generated);
            }
            for (int patternIndex = 0; patternIndex < ok.Value.Count; patternIndex++) {
                Result<CuttingPlan2<Flt64>, ErrorCode, Error<ErrorCode>> built =
                    CuttingPlan2Factory.CuttingPlan2Of(
                        Csp2dIds.PlanId2Of("P" + materialIndex + "_" + iteration + "_" + patternIndex),
                        ok.Value[patternIndex].Pattern,
                        catalogue);
                if (built is not Ok<CuttingPlan2<Flt64>, ErrorCode, Error<ErrorCode>> builtOk) {
                    return ReShape<IReadOnlyList<CuttingPlan2<Flt64>>, CuttingPlan2<Flt64>>(built);
                }
                plans.Add(builtOk.Value);
            }
        }
        if (plans.Count == 0) {
            return Results.Failed<IReadOnlyList<CuttingPlan2<Flt64>>>(new Csp2dSolvingError(
                "没有生成任何候选方案 / No candidate plan was generated"));
        }
        return Results.Ok<IReadOnlyList<CuttingPlan2<Flt64>>>(plans);
    }

    /// <summary>
    /// 装配主问题 / Assemble the master problem.
    /// </summary>
    /// <param name="name">模型名称 / The model name.</param>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <returns>元模型与使用量变量 / The meta model and the usage variables.</returns>
    /// <remarks>
    /// **六条约束一个不少、三条目标按模式取舍**：主目标跟模式走，次目标只在权重非零且不与主目标重复时才加——
    /// 同一个目标注册两次会让求解器把系数叠成一倍，而调用方以为权重生效了。
    ///
    /// **All six constraints, with the objectives chosen by mode**: the primary follows the mode and the secondary terms are added only when their weight is non-zero
    /// and they do not repeat the primary — registering the same objective twice would double its coefficients while the caller believes the weight took effect.
    /// </remarks>
    public static Result<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), ErrorCode, Error<ErrorCode>>
        AssembleMaster(string name, Csp2dProblem problem, IReadOnlyList<CuttingPlan2<Flt64>> plans) {
        LinearMetaModel<Flt64> model = NewModel(name);
        var assignment = new Csp2dPatternAssignment(plans);
        Result<Success, ErrorCode, Error<ErrorCode>> registered =
            assignment.Register(model, problem.TotalPieceDemand());
        if (registered is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            model.Dispose();
            return ReShape<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), Success>(registered);
        }

        List<Func<LinearMetaModel<Flt64>, Result<Success, ErrorCode, Error<ErrorCode>>>> constraints = new() {
            _ => new Csp2dDemandConstraint(plans, assignment, problem.Demands).Invoke(_),
            _ => new Csp2dMaterialAvailabilityConstraint(plans, assignment, problem.Materials).Invoke(_),
            _ => new Csp2dMachineCapacityConstraint(
                plans, assignment, problem.Machines ?? new List<Machine2<Flt64>>()).Invoke(_),
            _ => new Csp2dOverProductionBoundConstraint(
                plans, assignment, problem.Demands, problem.Yield).Invoke(_),
            _ => new Csp2dConsumedLengthConstraint(plans, assignment, problem.SheetAssignment).Invoke(_),
        };
        foreach (Func<LinearMetaModel<Flt64>, Result<Success, ErrorCode, Error<ErrorCode>>> constraint in constraints) {
            Result<Success, ErrorCode, Error<ErrorCode>> result = constraint(model);
            if (result is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                model.Dispose();
                return ReShape<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), Success>(result);
            }
        }

        Csp2dObjectiveKind primary = problem.PrimaryObjective();
        Result<Success, ErrorCode, Error<ErrorCode>> primaryRegistered = primary switch {
            Csp2dObjectiveKind.ConsumedLength => new Csp2dConsumedLengthObjective(plans, assignment)
                .Invoke(model),
            Csp2dObjectiveKind.SheetCount => new Csp2dSheetCountObjective(plans, assignment).Invoke(model),
            _ => new Csp2dWasteMinimization(
                plans, assignment, problem.Settings.PrimaryWeightOrDefault).Invoke(model),
        };
        if (primaryRegistered is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            model.Dispose();
            return ReShape<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), Success>(
                primaryRegistered);
        }

        if (problem.Settings.WasteWeightOrDefault.ToDouble() != 0.0
            && primary != Csp2dObjectiveKind.Waste) {
            Result<Success, ErrorCode, Error<ErrorCode>> waste = new Csp2dWasteMinimization(
                plans, assignment, problem.Settings.WasteWeightOrDefault).Invoke(model);
            if (waste is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                model.Dispose();
                return ReShape<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), Success>(waste);
            }
        }

        if (problem.Settings.SheetCountWeightOrDefault.ToDouble() != 0.0
            && primary != Csp2dObjectiveKind.SheetCount) {
            Result<Success, ErrorCode, Error<ErrorCode>> sheetCount =
                new Csp2dSheetCountObjective(plans, assignment).Invoke(model);
            if (sheetCount is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                model.Dispose();
                return ReShape<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment), Success>(sheetCount);
            }
        }

        return Results.Ok<(LinearMetaModel<Flt64> Model, Csp2dPatternAssignment Assignment)>((model, assignment));
    }

    /// <summary>
    /// 由使用次数构造解 / Build the solution from the usage counts.
    /// </summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="usages">使用次数 / The usage counts.</param>
    /// <param name="iterations">迭代次数 / The iteration count.</param>
    /// <returns>解 / The solution.</returns>
    /// <remarks>
    /// **非正使用次数直接丢弃**：它们既不该进 `Usages`，也不该计面积——把零使用算进去会让利用率被料域面积稀释。
    /// **消耗面积为零时利用率取零**，而不是除零。
    ///
    /// **Non-positive usages are dropped**: they neither belong in `Usages` nor contribute area, and counting a zero usage would dilute utilization with unconsumed region
    /// area. **A zero consumed area yields zero utilization**, not a division by zero.
    /// </remarks>
    public static Result<Csp2dSolution, ErrorCode, Error<ErrorCode>> SolutionOf(
        Csp2dProblem problem,
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        IReadOnlyList<int> usages,
        int iterations) {
        Result<Csp2dSheetAssignmentResult, ErrorCode, Error<ErrorCode>> assigned =
            Csp2dSheetAssignmentFactory.AssignmentOf(plans, usages, problem.SheetAssignment);
        if (assigned is not Ok<Csp2dSheetAssignmentResult, ErrorCode, Error<ErrorCode>> assignedOk) {
            return ReShape<Csp2dSolution, Csp2dSheetAssignmentResult>(assigned);
        }

        var kept = new List<Csp2dPlanUsage>();
        var coverage = new Dictionary<string, int>();
        double usedArea = 0.0;
        double wasteArea = 0.0;
        double consumedArea = 0.0;
        for (int index = 0; index < plans.Count; index++) {
            int usage = usages[index];
            if (usage <= 0) {
                continue;
            }
            kept.Add(new Csp2dPlanUsage(plans[index], usage));
            if (plans[index].UsedArea() is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> used) {
                usedArea += used.Value.Value.ToDouble() * usage;
            }
            if (plans[index].WasteArea() is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> waste) {
                wasteArea += waste.Value.Value.ToDouble() * usage;
            }
            consumedArea += plans[index].Pattern.Region.Area.Value.ToDouble() * usage;
            foreach (CuttingPlanDemandContribution2<Flt64> contribution in plans[index].Contributions
                ?? new List<CuttingPlanDemandContribution2<Flt64>>()) {
                string productId = contribution.Product.Id.Value;
                coverage[productId] = (coverage.TryGetValue(productId, out int known) ? known : 0)
                    + (int)contribution.Amount * usage;
            }
        }

        double utilization = consumedArea > 0.0 ? usedArea / consumedArea : 0.0;
        var kpi = new Dictionary<string, string> {
            ["sheet_count"] = assignedOk.Value.SheetCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["consumed_length"] = assignedOk.Value.ConsumedLength.ToDouble()
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["used_area"] = usedArea.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["waste_area"] = wasteArea.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["utilization"] = utilization.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["over_length"] = assignedOk.Value.OverLength.ToDouble()
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["over_length_detected"] = assignedOk.Value.OverLengthDetected.ToString(),
            ["iteration_count"] = iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        return Results.Ok(new Csp2dSolution(
            kept,
            assignedOk.Value.ConsumedLength.ToDouble(),
            assignedOk.Value.SheetCount,
            usedArea,
            wasteArea,
            utilization,
            coverage,
            iterations,
            kpi));
    }

    /// <summary>对外暴露的结果再塑形，供求解器入口使用 / The re-shaping exposed for the solver entries.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    public static Result<S, ErrorCode, Error<ErrorCode>> ReShapePublic<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        ReShape<S, TSource>(result);

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<S, ErrorCode, Error<ErrorCode>> ReShape<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal => new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            Ok<TSource, ErrorCode, Error<ErrorCode>> ok => Results.Ok((S)(object)ok.Value),
            _ => throw new System.InvalidOperationException("unreachable result case"),
        };
}
