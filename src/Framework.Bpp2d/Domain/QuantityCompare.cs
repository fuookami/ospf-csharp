// 量值比较辅助 / Quantity comparison helpers.
// 二维几何各文件都需要同一套"比较失败即条件不成立"的判定，集中一处，避免每个文件各写一份。
// Every 2D geometry file needs the same "comparison failure means the predicate is false" rule; keeping one copy
// avoids each file growing its own.

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 量值比较辅助 / Quantity comparison helpers.
/// </summary>
/// <remarks>
/// 量纲不可比时一律返回 <c>false</c>：几何判定里遇到不可比较的尺寸，视为"不重叠 / 不包含 / 可容纳不成立"
/// 比抛出异常更安全——错一件不等于炸掉整趟排样。
/// An incomparable dimension always yields <c>false</c>: in a geometry test, reporting "does not overlap / is not
/// inside / does not fit" is safer than throwing — one bad item should not take down a whole layout run.
/// </remarks>
internal static class QuantityCompare {
    /// <summary>大于等于 / Greater than or equal.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <param name="axis">轴名，用于错误信息 / Axis name, for the error message.</param>
    /// <returns>是否成立 / Whether it holds.</returns>
    public static bool AtLeast<V>(Quantity<V> lhs, Quantity<V> rhs, string axis)
        where V : struct, IFloatingNumber<V> =>
        Compare(lhs, rhs, axis) is { Comparable: true, Ord: Order.Greater or Order.Equal };

    /// <summary>小于等于 / Less than or equal.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <param name="axis">轴名，用于错误信息 / Axis name, for the error message.</param>
    /// <returns>是否成立 / Whether it holds.</returns>
    public static bool AtMost<V>(Quantity<V> lhs, Quantity<V> rhs, string axis)
        where V : struct, IFloatingNumber<V> =>
        Compare(lhs, rhs, axis) is { Comparable: true, Ord: Order.Less or Order.Equal };

    /// <summary>严格大于 / Strictly greater.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <param name="axis">轴名，用于错误信息 / Axis name, for the error message.</param>
    /// <returns>是否成立 / Whether it holds.</returns>
    public static bool StrictlyGreater<V>(Quantity<V> lhs, Quantity<V> rhs, string axis)
        where V : struct, IFloatingNumber<V> =>
        Compare(lhs, rhs, axis) is { Comparable: true, Ord: Order.Greater };

    /// <summary>量值的零值 / Zero value of a quantity.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="sample">样本量值 / Sample quantity.</param>
    /// <returns>零量值 / Zero quantity.</returns>
    public static Quantity<V> ZeroOf<V>(Quantity<V> sample)
        where V : struct, IFloatingNumber<V> => QuantityOps.QuantityZeroOf(sample);

    /// <summary>
    /// 比较并报告可比性 / Compare and report comparability.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左操作数 / Left operand.</param>
    /// <param name="rhs">右操作数 / Right operand.</param>
    /// <param name="axis">轴名，用于错误信息 / Axis name, for the error message.</param>
    /// <returns>比较结果与是否可比 / The order and whether the operands were comparable.</returns>
    public static (Order? Ord, bool Comparable) Compare<V>(
        Quantity<V> lhs,
        Quantity<V> rhs,
        string axis)
        where V : struct, IFloatingNumber<V> {
        Result<Order, ErrorCode, Error<ErrorCode>> result =
            QuantityOps.OrdSafe(lhs, rhs, axis);
        return result.IsOk ? (result.Value, true) : (null, false);
    }
}
