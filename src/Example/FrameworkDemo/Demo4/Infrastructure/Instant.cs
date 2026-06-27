#nullable enable

using System;
using System.Globalization;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;

/// <summary>
/// DateTimeOffset 的短时间格式化扩展方法。
/// Extension methods for short time formatting of DateTimeOffset.
/// </summary>
public static class InstantExtensions {
    private const string ShortTimePattern = "MMddHHmm";

    /// <summary>
    /// 使用 "MMddHHmm" 模式将 DateTimeOffset 格式化为短字符串。
    /// Formats a DateTimeOffset as a short string using the "MMddHHmm" pattern.
    /// </summary>
    /// <param name="instant">时间点 / The instant to format.</param>
    /// <returns>格式化后的短字符串 / The formatted short string.</returns>
    public static string ToShortString(this DateTimeOffset instant)
        => instant.ToString(ShortTimePattern, CultureInfo.InvariantCulture);
}
