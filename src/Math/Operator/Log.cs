#nullable enable

namespace Fuookami.Ospf.Math.Operator;
/// <summary>
/// 对数运算接口 / Logarithm Operation Interface
/// </summary>
/// <typeparam name="TBase">底数类型 / Base type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface ILog<in TBase, out TRet> {
    /// <summary>以指定底数的对数 / Logarithm with specified base</summary>
    TRet? Log(TBase base_);

    /// <summary>常用对数 lg(x) / Common logarithm lg(x)</summary>
    TRet? Lg();

    /// <summary>二进制对数 lb(x) / Binary logarithm lb(x)</summary>
    TRet? Lg2();

    /// <summary>自然对数 ln(x) / Natural logarithm ln(x)</summary>
    TRet? Ln();
}

/// <summary>
/// 对数运算函数扩展接口 / Logarithm Function Extension Interface
/// </summary>
/// <typeparam name="TSelf">接收者类型 / Receiver type</typeparam>
/// <typeparam name="TBase">底数类型 / Base type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface ILogFun<in TSelf, in TBase, out TRet> {
    /// <summary>以指定底数的对数 / Logarithm with specified base</summary>
    TRet Log(TSelf self, TBase base_);

    /// <summary>常用对数 / Common logarithm</summary>
    TRet? Lg(TSelf self);

    /// <summary>二进制对数 / Binary logarithm</summary>
    TRet? Lg2(TSelf self);

    /// <summary>自然对数 / Natural logarithm</summary>
    TRet? Ln(TSelf self);
}

/// <summary>
/// 带精度的对数运算接口 / Precision-aware Logarithm Operation Interface
/// </summary>
/// <typeparam name="TBase">底数类型 / Base type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface ILogP<in TBase, TRet> : ILog<TBase, TRet> {
    /// <summary>带精度的对数 / Precision-aware logarithm</summary>
    TRet? Log(TBase base_, int digits, TRet precision) => Log(base_);

    /// <summary>带精度的常用对数 / Precision-aware common logarithm</summary>
    TRet? Lg(int digits, TRet precision) => Lg();

    /// <summary>带精度的二进制对数 / Precision-aware binary logarithm</summary>
    TRet? Lg2(int digits, TRet precision) => Lg2();

    /// <summary>带精度的自然对数 / Precision-aware natural logarithm</summary>
    TRet? Ln(int digits, TRet precision) => Ln();
}

/// <summary>
/// 带精度的对数运算函数扩展接口 / Precision-aware Logarithm Function Extension Interface
/// </summary>
/// <typeparam name="TSelf">接收者类型 / Receiver type</typeparam>
/// <typeparam name="TBase">底数类型 / Base type</typeparam>
/// <typeparam name="TRet">返回值类型 / Return type</typeparam>
public interface ILogFunP<in TSelf, in TBase, TRet> {
    /// <summary>带精度的对数 / Precision-aware logarithm</summary>
    TRet? Log(TSelf self, TBase base_, int digits, TRet precision);

    /// <summary>带精度的常用对数 / Precision-aware common logarithm</summary>
    TRet? Lg(TSelf self, int digits, TRet precision);

    /// <summary>带精度的二进制对数 / Precision-aware binary logarithm</summary>
    TRet? Lg2(TSelf self, int digits, TRet precision);

    /// <summary>带精度的自然对数 / Precision-aware natural logarithm</summary>
    TRet? Ln(TSelf self, int digits, TRet precision);
}

/// <summary>
/// 对数运算辅助方法 / Logarithm operation helper methods
/// </summary>
public static class LogOperators {
    // Log overloads
    /// <summary>以指定底数的对数 / Logarithm with specified base</summary>
    public static TRet? Log<TBase, TNatural, TRet>(TBase base_, TNatural natural) where TNatural : ILog<TBase, TRet> => natural.Log(base_);

    /// <summary>使用扩展函数的对数 / Logarithm using extension function</summary>
    public static TRet? Log<TBase, TNatural, TRet, TFunc>(TBase base_, TNatural natural, TFunc func) where TFunc : ILogFun<TNatural, TBase, TRet> => func.Log(natural, base_);

