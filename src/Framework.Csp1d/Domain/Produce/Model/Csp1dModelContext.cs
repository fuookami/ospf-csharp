#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
// ===== Core Context Interfaces =====

/// <summary>
/// CSP1D 模型上下文接口 / CSP1D model context interface.
///
/// 定义模型注册的基本接口。domain context 实现此接口将建模逻辑注入 MetaModel，
/// 使变量、约束和目标的注册从 solver 硬编码中解耦。
///
/// Define the basic interface for model registration. Domain contexts implement this interface
/// to inject modeling logic into MetaModel, decoupling variable/constraint/objective registration
/// from solver hard-coding.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dModelContext<V>
    where V : struct {
    /// <summary>
    /// 注册到元模型 / Register to meta model.
    ///
    /// 将变量、中间值、约束和目标注册到指定的元模型中。
    /// Register variables, intermediate values, constraints, and objectives to the specified meta model.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    Try Register(LinearMetaModel<Flt64> model);

    /// <summary>
    /// 从元模型提取求解结果 / Extract solution from meta model.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>主问题产出 / Master problem output.</returns>
    Result<Produce<V>, ErrorCode, Error<ErrorCode>> ExtractSolution(IAbstractLinearMetaModel<Flt64> model);
}

/// <summary>
/// CSP1D 列生成上下文接口 / CSP1D column generation context interface.
///
/// 扩展 ModelContext，支持列生成所需的迭代操作：添加列、提取影子价格、移除列。
/// Extends ModelContext with iterative operations for column generation:
/// addColumns, extractShadowPrice, removeColumns.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dIterativeContext<V> : ICsp1dModelContext<V>
    where V : struct {
    /// <summary>
    /// 添加列（新切割方案） / Add columns (new cutting plans).
    ///
    /// 在列生成迭代过程中，将新生成的切割方案注册为新的列变量，
    /// 并更新相关中间值和约束表达式。
    ///
    /// During column generation iteration, register newly generated cutting plans
    /// as new column variables, and update related intermediate values and constraint expressions.
    /// </summary>
    /// <param name="iteration">当前迭代编号 / Current iteration number.</param>
    /// <param name="newPlans">新切割方案列表 / New cutting plan list.</param>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>去重后的新方案列表 / Deduplicated new plan list.</returns>
    Result<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> AddColumns(
        UInt64 iteration,
        IReadOnlyList<CuttingPlan<V>> newPlans,
        IAbstractLinearMetaModel<Flt64> model);

    /// <summary>
    /// 提取影子价格 / Extract shadow prices.
    ///
    /// 从 LP 松弛的对偶解中提取各约束的影子价格，
    /// 用于定价子问题计算 reduced cost。
    /// 通过 CGPipeline refresh / extractor 机制自动提取。
    ///
    /// Extract shadow prices of each constraint from the LP relaxation dual solution,
    /// for use in the pricing sub-problem to compute reduced cost.
    /// Extraction is automatic via CGPipeline refresh / extractor mechanism.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <param name="shadowPrices">对偶解 / Dual solution.</param>
    /// <returns>操作结果 / Operation result.</returns>
    Try ExtractShadowPrice(
        IAbstractLinearMetaModel<Flt64> model,
        MetaDualSolution shadowPrices);
}

/// <summary>
/// CSP1D 扩展模型上下文接口 / CSP1D extra model context interface.
///
/// 组合基础上下文，允许下游项目在不修改核心代码的情况下注入额外建模逻辑。
/// 下游可通过此接口注册类似 same unit length、same width、宽差、材质兼容等业务约束。
///
/// Composes base context, allowing downstream projects to inject additional modeling logic
/// without modifying core code. Downstream can register business constraints such as
/// same unit length, same width, width difference, material compatibility via this interface.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dExtraModelContext<V> : ICsp1dModelContext<V>
    where V : struct {
    /// <summary>
    /// 基础上下文 / Base context.
    /// </summary>
    ICsp1dModelContext<V> BaseContext { get; }
}

/// <summary>
/// CSP1D 扩展列生成上下文接口 / CSP1D extra iterative context interface.
///
/// 扩展 IterativeContext，允许下游项目在列生成迭代中注入额外逻辑。
/// Extends IterativeContext, allowing downstream projects to inject additional logic
/// during column generation iteration.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dExtraIterativeContext<V> : ICsp1dIterativeContext<V>, ICsp1dExtraModelContext<V>
    where V : struct {
    /// <summary>
    /// 基础迭代上下文 / Base iterative context.
    /// </summary>
    new ICsp1dIterativeContext<V> BaseContext { get; }
}

// ===== Modeling Mode Enums =====

