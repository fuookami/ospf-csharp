#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.MultiArray;
using System;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Variable;
/// <summary>
/// 组合变量项父接口（形状类型化）/ Combination variable item parent interface (shape-typed)
/// </summary>
public interface ICombinationVariableItemParent {
    /// <summary>维度 / Dimension</summary>
    int Dimension { get; }
    /// <summary>标识符 / Identifier</summary>
    UInt64 Identifier { get; }
    /// <summary>形状 / Shape</summary>
    IShape Shape { get; }
}

/// <summary>
/// 组合变量项（多维中的单个元素）/ Combination variable item (single element in multi-dimensional)
/// </summary>
public sealed class CombinationVariableItem<T, TType> : AbstractVariableItem<T, TType>
    where T : struct, IRealNumber<T>, INumberField<T>
    where TType : VariableType<T> {
    private readonly ICombinationVariableItemParent _parent;
    private readonly int _index;

    public CombinationVariableItem(
        ICombinationVariableItemParent parent,
        TType type,
        string name,
        int index,
        INumericConstants<T> constants)
        : base(type, name, constants) {
        _parent = parent;
        _index = index;
    }

    public override int Dimension => _parent.Dimension;
    public override UInt64 Identifier => _parent.Identifier;
    public override int Index => _index;
    public override int[] VectorView => _parent.Shape.Vector(Index).Value!;
}

/// <summary>
/// 多维变量组合基类 / Multi-dimensional variable combination base
/// </summary>
public abstract class VariableCombination<T, TType, S> : IVariableCombination, ICombinationVariableItemParent
    where T : struct, IRealNumber<T>, INumberField<T>
    where TType : VariableType<T>
    where S : IShape {
    /// <summary>变量类型 / Variable type</summary>
    public TType Type { get; }
    /// <summary>名称 / Name</summary>
    public string Name { get; }
    /// <summary>常量提供器 / Constants provider</summary>
    public INumericConstants<T> Constants { get; }
    /// <summary>形状 / Shape</summary>
    public S Shape { get; }
    /// <summary>标识符 / Identifier</summary>
    public UInt64 Identifier { get; } = IdentifierGenerator.Gen();
    /// <summary>维度 / Dimension</summary>
    public int Dimension => Shape.Dimension;
    /// <summary>元素数组 / Items array</summary>
    public CombinationVariableItem<T, TType>[] Items { get; }

    IShape ICombinationVariableItemParent.Shape => Shape;

    protected VariableCombination(TType type, string name, INumericConstants<T> constants, S shape) {
        Type = type;
        Name = name;
        Constants = constants;
        Shape = shape;
        Items = new CombinationVariableItem<T, TType>[shape.Size];
        for (int i = 0; i < shape.Size; i++) {
            int[] vector = shape.Vector(i).Value!;
            string elementName = name + "_" + string.Join("_", vector);
            Items[i] = new CombinationVariableItem<T, TType>(this, type, elementName, i, Constants);
        }
    }

    /// <summary>按索引访问 / Access by index</summary>
    public CombinationVariableItem<T, TType> this[int index] => Items[index];

    /// <summary>按向量访问 / Access by vector</summary>
    public CombinationVariableItem<T, TType> this[params int[] vector] => Items[Shape.Index(vector).Value!];
}

/// <summary>
/// 带物理单位的多维变量组合基类 / Multi-dimensional variable combination with physical unit
/// </summary>
public abstract class QuantityVariableCombination<T, TType, S> : IVariableCombination, ICombinationVariableItemParent
    where T : struct, IRealNumber<T>, INumberField<T>
    where TType : VariableType<T>
    where S : IShape {
    /// <summary>变量类型 / Variable type</summary>
    public TType Type { get; }
    /// <summary>名称 / Name</summary>
    public string Name { get; }
    /// <summary>常量提供器 / Constants provider</summary>
    public INumericConstants<T> Constants { get; }
    /// <summary>形状 / Shape</summary>
    public S Shape { get; }
    /// <summary>标识符 / Identifier</summary>
    public UInt64 Identifier { get; } = IdentifierGenerator.Gen();
    /// <summary>维度 / Dimension</summary>
    public int Dimension => Shape.Dimension;

    IShape ICombinationVariableItemParent.Shape => Shape;

    protected QuantityVariableCombination(TType type, string name, INumericConstants<T> constants, S shape) {
        Type = type;
        Name = name;
        Constants = constants;
        Shape = shape;
    }
}
