#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Math.Algebra.ValueRange;
/// <summary>
/// 值范围 / Value range.
/// 表示一个数值区间，包含下边界和上边界，支持集合操作和算术运算。
/// Represents a numerical interval with lower and upper bounds, supporting set operations and arithmetic.
/// </summary>
/// <typeparam name="T">数值类型 / Number type.</typeparam>
[JsonConverter(typeof(ValueRangeJsonConverterFactory))]
public sealed record ValueRange<T>(Bound<T> LowerBound, Bound<T> UpperBound) :
    IEq<ValueRange<T>>,
    IPlus<ValueRange<T>, ValueRange<T>?>,
    IMinus<ValueRange<T>, ValueRange<T>?>,
    ITimes<ValueRange<T>, ValueRange<T>?>,
    IDiv<T, ValueRange<T>?>,
    IContains<T>
    where T : struct, IRealNumber<T>, INumberField<T> {
    private static INumericConstants<T> Constants => NumericConstantsRegistry.For<T>();

    // ---------- 静态工厂 / Static factories ----------

    /// <summary>判断区间是否为空 / Determines if interval is empty.</summary>
    public static bool IsEmpty(ValueWrapper<T> lb, ValueWrapper<T> ub, Interval lbInterval, Interval ubInterval) {
        if (lb.IsNegativeInfinity) {
            return false;
        }

        if (ub.IsInfinity) {
            return false;
        }

        if (!lb.IsInfinityOrNegativeInfinity && !ub.IsInfinityOrNegativeInfinity) {
            return !lbInterval.LowerBoundOperator<T>()(lb.Unwrap(), ub.Unwrap())
                || !ubInterval.UpperBoundOperator<T>()(ub.Unwrap(), lb.Unwrap());
        }
        return true;
    }

    /// <summary>创建全范围值范围 / Creates full range value range.</summary>
    public static ValueRange<T> Full() => new(
        new Bound<T>(new ValueWrapper<T>.NegativeInfinity(), new Interval.Closed()),
        new Bound<T>(new ValueWrapper<T>.Infinity(), new Interval.Closed()));

    /// <summary>创建单点值范围 / Creates single-point value range.</summary>
    public static Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> Of(T value) => Of(value, value, new Interval.Closed(), new Interval.Closed());

    /// <summary>创建值范围 / Creates value range.</summary>
    public static Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> Of(T lb, T ub, Interval? lbInterval = null, Interval? ubInterval = null) {
        Interval lbi = lbInterval ?? new Interval.Closed();
        Interval ubi = ubInterval ?? new Interval.Closed();
        Result<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> lower = ValueWrapper<T>.Of(lb);
        if (lower is Failed<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> f1) {
            return new Failed<ValueRange<T>, ErrorCode, Error<ErrorCode>>(f1.Error);
        }

        Result<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> upper = ValueWrapper<T>.Of(ub);
        if (upper is Failed<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> f2) {
            return new Failed<ValueRange<T>, ErrorCode, Error<ErrorCode>>(f2.Error);
        }

        return Of(lower.Value, upper.Value, lbi, ubi);
    }

    /// <summary>从值包装器创建值范围 / Creates value range from value wrappers.</summary>
    public static Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> Of(ValueWrapper<T> lb, ValueWrapper<T> ub, Interval lbInterval, Interval ubInterval) {
        if (IsEmpty(lb, ub, lbInterval, ubInterval)) {
            return new Failed<ValueRange<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
                $"Invalid range: {lbInterval.LowerSign}{lb}, {ub}{ubInterval.UpperSign}");
        }
        return new Ok<ValueRange<T>, ErrorCode, Error<ErrorCode>>(
            new ValueRange<T>(new Bound<T>(lb, lbInterval), new Bound<T>(ub, ubInterval)));
    }

    /// <summary>创建到正无穷的值范围 / Creates value range to positive infinity.</summary>
    public static Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> OfToInfinity(T lb, Interval? lbInterval = null) {
        Interval lbi = lbInterval ?? new Interval.Closed();
        Result<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> lower = ValueWrapper<T>.Of(lb);
        if (lower is Failed<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> f) {
            return new Failed<ValueRange<T>, ErrorCode, Error<ErrorCode>>(f.Error);
        }

        return Of(lower.Value, new ValueWrapper<T>.Infinity(), lbi, new Interval.Open());
    }

    /// <summary>创建从负无穷的值范围 / Creates value range from negative infinity.</summary>
    public static Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> OfFromNegativeInfinity(T ub, Interval? ubInterval = null) {
        Interval ubi = ubInterval ?? new Interval.Closed();
        Result<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> upper = ValueWrapper<T>.Of(ub);
        if (upper is Failed<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> f) {
            return new Failed<ValueRange<T>, ErrorCode, Error<ErrorCode>>(f.Error);
        }

        return Of(new ValueWrapper<T>.NegativeInfinity(), upper.Value, new Interval.Open(), ubi);
    }

    /// <summary>创建大于等于指定值的值范围 / Creates value range greater than or equal to specified value.</summary>
    public static Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> Geq(T lb, Interval? lbInterval = null) =>
        OfToInfinity(lb, lbInterval ?? new Interval.Closed());

    /// <summary>创建大于指定值的值范围 / Creates value range greater than specified value.</summary>
    public static Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> Gr(T lb) => OfToInfinity(lb, new Interval.Open());

    /// <summary>创建小于等于指定值的值范围 / Creates value range less than or equal to specified value.</summary>
    public static Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> Leq(T ub, Interval? ubInterval = null) =>
        OfFromNegativeInfinity(ub, ubInterval ?? new Interval.Closed());

    /// <summary>创建小于指定值的值范围 / Creates value range less than specified value.</summary>
    public static Result<ValueRange<T>, ErrorCode, Error<ErrorCode>> Ls(T ub) => OfFromNegativeInfinity(ub, new Interval.Open());

    // ---------- 派生属性 / Derived properties ----------

    /// <summary>区间平均值（可能为 null）/ Interval mean value (nullable).</summary>
    public ValueWrapper<T>? MeanOrNull {
        get {
            ValueWrapper<T>? sum = LowerBound.Value.Plus(UpperBound.Value);
            return sum is null ? null : sum.Div(Constants.Two);
        }
    }

    /// <summary>区间平均值计算结果 / Interval mean value result.</summary>
    public Result<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> Mean => MeanOrNull is { } m
        ? new Ok<ValueWrapper<T>, ErrorCode, Error<ErrorCode>>(m)
        : new Failed<ValueWrapper<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
            "区间平均值未定义 / Interval mean is undefined: bounds cannot be added or divided by two.");

    /// <summary>区间宽度（可能为 null）/ Interval width (nullable).</summary>
    public ValueWrapper<T>? DiffOrNull => UpperBound.Value.Minus(LowerBound.Value);

    /// <summary>区间宽度计算结果 / Interval width result.</summary>
    public Result<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> Diff => DiffOrNull is { } d
        ? new Ok<ValueWrapper<T>, ErrorCode, Error<ErrorCode>>(d)
        : new Failed<ValueWrapper<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
            "区间宽度未定义 / Interval width is undefined: upper and lower bounds cannot be subtracted.");

    /// <summary>区间相对精度（可能为 null）/ Interval relative precision (nullable).</summary>
    public ValueWrapper<T>? GapOrNull {
        get {
            ValueWrapper<T>? diff = DiffOrNull;
            if (diff is null) {
                return null;
            }

            T? mean = MeanOrNull?.UnwrapOrNull();
            if (mean is null) {
                return null;
            }

            INumericConstants<T> c = Constants;
            T dp = c.DecimalPrecision ?? c.One;
            T negMean = mean.Value.Negate();
            T absMean = mean.Value.Geq(negMean) ? mean.Value : negMean;
            return diff.Div(absMean.Geq(dp) ? absMean : dp);
        }
    }

    /// <summary>区间相对精度计算结果 / Interval relative precision result.</summary>
    public Result<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> Gap => GapOrNull is { } g
        ? new Ok<ValueWrapper<T>, ErrorCode, Error<ErrorCode>>(g)
        : new Failed<ValueWrapper<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument,
            "区间相对精度未定义 / Interval relative precision is undefined: width or mean is unavailable.");

    /// <summary>是否为固定值（单点区间）/ Whether is a fixed value (single-point interval).</summary>
    public bool Fixed {
        get {
            if (LowerBound.Interval is not Interval.Closed || UpperBound.Interval is not Interval.Closed) {
                return false;
            }

            if (LowerBound.Value.IsInfinityOrNegativeInfinity || UpperBound.Value.IsInfinityOrNegativeInfinity) {
                return false;
            }

            return LowerBound.Value.Unwrap().Eq(UpperBound.Value.Unwrap());
        }
    }

    /// <summary>固定值 / Fixed value.</summary>
    public T? FixedValue => Fixed ? LowerBound.Value.Unwrap() : default;

    // ---------- 集合运算 / Set operations ----------

    /// <summary>计算与另一值范围的并集 / Computes union with another value range.</summary>
    public ValueRange<T>? Union(ValueRange<T> rhs) {
        if (UpperBound.Value.Ls(rhs.LowerBound.Value) || rhs.UpperBound.Value.Ls(LowerBound.Value)) {
            return null;
        }

        (ValueWrapper<T>? newLb, Interval? newLbi) = PickLower(rhs);
        (ValueWrapper<T>? newUb, Interval? newUbi) = PickUpper(rhs);
        return Of(newLb, newUb, newLbi, newUbi).Value;
    }

    /// <summary>计算与另一值范围的交集 / Computes intersection with another value range.</summary>
    public ValueRange<T>? Intersect(ValueRange<T> rhs) {
        (ValueWrapper<T>? newLb, Interval? newLbi) = PickMaxLower(rhs);
        (ValueWrapper<T>? newUb, Interval? newUbi) = PickMinUpper(rhs);
        return Of(newLb, newUb, newLbi, newUbi).Value;
    }

    /// <summary>判断值是否在范围内 / Determines if value is within range.</summary>
    public bool Contains(T value) {
        if (LowerBound.Value.IsNegativeInfinity && UpperBound.Value.IsInfinity) {
            return true;
        }

        if (LowerBound.Value.IsNegativeInfinity && !UpperBound.Value.IsInfinityOrNegativeInfinity) {
            return UpperBound.Interval.UpperBoundOperator<T>()(UpperBound.Value.Unwrap(), value);
        }

        if (!LowerBound.Value.IsInfinityOrNegativeInfinity && UpperBound.Value.IsInfinity) {
            return LowerBound.Interval.LowerBoundOperator<T>()(LowerBound.Value.Unwrap(), value);
        }

        if (!LowerBound.Value.IsInfinityOrNegativeInfinity && !UpperBound.Value.IsInfinityOrNegativeInfinity) {
            return LowerBound.Interval.LowerBoundOperator<T>()(LowerBound.Value.Unwrap(), value)
                && UpperBound.Interval.UpperBoundOperator<T>()(UpperBound.Value.Unwrap(), value);
        }

        return false;
    }

    /// <summary>判断另一值范围是否完全包含在本范围内 / Determines if another value range is fully contained.</summary>
    public bool Contains(ValueRange<T> rhs) {
        Order? lowerCmp = LowerBound.Value.PartialOrd(rhs.LowerBound.Value);
        bool lowerContains = lowerCmp switch {
            Order.Less => true,
            Order.Greater => false,
            _ => rhs.LowerBound.Interval is Interval.Open || LowerBound.Interval is Interval.Closed,
        };
        if (!lowerContains) {
            return false;
        }

        return UpperBound.Value.PartialOrd(rhs.UpperBound.Value) switch {
            Order.Greater => true,
            Order.Less => false,
            _ => rhs.UpperBound.Interval is Interval.Open || UpperBound.Interval is Interval.Closed,
        };
    }

    /// <summary>复制值范围 / Copies value range.</summary>
    public ValueRange<T> Copy() => new(LowerBound.Copy(), UpperBound.Copy());

    /// <summary>部分相等比较 / Partial equality comparison.</summary>
    public bool? PartialEq(ValueRange<T> rhs) =>
        LowerBound.PartialEq(rhs.LowerBound) switch {
            false or null => LowerBound.PartialEq(rhs.LowerBound),
            _ => UpperBound.PartialEq(rhs.UpperBound),
        };

    // ---------- 算术 / Arithmetic ----------

    /// <summary>值范围与数值相加 / Adds a number to value range.</summary>
    public static ValueRange<T>? operator +(ValueRange<T> r, T s) =>
        r.LowerBound.Plus(s) is { } nl && r.UpperBound.Plus(s) is { } nu
            ? new ValueRange<T>(nl, nu) : null;

    /// <summary>两个值范围相加（Minkowski 和）/ Adds two value ranges (Minkowski sum).</summary>
    public static ValueRange<T>? operator +(ValueRange<T> lhs, ValueRange<T> rhs) =>
        lhs.LowerBound.Plus(rhs.LowerBound) is { } nl && lhs.UpperBound.Plus(rhs.UpperBound) is { } nu
            ? new ValueRange<T>(nl, nu) : null;

    /// <summary>IPlus 接口实现 / IPlus interface implementation.</summary>
    public ValueRange<T>? Plus(ValueRange<T> rhs) => this + rhs;

    /// <summary>值范围与数值相减 / Subtracts a number from value range.</summary>
    public static ValueRange<T>? operator -(ValueRange<T> r, T s) =>
        r.LowerBound.Minus(s) is { } nl && r.UpperBound.Minus(s) is { } nu
            ? new ValueRange<T>(nl, nu) : null;

    /// <summary>两个值范围相减（Minkowski 差）/ Subtracts two value ranges (Minkowski difference).</summary>
    public static ValueRange<T>? operator -(ValueRange<T> lhs, ValueRange<T> rhs) =>
        lhs.LowerBound.Minus(rhs.UpperBound) is { } nl && lhs.UpperBound.Minus(rhs.LowerBound) is { } nu
            ? new ValueRange<T>(nl, nu) : null;

    /// <summary>IMinus 接口实现 / IMinus interface implementation.</summary>
    public ValueRange<T>? Minus(ValueRange<T> rhs) => this - rhs;

    /// <summary>值范围与数值相乘 / Multiplies value range by a number.</summary>
    public static ValueRange<T>? operator *(ValueRange<T> r, T s) {
        INumericConstants<T> c = Constants;
        if (s.Gr(c.Zero)) {
            return r.LowerBound.Times(s) is { } nl && r.UpperBound.Times(s) is { } nu
                ? new ValueRange<T>(nl, nu) : null;
        }

        if (s.Ls(c.Zero)) {
            return r.UpperBound.Times(s) is { } nl && r.LowerBound.Times(s) is { } nu
                ? new ValueRange<T>(nl, nu) : null;
        }

        return Of(c.Zero, c.Zero, r.LowerBound.Interval, r.UpperBound.Interval).Value;
    }

    /// <summary>两个值范围相乘 / Multiplies two value ranges.</summary>
    public static ValueRange<T>? operator *(ValueRange<T> lhs, ValueRange<T> rhs) {
        Bound<T>?[] bounds = new[]
        {
            lhs.LowerBound.Value.Times(rhs.LowerBound.Value) is { } v1 ? new Bound<T>(v1, lhs.LowerBound.Interval.Intersect(rhs.LowerBound.Interval)) : null,
            lhs.LowerBound.Value.Times(rhs.UpperBound.Value) is { } v2 ? new Bound<T>(v2, lhs.LowerBound.Interval.Intersect(rhs.UpperBound.Interval)) : null,
            lhs.UpperBound.Value.Times(rhs.LowerBound.Value) is { } v3 ? new Bound<T>(v3, lhs.UpperBound.Interval.Intersect(rhs.LowerBound.Interval)) : null,
            lhs.UpperBound.Value.Times(rhs.UpperBound.Value) is { } v4 ? new Bound<T>(v4, lhs.UpperBound.Interval.Intersect(rhs.UpperBound.Interval)) : null,
        };
        var nonNull = bounds.Where(b => b is not null).Cast<Bound<T>>().ToList();
        if (nonNull.Count != 4) {
            return null;
        }

        nonNull.Sort((a, b) => a.Ord(b).Value);
        return new ValueRange<T>(nonNull[0], nonNull[^1]);
    }

    /// <summary>ITimes 接口实现 / ITimes interface implementation.</summary>
    public ValueRange<T>? Times(ValueRange<T> rhs) => this * rhs;

    /// <summary>值范围除以数值 / Divides value range by a number.</summary>
    public ValueRange<T>? Div(T rhs) {
        INumericConstants<T> c = Constants;
        return rhs.Eq(c.Zero) ? null : this * rhs.Reciprocal();
    }

    /// <summary>IDiv 接口实现 / IDiv interface implementation.</summary>
    ValueRange<T>? IDiv<T, ValueRange<T>?>.Div(T rhs) => Div(rhs);

    /// <summary>转换为 Flt64 类型的值范围 / Converts to Flt64 typed value range.</summary>
    public ValueRange<Flt64> ToFlt64() => new(LowerBound.ToFlt64(), UpperBound.ToFlt64());

    /// <summary>取负：单一泛型算子 / Unary minus (generic).</summary>
    public static ValueRange<T> operator -(ValueRange<T> r) =>
        new(-r.UpperBound, -r.LowerBound);

    /// <summary>获取字符串表示 / Gets string representation.</summary>
    public override string ToString() =>
        $"{LowerBound.Interval.LowerSign}{LowerBound.Value}, {UpperBound.Value}{UpperBound.Interval.UpperSign}";

    // ---------- 私有助手 / Private helpers ----------

    private (ValueWrapper<T>, Interval) PickLower(ValueRange<T> rhs) {
        Order? cmp = LowerBound.Value.PartialOrd(rhs.LowerBound.Value);
        return cmp switch {
            Order.Less => (LowerBound.Value, LowerBound.Interval),
            Order.Greater => (rhs.LowerBound.Value, rhs.LowerBound.Interval),
            _ => (LowerBound.Value, LowerBound.Interval.Union(rhs.LowerBound.Interval)),
        };
    }

    private (ValueWrapper<T>, Interval) PickUpper(ValueRange<T> rhs) {
        Order? cmp = UpperBound.Value.PartialOrd(rhs.UpperBound.Value);
        return cmp switch {
            Order.Less => (rhs.UpperBound.Value, rhs.UpperBound.Interval),
            Order.Greater => (UpperBound.Value, UpperBound.Interval),
            _ => (UpperBound.Value, UpperBound.Interval.Union(rhs.UpperBound.Interval)),
        };
    }

    private (ValueWrapper<T>, Interval) PickMaxLower(ValueRange<T> rhs) {
        if (LowerBound.Value.IsInfinityOrNegativeInfinity) {
            return (rhs.LowerBound.Value, rhs.LowerBound.Interval);
        }

        if (rhs.LowerBound.Value.IsInfinityOrNegativeInfinity) {
            return (LowerBound.Value, LowerBound.Interval);
        }

        Order? cmp = LowerBound.Value.PartialOrd(rhs.LowerBound.Value);
        return cmp switch {
            Order.Less => (rhs.LowerBound.Value, rhs.LowerBound.Interval),
            Order.Greater => (LowerBound.Value, LowerBound.Interval),
            _ => (LowerBound.Value, LowerBound.Interval.Intersect(rhs.LowerBound.Interval)),
        };
    }

    private (ValueWrapper<T>, Interval) PickMinUpper(ValueRange<T> rhs) {
        if (UpperBound.Value.IsInfinityOrNegativeInfinity) {
            return (rhs.UpperBound.Value, rhs.UpperBound.Interval);
        }

        if (rhs.UpperBound.Value.IsInfinityOrNegativeInfinity) {
            return (UpperBound.Value, UpperBound.Interval);
        }

        Order? cmp = UpperBound.Value.PartialOrd(rhs.UpperBound.Value);
        return cmp switch {
            Order.Less => (UpperBound.Value, UpperBound.Interval),
            Order.Greater => (rhs.UpperBound.Value, rhs.UpperBound.Interval),
            _ => (UpperBound.Value, UpperBound.Interval.Intersect(rhs.UpperBound.Interval)),
        };
    }

    // 顶层扩展 → 静态助手 / Top-level extensions → static helpers
    /// <summary>数值与值范围相加 / Adds a number to value range.</summary>
    public static ValueRange<T>? ScalarPlusRange(T value, ValueRange<T> r) => r + value;

    /// <summary>数值与值范围相乘 / Multiplies a number with value range.</summary>
    public static ValueRange<T>? ScalarTimesRange(T value, ValueRange<T> r) => r * value;

    /// <summary>将数值强制约束在值范围内 / Coerces number within value range.</summary>
    public static T CoerceIn(T value, ValueRange<T> r) {
        T? lb = r.LowerBound.Value.UnwrapOrNull();
        T? ub = r.UpperBound.Value.UnwrapOrNull();
        if (lb is not null && value.Ord(lb.Value) is Order.Less) {
            return lb.Value;
        }

        if (ub is not null && value.Ord(ub.Value) is Order.Greater) {
            return ub.Value;
        }

        return value;
    }
}

