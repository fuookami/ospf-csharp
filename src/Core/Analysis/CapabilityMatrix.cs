#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>后端支持可以被调度的分析操作 / Analysis operations whose backend support can be dispatched.</summary>
/// <remarks>
/// 与基线 `CapabilityMatrix.kt` 的 `AnalysisCapability` 逐成员对应，**顺序也一致**。
/// Mirrors `AnalysisCapability` in the baseline, member for member and in order.
/// </remarks>
public enum AnalysisCapability
{
    /// <summary>活动性分析 / Activity analysis.</summary>
    Activity,
    /// <summary>固定整数 LP 灵敏度 / Fixed-integer LP sensitivity.</summary>
    FixedIntegerLpSensitivity,
    /// <summary>右端项扰动 / Right-hand-side perturbation.</summary>
    RhsPerturbation,
    /// <summary>自适应扰动 / Adaptive perturbation.</summary>
    AdaptivePerturbation,
    /// <summary>移除测试 / Removal test.</summary>
    RemovalTest,
    /// <summary>目标条件 / Objective target.</summary>
    ObjectiveTarget,
    /// <summary>Assumption 求解 / Assumption solving.</summary>
    AssumptionSolving,
    /// <summary>原生 unsat core / Native unsat core.</summary>
    NativeUnsatCore,
    /// <summary>冲突分析 / Conflict analysis.</summary>
    Conflict,
    /// <summary>最小不可满足子集 / Minimal unsatisfiable subset.</summary>
    Mus,
    /// <summary>约束组聚合 / Constraint-group aggregation.</summary>
    ConstraintGroupAggregation,
    /// <summary>热启动 / Warm start.</summary>
    WarmStart,
    /// <summary>精确 CP 降阶 / Exact CP lowering.</summary>
    ExactCpLowering,
}

/// <summary>分析操作的可用级别 / Availability of an analysis operation.</summary>
public enum CapabilitySupport
{
    /// <summary>有明确实现 / Available with the declared implementation.</summary>
    Supported,
    /// <summary>需要运行时或模型检查 / Available only after runtime or model checks.</summary>
    Conditional,
    /// <summary>没有可用实现 / No usable implementation is declared.</summary>
    Unsupported,
}

