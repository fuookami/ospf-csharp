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

/// <summary>**IntX 时长转换器** / IntX duration converter.</summary>
/// <remarks>
/// **`IntX` 是整数，所以 `TimeSpan` 的亚秒部分会被截断。**
/// **这不是精度损失，是类型本身的语义：一个整数秒的量在转换为 TimeSpan 时不带小数。**
/// **`value.toLong` 也是截断，行为一致。**
///
/// **`IntX` is integral, so the sub-second part of a `TimeSpan` is truncated.**
/// **That is not lost precision but the type semantics: an integer-seconds quantity carries no fraction when
/// converted.** **The code truncates with `value.toLong` too, so the behaviour matches.**
/// </remarks>
public sealed class IntXDurationConverter : IDurationConverter<IntX> {
    public static readonly IntXDurationConverter Instance = new();

    public Result<TimeSpan, ErrorCode, Error<ErrorCode>> ToDuration(Quantity<IntX> quantity) {
        Result<Quantity<IntX>, ErrorCode, Error<ErrorCode>> converted = quantity.To(SIBaseUnits.Second);
        if (converted.IsFailed) {
            return new Failed<TimeSpan, ErrorCode, Error<ErrorCode>>(
                ((Failed<Quantity<IntX>, ErrorCode, Error<ErrorCode>>)converted).Error);
        }

        var seconds = converted.Value.Value.ToBigInteger();
        if (seconds > long.MaxValue || seconds < long.MinValue) {
            return new Failed<TimeSpan, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "duration does not fit in a TimeSpan");
        }

        return new Ok<TimeSpan, ErrorCode, Error<ErrorCode>>(TimeSpan.FromSeconds((double)(long)seconds));
    }

    public Result<Quantity<IntX>, ErrorCode, Error<ErrorCode>> ToQuantity(TimeSpan duration, PhysicalUnit unit) {
        var seconds = new Quantity<IntX>(new IntX((long)duration.TotalSeconds), SIBaseUnits.Second);
        return seconds.To(unit);
    }
}

/// <summary>**UIntX 时长转换器** / UIntX duration converter.</summary>
/// <remarks>
/// **负时长必须拒绝，不能截断到 0**：**把 -5 秒变成 0 秒会让"早于基准点"的信息悄悄消失，
/// 而调用方看到的是一个合法的 0。**
///
/// **A negative duration has to be rejected, not truncated to zero**: **turning -5 s into 0 s silently erases
/// "before the reference point" while handing the caller a legal zero.**
/// </remarks>
public sealed class UIntXDurationConverter : IDurationConverter<UIntX> {
    public static readonly UIntXDurationConverter Instance = new();

    public Result<TimeSpan, ErrorCode, Error<ErrorCode>> ToDuration(Quantity<UIntX> quantity) {
        Result<Quantity<UIntX>, ErrorCode, Error<ErrorCode>> converted = quantity.To(SIBaseUnits.Second);
        if (converted.IsFailed) {
            return new Failed<TimeSpan, ErrorCode, Error<ErrorCode>>(
                ((Failed<Quantity<UIntX>, ErrorCode, Error<ErrorCode>>)converted).Error);
        }

        var seconds = converted.Value.Value.ToBigInteger();
        if (seconds > long.MaxValue) {
            return new Failed<TimeSpan, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "duration does not fit in a TimeSpan");
        }

        return new Ok<TimeSpan, ErrorCode, Error<ErrorCode>>(TimeSpan.FromSeconds((double)(long)seconds));
    }

    public Result<Quantity<UIntX>, ErrorCode, Error<ErrorCode>> ToQuantity(TimeSpan duration, PhysicalUnit unit) {
        if (duration < TimeSpan.Zero) {
            return new Failed<Quantity<UIntX>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"cannot convert negative duration {duration} to UIntX");
        }

        var seconds = new Quantity<UIntX>(new UIntX((long)duration.TotalSeconds), SIBaseUnits.Second);
        return seconds.To(unit);
    }
}
