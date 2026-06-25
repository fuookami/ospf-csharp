#nullable enable

using System;
using N = Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math
{
    /// <summary>
    /// 时间单位枚举 / Duration unit enum
    /// </summary>
    public enum DurationUnit
    {
        /// <summary>纳秒 / Nanoseconds</summary>
        Nanoseconds,
        /// <summary>微秒 / Microseconds</summary>
        Microseconds,
        /// <summary>毫秒 / Milliseconds</summary>
        Milliseconds,
        /// <summary>秒 / Seconds</summary>
        Seconds,
        /// <summary>分钟 / Minutes</summary>
        Minutes,
        /// <summary>小时 / Hours</summary>
        Hours,
        /// <summary>天 / Days</summary>
        Days,
    }

    /// <summary>
    /// 时间段扩展方法 / Duration extension methods
    /// </summary>
    public static class DurationExtensions
    {
        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this int value, DurationUnit unit) => unit switch
        {
            DurationUnit.Nanoseconds => TimeSpan.FromTicks(value * 100L),
            DurationUnit.Microseconds => TimeSpan.FromTicks(value * 10000L),
            DurationUnit.Milliseconds => TimeSpan.FromMilliseconds(value),
            DurationUnit.Seconds => TimeSpan.FromSeconds(value),
            DurationUnit.Minutes => TimeSpan.FromMinutes(value),
            DurationUnit.Hours => TimeSpan.FromHours(value),
            DurationUnit.Days => TimeSpan.FromDays(value),
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this uint value, DurationUnit unit) => ((int)value).ToDuration(unit);

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this long value, DurationUnit unit) => unit switch
        {
            DurationUnit.Nanoseconds => TimeSpan.FromTicks(value * 100L),
            DurationUnit.Microseconds => TimeSpan.FromTicks(value * 10000L),
            DurationUnit.Milliseconds => TimeSpan.FromMilliseconds(value),
            DurationUnit.Seconds => TimeSpan.FromSeconds(value),
            DurationUnit.Minutes => TimeSpan.FromMinutes(value),
            DurationUnit.Hours => TimeSpan.FromHours(value),
            DurationUnit.Days => TimeSpan.FromDays(value),
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this ulong value, DurationUnit unit) => ((long)value).ToDuration(unit);

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this double value, DurationUnit unit) => unit switch
        {
            DurationUnit.Nanoseconds => TimeSpan.FromTicks((long)(value * 100)),
            DurationUnit.Microseconds => TimeSpan.FromTicks((long)(value * 10000)),
            DurationUnit.Milliseconds => TimeSpan.FromMilliseconds(value),
            DurationUnit.Seconds => TimeSpan.FromSeconds(value),
            DurationUnit.Minutes => TimeSpan.FromMinutes(value),
            DurationUnit.Hours => TimeSpan.FromHours(value),
            DurationUnit.Days => TimeSpan.FromDays(value),
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this float value, DurationUnit unit) => ((double)value).ToDuration(unit);

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this N.Flt64 value, DurationUnit unit) => value.ToDouble().ToDuration(unit);

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this N.Flt32 value, DurationUnit unit) => ((double)float.Parse(value.ToString())).ToDuration(unit);

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this N.FltX value, DurationUnit unit) => double.Parse(value.ToString()).ToDuration(unit);

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this N.Int64 value, DurationUnit unit) => long.Parse(value.ToString()).ToDuration(unit);

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this N.UInt64 value, DurationUnit unit) => ulong.Parse(value.ToString()).ToDuration(unit);

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this N.IntX value, DurationUnit unit) => long.Parse(value.ToString()).ToDuration(unit);

        /// <summary>转换为 TimeSpan / Convert to TimeSpan</summary>
        public static TimeSpan ToDuration(this N.UIntX value, DurationUnit unit) => ulong.Parse(value.ToString()).ToDuration(unit);

        // Unit convenience methods
        /// <summary>纳秒 / Nanoseconds</summary>
        public static TimeSpan Nanoseconds(this int value) => value.ToDuration(DurationUnit.Nanoseconds);
        /// <summary>微秒 / Microseconds</summary>
        public static TimeSpan Microseconds(this int value) => value.ToDuration(DurationUnit.Microseconds);
        /// <summary>毫秒 / Milliseconds</summary>
        public static TimeSpan Milliseconds(this int value) => value.ToDuration(DurationUnit.Milliseconds);
        /// <summary>秒 / Seconds</summary>
        public static TimeSpan Seconds(this int value) => value.ToDuration(DurationUnit.Seconds);
        /// <summary>分钟 / Minutes</summary>
        public static TimeSpan Minutes(this int value) => value.ToDuration(DurationUnit.Minutes);
        /// <summary>小时 / Hours</summary>
        public static TimeSpan Hours(this int value) => value.ToDuration(DurationUnit.Hours);
        /// <summary>天 / Days</summary>
        public static TimeSpan Days(this int value) => value.ToDuration(DurationUnit.Days);

        /// <summary>纳秒 / Nanoseconds</summary>
        public static TimeSpan Nanoseconds(this long value) => value.ToDuration(DurationUnit.Nanoseconds);
        /// <summary>微秒 / Microseconds</summary>
        public static TimeSpan Microseconds(this long value) => value.ToDuration(DurationUnit.Microseconds);
        /// <summary>毫秒 / Milliseconds</summary>
        public static TimeSpan Milliseconds(this long value) => value.ToDuration(DurationUnit.Milliseconds);
        /// <summary>秒 / Seconds</summary>
        public static TimeSpan Seconds(this long value) => value.ToDuration(DurationUnit.Seconds);
        /// <summary>分钟 / Minutes</summary>
        public static TimeSpan Minutes(this long value) => value.ToDuration(DurationUnit.Minutes);
        /// <summary>小时 / Hours</summary>
        public static TimeSpan Hours(this long value) => value.ToDuration(DurationUnit.Hours);
        /// <summary>天 / Days</summary>
        public static TimeSpan Days(this long value) => value.ToDuration(DurationUnit.Days);

        /// <summary>纳秒 / Nanoseconds</summary>
        public static TimeSpan Nanoseconds(this double value) => value.ToDuration(DurationUnit.Nanoseconds);
        /// <summary>微秒 / Microseconds</summary>
        public static TimeSpan Microseconds(this double value) => value.ToDuration(DurationUnit.Microseconds);
        /// <summary>毫秒 / Milliseconds</summary>
        public static TimeSpan Milliseconds(this double value) => value.ToDuration(DurationUnit.Milliseconds);
        /// <summary>秒 / Seconds</summary>
        public static TimeSpan Seconds(this double value) => value.ToDuration(DurationUnit.Seconds);
        /// <summary>分钟 / Minutes</summary>
        public static TimeSpan Minutes(this double value) => value.ToDuration(DurationUnit.Minutes);
        /// <summary>小时 / Hours</summary>
        public static TimeSpan Hours(this double value) => value.ToDuration(DurationUnit.Hours);
        /// <summary>天 / Days</summary>
        public static TimeSpan Days(this double value) => value.ToDuration(DurationUnit.Days);

        /// <summary>纳秒 / Nanoseconds</summary>
        public static TimeSpan Nanoseconds(this N.Flt64 value) => value.ToDuration(DurationUnit.Nanoseconds);
        /// <summary>微秒 / Microseconds</summary>
        public static TimeSpan Microseconds(this N.Flt64 value) => value.ToDuration(DurationUnit.Microseconds);
        /// <summary>毫秒 / Milliseconds</summary>
        public static TimeSpan Milliseconds(this N.Flt64 value) => value.ToDuration(DurationUnit.Milliseconds);
        /// <summary>秒 / Seconds</summary>
        public static TimeSpan Seconds(this N.Flt64 value) => value.ToDuration(DurationUnit.Seconds);
        /// <summary>分钟 / Minutes</summary>
        public static TimeSpan Minutes(this N.Flt64 value) => value.ToDuration(DurationUnit.Minutes);
        /// <summary>小时 / Hours</summary>
        public static TimeSpan Hours(this N.Flt64 value) => value.ToDuration(DurationUnit.Hours);
        /// <summary>天 / Days</summary>
        public static TimeSpan Days(this N.Flt64 value) => value.ToDuration(DurationUnit.Days);
    }
}
