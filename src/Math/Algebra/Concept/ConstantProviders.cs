#nullable enable

namespace Fuookami.Ospf.Math.Algebra.Concept
{
    // ===== Atomic constant provider interfaces =====

    /// <summary>零元素接口 / Has zero element</summary>
    public interface IHasZero<T> { T Zero { get; } }

    /// <summary>单位元接口 / Has one element</summary>
    public interface IHasOne<T> { T One { get; } }

    /// <summary>二常量接口 / Has two constant</summary>
    public interface IHasTwo<T> { T Two { get; } }

    /// <summary>三常量接口 / Has three constant</summary>
    public interface IHasThree<T> { T Three { get; } }

    /// <summary>五常量接口 / Has five constant</summary>
    public interface IHasFive<T> { T Five { get; } }

    /// <summary>十常量接口 / Has ten constant</summary>
    public interface IHasTen<T> { T Ten { get; } }

    /// <summary>二分之一常量接口 / Has half constant</summary>
    public interface IHasHalf<T> { T Half { get; } }

    /// <summary>边界值接口 / Has bounds</summary>
    public interface IHasBounds<T>
    {
        /// <summary>最小值 / Minimum value</summary>
        T Minimum { get; }
        /// <summary>最大值 / Maximum value</summary>
        T Maximum { get; }
    }

    /// <summary>固定精度接口 / Has fixed precision</summary>
    public interface IHasFixedPrecision<T>
        where T : struct
    {
        /// <summary>十进制精度位数 / Decimal precision digits</summary>
        int? DecimalDigits { get; }
        /// <summary>十进制精度值 / Decimal precision value</summary>
        T? DecimalPrecision { get; }
        /// <summary>机器 epsilon / Machine epsilon</summary>
        T? Epsilon { get; }
    }

    /// <summary>无穷大接口 / Has infinity</summary>
    public interface IHasInfinity<T>
        where T : struct
    {
        /// <summary>正无穷 / Positive infinity</summary>
        T? Infinity { get; }
        /// <summary>负无穷 / Negative infinity</summary>
        T? NegativeInfinity { get; }
    }

    /// <summary>NaN 接口 / Has NaN</summary>
    public interface IHasNaN<T>
        where T : struct
    {
        /// <summary>NaN 值 / NaN value</summary>
        T? NaN { get; }
    }

    /// <summary>超越数接口 / Has transcendental constants</summary>
    public interface IHasTranscendentals<T>
    {
        /// <summary>圆周率 pi / Pi</summary>
        T Pi { get; }
        /// <summary>自然常数 e / Euler's number</summary>
        T E { get; }
        /// <summary>lg(2) / Log base 2</summary>
        T Lg2 { get; }
    }

    // ===== Aggregate constant interfaces =====

    /// <summary>
    /// 算术常量聚合接口 / Arithmetic constants aggregate
    /// </summary>
    public interface IArithmeticConst<T> : IHasZero<T>, IHasOne<T>
    {
    }

    /// <summary>
    /// 实数常量聚合接口 / Real constants aggregate
    /// </summary>
    public interface IRealConst<T> : IArithmeticConst<T>,
        IHasTwo<T>, IHasThree<T>, IHasFive<T>, IHasTen<T>,
        IHasBounds<T>, IHasFixedPrecision<T>, IHasInfinity<T>, IHasNaN<T>
        where T : struct
    {
    }

    /// <summary>
    /// 浮点常量聚合接口 / Floating constants aggregate
    /// </summary>
    public interface IFloatingConst<T> : IRealConst<T>, IHasHalf<T>, IHasTranscendentals<T>
        where T : struct
    {
    }
}
