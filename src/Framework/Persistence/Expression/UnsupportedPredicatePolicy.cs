#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Persistence.Expression;

/// <summary>
/// 不支持的谓词策略 / Unsupported predicate policy.
/// </summary>
public enum UnsupportedPredicatePolicy {
    /// <summary>快速失败 / Fail fast</summary>
    FailFast,
    /// <summary>始终返回 false / Always return false</summary>
    AlwaysFalse,
    /// <summary>客户端过滤 / Client-side filter</summary>
    ClientFilter
}

/// <summary>
/// 谓词翻译结果（密封）/ Predicate translation result (sealed).
/// </summary>
public abstract record PredicateTranslation<T> where T : class {
    /// <summary>成功翻译 / Successful translation.</summary>
    public sealed record Success : PredicateTranslation<T> {
        public object? TranslatedPredicate { get; init; }
    }

    /// <summary>不支持的谓词 / Unsupported predicate.</summary>
    public sealed record Unsupported : PredicateTranslation<T> {
        public IReadOnlyList<UnsupportedPredicateDetail> Details { get; init; } = [];
    }
}

/// <summary>
/// 不支持的谓词详情 / Unsupported predicate detail.
/// </summary>
/// <remarks>
/// 与基线 `UnsupportedPredicateDetail` 对齐。**策略决定错误码，不是决定错误消息**：
/// `FailFast` → `IllegalArgument`（调用方写错了谓词）、`ClientFilter` → `ApplicationFailed`（后端做不到，但换个后端可以）、
/// `AlwaysFalse` → `ApplicationError`（后端做不到，且已按"恒假"降级）。
/// 三个码分别指向三种不同的处置，合并成一个码会让调用方无从判断该改代码还是该换后端。
///
/// Aligned with its `UnsupportedPredicateDetail`. **The policy decides the error code, not just the message**: `FailFast` gives `IllegalArgument` (the caller wrote a predicate the backend
/// cannot take), `ClientFilter` gives `ApplicationFailed` (this backend cannot, another can) and `AlwaysFalse` gives `ApplicationError` (this backend cannot and has degraded to a constant false).
/// Three codes point at three different reactions, and merging them would leave the caller unable to tell whether to change the code or the backend.
/// </remarks>
public sealed record UnsupportedPredicateDetail(
    string Predicate,
    string Reason,
    UnsupportedPredicatePolicy Policy,
    string Backend = "") {
    /// <summary>构造一条"恒假"详情 / Build an always-false detail.</summary>
    /// <param name="predicate">谓词类型名 / The predicate type name.</param>
    /// <param name="reason">原因 / The reason.</param>
    /// <param name="backend">后端名 / The backend name.</param>
    /// <returns>详情 / The detail.</returns>
    public static UnsupportedPredicateDetail AlwaysFalse(string predicate, string reason, string backend) =>
        new(predicate, reason, UnsupportedPredicatePolicy.AlwaysFalse, backend);

    /// <summary>构造一条"快速失败"详情 / Build a fail-fast detail.</summary>
    /// <param name="predicate">谓词类型名 / The predicate type name.</param>
    /// <param name="reason">原因 / The reason.</param>
    /// <param name="backend">后端名 / The backend name.</param>
    /// <returns>详情 / The detail.</returns>
    public static UnsupportedPredicateDetail FailFast(string predicate, string reason, string backend) =>
        new(predicate, reason, UnsupportedPredicatePolicy.FailFast, backend);

    /// <summary>构造一条"客户端过滤"详情 / Build a client-filter detail.</summary>
    /// <param name="predicate">谓词类型名 / The predicate type name.</param>
    /// <param name="reason">原因 / The reason.</param>
    /// <param name="backend">后端名 / The backend name.</param>
    /// <returns>详情 / The detail.</returns>
    public static UnsupportedPredicateDetail ClientFilter(string predicate, string reason, string backend) =>
        new(predicate, reason, UnsupportedPredicatePolicy.ClientFilter, backend);

    /// <summary>
    /// 转成错误 / Turn into an error.
    /// </summary>
    /// <returns>错误 / The error.</returns>
    public Error<ErrorCode> ToError() => Policy switch {
        UnsupportedPredicatePolicy.FailFast => new ExErr<ErrorCode, UnsupportedPredicateDetail>(
            ErrorCode.IllegalArgument,
            $"Unsupported predicate [{Predicate}]: {Reason} (backend={Backend}, policy=FailFast)",
            this),
        UnsupportedPredicatePolicy.ClientFilter => new ExErr<ErrorCode, UnsupportedPredicateDetail>(
            ErrorCode.ApplicationFailed,
            $"Unsupported predicate [{Predicate}]: {Reason} (backend={Backend}, policy=ClientFilter)",
            this),
        _ => new ExErr<ErrorCode, UnsupportedPredicateDetail>(
            ErrorCode.ApplicationError,
            $"Unsupported predicate [{Predicate}]: {Reason} (backend={Backend}, policy=AlwaysFalse)",
            this),
    };
}

