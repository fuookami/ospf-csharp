#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **输入界的解析与校验** / Resolving and validating the input bounds.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 里"导入选项的界 → 真正能用的界"这一步。**这一层只做判定，不把界写进变量：**
/// **逐变量界属于模型层（`Range` 由 `NumericConstantsRegistry` 给，逐变量的界要靠模型注册），** **而在插件层假装写好界，
/// 会让"界没生效"以"近似对了"的面目出现。**
///
/// **能把界与界的使用分开验，本身就是一次分层收获：** **界缺失是配置问题，界用错是代码问题，**两种错误的消息不该混在一起。
///
/// Syncs the "options bounds → usable bounds" step. **This layer only decides, it does not write bounds into variables: per-variable bounds belong to the model layer
/// (`Range` comes from `NumericConstantsRegistry`, and per-variable bounds require model registration), and pretending here to have written them would let "the bounds
/// never took effect" present itself as "the approximation is right".**
///
/// **Keeping the decision and the use apart is itself a gain: a missing bound is a configuration problem and a misused bound a code problem, and their messages should
/// not be mixed.**
/// </remarks>
public static class OnnxInputBounds
{
    /// <summary>
    /// 解析结果 / The resolution result.
    /// </summary>
    /// <param name="Resolved">已解析的界 / The resolved bounds.</param>
    /// <param name="Missing">需要界但缺失的输入 / The inputs that need bounds and lack them.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Resolution(
        IReadOnlyDictionary<string, OnnxBounds> Resolved,
        IReadOnlyList<string> Missing,
        string? Failure)
    {
        /// <summary>是否完全可用 / Whether everything needed was resolved.</summary>
        public bool IsComplete => Failure is null && Missing.Count == 0;
    }

    /// <summary>
    /// 从选项里解析出图真正需要的界 / Resolve the bounds the graph actually needs from the options.
    /// </summary>
    /// <param name="graph">图 / The graph.</param>
    /// <param name="options">导入选项 / The import options.</param>
    /// <returns>解析结果 / The result.</returns>
    /// <remarks>
    /// **只有"需要界的算子"的输入才要界：** **要求全图每个输入都有界，会把一条纯线性链挡在门外——** **而那明明不需要任何近似。**
    ///
    /// **Only the inputs of operators that need bounds require them: demanding bounds for every input would turn away a purely linear chain that needs no
    /// approximation at all.**
    /// </remarks>
    public static Resolution Resolve(OnnxGraph graph, OnnxImportOptions options) {
        if (!options.Validate(out string? optionFailure)) {
            return new Resolution(new Dictionary<string, OnnxBounds>(), Array.Empty<string>(), optionFailure);
        }

        var resolved = new Dictionary<string, OnnxBounds>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (OnnxNode node in graph.Nodes) {
            if (!OnnxOperatorRegistry.Requirements.TryGetValue(node.OpType, out OnnxOperatorRegistry.Requirement? requirement)) {
                // 未知算子由前置检查负责报告，这里不重复。 / Unknown operators are the preflight's business; not repeated here.
                continue;
            }
            if (!requirement.NeedsBounds) {
                continue;
            }
            foreach (string input in node.Inputs) {
                if (input.Length == 0) {
                    continue;
                }
                if (resolved.ContainsKey(input)) {
                    continue;
                }
                if (!options.InputBounds.TryGetValue(input, out OnnxBounds? bounds)) {
                    if (!missing.Contains(input)) {
                        missing.Add(input);
                    }
                    continue;
                }
                if (!bounds.Validate(out string? boundFailure)) {
                    return new Resolution(resolved, missing, "输入 '" + input + "' 的" + boundFailure);
                }
                // 与分段近似共享同一条上限：超过它就是"形式有限、实际无界"。
                // Shares the piecewise limit: past it the range is formally finite and practically unbounded.
                if (System.Math.Abs(bounds.Lower) > OnnxPiecewiseApproximation.MaxPiecewiseBound
                    || System.Math.Abs(bounds.Upper) > OnnxPiecewiseApproximation.MaxPiecewiseBound) {
                    return new Resolution(
                        resolved,
                        missing,
                        "输入 '" + input + "' 的界超过分段近似的可识别上限 "
                        + OnnxPiecewiseApproximation.MaxPiecewiseBound
                        + "，按缺少有效范围处理 / the bound exceeds the piecewise limit and is treated as missing");
                }
                resolved[input] = bounds;
            }
        }

        return new Resolution(resolved, missing, null);
    }
}