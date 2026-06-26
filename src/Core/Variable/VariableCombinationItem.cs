#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.MultiArray;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Variable;

// ===== Generic 1-4 component combinations =====

/// <summary>
/// 一维泛型变量组合（1个分量维度）。
/// 1-dimensional generic variable combination (1 component dimension).
/// </summary>
/// <typeparam name="T">数值类型 / The number type</typeparam>
/// <typeparam name="TType">变量类型 / The variable type</typeparam>
public class Variable1<T, TType> : VariableCombination<T, TType, Shape1>
    where T : struct, IRealNumber<T>, INumberField<T>
    where TType : VariableType<T> {
    /// <summary>
    /// 创建一维变量组合 / Create 1-dimensional variable combination.
    /// </summary>
    /// <param name="type">变量类型 / Variable type</param>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    public Variable1(TType type, string name, int d1)
        : base(type, name, NumericConstantsRegistry.For<T>(), Shape1.Invoke(d1)) { }

    /// <summary>
    /// 使用已知常量创建一维变量组合 / Create 1-dimensional variable combination with known constants.
    /// </summary>
    public Variable1(TType type, string name, int d1, INumericConstants<T> constants)
        : base(type, name, constants, Shape1.Invoke(d1)) { }
}

/// <summary>
/// 二维泛型变量组合（2个分量维度）。
/// 2-dimensional generic variable combination (2 component dimensions).
/// </summary>
/// <typeparam name="T">数值类型 / The number type</typeparam>
/// <typeparam name="TType">变量类型 / The variable type</typeparam>
public class Variable2<T, TType> : VariableCombination<T, TType, Shape2>
    where T : struct, IRealNumber<T>, INumberField<T>
    where TType : VariableType<T> {
    /// <summary>
    /// 创建二维变量组合 / Create 2-dimensional variable combination.
    /// </summary>
    /// <param name="type">变量类型 / Variable type</param>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    public Variable2(TType type, string name, int d1, int d2)
        : base(type, name, NumericConstantsRegistry.For<T>(), Shape2.Invoke(d1, d2)) { }

    /// <summary>
    /// 使用已知常量创建二维变量组合 / Create 2-dimensional variable combination with known constants.
    /// </summary>
    public Variable2(TType type, string name, int d1, int d2, INumericConstants<T> constants)
        : base(type, name, constants, Shape2.Invoke(d1, d2)) { }
}

/// <summary>
/// 三维泛型变量组合（3个分量维度）。
/// 3-dimensional generic variable combination (3 component dimensions).
/// </summary>
/// <typeparam name="T">数值类型 / The number type</typeparam>
/// <typeparam name="TType">变量类型 / The variable type</typeparam>
public class Variable3<T, TType> : VariableCombination<T, TType, Shape3>
    where T : struct, IRealNumber<T>, INumberField<T>
    where TType : VariableType<T> {
    /// <summary>
    /// 创建三维变量组合 / Create 3-dimensional variable combination.
    /// </summary>
    /// <param name="type">变量类型 / Variable type</param>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    public Variable3(TType type, string name, int d1, int d2, int d3)
        : base(type, name, NumericConstantsRegistry.For<T>(), Shape3.Invoke(d1, d2, d3)) { }

    /// <summary>
    /// 使用已知常量创建三维变量组合 / Create 3-dimensional variable combination with known constants.
    /// </summary>
    public Variable3(TType type, string name, int d1, int d2, int d3, INumericConstants<T> constants)
        : base(type, name, constants, Shape3.Invoke(d1, d2, d3)) { }
}

/// <summary>
/// 四维泛型变量组合（4个分量维度）。
/// 4-dimensional generic variable combination (4 component dimensions).
/// </summary>
/// <typeparam name="T">数值类型 / The number type</typeparam>
/// <typeparam name="TType">变量类型 / The variable type</typeparam>
public class Variable4<T, TType> : VariableCombination<T, TType, Shape4>
    where T : struct, IRealNumber<T>, INumberField<T>
    where TType : VariableType<T> {
    /// <summary>
    /// 创建四维变量组合 / Create 4-dimensional variable combination.
    /// </summary>
    /// <param name="type">变量类型 / Variable type</param>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    /// <param name="d4">第四维度长度 / Length of fourth dimension</param>
    public Variable4(TType type, string name, int d1, int d2, int d3, int d4)
        : base(type, name, NumericConstantsRegistry.For<T>(), Shape4.Invoke(d1, d2, d3, d4)) { }

    /// <summary>
    /// 使用已知常量创建四维变量组合 / Create 4-dimensional variable combination with known constants.
    /// </summary>
    public Variable4(TType type, string name, int d1, int d2, int d3, int d4, INumericConstants<T> constants)
        : base(type, name, constants, Shape4.Invoke(d1, d2, d3, d4)) { }
}

