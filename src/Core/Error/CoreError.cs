#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Core.Error
{
    /// <summary>
    /// 结构化核心错误接口 / Structured core error interface
    /// </summary>
    public interface IStructuredCoreError
    {
        /// <summary>错误码 / Error code</summary>
        ErrorCode ErrorCode { get; }
        /// <summary>错误消息 / Error message</summary>
        string Message { get; }

        /// <summary>转换为通用 Error / Convert to a generic Error</summary>
        Error<ErrorCode> ToError() => new Err<ErrorCode>(ErrorCode, Message);

        /// <summary>转换为失败的 Result / Convert to a failed Result</summary>
        Fuookami.Ospf.Utils.Functional.Result<T, ErrorCode, Error<ErrorCode>> ToFailed<T>() =>
            new Fuookami.Ospf.Utils.Functional.Failed<T, ErrorCode, Error<ErrorCode>>(ToError());
    }

    /// <summary>
    /// 核心错误密封基类 / Sealed base class for core errors
    /// </summary>
    /// <param name="ErrorCode">错误码 / Error code</param>
    /// <param name="Message">错误消息 / Error message</param>
    public abstract record CoreError(ErrorCode ErrorCode, string Message) : IStructuredCoreError
    {
        /// <summary>变量错误 / Variable error wrapper</summary>
        public sealed record Variable(VariableError Detail) : CoreError(Detail.ErrorCode, Detail.Message);

        /// <summary>模型错误 / Model error wrapper</summary>
        public sealed record Model(ModelError Detail) : CoreError(Detail.ErrorCode, Detail.Message);

        /// <summary>求解器错误 / Solver error wrapper</summary>
        public sealed record Solver(SolverError Detail) : CoreError(Detail.ErrorCode, Detail.Message);

        /// <summary>未实现 / Not implemented</summary>
        public sealed record NotImplemented(string Detail)
            : CoreError(ErrorCode.IllegalArgument, $"Not implemented: {Detail}");

        /// <summary>内部错误 / Internal error</summary>
        public sealed record Internal(string Detail)
            : CoreError(ErrorCode.ApplicationException, $"Internal error: {Detail}");
    }

    /// <summary>
    /// 变量相关错误密封基类 / Sealed base for variable-related errors
    /// </summary>
    public abstract record VariableError(ErrorCode ErrorCode, string Message) : IStructuredCoreError
    {
        /// <summary>变量未找到 / Variable not found</summary>
        public sealed record NotFound(string Variable)
            : VariableError(ErrorCode.DataNotFound, $"Variable not found: {Variable}");

        /// <summary>变量已存在 / Variable already exists</summary>
        public sealed record AlreadyExists(string Variable)
            : VariableError(ErrorCode.TokenExisted, $"Variable already exists: {Variable}");

        /// <summary>变量范围无效 / Invalid variable range</summary>
        public sealed record InvalidRange(string? Lower, string? Upper)
            : VariableError(ErrorCode.IllegalArgument, $"Invalid variable range: lower bound {Lower} > upper bound {Upper}");

        /// <summary>变量值无效 / Invalid variable value</summary>
        public sealed record InvalidValue(string Variable, string Value)
            : VariableError(ErrorCode.IllegalArgument, $"Invalid variable value: {Value} for variable {Variable}");

        /// <summary>变量名冲突 / Variable name conflict</summary>
        public sealed record NameConflict(string Name)
            : VariableError(ErrorCode.SymbolRepetitive, $"Variable name conflict: {Name}");
    }

    /// <summary>
    /// 模型相关错误密封基类 / Sealed base for model-related errors
    /// </summary>
    public abstract record ModelError(ErrorCode ErrorCode, string Message) : IStructuredCoreError
    {
        /// <summary>模型未初始化 / Model not initialized</summary>
        public sealed record NotInitialized()
            : ModelError(ErrorCode.ApplicationError, "Model not initialized");

        /// <summary>模型已求解 / Model already solved</summary>
        public sealed record AlreadySolved()
            : ModelError(ErrorCode.ApplicationError, "Model already solved");

        /// <summary>约束冲突 / Constraint conflict</summary>
        public sealed record ConstraintConflict(string Detail)
            : ModelError(ErrorCode.IllegalArgument, $"Constraint conflict: {Detail}");

        /// <summary>缺少目标函数 / Missing objective function</summary>
        public sealed record MissingObjective()
            : ModelError(ErrorCode.DataEmpty, "Missing objective function");

        /// <summary>无效约束 / Invalid constraint</summary>
        public sealed record InvalidConstraint(string Detail)
            : ModelError(ErrorCode.IllegalArgument, $"Invalid constraint: {Detail}");

        /// <summary>符号未注册 / Symbol not registered</summary>
        public sealed record SymbolNotRegistered(string Symbol)
            : ModelError(ErrorCode.DataNotFound, $"Symbol not registered: {Symbol}");
    }

    /// <summary>
    /// 求解器相关错误密封基类 / Sealed base for solver-related errors
    /// </summary>
    public abstract record SolverError(ErrorCode ErrorCode, string Message) : IStructuredCoreError
    {
        /// <summary>求解器不可用 / Solver not available</summary>
        public sealed record NotAvailable(string Solver)
            : SolverError(ErrorCode.SolverNotFound, $"Solver not available: {Solver}");

        /// <summary>求解失败 / Solve failed</summary>
        public sealed record SolveFailed(string Detail)
            : SolverError(ErrorCode.OREngineSolvingException, $"Solve failed: {Detail}");

        /// <summary>无解 / No solution found</summary>
        public sealed record NoSolution()
            : SolverError(ErrorCode.ORSolutionInvalid, "No solution found");

        /// <summary>无界 / Solution is unbounded</summary>
        public sealed record Unbounded()
            : SolverError(ErrorCode.ORModelUnbounded, "Solution is unbounded");

        /// <summary>不可行 / Problem is infeasible</summary>
        public sealed record Infeasible()
            : SolverError(ErrorCode.ORModelInfeasible, "Problem is infeasible");

        /// <summary>数值错误 / Numerical error</summary>
        public sealed record NumericalError(string Detail)
            : SolverError(ErrorCode.OREngineSolvingException, $"Numerical error: {Detail}");

        /// <summary>精度损失 / Precision loss</summary>
        public sealed record PrecisionLoss(string Detail)
            : SolverError(ErrorCode.ORSolutionInvalid, $"Precision loss: {Detail}");

        /// <summary>溢出 / Overflow</summary>
        public sealed record Overflow(string Detail)
            : SolverError(ErrorCode.ORSolutionInvalid, $"Overflow: {Detail}");

        /// <summary>非有限值 / Non-finite value</summary>
        public sealed record NonFinite(string Detail)
            : SolverError(ErrorCode.ORSolutionInvalid, $"Non-finite value: {Detail}");

        /// <summary>不支持的值类型 / Unsupported value type</summary>
        public sealed record UnsupportedValueType(string Type)
            : SolverError(ErrorCode.IllegalArgument, $"Unsupported value type: {Type}");

        /// <summary>求解器超时 / Solver timeout</summary>
        public sealed record Timeout(TimeSpan Duration)
            : SolverError(ErrorCode.OREngineTerminated, $"Solver timeout after {Duration}");

        /// <summary>许可证错误 / License error</summary>
        public sealed record LicenseError(string Detail)
            : SolverError(ErrorCode.AuthenticationError, $"License error: {Detail}");
    }

    /// <summary>求解器未找到错误 / Solver not found error</summary>
    public sealed record SolverNotFoundError(string? solver = null)
        : Err<ErrorCode>(ErrorCode.SolverNotFound, solver is not null ? $"No solver valid: {solver}" : "No solver valid.")
    {
        /// <summary>求解器名称 / Solver name</summary>
        public string? Solver => solver;
    }

    /// <summary>求解器环境丢失错误 / Solver environment lost error</summary>
    public sealed record SolverEnvironmentLostError(string? detail = null)
        : Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, detail ?? "Solver environment lost.")
    {
        /// <summary>详细信息 / Detail</summary>
        public string? Detail => detail;
    }

    /// <summary>求解器求解错误 / Solver solving error</summary>
    public sealed record SolverSolvingError(string? detail = null)
        : Err<ErrorCode>(ErrorCode.OREngineSolvingException, detail ?? "Solver solving exception.")
    {
        /// <summary>详细信息 / Detail</summary>
        public string? Detail => detail;
    }

    /// <summary>求解器建模错误 / Solver modeling error</summary>
    public sealed record SolverModelingError(string? detail = null)
        : Err<ErrorCode>(ErrorCode.OREngineModelingException, detail ?? "Solver modeling exception.")
    {
        /// <summary>详细信息 / Detail</summary>
        public string? Detail => detail;
    }

    /// <summary>求解器终止错误 / Solver terminated error</summary>
    public sealed record SolverTerminatedError()
        : Err<ErrorCode>(ErrorCode.OREngineTerminated, "Solver terminated.");

    /// <summary>
    /// 核心错误扩展方法 / Core error extension methods
    /// </summary>
    public static class CoreErrorExtensions
    {
        /// <summary>变量错误转核心错误 / Convert variable error to core error</summary>
        public static CoreError AsCoreError(this VariableError e) => new CoreError.Variable(e);

        /// <summary>模型错误转核心错误 / Convert model error to core error</summary>
        public static CoreError AsCoreError(this ModelError e) => new CoreError.Model(e);

        /// <summary>求解器错误转核心错误 / Convert solver error to core error</summary>
        public static CoreError AsCoreError(this SolverError e) => new CoreError.Solver(e);
    }
}
