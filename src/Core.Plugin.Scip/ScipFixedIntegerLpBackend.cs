#nullable enable

using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver;
using System;

namespace Fuookami.Ospf.Core.Plugin.Scip;

/// <summary>
/// SCIP 侧的固定整数 LP 后端工厂 / The SCIP-side fixed-integer LP backend factory.
/// </summary>
/// <remarks>
/// **这是里 `scipFixedIntegerLpBackend` 的对应物。**
///
/// ### 与 Gurobi 那一端&lt;b&gt;共用同一条路径**
///
/// **SCIP 文件里有一个自己的 `ScipFixedIntegerLpBackend` 类，把"降阶 → dump → 求解 → 取对偶"
/// 又写了一遍。**
///
/// **本仓不沿用那个重复：** **这条链已经抽成了一层与后端无关的实现
/// （`LinearSolverFixedIntegerLpSolver`），而 `ScipLinearSolver` 与 `GurobiLinearSolver`
/// 都实现同一个 `IAbstractLinearSolver`。**
///
/// **所以两个插件工厂现在&lt;b&gt;是同一句话，只换了求解器**——
/// **这不是"少写了代码"，而是把那份重复消掉了，
/// 并且让"两个后端行为一致"变成结构性事实，而不是靠人守。**
///
/// ### 两处显式的"没有"
///
/// - **`SuppliesSensitivityRanges = false`** —— **区间需要后端原生的 ranging 能力，SCIP 这一端尚未做。**
/// - **`SuppliesPerturbationBackend = false`** —— **扰动后端（`scipPerturbationBackend`）
///   尚未实现；它要的是"改边界后重解"的编排，而那条编排目前在 `ConstraintPerturbationPipeline` 里
///   只对自带该能力的后端开放。**
///
/// **两者都写成公开常量而不是注释，理由与 Gurobi 那一批相同：**
/// **注释只能被读，常量能被判断。**
///
/// **This is the counterpart of `scipFixedIntegerLpBackend`.** **The code's SCIP file carries its own
/// `ScipFixedIntegerLpBackend` class which writes the "lower → dump → solve → duals" chain a second time.** **This repo does not
/// copy that duplication: the chain is already extracted into a backend-neutral layer, and `ScipLinearSolver` and
/// `GurobiLinearSolver` implement the same `IAbstractLinearSolver`.** **So the two plugin factories are now &lt;b&gt;the same
/// sentence with a different solver&lt;/b&gt; — not less code for its own sake, but its duplication removed, which turns
/// "both backends behave the same" into a structural fact instead of something a person must maintain.**
///
/// **Two stated absences: sensitivity ranges need a backend's native ranging capability, not done for SCIP; and the
/// perturbation backend is not implemented, because it needs "change a bound and re-solve" orchestration that
/// `ConstraintPerturbationPipeline` currently opens only to backends that bring the capability.** **Both are public constants
/// rather than comments, for the same reason as the Gurobi batch: comments are read, constants are judged.**
/// </remarks>
public static class ScipFixedIntegerLpBackend
{
    /// <summary>构造 SCIP 的固定整数 LP 后端 / Build the SCIP fixed-integer LP backend.</summary>
    /// <param name="linearSolver">SCIP 线性求解器 / The SCIP linear solver.</param>
    /// <returns>固定整数 LP 后端 / A fixed-integer LP backend.</returns>
    public static IFixedIntegerLpBackend Create(IAbstractLinearSolver linearSolver) {
        ArgumentNullException.ThrowIfNull(linearSolver);
        return new LoweredFixedIntegerLpBackend(
            new LinearSolverFixedIntegerLpSolver(linearSolver));
    }

    /// <summary>
    /// **是否提供原生区间（为何是 false）** / Whether native ranges are supplied, and why not.
    /// </summary>
    /// <remarks>
    /// **差的东西很具体：** **`ScipNative.cs` 一次导入 53 个 SCIP 函数，其中&lt;b&gt;没有任何一个&lt;/b&gt;与 RHS 区间、灵敏度或最优基有关。**
    ///
    /// **SCIP 也没有 Gurobi 那种 `SARHSLow`／`SARHSUp`：** **它的等价做法是"把 RHS 挪一点再解一次"——而那正是 `ConstraintPerturbationPipeline` 已经在做的事。**
    ///
    /// **所以这一端不是"少一块实现"，而是"换一条路走，而那条路已经存在"。**
    /// **判据：`ScipRangingAbsenceTests`。**
    ///
    /// **What is missing is specific: the 53 imported SCIP functions contain none for RHS ranges, sensitivity or an optimal
    /// basis, and SCIP has no `SARHSLow`/`SARHSUp` equivalent — its substitute is re-solving with a displaced RHS, which
    /// `ConstraintPerturbationPipeline` already does.** **So this is not "one implementation short" but "there is another road, and
    /// it already exists".** **Criterion: `ScipRangingAbsenceTests`.**
    /// </remarks>
    public const bool SuppliesSensitivityRanges = false;

    /// <summary>**是否提供扰动后端** / Whether a perturbation backend is supplied.</summary>
    public const bool SuppliesPerturbationBackend = false;
}