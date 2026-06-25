#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Variable
{
    /// <summary>
    /// 变量类型分类接口（二值/无符号/整数/连续判断）
    /// Variable type kind interface (binary/unsigned/integer/continuous classification)
    /// </summary>
    public interface IVariableTypeKind
    {
        /// <summary>是否为二值类型 / Whether is binary type</summary>
        bool IsBinaryType => false;
        /// <summary>是否为无符号类型 / Whether is unsigned type</summary>
        bool IsUnsignedType => false;
        /// <summary>是否为整数类型 / Whether is integer type</summary>
        bool IsIntegerType => false;
        /// <summary>是否为无符号整数类型 / Whether is unsigned integer type</summary>
        bool IsUnsignedIntegerType => IsIntegerType && IsUnsignedType;
        /// <summary>是否为连续类型 / Whether is continuous type</summary>
        bool IsContinuousType => !IsIntegerType;
        /// <summary>是否为无符号连续类型 / Whether is unsigned continuous type</summary>
        bool IsUnsignedContinuousType => IsContinuousType && IsUnsignedType;
        /// <summary>是否为非二值整数类型 / Whether is non-binary integer type</summary>
        bool IsNotBinaryIntegerType => !IsBinaryType && IsIntegerType;
    }

    /// <summary>
    /// 变量类型完整接口 / Full variable type interface
    /// </summary>
    /// <typeparam name="T">数值类型 / The number type</typeparam>
    public interface IVariableType<T> : IVariableTypeKind
        where T : struct, IRealNumber<T>, INumberField<T>
    {
        /// <summary>类型名称 / Type name</summary>
        string Name { get; }
        /// <summary>类型短名称 / Type short name</summary>
        string ShortName { get; }
        /// <summary>常量提供器 / Constants provider</summary>
        INumericConstants<T> Constants { get; }
        /// <summary>最小值 / Minimum value</summary>
        T Minimum { get; }
        /// <summary>最大值 / Maximum value</summary>
        T Maximum { get; }
    }

    /// <summary>
    /// 有符号整数变量类型接口 / Signed integer variable type interface
    /// </summary>
    public interface IIntegerVariableType<T> : IVariableType<T>
        where T : struct, IIntegerNumber<T>
    {
        T IVariableType<T>.Minimum => Constants.Minimum;
        T IVariableType<T>.Maximum => Constants.Maximum;
        bool IVariableTypeKind.IsIntegerType => true;
    }

    /// <summary>
    /// 无符号整数变量类型接口 / Unsigned integer variable type interface
    /// </summary>
    public interface IUIntegerVariableType<T> : IVariableType<T>
        where T : struct, IUIntegerNumber<T>
    {
        T IVariableType<T>.Minimum => Constants.Zero;
        T IVariableType<T>.Maximum => Constants.Maximum;
        bool IVariableTypeKind.IsUnsignedType => true;
        bool IVariableTypeKind.IsIntegerType => true;
    }

    /// <summary>
    /// 有符号连续变量类型接口 / Signed continuous variable type interface
    /// </summary>
    public interface IContinuousVariableType<T> : IVariableType<T>
        where T : struct, IFloatingNumber<T>
    {
        T IVariableType<T>.Minimum => Constants.DecimalPrecision.GetValueOrDefault().Reciprocal().Negate();
        T IVariableType<T>.Maximum => Constants.DecimalPrecision.GetValueOrDefault().Reciprocal();
    }

    /// <summary>
    /// 无符号连续变量类型接口 / Unsigned continuous variable type interface
    /// </summary>
    public interface IUContinuousVariableType<T> : IVariableType<T>
        where T : struct, IFloatingNumber<T>
    {
        T IVariableType<T>.Minimum => Constants.Zero;
        T IVariableType<T>.Maximum => Constants.DecimalPrecision.GetValueOrDefault().Reciprocal();
        bool IVariableTypeKind.IsUnsignedType => true;
    }

    /// <summary>
    /// 变量类型密封基类 / Sealed base class for variable types
    /// </summary>
    public abstract class VariableType<T> : IVariableType<T>
        where T : struct, IRealNumber<T>, INumberField<T>
    {
        /// <summary>常量提供器 / Constants provider</summary>
        public INumericConstants<T> Constants { get; }

        protected VariableType(INumericConstants<T> constants) => Constants = constants;

        public abstract string Name { get; }
        public abstract string ShortName { get; }
        public virtual T Minimum => Constants.Minimum;
        public virtual T Maximum => Constants.Maximum;
    }

    // ===== Concrete variable types =====

    /// <summary>
    /// 二值变量类型（0/1）/ Binary variable type (0/1)
    /// </summary>
    public sealed class Binary : VariableType<UInt8>, IUIntegerVariableType<UInt8>
    {
        /// <summary>单例实例 / Singleton instance</summary>
        public static readonly Binary Instance = new();
        private Binary() : base(NumericConstantsRegistry.For<UInt8>()) { }
        public override string Name => "Binary";
        public override string ShortName => "bin";
        public override UInt8 Maximum => Constants.One;
        bool IVariableTypeKind.IsBinaryType => true;
        public override string ToString() => "Binary";
    }

    /// <summary>
    /// 三值变量类型（0/1/2）/ Ternary variable type (0/1/2)
    /// </summary>
    public sealed class Ternary : VariableType<UInt8>, IUIntegerVariableType<UInt8>
    {
        /// <summary>单例实例 / Singleton instance</summary>
        public static readonly Ternary Instance = new();
        private Ternary() : base(NumericConstantsRegistry.For<UInt8>()) { }
        public override string Name => "Ternary";
        public override string ShortName => "ter";
        public override UInt8 Maximum => Constants.Two;
        public override string ToString() => "Ternary";
    }

    /// <summary>
    /// 平衡三值变量类型（-1/0/1）/ Balanced ternary variable type (-1/0/1)
    /// </summary>
    public sealed class BalancedTernary : VariableType<Int8>, IIntegerVariableType<Int8>
    {
        /// <summary>单例实例 / Singleton instance</summary>
        public static readonly BalancedTernary Instance = new();
        private BalancedTernary() : base(NumericConstantsRegistry.For<Int8>()) { }
        public override string Name => "BalancedTernary";
        public override string ShortName => "bter";
        public override Int8 Minimum => Constants.One.Negate();
        public override Int8 Maximum => Constants.One;
        public override string ToString() => "BalancedTernary";
    }

    /// <summary>
    /// 百分比变量类型（[0, 1]）/ Percentage variable type ([0, 1])
    /// </summary>
    public sealed class Percentage : VariableType<Flt64>, IUContinuousVariableType<Flt64>
    {
        /// <summary>单例实例 / Singleton instance</summary>
        public static readonly Percentage Instance = new();
        private Percentage() : base(NumericConstantsRegistry.For<Flt64>()) { }
        public override string Name => "Percentage";
        public override string ShortName => "pct";
        public override Flt64 Maximum => Constants.One;
        public override string ToString() => "Percentage";
    }

    /// <summary>
    /// 有符号整数变量类型 / Signed integer variable type
    /// </summary>
    public sealed class Integer : VariableType<Int64>, IIntegerVariableType<Int64>
    {
        /// <summary>单例实例 / Singleton instance</summary>
        public static readonly Integer Instance = new();
        private Integer() : base(NumericConstantsRegistry.For<Int64>()) { }
        public override string Name => "Integer";
        public override string ShortName => "int";
        public override string ToString() => "Integer";
    }

    /// <summary>
    /// 无符号整数变量类型 / Unsigned integer variable type
    /// </summary>
    public sealed class UInteger : VariableType<UInt64>, IUIntegerVariableType<UInt64>
    {
        /// <summary>单例实例 / Singleton instance</summary>
        public static readonly UInteger Instance = new();
        private UInteger() : base(NumericConstantsRegistry.For<UInt64>()) { }
        public override string Name => "UInteger";
        public override string ShortName => "uint";
        public override string ToString() => "UInteger";
    }

    /// <summary>
    /// 有符号连续变量类型 / Signed continuous variable type
    /// </summary>
    public sealed class Continuous : VariableType<Flt64>, IContinuousVariableType<Flt64>
    {
        /// <summary>单例实例 / Singleton instance</summary>
        public static readonly Continuous Instance = new();
        private Continuous() : base(NumericConstantsRegistry.For<Flt64>()) { }
        public override string Name => "Continuous";
        public override string ShortName => "real";
        // Kotlin returns "Continues" (typo); C# corrects to "Continuous"
        public override string ToString() => "Continuous";
    }

    /// <summary>
    /// 无符号连续变量类型 / Unsigned continuous variable type
    /// </summary>
    public sealed class UContinuous : VariableType<Flt64>, IUContinuousVariableType<Flt64>
    {
        /// <summary>单例实例 / Singleton instance</summary>
        public static readonly UContinuous Instance = new();
        private UContinuous() : base(NumericConstantsRegistry.For<Flt64>()) { }
        public override string Name => "UContinuous";
        public override string ShortName => "ureal";
        // Kotlin returns "UContinues" (typo); C# corrects to "UContinuous"
        public override string ToString() => "UContinuous";
    }
}
