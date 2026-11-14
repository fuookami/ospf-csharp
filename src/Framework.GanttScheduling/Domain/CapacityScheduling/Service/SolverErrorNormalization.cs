// 求解器错误规范化 / Solver error normalization.
// 与固定 Kotlin 基线 `gantt-scheduling-domain-capacity-scheduling-context/.../SlotBasedCapacityPreSolver.kt`
// 里的 `errorCodeErrorOf` / `errorCodeErrorsOf` 对应。
// Mirrors `errorCodeErrorOf` / `errorCodeErrorsOf` in the pinned Kotlin baseline's `SlotBasedCapacityPreSolver.kt`.

using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service;

/// <summary>
/// 求解器错误规范化 / Solver error normalization.
/// </summary>
/// <remarks>
/// **预求解链路约定求解器只返回 `ErrorCode` 错误**，而求解器适配层面对的是通配 `Error&lt;C&gt;`。
/// 一个非 `ErrorCode` 的编码（比如某个后端自己的 `SolverErrorCode`）不能直接塞进这条链路：
/// 下游按 `ErrorCode` 分支（可重试、不可行、超时），而未知枚举值会走到 `default` 分支，
/// 于是"求解器超时"被当成"未知错误"处理，重试策略整体失效。
///
/// 所以这里把非 `ErrorCode` 的编码**降级为 `Unknown`，同时保留原始编码与消息**：
/// 降级是给下游一个能分支的值，保留是为了让人还能查到真正发生了什么。**只做前者就成了信息黑洞**。
///
/// **The pre-solve pipeline requires the solver to return `ErrorCode` errors**, while the solver adapters deal in the generic `Error&lt;C&gt;`. A non-`ErrorCode` code (a
/// backend's own `SolverErrorCode`, say) cannot simply be pushed through: downstream branches on `ErrorCode` (retryable, infeasible, timeout) and an unknown enum value
/// falls into `default`, so "the solver timed out" gets treated as "unknown error" and the retry policy stops working entirely.
///
/// So a non-`ErrorCode` code is **degraded to `Unknown` while the original code and message are preserved**: the degradation gives downstream something to branch on, the
/// preservation keeps it possible to find out what actually happened. **Doing only the former makes an information black hole.**
/// </remarks>
public static class SolverErrorNormalization {
    /// <summary>
    /// 把通配错误规范化为 `ErrorCode` 错误 / Normalize a generic error into an `ErrorCode` error.
    /// </summary>
    /// <typeparam name="C">原始编码类型 / The original code type.</typeparam>
    /// <param name="error">原始错误 / The original error.</param>
    /// <returns>规范化后的错误 / The normalized error.</returns>
    /// <remarks>
    /// **关联值要跟着走**：`WithValue` 为真时返回 `ExErr`，否则返回 `Err`。若一律返回 `Err`，
    /// 一个携带模型片段的求解器错误会在这一层丢掉那段片段，而它往往正是定位问题的唯一线索。
    ///
    /// **The associated value travels with it**: when `WithValue` is true an `ExErr` is returned, otherwise an `Err`. Returning `Err` unconditionally would drop the model
    /// fragment an error carries at exactly this layer, and that fragment is often the only clue for locating the problem.
    /// </remarks>
    public static Error<ErrorCode> Normalize<C>(Error<C> error) where C : notnull {
        System.ArgumentNullException.ThrowIfNull(error);

        if (error.Code is ErrorCode code) {
            return error.WithValue
                ? new ExErr<ErrorCode, object?>(code, error.Message, error.Value)
                : new Err<ErrorCode>(code, error.Message);
        }

        return new ExErr<ErrorCode, object?>(
            ErrorCode.Unknown,
            "slot_based_capacity_presolver.solve received non-ErrorCode solver error.",
            error.ToString());
    }

    /// <summary>
    /// 规范化一列通配错误 / Normalize a list of generic errors.
    /// </summary>
    /// <typeparam name="C">原始编码类型 / The original code type.</typeparam>
    /// <param name="errors">原始错误列表 / The list of original errors.</param>
    /// <returns>规范化后的错误列表 / The list of normalized errors.</returns>
    /// <remarks>
    /// **空列表返回空列表，而不是 null**：`Fatal` 分支的调用方会直接把它交给上层的 `Fatal` 构造，
    /// 一个 null 会在那里变成 `NullReferenceException`——把一个求解失败升级成崩溃。
    ///
    /// **An empty list returns an empty list, not null**: the `Fatal` branch's callers hand it straight to the outer `Fatal` constructor, where a null becomes a
    /// `NullReferenceException` — upgrading a solve failure into a crash.
    /// </remarks>
    public static IReadOnlyList<Error<ErrorCode>> NormalizeAll<C>(IReadOnlyList<Error<C>> errors) where C : notnull {
        System.ArgumentNullException.ThrowIfNull(errors);

        var normalized = new List<Error<ErrorCode>>(errors.Count);
        foreach (Error<C> error in errors) {
            normalized.Add(Normalize(error));
        }
        return normalized;
    }
}
