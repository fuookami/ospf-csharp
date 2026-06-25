#nullable enable

using System;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Algebra.Concept
{
    // ===== Typed constants interfaces (registry-backed) =====

    /// <summary>
    /// 实数常量接口（类型化视图）/ Real number constants interface (typed view)
    /// </summary>
    public interface IRealNumberConstants<T> : IArithmeticConstants<T>
        where T : struct, IRealNumber<T>
    {
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
        /// <summary>正最小值 / Positive minimum</summary>
        T PositiveMinimum => One;
        /// <summary>十进制精度位数 / Decimal precision digits</summary>
        int? DecimalDigits => null;
        /// <summary>十进制精度值 / Decimal precision</summary>
        T DecimalPrecision => Zero;
        /// <summary>机器 epsilon / Machine epsilon</summary>
        T Epsilon => Zero;
        /// <summary>NaN 值 / NaN value</summary>
        T? NaN => default;
        /// <summary>正无穷 / Positive infinity</summary>
        T? Infinity => default;
        /// <summary>负无穷 / Negative infinity</summary>
        T? NegativeInfinity => default;
    }

    /// <summary>
    /// 有理数常量接口 / Rational number constants interface
    /// </summary>
    public interface IRationalNumberConstants<T, I> : IRealNumberConstants<T>
        where T : struct, IRationalNumber<T, I>
        where I : struct, IInteger<I>
    {
        /// <summary>二分之一 / Half</summary>
        T Half { get; }
    }

    /// <summary>
    /// 浮点数常量接口 / Floating number constants interface
    /// </summary>
    public interface IFloatingNumberConstants<T> : IRealNumberConstants<T>, IFloatingConst<T>
        where T : struct, IFloatingNumber<T>
    {
        /// <summary>正最小值 / Positive minimum</summary>
        new T PositiveMinimum => ((IRealNumberConstants<T>)this).Epsilon;
        /// <summary>二分之一 / Half</summary>
        new T Half { get; }
        /// <summary>圆周率 / Pi</summary>
        new T Pi { get; }
        /// <summary>自然常数 / Euler's number</summary>
        new T E { get; }
        /// <summary>lg(2) / Log base 2</summary>
        new T Lg2 { get; }
    }

    // ===== Number ring and field =====

    /// <summary>
    /// 数环接口 / Number ring interface
    /// </summary>
    public interface INumberRing<TSelf> : IRing<TSelf>, IPlusGroup<TSelf>, ITimesSemigroup<TSelf>
        where TSelf : struct, INumberRing<TSelf>, IRealNumber<TSelf>
    {
    }

    /// <summary>
    /// 数域接口 / Number field interface
    /// </summary>
    public interface INumberField<TSelf> : IField<TSelf>, INumberRing<TSelf>, ITimesGroup<TSelf>
        where TSelf : struct, INumberField<TSelf>, IRealNumber<TSelf>
    {
    }

    // ===== Scalar =====

    /// <summary>
    /// 标量接口 / Scalar interface
    /// </summary>
    public interface IScalar<TSelf> : IArithmetic<TSelf>, IPlusSemigroup<TSelf>, ITimesSemigroup<TSelf>,
        ICross<TSelf, TSelf>, IAbs<TSelf>
        where TSelf : struct, IScalar<TSelf>
    {
        /// <summary>叉积（标量退化为乘法）/ Cross product (degrades to multiplication for scalars)</summary>
        TSelf Cross(TSelf rhs) => Times(rhs);
    }

    // ===== Real number (CRTP root) =====

    /// <summary>
    /// 实数接口（CRTP 根）/ Real number interface (CRTP root)
    /// </summary>
    public interface IRealNumber<TSelf> : IScalar<TSelf>, IInvariant<TSelf>,
        IOrd<TSelf>, IEq<TSelf>,
        IBounded<TSelf>, IInfinite<TSelf>, IFixed<TSelf>, IEpsilon<TSelf>
        where TSelf : struct, IRealNumber<TSelf>
    {
        /// <summary>获取常量（类型化视图，由 NumericConstantsRegistry 支持）/ Get constants (typed view, backed by NumericConstantsRegistry)</summary>
        IRealNumberConstants<TSelf> Constants { get; }

        /// <summary>是否为正无穷 / Whether is positive infinity</summary>
        bool IsPositiveInfinity();
        /// <summary>是否为负无穷 / Whether is negative infinity</summary>
        bool IsNegativeInfinity();
        /// <summary>是否为无穷大 / Whether is infinite</summary>
        bool IsInfinite() => IsPositiveInfinity() || IsNegativeInfinity();
        /// <summary>是否为有限值 / Whether is finite</summary>
        bool IsFinite() => !IsInfinite();
        /// <summary>值是否在自身范围内 / Whether value is within self bounds</summary>
        bool IsSelfWithinBounds();
        /// <summary>将自身限制在范围内 / Clamp self to bounds</summary>
        TSelf ClampSelfToBounds();
        /// <summary>近似相等 / Approximately equal</summary>
        bool Equiv(TSelf rhs) => Equals(rhs);

        // ===== Type conversions =====
        /// <summary>转为 Flt64 / Convert to Flt64</summary>
        Algebra.Number.Flt64 ToFlt64();
        /// <summary>转为 Flt32 / Convert to Flt32</summary>
        Algebra.Number.Flt32 ToFlt32();
        /// <summary>转为 FltX / Convert to FltX</summary>
        Algebra.Number.FltX ToFltX();
        /// <summary>转为 Int64 / Convert to Int64</summary>
        Algebra.Number.Int64 ToInt64();
        /// <summary>转为 Int32 / Convert to Int32</summary>
        Algebra.Number.Int32 ToInt32();
    }

    // ===== Integer types =====

    /// <summary>
    /// 整数接口 / Integer interface
    /// </summary>
    public interface IInteger<TSelf> : IRealNumber<TSelf>, IPlusGroup<TSelf>, IRangeTo<TSelf, TSelf>
        where TSelf : struct, IInteger<TSelf>
    {
    }

    /// <summary>
    /// 有符号整数接口 / Signed integer number interface
    /// </summary>
    public interface IIntegerNumber<TSelf> : IInteger<TSelf>, INumberField<TSelf>, IPow<TSelf>
        where TSelf : struct, IIntegerNumber<TSelf>
    {
    }

    /// <summary>
    /// 无符号整数接口 / Unsigned integer number interface
    /// </summary>
    public interface IUIntegerNumber<TSelf> : IInteger<TSelf>, INumberField<TSelf>, IPow<TSelf>
        where TSelf : struct, IUIntegerNumber<TSelf>
    {
    }

    // ===== Rational and floating =====

    /// <summary>
    /// 有理数接口 / Rational number interface
    /// </summary>
    public interface IRationalNumber<TSelf, I> : IRealNumber<TSelf>, INumberField<TSelf>, IPow<TSelf>
        where TSelf : struct, IRationalNumber<TSelf, I>
        where I : struct, IInteger<I>
    {
    }

    /// <summary>
    /// 浮点数接口 / Floating number interface
    /// </summary>
    public interface IFloatingNumber<TSelf> : IRealNumber<TSelf>, INumberField<TSelf>, IPow<TSelf>
        where TSelf : struct, IFloatingNumber<TSelf>
    {
        /// <summary>获取浮点数常量 / Get floating number constants</summary>
        new IFloatingNumberConstants<TSelf> Constants { get; }
        /// <summary>倒数 / Reciprocal</summary>
        TSelf Reciprocal();
        /// <summary>平方根 / Square root</summary>
        TSelf Sqrt();
        /// <summary>反余弦（弧度），值域 [0, pi]，|x|>1 返回 NaN / Arccosine (radians), range [0, pi], NaN for |x|>1</summary>
        TSelf Acos();
        /// <summary>从 Int32 转换 / Convert from Int32</summary>
        static abstract TSelf FromInt32(int value);
    }

    // ===== Numeric integer (wrapper) types =====

    /// <summary>
    /// 数值有符号整数接口 / Numeric signed integer interface
    /// </summary>
    public interface INumericIntegerNumber<TSelf, I> : IInteger<TSelf>, IPlusGroup<TSelf>, ITimesSemigroup<TSelf>
        where TSelf : struct, INumericIntegerNumber<TSelf, I>
        where I : struct, IIntegerNumber<I>
    {
    }

    /// <summary>
    /// 数值无符号整数接口 / Numeric unsigned integer interface
    /// </summary>
    public interface INumericUIntegerNumber<TSelf, I> : IInteger<TSelf>, IPlusSemigroup<TSelf>, ITimesSemigroup<TSelf>
        where TSelf : struct, INumericUIntegerNumber<TSelf, I>
        where I : struct, IUIntegerNumber<I>
    {
    }
}