    /// <summary>带精度的对数 / Precision-aware logarithm</summary>
    public static TRet? Log<TBase, TNatural, TRet>(TBase base_, TNatural natural, int digits, TRet precision) where TNatural : ILogP<TBase, TRet> => natural.Log(base_, digits, precision);

    /// <summary>使用扩展函数带精度的对数 / Precision-aware logarithm using extension function</summary>
    public static TRet? Log<TBase, TNatural, TRet, TFunc>(TBase base_, TNatural natural, int digits, TRet precision, TFunc func) where TFunc : ILogFunP<TNatural, TBase, TRet> => func.Log(natural, base_, digits, precision);

    // Lg overloads
    /// <summary>常用对数 / Common logarithm</summary>
    public static TRet? Lg<TBase, TNatural, TRet>(TNatural natural) where TNatural : ILog<TBase, TRet> => natural.Lg();

    /// <summary>使用扩展函数的常用对数 / Common logarithm using extension function</summary>
    public static TRet? Lg<TBase, TNatural, TRet, TFunc>(TNatural natural, TFunc func) where TFunc : ILogFun<TNatural, TBase, TRet> => func.Lg(natural);

    /// <summary>带精度的常用对数 / Precision-aware common logarithm</summary>
    public static TRet? Lg<TBase, TNatural, TRet>(TNatural natural, int digits, TRet precision) where TNatural : ILogP<TBase, TRet> => natural.Lg(digits, precision);

    /// <summary>使用扩展函数带精度的常用对数 / Precision-aware common logarithm using extension function</summary>
    public static TRet? Lg<TBase, TNatural, TRet, TFunc>(TNatural natural, int digits, TRet precision, TFunc func) where TFunc : ILogFunP<TNatural, TBase, TRet> => func.Lg(natural, digits, precision);

    // Lg2 overloads
    /// <summary>二进制对数 / Binary logarithm</summary>
    public static TRet? Lg2<TBase, TNatural, TRet>(TNatural natural) where TNatural : ILog<TBase, TRet> => natural.Lg2();

    /// <summary>使用扩展函数的二进制对数 / Binary logarithm using extension function</summary>
    public static TRet? Lg2<TBase, TNatural, TRet, TFunc>(TNatural natural, TFunc func) where TFunc : ILogFun<TNatural, TBase, TRet> => func.Lg2(natural);

    /// <summary>带精度的二进制对数 / Precision-aware binary logarithm</summary>
    public static TRet? Lg2<TBase, TNatural, TRet>(TNatural natural, int digits, TRet precision) where TNatural : ILogP<TBase, TRet> => natural.Lg2(digits, precision);

    /// <summary>使用扩展函数带精度的二进制对数 / Precision-aware binary logarithm using extension function</summary>
    public static TRet? Lg2<TBase, TNatural, TRet, TFunc>(TNatural natural, int digits, TRet precision, TFunc func) where TFunc : ILogFunP<TNatural, TBase, TRet> => func.Lg2(natural, digits, precision);

    // Ln overloads
    /// <summary>自然对数 / Natural logarithm</summary>
    public static TRet? Ln<TBase, TNatural, TRet>(TNatural natural) where TNatural : ILog<TBase, TRet> => natural.Ln();

    /// <summary>使用扩展函数的自然对数 / Natural logarithm using extension function</summary>
    public static TRet? Ln<TBase, TNatural, TRet, TFunc>(TNatural natural, TFunc func) where TFunc : ILogFun<TNatural, TBase, TRet> => func.Ln(natural);

    /// <summary>带精度的自然对数 / Precision-aware natural logarithm</summary>
    public static TRet? Ln<TBase, TNatural, TRet>(TNatural natural, int digits, TRet precision) where TNatural : ILogP<TBase, TRet> => natural.Ln(digits, precision);

    /// <summary>使用扩展函数带精度的自然对数 / Precision-aware natural logarithm using extension function</summary>
    public static TRet? Ln<TBase, TNatural, TRet, TFunc>(TNatural natural, int digits, TRet precision, TFunc func) where TFunc : ILogFunP<TNatural, TBase, TRet> => func.Ln(natural, digits, precision);
}
