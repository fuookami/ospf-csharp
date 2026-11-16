#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 把连续半径求解结果装配成"半径选择结果" / Assemble continuous-radius solver results into radius-selection results.
/// </summary>
/// <remarks>
/// 四处沿用：
///
/// 1. **空输入给空列表**，不是抛错——没有可装配的东西不是失败。
/// 2. **求解结果里没有的变量名一律跳过**，而不是造一个零半径结果——零半径会被 `Validate` 拒，造出来只会炸在别处。
/// 3. **两类缺失处理不同**：缺 `solverRadius` **跳过**（没有半径就没有结果）；缺误差字段**按 0 处理**（误差可以缺省为零，
///    半径不能）。这条区别是整段代码最容易抄错的地方。
/// 4. **原型必须有 `RadiusWeightFunctionKey`**，它是结果的键；没有就抛——键是回指依据，不允许为空。
///
/// Aligned with its. Four things copied: **empty input yields an empty list** rather than throwing, since nothing to assemble is not a failure;
/// **names absent from the solver results are skipped** rather than given a zero-radius result, which `Validate` would reject anyway and which would only blow up somewhere else; **the two kinds
/// of absence differ**, a missing `solverRadius` **skipping the entry** while missing error fields **default to zero**, because an error may be absent but a radius may not — the easiest line here
/// to copy wrongly; and **a prototype must carry a `RadiusWeightFunctionKey`**, its result key, throwing when absent, since a key is how a result is traced back.
/// </remarks>
public static class ContinuousRadiusSelectionExtractor {
    /// <summary>
    /// 由原生求解结果装配 / Assemble from native solver results.
    /// </summary>
    /// <param name="prototypes">原型 / The prototypes.</param>
    /// <param name="solverResults">变量名到半径 / Variable names to radii.</param>
    /// <returns>选择结果 / The selection results.</returns>
    public static IReadOnlyList<CylinderRadiusSelectionResult> BuildNativeResults(
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes,
        IReadOnlyDictionary<string, FltX> solverResults) {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(solverResults);
        if (solverResults.Count == 0) {
            return Array.Empty<CylinderRadiusSelectionResult>();
        }

        var results = new List<CylinderRadiusSelectionResult>(prototypes.Count);
        foreach (ContinuousCylinderRadiusSolverPrototype prototype in prototypes) {
            if (!solverResults.TryGetValue(prototype.VariableName, out FltX radius)) {
                continue;
            }
            results.Add(WithSelected(prototype, radius, null));
        }
        return results;
    }

    /// <summary>
    /// 由 PWL 求解结果（按变量名的字典）装配 / Assemble from PWL solver results keyed by variable name.
    /// </summary>
    /// <param name="prototypes">原型 / The prototypes.</param>
    /// <param name="pwlResults">变量名到字段字典 / Variable names to field dictionaries.</param>
    /// <returns>选择结果 / The selection results.</returns>
    public static IReadOnlyList<CylinderRadiusSelectionResult> BuildPwlResults(
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, FltX>> pwlResults) {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(pwlResults);
        if (pwlResults.Count == 0) {
            return Array.Empty<CylinderRadiusSelectionResult>();
        }

        var results = new List<CylinderRadiusSelectionResult>(pwlResults.Count);
        foreach (KeyValuePair<string, IReadOnlyDictionary<string, FltX>> entry in pwlResults) {
            ContinuousCylinderRadiusSolverPrototype? prototype = FindPrototype(prototypes, entry.Key);
            if (prototype is null) {
                continue;
            }
            IReadOnlyDictionary<string, FltX> values = entry.Value;
            if (!values.TryGetValue("solverRadius", out FltX solverRadius)) {
                continue;
            }
            if (!values.TryGetValue("solverRadiusSquared", out FltX solverRadiusSquared)
                || !values.TryGetValue("actualRadiusSquared", out FltX actualRadiusSquared)) {
                continue;
            }

            var metadata = new PWLRadiusSelectionMetadata(
                SolverRadiusSquared: solverRadiusSquared,
                ActualRadiusSquared: actualRadiusSquared,
                PwlAbsoluteError: values.TryGetValue("pwlAbsoluteError", out FltX absolute) ? absolute : new FltX(0.0),
                PwlRelativeError: values.TryGetValue("pwlRelativeError", out FltX relative) ? relative : new FltX(0.0),
                MaxPwlRelativeError: values.TryGetValue("maxPWLRelativeError", out FltX maxRelative)
                    ? maxRelative
                    : new FltX(0.0),
                NumSegments: values.TryGetValue("numSegments", out FltX segments)
                    ? System.Math.Max(1, (int)segments.ToFlt64().ToDouble())
                    : 1,
                IsWithinEnvelope: values.TryGetValue("isWithinEnvelope", out FltX within)
                    && within.ToFlt64().ToDouble() > 0.5);
            results.Add(WithSelected(prototype, solverRadius, metadata));
        }
        return results;
    }

