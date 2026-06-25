#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Variable;
/// <summary>
/// 变量项键（标识符+索引）/ Variable item key (identifier + index)
/// </summary>
public sealed record VariableItemKey(UInt64 Identifier, int Index) : IOrd<VariableItemKey> {
    /// <summary>三路比较 / Three-way comparison</summary>
    public Order Ord(VariableItemKey rhs) =>
        Identifier < rhs.Identifier ? new Order.Less()
        : Identifier > rhs.Identifier ? new Order.Greater()
        : Index.Ord(rhs.Index);

    /// <summary>部分序比较 / Partial order comparison</summary>
    public Order? PartialOrd(VariableItemKey rhs) => Ord(rhs);

    /// <summary>获取哈希码 / Get hash code</summary>
    public override int GetHashCode() => Identifier.ToInt32().GetHashCode() * 31 + Index;
}

/// <summary>
/// 非泛型变量项契约 / Non-generic variable item contract
/// </summary>
public interface IVariableItem : ISymbol {
    /// <summary>标识符 / Identifier</summary>
    UInt64 Identifier { get; }
    /// <summary>索引 / Index</summary>
    int Index { get; }
    /// <summary>键 / Key</summary>
    VariableItemKey Key { get; }
    /// <summary>显示名称 / Display name</summary>
    new string Name { get; }
    /// <summary>变量类型分类 / Variable type kind</summary>
    IVariableTypeKind TypeKind { get; }
    /// <summary>下界（Flt64 视图）/ Lower bound (Flt64 view)</summary>
    Bound<Flt64>? LowerBound { get; }
    /// <summary>上界（Flt64 视图）/ Upper bound (Flt64 view)</summary>
    Bound<Flt64>? UpperBound { get; }
    /// <summary>是否属于指定变量项 / Whether belongs to the specified variable item</summary>
    bool BelongsTo(IVariableItem item);
    /// <summary>是否属于指定变量组合 / Whether belongs to the specified variable combination</summary>
    bool BelongsTo(IVariableCombination combination);
}

/// <summary>
/// 非泛型变量组合契约 / Non-generic variable combination contract
/// </summary>
public interface IVariableCombination {
    /// <summary>标识符 / Identifier</summary>
    UInt64 Identifier { get; }
}

/// <summary>
/// 抽象变量项基类 / Abstract base class for variable items
/// </summary>
/// <typeparam name="T">数值类型 / The number type</typeparam>
/// <typeparam name="TType">变量类型 / The variable type</typeparam>
public abstract class AbstractVariableItem<T, TType> : IVariableItem
    where T : struct, IRealNumber<T>, INumberField<T>
    where TType : VariableType<T> {
    /// <summary>变量类型 / Variable type</summary>
    public TType Type { get; }
    /// <summary>变量名称 / Variable name</summary>
    public string Name { get; set; }
    /// <summary>常量提供器 / Constants provider</summary>
    public INumericConstants<T> Constants { get; }
    /// <summary>变量类型分类（非泛型视图）/ Variable type kind (non-generic view)</summary>
    IVariableTypeKind IVariableItem.TypeKind => Type;

    /// <summary>维度 / Dimension</summary>
    public abstract int Dimension { get; }
    /// <summary>标识符 / Identifier</summary>
    public abstract UInt64 Identifier { get; }
    /// <summary>索引 / Index</summary>
    public abstract int Index { get; }
    /// <summary>向量视图 / Vector view</summary>
    public abstract int[] VectorView { get; }
    /// <summary>显示名称 / Display name</summary>
    public string DisplayName => Name;
    /// <summary>无符号索引 / Unsigned index</summary>
    public UInt64 UIndex => new UInt64((ulong)Index);
    /// <summary>无符号向量 / Unsigned vector</summary>
    public IReadOnlyList<UInt64> UVector => VectorView.Select(i => new UInt64((ulong)i)).ToList();
    /// <summary>值范围 / Value range</summary>
    public Range<TType, T> Range { get; }
    /// <summary>下界（Flt64 视图）/ Lower bound (Flt64 view)</summary>
    public Bound<Flt64>? LowerBound => Range.LowerBound?.ToFlt64();
    /// <summary>上界（Flt64 视图）/ Upper bound (Flt64 view)</summary>
    public Bound<Flt64>? UpperBound => Range.UpperBound?.ToFlt64();
    /// <summary>键 / Key</summary>
    public VariableItemKey Key => new(Identifier, Index);

    protected AbstractVariableItem(TType type, string name, INumericConstants<T> constants) {
        Type = type;
        Name = name;
        Constants = constants;
        Range = new Range<TType, T>(type, constants);
    }

    /// <summary>是否属于指定变量项 / Whether belongs to the specified variable item</summary>
    public virtual bool BelongsTo(IVariableItem item) => Identifier == item.Identifier;
    /// <summary>是否属于指定变量组合 / Whether belongs to the specified variable combination</summary>
    public virtual bool BelongsTo(IVariableCombination combination) => Identifier == combination.Identifier;

    public override int GetHashCode() => Key.GetHashCode();
    public override bool Equals(object? obj) =>
        obj is AbstractVariableItem<T, TType> other && Identifier == other.Identifier && Index == other.Index;
    public override string ToString() => Name;
}

