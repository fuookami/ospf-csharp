#nullable enable

namespace Fuookami.Ospf.Math.Operator;
// ===== Integer-exponent power =====

/// <summary>
/// 整数幂运算接口 / Integer Power Operation Interface
/// </summary>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IPow<out TRet> {
    /// <summary>计算整数幂 x^n / Calculates integer power x^n</summary>
    /// <param name="index">指数 / Exponent</param>
    /// <returns>幂运算结果 / Power result</returns>
    TRet Pow(int index);

    /// <summary>计算平方 x^2 / Calculates the square x^2</summary>
    TRet Sqr();

    /// <summary>计算立方 x^3 / Calculates the cube x^3</summary>
    TRet Cub();
}

/// <summary>
/// 带精度的整数幂运算接口 / Precision-aware Integer Power Operation Interface
/// </summary>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IPowP<TRet> : IPow<TRet> {
    /// <summary>带精度的整数幂运算 / Precision-aware integer power</summary>
    TRet Pow(int index, int digits, TRet precision) => Pow(index);
}

/// <summary>
/// 整数幂运算函数扩展接口 / Integer Power Function Extension Interface
/// </summary>
/// <typeparam name="TSelf">接收者类型 / Receiver type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IPowFun<in TSelf, out TRet> {
    /// <summary>计算整数幂 / Calculates integer power</summary>
    TRet Pow(TSelf self, int index);

    /// <summary>计算平方 / Calculates the square</summary>
    TRet Sqr(TSelf self);

    /// <summary>计算立方 / Calculates the cube</summary>
    TRet Cub(TSelf self);
}

/// <summary>
/// 带精度的整数幂运算函数扩展接口 / Precision-aware Integer Power Function Extension Interface
/// </summary>
/// <typeparam name="TSelf">接收者类型 / Receiver type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IPowFunP<in TSelf, TRet> {
    /// <summary>带精度的整数幂运算 / Precision-aware integer power</summary>
    TRet Pow(TSelf self, int index, int digits, TRet precision);
}

// ===== Floating-exponent power =====

/// <summary>
/// 浮点幂运算接口 / Floating-point Power Operation Interface
/// </summary>
/// <typeparam name="TIndex">指数类型 / Exponent type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IPowF<in TIndex, out TRet> {
    /// <summary>计算浮点幂 x^index / Calculates floating-point power x^index</summary>
    TRet Pow(TIndex index);

    /// <summary>计算平方根 x^(1/2) / Calculates the square root</summary>
    TRet Sqrt();

    /// <summary>计算立方根 x^(1/3) / Calculates the cube root</summary>
    TRet Cbrt();
}

/// <summary>
/// 浮点幂运算函数扩展接口 / Floating-point Power Function Extension Interface
/// </summary>
/// <typeparam name="TSelf">接收者类型 / Receiver type</typeparam>
/// <typeparam name="TIndex">指数类型 / Exponent type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IPowFFun<in TSelf, in TIndex, out TRet> {
    /// <summary>计算浮点幂 / Calculates floating-point power</summary>
    TRet Pow(TSelf self, TIndex index);

    /// <summary>计算平方根 / Calculates the square root</summary>
    TRet Sqrt(TSelf self);

    /// <summary>计算立方根 / Calculates the cube root</summary>
    TRet Cbrt(TSelf self);
}

/// <summary>
/// 带精度的浮点幂运算接口 / Precision-aware Floating-point Power Operation Interface
/// </summary>
/// <typeparam name="TIndex">指数类型 / Exponent type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IPowFP<in TIndex, TRet> : IPowF<TIndex, TRet> {
    /// <summary>带精度的浮点幂运算 / Precision-aware floating-point power</summary>
    TRet Pow(TIndex index, int digits, TRet precision) => Pow(index);

    /// <summary>带精度的平方根 / Precision-aware square root</summary>
    TRet Sqrt(int digits, TRet precision) => Sqrt();

    /// <summary>带精度的立方根 / Precision-aware cube root</summary>
    TRet Cbrt(int digits, TRet precision) => Cbrt();
}

/// <summary>
/// 带精度的浮点幂运算函数扩展接口 / Precision-aware Floating-point Power Function Extension Interface
/// </summary>
/// <typeparam name="TSelf">接收者类型 / Receiver type</typeparam>
/// <typeparam name="TIndex">指数类型 / Exponent type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface IPowFPFun<in TSelf, in TIndex, TRet> {
    /// <summary>带精度的浮点幂运算 / Precision-aware floating-point power</summary>
    TRet Pow(TSelf self, TIndex index, int digits, TRet precision);

    /// <summary>带精度的平方根 / Precision-aware square root</summary>
    TRet Sqrt(TSelf self, int digits, TRet precision);

    /// <summary>带精度的立方根 / Precision-aware cube root</summary>
    TRet Cbrt(TSelf self, int digits, TRet precision);
}

// ===== Top-level carrier functions =====

/// <summary>
/// 幂运算辅助方法 / Power operation helper methods
/// </summary>
public static class PowOperators {
    // Integer-exponent overloads

