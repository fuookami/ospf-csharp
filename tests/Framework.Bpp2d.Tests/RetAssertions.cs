#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;
/// <summary>
/// 解包 Ret 结果的测试辅助方法
/// Test helper methods for unwrapping Result values.
/// </summary>
internal static class RetAssertions {
    /// <summary>
    /// 解包 Result 结果，失败时抛出 InvalidOperationException.
    /// Unwrap Result, throw InvalidOperationException on failure.
    /// </summary>
    public static T ValueOrFail<T>(this Result<T, ErrorCode, Error<ErrorCode>> result, string message = "result should succeed") {
        return result switch {
            Ok<T, ErrorCode, Error<ErrorCode>> ok => ok.Value,
            _ => throw new InvalidOperationException(message)
        };
    }

    /// <summary>
    /// 断言可空值非空，为空时抛出 InvalidOperationException.
    /// Assert nullable value is not null, throw InvalidOperationException when null.
    /// </summary>
    public static T OrFail<T>(this T? value, string message = "value should not be null")
        where T : class => value ?? throw new InvalidOperationException(message);

    /// <summary>
    /// 断言可空值类型非空，为空时抛出 InvalidOperationException.
    /// Assert nullable value type is not null, throw InvalidOperationException when null.
    /// </summary>
    public static T OrFail<T>(this T? value, string message = "value should not be null")
        where T : struct => value ?? throw new InvalidOperationException(message);
}
