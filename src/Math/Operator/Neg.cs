#nullable enable

namespace Fuookami.Ospf.Math.Operator
{
    /// <summary>
    /// 负号运算符接口 / Negation Operator Interface
    /// </summary>
    /// <typeparam name="TRet">返回值类型 / Return type</typeparam>
    public interface INeg<out TRet>
    {
        /// <summary>
        /// 取负运算 / Negation operation
        /// </summary>
        /// <returns>相反数 / Opposite number</returns>
        TRet Negate();
    }
}
