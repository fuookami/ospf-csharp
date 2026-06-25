#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;
/// <summary>
/// 物理量算术辅助类（内部共享）
/// Quantity arithmetic helpers (internal shared).
/// Mirrors the 6 Kotlin private file-level functions from RectangularPackingDemand.kt.
/// </summary>
internal static class QuantityArithmetic {
    /// <summary>
    /// 量值加法 / Quantity addition.
    /// Dimension mismatch throws ArgumentException (mirrors Kotlin .value!! force-unwrap).
    /// </summary>
    public static Quantity<V> Plus<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> result = lhs.Add(rhs);
        if (result.IsFailed) {
            throw new ArgumentException(result is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f
                ? f.Message
                : "Quantity addition failed.");
        }

        return result.Value;
    }

    /// <summary>
    /// 量值减法 / Quantity subtraction.
    /// Dimension mismatch throws ArgumentException (mirrors Kotlin .value!! force-unwrap).
    /// </summary>
    public static Quantity<V> Minus<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> result = lhs.Subtract(rhs);
        if (result.IsFailed) {
            throw new ArgumentException(result is Failed<Quantity<V>, ErrorCode, Error<ErrorCode>> f
                ? f.Message
                : "Quantity subtraction failed.");
        }

        return result.Value;
    }

    /// <summary>
    /// 量值乘法 / Quantity multiplication.
    /// </summary>
    public static Quantity<V> Product<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> => lhs.Multiply(rhs);

    /// <summary>
    /// 量值比较 / Quantity comparison.
    /// Returns the Order result; throws on incomparable (mirrors Kotlin .value!!).
    /// </summary>
    public static Order Compare<V>(Quantity<V> lhs, Quantity<V> rhs, string axis)
        where V : struct, IFloatingNumber<V> {
        Result<Order, ErrorCode, Error<ErrorCode>> result = QuantityOps.OrdSafe(lhs, rhs, axis);
        if (result.IsFailed) {
            throw new ArgumentException(result is Failed<Order, ErrorCode, Error<ErrorCode>> f
                ? f.Message
                : $"Incomparable quantity on axis {axis}.");
        }

        return result.Value;
    }

    /// <summary>
    /// 量值取最大 / Quantity max.
    /// </summary>
    public static Quantity<V> Max<V>(Quantity<V> lhs, Quantity<V> rhs, string axis)
        where V : struct, IFloatingNumber<V> {
        Order ord = Compare(lhs, rhs, axis);
        return ord is Order.Greater or Order.Equal ? lhs : rhs;
    }

    /// <summary>
    /// 量值取最小 / Quantity min.
    /// </summary>
    public static Quantity<V> Min<V>(Quantity<V> lhs, Quantity<V> rhs, string axis)
        where V : struct, IFloatingNumber<V> {
        Order ord = Compare(lhs, rhs, axis);
        return ord is Order.Greater ? rhs : lhs;
    }

    /// <summary>
    /// 获取量值的零值 / Get zero value for a quantity.
    /// </summary>
    public static Quantity<V> ZeroOf<V>(Quantity<V> quantity)
        where V : struct, IFloatingNumber<V> => QuantityOps.QuantityZeroOf(quantity);
}
