// 板长分配：配置、约束、目标与结果 / Sheet assignment: configuration, constraints, objectives and the result.
// 与固定 Kotlin 基线 `csp2d/domain/sheet_assignment/SheetAssignmentContext.kt` 对应。
// Mirrors the pinned Kotlin baseline's `SheetAssignmentContext.kt`.

using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.SheetAssignment;

/// <summary>
/// 板长分配配置 / Sheet assignment configuration.
/// </summary>
/// <param name="MaxConsumedLength">卷材允许的最大消耗长度，为空表示不限 / The maximum allowed consumed coil length; null means unlimited.</param>
/// <param name="OverLengthWeight">超长惩罚权重，为空表示超长作为硬约束 / The over-length penalty weight; null means over-length is a hard constraint.</param>
/// <remarks>
/// **`OverLengthWeight` 与基线一样只作为配置存在**：当前的三条约束与三个目标都不读它。
/// 保留它是因为**软化的口径是产品需求**（"超一点可以，但要罚"），而软化需要额外的松弛变量，
/// 那是另一件事，不该在这一层偷偷加。
///
/// **`OverLengthWeight` exists only as configuration, exactly like the baseline**: none of the three constraints or objectives reads it. It is kept because **a softened
/// measure is a product requirement** ("a little over is fine but must be penalised"), and softening needs extra slack variables — a separate change that must not be
/// slipped in at this layer.
/// </remarks>
public sealed record Csp2dSheetAssignmentConfig(
    Flt64? MaxConsumedLength = null,
    Flt64? OverLengthWeight = null);

/// <summary>
/// 消耗段高约束：`Σ_j 段高_j · y_j ≤ 最大消耗长度` / Consumed segment height constraint.
/// </summary>
/// <remarks>
/// **只对卷材注册**；定尺板材的"长度"就是板高，不参与约束——把板材也算进来会让"最大消耗长度"变成一个既管卷长又管板数的混合口径。
///
/// **Registered for coils only**; a fixed sheet's "length" is its height and does not take part — counting sheets too would turn the maximum consumed length into a
/// mixed measure governing both coil length and sheet count.
/// </remarks>
public sealed class Csp2dConsumedLengthConstraint {
    private readonly IReadOnlyList<CuttingPlan2<Flt64>> _plans;
    private readonly Csp2dPatternAssignment _assignment;
    private readonly Csp2dSheetAssignmentConfig _config;

    /// <summary>构造消耗段高约束 / Build the consumed length constraint.</summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="assignment">图案使用量变量 / The pattern usage variables.</param>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="name">约束名称 / The constraint name.</param>
    public Csp2dConsumedLengthConstraint(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        Csp2dPatternAssignment assignment,
        Csp2dSheetAssignmentConfig config,
        string name = "csp2d_consumed_length") {
        _plans = plans;
        _assignment = assignment;
        _config = config;
        Name = name;
    }

    /// <summary>约束名称 / The constraint name.</summary>
    public string Name { get; }

    /// <summary>注册消耗段高约束 / Register the consumed segment height constraint.</summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Invoke(LinearMetaModel<Flt64> model) {
        if (_config.MaxConsumedLength is not { } limit) {
            return Results.OkInstance;
        }
        List<LinearMonomial<Flt64>> monomials = new();
        for (int index = 0; index < _plans.Count; index++) {
            if (_plans[index].Material.Kind != Material2Kind.Coil) {
                continue;
            }
            monomials.Add(new LinearMonomial<Flt64>(
                _plans[index].Pattern.Region.Height.Value, _assignment.Usage[index]));
        }
        if (monomials.Count == 0) {
            return Results.OkInstance;
        }
        MutableLinearPolynomial<Flt64> polynomial = Csp2dProduceSupport.SafeCsp2dPolynomial(monomials);
        return model.AddConstraint(
            new LinearInequality<Flt64>(
                polynomial.ToLinearPolynomial(),
                Csp2dProduceSupport.ConstantPolynomial(limit),
                Comparison.LE),
            group: null,
            name: Name);
    }
}

/// <summary>
/// 消耗长度最小化目标：`min Σ_j 段高_j · y_j`（卷材条带排样的经典目标）/ Consumed length minimisation objective, the classic strip packing objective.
/// </summary>
public sealed class Csp2dConsumedLengthObjective {
    private readonly IReadOnlyList<CuttingPlan2<Flt64>> _plans;
    private readonly Csp2dPatternAssignment _assignment;

    /// <summary>构造消耗长度目标 / Build the consumed length objective.</summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="assignment">图案使用量变量 / The pattern usage variables.</param>
    /// <param name="name">目标名称 / The objective name.</param>
    public Csp2dConsumedLengthObjective(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        Csp2dPatternAssignment assignment,
        string name = "csp2d_consumed_length") {
        _plans = plans;
        _assignment = assignment;
        Name = name;
    }

    /// <summary>目标名称 / The objective name.</summary>
    public string Name { get; }

    /// <summary>注册消耗长度目标 / Register the consumed length objective.</summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    /// <remarks>
    /// **定尺方案在这里的系数是零**：它们不消耗卷长，混进来会让目标在两种模式之间"各算一半"。
    ///
    /// **Fixed-sheet plans carry a zero coefficient here**: they consume no coil length, and including them would make the objective count "half of each mode".
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Invoke(LinearMetaModel<Flt64> model) =>
        new Csp2dUsageObjective(_plans, _assignment, Coefficient, Name).Invoke(model);

