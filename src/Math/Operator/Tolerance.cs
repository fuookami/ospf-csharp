#nullable enable

using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Operator
{
    /// <summary>
    /// 容差接口 / Tolerance Interface
    /// </summary>
    /// <typeparam name="T">容差值类型 / Tolerance value type</typeparam>
    public interface ITolerance<T>
    {
        /// <summary>
        /// 容差值 / Tolerance value
        /// </summary>
        T Tolerance { get; }
    }

    /// <summary>
    /// 绝对容差记录 / Absolute Tolerance Record
    /// </summary>
    /// <typeparam name="T">容差值类型 / Tolerance value type</typeparam>
    /// <param name="Tolerance">容差值 / Tolerance value</param>
    public sealed record AbsoluteTolerance<T>(T Tolerance) : ITolerance<T>;

    /// <summary>
    /// 容差相等比较委托 / Toleranced equality comparison delegate
    /// </summary>
    /// <typeparam name="T">比较值类型 / Comparison value type</typeparam>
    /// <param name="lhs">左操作数 / Left operand</param>
    /// <param name="rhs">右操作数 / Right operand</param>
    /// <param name="tolerance">容差 / Tolerance</param>
    /// <returns>是否相等 / Whether equal</returns>
    public delegate bool TolerancedEq<T>(T lhs, T rhs, T tolerance);

    /// <summary>
    /// 容差排序比较委托 / Toleranced ordering comparison delegate
    /// </summary>
    /// <typeparam name="T">比较值类型 / Comparison value type</typeparam>
    /// <param name="lhs">左操作数 / Left operand</param>
    /// <param name="rhs">右操作数 / Right operand</param>
    /// <param name="tolerance">容差 / Tolerance</param>
    /// <returns>排序结果 / Order result</returns>
    public delegate Order TolerancedOrd<T>(T lhs, T rhs, T tolerance);
}