    /// <summary>
    /// 由已抽取的 PWL 结果装配 / Assemble from already-extracted PWL results.
    /// </summary>
    /// <param name="prototypes">原型 / The prototypes.</param>
    /// <param name="extractedResults">已抽取结果 / The extracted results.</param>
    /// <returns>选择结果 / The selection results.</returns>
    /// <remarks>
    /// 这条路径的误差字段**直接来自 `PwlExtractedRadius`**，段数与最大相对误差取自它的近似对象——
    /// 与上面那条"从字典里捞"的路径不同，这里没有缺省问题，所以也不该再给默认值。
    ///
    /// On this path the error fields **come straight from `PwlExtractedRadius`**, with the segment count and maximum relative error taken from its approximation object. Unlike the dictionary path
    /// above there is nothing to default, so nothing should be defaulted here.
    /// </remarks>
    public static IReadOnlyList<CylinderRadiusSelectionResult> BuildPwlResultsFromExtracted(
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes,
        IReadOnlyList<PwlExtractedRadius> extractedResults) {
        ArgumentNullException.ThrowIfNull(prototypes);
        ArgumentNullException.ThrowIfNull(extractedResults);
        if (extractedResults.Count == 0) {
            return Array.Empty<CylinderRadiusSelectionResult>();
        }

        var results = new List<CylinderRadiusSelectionResult>(extractedResults.Count);
        foreach (PwlExtractedRadius extracted in extractedResults) {
            ContinuousCylinderRadiusSolverPrototype? prototype = FindPrototype(prototypes, extracted.VariableName);
            if (prototype is null) {
                continue;
            }
            var metadata = new PWLRadiusSelectionMetadata(
                SolverRadiusSquared: extracted.SolverRadiusSquared,
                ActualRadiusSquared: extracted.ActualRadiusSquared,
                PwlAbsoluteError: extracted.PwlAbsoluteError,
                PwlRelativeError: extracted.PwlRelativeError,
                MaxPwlRelativeError: new FltX(extracted.PwlApproximation.MaxRelativeError),
                NumSegments: System.Math.Max(1, extracted.PwlApproximation.SegmentCount),
                IsWithinEnvelope: extracted.IsWithinEnvelope);
            results.Add(WithSelected(prototype, extracted.SolverRadius, metadata));
        }
        return results;
    }

    private static ContinuousCylinderRadiusSolverPrototype? FindPrototype(
        IReadOnlyList<ContinuousCylinderRadiusSolverPrototype> prototypes, string variableName) {
        foreach (ContinuousCylinderRadiusSolverPrototype prototype in prototypes) {
            if (string.Equals(prototype.VariableName, variableName, StringComparison.Ordinal)) {
                return prototype;
            }
        }
        return null;
    }

    private static CylinderRadiusSelectionResult WithSelected(
        ContinuousCylinderRadiusSolverPrototype prototype, FltX radius, PWLRadiusSelectionMetadata? metadata) {
        if (prototype.RadiusWeightFunctionKey is null) {
            throw new InvalidOperationException(
                "continuous cylinder radius solver prototype must have a radius weight function key to build a selection result");
        }
        var result = new CylinderRadiusSelectionResult(
            Key: prototype.RadiusWeightFunctionKey,
            SelectedRadius: new Quantity<FltX>(radius, SIBaseUnits.Meter),
            Axis: prototype.Axis,
            RadiusMin: prototype.RadiusMin,
            RadiusMax: prototype.RadiusMax,
            VariableName: prototype.VariableName,
            Source: prototype.Source,
            PwlMetadata: metadata);
        result.Validate();
        return result;
    }
}
