#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Quantities.Quantity;

/// <summary>
/// 物理量值类型转换和映射扩展 / Quantity value type conversion and mapping extensions.
/// </summary>
public static class QuantityValueExtensions {
    /// <summary>
    /// 转换为 Flt64 物理量 / Convert to Flt64 quantity.
    /// </summary>
    public static Quantity<Flt64> ToFlt64<V>(this Quantity<V> quantity) where V : struct, IRealNumber<V>
        => new(quantity.Value.ToFlt64(), quantity.Unit);

    /// <summary>
    /// 转换为 FltX 物理量 / Convert to FltX quantity.
    /// </summary>
    public static Quantity<FltX> ToFltXQuantity<V>(this Quantity<V> quantity) where V : struct, IRealNumber<V>
        => new(quantity.Value.ToFltX(), quantity.Unit);

    /// <summary>
    /// 转换为 Int64 物理量 / Convert to Int64 quantity.
    /// </summary>
    public static Quantity<Int64> ToInt64<V>(this Quantity<V> quantity) where V : struct, IRealNumber<V>
        => new(quantity.Value.ToInt64(), quantity.Unit);

    /// <summary>
    /// 向下取整 / Floor the value.
    /// </summary>
    public static Quantity<Flt64> Floor(this Quantity<Flt64> quantity)
        => new(quantity.Value.Floor(), quantity.Unit);

    /// <summary>
    /// 向下取整 / Floor the value.
    /// </summary>
    public static Quantity<FltX> Floor(this Quantity<FltX> quantity)
        => new(quantity.Value.Floor(), quantity.Unit);

    /// <summary>
    /// 向上取整 / Ceil the value.
    /// </summary>
    public static Quantity<Flt64> Ceil(this Quantity<Flt64> quantity)
        => new(quantity.Value.Ceil(), quantity.Unit);

    /// <summary>
    /// 向上取整 / Ceil the value.
    /// </summary>
    public static Quantity<FltX> Ceil(this Quantity<FltX> quantity)
        => new(quantity.Value.Ceil(), quantity.Unit);

    /// <summary>
    /// 四舍五入 / Round the value.
    /// </summary>
    public static Quantity<Flt64> Round(this Quantity<Flt64> quantity)
        => new(quantity.Value.Round(), quantity.Unit);

    /// <summary>
    /// 四舍五入 / Round the value.
    /// </summary>
    public static Quantity<FltX> Round(this Quantity<FltX> quantity)
        => new(quantity.Value.Round(), quantity.Unit);

    /// <summary>
    /// 映射值 / Map value to another type.
    /// </summary>
    public static Quantity<U> MapValue<V, U>(this Quantity<V> quantity, Func<V, U> f)
        => new(f(quantity.Value), quantity.Unit);

    /// <summary>
    /// 尝试映射值（可能失败）/ Try to map value (may fail).
    /// </summary>
    public static Quantity<U>? TryMapValue<V, U>(this Quantity<V> quantity, Func<V, U?> f) where U : struct {
        U? newValue = f(quantity.Value);
        return newValue.HasValue ? new Quantity<U>(newValue.Value, quantity.Unit) : null;
    }

    /// <summary>
    /// 尝试映射值（引用类型）/ Try to map value (reference type).
    /// </summary>
    public static Quantity<U>? TryMapValueRef<V, U>(this Quantity<V> quantity, Func<V, U?> f) where U : class {
        U? newValue = f(quantity.Value);
        return newValue != null ? new Quantity<U>(newValue, quantity.Unit) : null;
    }
}
