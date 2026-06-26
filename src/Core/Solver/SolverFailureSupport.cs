#nullable enable

using Fuookami.Ospf.Core.Error;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 求解器失败工厂，提供各类求解器异常和错误的统一构造方法。
/// Solver failure factory, providing unified construction methods for various solver exceptions and errors.
/// </summary>
public static class SolverFailureFactory {
    /// <summary>
    /// 安全执行创建环境回调，捕获异常并返回失败结果。
    /// Safely execute the creating-environment callback, catching exceptions and returning a failure result.
    /// </summary>
    /// <typeparam name="T">目标类型 / Target type</typeparam>
    /// <param name="target">回调目标 / Callback target</param>
    /// <param name="callBack">回调函数（可选）/ Callback function (optional)</param>
    /// <returns>操作结果 / Operation result</returns>
    public static Try ExecuteCreatingEnvironmentCallback<T>(T target, Func<T, Try>? callBack) {
        if (callBack is null) {
            return Results.Ok(Results.SuccessInstance);
        }

        try {
            return callBack(target);
        }
        catch (Exception ex) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, $"Creating environment callback failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// 创建环境丢失的失败结果。
    /// Create a failure result for environment lost.
    /// </summary>
    /// <param name="message">错误消息（可选）/ Error message (optional)</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try EnvironmentLost(string? message = null) {
        return Results.Failed<Success>(
            new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, message ?? "Environment lost."));
    }

    /// <summary>
    /// 使用求解器错误详情创建环境丢失的失败结果。
    /// Create a failure result for environment lost with solver error detail.
    /// </summary>
    /// <param name="detail">求解器错误详情 / Solver error detail</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try EnvironmentLost(SolverError detail) {
        return Results.Failed<Success>(new Err<ErrorCode>(detail.ErrorCode, detail.Message));
    }

    /// <summary>
    /// 创建求解异常的失败结果。
    /// Create a failure result for solving exception.
    /// </summary>
    /// <param name="message">错误消息（可选）/ Error message (optional)</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try SolvingException(string? message = null) {
        return Results.Failed<Success>(
            new Err<ErrorCode>(ErrorCode.OREngineSolvingException, message ?? "Solving exception."));
    }

    /// <summary>
    /// 使用求解器错误详情创建求解异常的失败结果。
    /// Create a failure result for solving exception with solver error detail.
    /// </summary>
    /// <param name="detail">求解器错误详情 / Solver error detail</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try SolvingException(SolverError detail) {
        return Results.Failed<Success>(new Err<ErrorCode>(detail.ErrorCode, detail.Message));
    }

    /// <summary>
    /// 创建建模异常的失败结果。
    /// Create a failure result for modeling exception.
    /// </summary>
    /// <param name="message">错误消息（可选）/ Error message (optional)</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try ModelingException(string? message = null) {
        return Results.Failed<Success>(
            new Err<ErrorCode>(ErrorCode.OREngineModelingException, message ?? "Modeling exception."));
    }

    /// <summary>
    /// 使用求解器错误详情创建建模异常的失败结果。
    /// Create a failure result for modeling exception with solver error detail.
    /// </summary>
    /// <param name="detail">求解器错误详情 / Solver error detail</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try ModelingException(SolverError detail) {
        return Results.Failed<Success>(new Err<ErrorCode>(detail.ErrorCode, detail.Message));
    }

    /// <summary>
    /// 创建求解器终止的失败结果。
    /// Create a failure result for solver terminated.
    /// </summary>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try Terminated() {
        return Results.Failed<Success>(
            new Err<ErrorCode>(ErrorCode.OREngineTerminated, "Solver terminated."));
    }

    /// <summary>
    /// 使用求解器错误详情创建求解器终止的失败结果。
    /// Create a failure result for solver terminated with solver error detail.
    /// </summary>
    /// <param name="detail">求解器错误详情 / Solver error detail</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try Terminated(SolverError detail) {
        return Results.Failed<Success>(new Err<ErrorCode>(detail.ErrorCode, detail.Message));
    }

    /// <summary>
    /// 创建求解器未找到的失败结果。
    /// Create a failure result for solver not found.
    /// </summary>
    /// <param name="solver">求解器名称（可选）/ Solver name (optional)</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try SolverNotFound(string? solver = null) {
        return Results.Failed<Success>(new SolverNotFoundError(solver));
    }

    /// <summary>
    /// 创建求解器环境丢失的失败结果。
    /// Create a failure result for solver environment lost.
    /// </summary>
    /// <param name="detail">详细信息（可选）/ Detail (optional)</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try SolverEnvironmentLost(string? detail = null) {
        return Results.Failed<Success>(new SolverEnvironmentLostError(detail));
    }

    /// <summary>
    /// 创建求解器求解异常的失败结果。
    /// Create a failure result for solver solving exception.
    /// </summary>
    /// <param name="detail">详细信息（可选）/ Detail (optional)</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try SolverSolvingException(string? detail = null) {
        return Results.Failed<Success>(new SolverSolvingError(detail));
    }

    /// <summary>
    /// 创建求解器建模异常的失败结果。
    /// Create a failure result for solver modeling exception.
    /// </summary>
    /// <param name="detail">详细信息（可选）/ Detail (optional)</param>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try SolverModelingException(string? detail = null) {
        return Results.Failed<Success>(new SolverModelingError(detail));
    }

    /// <summary>
    /// 创建求解器终止的失败结果。
    /// Create a failure result for solver terminated.
    /// </summary>
    /// <returns>失败的 Try 结果 / Failed Try result</returns>
    public static Try SolverTerminated() {
        return Results.Failed<Success>(new SolverTerminatedError());
    }
}
