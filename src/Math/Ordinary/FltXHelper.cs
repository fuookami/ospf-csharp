#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Ordinary
{
    /// <summary>
    /// FltX 辅助方法 / FltX helper methods.
    /// <para>FltX 内部使用 decimal，缺少 Kotlin FltX 的 WithScale/Round/StripTrailingZeros/ToIntX 方法。</para>
    /// <para>FltX uses decimal internally; provides helpers for Kotlin FltX methods not yet on the struct.</para>
    /// </summary>
    internal static class FltXHelper
    {
        /// <summary>对 FltX 值进行指定精度的四舍五入 / Round FltX to specified decimal places.</summary>
        internal static FltX WithScale(this FltX value, int scale, MidpointRounding mode)
            => new(decimal.Round(value.Value, scale, mode));

        /// <summary>对 FltX 值四舍五入到整数 / Round FltX to nearest integer.</summary>
        internal static FltX Round(this FltX value)
            => new(decimal.Round(value.Value, MidpointRounding.AwayFromZero));

        /// <summary>取 FltX 的整数部分（向零取整）/ Floor of FltX (toward zero for positive).</summary>
        internal static FltX Floor(this FltX value)
            => new(decimal.Floor(value.Value));

        /// <summary>去掉 FltX 尾部的零 / Strip trailing zeros from FltX.</summary>
        internal static FltX StripTrailingZeros(this FltX value)
            => new(decimal.Parse(value.Value.ToString().TrimEnd('0').TrimEnd('.')));

        /// <summary>将 FltX 转换为 IntX / Convert FltX to IntX.</summary>
        internal static IntX ToIntX(this FltX value)
            => new(new BigInteger(value.Value));

        /// <summary>对 Flt64 值取整 / Floor of Flt64.</summary>
        internal static Flt64 Floor(this Flt64 value)
            => new(global::System.Math.Floor(value.Value));

        /// <summary>将多个 FltX 缩放为整数列表（共用辅助）/ Scale multiple FltX values to integers (shared helper).</summary>
        internal static (List<IntX> IntegerNumbers, int Factor) ScaleToIntegers(IEnumerable<FltX> numbers)
        {
            var factor = 0;
            var scaled = new List<FltX>(numbers);
            while (true)
            {
                var integerNumbers = new List<IntX>();
                var allInteger = true;
                foreach (var num in scaled)
                {
                    if (num.Round().Eq(num))
                    {
                        integerNumbers.Add(num.ToIntX().Abs());
                    }
                    else
                    {
                        factor++;
                        for (var i = 0; i < scaled.Count; i++)
                            scaled[i] = scaled[i] * FltXConstants.Instance.Ten;
                        allInteger = false;
                        break;
                    }
                }
                if (allInteger) return (integerNumbers, factor);
            }
        }
    }
}