/// <summary>
/// 变量标识符生成器（线程不安全）/ Variable identifier generator (not thread-safe)
/// </summary>
internal sealed class IdentifierGenerator {
    private static UInt64 _next = UInt64.Zero;

    /// <summary>重置标识符 / Flush identifiers</summary>
    public static void Flush() => _next = UInt64.Zero;

    /// <summary>生成下一个标识符 / Generate next identifier</summary>
    public static UInt64 Gen() => _next++;
}

/// <summary>
/// 独立变量项（维度 0）/ Independent variable item (dimension 0)
/// </summary>
public abstract class IndependentVariableItem<T, TType> : AbstractVariableItem<T, TType>
    where T : struct, IRealNumber<T>, INumberField<T>
    where TType : VariableType<T> {
    public sealed override int Dimension => 0;
    public sealed override UInt64 Identifier { get; } = IdentifierGenerator.Gen();
    public sealed override int Index => 0;
    public sealed override int[] VectorView => new[] { 0 };

    protected IndependentVariableItem(TType type, string name, INumericConstants<T> constants)
        : base(type, name, constants) { }

    /// <summary>独立变量不属于任何组合 / Independent item does not belong to any combination</summary>
    public override bool BelongsTo(IVariableCombination combination) => false;
}

// ===== Concrete scalar variables =====

/// <summary>二值标量变量 / Binary scalar variable</summary>
public sealed class BinVar : IndependentVariableItem<UInt8, Binary> {
    public BinVar(string name = "") : base(Binary.Instance, name, NumericConstantsRegistry.For<UInt8>()) { }
}

/// <summary>三值标量变量 / Ternary scalar variable</summary>
public sealed class TerVar : IndependentVariableItem<UInt8, Ternary> {
    public TerVar(string name = "") : base(Ternary.Instance, name, NumericConstantsRegistry.For<UInt8>()) { }
}

/// <summary>平衡三值标量变量 / Balanced ternary scalar variable</summary>
public sealed class BTerVar : IndependentVariableItem<Int8, BalancedTernary> {
    public BTerVar(string name = "") : base(BalancedTernary.Instance, name, NumericConstantsRegistry.For<Int8>()) { }
}

/// <summary>百分比标量变量 / Percentage scalar variable</summary>
public sealed class PctVar : IndependentVariableItem<Flt64, Percentage> {
    public PctVar(string name = "") : base(Percentage.Instance, name, NumericConstantsRegistry.For<Flt64>()) { }
}

/// <summary>整数标量变量 / Integer scalar variable</summary>
public sealed class IntVar : IndependentVariableItem<Int64, Integer> {
    public IntVar(string name = "") : base(Integer.Instance, name, NumericConstantsRegistry.For<Int64>()) { }
}

/// <summary>无符号整数标量变量 / Unsigned integer scalar variable</summary>
public sealed class UIntVar : IndependentVariableItem<UInt64, UInteger> {
    public UIntVar(string name = "") : base(UInteger.Instance, name, NumericConstantsRegistry.For<UInt64>()) { }
}

/// <summary>连续标量变量 / Continuous scalar variable</summary>
public sealed class RealVar : IndependentVariableItem<Flt64, Continuous> {
    public RealVar(string name = "") : base(Continuous.Instance, name, NumericConstantsRegistry.For<Flt64>()) { }
}

/// <summary>无符号连续标量变量 / Unsigned continuous scalar variable</summary>
public sealed class URealVar : IndependentVariableItem<Flt64, UContinuous> {
    public URealVar(string name = "") : base(UContinuous.Instance, name, NumericConstantsRegistry.For<Flt64>()) { }
}