/// <summary>ValueRange JSON 转换器工厂 / ValueRange JSON converter factory.</summary>
internal sealed class ValueRangeJsonConverterFactory : JsonConverterFactory {
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ValueRange<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) {
        Type t = typeToConvert.GenericTypeArguments[0];
        Type converterType = typeof(ValueRangeJsonConverter<>).MakeGenericType(t);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>ValueRange ↔ JSON / ValueRange JSON converter.</summary>
public sealed class ValueRangeJsonConverter<T> : JsonConverter<ValueRange<T>>
    where T : struct, IRealNumber<T>, INumberField<T> {
    public override ValueRange<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using var doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;
        ValueWrapper<T> lower = root.GetProperty("lowerBound").Deserialize<ValueWrapper<T>>(options)!;
        ValueWrapper<T> upper = root.GetProperty("upperBound").Deserialize<ValueWrapper<T>>(options)!;
        Interval lbi = root.GetProperty("lowerInterval").GetString() == "open"
            ? new Interval.Open() : (Interval)new Interval.Closed();
        Interval ubi = root.GetProperty("upperInterval").GetString() == "open"
            ? new Interval.Open() : (Interval)new Interval.Closed();
        return ValueRange<T>.Of(lower, upper, lbi, ubi).Value;
    }

    public override void Write(Utf8JsonWriter writer, ValueRange<T> value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WritePropertyName("lowerBound");
        JsonSerializer.Serialize(writer, value.LowerBound.Value, options);
        writer.WritePropertyName("upperBound");
        JsonSerializer.Serialize(writer, value.UpperBound.Value, options);
        writer.WriteString("lowerInterval", value.LowerBound.Interval is Interval.Open ? "open" : "closed");
        writer.WriteString("upperInterval", value.UpperBound.Interval is Interval.Open ? "open" : "closed");
        writer.WriteEndObject();
    }
}
