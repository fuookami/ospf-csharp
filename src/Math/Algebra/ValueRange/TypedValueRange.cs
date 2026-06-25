#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Math.Algebra.ValueRange;
/// <summary>
/// "任意区间标记" 的公共接口，替代 Kotlin 的 TypedValueRange&lt;T, *, *&gt; 使用处型变。
/// Common interface replacing Kotlin's use-site variance TypedValueRange&lt;T, *, *&gt;.
/// </summary>
public interface ITypedValueRange<T> : IContains<T>
    where T : struct, IRealNumber<T>, INumberField<T> {
    /// <summary>内部 ValueRange / Inner range.</summary>
    ValueRange<T> Range { get; }
    /// <summary>下边界值 / Lower bound value.</summary>
    ValueWrapper<T> LowerBound { get; }
    /// <summary>上边界值 / Upper bound value.</summary>
    ValueWrapper<T> UpperBound { get; }
    /// <summary>下边界区间类型 / Lower bound interval type.</summary>
    Interval LowerInterval { get; }
    /// <summary>上边界区间类型 / Upper bound interval type.</summary>
    Interval UpperInterval { get; }
    /// <summary>是否为固定值 / Whether is a fixed value.</summary>
    bool Fixed { get; }
    /// <summary>固定值 / Fixed value.</summary>
    T? FixedValue { get; }
    /// <summary>转换为动态值范围（拷贝）/ Converts to dynamic value range (copy).</summary>
    ValueRange<T> ToDynamic();
}