// ===== Type-specific aliases: Binary (UInt8 / Binary) =====

/// <summary>
/// 一维二值变量组合 / 1-dimensional binary variable combination.
/// </summary>
public sealed class BinVariable1 : Variable1<UInt8, Binary> {
    /// <summary>创建一维二值变量组合 / Create 1-dimensional binary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    public BinVariable1(string name, int d1) : base(Binary.Instance, name, d1) { }
}

/// <summary>
/// 二维二值变量组合 / 2-dimensional binary variable combination.
/// </summary>
public sealed class BinVariable2 : Variable2<UInt8, Binary> {
    /// <summary>创建二维二值变量组合 / Create 2-dimensional binary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    public BinVariable2(string name, int d1, int d2) : base(Binary.Instance, name, d1, d2) { }
}

/// <summary>
/// 三维二值变量组合 / 3-dimensional binary variable combination.
/// </summary>
public sealed class BinVariable3 : Variable3<UInt8, Binary> {
    /// <summary>创建三维二值变量组合 / Create 3-dimensional binary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    public BinVariable3(string name, int d1, int d2, int d3) : base(Binary.Instance, name, d1, d2, d3) { }
}

/// <summary>
/// 四维二值变量组合 / 4-dimensional binary variable combination.
/// </summary>
public sealed class BinVariable4 : Variable4<UInt8, Binary> {
    /// <summary>创建四维二值变量组合 / Create 4-dimensional binary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    /// <param name="d4">第四维度长度 / Length of fourth dimension</param>
    public BinVariable4(string name, int d1, int d2, int d3, int d4) : base(Binary.Instance, name, d1, d2, d3, d4) { }
}

// ===== Type-specific aliases: Ternary (UInt8 / Ternary) =====

/// <summary>
/// 一维三值变量组合 / 1-dimensional ternary variable combination.
/// </summary>
public sealed class TerVariable1 : Variable1<UInt8, Ternary> {
    /// <summary>创建一维三值变量组合 / Create 1-dimensional ternary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    public TerVariable1(string name, int d1) : base(Ternary.Instance, name, d1) { }
}

/// <summary>
/// 二维三值变量组合 / 2-dimensional ternary variable combination.
/// </summary>
public sealed class TerVariable2 : Variable2<UInt8, Ternary> {
    /// <summary>创建二维三值变量组合 / Create 2-dimensional ternary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    public TerVariable2(string name, int d1, int d2) : base(Ternary.Instance, name, d1, d2) { }
}

/// <summary>
/// 三维三值变量组合 / 3-dimensional ternary variable combination.
/// </summary>
public sealed class TerVariable3 : Variable3<UInt8, Ternary> {
    /// <summary>创建三维三值变量组合 / Create 3-dimensional ternary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    public TerVariable3(string name, int d1, int d2, int d3) : base(Ternary.Instance, name, d1, d2, d3) { }
}

/// <summary>
/// 四维三值变量组合 / 4-dimensional ternary variable combination.
/// </summary>
public sealed class TerVariable4 : Variable4<UInt8, Ternary> {
    /// <summary>创建四维三值变量组合 / Create 4-dimensional ternary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    /// <param name="d4">第四维度长度 / Length of fourth dimension</param>
    public TerVariable4(string name, int d1, int d2, int d3, int d4) : base(Ternary.Instance, name, d1, d2, d3, d4) { }
}

// ===== Type-specific aliases: Integer (Int64 / Integer) =====

/// <summary>
/// 一维整数变量组合 / 1-dimensional integer variable combination.
/// </summary>
public sealed class IntVariable1 : Variable1<Int64, Integer> {
    /// <summary>创建一维整数变量组合 / Create 1-dimensional integer variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    public IntVariable1(string name, int d1) : base(Integer.Instance, name, d1) { }
}

/// <summary>
/// 二维整数变量组合 / 2-dimensional integer variable combination.
/// </summary>
public sealed class IntVariable2 : Variable2<Int64, Integer> {
    /// <summary>创建二维整数变量组合 / Create 2-dimensional integer variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    public IntVariable2(string name, int d1, int d2) : base(Integer.Instance, name, d1, d2) { }
}

/// <summary>
/// 三维整数变量组合 / 3-dimensional integer variable combination.
/// </summary>
public sealed class IntVariable3 : Variable3<Int64, Integer> {
    /// <summary>创建三维整数变量组合 / Create 3-dimensional integer variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    public IntVariable3(string name, int d1, int d2, int d3) : base(Integer.Instance, name, d1, d2, d3) { }
}

/// <summary>
/// 四维整数变量组合 / 4-dimensional integer variable combination.
/// </summary>
public sealed class IntVariable4 : Variable4<Int64, Integer> {
    /// <summary>创建四维整数变量组合 / Create 4-dimensional integer variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    /// <param name="d4">第四维度长度 / Length of fourth dimension</param>
    public IntVariable4(string name, int d1, int d2, int d3, int d4) : base(Integer.Instance, name, d1, d2, d3, d4) { }
}

