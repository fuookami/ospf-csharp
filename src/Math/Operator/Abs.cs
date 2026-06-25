#nullable enable

namespace Fuookami.Ospf.Math.Operator
{
    /// <summary>
    /// 绝对值运算接口 / Absolute Value Operation Interface
    /// </summary>
    /// <typeparam name="TRet">返回值类型 / Return type</typeparam>
    public interface IAbs<out TRet>
    {
        /// <summary>
        /// 计算绝对值 / Calculates the absolute value
        /// </summary>
        /// <returns>绝对值 / Absolute value</returns>
        TRet Abs();
    }

    /// <summary>
    /// 绝对值运算辅助方法 / Absolute value operation helpers
    /// </summary>
    public static class AbsOperators
    {
        /// <summary>
        /// 计算数值的绝对值 / Calculates the absolute value of a number
        /// </summary>
        /// <typeparam name="T">返回值类型 / Return type</typeparam>
        /// <typeparam name="TU">输入类型 / Input type</typeparam>
        /// <param name="num">数值 / Number</param>
        /// <returns>绝对值 / Absolute value</returns>
        public static T Abs<T, TU>(TU num) where TU : IAbs<T> => num.Abs();
    }
}
