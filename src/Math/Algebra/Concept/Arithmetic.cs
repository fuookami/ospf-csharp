#nullable enable

using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Math.Algebra.Concept
{
    /// <summary>
    /// 算术常量接口 / Arithmetic constants interface
    /// </summary>
    public interface IArithmeticConstants<T> : IArithmeticConst<T>
    {
    }

    /// <summary>
    /// 算术接口 / Arithmetic interface
    /// </summary>
    public interface IArithmetic<TSelf> : ICopyable<TSelf>, IPartialEq<TSelf>
        where TSelf : struct, IArithmetic<TSelf>
    {
        /// <summary>获取常量 / Get constants</summary>
        IArithmeticConstants<TSelf> Constants { get; }
        /// <summary>近似相等 / Approximately equal</summary>
        bool Equiv(TSelf rhs);
    }

    /// <summary>
    /// Flt64 值转换器接口 / Flt64 value converter interface
    /// </summary>
    public interface IFlt64ValueConverter<V> : IHasZero<V>, IHasOne<V>
        where V : struct
    {
        /// <summary>从 Flt64 转换为 V / Convert from Flt64 to V</summary>
        V IntoValue(Algebra.Number.Flt64 value);
        /// <summary>从 V 转换为 Flt64 / Convert from V to Flt64</summary>
        Algebra.Number.Flt64 FromValue(V value);
    }
}