    /// <summary>计算整数幂 / Calculates integer power</summary>
    public static TRet Pow<TBase, TRet>(TBase base_, int index) where TBase : IPow<TRet> => base_.Pow(index);

    /// <summary>使用扩展函数计算整数幂 / Calculates integer power using extension function</summary>
    public static TRet Pow<TBase, TRet, TFunc>(TBase base_, int index, TFunc func) where TFunc : IPowFun<TBase, TRet> => func.Pow(base_, index);

    /// <summary>计算平方 / Calculates the square</summary>
    public static TRet Sqr<TBase, TRet>(TBase base_) where TBase : IPow<TRet> => base_.Sqr();

    /// <summary>使用扩展函数计算平方 / Calculates the square using extension function</summary>
    public static TRet Sqr<TBase, TRet, TFunc>(TBase base_, TFunc func) where TFunc : IPowFun<TBase, TRet> => func.Sqr(base_);

    /// <summary>计算立方 / Calculates the cube</summary>
    public static TRet Cub<TBase, TRet>(TBase base_) where TBase : IPow<TRet> => base_.Cub();

    /// <summary>使用扩展函数计算立方 / Calculates the cube using extension function</summary>
    public static TRet Cub<TBase, TRet, TFunc>(TBase base_, TFunc func) where TFunc : IPowFun<TBase, TRet> => func.Cub(base_);

    // Floating-exponent overloads

    /// <summary>计算浮点幂 / Calculates floating-point power</summary>
    public static TRet Pow<TBase, TIndex, TRet>(TBase base_, TIndex index) where TBase : IPowF<TIndex, TRet> => base_.Pow(index);

    /// <summary>使用扩展函数计算浮点幂 / Calculates floating-point power using extension function</summary>
    public static TRet Pow<TBase, TIndex, TRet, TFunc>(TBase base_, TIndex index, TFunc func) where TFunc : IPowFFun<TBase, TIndex, TRet> => func.Pow(base_, index);

    /// <summary>带精度的浮点幂运算 / Precision-aware floating-point power</summary>
    public static TRet Pow<TBase, TIndex, TRet>(TBase base_, TIndex index, int digits, TRet precision) where TBase : IPowFP<TIndex, TRet> => base_.Pow(index, digits, precision);

    /// <summary>使用扩展函数带精度计算浮点幂 / Precision-aware floating-point power using extension function</summary>
    public static TRet Pow<TBase, TIndex, TRet, TFunc>(TBase base_, TIndex index, int digits, TRet precision, TFunc func) where TFunc : IPowFPFun<TBase, TIndex, TRet> => func.Pow(base_, index, digits, precision);

    /// <summary>计算平方根 / Calculates the square root</summary>
    public static TRet Sqrt<TBase, TIndex, TRet>(TBase base_) where TBase : IPowF<TIndex, TRet> => base_.Sqrt();

    /// <summary>使用扩展函数计算平方根 / Calculates the square root using extension function</summary>
    public static TRet Sqrt<TBase, TIndex, TRet, TFunc>(TBase base_, TFunc func) where TFunc : IPowFFun<TBase, TIndex, TRet> => func.Sqrt(base_);

    /// <summary>带精度的平方根 / Precision-aware square root</summary>
    public static TRet Sqrt<TBase, TIndex, TRet>(TBase base_, int digits, TRet precision) where TBase : IPowFP<TIndex, TRet> => base_.Sqrt(digits, precision);

    /// <summary>使用扩展函数带精度计算平方根 / Precision-aware square root using extension function</summary>
    public static TRet Sqrt<TBase, TIndex, TRet, TFunc>(TBase base_, int digits, TRet precision, TFunc func) where TFunc : IPowFPFun<TBase, TIndex, TRet> => func.Sqrt(base_, digits, precision);

    /// <summary>计算立方根 / Calculates the cube root</summary>
    public static TRet Cbrt<TBase, TIndex, TRet>(TBase base_) where TBase : IPowF<TIndex, TRet> => base_.Cbrt();

    /// <summary>使用扩展函数计算立方根 / Calculates the cube root using extension function</summary>
    public static TRet Cbrt<TBase, TIndex, TRet, TFunc>(TBase base_, TFunc func) where TFunc : IPowFFun<TBase, TIndex, TRet> => func.Cbrt(base_);

    /// <summary>带精度的立方根 / Precision-aware cube root</summary>
    public static TRet Cbrt<TBase, TIndex, TRet>(TBase base_, int digits, TRet precision) where TBase : IPowFP<TIndex, TRet> => base_.Cbrt(digits, precision);

    /// <summary>使用扩展函数带精度计算立方根 / Precision-aware cube root using extension function</summary>
    public static TRet Cbrt<TBase, TIndex, TRet, TFunc>(TBase base_, int digits, TRet precision, TFunc func) where TFunc : IPowFPFun<TBase, TIndex, TRet> => func.Cbrt(base_, digits, precision);
}
