#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **子图控制流的可展开性判定** / Whether a subgraph's control flow can be unrolled.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxSubgraph`。**基线的做法是"全部展开"：** **`If` 的条件必须是常量、`Loop` 的迭代次数与终止条件必须是常量、
/// `Scan` 的扫描长度必须来自常量——** **于是建模规模随迭代次数线性增长，而这一切都发生在导入期。**
///
/// **那一整套展开依赖降阶上下文（表达式、张量、令牌），还没移植；** **但展开之前的<b>可展开性判定</b>是独立的，而它恰好是"能不能导入"的分界线：**
/// **一个迭代次数依赖数据的 `Loop` 若被放过，** **模型会按某个猜出来的次数展开，解看着正常——只是优化的是另一个网络。**
///
/// Syncs `OnnxSubgraph`. **The baseline unrolls everything: an `If` condition must be constant, a `Loop` trip count and termination constant, a `Scan` length from a
/// constant — so the modelling size grows linearly with the trip count, all at import time.**
///
/// **The unrolling itself needs the lowering context (expressions, tensors, tokens) and is not ported yet; the unrollability decision that precedes it is independent,
/// and it is exactly the line between "importable" and "not": a loop whose trip count depends on data, if let through, gets unrolled some guessed number of times and
/// the answer looks fine — it simply optimises a different network.**
/// </remarks>
public static class OnnxSubgraphPolicy
{
    /// <summary>可展开的迭代次数上限 / The highest trip count that may be unrolled.</summary>
    /// <remarks>
    /// **上限不是性能考虑而是规模守门：** **一个一万次的循环展开会产出一个没人能转储、没人能诊断的模型，** **而在导入阶段它"成功"了。**
    ///
    /// **The limit guards size rather than performance: unrolling ten thousand iterations yields a model nobody can dump or diagnose, and it "succeeds" at import time.**
    /// </remarks>
    public const int MaxUnroll = 256;

    /// <summary>支持的控制流算子 / The supported control-flow operators.</summary>
    public static readonly IReadOnlySet<string> Operators = new HashSet<string>(StringComparer.Ordinal) {
        "If", "Loop", "Scan",
    };

    /// <summary>
    /// 判定结果 / The decision.
    /// </summary>
    /// <param name="Failure">失败原因，可展开时为 null / The failure reason, null when unrollable.</param>
    /// <param name="TripCount">展开次数 / The unroll count.</param>
    public sealed record Decision(string? Failure, int TripCount = 0)
    {
        /// <summary>是否可展开 / Whether it can be unrolled.</summary>
        public bool CanUnroll => Failure is null;
    }

    /// <summary>
    /// 判定一个控制流节点能否展开 / Decide whether one control-flow node can be unrolled.
    /// </summary>
    /// <param name="opType">算子类型 / The operator type.</param>
    /// <param name="tripCount">迭代次数（`If` 为分支数 1，`Scan` 为扫描长度）/ The trip count (1 for If, the scan length for Scan).</param>
    /// <param name="constant">条件或次数是否来自常量 / Whether the condition or count is constant.</param>
    /// <param name="hasBothBranches">`If` 是否同时给出两个分支 / Whether If supplies both branches.</param>
    /// <returns>判定结果 / The decision.</returns>
    public static Decision Decide(string opType, long tripCount, bool constant, bool hasBothBranches = true) {
        if (!Operators.Contains(opType)) {
            return new Decision("算子 '" + opType + "' 不是子图控制流 / the operator is not subgraph control flow");
        }
        if (!constant) {
            return new Decision(
                "'" + opType + "' 的条件或迭代次数不是常量 / the condition or trip count is not constant, so nothing can be unrolled at import time");
        }
        if (opType == "If" && !hasBothBranches) {
            return new Decision("'If' 缺少一个分支 / one of the branches is missing");
        }
        if (tripCount < 0) {
            return new Decision("'" + opType + "' 的迭代次数为负 / the trip count is negative");
        }
        if (tripCount > MaxUnroll) {
            return new Decision(
                "'" + opType + "' 的迭代次数 " + tripCount + " 超出可展开区间 0.." + MaxUnroll
                + " / the trip count is outside the unrollable range");
        }
        return new Decision(null, (int)tripCount);
    }

    /// <summary>
    /// 判定嵌套展开的总规模 / Decide the total size of a nested unroll.
    /// </summary>
    /// <param name="tripCounts">从外到内的迭代次数 / The trip counts, outermost first.</param>
    /// <returns>判定结果；`TripCount` 是总展开次数 / The decision, whose TripCount is the total unroll count.</returns>
    /// <remarks>
    /// **每一层都在上限内、乘起来却超出上限，** **这是嵌套循环最容易漏的地方：** **单看每层都"合规"，** **而 256×256 已经是六万五千个节点——**
    /// **导入会"成功"，然后模型大到没人能看。**
    ///
    /// **Each level within the limit while the product exceeds it is what nested loops most easily slip past: every level looks compliant on its own, and 256 × 256 is
    /// already sixty-five thousand nodes — the import "succeeds" and the model becomes too large to inspect.**
    /// </remarks>
    public static Decision DecideNested(IReadOnlyList<long> tripCounts) {
        if (tripCounts.Count == 0) {
            return new Decision(null, 1);
        }
        long total = 1;
        for (int index = 0; index < tripCounts.Count; ++index) {
            long count = tripCounts[index];
            if (count < 0) {
                return new Decision("第 " + index + " 层的迭代次数为负 / a trip count is negative");
            }
            total *= count;
            if (total > MaxUnroll) {
                return new Decision(
                    "嵌套展开的总次数 " + total + " 超出上限 " + MaxUnroll
                    + "（第 " + index + " 层之后）/ the nested unroll exceeds the limit",
                    0);
            }
        }
        return new Decision(null, (int)total);
    }
}