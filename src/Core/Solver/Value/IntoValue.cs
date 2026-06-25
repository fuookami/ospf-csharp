#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Value
{
    /// <summary>
    /// 值类型转换接口。
    /// 将源数值类型转换为统一的泛型值类型 V。
    /// Value type conversion trait.
    /// Converts source numeric types into a unified generic value type V.
    /// </summary>
    /// <remarks>
    /// 主要用途：将 Flt64（求解器标准类型）转换为 V（泛型值类型）。
    /// Primary use: convert Flt64 (solver standard) to V (generic value type).
    /// </remarks>
    public interface IIntoValue<V>
        where V : struct, IRealNumber<V>
    {
        /// <summary>将 Flt64 转换为泛型值类型 V / Convert Flt64 to generic value type V</summary>
        V IntoValue(Flt64 value);

        /// <summary>零值 / Zero value</summary>
        V Zero { get; }

        /// <summary>单值 / One value</summary>
        V One { get; }

        /// <summary>负无穷 / Negative infinity</summary>
        V NegativeInfinity => IntoValue(new Flt64(double.NegativeInfinity));

        /// <summary>正无穷 / Positive infinity</summary>
        V Infinity => IntoValue(new Flt64(double.PositiveInfinity));

        /// <summary>将泛型值类型 V 转换回 Flt64 / Convert generic value type V back to Flt64</summary>
        Flt64 FromValue(V value);

        /// <summary>Flt64 恒等转换器 / Flt64 identity converter</summary>
        static IIntoValue<Flt64> Identity { get; } = Flt64IdentityConverter.Instance;
    }

    /// <summary>
    /// Flt64 恒等转换器（Flt64 到 Flt64 的透传）。
    /// Flt64 identity converter (passthrough from Flt64 to Flt64).
    /// </summary>
    internal sealed class Flt64IdentityConverter : IIntoValue<Flt64>
    {
        public static readonly Flt64IdentityConverter Instance = new();
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 FromValue(Flt64 value) => value;
    }
}
