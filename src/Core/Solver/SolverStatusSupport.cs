#nullable enable

using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 求解器状态支持扩展方法，提供状态到错误码的映射和失败结果构建。
/// Solver status support extension methods, providing status-to-error-code mapping and failure result construction.
/// </summary>
public static class SolverStatusSupportExtensions {
    /// <summary>
    /// 将求解器状态解析为错误码，使用现有的 ErrCode() 扩展方法，若为空则回退到指定错误码或 OREngineSolvingException。
    /// Resolve solver status to error code using the existing ErrCode() extension, falling back to the specified code or OREngineSolvingException.
    /// </summary>
    /// <param name="status">求解器状态 / Solver status</param>
    /// <param name="fallback">回退错误码（可选）/ Fallback error code (optional)</param>
    /// <returns>解析后的错误码 / Resolved error code</returns>
    public static ErrorCode ResolveErrCode(this SolverStatus status, ErrorCode? fallback = null) {
        return status.ErrCode() ?? fallback ?? ErrorCode.OREngineSolvingException;
    }

    /// <summary>
    /// 根据求解器状态返回失败的 Try 结果。
    /// Return a failed Try result based on the solver status.
    /// </summary>
    /// <param name="status">求解器状态 / Solver status</param>
    /// <param name="fallback">回退错误码（可选）/ Fallback error code (optional)</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try FailByStatus(this SolverStatus status, ErrorCode? fallback = null) {
        ErrorCode code = status.ResolveErrCode(fallback);
        return Results.Failed<Success>(new Err<ErrorCode>(code, code.ToReadableString()));
    }

    /// <summary>
    /// 检查回调结果是否失败，若失败则执行中止操作并返回 true。
    /// Check whether the callback result failed; if so, execute the abort action and return true.
    /// </summary>
    /// <param name="callbackResult">回调结果（可选）/ Callback result (optional)</param>
    /// <param name="abort">中止操作 / Abort action</param>
    /// <returns>是否应中止 / Whether should abort</returns>
    public static bool ShouldAbortOnCallbackFailure(Try? callbackResult, Action abort) {
        if (callbackResult is Failed<Success, ErrorCode, Error<ErrorCode>>) {
            abort();
            return true;
        }
        return false;
    }
}