/// <summary>
/// CSP1D 建模模式 / CSP1D modeling mode.
///
/// 区分普通 MILP 和列生成 LP 松弛两种建模路径。
/// LP 模式不加 yield/length slack 变量，保持 demand >= 约束以提取 shadow price。
///
/// Distinguish between normal MILP and column generation LP relaxation modeling paths.
/// LP mode does not add yield/length slack variables, keeping demand >= constraints
/// for shadow price extraction.
/// </summary>
public enum Csp1dModelingMode {
    /// <summary>普通 MILP 模式 / Normal MILP mode.</summary>
    MILP,
    /// <summary>列生成 LP 松弛模式 / Column generation LP relaxation mode.</summary>
    LP
}

/// <summary>
/// CSP1D 扩展适用模式 / CSP1D extension applicable mode.
///
/// 扩展管线可注册到哪些求解阶段。默认 ALL 表示所有阶段均生效。
/// Extension pipelines can be registered to specific solve stages.
/// Default ALL means the extension applies to all stages.
/// </summary>
public enum Csp1dExtensionMode {
    /// <summary>普通 MILP / Normal MILP.</summary>
    MILP,
    /// <summary>列生成 LP 松弛 / Column generation LP relaxation.</summary>
    LP,
    /// <summary>列生成最终 MILP / Column generation final MILP.</summary>
    FINAL_MILP,
    /// <summary>所有模式 / All modes.</summary>
    ALL
}

/// <summary>
/// CSP1D 扩展适用模式扩展方法 / CSP1D extension mode extension methods.
/// </summary>
public static class Csp1dExtensionModeExtensions {
    /// <summary>
    /// 判断是否匹配指定建模模式 / Check if this extension mode matches the given modeling mode.
    /// </summary>
    /// <param name="mode">扩展模式 / Extension mode.</param>
    /// <param name="modelingMode">建模模式 / Modeling mode.</param>
    /// <param name="isFinalMilp">是否为列生成最终 MILP / Whether this is a column generation final MILP.</param>
    /// <returns>是否匹配 / Whether it matches.</returns>
    public static bool Matches(this Csp1dExtensionMode mode, Csp1dModelingMode modelingMode, bool isFinalMilp = false) {
        return mode switch {
            Csp1dExtensionMode.MILP => modelingMode == Csp1dModelingMode.MILP && !isFinalMilp,
            Csp1dExtensionMode.LP => modelingMode == Csp1dModelingMode.LP,
            Csp1dExtensionMode.FINAL_MILP => isFinalMilp,
            Csp1dExtensionMode.ALL => true,
            _ => false
        };
    }
}

// ===== Modeling Extension =====

/// <summary>
/// CSP1D 建模扩展 / CSP1D modeling extension.
///
/// 承载可在求解各阶段注入的额外管线。下游通过此类型注册 same unit length、
/// same width、宽差、材质兼容等业务约束，而不修改 framework 核心代码。
///
/// Carries additional pipelines that can be injected at various solve stages.
/// Downstream uses this type to register business constraints such as
/// same unit length, same width, width difference, material compatibility,
/// without modifying framework core code.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Csp1dModelingExtension<V>
    where V : struct {
    /// <summary>
    /// 扩展管线 / Extension pipeline.
    /// 当 ContextAwarePipeline 存在且 ResolvePipeline 收到 context 时被忽略。
    /// Ignored when ContextAwarePipeline is present and ResolvePipeline receives a context.
    /// </summary>
    public IPipeline<LinearMetaModel<Flt64>>? Pipeline { get; }

    /// <summary>
    /// 扩展适用模式 / Extension applicable mode.
    /// </summary>
    public Csp1dExtensionMode Mode { get; }

    /// <summary>
    /// 上下文感知扩展管线工厂 / Context-aware pipeline factory.
    ///
    /// 接收 Csp1dModelingContext 返回 Pipeline；优先于 Pipeline 使用。
    /// Receives Csp1dModelingContext and returns Pipeline; takes priority over Pipeline when present.
    /// </summary>
    public Func<ICsp1dModelingContext<V>, IPipeline<LinearMetaModel<Flt64>>>? ContextAwarePipeline { get; }

    /// <summary>
    /// 构造建模扩展 / Construct modeling extension.
    /// </summary>
    /// <param name="pipeline">扩展管线 / Extension pipeline.</param>
    /// <param name="mode">扩展适用模式 / Extension applicable mode.</param>
    /// <param name="contextAwarePipeline">上下文感知管线工厂 / Context-aware pipeline factory.</param>
    /// <exception cref="ArgumentException">Pipeline 和 ContextAwarePipeline 不能同时为 null。</exception>
    public Csp1dModelingExtension(
        IPipeline<LinearMetaModel<Flt64>>? pipeline = null,
        Csp1dExtensionMode mode = Csp1dExtensionMode.ALL,
        Func<ICsp1dModelingContext<V>, IPipeline<LinearMetaModel<Flt64>>>? contextAwarePipeline = null) {
        if (pipeline is null && contextAwarePipeline is null) {
            throw new ArgumentException("Csp1dModelingExtension must have either pipeline or contextAwarePipeline.");
        }
        Pipeline = pipeline;
        Mode = mode;
        ContextAwarePipeline = contextAwarePipeline;
    }

    /// <summary>
    /// 解析实际使用的管线 / Resolve the actual pipeline to use.
    ///
    /// 如果有 ContextAwarePipeline 且提供了 context，则用它生成管线；
    /// 否则回退到静态 Pipeline。
    ///
    /// If ContextAwarePipeline is present and context is provided, use it to generate the pipeline;
    /// otherwise fall back to the static Pipeline.
    /// </summary>
    /// <param name="context">建模上下文 / Modeling context.</param>
    /// <returns>解析后的管线 / Resolved pipeline.</returns>
    public IPipeline<LinearMetaModel<Flt64>> ResolvePipeline(ICsp1dModelingContext<V>? context = null) {
        if (ContextAwarePipeline is not null && context is not null) {
            return ContextAwarePipeline(context);
        }
        return Pipeline!;
    }
}

