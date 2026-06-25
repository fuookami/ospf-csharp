#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Solver.Remote.Domain
{
    /// <summary>
    /// 远程求解器错误码 / Remote solver error code.
    /// </summary>
    public enum RemoteSolverErrorCode
    {
        /// <summary>未知错误 / Unknown error</summary>
        Unknown = 0,
        /// <summary>连接失败 / Connection failed</summary>
        ConnectionFailed = 1,
        /// <summary>超时 / Timeout</summary>
        Timeout = 2,
        /// <summary>认证失败 / Authentication failed</summary>
        AuthenticationFailed = 3,
        /// <summary>求解未在最大轮次内完成 / Solve not completed within max rounds</summary>
        RemoteSolveNotCompletedWithinMaxRounds = 4,
        /// <summary>模型无效 / Invalid model</summary>
        InvalidModel = 5,
        /// <summary>资源不可用 / Resource unavailable</summary>
        ResourceUnavailable = 6,
        /// <summary>内部错误 / Internal error</summary>
        InternalError = 7
    }

    /// <summary>
    /// 远程求解器异常 / Remote solver exception.
    /// </summary>
    public sealed class RemoteSolverException : Exception
    {
        /// <summary>错误码 / Error code</summary>
        public RemoteSolverErrorCode ErrorCode { get; }

        public RemoteSolverException(RemoteSolverErrorCode errorCode, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// 远程求解器失败详情 / Remote solver failure detail.
    /// </summary>
    public sealed record RemoteSolverFailureDetail(
        RemoteSolverErrorCode Code,
        string Message,
        Dictionary<string, string>? Metadata = null,
        string? TaskId = null,
        string? SliceId = null);

    /// <summary>
    /// 远程求解器错误映射器 / Remote solver error mapper.
    /// </summary>
    public static class RemoteSolverErrorMapper
    {
        /// <summary>映射错误码到失败详情 / Map error code to failure detail.</summary>
        public static RemoteSolverFailureDetail Map(RemoteSolverErrorCode code, string? message = null)
        {
            return new RemoteSolverFailureDetail(
                Code: code,
                Message: message ?? code.ToString());
        }
    }
}