// ===== Type-specific aliases: Continuous (Flt64 / Continuous) =====

/// <summary>
/// 一维连续变量组合 / 1-dimensional continuous variable combination.
/// </summary>
public sealed class RealVariable1 : Variable1<Flt64, Continuous> {
    /// <summary>创建一维连续变量组合 / Create 1-dimensional continuous variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    public RealVariable1(string name, int d1) : base(Continuous.Instance, name, d1) { }
}

/// <summary>
/// 二维连续变量组合 / 2-dimensional continuous variable combination.
/// </summary>
public sealed class RealVariable2 : Variable2<Flt64, Continuous> {
    /// <summary>创建二维连续变量组合 / Create 2-dimensional continuous variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    public RealVariable2(string name, int d1, int d2) : base(Continuous.Instance, name, d1, d2) { }
}

/// <summary>
/// 三维连续变量组合 / 3-dimensional continuous variable combination.
/// </summary>
public sealed class RealVariable3 : Variable3<Flt64, Continuous> {
    /// <summary>创建三维连续变量组合 / Create 3-dimensional continuous variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    public RealVariable3(string name, int d1, int d2, int d3) : base(Continuous.Instance, name, d1, d2, d3) { }
}

/// <summary>
/// 四维连续变量组合 / 4-dimensional continuous variable combination.
/// </summary>
public sealed class RealVariable4 : Variable4<Flt64, Continuous> {
    /// <summary>创建四维连续变量组合 / Create 4-dimensional continuous variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    /// <param name="d4">第四维度长度 / Length of fourth dimension</param>
    public RealVariable4(string name, int d1, int d2, int d3, int d4) : base(Continuous.Instance, name, d1, d2, d3, d4) { }
}

// ===== Type-specific aliases: Unsigned Integer (UInt64 / UInteger) =====

/// <summary>
/// 一维无符号整数变量组合 / 1-dimensional unsigned integer variable combination.
/// </summary>
public sealed class UIntVar1 : Variable1<UInt64, UInteger> {
    /// <summary>创建一维无符号整数变量组合 / Create 1-dimensional unsigned integer variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    public UIntVar1(string name, int d1) : base(UInteger.Instance, name, d1) { }
}

/// <summary>
/// 二维无符号整数变量组合 / 2-dimensional unsigned integer variable combination.
/// </summary>
public sealed class UIntVar2 : Variable2<UInt64, UInteger> {
    /// <summary>创建二维无符号整数变量组合 / Create 2-dimensional unsigned integer variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    public UIntVar2(string name, int d1, int d2) : base(UInteger.Instance, name, d1, d2) { }
}

/// <summary>
/// 三维无符号整数变量组合 / 3-dimensional unsigned integer variable combination.
/// </summary>
public sealed class UIntVar3 : Variable3<UInt64, UInteger> {
    /// <summary>创建三维无符号整数变量组合 / Create 3-dimensional unsigned integer variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    public UIntVar3(string name, int d1, int d2, int d3) : base(UInteger.Instance, name, d1, d2, d3) { }
}

/// <summary>
/// 四维无符号整数变量组合 / 4-dimensional unsigned integer variable combination.
/// </summary>
public sealed class UIntVar4 : Variable4<UInt64, UInteger> {
    /// <summary>创建四维无符号整数变量组合 / Create 4-dimensional unsigned integer variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    /// <param name="d4">第四维度长度 / Length of fourth dimension</param>
    public UIntVar4(string name, int d1, int d2, int d3, int d4) : base(UInteger.Instance, name, d1, d2, d3, d4) { }
}

// ===== Type-specific aliases: Unsigned Continuous (Flt64 / UContinuous) =====

/// <summary>
/// 一维无符号连续变量组合 / 1-dimensional unsigned continuous variable combination.
/// </summary>
public sealed class URealVariable1 : Variable1<Flt64, UContinuous> {
    /// <summary>创建一维无符号连续变量组合 / Create 1-dimensional unsigned continuous variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    public URealVariable1(string name, int d1) : base(UContinuous.Instance, name, d1) { }
}

/// <summary>
/// 二维无符号连续变量组合 / 2-dimensional unsigned continuous variable combination.
/// </summary>
public sealed class URealVariable2 : Variable2<Flt64, UContinuous> {
    /// <summary>创建二维无符号连续变量组合 / Create 2-dimensional unsigned continuous variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    public URealVariable2(string name, int d1, int d2) : base(UContinuous.Instance, name, d1, d2) { }
}