// ===== Modeling Context =====

/// <summary>
/// CSP1D 建模上下文 / CSP1D modeling context.
///
/// 提供建模管线注册所需的完整领域信息。
/// 下游扩展管线可通过此接口访问产品/物料/设备/方案/聚合根等数据，
/// 无需闭包捕获。
///
/// Provides complete domain information for modeling pipeline registration.
/// Downstream extension pipelines can access product/material/machine/plan/aggregation
/// data through this interface without closure capture.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dModelingContext<V>
    where V : struct {
    /// <summary>建模模式（MILP 或 LP 松弛）/ Modeling mode (MILP or LP relaxation).</summary>
    Csp1dModelingMode Mode { get; }

    /// <summary>是否为列生成最终 MILP 阶段 / Whether this is column generation final MILP stage.</summary>
    bool IsFinalMilp { get; }

    /// <summary>产出聚合根，包含切割方案使用量变量 x[i] / Produce aggregation with plan usage variables.</summary>
    ProduceAggregation<V> Produce { get; }

    /// <summary>需求列表 / Demand list.</summary>
    IReadOnlyList<ProductDemand<V>> Demands { get; }

    /// <summary>物料列表 / Material list.</summary>
    IReadOnlyList<Material<V>> Materials { get; }

    /// <summary>设备列表 / Machine list.</summary>
    IReadOnlyList<Machine<V>> Machines { get; }

    /// <summary>切割方案列表 / Cutting plan list.</summary>
    IReadOnlyList<CuttingPlan<V>> CuttingPlans => Produce.CuttingPlans;

    /// <summary>领域数值样本，用于 solver 值显式转换 / Domain value sample for explicit solver value conversion.</summary>
    V DomainValueSample { get; }

    /// <summary>转换为领域数值 / Convert to domain value.</summary>
    /// <param name="value">求解器值 / Solver value.</param>
    /// <returns>领域数值 / Domain value.</returns>
    V ToDomainValue(Flt64 value);
}

// ===== Incremental Pipeline =====

/// <summary>
/// CSP1D 增量扩展管线 / CSP1D incremental extension pipeline.
///
/// 普通 Pipeline 只描述首次注册。若扩展约束或目标需要在列生成新增列时同步刷新，
/// 应实现此接口，让 Csp1dProduceContext.addColumns 在同一个主模型上调用。
///
/// Plain Pipeline only describes initial registration. When an extension constraint or
/// objective must be refreshed for newly generated columns, implement this interface so
/// Csp1dProduceContext.addColumns can invoke it on the same master model.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dIncrementalPipeline<V> : IPipeline<LinearMetaModel<Flt64>>
    where V : struct {
    /// <summary>
    /// 新增列后的增量刷新 / Incremental refresh after adding columns.
    /// </summary>
    /// <param name="context">建模上下文 / Modeling context.</param>
    /// <param name="iteration">当前迭代号 / Current iteration number.</param>
    /// <param name="newPlans">已完成去重并注册的新增方案 / Newly added plans after deduplication and registration.</param>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>增量刷新结果 / Incremental refresh result.</returns>
    Result<IReadOnlyList<CuttingPlan<V>>, ErrorCode, Error<ErrorCode>> AddColumns(
        ICsp1dModelingContext<V> context,
        UInt64 iteration,
        IReadOnlyList<CuttingPlan<V>> newPlans,
        IAbstractLinearMetaModel<Flt64> model) => Results.Ok<IReadOnlyList<CuttingPlan<V>>>(newPlans);
}