/// <summary>
/// 类型化值范围 / Typed value range.
/// 泛型参数 TLower/TUpper 在编译期静态编码区间开闭性。
/// Generic parameters TLower/TUpper statically encode interval openness/closedness at compile time.
/// </summary>
public sealed class TypedValueRange<T, TLower, TUpper> : ITypedValueRange<T>
    where T : struct, IRealNumber<T>, INumberField<T>
    where TLower : IntervalKind
    where TUpper : IntervalKind {
    private readonly ValueRange<T> _range;

    public TLower LowerKind { get; }
    public TUpper UpperKind { get; }

    private TypedValueRange(ValueRange<T> range, TLower lowerKind, TUpper upperKind) {
        _range = range;
        LowerKind = lowerKind;
        UpperKind = upperKind;
    }

    // ---------- ITypedValueRange ----------
    public ValueRange<T> Range => _range;
    public ValueWrapper<T> LowerBound => _range.LowerBound.Value;
    public ValueWrapper<T> UpperBound => _range.UpperBound.Value;
    public Interval LowerInterval => _range.LowerBound.Interval;
    public Interval UpperInterval => _range.UpperBound.Interval;
    public bool Fixed => _range.Fixed;
    public T? FixedValue => _range.FixedValue;
    public ValueRange<T> ToDynamic() => _range.Copy();
    public bool Contains(T value) => _range.Contains(value);

    /// <summary>判断另一类型化值范围是否完全包含 / Determines if another typed value range is fully contained.</summary>
    public bool Contains(ITypedValueRange<T> rhs) => _range.Contains(rhs.Range);

    // ---------- 静态工厂 / Static factories ----------

    /// <summary>从动态值范围创建类型化值范围 / Creates typed value range from dynamic value range.</summary>
    public static Result<TypedValueRange<T, TLower, TUpper>, ErrorCode, Error<ErrorCode>> FromDynamic(
        ValueRange<T> range, TLower lowerKind, TUpper upperKind) {
        if (range.LowerBound.Interval == lowerKind.Interval && range.UpperBound.Interval == upperKind.Interval) {
            return new Ok<TypedValueRange<T, TLower, TUpper>, ErrorCode, Error<ErrorCode>>(
                new TypedValueRange<T, TLower, TUpper>(range.Copy(), lowerKind, upperKind));
        }

        return new Failed<TypedValueRange<T, TLower, TUpper>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
            $"TypedValueRange interval mismatch: expected lower={lowerKind.Interval}, upper={upperKind.Interval}, " +
            $"actual lower={range.LowerBound.Interval}, upper={range.UpperBound.Interval}.");
    }

    /// <summary>从数值创建类型化值范围 / Creates typed value range from values.</summary>
    public static Result<TypedValueRange<T, TLower, TUpper>, ErrorCode, Error<ErrorCode>> FromValues(
        T lb, T ub, TLower lowerKind, TUpper upperKind) {
        Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> r = ValueRange<T>.Of(lb, ub, lowerKind.Interval, upperKind.Interval);
        if (r is Ok<ValueRange<T>, ErrorCode, Error<ErrorCode>> ok) {
            return FromDynamic(ok.Value, lowerKind, upperKind);
        }

        return r.Map(v => new TypedValueRange<T, TLower, TUpper>(v, lowerKind, upperKind));
    }

    /// <summary>从值包装器创建类型化值范围 / Creates typed value range from bounds.</summary>
    public static Result<TypedValueRange<T, TLower, TUpper>, ErrorCode, Error<ErrorCode>> FromBounds(
        ValueWrapper<T> lb, ValueWrapper<T> ub, TLower lowerKind, TUpper upperKind) {
        Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> r = ValueRange<T>.Of(lb, ub, lowerKind.Interval, upperKind.Interval);
        if (r is Ok<ValueRange<T>, ErrorCode, Error<ErrorCode>> ok) {
            return FromDynamic(ok.Value, lowerKind, upperKind);
        }

        return r.Map(v => new TypedValueRange<T, TLower, TUpper>(v, lowerKind, upperKind));
    }

    // 便捷工厂 / Convenience factories
    /// <summary>创建闭区间类型化值范围 / Creates closed typed value range.</summary>
    public static Result<TypedValueRange<T, IntervalKind.ClosedIntervalKind, IntervalKind.ClosedIntervalKind>, ErrorCode, Error<ErrorCode>> Closed(T lb, T ub) {
        var lk = new IntervalKind.ClosedIntervalKind();
        var uk = new IntervalKind.ClosedIntervalKind();
        Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> r = ValueRange<T>.Of(lb, ub, lk.Interval, uk.Interval);
        if (r is Failed<ValueRange<T>, ErrorCode, Error<ErrorCode>> f) {
            return new Failed<TypedValueRange<T, IntervalKind.ClosedIntervalKind, IntervalKind.ClosedIntervalKind>, ErrorCode, Error<ErrorCode>>(f.Error);
        }

        return TypedValueRange<T, IntervalKind.ClosedIntervalKind, IntervalKind.ClosedIntervalKind>.FromDynamic(r.Value, lk, uk);
    }

    /// <summary>创建开区间类型化值范围 / Creates open typed value range.</summary>
    public static Result<TypedValueRange<T, IntervalKind.OpenIntervalKind, IntervalKind.OpenIntervalKind>, ErrorCode, Error<ErrorCode>> Open(T lb, T ub) {
        var lk = new IntervalKind.OpenIntervalKind();
        var uk = new IntervalKind.OpenIntervalKind();
        Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> r = ValueRange<T>.Of(lb, ub, lk.Interval, uk.Interval);
        if (r is Failed<ValueRange<T>, ErrorCode, Error<ErrorCode>> f) {
            return new Failed<TypedValueRange<T, IntervalKind.OpenIntervalKind, IntervalKind.OpenIntervalKind>, ErrorCode, Error<ErrorCode>>(f.Error);
        }

        return TypedValueRange<T, IntervalKind.OpenIntervalKind, IntervalKind.OpenIntervalKind>.FromDynamic(r.Value, lk, uk);
    }

    /// <summary>创建左闭右开区间类型化值范围 / Creates closed-open typed value range.</summary>
    public static Result<TypedValueRange<T, IntervalKind.ClosedIntervalKind, IntervalKind.OpenIntervalKind>, ErrorCode, Error<ErrorCode>> ClosedOpen(T lb, T ub) {
        var lk = new IntervalKind.ClosedIntervalKind();
        var uk = new IntervalKind.OpenIntervalKind();
        Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> r = ValueRange<T>.Of(lb, ub, lk.Interval, uk.Interval);
        if (r is Failed<ValueRange<T>, ErrorCode, Error<ErrorCode>> f) {
            return new Failed<TypedValueRange<T, IntervalKind.ClosedIntervalKind, IntervalKind.OpenIntervalKind>, ErrorCode, Error<ErrorCode>>(f.Error);
        }

        return TypedValueRange<T, IntervalKind.ClosedIntervalKind, IntervalKind.OpenIntervalKind>.FromDynamic(r.Value, lk, uk);
    }

    /// <summary>创建左开右闭区间类型化值范围 / Creates open-closed typed value range.</summary>
    public static Result<TypedValueRange<T, IntervalKind.OpenIntervalKind, IntervalKind.ClosedIntervalKind>, ErrorCode, Error<ErrorCode>> OpenClosed(T lb, T ub) {
        var lk = new IntervalKind.OpenIntervalKind();
        var uk = new IntervalKind.ClosedIntervalKind();
        Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> r = ValueRange<T>.Of(lb, ub, lk.Interval, uk.Interval);
        if (r is Failed<ValueRange<T>, ErrorCode, Error<ErrorCode>> f) {
            return new Failed<TypedValueRange<T, IntervalKind.OpenIntervalKind, IntervalKind.ClosedIntervalKind>, ErrorCode, Error<ErrorCode>>(f.Error);
        }

        return TypedValueRange<T, IntervalKind.OpenIntervalKind, IntervalKind.ClosedIntervalKind>.FromDynamic(r.Value, lk, uk);
    }

    // ---------- 集合运算 / Set operations ----------

    /// <summary>并集（返回动态类型）/ Union (returns dynamic type).</summary>
    public TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind>? Union(ITypedValueRange<T> rhs) =>
        _range.Union(rhs.Range) is { } r ? ToDynamicRange(r) : null;

    /// <summary>交集（返回动态类型）/ Intersection (returns dynamic type).</summary>
    public TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind>? Intersect(ITypedValueRange<T> rhs) =>
        _range.Intersect(rhs.Range) is { } r ? ToDynamicRange(r) : null;

    /// <summary>同类型并集（保持类型）/ Same-kind union (preserves type).</summary>
    public TypedValueRange<T, TLower, TUpper>? UnionTyped(TypedValueRange<T, TLower, TUpper> rhs) =>
        _range.Union(rhs._range) is { } r ? FromDynamic(r, LowerKind, UpperKind).Value : null;

    /// <summary>同类型交集（保持类型）/ Same-kind intersection (preserves type).</summary>
    public TypedValueRange<T, TLower, TUpper>? IntersectTyped(TypedValueRange<T, TLower, TUpper> rhs) =>
        _range.Intersect(rhs._range) is { } r ? FromDynamic(r, LowerKind, UpperKind).Value : null;

    // ---------- 算术 / Arithmetic ----------

    /// <summary>与数值相加（保持类型）/ Adds a number (preserves type).</summary>
    public TypedValueRange<T, TLower, TUpper>? PlusTyped(T rhs) =>
        (_range + rhs) is { } r ? FromDynamic(r, LowerKind, UpperKind).Value : null;

    /// <summary>与数值相加 / Adds a number.</summary>
    public static TypedValueRange<T, TLower, TUpper>? operator +(TypedValueRange<T, TLower, TUpper> lhs, T rhs) =>
        lhs.PlusTyped(rhs);

    /// <summary>与动态类型相加 / Adds with dynamic type.</summary>
    public TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind>? Plus(ITypedValueRange<T> rhs) =>
        (_range + rhs.Range) is { } r ? ToDynamicRange(r) : null;

    /// <summary>同类型相加（保持类型）/ Same-kind addition (preserves type).</summary>
    public TypedValueRange<T, TLower, TUpper>? PlusTyped(TypedValueRange<T, TLower, TUpper> rhs) =>
        (_range + rhs._range) is { } r ? FromDynamic(r, LowerKind, UpperKind).Value : null;

    /// <summary>跨类型相加 / Cross-kind addition.</summary>
    public ITypedValueRange<T>? PlusTypedAcrossKinds(ITypedValueRange<T> rhs) =>
        (_range + rhs.Range) is { } r ? ToMostStaticKindRange(r) : null;

    /// <summary>与数值相减（保持类型）/ Subtracts a number (preserves type).</summary>
    public TypedValueRange<T, TLower, TUpper>? MinusTyped(T rhs) =>
        (_range - rhs) is { } r ? FromDynamic(r, LowerKind, UpperKind).Value : null;

    /// <summary>与数值相减 / Subtracts a number.</summary>
    public static TypedValueRange<T, TLower, TUpper>? operator -(TypedValueRange<T, TLower, TUpper> lhs, T rhs) =>
        lhs.MinusTyped(rhs);

    /// <summary>与动态类型相减 / Subtracts with dynamic type.</summary>
    public TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind>? Minus(ITypedValueRange<T> rhs) =>
        (_range - rhs.Range) is { } r ? ToDynamicRange(r) : null;

    /// <summary>同类型相减（保持类型）/ Same-kind subtraction (preserves type).</summary>
    public TypedValueRange<T, TLower, TUpper>? MinusTyped(TypedValueRange<T, TLower, TUpper> rhs) =>
        (_range - rhs._range) is { } r ? FromDynamic(r, LowerKind, UpperKind).Value : null;

    /// <summary>跨类型相减 / Cross-kind subtraction.</summary>
    public ITypedValueRange<T>? MinusTypedAcrossKinds(ITypedValueRange<T> rhs) =>
        (_range - rhs.Range) is { } r ? ToMostStaticKindRange(r) : null;

    /// <summary>与数值相乘（动态类型）/ Multiplies by a number (dynamic type).</summary>
    public TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind>? Times(T rhs) =>
        (_range * rhs) is { } r ? ToDynamicRange(r) : null;

    /// <summary>与数值相乘 / Multiplies by a number.</summary>
    public static TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind>? operator *(
        TypedValueRange<T, TLower, TUpper> lhs, T rhs) => lhs.Times(rhs);

    /// <summary>乘正数（保持类型）/ Multiply by positive (preserves type).</summary>
    public TypedValueRange<T, TLower, TUpper>? TimesPositive(T rhs) =>
        IsPositive(rhs) && _range * rhs is { } r ? FromDynamic(r, LowerKind, UpperKind).Value : null;

    /// <summary>乘负数（上下边界类型翻转）/ Multiply by negative (flips LB/UB kinds).</summary>
    public TypedValueRange<T, TUpper, TLower>? TimesNegative(T rhs) =>
        IsNegative(rhs) && _range * rhs is { } r
            ? TypedValueRange<T, TUpper, TLower>.FromDynamic(r, UpperKind, LowerKind).Value : null;

    /// <summary>与数值相乘（跨类型推导）/ Multiplies by a number (cross-kind inference).</summary>
    public ITypedValueRange<T>? TimesTyped(T rhs) =>
        (_range * rhs) is { } r ? ToMostStaticKindRange(r) : null;

    /// <summary>与动态类型相乘 / Multiplies with dynamic type.</summary>
    public TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind>? Times(ITypedValueRange<T> rhs) =>
        (_range * rhs.Range) is { } r ? ToDynamicRange(r) : null;

    /// <summary>跨类型相乘 / Cross-kind multiplication.</summary>
    public ITypedValueRange<T>? TimesTypedAcrossKinds(ITypedValueRange<T> rhs) =>
        (_range * rhs.Range) is { } r ? ToMostStaticKindRange(r) : null;

    /// <summary>除以数值（动态类型）/ Divides by a number (dynamic type).</summary>
    public TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind>? Div(T rhs) =>
        _range.Div(rhs) is { } r ? ToDynamicRange(r) : null;

    /// <summary>除以数值 / Divides by a number.</summary>
    public static TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind>? operator /(
        TypedValueRange<T, TLower, TUpper> lhs, T rhs) =>
        lhs._range.Div(rhs) is { } r ? ToDynamicRange(r) : null;

    /// <summary>除以正数（保持类型）/ Divides by positive (preserves type).</summary>
    public TypedValueRange<T, TLower, TUpper>? DivPositive(T rhs) =>
        IsPositive(rhs) && _range.Div(rhs) is { } r ? FromDynamic(r, LowerKind, UpperKind).Value : null;

    /// <summary>除以负数（上下边界类型翻转）/ Divides by negative (flips LB/UB kinds).</summary>
    public TypedValueRange<T, TUpper, TLower>? DivNegative(T rhs) =>
        IsNegative(rhs) && _range.Div(rhs) is { } r
            ? TypedValueRange<T, TUpper, TLower>.FromDynamic(r, UpperKind, LowerKind).Value : null;

    /// <summary>除以数值（跨类型推导）/ Divides by a number (cross-kind inference).</summary>
    public ITypedValueRange<T>? DivTyped(T rhs) =>
        _range.Div(rhs) is { } r ? ToMostStaticKindRange(r) : null;

    public override bool Equals(object? obj) =>
        obj is TypedValueRange<T, TLower, TUpper> other
        && _range == other._range && LowerKind == other.LowerKind && UpperKind == other.UpperKind;

    public override int GetHashCode() => HashCode.Combine(_range, LowerKind, UpperKind);

    public override string ToString() =>
        $"TypedValueRange(lower={LowerBound}, upper={UpperBound}, lowerInterval={LowerInterval}, upperInterval={UpperInterval})";

    // ---------- 私有助手 / Private helpers ----------

    private static bool IsPositive(T v) { T zero = v.Minus(v); return v.Gr(zero); }
    private static bool IsNegative(T v) { T zero = v.Minus(v); return v.Ls(zero); }

    private static TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind> ToDynamicRange(ValueRange<T> r) =>
        new(r.Copy(), new IntervalKind.RuntimeIntervalKind(r.LowerBound.Interval), new IntervalKind.RuntimeIntervalKind(r.UpperBound.Interval));

    private static ITypedValueRange<T>? ToMostStaticKindRange(ValueRange<T> r) => r.LowerBound.Interval switch {
        Interval.Closed when r.UpperBound.Interval is Interval.Closed =>
            TypedValueRange<T, IntervalKind.ClosedIntervalKind, IntervalKind.ClosedIntervalKind>
                .FromDynamic(r, new IntervalKind.ClosedIntervalKind(), new IntervalKind.ClosedIntervalKind()).Value,
        Interval.Open when r.UpperBound.Interval is Interval.Open =>
            TypedValueRange<T, IntervalKind.OpenIntervalKind, IntervalKind.OpenIntervalKind>
                .FromDynamic(r, new IntervalKind.OpenIntervalKind(), new IntervalKind.OpenIntervalKind()).Value,
        Interval.Closed when r.UpperBound.Interval is Interval.Open =>
            TypedValueRange<T, IntervalKind.ClosedIntervalKind, IntervalKind.OpenIntervalKind>
                .FromDynamic(r, new IntervalKind.ClosedIntervalKind(), new IntervalKind.OpenIntervalKind()).Value,
        _ => TypedValueRange<T, IntervalKind.OpenIntervalKind, IntervalKind.ClosedIntervalKind>
                .FromDynamic(r, new IntervalKind.OpenIntervalKind(), new IntervalKind.ClosedIntervalKind()).Value,
    };
}

/// <summary>ValueRange → DynamicTypedValueRange 扩展 / Extension.</summary>
public static class TypedValueRangeExtensions {
    /// <summary>将值范围转换为动态类型化值范围 / Converts value range to dynamic typed value range.</summary>
    public static TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind> ToDynamicTypedValueRange<T>(
        this ValueRange<T> range)
        where T : struct, IRealNumber<T>, INumberField<T>
        => TypedValueRange<T, IntervalKind.RuntimeIntervalKind, IntervalKind.RuntimeIntervalKind>
            .FromDynamic(range, new IntervalKind.RuntimeIntervalKind(range.LowerBound.Interval),
                                 new IntervalKind.RuntimeIntervalKind(range.UpperBound.Interval)).Value!;
}