/// <summary>
/// 三维无符号连续变量组合 / 3-dimensional unsigned continuous variable combination.
/// </summary>
public sealed class URealVariable3 : Variable3<Flt64, UContinuous> {
    /// <summary>创建三维无符号连续变量组合 / Create 3-dimensional unsigned continuous variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    public URealVariable3(string name, int d1, int d2, int d3) : base(UContinuous.Instance, name, d1, d2, d3) { }
}

/// <summary>
/// 四维无符号连续变量组合 / 4-dimensional unsigned continuous variable combination.
/// </summary>
public sealed class URealVariable4 : Variable4<Flt64, UContinuous> {
    /// <summary>创建四维无符号连续变量组合 / Create 4-dimensional unsigned continuous variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    /// <param name="d4">第四维度长度 / Length of fourth dimension</param>
    public URealVariable4(string name, int d1, int d2, int d3, int d4) : base(UContinuous.Instance, name, d1, d2, d3, d4) { }
}

// ===== Type-specific aliases: Percentage (Flt64 / Percentage) =====

/// <summary>
/// 一维百分比变量组合 / 1-dimensional percentage variable combination.
/// </summary>
public sealed class PctVariable1 : Variable1<Flt64, Percentage> {
    /// <summary>创建一维百分比变量组合 / Create 1-dimensional percentage variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    public PctVariable1(string name, int d1) : base(Percentage.Instance, name, d1) { }
}

/// <summary>
/// 二维百分比变量组合 / 2-dimensional percentage variable combination.
/// </summary>
public sealed class PctVariable2 : Variable2<Flt64, Percentage> {
    /// <summary>创建二维百分比变量组合 / Create 2-dimensional percentage variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    public PctVariable2(string name, int d1, int d2) : base(Percentage.Instance, name, d1, d2) { }
}

/// <summary>
/// 三维百分比变量组合 / 3-dimensional percentage variable combination.
/// </summary>
public sealed class PctVariable3 : Variable3<Flt64, Percentage> {
    /// <summary>创建三维百分比变量组合 / Create 3-dimensional percentage variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    public PctVariable3(string name, int d1, int d2, int d3) : base(Percentage.Instance, name, d1, d2, d3) { }
}

/// <summary>
/// 四维百分比变量组合 / 4-dimensional percentage variable combination.
/// </summary>
public sealed class PctVariable4 : Variable4<Flt64, Percentage> {
    /// <summary>创建四维百分比变量组合 / Create 4-dimensional percentage variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    /// <param name="d4">第四维度长度 / Length of fourth dimension</param>
    public PctVariable4(string name, int d1, int d2, int d3, int d4) : base(Percentage.Instance, name, d1, d2, d3, d4) { }
}

// ===== Type-specific aliases: Balanced Ternary (Int8 / BalancedTernary) =====

/// <summary>
/// 一维平衡三值变量组合 / 1-dimensional balanced ternary variable combination.
/// </summary>
public sealed class BTerVariable1 : Variable1<Int8, BalancedTernary> {
    /// <summary>创建一维平衡三值变量组合 / Create 1-dimensional balanced ternary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    public BTerVariable1(string name, int d1) : base(BalancedTernary.Instance, name, d1) { }
}

/// <summary>
/// 二维平衡三值变量组合 / 2-dimensional balanced ternary variable combination.
/// </summary>
public sealed class BTerVariable2 : Variable2<Int8, BalancedTernary> {
    /// <summary>创建二维平衡三值变量组合 / Create 2-dimensional balanced ternary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    public BTerVariable2(string name, int d1, int d2) : base(BalancedTernary.Instance, name, d1, d2) { }
}

/// <summary>
/// 三维平衡三值变量组合 / 3-dimensional balanced ternary variable combination.
/// </summary>
public sealed class BTerVariable3 : Variable3<Int8, BalancedTernary> {
    /// <summary>创建三维平衡三值变量组合 / Create 3-dimensional balanced ternary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    public BTerVariable3(string name, int d1, int d2, int d3) : base(BalancedTernary.Instance, name, d1, d2, d3) { }
}

/// <summary>
/// 四维平衡三值变量组合 / 4-dimensional balanced ternary variable combination.
/// </summary>
public sealed class BTerVariable4 : Variable4<Int8, BalancedTernary> {
    /// <summary>创建四维平衡三值变量组合 / Create 4-dimensional balanced ternary variable combination.</summary>
    /// <param name="name">变量名称 / Variable name</param>
    /// <param name="d1">第一维度长度 / Length of first dimension</param>
    /// <param name="d2">第二维度长度 / Length of second dimension</param>
    /// <param name="d3">第三维度长度 / Length of third dimension</param>
    /// <param name="d4">第四维度长度 / Length of fourth dimension</param>
    public BTerVariable4(string name, int d1, int d2, int d3, int d4) : base(BalancedTernary.Instance, name, d1, d2, d3, d4) { }
}