// ===== Plan Judgment Context =====

/// <summary>
/// CSP1D 方案判断上下文 / CSP1D plan judgment context.
///
/// 用于方案级约束判断（如 same unit length、same width）。
/// 提供同物料/同设备的方案索引集合，支持跨方案约束注册。
///
/// Used for plan-level constraint judgment (e.g. same unit length, same width).
/// Provides same-material/same-machine plan index sets for cross-plan constraint registration.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dPlanJudgmentContext<V> : ICsp1dDomainCalculationContext<V>
    where V : struct {
    /// <summary>同物料的所有方案索引 / All plan indices for same material.</summary>
    IReadOnlyList<int> SameMaterialPlanIndices { get; }

    /// <summary>同设备的所有方案索引 / All plan indices for same machine.</summary>
    IReadOnlyList<int> SameMachinePlanIndices { get; }

    /// <summary>所有切割方案 / All cutting plans.</summary>
    IReadOnlyList<CuttingPlan<V>> AllPlans { get; }
}

// ===== Unified Extension Policies =====

/// <summary>
/// CSP1D 目标策略接口 / CSP1D objective policy interface.
///
/// 允许下游注入额外目标项或修正基础目标系数。
/// 默认实现不修改任何目标。
///
/// Allows downstream to inject additional objective terms or modify
/// base objective coefficients. Default implementation does not modify any objective.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dObjectivePolicy<V>
    where V : struct {
    /// <summary>策略名称 / Policy name.</summary>
    string Name { get; }

    /// <summary>
    /// 修正基础 batch coefficient / Modify base batch coefficient for a plan.
    /// </summary>
    /// <param name="context">领域计算上下文 / Domain calculation context.</param>
    /// <param name="baseCoefficient">基础系数 / Base coefficient.</param>
    /// <returns>修正后的系数 / Modified coefficient.</returns>
    Flt64 ModifyBatchCoefficient(ICsp1dDomainCalculationContext<V> context, Flt64 baseCoefficient) => baseCoefficient;
}

/// <summary>
/// CSP1D 生成策略接口 / CSP1D generation policy interface.
///
/// 允许下游注入候选生成过滤、排序和验收逻辑。
/// 默认实现不改变任何生成行为。
///
/// Allows downstream to inject candidate generation filtering,
/// sorting and acceptance logic. Default implementation does not
/// change any generation behavior.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dGenerationStrategy<V>
    where V : struct {
    /// <summary>策略名称 / Policy name.</summary>
    string Name { get; }

    /// <summary>
    /// 判断候选方案是否应被接受 / Check if candidate plan should be accepted.
    /// </summary>
    /// <param name="candidate">候选方案 / Candidate plan.</param>
    /// <param name="existingPlans">现有方案池 / Existing plan pool.</param>
    /// <returns>true 表示接受 / true if accepted.</returns>
    bool AcceptCandidate(CuttingPlan<V> candidate, IReadOnlyList<CuttingPlan<V>> existingPlans) => true;

    /// <summary>
    /// 自定义候选方案的 canonical key / Customize canonical key for candidate plan.
    ///
    /// 返回 null 表示使用默认 canonical key。
    /// Return null to use the default canonical key.
    /// </summary>
    /// <param name="candidate">候选方案 / Candidate plan.</param>
    /// <returns>自定义 canonical key 字符串，或 null 使用默认 / Custom canonical key string, or null for default.</returns>
    string? CanonicalKeyFor(CuttingPlan<V> candidate) => null;

    /// <summary>
    /// dominance 判断：是否接受新候选替代已有方案 /
    /// Dominance judgment: whether to accept new candidate over existing.
    ///
    /// 返回 true 表示新候选通过 dominance 判断应被接受。
    /// 默认返回 true（不额外过滤 dominance）。
    ///
    /// Return true if the new candidate passes dominance check and should be accepted.
    /// Default returns true (no extra dominance filtering).
    /// </summary>
    /// <param name="candidate">候选方案 / Candidate plan.</param>
    /// <param name="existingPlans">现有方案池 / Existing plan pool.</param>
    /// <returns>true 表示通过 dominance 判断 / true if passes dominance check.</returns>
    bool AcceptDominance(CuttingPlan<V> candidate, IReadOnlyList<CuttingPlan<V>> existingPlans) => true;
}

