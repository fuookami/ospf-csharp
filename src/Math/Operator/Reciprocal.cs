#nullable enable

namespace Fuookami.Ospf.Math.Operator;
/// <summary>
/// 倒数运算接口 / Reciprocal Operation Interface
/// </summary>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IReciprocal<out TRet> {
    /// <summary>
    /// 计算倒数 1/x / Calculates the reciprocal 1/x
    /// </summary>
    /// <returns>倒数 / Reciprocal</returns>
    TRet Reciprocal();
}