/// <summary>
/// 谓词翻译的失败传播与降级 / Failure propagation and degradation for predicate translation.
/// </summary>
/// <remarks>
/// 与基线 `propagateFailure` / `withAlwaysFalseUnsupported` 对齐。**`AlwaysFalse` 是唯一会被吞掉的策略**：
/// 调用方显式声明"这个谓词后端做不到时按恒假处理"，其余两种必须原样传播——
/// 把 `FailFast` 也降级成恒假会让"写错了谓词"变成"查询结果为空"，最难查的一种。
///
/// Aligned with its `propagateFailure` / `withAlwaysFalseUnsupported`. **`AlwaysFalse` is the only policy that gets swallowed**: the caller has explicitly said "treat this predicate as
/// constant false when the backend cannot take it", while the other two must propagate untouched — degrading `FailFast` to constant false would turn "the predicate was written wrongly" into "the
/// query returned nothing", the hardest kind to diagnose.
/// </remarks>
public static class PredicateTranslationExtensions {
    /// <summary>
    /// 传播失败 / Propagate a failure.
    /// </summary>
    /// <typeparam name="U">目标值类型 / The target value type.</typeparam>
    /// <param name="result">源结果 / The source result.</param>
    /// <returns>失败副本，成功时为 null / A failed copy, null on success.</returns>
    public static Result<U, ErrorCode, Error<ErrorCode>>? PropagateFailure<U>(
        this Result<object?, ErrorCode, Error<ErrorCode>> result) {
        ArgumentNullException.ThrowIfNull(result);
        return result switch {
            Failed<object?, ErrorCode, Error<ErrorCode>> failed => Results.Failed<U>(failed.Error),
            Fatal<object?, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<U, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => null,
        };
    }

    /// <summary>
    /// 按 `AlwaysFalse` 降级 / Degrade according to `AlwaysFalse`.
    /// </summary>
    /// <typeparam name="U">目标值类型 / The target value type.</typeparam>
    /// <param name="result">源结果 / The source result.</param>
    /// <param name="createFalse">恒假值的构造 / Builds the constant-false value.</param>
    /// <returns>原结果，或降级后的成功 / The original result, or a degraded success.</returns>
    public static Result<U, ErrorCode, Error<ErrorCode>> WithAlwaysFalseUnsupported<U>(
        this Result<U, ErrorCode, Error<ErrorCode>> result,
        Func<U> createFalse) {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(createFalse);

        if (result is Failed<U, ErrorCode, Error<ErrorCode>> failed
            && failed.Error.Value is UnsupportedPredicateDetail detail
            && detail.Policy == UnsupportedPredicatePolicy.AlwaysFalse) {
            return Results.Ok(createFalse());
        }
        return result;
    }
}
