#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Quantities.Quantity;
/// <summary>
/// 时间单位枚举 / Duration unit enum.
/// </summary>
public enum DurationUnit {
    /// <summary>秒 / Second.</summary>
    Second,
    /// <summary>毫秒 / Millisecond.</summary>
    Millisecond,
    /// <summary>微秒 / Microsecond.</summary>
    Microsecond,
    /// <summary>纳秒 / Nanosecond.</summary>
    Nanosecond,
    /// <summary>分 / Minute.</summary>
    Minute,
    /// <summary>时 / Hour.</summary>
    Hour,
}

/// <summary>
/// 时长转换器接口 / Converter between TimeSpan and Quantity{V}.
/// </summary>
public interface IDurationConverter<V> where V : struct {
    /// <summary>将 Quantity 转为 TimeSpan / Convert a quantity to TimeSpan.</summary>
    Result<TimeSpan, ErrorCode, Error<ErrorCode>> ToDuration(Quantity<V> quantity);

    /// <summary>将 TimeSpan 转为指定单位的 Quantity / Convert TimeSpan to a quantity.</summary>
    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ToQuantity(TimeSpan duration, PhysicalUnit unit);
}

/// <summary>Flt64 时长转换器 / Flt64 duration converter.</summary>
public sealed class Flt64DurationConverter : IDurationConverter<Flt64> {
    public static readonly Flt64DurationConverter Instance = new();

    public Result<TimeSpan, ErrorCode, Error<ErrorCode>> ToDuration(Quantity<Flt64> quantity) {
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> converted = quantity.To(SIBaseUnits.Second);
        if (converted.IsFailed) {
            return new Failed<TimeSpan, ErrorCode, Error<ErrorCode>>(((Failed<Quantity<Flt64>, ErrorCode, Error<ErrorCode>>)converted).Error);
        }

        return new Ok<TimeSpan, ErrorCode, Error<ErrorCode>>(TimeSpan.FromSeconds(converted.Value.Value.ToDouble()));
    }

    public Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> ToQuantity(TimeSpan duration, PhysicalUnit unit) {
        var seconds = new Quantity<Flt64>(new Flt64(duration.TotalSeconds), SIBaseUnits.Second);
        return seconds.To(unit);
    }
}

/// <summary>FltX 时长转换器 / FltX duration converter.</summary>
public sealed class FltXDurationConverter : IDurationConverter<FltX> {
    public static readonly FltXDurationConverter Instance = new();

    public Result<TimeSpan, ErrorCode, Error<ErrorCode>> ToDuration(Quantity<FltX> quantity) {
        Result<Quantity<FltX>, ErrorCode, Error<ErrorCode>> converted = quantity.To(SIBaseUnits.Second);
        if (converted.IsFailed) {
            return new Failed<TimeSpan, ErrorCode, Error<ErrorCode>>(((Failed<Quantity<FltX>, ErrorCode, Error<ErrorCode>>)converted).Error);
        }

        return new Ok<TimeSpan, ErrorCode, Error<ErrorCode>>(TimeSpan.FromSeconds(converted.Value.Value.ToFlt64().ToDouble()));
    }

    public Result<Quantity<FltX>, ErrorCode, Error<ErrorCode>> ToQuantity(TimeSpan duration, PhysicalUnit unit) {
        var seconds = new Quantity<FltX>(new FltX(duration.TotalSeconds), SIBaseUnits.Second);
        return seconds.To(unit);
    }
}

/// <summary>Int64 时长转换器 / Int64 duration converter.</summary>
public sealed class Int64DurationConverter : IDurationConverter<Int64> {
    public static readonly Int64DurationConverter Instance = new();

    public Result<TimeSpan, ErrorCode, Error<ErrorCode>> ToDuration(Quantity<Int64> quantity) {
        Result<Quantity<Int64>, ErrorCode, Error<ErrorCode>> converted = quantity.To(SIBaseUnits.Second);
        if (converted.IsFailed) {
            return new Failed<TimeSpan, ErrorCode, Error<ErrorCode>>(((Failed<Quantity<Int64>, ErrorCode, Error<ErrorCode>>)converted).Error);
        }

        return new Ok<TimeSpan, ErrorCode, Error<ErrorCode>>(TimeSpan.FromSeconds(converted.Value.Value.ToFlt64().ToDouble()));
    }

    public Result<Quantity<Int64>, ErrorCode, Error<ErrorCode>> ToQuantity(TimeSpan duration, PhysicalUnit unit) {
        var seconds = new Quantity<Int64>(new Int64((long)duration.TotalSeconds), SIBaseUnits.Second);
        return seconds.To(unit);
    }
}

/// <summary>UInt64 时长转换器 / UInt64 duration converter.</summary>
public sealed class UInt64DurationConverter : IDurationConverter<UInt64> {
    public static readonly UInt64DurationConverter Instance = new();

    public Result<TimeSpan, ErrorCode, Error<ErrorCode>> ToDuration(Quantity<UInt64> quantity) {
        Result<Quantity<UInt64>, ErrorCode, Error<ErrorCode>> converted = quantity.To(SIBaseUnits.Second);
        if (converted.IsFailed) {
            return new Failed<TimeSpan, ErrorCode, Error<ErrorCode>>(((Failed<Quantity<UInt64>, ErrorCode, Error<ErrorCode>>)converted).Error);
        }

        return new Ok<TimeSpan, ErrorCode, Error<ErrorCode>>(TimeSpan.FromSeconds(converted.Value.Value.ToFlt64().ToDouble()));
    }

    public Result<Quantity<UInt64>, ErrorCode, Error<ErrorCode>> ToQuantity(TimeSpan duration, PhysicalUnit unit) {
        var seconds = new Quantity<UInt64>(new UInt64((ulong)duration.TotalSeconds), SIBaseUnits.Second);
        return seconds.To(unit);
    }
}
