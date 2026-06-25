#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Utils;
/// <summary>时间扩展方法 / Time extension methods (mirrors ospf-kotlin Time.kt).</summary>
public static class TimeExtensions {
    /// <summary>取最大日期时间 / Max date-time.</summary>
    public static DateTimeOffset Max(DateTimeOffset lhs, DateTimeOffset rhs) => lhs > rhs ? lhs : rhs;

    /// <summary>取最小日期时间 / Min date-time.</summary>
    public static DateTimeOffset Min(DateTimeOffset lhs, DateTimeOffset rhs) => lhs < rhs ? lhs : rhs;

    /// <summary>取最大时间跨度 / Max time span.</summary>
    public static TimeSpan Max(TimeSpan lhs, TimeSpan rhs) => lhs > rhs ? lhs : rhs;

    /// <summary>取最小时间跨度 / Min time span.</summary>
    public static TimeSpan Min(TimeSpan lhs, TimeSpan rhs) => lhs < rhs ? lhs : rhs;

    /// <summary>截断到指定精度 / Truncate to specified precision.</summary>
    public static DateTimeOffset TruncatedTo(this DateTimeOffset t, TimeSpan unit) =>
        new(t.Ticks - (t.Ticks % unit.Ticks), t.Offset);

    /// <summary>求和时间跨度 / Sum time spans.</summary>
    public static TimeSpan Sum(this IEnumerable<TimeSpan> source) =>
        source.Aggregate(TimeSpan.Zero, (acc, t) => acc + t);

    /// <summary>按提取器求和时间跨度 / Sum time spans by extractor.</summary>
    public static TimeSpan SumOf<T>(this IEnumerable<T> source, Func<T, TimeSpan> extractor) =>
        source.Select(extractor).Sum();

    /// <summary>下一天 / Next day.</summary>
    public static DateTimeOffset NextDay(this DateTimeOffset t) => t.AddDays(1);

    /// <summary>上一天 / Last day.</summary>
    public static DateTimeOffset LastDay(this DateTimeOffset t) => t.AddDays(-1);

    /// <summary>下一天（DateOnly）/ Next day (DateOnly).</summary>
    public static DateOnly NextDay(this DateOnly d) => d.AddDays(1);

    /// <summary>上一天（DateOnly）/ Last day (DateOnly).</summary>
    public static DateOnly LastDay(this DateOnly d) => d.AddDays(-1);

    /// <summary>下一天（DateTime）/ Next day (DateTime).</summary>
    public static DateTime NextDay(this DateTime dt) => dt.AddDays(1);

    /// <summary>上一天（DateTime）/ Last day (DateTime).</summary>
    public static DateTime LastDay(this DateTime dt) => dt.AddDays(-1);
}
