// 结果取值小工具 / A result-unwrapping helper.
// 放在**根命名空间**，让各子命名空间的判据文件都能直接用。
// Lives in the **root namespace** so every test file in its sub-namespaces can use it.

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Tests;

/// <summary>结果取值小工具 / A small result-unwrapping helper.</summary>
internal static class ResultUnwrap {
    /// <summary>取成功值，失败时抛 / Take the success value, throwing on failure.</summary>
    /// <typeparam name="T">成功类型 / Success type.</typeparam>
    /// <param name="result">结果 / The result.</param>
    /// <returns>成功值 / The success value.</returns>
    public static T ValueOrThrow<T>(this Result<T, ErrorCode, Error<ErrorCode>> result)
        where T : notnull =>
        result is Ok<T, ErrorCode, Error<ErrorCode>> ok
            ? ok.Value
            : throw new System.InvalidOperationException(result.ToString());
}
