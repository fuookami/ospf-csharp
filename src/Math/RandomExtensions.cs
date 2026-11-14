#nullable enable

using System;
using N = Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math;
/// <summary>
/// 随机数扩展方法 / Random extension methods
/// </summary>
public static class RandomExtensions {
    /// <summary>生成随机 Int64 / Generate random Int64</summary>
    public static N.Int64 NextInt64(this Random rng) => new(rng.NextInt64());

    /// <summary>生成指定范围内的随机 Int64 / Generate random Int64 within range</summary>
    public static N.Int64 NextInt64(this Random rng, long until) => new(rng.NextInt64(until));

    /// <summary>生成指定范围内的随机 Int64 / Generate random Int64 within range</summary>
    public static N.Int64 NextInt64(this Random rng, IntegerRange<N.Int64> range) {
        long start = range.Start.ToLong();
        long end = range.EndInclusive.ToLong();
        if (start > end) {
            throw new ArgumentOutOfRangeException(nameof(range), "Range start must not exceed range end.");
        }

        ulong width = unchecked((ulong)(end - start) + 1UL);
        if (width == 0) {
            return new N.Int64(unchecked((long)NextRawUInt64(rng)));
        }

        return new N.Int64(unchecked((long)((ulong)start + NextUInt64Below(rng, width))));
    }

    /// <summary>生成指定范围内的随机 Int64 / Generate random Int64 within range</summary>
    public static N.Int64 NextInt64(this Random rng, long lowerBound, long upperBound) =>
        new(rng.NextInt64(lowerBound, upperBound));

    /// <summary>生成随机 UInt64 / Generate random UInt64</summary>
    public static N.UInt64 NextUInt64(this Random rng) => new(NextRawUInt64(rng));

    /// <summary>生成指定范围内的随机 UInt64 / Generate random UInt64 within range</summary>
    public static N.UInt64 NextUInt64(this Random rng, ulong until) =>
        new(NextUInt64Below(rng, until));

    /// <summary>生成指定范围内的随机 UInt64 / Generate random UInt64 within range</summary>
    public static N.UInt64 NextUInt64(this Random rng, IntegerRange<N.UInt64> range) {
        ulong start = range.Start.ToULong();
        ulong end = range.EndInclusive.ToULong();
        if (start > end) {
            throw new ArgumentOutOfRangeException(nameof(range), "Range start must not exceed range end.");
        }

        ulong width = end - start + 1UL;
        return new N.UInt64(width == 0
            ? NextRawUInt64(rng)
            : start + NextUInt64Below(rng, width));
    }

    /// <summary>生成指定范围内的随机 UInt64 / Generate random UInt64 within range</summary>
    public static N.UInt64 NextUInt64(this Random rng, ulong lowerBound, ulong upperBound) {
        if (lowerBound >= upperBound) {
            throw new ArgumentOutOfRangeException(nameof(upperBound), "Upper bound must exceed lower bound.");
        }

        return new N.UInt64(lowerBound + NextUInt64Below(rng, upperBound - lowerBound));
    }

    /// <summary>生成随机 Flt64 / Generate random Flt64</summary>
    public static N.Flt64 NextFlt64(this Random rng) => new(rng.NextDouble());

    /// <summary>生成指定范围内的随机 Flt64 / Generate random Flt64 within range</summary>
    public static N.Flt64 NextFlt64(this Random rng, N.Flt64 until) =>
        new(rng.NextDouble() * until.ToDouble());

    /// <summary>生成指定范围内的随机 Flt64 / Generate random Flt64 within range</summary>
    public static N.Flt64 NextFlt64(this Random rng, N.Flt64 lowerBound, N.Flt64 upperBound) {
        double lo = lowerBound.ToDouble();
        double hi = upperBound.ToDouble();
        return new N.Flt64(lo + rng.NextDouble() * (hi - lo));
    }

    private static ulong NextRawUInt64(Random rng) {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        rng.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private static ulong NextUInt64Below(Random rng, ulong bound) {
        if (bound == 0) {
            throw new ArgumentOutOfRangeException(nameof(bound), "Upper bound must be positive.");
        }

        ulong threshold = unchecked(0UL - bound) % bound;
        while (true) {
            ulong value = NextRawUInt64(rng);
            if (value >= threshold) {
                return value % bound;
            }
        }
    }
}
