#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Quantities.Quantity;

/// <summary>
/// 值范围物理量扩展 / Value range quantity extensions.
/// 为 Quantity{ValueRange{V}} 提供下界、上界、差值等属性。
/// Provides lower bound, upper bound, diff, etc. for Quantity{ValueRange{V}}.
/// </summary>
public static class ValueRangeQuantityExtensions {
    /// <summary>
    /// 获取值范围物理量的下界 / Get the lower bound of a value range quantity.
    /// </summary>
    public static Quantity<V> LowerBoundQuantity<V>(this Quantity<ValueRange<V>> quantity)
        where V : struct, IRealNumber<V>, INumberField<V>
        => new(quantity.Value.LowerBound.Value.Unwrap(), quantity.Unit);

    /// <summary>
    /// 获取值范围物理量的上界 / Get the upper bound of a value range quantity.
    /// </summary>
    public static Quantity<V> UpperBoundQuantity<V>(this Quantity<ValueRange<V>> quantity)
        where V : struct, IRealNumber<V>, INumberField<V>
        => new(quantity.Value.UpperBound.Value.Unwrap(), quantity.Unit);

    /// <summary>
    /// 获取值范围物理量的差值（可能为 null）/ Get the diff of a value range quantity (nullable).
    /// </summary>
    public static Quantity<ValueWrapper<V>>? DiffOrNull<V>(this Quantity<ValueRange<V>> quantity)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ValueWrapper<V>? diff = quantity.Value.DiffOrNull;
        return diff != null ? new Quantity<ValueWrapper<V>>(diff, quantity.Unit) : null;
    }

    /// <summary>
    /// 获取值范围物理量的差值结果 / Get the diff result of a value range quantity.
    /// </summary>
    public static Result<Quantity<ValueWrapper<V>>, ErrorCode, Error<ErrorCode>> Diff<V>(
        this Quantity<ValueRange<V>> quantity)
        where V : struct, IRealNumber<V>, INumberField<V> {
        ValueWrapper<V>? diff = quantity.Value.DiffOrNull;
        if (diff == null) {
            return new Failed<Quantity<ValueWrapper<V>>, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "Interval width is undefined: upper and lower bounds cannot be subtracted."));
        }
        return new Ok<Quantity<ValueWrapper<V>>, ErrorCode, Error<ErrorCode>>(
            new Quantity<ValueWrapper<V>>(diff, quantity.Unit));
    }

    /// <summary>
    /// 获取边界物理量的边界值 / Get the bound value of a bound quantity.
    /// </summary>
    public static Quantity<V> BoundValue<V>(this Quantity<Bound<V>> quantity)
        where V : struct, IRealNumber<V>, INumberField<V>
        => new(quantity.Value.Value.Unwrap(), quantity.Unit);

    /// <summary>
    /// 解包值包装器物理量 / Unwrap a value wrapper quantity.
    /// </summary>
    public static Quantity<V> Unwrap<V>(this Quantity<ValueWrapper<V>> quantity)
        where V : struct, IRealNumber<V>, INumberField<V>
        => new(quantity.Value.Unwrap(), quantity.Unit);

    /// <summary>
    /// 解包值包装器物理量（可空）/ Unwrap a value wrapper quantity (nullable).
    /// </summary>
    public static Quantity<V>? UnwrapOrNull<V>(this Quantity<ValueWrapper<V>> quantity)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V? unwrapped = quantity.Value.UnwrapOrNull();
        return unwrapped.HasValue ? new Quantity<V>(unwrapped.Value, quantity.Unit) : null;
    }
}
