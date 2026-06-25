#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Math.Algebra.ValueRange;
/// <summary>
/// 值包装器 / Value wrapper.
/// 密封→抽象 record：Value 普通值、Infinity 正无穷、NegativeInfinity 负无穷。
/// Sealed→abstract record: Value / Infinity / NegativeInfinity.
/// 常量通过 NumericConstantsRegistry.For&lt;T&gt;() 解析，不使用 reified 反射。
/// </summary>
[JsonConverter(typeof(ValueWrapperJsonConverterFactory))]
public abstract record ValueWrapper<T> :
    IOrd<ValueWrapper<T>>,
    IPlus<ValueWrapper<T>, ValueWrapper<T>?>,
    IMinus<ValueWrapper<T>, ValueWrapper<T>?>,
    ITimes<ValueWrapper<T>, ValueWrapper<T>?>,
    IDiv<ValueWrapper<T>, ValueWrapper<T>?>
    where T : struct, IRealNumber<T>, INumberField<T> {
    /// <summary>注册表解析的常量 / Constants resolved via registry.</summary>
    protected static INumericConstants<T> Constants => NumericConstantsRegistry.For<T>();

    /// <summary>是否为正无穷 / Whether is positive infinity.</summary>
    public abstract bool IsInfinity { get; }

    /// <summary>是否为负无穷 / Whether is negative infinity.</summary>
    public abstract bool IsNegativeInfinity { get; }

    /// <summary>是否为无穷（正无穷或负无穷）/ Whether is infinity.</summary>
    public bool IsInfinityOrNegativeInfinity => IsInfinity || IsNegativeInfinity;

    // 与数值 / With scalar
    /// <summary>与数值相加 / Adds with a number.</summary>
    public abstract ValueWrapper<T>? Plus(T rhs);
    /// <summary>与数值相减 / Subtracts with a number.</summary>
    public abstract ValueWrapper<T>? Minus(T rhs);
    /// <summary>与数值相乘 / Multiplies with a number.</summary>
    public abstract ValueWrapper<T>? Times(T rhs);
    /// <summary>与数值相除 / Divides with a number.</summary>
    public abstract ValueWrapper<T>? Div(T rhs);

    // 与值包装器 / With value wrapper
    /// <summary>与值包装器相加 / Adds with a value wrapper.</summary>
    public abstract ValueWrapper<T>? Plus(ValueWrapper<T> rhs);
    /// <summary>与值包装器相减 / Subtracts with a value wrapper.</summary>
    public abstract ValueWrapper<T>? Minus(ValueWrapper<T> rhs);
    /// <summary>与值包装器相乘 / Multiplies with a value wrapper.</summary>
    public abstract ValueWrapper<T>? Times(ValueWrapper<T> rhs);
    /// <summary>与值包装器相除 / Divides with a value wrapper.</summary>
    public abstract ValueWrapper<T>? Div(ValueWrapper<T> rhs);

    /// <summary>转换为 Flt64 / Converts to Flt64.</summary>
    public abstract Flt64 ToFlt64();
    /// <summary>部分相等 / Partial equality.</summary>
    public abstract bool? PartialEq(ValueWrapper<T> rhs);
    /// <summary>部分序比较 / Partial order comparison.</summary>
    public abstract Order? PartialOrd(ValueWrapper<T> rhs);
    /// <summary>复制 / Copy.</summary>
    public abstract ValueWrapper<T> Copy();

    /// <summary>全序比较 / Total order comparison.</summary>
    public Order Ord(ValueWrapper<T> rhs) => PartialOrd(rhs) ?? new Order.Less();

    /// <summary>部分序比较（显式接口实现）/ Partial order comparison (explicit interface).</summary>
    Order? IPartialOrd<ValueWrapper<T>>.PartialOrd(ValueWrapper<T> rhs) => PartialOrd(rhs);

    /// <summary>相等比较 / Equality comparison.</summary>
    public bool Eq(ValueWrapper<T> rhs) => PartialEq(rhs) == true;

    /// <summary>不等比较 / Inequality comparison.</summary>
    public bool Neq(ValueWrapper<T> rhs) => !Eq(rhs);

    /// <summary>比较到（用于 IComparable）/ Compare to (for IComparable).</summary>
    public int CompareTo(ValueWrapper<T>? other) => other is null ? 1 : Ord(other).Value;

    /// <summary>小于 / Less than.</summary>
    public bool Ls(ValueWrapper<T> rhs) => CompareTo(rhs) < 0;
    /// <summary>小于等于 / Less than or equal.</summary>
    public bool Leq(ValueWrapper<T> rhs) => CompareTo(rhs) <= 0;
    /// <summary>大于 / Greater than.</summary>
    public bool Gr(ValueWrapper<T> rhs) => CompareTo(rhs) > 0;
    /// <summary>大于等于 / Greater than or equal.</summary>
    public bool Geq(ValueWrapper<T> rhs) => CompareTo(rhs) >= 0;

    /// <summary>解包获取实际数值 / Unwraps to get actual number.</summary>
    public T Unwrap() => this switch {
        Value v => v.Number,
        Infinity => Constants.PositiveInfinity.Value,
        NegativeInfinity => Constants.NegativeInfinity.Value,
        _ => throw new InvalidOperationException(),
    };

    /// <summary>解包获取实际数值（可空）/ Unwraps to get actual number (nullable).</summary>
    public T? UnwrapOrNull() => this switch {
        Value v => v.Number,
        Infinity => Constants.PositiveInfinity,
        NegativeInfinity => Constants.NegativeInfinity,
        _ => default,
    };

    /// <summary>判断是否等于指定数值（可能为 null）/ Determines if equals specified number (nullable).</summary>
    public bool? EqOrNull(T rhs) {
        INumericConstants<T> c = Constants;
        if (rhs.Equals(c.PositiveInfinity)) {
            return this is Infinity;
        }

        if (rhs.Equals(c.NegativeInfinity)) {
            return this is NegativeInfinity;
        }

        if (c.IsNaN(rhs)) {
            return null;
        }

        return (this as Value)?.Number.Eq(rhs) == true;
    }

    /// <summary>判断是否等于指定数值 / Determines if equals specified number.</summary>
    public bool EqScalar(T rhs) => EqOrNull(rhs) == true;

    // 取负：单一泛型算子 / Unary minus (generic)
    public static ValueWrapper<T> operator -(ValueWrapper<T> w) => w switch {
        Value v => new Value(v.Number.Negate()),
        Infinity => new NegativeInfinity(),
        NegativeInfinity => new Infinity(),
        _ => throw new InvalidOperationException(),
    };

    // 工厂 / Factories
    /// <summary>从数值创建值包装器（注册表解析）/ Creates value wrapper from number (registry-resolved).</summary>
    public static Result<ValueWrapper<T>, ErrorCode, Error<ErrorCode>> Of(T value) {
        INumericConstants<T> c = NumericConstantsRegistry.For<T>();
        if (value.Equals(c.PositiveInfinity)) {
            return new Ok<ValueWrapper<T>, ErrorCode, Error<ErrorCode>>(new Infinity());
        }

        if (value.Equals(c.NegativeInfinity)) {
            return new Ok<ValueWrapper<T>, ErrorCode, Error<ErrorCode>>(new NegativeInfinity());
        }

        if (c.IsNaN(value)) {
            return new Failed<ValueWrapper<T>, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, "Illegal argument NaN for value range!!!");
        }

        return new Ok<ValueWrapper<T>, ErrorCode, Error<ErrorCode>>(new Value(value));
    }

    /// <summary>普通值包装器 / Normal value wrapper.</summary>
    public sealed record Value(T Number) : ValueWrapper<T> {
        public override bool IsInfinity => false;
        public override bool IsNegativeInfinity => false;
        public override ValueWrapper<T> Copy() => new Value(Number);
        public override bool? PartialEq(ValueWrapper<T> rhs) => rhs is Value v && Number.Eq(v.Number);
        public override Order? PartialOrd(ValueWrapper<T> rhs) => rhs switch {
            Value v => Number.Ord(v.Number),
            Infinity => new Order.Less(),
            NegativeInfinity => new Order.Greater(),
            _ => null,
        };
        public override ValueWrapper<T>? Plus(T rhs) => Of(Number.Plus(rhs)).Value;
        public override ValueWrapper<T>? Plus(ValueWrapper<T> rhs) => rhs switch {
            Value v => Of(Number.Plus(v.Number)).Value,
            Infinity => new Infinity(),
            NegativeInfinity => new NegativeInfinity(),
            _ => null,
        };
        public override ValueWrapper<T>? Minus(T rhs) => Of(Number.Minus(rhs)).Value;
        public override ValueWrapper<T>? Minus(ValueWrapper<T> rhs) => rhs switch {
            Value v => Of(Number.Minus(v.Number)).Value,
            Infinity => new NegativeInfinity(),
            NegativeInfinity => new Infinity(),
            _ => null,
        };
        public override ValueWrapper<T>? Times(T rhs) => Of(Number.Times(rhs)).Value;
        public override ValueWrapper<T>? Times(ValueWrapper<T> rhs) => rhs switch {
            Value v => Of(Number.Times(v.Number)).Value,
            Infinity => Number.Ls(Constants.Zero) ? (ValueWrapper<T>)new NegativeInfinity()
                         : Number.Gr(Constants.Zero) ? new Infinity()
                         : new Value(Constants.Zero),
            NegativeInfinity => Number.Ls(Constants.Zero) ? new Infinity()
                                : Number.Gr(Constants.Zero) ? new NegativeInfinity()
                                : new Value(Constants.Zero),
            _ => null,
        };
        public override ValueWrapper<T>? Div(T rhs) => Of(Number.Div(rhs)).Value;
        public override ValueWrapper<T>? Div(ValueWrapper<T> rhs) => rhs switch {
            Value v => Of(Number.Div(v.Number)).Value,
            Infinity => Number.Ls(Constants.Zero) ? new Value(Constants.Epsilon!.Value.Negate())
                        : Number.Gr(Constants.Zero) ? new Value(Constants.Epsilon!.Value)
                        : new Value(Constants.Zero),
            NegativeInfinity => Number.Ls(Constants.Zero) ? new Value(Constants.Epsilon!.Value)
                                : Number.Gr(Constants.Zero) ? new Value(Constants.Epsilon!.Value.Negate())
                                : new Value(Constants.Zero),
            _ => null,
        };
        public override Flt64 ToFlt64() => Number.ToFlt64();
        public override string ToString() => Number.ToString()!;
    }

    /// <summary>正无穷 / Positive infinity.</summary>
    public sealed record Infinity : ValueWrapper<T> {
        public override bool IsInfinity => true;
        public override bool IsNegativeInfinity => false;
        public override ValueWrapper<T> Copy() => new Infinity();
        public override bool? PartialEq(ValueWrapper<T> rhs) => rhs is Infinity;
        public override Order? PartialOrd(ValueWrapper<T> rhs) => rhs is Infinity ? new Order.Equal() : new Order.Greater();
        public override ValueWrapper<T>? Plus(T rhs) =>
            Constants.IsNaN(rhs) || rhs.Equals(Constants.NegativeInfinity) ? null
            : new Infinity();
        public override ValueWrapper<T>? Plus(ValueWrapper<T> rhs) => rhs switch {
            Value => new Infinity(),
            Infinity => new Infinity(),
            NegativeInfinity => null,
            _ => null,
        };
        public override ValueWrapper<T>? Minus(T rhs) =>
            Constants.IsNaN(rhs) || rhs.Equals(Constants.PositiveInfinity) ? null : new Infinity();
        public override ValueWrapper<T>? Minus(ValueWrapper<T> rhs) => rhs switch {
            Value => new Infinity(),
            Infinity => null,
            NegativeInfinity => new Infinity(),
            _ => null,
        };
        public override ValueWrapper<T>? Times(T rhs) {
            if (Constants.IsNaN(rhs)) {
                return null;
            }

            if (rhs.Equals(Constants.NegativeInfinity)) {
                return new NegativeInfinity();
            }

            if (rhs.Equals(Constants.PositiveInfinity)) {
                return new Infinity();
            }

            if (rhs.Equals(Constants.Zero)) {
                return new Value(Constants.Zero);
            }

            return rhs.Ls(Constants.Zero) ? new NegativeInfinity() : new Infinity();
        }
        public override ValueWrapper<T>? Times(ValueWrapper<T> rhs) => rhs switch {
            Value v => v.Number.Ls(Constants.Zero) ? new NegativeInfinity()
                       : v.Number.Gr(Constants.Zero) ? new Infinity()
                       : new Value(Constants.Zero),
            Infinity => new Infinity(),
            NegativeInfinity => new NegativeInfinity(),
            _ => null,
        };
        public override ValueWrapper<T>? Div(T rhs) {
            if (Constants.IsNaN(rhs) || rhs.Equals(Constants.PositiveInfinity)
                || rhs.Equals(Constants.NegativeInfinity) || rhs.Equals(Constants.Zero)) {
                return null;
            }

            return rhs.Ls(Constants.Zero) ? new NegativeInfinity() : new Infinity();
        }
        public override ValueWrapper<T>? Div(ValueWrapper<T> rhs) => rhs switch {
            Value v => v.Number.Ls(Constants.Zero) ? new NegativeInfinity()
                       : v.Number.Gr(Constants.Zero) ? new Infinity()
                       : null,
            Infinity => null,
            NegativeInfinity => null,
            _ => null,
        };
        public override Flt64 ToFlt64() => new(double.PositiveInfinity);
        public override string ToString() => "inf";
    }

    /// <summary>负无穷 / Negative infinity.</summary>
    public sealed record NegativeInfinity : ValueWrapper<T> {
        public override bool IsInfinity => false;
        public override bool IsNegativeInfinity => true;
        public override ValueWrapper<T> Copy() => new NegativeInfinity();
        public override bool? PartialEq(ValueWrapper<T> rhs) => rhs is NegativeInfinity;
        public override Order? PartialOrd(ValueWrapper<T> rhs) => rhs is NegativeInfinity ? new Order.Equal() : new Order.Less();
        public override ValueWrapper<T>? Plus(T rhs) =>
            Constants.IsNaN(rhs) || rhs.Equals(Constants.PositiveInfinity) ? null : new NegativeInfinity();
        public override ValueWrapper<T>? Plus(ValueWrapper<T> rhs) => rhs switch {
            Value => new NegativeInfinity(),
            Infinity => null,
            NegativeInfinity => new NegativeInfinity(),
            _ => null,
        };
        public override ValueWrapper<T>? Minus(T rhs) =>
            Constants.IsNaN(rhs) || rhs.Equals(Constants.NegativeInfinity) ? null : new NegativeInfinity();
        public override ValueWrapper<T>? Minus(ValueWrapper<T> rhs) => rhs switch {
            Value => new NegativeInfinity(),
            Infinity => new NegativeInfinity(),
            NegativeInfinity => null,
            _ => null,
        };
        public override ValueWrapper<T>? Times(T rhs) {
            if (Constants.IsNaN(rhs)) {
                return null;
            }

            if (rhs.Equals(Constants.NegativeInfinity)) {
                return new Infinity();
            }

            if (rhs.Equals(Constants.PositiveInfinity)) {
                return new NegativeInfinity();
            }

            if (rhs.Equals(Constants.Zero)) {
                return new Value(Constants.Zero);
            }

            return rhs.Ls(Constants.Zero) ? new Infinity() : new NegativeInfinity();
        }
        public override ValueWrapper<T>? Times(ValueWrapper<T> rhs) => rhs switch {
            Value v => v.Number.Ls(Constants.Zero) ? new Infinity()
                       : v.Number.Gr(Constants.Zero) ? new NegativeInfinity()
                       : new Value(Constants.Zero),
            Infinity => new NegativeInfinity(),
            NegativeInfinity => new Infinity(),
            _ => null,
        };
        public override ValueWrapper<T>? Div(T rhs) {
            if (Constants.IsNaN(rhs) || rhs.Equals(Constants.NegativeInfinity)
                || rhs.Equals(Constants.PositiveInfinity) || rhs.Equals(Constants.Zero)) {
                return null;
            }

            return rhs.Ls(Constants.Zero) ? new Infinity() : new NegativeInfinity();
        }
        public override ValueWrapper<T>? Div(ValueWrapper<T> rhs) => rhs switch {
            Value v => v.Number.Ls(Constants.Zero) ? new Infinity()
                       : v.Number.Gr(Constants.Zero) ? new NegativeInfinity()
                       : null,
            Infinity => null,
            NegativeInfinity => null,
            _ => null,
        };
        public override Flt64 ToFlt64() => new(double.NegativeInfinity);
        public override string ToString() => "-inf";
    }
}

