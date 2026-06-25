#nullable enable

using Fuookami.Ospf.Utils.Functional;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fuookami.Ospf.Math.Algebra.ValueRange;
/// <summary>
/// 区间类型 / Interval type.
/// 表示区间的开闭性质：Open 开区间、Closed 闭区间。
/// Represents openness/closedness of an interval: Open / Closed.
/// </summary>
[JsonConverter(typeof(IntervalJsonConverter))]
public abstract record Interval {
    /// <summary>下边界符号 / Lower bound symbol.</summary>
    public abstract string LowerSign { get; }

    /// <summary>上边界符号 / Upper bound symbol.</summary>
    public abstract string UpperSign { get; }

    /// <summary>并集类型：Open∪x=x，Closed∪x=Closed / Union type.</summary>
    public abstract Interval Union(Interval rhs);

    /// <summary>交集类型：Open∩x=Open，Closed∩x=x / Intersection type.</summary>
    public abstract Interval Intersect(Interval rhs);

    /// <summary>当前区间是否在另一区间外部（更宽松）/ Outer (more relaxed) predicate.</summary>
    public abstract bool Outer(Interval rhs);

    /// <summary>下边界比较算子：Open→严格 &lt;，Closed→&lt;= / Lower bound operator.</summary>
    public Func<T, T, bool> LowerBoundOperator<T>() where T : IOrd<T> {
        return this is Open
            ? (lhs, rhs) => lhs.Ord(rhs) is Order.Less
            : (lhs, rhs) => lhs.Ord(rhs) is Order.Less or Order.Equal;
    }

    /// <summary>上边界比较算子：Open→严格 &gt;，Closed→&gt;= / Upper bound operator.</summary>
    public Func<T, T, bool> UpperBoundOperator<T>() where T : IOrd<T> {
        return this is Open
            ? (lhs, rhs) => lhs.Ord(rhs) is Order.Greater
            : (lhs, rhs) => lhs.Ord(rhs) is Order.Greater or Order.Equal;
    }

    /// <summary>开区间 / Open interval: (a, b).</summary>
    public sealed record Open : Interval {
        public override string LowerSign => "(";
        public override string UpperSign => ")";
        public override Interval Union(Interval rhs) => rhs;
        public override Interval Intersect(Interval rhs) => this;
        public override bool Outer(Interval rhs) => false;
    }

    /// <summary>闭区间 / Closed interval: [a, b].</summary>
    public sealed record Closed : Interval {
        public override string LowerSign => "[";
        public override string UpperSign => "]";
        public override Interval Union(Interval rhs) => this;
        public override Interval Intersect(Interval rhs) => rhs;
        public override bool Outer(Interval rhs) => rhs is Open;
    }
}

/// <summary>Interval 与 "open"/"closed" 小写字符串互转 / Interval ↔ lowercase string.</summary>
public sealed class IntervalJsonConverter : JsonConverter<Interval> {
    public override Interval? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        string s = reader.GetString() ?? throw new JsonException("Interval token null");
        return s switch {
            "open" => new Interval.Open(),
            "closed" => new Interval.Closed(),
            _ => throw new JsonException($"Unknown interval: {s}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, Interval value, JsonSerializerOptions options) {
        string s = value switch {
            Interval.Open => "open",
            Interval.Closed => "closed",
            _ => throw new JsonException($"Unknown interval: {value}"),
        };
        writer.WriteStringValue(s);
    }
}
