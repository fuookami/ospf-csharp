#nullable enable

namespace Fuookami.Ospf.Math.Algebra.ValueRange;
/// <summary>区间类型标记 / Interval-kind marker (phantom-type root).</summary>
public abstract record IntervalKind {
    /// <summary>对应的 Interval 值 / Corresponding Interval value.</summary>
    public abstract Interval Interval { get; }

    /// <summary>闭区间标记 / Closed-interval marker.</summary>
    public sealed record ClosedIntervalKind : IntervalKind {
        public override Interval Interval => new Interval.Closed();
    }

    /// <summary>开区间标记 / Open-interval marker.</summary>
    public sealed record OpenIntervalKind : IntervalKind {
        public override Interval Interval => new Interval.Open();
    }

    /// <summary>运行时区间标记 / Runtime interval marker (dynamic).</summary>
    public sealed record RuntimeIntervalKind(Interval Interval) : IntervalKind {
        public override Interval Interval { get; } = Interval;
    }
}