    /// <summary>单个方案的段高系数 / The segment height coefficient of one plan.</summary>
    /// <param name="plan">方案 / The plan.</param>
    /// <returns>系数 / The coefficient.</returns>
    private static Flt64 Coefficient(CuttingPlan2<Flt64> plan) =>
        plan.Material.Kind == Material2Kind.Coil ? plan.Pattern.Region.Height.Value : Flt64.Zero;
}

/// <summary>
/// 板材张数最小化目标：`min Σ_j y_j`（定尺模式的经典目标）/ Sheet count minimisation objective, the classic fixed-sheet objective.
/// </summary>
/// <remarks>
/// 系数**恒为 1**，不分模式：一次使用就是一张（或一段）料，这正是"张数"的定义。
///
/// The coefficient is **always 1, regardless of mode**: one usage is one sheet (or one segment), which is exactly the definition of "count".
/// </remarks>
public sealed class Csp2dSheetCountObjective {
    private readonly IReadOnlyList<CuttingPlan2<Flt64>> _plans;
    private readonly Csp2dPatternAssignment _assignment;

    /// <summary>构造张数目标 / Build the sheet count objective.</summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="assignment">图案使用量变量 / The pattern usage variables.</param>
    /// <param name="name">目标名称 / The objective name.</param>
    public Csp2dSheetCountObjective(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        Csp2dPatternAssignment assignment,
        string name = "csp2d_sheet_count") {
        _plans = plans;
        _assignment = assignment;
        Name = name;
    }

    /// <summary>目标名称 / The objective name.</summary>
    public string Name { get; }

    /// <summary>注册张数目标 / Register the sheet count objective.</summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Invoke(LinearMetaModel<Flt64> model) =>
        new Csp2dUsageObjective(_plans, _assignment, _ => Flt64.One, Name).Invoke(model);
}

/// <summary>
/// 板长分配结果 / A sheet assignment result.
/// </summary>
/// <param name="ConsumedLength">消耗段高之和（卷材）/ The total consumed segment height for coils.</param>
/// <param name="SheetCount">板材张数（定尺）/ The sheet count.</param>
/// <param name="OverLength">超出最大消耗长度的部分 / The amount exceeding the maximum consumed length.</param>
/// <param name="OverLengthDetected">是否检测到超长 / Whether over-length was detected.</param>
public sealed record Csp2dSheetAssignmentResult(
    Flt64 ConsumedLength,
    int SheetCount,
    Flt64 OverLength,
    bool OverLengthDetected);

/// <summary>由使用次数计算板长分配结果的工厂 / The factory computing a sheet assignment result from usage counts.</summary>
public static class Csp2dSheetAssignmentFactory {
    /// <summary>
    /// 由使用次数计算板长分配结果 / Compute the sheet assignment result from usage counts.
    /// </summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    /// <param name="usages">与方案一一对应的使用次数 / The usage counts aligned with the plans.</param>
    /// <param name="config">配置 / The configuration.</param>
    /// <returns>板长分配结果或失败 / The sheet assignment result or a failure.</returns>
    /// <remarks>
    /// **纯函数**：列生成与恢复阶段都能直接复用，不需要访问模型。
    /// **非正使用次数直接跳过**——负数会让"消耗长度"倒退，而那不是任何合法解。
    ///
    /// **A pure function**: column generation and recovery can reuse it without touching the model. **Non-positive usages are skipped** — a negative one would *reduce*
    /// the consumed length, which no legal solution does.
    /// </remarks>
    public static Result<Csp2dSheetAssignmentResult, ErrorCode, Error<ErrorCode>> AssignmentOf(
        IReadOnlyList<CuttingPlan2<Flt64>> plans,
        IReadOnlyList<int> usages,
        Csp2dSheetAssignmentConfig config) {
        if (plans.Count != usages.Count) {
            return Results.Failed<Csp2dSheetAssignmentResult>(new Csp2dValidationError(
                "方案数与使用次数不一致 / The plan count and the usage count do not match: "
                + plans.Count + " vs " + usages.Count));
        }

        double consumed = 0.0;
        int sheetCount = 0;
        for (int index = 0; index < plans.Count; index++) {
            int usage = usages[index];
            if (usage <= 0) {
                continue;
            }
            sheetCount += usage;
            if (plans[index].Material.Kind == Material2Kind.Coil) {
                consumed += plans[index].Pattern.Region.Height.Value.ToDouble() * usage;
            }
        }

        double over = 0.0;
        if (config.MaxConsumedLength is { } limit && consumed > limit.ToDouble()) {
            over = consumed - limit.ToDouble();
        }
        return Results.Ok(new Csp2dSheetAssignmentResult(
            new Flt64(consumed),
            sheetCount,
            new Flt64(over),
            over > 0.0));
    }
}

/// <summary>物料的供给模式判断 / The supply-mode check for a material.</summary>
public static class SheetAssignmentModes {
    /// <summary>
    /// 判断物料是否属于卷材模式 / Whether the material belongs to the coil mode.
    /// </summary>
    /// <param name="material">物料 / The material.</param>
    /// <returns>是否为卷材 / Whether it is a coil.</returns>
    /// <remarks>
    /// **只认 `Kind`**，不看有没有给卷长：给定卷长的卷材仍然是卷材，只是问题**退化**成固定长度切段。
    /// 在这里顺手判"有没有长度"会让同一块料在两次调用里得到不同的模式。
    ///
    /// **Only `Kind` decides**; whether a coil length is given does not matter: a coil with a given length is still a coil, the problem merely **degenerates** into
    /// fixed-length segments. Deciding "does it have a length" here would give the same material a different mode across two calls.
    /// </remarks>
    public static bool IsCoilMode(Material2<Flt64> material) => material.Kind == Material2Kind.Coil;
}
