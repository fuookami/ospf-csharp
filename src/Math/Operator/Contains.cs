#nullable enable

namespace Fuookami.Ospf.Math.Operator
{
    /// <summary>
    /// 包含运算接口 / Contains Operation Interface
    /// </summary>
    /// <typeparam name="T">元素类型 / Element type</typeparam>
    public interface IContains<in T>
    {
        /// <summary>
        /// 判断是否包含指定值 / Determines whether the specified value is contained
        /// </summary>
        /// <param name="value">要检查的值 / Value to check</param>
        /// <returns>是否包含 / Whether contained</returns>
        bool Contains(T value);
    }
}