/// <summary>ValueWrapper JSON 转换器工厂 / ValueWrapper JSON converter factory.</summary>
internal sealed class ValueWrapperJsonConverterFactory : JsonConverterFactory {
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ValueWrapper<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) {
        Type t = typeToConvert.GenericTypeArguments[0];
        Type converterType = typeof(ValueWrapperJsonConverter<>).MakeGenericType(t);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>ValueWrapper ↔ JSON：+∞/-∞ ↔ Double.PositiveInfinity/NegativeInfinity.</summary>
public sealed class ValueWrapperJsonConverter<T> : JsonConverter<ValueWrapper<T>>
    where T : struct, IRealNumber<T>, INumberField<T> {
    public override ValueWrapper<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.String) {
            string? s = reader.GetString();
            if (s == "inf") {
                return new ValueWrapper<T>.Infinity();
            }

            if (s == "-inf") {
                return new ValueWrapper<T>.NegativeInfinity();
            }

            throw new JsonException($"Unknown ValueWrapper string: {s}");
        }
        double d = reader.GetDouble();
        if (double.IsPositiveInfinity(d)) {
            return new ValueWrapper<T>.Infinity();
        }

        if (double.IsNegativeInfinity(d)) {
            return new ValueWrapper<T>.NegativeInfinity();
        }

        INumericConstants<T> c = NumericConstantsRegistry.For<T>();
        if (c is IFlt64ValueConverter<T> converter) {
            T v = converter.IntoValue(new Flt64(d));
            return ValueWrapper<T>.Of(v).Value;
        }
        // Fallback for integer types: convert via double → long → T
        long l = (long)d;
        return ValueWrapper<T>.Of((T)Convert.ChangeType(l, typeof(T))).Value;
    }

    public override void Write(Utf8JsonWriter writer, ValueWrapper<T> value, JsonSerializerOptions options) {
        switch (value) {
            case ValueWrapper<T>.Infinity:
                writer.WriteNumberValue(double.PositiveInfinity);
                break;
            case ValueWrapper<T>.NegativeInfinity:
                writer.WriteNumberValue(double.NegativeInfinity);
                break;
            case ValueWrapper<T>.Value v:
                double d = v.Number.ToFlt64().Value;
                writer.WriteNumberValue(d);
                break;
            default:
                throw new JsonException("Unknown ValueWrapper type");
        }
    }
}
