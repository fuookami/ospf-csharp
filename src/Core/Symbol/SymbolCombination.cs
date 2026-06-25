#nullable enable

using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Quantities.Quantity;
using System;
using System.Collections.Generic;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Symbol;
/// <summary>
/// 符号组合抽象接口 / Abstract symbol combination interface.
/// </summary>
/// <typeparam name="S">形状类型 / Shape type</typeparam>
public interface IAbstractSymbolCombination<S> where S : IShape {
    /// <summary>维度数 / Number of dimensions.</summary>
    int Dimension { get; }
    /// <summary>全局唯一标识符 / Globally unique identifier.</summary>
    UInt64 Identifier { get; }
    /// <summary>数组形状 / Array shape.</summary>
    S Shape { get; }
}

/// <summary>
/// 符号组合 / Symbol combination.
/// <para>基于 MultiArray 的中间符号多维容器；创建时自动为每个元素设置组引用与索引。</para>
/// <para>A MultiArray-based multi-dimensional container for intermediate symbols;
/// automatically sets group references and indices for each element on creation.</para>
/// </summary>
/// <typeparam name="TSym">符号类型 / Symbol type</typeparam>
/// <typeparam name="S">形状类型 / Shape type</typeparam>
public sealed class SymbolCombination<TSym, S> : MultiArray<TSym, S>, IAbstractSymbolCombination<S>
    where TSym : IIntermediateSymbol
    where S : IShape {
    /// <summary>组合名称 / Combination name.</summary>
    public string Name { get; }

    /// <summary>全局唯一标识符 / Globally unique identifier.</summary>
    public UInt64 Identifier { get; } = IdentifierGenerator.Gen();

    public SymbolCombination(string name, S shape, Func<int, int[], TSym> ctor)
        : base(shape, ctor) {
        Name = name;
        int i = 0;
        foreach (TSym sym in this) {
            if (sym is LinearExpressionSymbol lin) {
                lin.GroupIdentifier = Identifier;
                lin.IndexInGroup = i;
            }
            else if (sym is QuadraticExpressionSymbol quad) {
                quad.GroupIdentifier = Identifier;
                quad.IndexInGroup = i;
            }
            ++i;
        }
    }
}

/// <summary>
/// 量纲符号组合 / Quantity symbol combination.
/// <para>基于 MultiArray 的量纲中间符号多维容器。</para>
/// </summary>
/// <typeparam name="TSym">符号类型 / Symbol type</typeparam>
/// <typeparam name="S">形状类型 / Shape type</typeparam>
public sealed class QuantitySymbolCombination<TSym, S> : MultiArray<Quantity<TSym>, S>, IAbstractSymbolCombination<S>
    where TSym : IIntermediateSymbol
    where S : IShape {
    /// <summary>组合名称 / Combination name.</summary>
    public string Name { get; }
    /// <summary>全局唯一标识符 / Globally unique identifier.</summary>
    public UInt64 Identifier { get; } = IdentifierGenerator.Gen();

    public QuantitySymbolCombination(string name, S shape, Func<int, int[], Quantity<TSym>> ctor)
        : base(shape, ctor) {
        Name = name;
        int i = 0;
        foreach (Quantity<TSym> qsym in this) {
            if (qsym.Value is LinearExpressionSymbol lin) {
                lin.GroupIdentifier = Identifier;
                lin.IndexInGroup = i;
            }
            else if (qsym.Value is QuadraticExpressionSymbol quad) {
                quad.GroupIdentifier = Identifier;
                quad.IndexInGroup = i;
            }
            ++i;
        }
    }
}

// Note: Kotlin typealias (LinearExpressionSymbols1<V> etc.) cannot be expressed
// as C# using aliases with open generic parameters. Use SymbolCombination<TSym,S>
// directly in consuming code.
