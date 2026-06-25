#nullable enable

using System;
using N = Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math
{
    /// <summary>
    /// 随机数扩展方法 / Random extension methods
    /// </summary>
    public static class RandomExtensions
    {
        /// <summary>生成随机 Int64 / Generate random Int64</summary>
        public static N.Int64 NextInt64(this Random rng) => new(rng.NextInt64());

        /// <summary>生成指定范围内的随机 Int64 / Generate random Int64 within range</summary>
        public static N.Int64 NextInt64(this Random rng, long until) => new(rng.NextInt64(until));

        /// <summary>生成指定范围内的随机 Int64 / Generate random Int64 within range</summary>
        public static N.Int64 NextInt64(this Random rng, IntegerRange<N.Int64> range)
        {
            long start = long.Parse(range.Start.ToString());
            long end = long.Parse(range.EndInclusive.ToString());
            return new N.Int64(rng.NextInt64(start, end + 1));
        }

        /// <summary>生成指定范围内的随机 Int64 / Generate random Int64 within range</summary>
        public static N.Int64 NextInt64(this Random rng, long lowerBound, long upperBound) =>
            new(rng.NextInt64(lowerBound, upperBound));

        /// <summary>生成随机 UInt64 / Generate random UInt64</summary>
        public static N.UInt64 NextUInt64(this Random rng) => new((ulong)rng.NextInt64());

        /// <summary>生成指定范围内的随机 UInt64 / Generate random UInt64 within range</summary>
        public static N.UInt64 NextUInt64(this Random rng, ulong until) => new((ulong)rng.NextInt64((long)until));

        /// <summary>生成指定范围内的随机 UInt64 / Generate random UInt64 within range</summary>
        public static N.UInt64 NextUInt64(this Random rng, IntegerRange<N.UInt64> range)
        {
            ulong start = ulong.Parse(range.Start.ToString());
            ulong end = ulong.Parse(range.EndInclusive.ToString());
            return new N.UInt64((ulong)rng.NextInt64((long)start, (long)(end + 1)));
        }

        /// <summary>生成指定范围内的随机 UInt64 / Generate random UInt64 within range</summary>
        public static N.UInt64 NextUInt64(this Random rng, ulong lowerBound, ulong upperBound) =>
            new((ulong)rng.NextInt64((long)lowerBound, (long)upperBound));

        /// <summary>生成随机 Flt64 / Generate random Flt64</summary>
        public static N.Flt64 NextFlt64(this Random rng) => new(rng.NextDouble());

        /// <summary>生成指定范围内的随机 Flt64 / Generate random Flt64 within range</summary>
        public static N.Flt64 NextFlt64(this Random rng, N.Flt64 until) =>
            new(rng.NextDouble() * double.Parse(until.ToString()));

        /// <summary>生成指定范围内的随机 Flt64 / Generate random Flt64 within range</summary>
        public static N.Flt64 NextFlt64(this Random rng, N.Flt64 lowerBound, N.Flt64 upperBound)
        {
            double lo = double.Parse(lowerBound.ToString());
            double hi = double.Parse(upperBound.ToString());
            return new N.Flt64(lo + rng.NextDouble() * (hi - lo));
        }
    }
}