/// <summary>
/// 用于分析调度、与求解器无关的能力矩阵 / Solver-neutral capability matrix used by analysis dispatch.
/// </summary>
/// <remarks>
/// 与基线 `CapabilityMatrix.kt`（297 行）对应。**基础标志映射 `SolverCapabilities`，分析能力映射允许后端适配器覆盖某一阶段**——
/// 因为有些阶段的可用性取决于通用求解器描述符推断不出的细节。
///
/// 补**：它不依赖原生后端（只依赖报告层与 CP 类型），所以能独立移植。
/// **一处刻意的保守**：`FallbackSatisfactionOnly` **只在后端声明了 CP 满足路径时才成立**。基线为此写过一条注释——
/// 从任意模型类型推导，会让只支持线性/MIP 的后端**声称一个它兑现不了的回退**。
///
/// Mirrors the baseline's `CapabilityMatrix.kt` (297 lines). **The primitive flags mirror `SolverCapabilities`, and the analysis map lets a backend adapter override a stage**
/// because some stages depend on details the generic descriptor cannot infer. ****: it needs no native backend, only the report and CP types. **One deliberate
/// conservatism**: `FallbackSatisfactionOnly` **holds only when the backend declares a CP satisfaction path** — deriving it from any model type would let a linear/MIP-only
/// backend **advertise a fallback it cannot deliver**, which the baseline calls out in a comment.
/// </remarks>
/// <param name="ModelTypes">支持的求解器模型类型 / Supported solver model types.</param>
/// <param name="NativeAssumptionSolving">是否原生支持 assumption 求解 / Whether assumption solving is native.</param>
/// <param name="NativeUnsatCore">是否原生支持 unsat core / Whether unsat-core extraction is native.</param>
/// <param name="FallbackSatisfactionOnly">是否支持仅满足性回退 / Whether satisfaction-only fallback is available.</param>
/// <param name="Dual">是否支持对偶值 / Whether dual values are available.</param>
/// <param name="WarmStart">是否支持热启动 / Whether warm starts are supported.</param>
/// <param name="ExactCpLowering">是否支持精确 CP 降阶 / Whether exact CP lowering is supported.</param>
/// <param name="ConstraintProgrammingFeatures">CP 特性支持级别 / CP feature support levels.</param>
/// <param name="AnalysisCapabilities">显式分析能力覆盖项 / Explicit analysis capability overrides.</param>
public sealed record CapabilityMatrix(
    IReadOnlySet<SolverModelType> ModelTypes,
    bool NativeAssumptionSolving = false,
    bool NativeUnsatCore = false,
    bool FallbackSatisfactionOnly = false,
    bool Dual = false,
    bool WarmStart = false,
    bool ExactCpLowering = false,
    IReadOnlyDictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel>? ConstraintProgrammingFeatures = null,
    IReadOnlyDictionary<AnalysisCapability, CapabilitySupport>? AnalysisCapabilities = null)
{
    /// <summary>灵敏度适配器使用的别名 / Alias used by sensitivity adapters.</summary>
    public bool LpDual => Dual;

    /// <summary>CP 特性声明，未给出时为空 / The CP feature declaration, empty when absent.</summary>
    public IReadOnlyDictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel> Features =>
        ConstraintProgrammingFeatures ?? new Dictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel>();

    /// <summary>显式覆盖项，未给出时为空 / The explicit overrides, empty when absent.</summary>
    public IReadOnlyDictionary<AnalysisCapability, CapabilitySupport> Overrides =>
        AnalysisCapabilities ?? new Dictionary<AnalysisCapability, CapabilitySupport>();

    /// <summary>
    /// 返回操作声明或推断出的支持级别 / Return the declared or inferred support for an operation.
    /// </summary>
    /// <param name="capability">要查询的分析操作 / Analysis operation to query.</param>
    /// <returns>操作支持级别 / Operation support level.</returns>
    public CapabilitySupport Support(AnalysisCapability capability) =>
        Overrides.TryGetValue(capability, out CapabilitySupport declared) ? declared : InferSupport(capability);

    /// <summary>
    /// 分析器完成检查后即可使用条件支持 / Conditional support is usable after the analyzer performs its checks.
    /// </summary>
    /// <param name="capability">要检查的分析操作 / Analysis operation to check.</param>
    /// <returns>是否存在可用实现 / Whether a usable implementation exists.</returns>
    public bool Supports(AnalysisCapability capability) => Support(capability) != CapabilitySupport.Unsupported;

    /// <summary>
    /// 声明的支持是否为原生实现而非框架回退 / Whether the declared support is native rather than a framework fallback.
    /// </summary>
    /// <param name="capability">要检查的分析操作 / Analysis operation to check.</param>
    /// <returns>是否为原生支持 / Whether native support is declared.</returns>
    public bool IsNative(AnalysisCapability capability) => capability switch
    {
        AnalysisCapability.AssumptionSolving => NativeAssumptionSolving,
        AnalysisCapability.NativeUnsatCore => NativeUnsatCore,
        AnalysisCapability.WarmStart => WarmStart,
        AnalysisCapability.ExactCpLowering => ExactCpLowering,
        _ => false,
    };

    /// <summary>
    /// 使用现有 CP 支持级别词汇查询 CP 特性支持 / CP feature support with the existing support-level vocabulary.
    /// </summary>
    /// <param name="feature">要查询的 CP 特性 / CP feature to query.</param>
    /// <returns>CP 特性支持级别 / CP feature support level.</returns>
    public ConstraintProgrammingSupportLevel CpSupport(ConstraintProgrammingFeature feature) =>
        Features.TryGetValue(feature, out ConstraintProgrammingSupportLevel level)
            ? level
            : ConstraintProgrammingSupportLevel.Unsupported;

    /// <summary>
    /// CP 特性是否以原生或精确降阶级别可用 / Whether a CP feature is available at native or exact-lowering level.
    /// </summary>
    /// <param name="feature">要检查的 CP 特性 / CP feature to check.</param>
    /// <returns>CP 特性是否可用 / Whether the CP feature is available.</returns>
    public bool Supports(ConstraintProgrammingFeature feature) =>
        CpSupport(feature) != ConstraintProgrammingSupportLevel.Unsupported;

    private CapabilitySupport InferSupport(AnalysisCapability capability) => capability switch
    {
        AnalysisCapability.Activity or AnalysisCapability.ConstraintGroupAggregation => CapabilitySupport.Supported,

        AnalysisCapability.FixedIntegerLpSensitivity => Dual
            ? (ModelTypes.Contains(SolverModelType.Mip) || ExactCpLowering
                ? CapabilitySupport.Supported
                : CapabilitySupport.Conditional)
            : CapabilitySupport.Unsupported,

        AnalysisCapability.RhsPerturbation
            or AnalysisCapability.RemovalTest
            or AnalysisCapability.ObjectiveTarget => ModelTypes.Count == 0
                ? CapabilitySupport.Conditional
                : CapabilitySupport.Supported,

        // **自适应扰动不能比它所依赖的扰动更强**：上游是 Conditional，这里也必须是 Conditional。
        // **Adaptive perturbation cannot be stronger than the perturbation it depends on.**
        AnalysisCapability.AdaptivePerturbation => Support(AnalysisCapability.RhsPerturbation),

        AnalysisCapability.AssumptionSolving => NativeAssumptionSolving
            ? CapabilitySupport.Supported
            : (FallbackSatisfactionOnly ? CapabilitySupport.Conditional : CapabilitySupport.Unsupported),

        AnalysisCapability.NativeUnsatCore => NativeUnsatCore
            ? CapabilitySupport.Supported
            : CapabilitySupport.Unsupported,

        AnalysisCapability.Conflict => NativeUnsatCore
            ? CapabilitySupport.Supported
            : (FallbackSatisfactionOnly ? CapabilitySupport.Conditional : CapabilitySupport.Unsupported),

        AnalysisCapability.Mus => Support(AnalysisCapability.Conflict) == CapabilitySupport.Unsupported
            ? CapabilitySupport.Unsupported
            : (ModelTypes.Count == 0 ? CapabilitySupport.Conditional : CapabilitySupport.Supported),

        AnalysisCapability.WarmStart => WarmStart ? CapabilitySupport.Supported : CapabilitySupport.Unsupported,

        AnalysisCapability.ExactCpLowering => ExactCpLowering
            ? CapabilitySupport.Supported
            : CapabilitySupport.Unsupported,

        _ => CapabilitySupport.Unsupported,
    };

    /// <summary>
    /// 从现有求解器描述符构造矩阵 / Build a matrix from the existing solver descriptor.
    /// </summary>
    /// <param name="descriptor">求解器描述符 / Solver descriptor.</param>
    /// <returns>能力矩阵 / Capability matrix.</returns>
    public static CapabilityMatrix From(SolverDescriptor descriptor) {
        ArgumentNullException.ThrowIfNull(descriptor);
        return From(descriptor.Capabilities);
    }

    /// <summary>
    /// 从现有求解器能力记录构造矩阵 / Build a matrix from the existing solver capability record.
    /// </summary>
    /// <param name="capabilities">求解器能力记录 / Solver capability record.</param>
    /// <returns>能力矩阵 / Capability matrix.</returns>
    public static CapabilityMatrix From(SolverCapabilities capabilities) {
        ArgumentNullException.ThrowIfNull(capabilities);

        IReadOnlyDictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel> features =
            capabilities.ConstraintProgrammingFeatures
            ?? new Dictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel>();

        // **满足性回退只在后端声明了 CP 满足路径时才成立**：从任意模型类型推导，会让只支持线性/MIP 的后端
        // **声称一个它兑现不了的回退**——上层于是选一条不存在的路径。
        // **The satisfaction fallback holds only when the backend declares a CP satisfaction path**: deriving it from any model type would let a linear/MIP-only backend
        // **advertise a fallback it cannot deliver**, and the layer above would then pick a path that does not exist.
        bool cpSatisfaction = features.TryGetValue(ConstraintProgrammingFeature.BooleanLogic, out ConstraintProgrammingSupportLevel logic)
            && logic != ConstraintProgrammingSupportLevel.Unsupported;

        return new CapabilityMatrix(
            ModelTypes: capabilities.ModelTypes,
            NativeAssumptionSolving: features.TryGetValue(ConstraintProgrammingFeature.Assumption, out ConstraintProgrammingSupportLevel assumption)
                && assumption == ConstraintProgrammingSupportLevel.Native,
            NativeUnsatCore: features.TryGetValue(ConstraintProgrammingFeature.ConflictCore, out ConstraintProgrammingSupportLevel core)
                && core == ConstraintProgrammingSupportLevel.Native,
            FallbackSatisfactionOnly: cpSatisfaction,
            Dual: capabilities.Dual,
            WarmStart: capabilities.WarmStart,
            ExactCpLowering: features.Values.Any(level => level == ConstraintProgrammingSupportLevel.ExactLowering),
            ConstraintProgrammingFeatures: features);
    }

    /// <summary>
    /// 返回活动原始模型中被显式声明为不支持的 CP 特性 / CP features used by the active original model that are explicitly declared unsupported.
    /// </summary>
    /// <param name="snapshot">原始模型快照 / The original model snapshot.</param>
    /// <param name="activeSources">只统计这些来源，<c>null</c> 表示全部 / Count only these sources; <c>null</c> means all.</param>
    /// <returns>被显式声明为不支持、且确实被用到的特性 / Features declared unsupported that are actually used.</returns>
    /// <remarks>
    /// **空声明不等于"全都不支持"**：空 map 表示后端没发布 CP 特性声明（例如协议测试用的假求解器），
    /// 把它当成"所有特性都不支持"会让这类后端在每个模型上都被判为不可用。
    ///
    /// **An empty declaration is not "nothing is supported"**: an empty map means the backend published no CP feature map (the fake solver used by protocol tests, say), and
    /// reading it as "all unsupported" would mark every such backend unusable on every model.
    /// </remarks>
    public IReadOnlySet<ConstraintProgrammingFeature> UnsupportedDeclaredCpFeatures(
        ConstraintProgrammingModelSnapshot snapshot,
        IReadOnlySet<DiagnosticSource>? activeSources = null) {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (Features.Count == 0) {
            return new HashSet<ConstraintProgrammingFeature>();
        }

        var declaredUnsupported = Features
            .Where(pair => pair.Value == ConstraintProgrammingSupportLevel.Unsupported)
            .Select(pair => pair.Key)
            .ToHashSet();
        if (declaredUnsupported.Count == 0) {
            return new HashSet<ConstraintProgrammingFeature>();
        }

        var used = new HashSet<ConstraintProgrammingFeature>();
        foreach (ConstraintProgrammingVariableSnapshot variable in snapshot.Variables) {
            if (variable.Domain == IntegerDomain.Boolean) {
                used.Add(ConstraintProgrammingFeature.BooleanLogic);
            } else if (variable.Domain is IntegerDomain.Values) {
                used.Add(ConstraintProgrammingFeature.SparseDomain);
            }
        }
        foreach (IntervalVariable interval in snapshot.Intervals) {
            used.Add(ConstraintProgrammingFeature.Interval);
            if (interval.Presence is not null) {
                used.Add(ConstraintProgrammingFeature.OptionalInterval);
            }
        }
        foreach (ConstraintProgrammingConstraintSnapshot entry in snapshot.Constraints) {
            if (activeSources is null
                || activeSources.Contains(new DiagnosticSource.Constraint(entry.Id))) {
                CollectCpFeatures(entry.Constraint, used);
            }
        }

        used.IntersectWith(declaredUnsupported);
        return used;
    }

    private static void CollectCpFeatures(
        ConstraintProgrammingConstraint constraint,
        HashSet<ConstraintProgrammingFeature> used) {
        switch (constraint) {
            case ConstraintProgrammingConstraint.IntegerComparison:
                break;
            case ConstraintProgrammingConstraint.BoolAnd:
            case ConstraintProgrammingConstraint.BoolOr:
            case ConstraintProgrammingConstraint.BoolXor:
            case ConstraintProgrammingConstraint.Literal:
                used.Add(ConstraintProgrammingFeature.BooleanLogic);
                break;
            case ConstraintProgrammingConstraint.Implication:
            case ConstraintProgrammingConstraint.Reified:
                used.Add(ConstraintProgrammingFeature.Reification);
                break;
            case ConstraintProgrammingConstraint.AllDifferent:
                used.Add(ConstraintProgrammingFeature.AllDifferent);
                break;
            case ConstraintProgrammingConstraint.Element:
                used.Add(ConstraintProgrammingFeature.Element);
                break;
            case ConstraintProgrammingConstraint.AllowedAssignments:
            case ConstraintProgrammingConstraint.ForbiddenAssignments:
                used.Add(ConstraintProgrammingFeature.Table);
                break;
            case NoOverlap:
                used.Add(ConstraintProgrammingFeature.NoOverlap);
                break;
            case Cumulative:
                used.Add(ConstraintProgrammingFeature.Cumulative);
                break;
            case ConstraintProgrammingConstraint.Circuit:
                used.Add(ConstraintProgrammingFeature.Circuit);
                break;
            case ConstraintProgrammingConstraint.Automaton:
                used.Add(ConstraintProgrammingFeature.Automaton);
                break;
            case ConstraintProgrammingConstraint.Reservoir:
                used.Add(ConstraintProgrammingFeature.Reservoir);
                break;
            default:
                // **不认识的约束不猜**：猜一个特性会让"被声明为不支持"的判定落在错的特性上。
                // **An unknown constraint is not guessed**: guessing would put the "declared unsupported" verdict on the wrong feature.
                break;
        }
    }
}
