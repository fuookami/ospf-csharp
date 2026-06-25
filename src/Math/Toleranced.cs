#nullable enable

using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math;
/// <summary>
/// 容差比较默认实现 / Default toleranced comparison implementations
/// </summary>
public static class Toleranced {
    /// <summary>
    /// 默认容差相等比较 / Default toleranced equality comparison
    /// </summary>
    public static TolerancedEq<T> DefaultEq<T>()
        where T : IMinus<T, T>, IAbs<T>, IOrd<T> {
        return (T lhs, T rhs, T tolerance) => {
            T diff = lhs.Geq(rhs) ? lhs.Minus(rhs) : rhs.Minus(lhs);
            return diff.Abs().Leq(tolerance);
        };
    }

    /// <summary>
    /// 默认容差排序比较 / Default toleranced ordering comparison
    /// </summary>
    public static TolerancedOrd<T> DefaultOrd<T>()
        where T : IMinus<T, T>, IAbs<T>, IOrd<T> {
        return (T lhs, T rhs, T tolerance) => {
            T diff = lhs.Geq(rhs) ? lhs.Minus(rhs) : rhs.Minus(lhs);
            if (diff.Abs().Leq(tolerance)) {
                return new Order.Equal();
            }
            return lhs.Ls(rhs) ? new Order.Less() : new Order.Greater();
        };
    }
}
