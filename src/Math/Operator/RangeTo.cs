#nullable enable

using System;

namespace Fuookami.Ospf.Math.Operator
{
    /// <summary>
    /// 范围运算接口 / Range Operation Interface
    /// </summary>
    /// <typeparam name="TRhs">右操作数类型 / Right operand type</typeparam>
    /// <typeparam name="TRet">返回值类型（必须可比较）/ Return type (must be comparable)</typeparam>
    public interface IRangeTo<in TRhs, TRet> where TRet : IComparable<TRet>
    {
        /// <summary>
        /// 创建闭区间 [this, rhs] / Creates closed interval [this, rhs]
        /// </summary>
        /// <param name="rhs">右端点 / Right endpoint</param>
        /// <returns>范围 / Range</returns>
        Range RangeTo(TRhs rhs);

        /// <summary>
        /// 创建半开区间 [this, rhs) / Creates half-open interval [this, rhs)
        /// </summary>
        /// <param name="rhs">右端点 / Right endpoint</param>
        /// <returns>范围 / Range</returns>
        Range Until(TRhs rhs);
    }
}
