#nullable enable

using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver;
using System;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 侧的固定整数 LP 后端工厂 / The Gurobi-side fixed-integer LP backend factory.
/// </summary>
/// <remarks>
/// **这是基线 `GurobiCriticalConstraintAnalysis.kt` 里 `gurobiFixedIntegerLpBackend` 的对应物。**
///
/// ### 它&lt;b&gt;不**自己实现求解，这是有意的&lt;/b&gt;
///
/// **基线原话：** **"实现复用与后端无关的 `LinearSolverFixedIntegerLpBackend`；Gurobi 只额外提供原生 ranging 钩子，
/// 因此两端（Gurobi/SCIP）共用同一条经测试的实现路径，不存在重复逻辑。"**
///
/// **所以这里就是三层的串联：**
/// **`LoweredFixedIntegerLpBackend`（先降阶）→ `LinearSolverFixedIntegerLpSolver`（求解并对偶映射）
/// → Gurobi 的线性求解器。**
///
/// **此前，这条链的中间一层不存在，两个插件因此都接不上；
/// 这里把 Gurobi 这一端接上了。**
///
/// ### 一处显式的"没有"
///
/// **基线允许"未提供 ranging 钩子时 `sensitivityRange` 保持为空——按计划红线，宁缺勿造"。**
///
/// **本工厂&lt;b&gt;不提供**该钩子：** **Gurobi 的 `SARHSLow`／`SARHSUp` 必须在&lt;b&gt;主求解过程**中、
/// 且关掉 presolve 才读得对（基线在同一文件里记了实测：
/// 二次求解时 presolve 配置不再生效，`SARHSLow` 会退化成哨兵值）。**
/// **在没做那件事之前，报告里的区间保持为 `null`——** **而不是拿一个"看起来像区间"的数填上。**
///
/// **This is the counterpart of `gurobiFixedIntegerLpBackend` in the baseline.** **The baseline says the implementation reuses
/// the backend-neutral `LinearSolverFixedIntegerLpBackend`, with Gurobi adding only a native ranging hook, so both backends share
/// one tested path with no duplicated logic.** **So this is a three-layer chain: lowering adapter → solver-and-dual-mapping →
/// Gurobi's linear solver.** **The middle layer did not exist at first, which is why neither plugin could connect; this change
/// connects Gurobi's end.**
///
/// **One explicit absence: the baseline allows the ranging hook to be omitted, leaving `sensitivityRange` empty — "better absent
/// than invented".** **This factory omits it: Gurobi's `SARHSLow`/`SARHSUp` are only correct when read during the
/// &lt;b&gt;primary&lt;/b&gt; solve with presolve disabled (the baseline records the measurement that a second solve no longer honours the
/// presolve configuration and returns sentinel values).** **Until that is done, ranges stay `null` rather than being filled with
/// something that merely looks like a range.**
/// </remarks>
public static class GurobiFixedIntegerLpBackend
{
    /// <summary>构造 Gurobi 的固定整数 LP 后端 / Build the Gurobi fixed-integer LP backend.</summary>
    /// <param name="linearSolver">Gurobi 线性求解器 / The Gurobi linear solver.</param>
    /// <returns>固定整数 LP 后端 / A fixed-integer LP backend.</returns>
    public static IFixedIntegerLpBackend Create(IAbstractLinearSolver linearSolver) {
        ArgumentNullException.ThrowIfNull(linearSolver);

        // **是 Gurobi 的求解器就走带范围的那条；别的求解器走共享路径但不带范围。**
        //
        // **这条分支不是"特殊照顾 Gurobi"，而是"范围只有 Gurobi 能读"**——
        // **求解与对偶仍然全部在共享层里，这一层只决定"要不要顺手把范围读出来"。**
        //
        // **A Gurobi solver takes the range-capturing variant; any other solver takes the shared path without ranges.** **This branch
        // is not favouritism: only Gurobi can read ranges at all, while solving and dual mapping still happen entirely in the shared
        // layer — this layer only decides whether to read ranges along the way.**
        if (linearSolver is GurobiLinearSolver gurobi) {
            gurobi.CaptureSensitivityRanges = true;
            return new LoweredFixedIntegerLpBackend(new LinearSolverFixedIntegerLpSolver(
                gurobi,
                sensitivityRanges: rowOrder => GurobiSensitivityRangeMapper.ToRanges(
                    gurobi.SensitivityRangesByRowIndex,
                    rowOrder)));
        }
        return new LoweredFixedIntegerLpBackend(new LinearSolverFixedIntegerLpSolver(linearSolver));
    }

    /// <summary>
    /// **本工厂是否提供原生 ranging 钩子** / Whether this factory supplies the native ranging hook.
    /// </summary>
    /// <remarks>
    /// **写成公开常量，而不是一句注释：** **调用方可以据此判断"报告里没有区间"是&lt;b&gt;本后端的已知边界**，
    /// 还是&lt;b&gt;某处坏了**。** **一个只在文档里说"没有区间"的后端，会让人反复怀疑自己用错了。**
    ///
    /// **An exposed constant rather than a comment: callers can tell whether "no ranges in the report" is this backend's known
    /// boundary or a breakage somewhere.** **A backend that only says so in prose leaves people wondering whether they used it
    /// wrongly.**
    /// </remarks>
    public const bool SuppliesSensitivityRanges = true;
}