#nullable enable

using System;

namespace Fuookami.Ospf.Math.Algebra.Concept
{
    /// <summary>
    /// 不变量接口 / Invariant interface
    /// </summary>
    public interface IInvariant<TSelf>
    {
        /// <summary>获取值 / Get value</summary>
        TSelf Value();
    }

    /// <summary>
    /// 变量接口 / Variant interface
    /// </summary>
    public interface IVariant<TSelf>
    {
        /// <summary>获取值（可能为 null）/ Get value (may be null)</summary>
        TSelf? Value();
    }

    /// <summary>
    /// 有界接口 / Bounded interface
    /// </summary>
    public interface IBounded<TSelf>
        where TSelf : struct, IComparable<TSelf>
    {
        /// <summary>是否有限 / Whether bounded</summary>
        bool IsBounded { get; }
        /// <summary>最小 bound / Minimum bound</summary>
        TSelf? MinBound { get; }
        /// <summary>最大 bound / Maximum bound</summary>
        TSelf? MaxBound { get; }
        /// <summary>是否有下界 / Whether has lower bound</summary>
        bool HasLowerBound => MinBound.HasValue;
        /// <summary>是否有上界 / Whether has upper bound</summary>
        bool HasUpperBound => MaxBound.HasValue;
        /// <summary>值是否在范围内 / Whether value is within bounds</summary>
        bool IsWithinBounds(TSelf value);
        /// <summary>将值限制在范围内 / Clamp value to bounds</summary>
        TSelf ClampToBounds(TSelf value);
    }

    /// <summary>
    /// 无穷大支持接口 / Infinite support interface
    /// </summary>
    public interface IInfinite<TSelf>
        where TSelf : struct
    {
        /// <summary>是否支持无穷大 / Whether supports infinity</summary>
        bool SupportsInfinity { get; }
        /// <summary>正无穷 / Positive infinity</summary>
        TSelf? PositiveInfinity { get; }
        /// <summary>负无穷 / Negative infinity</summary>
        TSelf? NegativeInfinityValue { get; }
        /// <summary>是否有正无穷 / Whether has positive infinity</summary>
        bool HasPositiveInfinity => PositiveInfinity.HasValue;
        /// <summary>是否有负无穷 / Whether has negative infinity</summary>
        bool HasNegativeInfinity => NegativeInfinityValue.HasValue;
        /// <summary>是否为正无穷 / Whether is positive infinity</summary>
        bool IsPositiveInfinity(TSelf value);
        /// <summary>是否为负无穷 / Whether is negative infinity</summary>
        bool IsNegativeInfinity(TSelf value);
        /// <summary>是否为无穷大 / Whether is infinite</summary>
        bool IsInfinite(TSelf value) => IsPositiveInfinity(value) || IsNegativeInfinity(value);
        /// <summary>是否为有限值 / Whether is finite</summary>
        bool IsFinite(TSelf value) => !IsInfinite(value);
    }

    /// <summary>
    /// 固定精度接口 / Fixed precision interface
    /// </summary>
    public interface IFixed<TSelf>
        where TSelf : struct
    {
        /// <summary>是否固定精度 / Whether fixed precision</summary>
        bool IsFixed { get; }
        /// <summary>固定小数位数 / Fixed decimal digits</summary>
        int? FixedDigits { get; }
        /// <summary>固定精度值 / Fixed precision value</summary>
        TSelf? FixedPrecision { get; }
        /// <summary>是否有固定精度 / Whether has fixed precision</summary>
        bool HasFixedPrecision => FixedDigits is not null || FixedPrecision.HasValue;
        /// <summary>固定值（退化时）/ Fixed value (when degenerate)</summary>
        TSelf? FixedValue => default;
        /// <summary>是否退化 / Whether degenerate</summary>
        bool IsDegenerate => FixedValue.HasValue;
    }

    /// <summary>
    /// 精度 epsilon 接口 / Precision epsilon interface
    /// </summary>
    public interface IEpsilon<TSelf>
        where TSelf : struct
    {
        /// <summary>精度 epsilon / Precision epsilon</summary>
        TSelf? PrecisionEpsilon { get; }
        /// <summary>是否有 epsilon / Whether has epsilon</summary>
        bool HasEpsilon => PrecisionEpsilon.HasValue;
    }
}
