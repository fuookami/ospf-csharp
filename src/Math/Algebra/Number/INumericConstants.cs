#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Math.Algebra.Number;
/// <summary>
/// 统一数值常量接口 / Unified numeric constants interface
/// <para>
/// 替代 Kotlin 的 RealNumberConstants / FloatingNumberConstants / RationalNumberConstants。
/// 每个数值类型通过实现此接口提供其常量，并在 NumericConstantsRegistry 中注册。
/// </para>
/// <para>
/// Replaces Kotlin's RealNumberConstants / FloatingNumberConstants / RationalNumberConstants.
/// Each number type provides its constants by implementing this interface and registering with NumericConstantsRegistry.
/// </para>
/// </summary>
/// <typeparam name="T">数值类型 / Number type</typeparam>
public interface INumericConstants<T>
    where T : struct, IRealNumber<T> {
    /// <summary>零 / Zero</summary>
    T Zero { get; }
    /// <summary>一 / One</summary>
    T One { get; }
    /// <summary>二 / Two</summary>
    T Two { get; }
    /// <summary>三 / Three</summary>
    T Three { get; }
    /// <summary>五 / Five</summary>
    T Five { get; }
    /// <summary>十 / Ten</summary>
    T Ten { get; }

    /// <summary>最小值 / Minimum</summary>
    T Minimum { get; }
    /// <summary>最大值 / Maximum</summary>
    T Maximum { get; }

    /// <summary>二分之一（整数类型返回 null）/ Half (null for integer types)</summary>
    T? Half { get; }
    /// <summary>正无穷（仅浮点类型）/ Positive infinity (floating types only)</summary>
    T? PositiveInfinity { get; }
    /// <summary>负无穷（仅浮点类型）/ Negative infinity (floating types only)</summary>
    T? NegativeInfinity { get; }
    /// <summary>NaN（仅浮点类型）/ NaN (floating types only)</summary>
    T? NaN { get; }
    /// <summary>机器 epsilon / Machine epsilon</summary>
    T? Epsilon { get; }
    /// <summary>十进制精度位数 / Decimal precision digits</summary>
    int? DecimalDigits { get; }
    /// <summary>十进制精度值 / Decimal precision value</summary>
    T? DecimalPrecision { get; }
    /// <summary>圆周率 pi / Pi</summary>
    T? Pi { get; }
    /// <summary>自然常数 e / Euler's number</summary>
    T? E { get; }
    /// <summary>lg(2) / Log base 2</summary>
    T? Lg2 { get; }

    /// <summary>正最小值 / Positive minimum</summary>
    T PositiveMinimum => Half ?? One;

    /// <summary>是否为 NaN / Is NaN</summary>
    bool IsNaN(T value);
}
