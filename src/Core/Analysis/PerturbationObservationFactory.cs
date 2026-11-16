#nullable enable

using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 扰动观察的构造器 / Constructors for perturbation observations.
/// </summary>
/// <remarks>
/// **这是 的 `unknownObservation` 的实现。**
///
/// 它写成"只填三个字段、其余全默认"的工厂：
///
/// | 字段 | 值 | 为什么 |
/// | --- | --- | --- |
/// | `Status` | **`Unknown`** | 没跑成，不是"不支持" |
/// | `Outcome` | **`Unknown`** | 同上 |
/// | `PerturbedRhs` / 两个目标值 / `SolveTime` | **全 `null`** | **没测过就是没测过** |
///
/// **`Status` 与 `Outcome` 必须同时是 `Unknown`。** 只改一个会造出一种自相矛盾的观察：
/// 状态说"不知道"，结果说"没效果"——**而"没效果"是一个结论。**
///
/// **`Status` and `Outcome` must both be `Unknown`.** Changing only one produces a self-contradictory observation: the status says "unknown" while the outcome says "no effect" — **and "no effect" is
/// a conclusion.**
///
/// **This implementation writes this as a factory filling three fields and defaulting the rest:**
/// </remarks>
public static class PerturbationObservationFactory {
    /// <summary>
    /// 构造一条"未知"观察 / Build an unknown observation.
    /// </summary>
    /// <param name="constraintId">约束身份 / The constraint identity.</param>
    /// <param name="kind">扰动种类 / The perturbation kind.</param>
    /// <param name="message">说明 / The explanation.</param>
    /// <param name="delta">扰动步长；未指定时为 <c>null</c> / The delta, or <c>null</c>.</param>
    /// <returns>一条未知观察 / An unknown observation.</returns>
    public static ConstraintPerturbationObservation Unknown(
        ConstraintId constraintId,
        ConstraintPerturbationKind kind,
        string message,
        Flt64? delta = null) =>
        new(
            ConstraintId: constraintId,
            Kind: kind,
            Delta: delta,
            // **没测过就是没测过**：右端项与目标值一律留空，而不是填 0。
            // **Not measured stays not measured**: the right-hand side and the objectives are left empty, never zero.
            PerturbedRhs: null,
            Status: AnalysisStatus.Unknown,
            BaselineObjective: null,
            NewObjective: null,
            ObjectiveImprovement: null,
            IntegerPatternChanged: null,
            SolveTime: null,
            Outcome: PerturbationOutcome.Unknown,
            Message: message);
}
