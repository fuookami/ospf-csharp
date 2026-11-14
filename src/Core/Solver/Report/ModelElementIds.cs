#nullable enable

using System;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;

namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>
/// 原生诊断使用的模型元素标识解析。
/// Model-element identifier resolution used by native diagnostics.
/// </summary>
/// <remarks>
/// 显式 ID 可以承载跨重建身份；由来源标识或行、列索引生成的键只用于当前流水线的诊断，
/// 不得提升为跨重建身份。
/// Explicit identifiers may carry cross-rebuild identity; keys derived from an origin or an index are
/// pipeline-local diagnostics and must not be presented as a cross-rebuild identity.
/// </remarks>
public static class ModelElementIds {
    /// <summary>解析变量诊断标识 / Resolve the diagnostic variable identifier.</summary>
    /// <param name="variable">模型视图变量 / Model-view variable.</param>
    /// <returns>变量诊断标识 / Diagnostic variable identifier.</returns>
    public static VariableId DiagnosticVariableId(this ModelViewVariable variable) {
        if (variable.Id is { } id) {
            return id;
        }
        return variable.Origin is { } origin
            ? new VariableId($"{origin.Identifier}:{origin.Index}")
            : new VariableId($"model-local-variable:{variable.Index}");
    }

    /// <summary>解析线性行诊断标识 / Resolve the diagnostic linear-row identifier.</summary>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <param name="index">模型约束行索引 / Model constraint row index.</param>
    /// <returns>线性约束诊断标识 / Diagnostic linear constraint identifier.</returns>
    public static ConstraintId DiagnosticConstraintId(this ILinearTriadModelView model, int index) =>
        DiagnosticConstraintId(model.Constraints, index);

    /// <summary>解析二次行诊断标识 / Resolve the diagnostic quadratic-row identifier.</summary>
    /// <param name="model">二次四元模型视图 / Quadratic tetrad model view.</param>
    /// <param name="index">模型约束行索引 / Model constraint row index.</param>
    /// <returns>二次约束诊断标识 / Diagnostic quadratic constraint identifier.</returns>
    public static ConstraintId DiagnosticConstraintId(this IQuadraticTetradModelView model, int index) =>
        DiagnosticConstraintId(model.Constraints, index);

    private static ConstraintId DiagnosticConstraintId<ConCell>(ModelConstraint<ConCell> constraints, int index)
        where ConCell : IConstraintCell<ConCell>, ICopyable<ConCell> {
        ConstraintId? explicitId = index >= 0 && index < constraints.Ids.Count ? constraints.Ids[index] : null;
        if (explicitId is { } id && !string.IsNullOrEmpty(id.Value)) {
            return id;
        }
        string originName = constraints.IdentityOriginAt(index)?.Key ?? "";
        string displayName = index >= 0 && index < constraints.Names.Count ? constraints.Names[index] : "";
        string prefix = !string.IsNullOrWhiteSpace(originName)
            ? $"origin-constraint:{originName}"
            : !string.IsNullOrWhiteSpace(displayName)
                ? $"model-local-constraint:{displayName}"
                : "model-local-constraint";
        return new ConstraintId($"{prefix}:{index}");
    }
}
