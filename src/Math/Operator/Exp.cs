#nullable enable

namespace Fuookami.Ospf.Math.Operator;
/// <summary>
/// 指数运算接口 / Exponential Operation Interface
/// </summary>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IExp<out TRet> {
    /// <summary>
    /// 计算指数 e^x / Calculates the exponential e^x
    /// </summary>
    /// <returns>指数值 / Exponential value</returns>
    TRet Exp();
}

/// <summary>
/// 带精度的指数运算接口 / Precision-aware Exponential Operation Interface
/// </summary>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IExpP<TRet> : IExp<TRet> {
    /// <summary>
    /// 带精度的指数运算 / Precision-aware exponential
    /// </summary>
    TRet Exp(int digits, TRet precision) => Exp();
}

/// <summary>
/// 指数运算辅助方法 / Exponential operation helper methods
/// </summary>
public static class ExpOperators {
    /// <summary>计算指数 / Calculates the exponential</summary>
    public static TRet Exp<TBase, TRet>(TBase base_) where TBase : IExp<TRet> => base_.Exp();

    /// <summary>带精度的指数运算 / Precision-aware exponential</summary>
    public static TRet Exp<TBase, TRet>(TBase base_, int digits, TRet precision) where TBase : IExpP<TRet> => base_.Exp(digits, precision);
}
