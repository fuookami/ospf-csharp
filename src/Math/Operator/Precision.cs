#nullable enable

using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Operator;
/// <summary>
/// 精度比较类 / Precision comparison class
/// </summary>
/// <typeparam name="T">数值类型 / Numeric type</typeparam>
public sealed class Precision<T>
    where T : IMinus<T, T>, IAbs<T>, IOrd<T> {
    private readonly T _precision;

    /// <summary>
    /// 创建精度比较实例 / Creates a precision comparison instance
    /// </summary>
    /// <param name="precision">精度值 / Precision value</param>
    public Precision(T precision) {
        _precision = precision.Abs();
    }

    /// <summary>
    /// 精度值 / Precision value
    /// </summary>
    public T Value => _precision;

    /// <summary>
    /// 判断是否相等 / Determines if equal
    /// </summary>
    public bool Equal(T lhs, T rhs) =>
        lhs.Minus(rhs).Abs().Leq(_precision);

    /// <summary>
    /// 三路比较 / Three-way comparison
    /// </summary>
    public Order Order(T lhs, T rhs) {
        if (Equal(lhs, rhs)) {
            return new Order.Equal();
        }
        return lhs.Ls(rhs) ? new Order.Less() : new Order.Greater();
    }

    /// <summary>
    /// 判断是否不等 / Determines if unequal
    /// </summary>
    public bool Unequal(T lhs, T rhs) =>
        lhs.Minus(rhs).Abs().Gr(_precision);

    /// <summary>
    /// 判断是否小于 / Determines if less than
    /// </summary>
    public bool Less(T lhs, T rhs) =>
        rhs.Minus(lhs).Abs().Geq(_precision) && lhs.Ls(rhs);

    /// <summary>
    /// 判断是否小于等于 / Determines if less than or equal
    /// </summary>
    public bool LessEqual(T lhs, T rhs) =>
        lhs.Minus(rhs).Abs().Leq(_precision) || lhs.Ls(rhs);

    /// <summary>
    /// 判断是否大于 / Determines if greater than
    /// </summary>
    public bool Greater(T lhs, T rhs) =>
        lhs.Minus(rhs).Abs().Geq(_precision) && lhs.Gr(rhs);

    /// <summary>
    /// 判断是否大于等于 / Determines if greater than or equal
    /// </summary>
    public bool GreaterEqual(T lhs, T rhs) =>
        rhs.Minus(lhs).Abs().Leq(_precision) || lhs.Gr(rhs);
}