/// <summary>
/// CSP1D 定价策略接口 / CSP1D pricing policy interface.
///
/// 允许下游注入 reduced cost 成本修正、isImproving 判断和候选排序逻辑。
/// 默认实现不改变任何定价行为。
///
/// Allows downstream to inject reduced cost cost modification,
/// isImproving judgment and candidate sorting logic.
/// Default implementation does not change any pricing behavior.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dPricingPolicy<V>
    where V : struct {
    /// <summary>策略名称 / Policy name.</summary>
    string Name { get; }

    /// <summary>
    /// 修正 reduced cost 成本 / Modify reduced cost cost for a candidate plan.
    /// </summary>
    /// <param name="candidate">候选方案 / Candidate plan.</param>
    /// <param name="baseCost">基础成本 / Base cost.</param>
    /// <returns>修正后的成本 / Modified cost.</returns>
    V ModifyCost(CuttingPlan<V> candidate, V baseCost) => baseCost;

    /// <summary>
    /// 修正 reduced cost benefit / Modify reduced cost benefit for a candidate plan.
    /// </summary>
    /// <param name="candidate">候选方案 / Candidate plan.</param>
    /// <param name="baseBenefit">基础对偶收益 / Base dual benefit.</param>
    /// <returns>修正后的收益 / Modified benefit.</returns>
    V ModifyBenefit(CuttingPlan<V> candidate, V baseBenefit) => baseBenefit;

    /// <summary>
    /// 自定义 isImproving 判断 / Custom isImproving judgment.
    ///
    /// 返回 null 表示使用默认判断（benefit > cost）。
    /// 返回 true/false 表示强制判定结果。
    ///
    /// Return null to use default judgment (benefit > cost).
    /// Return true/false to force the judgment result.
    /// </summary>
    /// <param name="candidate">候选方案 / Candidate plan.</param>
    /// <param name="benefit">对偶收益 / Dual benefit.</param>
    /// <param name="cost">目标成本 / Objective cost.</param>
    /// <returns>null 使用默认判断，true/false 强制结果 / null for default, true/false to force.</returns>
    bool? IsImproving(CuttingPlan<V> candidate, V benefit, V cost) => null;
}

// ===== Flow Context =====

/// <summary>
/// CSP1D 流程上下文 / CSP1D flow context.
///
/// 为流程策略提供求解器状态上下文。下游通过此接口访问
/// 当前迭代号、方案池、迭代上限等信息，用于流程判断。
///
/// Provides solver state context for flow policies. Downstream
/// accesses current iteration, plan pool, iteration limit etc.
/// for flow control decisions.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dFlowContext<V>
    where V : struct {
    /// <summary>当前迭代号（0-based）/ Current iteration number (0-based).</summary>
    long Iteration { get; }

    /// <summary>当前方案池 / Current plan pool.</summary>
    IReadOnlyList<CuttingPlan<V>> CurrentPlans { get; }

    /// <summary>列生成迭代上限 / Column generation iteration limit.</summary>
    long IterationLimit { get; }

    /// <summary>是否允许部分解 / Whether partial solution is allowed.</summary>
    bool AllowPartialSolution { get; }

    /// <summary>本轮新方案列表（定价后去重前为空）/ New plans from current pricing (empty before pricing).</summary>
    IReadOnlyList<CuttingPlan<V>> NewPlans => Array.Empty<CuttingPlan<V>>();

    /// <summary>pricing 生成统计（累计）/ Accumulated pricing generation statistics.</summary>
    CuttingPlanGenerationStatistics? PricingStatistics => null;

    /// <summary>LP 求解是否曾成功 / Whether at least one LP solve has succeeded.</summary>
    bool HasValidLpResult => false;

    /// <summary>
    /// warm start 方案数量（recovery 场景可用）/ Warm start plan count (available in recovery scenarios).
    ///
    /// 非 recovery 场景默认为 0。下游 policy 可据此判断 warm start 可复用方案规模。
    /// Default 0 in non-recovery scenarios. Downstream can use this to gauge warm-start reusable plan scale.
    /// </summary>
    long WarmStartPlanCount => 0L;

    /// <summary>
    /// warm start 是否需要 fallback（recovery 场景可用）/ Whether warm start requires fallback (recovery scenarios).
    ///
    /// 非 recovery 场景默认为 false。当 warm start 不可用（Invalid 或 AdapterUnsupported）时为 true。
    /// Default false in non-recovery scenarios. True when warm start is unusable (Invalid or AdapterUnsupported).
    /// </summary>
    bool WarmStartRequiresFallback => false;
}

// ===== Flow Policy =====

