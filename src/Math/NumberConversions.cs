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

    /// <summary>
    /// **把任意数值类型转成 `FltX`** / Convert any numeric type to `FltX`.
    /// </summary>
    /// <typeparam name="V">数值类型 / The numeric type.</typeparam>
    /// <param name="value">数值 / The value.</param>
    /// <returns>`FltX` 表示 / The `FltX` representation.</returns>
    /// <remarks>
    /// 基线的 `Product&lt;V : RealNumber&gt;.toRenderDto()` 直接调 `toFltX()`——**接口上有这个方法**，所以十二个数值类型全通。
    /// C# 的 `IRealNumber&lt;TSelf&gt;` 上也有 `ToFltX()`，但调用方只约束到 `where V : struct`，**泛型参数上拿不到接口成员**，
    /// 于是每个调用点各写一段"认得出 `Flt64` / `FltX` / `Int64` / `UInt64`"的 `is` 链，其余类型抛 `NotSupportedException`。
    /// 两处调用点（渲染映射、解渲染）各缺一半，`Flt32` / `Int32` / `UInt32` 在运行期炸。
    ///
    /// 本方法把这条链**收成一处**并补全到十二个类型。仍然是个运行期 `switch`，而不是约束——
    /// 把 `where V : struct` 改成 `where V : struct, IRealNumber&lt;V&gt;` 会顺着 `Product&lt;V&gt;` / `Csp1dSolution&lt;V&gt;`
    /// 等几十个声明级联，与本缺陷的收益不成比例。
    ///
    /// The baseline's `Product&lt;V : RealNumber&gt;.toRenderDto()` calls `toFltX()` straight off the interface, so all twelve numeric types work. C#'s `IRealNumber&lt;TSelf&gt;` carries
    /// `ToFltX()` too, but callers constrain only `where V : struct`, **and interface members are unreachable through such a type parameter**, so every call site grew its own `is` chain
    /// recognising `Flt64` / `FltX` / `Int64` / `UInt64` and throwing `NotSupportedException` for the rest. Each of the two call sites was missing a different half, so `Flt32` / `Int32` /
    /// `UInt32` blew up at run time. This method **consolidates the chain in one place** and completes it for all twelve types. It is still a run-time `switch` rather than a constraint:
    /// changing `where V : struct` to `where V : struct, IRealNumber&lt;V&gt;` would cascade through dozens of declarations such as `Product&lt;V&gt;` and `Csp1dSolution&lt;V&gt;`, out of
    /// proportion to what this defect is worth.
    /// </remarks>
    public static N.FltX ToFltXValue<V>(this V value) where V : struct =>
        value switch {
            N.FltX fx => fx,
            N.Flt64 f64 => f64.ToFltX(),
            N.Flt32 f32 => f32.ToFltX(),
            N.Int64 i64 => i64.ToFltX(),
            N.Int32 i32 => i32.ToFltX(),
            N.Int16 i16 => i16.ToFltX(),
            N.Int8 i8 => i8.ToFltX(),
            N.UInt64 u64 => u64.ToFltX(),
            N.UInt32 u32 => u32.ToFltX(),
            N.UInt16 u16 => u16.ToFltX(),
            N.UInt8 u8 => u8.ToFltX(),
            N.IntX ix => ix.ToFltX(),
            N.UIntX ux => ux.ToFltX(),
            _ => throw new NotSupportedException($"Conversion to FltX not supported for type {typeof(V).Name}"),
        };
}
