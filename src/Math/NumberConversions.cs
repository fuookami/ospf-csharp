#nullable enable

using System;
using N = Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math;
/// <summary>
/// 数值转换扩展方法 / Number conversion extension methods
/// </summary>
public static class NumberConversions {
    // bool conversions
    /// <summary>布尔值转 Int8 / Bool to Int8</summary>
    public static N.Int8 ToInt8(this bool value) => value ? N.Int8.One : N.Int8.Zero;
    /// <summary>布尔值转 UInt8 / Bool to UInt8</summary>
    public static N.UInt8 ToUInt8(this bool value) => value ? N.UInt8.One : N.UInt8.Zero;
    /// <summary>布尔值转 UInt64 / Bool to UInt64</summary>
    public static N.UInt64 ToUInt64(this bool value) => value ? N.UInt64.One : N.UInt64.Zero;
    /// <summary>布尔值转 Flt64 / Bool to Flt64</summary>
    public static N.Flt64 ToFlt64(this bool value) => value ? N.Flt64.One : N.Flt64.Zero;

    // Flt64 conversions
    /// <summary>Flt64 转 UInt64 / Flt64 to UInt64</summary>
    public static N.UInt64 ToUInt64(this N.Flt64 value) => new((ulong)value.Value);

    // string conversions - Int8
    /// <summary>字符串转 Int8 / String to Int8</summary>
    public static N.Int8 ToInt8(this string value) => new(sbyte.Parse(value));
    /// <summary>字符串转 Int8（可能为 null）/ String to Int8 (nullable)</summary>
    public static N.Int8? ToInt8OrNull(this string value) => sbyte.TryParse(value, out sbyte r) ? new N.Int8(r) : null;

    // string conversions - Int16
    /// <summary>字符串转 Int16 / String to Int16</summary>
    public static N.Int16 ToInt16(this string value) => new(short.Parse(value));
    /// <summary>字符串转 Int16（可能为 null）/ String to Int16 (nullable)</summary>
    public static N.Int16? ToInt16OrNull(this string value) => short.TryParse(value, out short r) ? new N.Int16(r) : null;

    // string conversions - Int64
    /// <summary>字符串转 Int64 / String to Int64</summary>
    public static N.Int64 ToInt64(this string value) => new(long.Parse(value));
    /// <summary>字符串转 Int64（可能为 null）/ String to Int64 (nullable)</summary>
    public static N.Int64? ToInt64OrNull(this string value) => long.TryParse(value, out long r) ? new N.Int64(r) : null;

    // string conversions - IntX
    /// <summary>字符串转 IntX / String to IntX</summary>
    public static N.IntX ToIntX(this string value, int radix = 10) => new(System.Numerics.BigInteger.Parse(value));
    /// <summary>字符串转 IntX（可能为 null）/ String to IntX (nullable)</summary>
    public static N.IntX? ToIntXOrNull(this string value, int radix = 10) =>
        System.Numerics.BigInteger.TryParse(value, out System.Numerics.BigInteger r) ? new N.IntX(r) : null;

    // string conversions - UInt8
    /// <summary>字符串转 UInt8 / String to UInt8</summary>
    public static N.UInt8 ToUInt8(this string value) => new(byte.Parse(value));
    /// <summary>字符串转 UInt8（可能为 null）/ String to UInt8 (nullable)</summary>
    public static N.UInt8? ToUInt8OrNull(this string value) => byte.TryParse(value, out byte r) ? new N.UInt8(r) : null;

    // string conversions - UInt16
    /// <summary>字符串转 UInt16 / String to UInt16</summary>
    public static N.UInt16 ToUInt16(this string value) => new(ushort.Parse(value));
    /// <summary>字符串转 UInt16（可能为 null）/ String to UInt16 (nullable)</summary>
    public static N.UInt16? ToUInt16OrNull(this string value) => ushort.TryParse(value, out ushort r) ? new N.UInt16(r) : null;

    // string conversions - UInt32
    /// <summary>字符串转 UInt32 / String to UInt32</summary>
    public static N.UInt32 ToUInt32(this string value) => new(uint.Parse(value));
    /// <summary>字符串转 UInt32（可能为 null）/ String to UInt32 (nullable)</summary>
    public static N.UInt32? ToUInt32OrNull(this string value) => uint.TryParse(value, out uint r) ? new N.UInt32(r) : null;

    // string conversions - UInt64
    /// <summary>字符串转 UInt64 / String to UInt64</summary>
    public static N.UInt64 ToUInt64(this string value) => new(ulong.Parse(value));
    /// <summary>字符串转 UInt64（可能为 null）/ String to UInt64 (nullable)</summary>
    public static N.UInt64? ToUInt64OrNull(this string value) => ulong.TryParse(value, out ulong r) ? new N.UInt64(r) : null;

    // string conversions - UIntX
    /// <summary>字符串转 UIntX / String to UIntX</summary>
    public static N.UIntX ToUIntX(this string value, int radix = 10) => new(System.Numerics.BigInteger.Parse(value));
    /// <summary>字符串转 UIntX（可能为 null）/ String to UIntX (nullable)</summary>
    public static N.UIntX? ToUIntXOrNull(this string value, int radix = 10) =>
        System.Numerics.BigInteger.TryParse(value, out System.Numerics.BigInteger r) ? new N.UIntX(r) : null;

    // string conversions - Flt32
    /// <summary>字符串转 Flt32 / String to Flt32</summary>
    public static N.Flt32 ToFlt32(this string value) => new(float.Parse(value));
    /// <summary>字符串转 Flt32（可能为 null）/ String to Flt32 (nullable)</summary>
    public static N.Flt32? ToFlt32OrNull(this string value) => float.TryParse(value, out float r) ? new N.Flt32(r) : null;

    // string conversions - Flt64
    /// <summary>字符串转 Flt64 / String to Flt64</summary>
    public static N.Flt64 ToFlt64(this string value) => new(double.Parse(value));
    /// <summary>字符串转 Flt64（可能为 null）/ String to Flt64 (nullable)</summary>
    public static N.Flt64? ToFlt64OrNull(this string value) => double.TryParse(value, out double r) ? new N.Flt64(r) : null;

    // string conversions - FltX
    /// <summary>字符串转 FltX / String to FltX</summary>
    public static N.FltX ToFltX(this string value) => new(double.Parse(value));
    /// <summary>字符串转 FltX（可能为 null）/ String to FltX (nullable)</summary>
    public static N.FltX? ToFltXOrNull(this string value) => double.TryParse(value, out double r) ? new N.FltX(r) : null;
}