/// <summary>
/// CSP1D 流程策略接口 / CSP1D flow policy interface.
///
/// 允许下游注入求解流程控制逻辑，如初始方案过滤、
/// 去重等价判断、终止条件、partial 接受、recovery fallback 等。
/// 默认实现保持当前硬编码行为。
///
/// Allows downstream to inject solver flow control logic,
/// such as initial plan filtering, dedup equivalence judgment,
/// termination conditions, partial acceptance, recovery fallback.
/// Default implementation preserves current hard-coded behavior.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dFlowPolicy<V>
    where V : struct {
    /// <summary>策略名称 / Policy name.</summary>
    string Name { get; }

    /// <summary>
    /// 过滤初始方案池 / Filter initial plan pool.
    /// </summary>
    /// <param name="plans">初始方案列表 / Initial plan list.</param>
    /// <returns>过滤后的方案列表 / Filtered plan list.</returns>
    IReadOnlyList<CuttingPlan<V>> FilterInitialPlans(IReadOnlyList<CuttingPlan<V>> plans) => plans;

    /// <summary>
    /// 带上下文的初始方案过滤 / Context-aware initial plan filtering.
    ///
    /// 默认回退到无上下文版本。
    /// Default falls back to context-free version.
    /// </summary>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <param name="plans">初始方案列表 / Initial plan list.</param>
    /// <returns>过滤后的方案列表 / Filtered plan list.</returns>
    IReadOnlyList<CuttingPlan<V>> FilterInitialPlans(ICsp1dFlowContext<V> context, IReadOnlyList<CuttingPlan<V>> plans) => FilterInitialPlans(plans);

    /// <summary>
    /// 判断两个方案是否等价（用于去重）/ Check if two plans are equivalent (for deduplication).
    /// </summary>
    /// <param name="existing">已有方案 / Existing plan.</param>
    /// <param name="candidate">候选方案 / Candidate plan.</param>
    /// <returns>true 表示等价 / true if equivalent.</returns>
    bool IsEquivalent(CuttingPlan<V> existing, CuttingPlan<V> candidate) => false;

    /// <summary>
    /// 带上下文的等价判断 / Context-aware equivalence check.
    ///
    /// 默认回退到无上下文版本。
    /// Default falls back to context-free version.
    /// </summary>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <param name="existing">已有方案 / Existing plan.</param>
    /// <param name="candidate">候选方案 / Candidate plan.</param>
    /// <returns>true 表示等价 / true if equivalent.</returns>
    bool IsEquivalent(ICsp1dFlowContext<V> context, CuttingPlan<V> existing, CuttingPlan<V> candidate) => IsEquivalent(existing, candidate);

    /// <summary>
    /// 是否提前终止迭代（iteration limit 之外的业务停止条件）/
    /// Whether to stop iteration early (business stop condition beyond iteration limit).
    ///
    /// 默认不停止。下游可基于当前方案池大小、LP 目标趋势等判断提前终止。
    /// Default does not stop. Downstream may decide early termination based on
    /// current plan pool size, LP objective trend, etc.
    /// </summary>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <returns>true 表示应停止迭代 / true if iteration should stop.</returns>
    bool ShouldStopIteration(ICsp1dFlowContext<V> context) => false;

    /// <summary>
    /// 自定义终止原因和消息 / Customize termination reason and message.
    ///
    /// 允许扩展 termination reason/message，但默认不变。
    /// Allows extending termination reason/message, but default is unchanged.
    /// </summary>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <param name="defaultReason">默认终止原因 / Default termination reason.</param>
    /// <param name="defaultMessage">默认消息 / Default message.</param>
    /// <returns>(终止原因名, 消息) 元组 / (reason name, message) tuple.</returns>
    (string Reason, string? Message) SelectTermination(
        ICsp1dFlowContext<V> context,
        string defaultReason,
        string? defaultMessage) => (defaultReason, defaultMessage);

    /// <summary>
    /// final MILP 失败时是否接受 partial / Whether to accept partial solution when final MILP fails.
    /// </summary>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <param name="defaultDecision">默认决策 / Default decision.</param>
    /// <returns>true 表示接受 partial / true to accept partial.</returns>
    bool AcceptPartial(ICsp1dFlowContext<V> context, bool defaultDecision) => defaultDecision;

    /// <summary>
    /// recovery/fallback 是否启用 / Whether recovery/fallback is enabled.
    /// </summary>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <param name="defaultDecision">默认决策 / Default decision.</param>
    /// <returns>true 表示启用 / true to enable.</returns>
    bool AllowRecoveryFallback(ICsp1dFlowContext<V> context, bool defaultDecision) => defaultDecision;
}

// ===== Flow Policy Helper Functions =====

