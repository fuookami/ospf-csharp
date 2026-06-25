#nullable enable

using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Utils.Functional;
/// <summary>
/// 无返回值的结果类型工厂 / Result type factory with no meaningful return value.
/// </summary>
public static class TryFactory {
    private static readonly Success _success = new();

    public static Result<Success, ErrorCode, Error<ErrorCode>> Create() =>
        new Ok<Success, ErrorCode, Error<ErrorCode>>(_success);
}

/// <summary>
/// 带返回值的结果类型工厂 / Result type factory with a meaningful return value.
/// </summary>
public static class RetFactory {
    public static Result<T, ErrorCode, Error<ErrorCode>> Create<T>(T value) =>
        new Ok<T, ErrorCode, Error<ErrorCode>>(value);

    public static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(Error<ErrorCode> error) =>
        new Failed<T, ErrorCode, Error<ErrorCode>>(error);
}
