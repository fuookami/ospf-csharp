#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Symbol;
/// <summary>
/// 中间符号接口（非泛型）/ Intermediate symbol interface (non-generic).
/// <para>非泛型视图，用于符号注册与代币表操作。完整泛型版本见 <see cref="IIntermediateSymbol{V}"/>。</para>
/// <para>Non-generic view for symbol registration and token-table operations. See <see cref="IIntermediateSymbol{V}"/> for the full generic version.</para>
/// </summary>
public interface IIntermediateSymbol : ISymbol {
    /// <summary>符号名称 / Symbol name.</summary>
    new string Name { get; set; }
    /// <summary>可选显示名称 / Optional display name.</summary>
    new string? DisplayName { get; set; }
    /// <summary>全局唯一标识符 / Globally unique identifier.</summary>
    UInt64 Identifier { get; }
    /// <summary>在所属组合中的索引 / Index within owning combination.</summary>
    int Index { get; }
    /// <summary>父符号 / Parent symbol.</summary>
    IIntermediateSymbol? Parent { get; }
    /// <summary>参数 / Arguments.</summary>
    object? Args => Parent?.Args;
    /// <summary>原始字符串表示 / Raw string representation.</summary>
    string ToRawString(UInt64 unfold);
}

/// <summary>
/// 中间符号核心接口 / Intermediate symbol interface.
/// <para>数学优化模型中所有中间符号的基础接口，封装表达式求值逻辑，支持缓存、依赖追踪与边界管理。</para>
/// <para>Base interface for all intermediate symbols; encapsulates evaluation with caching,
/// dependency tracking, and bound management.</para>
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public interface IIntermediateSymbol<V> : IIntermediateSymbol
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>是否为离散变量 / Whether the symbol is discrete.</summary>
    bool Discrete => false;

    /// <summary>表达式值域 / Expression value range.</summary>
    ExpressionRange<V> Range { get; }

    /// <summary>下界 / Lower bound.</summary>
    Bound<V>? LowerBound => Range.LowerBound;
    /// <summary>上界 / Upper bound.</summary>
    Bound<V>? UpperBound => Range.UpperBound;
    /// <summary>固定值 / Fixed value.</summary>
    V? FixedValue => Range.FixedValue;

    /// <summary>根据固定值与令牌表求值 / Evaluate with fixed values and token table.</summary>
    V? Prepare(IReadOnlyDictionary<ISymbol, V>? values, IAbstractTokenTable<V> tokenTable, IFlt64ValueConverter<V> converter);

    /// <summary>准备符号值并缓存结果 / Prepare and cache the result.</summary>
    void PrepareAndCache(IReadOnlyDictionary<ISymbol, V>? values, IAbstractTokenTable<V> tokenTable, IFlt64ValueConverter<V> converter);

    /// <summary>使用令牌表求值 / Evaluate using the token table.</summary>
    V? Evaluate(IAbstractTokenTable<V> tokenTable, IFlt64ValueConverter<V> converter, bool zeroIfNone = false);

    /// <summary>使用解向量与令牌表求值 / Evaluate using a solution vector and token table.</summary>
    V? Evaluate(IReadOnlyList<V> results, IAbstractTokenTable<V> tokenTable, IFlt64ValueConverter<V> converter, bool zeroIfNone = false);

    /// <summary>使用固定值映射与令牌表求值 / Evaluate using a fixed-values map and token table.</summary>
    V? Evaluate(IReadOnlyDictionary<ISymbol, V> values, IAbstractTokenTable<V>? tokenTable, IFlt64ValueConverter<V> converter, bool zeroIfNone = false);

    /// <summary>从令牌表直接求值 / Evaluate directly from the token table.</summary>
    V? EvaluateFromTokens(IAbstractTokenTable<V> tokenTable, IFlt64ValueConverter<V> converter, bool zeroIfNone = false) =>
        Evaluate(tokenTable, converter, zeroIfNone);

    /// <summary>符号类别 / Symbol category.</summary>
    Category Category { get; }
    /// <summary>操作类别 / Operation category.</summary>
    Category OperationCategory => Category;

    /// <summary>是否已缓存 / Whether the value is cached.</summary>
    bool Cached { get; }

    /// <summary>父符号 / Parent symbol.</summary>
    new IIntermediateSymbol? Parent => null;

    /// <summary>参数 / Arguments.</summary>
    new object? Args => Parent?.Args;

    /// <summary>依赖符号集合 / Set of dependency symbols.</summary>
    IReadOnlySet<IIntermediateSymbol> Dependencies { get; }

    /// <summary>刷新缓存 / Flush cache.</summary>
    void Flush(bool force = false);

    /// <summary>注册辅助令牌 / Register auxiliary tokens.</summary>
    Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>原始字符串表示 / Raw string representation.</summary>
    new string ToRawString(UInt64 unfold);
}

/// <summary>
/// 线性中间符号接口 / Linear intermediate symbol interface.
/// <para>表示可转换为线性多项式的中间符号。支持可变与不可变多项式访问。</para>
/// <para>Represents an intermediate symbol that can be converted to a linear polynomial.</para>
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public interface ILinearIntermediateSymbol<V> : IIntermediateSymbol<V>, IToLinearPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>对应的线性多项式 / The associated linear polynomial.</summary>
    LinearPolynomial<V> Polynomial { get; }

    /// <summary>获取可变线性多项式表示 / Get mutable linear polynomial representation.</summary>
    MutableLinearPolynomial<V> AsMutable();

    LinearPolynomial<V> IToLinearPolynomial<V>.ToLinearPolynomial() => Polynomial;
}

/// <summary>
/// 二次中间符号接口 / Quadratic intermediate symbol interface.
/// <para>表示可转换为二次多项式的中间符号。支持可变与不可变多项式访问。</para>
/// <para>Represents an intermediate symbol that can be converted to a quadratic polynomial.</para>
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public interface IQuadraticIntermediateSymbol<V> : IIntermediateSymbol<V>, IToQuadraticPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>对应的二次多项式 / The associated quadratic polynomial.</summary>
    QuadraticPolynomial<V> Polynomial { get; }

    /// <summary>获取可变二次多项式表示 / Get mutable quadratic polynomial representation.</summary>
    MutableQuadraticPolynomial<V> AsMutable();

    QuadraticPolynomial<V> IToQuadraticPolynomial<V>.ToQuadraticPolynomial() => Polynomial;
}