/// <summary>
/// 流程策略辅助方法 / Flow policy helper methods.
/// </summary>
public static class Csp1dFlowPolicyHelpers {
    /// <summary>
    /// 通过流程策略列表过滤初始方案 / Filter initial plans through flow policy list.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="policies">流程策略列表 / Flow policy list.</param>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <param name="plans">初始方案列表 / Initial plan list.</param>
    /// <returns>过滤后的方案列表 / Filtered plan list.</returns>
    public static IReadOnlyList<CuttingPlan<V>> FilterInitialPlansByPolicies<V>(
        IReadOnlyList<ICsp1dFlowPolicy<V>> policies,
        ICsp1dFlowContext<V> context,
        IReadOnlyList<CuttingPlan<V>> plans)
        where V : struct {
        IReadOnlyList<CuttingPlan<V>> result = plans;
        foreach (ICsp1dFlowPolicy<V> policy in policies) {
            result = policy.FilterInitialPlans(context, result);
        }
        return result;
    }

    /// <summary>
    /// 通过流程策略列表检查等价 / Check equivalence through flow policy list.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="policies">流程策略列表 / Flow policy list.</param>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <param name="existing">已有方案 / Existing plan.</param>
    /// <param name="candidate">候选方案 / Candidate plan.</param>
    /// <returns>true 表示等价 / true if equivalent.</returns>
    public static bool IsEquivalentByPolicies<V>(
        IReadOnlyList<ICsp1dFlowPolicy<V>> policies,
        ICsp1dFlowContext<V> context,
        CuttingPlan<V> existing,
        CuttingPlan<V> candidate)
        where V : struct {
        foreach (ICsp1dFlowPolicy<V> policy in policies) {
            if (policy.IsEquivalent(context, existing, candidate)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 通过流程策略列表判断是否提前停止 / Check early stop through flow policy list.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="policies">流程策略列表 / Flow policy list.</param>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <returns>true 表示应停止 / true if should stop.</returns>
    public static bool ShouldStopByPolicies<V>(
        IReadOnlyList<ICsp1dFlowPolicy<V>> policies,
        ICsp1dFlowContext<V> context)
        where V : struct {
        foreach (ICsp1dFlowPolicy<V> policy in policies) {
            if (policy.ShouldStopIteration(context)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 通过流程策略列表自定义终止 / Select termination through flow policy list.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="policies">流程策略列表 / Flow policy list.</param>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <param name="defaultReason">默认终止原因 / Default termination reason.</param>
    /// <param name="defaultMessage">默认消息 / Default message.</param>
    /// <returns>(终止原因名, 消息) 元组 / (reason name, message) tuple.</returns>
    public static (string Reason, string? Message) SelectTerminationByPolicies<V>(
        IReadOnlyList<ICsp1dFlowPolicy<V>> policies,
        ICsp1dFlowContext<V> context,
        string defaultReason,
        string? defaultMessage)
        where V : struct {
        (string defaultReason, string? defaultMessage) result = (defaultReason, defaultMessage);
        foreach (ICsp1dFlowPolicy<V> policy in policies) {
            result = policy.SelectTermination(context, result.Item1, result.Item2);
        }
        return result;
    }

    /// <summary>
    /// 通过流程策略列表判断 partial 接受 / Accept partial through flow policy list.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="policies">流程策略列表 / Flow policy list.</param>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <param name="defaultDecision">默认决策 / Default decision.</param>
    /// <returns>true 表示接受 partial / true to accept partial.</returns>
    public static bool AcceptPartialByPolicies<V>(
        IReadOnlyList<ICsp1dFlowPolicy<V>> policies,
        ICsp1dFlowContext<V> context,
        bool defaultDecision)
        where V : struct {
        bool result = defaultDecision;
        foreach (ICsp1dFlowPolicy<V> policy in policies) {
            result = policy.AcceptPartial(context, result);
        }
        return result;
    }

    /// <summary>
    /// 通过流程策略列表判断 recovery fallback / Allow recovery fallback through flow policy list.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="policies">流程策略列表 / Flow policy list.</param>
    /// <param name="context">流程上下文 / Flow context.</param>
    /// <param name="defaultDecision">默认决策 / Default decision.</param>
    /// <returns>true 表示启用 / true to enable.</returns>
    public static bool AllowRecoveryFallbackByPolicies<V>(
        IReadOnlyList<ICsp1dFlowPolicy<V>> policies,
        ICsp1dFlowContext<V> context,
        bool defaultDecision)
        where V : struct {
        bool result = defaultDecision;
        foreach (ICsp1dFlowPolicy<V> policy in policies) {
            result = policy.AllowRecoveryFallback(context, result);
        }
        return result;
    }
}

// ===== Extraction Policy =====

/// <summary>
/// CSP1D 提取策略接口 / CSP1D extraction policy interface.
///
/// 允许下游在 solution enrichment 阶段向 solution details 或 render KPI
/// 写入自定义信息，而不修改 solution 模型本身。
/// 默认空策略不改变现有 solution、KPI、render 输出。
///
/// Allows downstream to write custom information to solution details or render KPI
/// during solution enrichment, without modifying the solution model itself.
/// Default empty policy does not change existing solution, KPI, or render output.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public interface ICsp1dExtractionPolicy<V>
    where V : struct {
    /// <summary>策略名称 / Policy name.</summary>
    string Name { get; }

    /// <summary>
    /// 向 solution details 和 render KPI 写入自定义信息 /
    /// Write custom information to solution details and render KPI.
    /// </summary>
    /// <param name="details">可写的 solution details map / Mutable solution details map.</param>
    /// <param name="renderKpi">可写的 render KPI map / Mutable render KPI map.</param>
    /// <param name="produce">求解产出 / Solve output.</param>
    /// <param name="demands">需求列表 / Demand list.</param>
    /// <param name="materials">物料列表 / Material list.</param>
    /// <param name="machines">设备列表 / Machine list.</param>
    /// <param name="generatedPlans">生成的方案池 / Generated plan pool.</param>
    /// <param name="iterationCount">列生成迭代次数 / Column generation iteration count.</param>
    /// <param name="terminationReason">列生成终止原因名 / Column generation termination reason name.</param>
    /// <param name="finalMilpStatus">最终 MILP 状态名 / Final MILP status name.</param>
    /// <param name="pricingStatistics">pricing 生成统计 / Pricing generation statistics.</param>
    void EnrichOutput(
        IDictionary<string, string> details,
        IDictionary<string, string> renderKpi,
        Produce<V> produce,
        IReadOnlyList<ProductDemand<V>> demands,
        IReadOnlyList<Material<V>> materials,
        IReadOnlyList<Machine<V>> machines,
        IReadOnlyList<CuttingPlan<V>> generatedPlans,
        long iterationCount,
        string? terminationReason,
        string? finalMilpStatus,
        CuttingPlanGenerationStatistics? pricingStatistics) {
        // Default no-op
    }
}

// ===== Extension Set =====

/// <summary>
/// CSP1D 扩展包 / CSP1D extension set.
///
/// 承载所有扩展策略的统一容器。下游通过此类型注入
/// 建模管线、领域策略、目标策略、生成策略、定价策略和流程策略。
/// 默认空实现不改变现有求解行为。
///
/// Unified container for all extension policies. Downstream injects
/// modeling pipelines, domain policies, objective policies,
/// generation policies, pricing policies and flow policies through this type.
/// Default empty implementation does not change existing solver behavior.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed record Csp1dExtensionSet<V>(
    /// <summary>建模扩展管线列表 / Modeling extension pipeline list.</summary>
    IReadOnlyList<Csp1dModelingExtension<V>> ModelingExtensions,
    /// <summary>领域策略列表 / Domain policy list.</summary>
    IReadOnlyList<ICsp1dDomainPolicy<V>> DomainPolicies,
    /// <summary>目标策略列表 / Objective policy list.</summary>
    IReadOnlyList<ICsp1dObjectivePolicy<V>> ObjectivePolicies,
    /// <summary>生成策略列表 / Generation strategy list.</summary>
    IReadOnlyList<ICsp1dGenerationStrategy<V>> GenerationStrategies,
    /// <summary>定价策略列表 / Pricing policy list.</summary>
    IReadOnlyList<ICsp1dPricingPolicy<V>> PricingPolicies,
    /// <summary>流程策略列表 / Flow policy list.</summary>
    IReadOnlyList<ICsp1dFlowPolicy<V>> FlowPolicies,
    /// <summary>提取策略列表 / Extraction policy list.</summary>
    IReadOnlyList<ICsp1dExtractionPolicy<V>> ExtractionPolicies
)
    where V : struct {
    /// <summary>
    /// 空扩展包 / Empty extension set.
    /// </summary>
    /// <returns>空扩展包实例 / Empty extension set instance.</returns>
    public static Csp1dExtensionSet<V> Empty { get; } = new(
        ModelingExtensions: Array.Empty<Csp1dModelingExtension<V>>(),
        DomainPolicies: Array.Empty<ICsp1dDomainPolicy<V>>(),
        ObjectivePolicies: Array.Empty<ICsp1dObjectivePolicy<V>>(),
        GenerationStrategies: Array.Empty<ICsp1dGenerationStrategy<V>>(),
        PricingPolicies: Array.Empty<ICsp1dPricingPolicy<V>>(),
        FlowPolicies: Array.Empty<ICsp1dFlowPolicy<V>>(),
        ExtractionPolicies: Array.Empty<ICsp1dExtractionPolicy<V>>()
    );
}
