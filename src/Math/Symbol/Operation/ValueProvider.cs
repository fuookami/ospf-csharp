#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Operation;
/// <summary>
/// 缺失值策略 / Missing value policy.
/// 当求值时变量不在 provider 中时的处理策略。
/// Policy for handling missing symbols during evaluation.
/// </summary>
public enum MissingValuePolicy {
    /// <summary>使用零值 / Use zero value.</summary>
    UseZero,
    /// <summary>抛出异常 / Throw exception.</summary>
    Throw,
}

/// <summary>
/// 值提供者接口 / Value provider interface.
/// 为符号提供对应数值的函数式接口。
/// Functional interface providing numeric values for symbols.
/// </summary>
/// <typeparam name="T">数值类型 / Numeric type.</typeparam>
public interface IValueProvider<T> where T : struct {
    /// <summary>尝试获取符号的值 / Try to get the value for a symbol.</summary>
    T? TryGetValue(ISymbol symbol);
}

/// <summary>
/// 基于字典的值提供者 / Map-backed value provider.
/// 使用符号→值字典实现 IValueProvider。
/// Implements IValueProvider using a symbol-to-value dictionary.
/// </summary>
/// <typeparam name="T">数值类型 / Numeric type.</typeparam>
public sealed class MapValueProvider<T> : IValueProvider<T> where T : struct {
    private readonly IReadOnlyDictionary<ISymbol, T> _map;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="map">符号→值映射 / Symbol to value mapping.</param>
    public MapValueProvider(IReadOnlyDictionary<ISymbol, T> map) {
        _map = map;
    }

    /// <inheritdoc/>
    public T? TryGetValue(ISymbol symbol)
        => _map.TryGetValue(symbol, out T v) ? v : null;
}
