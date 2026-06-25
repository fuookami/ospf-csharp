#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Core.Model.Basic;

namespace Fuookami.Ospf.Core.Variable
{
    /// <summary>
    /// 变量值域（扩展 ExpressionRange），基于变量类型初始化边界
    /// Variable range (extends ExpressionRange), initializes bounds from variable type
    /// </summary>
    /// <typeparam name="TType">变量类型 / The variable type</typeparam>
    /// <typeparam name="V">数值类型 / The number type</typeparam>
    public sealed class Range<TType, V> : ExpressionRange<V>
        where TType : VariableType<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        /// <summary>变量类型 / Variable type</summary>
        public TType Type { get; }

        /// <summary>使用变量类型和常量创建 / Create with variable type and constants</summary>
        public Range(TType type, INumericConstants<V> constants)
            : base(
                ValueRange<V>.Of(type.Minimum, type.Maximum, new Interval.Closed(), new Interval.Closed()).Value!,
                constants)
        {
            Type = type;
        }
    }

    /// <summary>
    /// 二值变量范围扩展方法（Boolean 支持）
    /// Binary variable range extension methods (Boolean support)
    /// </summary>
    public static class BinaryRangeExtensions
    {
        /// <summary>设置上界（Boolean）/ Set upper bound (Boolean)</summary>
        public static bool Ls(this Range<Binary, UInt8> range, bool value) =>
            range.Ls(new InvariantUInt8Wrapper(value ? UInt8.One : UInt8.Zero));

        /// <summary>设置上界（Boolean）/ Set upper bound (Boolean)</summary>
        public static bool Leq(this Range<Binary, UInt8> range, bool value) => range.Ls(value);

        /// <summary>设置下界（Boolean）/ Set lower bound (Boolean)</summary>
        public static bool Gr(this Range<Binary, UInt8> range, bool value) =>
            range.Geq(new InvariantUInt8Wrapper(value ? UInt8.One : UInt8.Zero));

        /// <summary>设置下界（Boolean）/ Set lower bound (Boolean)</summary>
        public static bool Geq(this Range<Binary, UInt8> range, bool value) => range.Gr(value);

        /// <summary>设置相等（Boolean）/ Set equality (Boolean)</summary>
        public static bool Eq(this Range<Binary, UInt8> range, bool value) =>
            range.Eq(new InvariantUInt8Wrapper(value ? UInt8.One : UInt8.Zero));

        /// <summary>设置为 true / Set to true</summary>
        public static bool SetTrue(this Range<Binary, UInt8> range) =>
            range.Geq(new InvariantUInt8Wrapper(UInt8.One));

        /// <summary>设置为 false / Set to false</summary>
        public static bool SetFalse(this Range<Binary, UInt8> range) =>
            range.Leq(new InvariantUInt8Wrapper(UInt8.Zero));
    }

    /// <summary>
    /// UInt8 的不变量包装器 / Invariant wrapper for UInt8
    /// </summary>
    public readonly struct InvariantUInt8Wrapper : IInvariant<UInt8>
    {
        private readonly UInt8 _value;
        public InvariantUInt8Wrapper(UInt8 value) => _value = value;
        public UInt8 Value() => _value;
    }

    /// <summary>
    /// Int8 的不变量包装器 / Invariant wrapper for Int8
    /// </summary>
    public readonly struct InvariantInt8Wrapper : IInvariant<Int8>
    {
        private readonly Int8 _value;
        public InvariantInt8Wrapper(Int8 value) => _value = value;
        public Int8 Value() => _value;
    }

    /// <summary>
    /// Flt64 的不变量包装器 / Invariant wrapper for Flt64
    /// </summary>
    public readonly struct InvariantFlt64Wrapper : IInvariant<Flt64>
    {
        private readonly Flt64 _value;
        public InvariantFlt64Wrapper(Flt64 value) => _value = value;
        public Flt64 Value() => _value;
    }
}
