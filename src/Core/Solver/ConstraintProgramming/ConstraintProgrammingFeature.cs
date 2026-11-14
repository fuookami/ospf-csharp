#nullable enable

namespace Fuookami.Ospf.Core.Solver.ConstraintProgramming;

/// <summary>约束规划能力 / Constraint-programming capability.</summary>
public enum ConstraintProgrammingFeature {
    /// <summary>布尔逻辑 / Boolean logic.</summary>
    BooleanLogic,
    /// <summary>重ification / Reification.</summary>
    Reification,
    /// <summary>稀疏值域 / Sparse domain.</summary>
    SparseDomain,
    /// <summary>全不同 / All-different.</summary>
    AllDifferent,
    /// <summary>元素约束 / Element constraint.</summary>
    Element,
    /// <summary>表约束 / Table constraint.</summary>
    Table,
    /// <summary>区间变量 / Interval variable.</summary>
    Interval,
    /// <summary>可选区间变量 / Optional interval variable.</summary>
    OptionalInterval,
    /// <summary>不重叠约束 / No-overlap constraint.</summary>
    NoOverlap,
    /// <summary>累积约束 / Cumulative constraint.</summary>
    Cumulative,
    /// <summary>回路约束 / Circuit constraint.</summary>
    Circuit,
    /// <summary>自动机约束 / Automaton constraint.</summary>
    Automaton,
    /// <summary>水库约束 / Reservoir constraint.</summary>
    Reservoir,
    /// <summary>假设求解 / Assumption solving.</summary>
    Assumption,
    /// <summary>冲突核 / Conflict core.</summary>
    ConflictCore,
    /// <summary>解提示 / Solution hint.</summary>
    SolutionHint,
    /// <summary>增量求解 / Incremental solve.</summary>
    IncrementalSolve,
}

/// <summary>约束规划能力支持级别 / Constraint-programming support level.</summary>
public enum ConstraintProgrammingSupportLevel {
    /// <summary>后端原生支持 / Native backend support.</summary>
    Native,
    /// <summary>精确降阶支持 / Exact lowering support.</summary>
    ExactLowering,
    /// <summary>不支持 / Unsupported.</summary>
    Unsupported,
}

/// <summary>约束规划能力查询扩展 / Constraint-programming capability queries.</summary>
public static class ConstraintProgrammingCapabilityExtensions {
    /// <summary>获取能力支持级别 / Get the support level for a feature.</summary>
    /// <param name="capabilities">求解器能力 / Solver capabilities.</param>
    /// <param name="feature">约束规划能力 / Constraint-programming feature.</param>
    /// <returns>支持级别；缺失时视为不支持 / Support level, defaulting to unsupported.</returns>
    public static ConstraintProgrammingSupportLevel ConstraintProgrammingSupport(
        this Fuookami.Ospf.Core.Solver.Report.SolverCapabilities capabilities,
        ConstraintProgrammingFeature feature) {
        return capabilities.ConstraintProgrammingFeatures.TryGetValue(feature, out ConstraintProgrammingSupportLevel support)
            ? support
            : ConstraintProgrammingSupportLevel.Unsupported;
    }

    /// <summary>判断约束规划能力是否可用 / Check whether a constraint-programming feature is available.</summary>
    /// <param name="capabilities">求解器能力 / Solver capabilities.</param>
    /// <param name="feature">约束规划能力 / Constraint-programming feature.</param>
    /// <returns>是否可用 / Whether the feature is available.</returns>
    public static bool SupportsConstraintProgramming(
        this Fuookami.Ospf.Core.Solver.Report.SolverCapabilities capabilities,
        ConstraintProgrammingFeature feature) =>
        capabilities.ConstraintProgrammingSupport(feature) != ConstraintProgrammingSupportLevel.Unsupported;
}
