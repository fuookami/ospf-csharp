#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Quantities.Dimension;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Quantities.Symbol;

/// <summary>
/// 运算类型 / Operation type.
/// 用于量纲推导时指定运算类型 / Used to specify operation type for dimension inference.
/// </summary>
public enum Operation
{
    /// <summary>加法 / Addition.</summary>
    Add,

    /// <summary>减法 / Subtraction.</summary>
    Subtract,

    /// <summary>乘法 / Multiplication.</summary>
    Multiply,

    /// <summary>除法 / Division.</summary>
    Divide,
}

/// <summary>
/// 符号量纲注册表 / Symbol dimension registry.
/// 维护符号到量纲的映射，用于表达式构造前/后的量纲校验。
/// Maintains symbol-to-dimension mapping for dimension validation before/after expression construction.
/// 使用 ConcurrentDictionary 保证线程安全 / Uses ConcurrentDictionary for thread safety.
/// </summary>
public sealed class SymbolDimensionRegistry
{
    private readonly ConcurrentDictionary<ISymbol, DimensionedSymbol> _symbolDimensions = new(SymbolNameComparer.Instance);

    /// <summary>注册符号及其量纲 / Register symbol with its dimension.</summary>
    public void Register(DimensionedSymbol symbol) => _symbolDimensions[symbol] = symbol;

    /// <summary>
    /// 获取符号的量纲信息 / Get dimension info for a symbol.
    /// </summary>
    /// <returns>带量纲的符号，或 null 如果未注册 / Dimensioned symbol, or null if not registered.</returns>
    public DimensionedSymbol? GetDimension(ISymbol symbol) =>
        _symbolDimensions.TryGetValue(symbol, out var dim) ? dim : null;

    /// <summary>
    /// 校验加减运算的量纲一致性 / Validate dimension consistency for add/sub operations.
    /// 确保所有符号具有相同的量纲，否则返回 Failed。
    /// Ensures all symbols have the same dimension, otherwise returns Failed.
    /// </summary>
    public Result<Fuookami.Ospf.Utils.Functional.Unit, ErrorCode, Error<ErrorCode>> ValidateAddSubDimension(IEnumerable<ISymbol> symbols)
    {
        var list = symbols.ToList();
        if (list.Count == 0)
        {
            return Results.Ok(default(Fuookami.Ospf.Utils.Functional.Unit));
        }

        if (!_symbolDimensions.TryGetValue(list[0], out var first) || first is null)
        {
            return Results.Failed<Fuookami.Ospf.Utils.Functional.Unit>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Symbol {list[0].Name} not registered"));
        }

        var firstDimension = first.Quantity;
        for (var i = 1; i < list.Count; i++)
        {
            if (!_symbolDimensions.TryGetValue(list[i], out var dim) || dim is null)
            {
                return Results.Failed<Fuookami.Ospf.Utils.Functional.Unit>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Symbol {list[i].Name} not registered"));
            }

            if (!dim.Quantity.Equals(firstDimension))
            {
                return Results.Failed<Fuookami.Ospf.Utils.Functional.Unit>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument,
                        $"Dimension mismatch for addition/subtraction: expected {firstDimension.DimensionSymbol()}, got {dim.Quantity.DimensionSymbol()}"));
            }
        }

        return Results.Ok(default(Fuookami.Ospf.Utils.Functional.Unit));
    }

    /// <summary>
    /// 推导运算结果的量纲 / Infer result dimension from operation.
    /// 规则 / Rules:
    /// - 加减: 结果量纲与操作数相同 / Add/Subtract: result dimension same as operands.
    /// - 乘法: 结果量纲为操作数量纲之积 / Multiply: product of operands' dimensions.
    /// - 除法: 结果量纲为操作数量纲之商 / Divide: quotient of operands' dimensions.
    /// </summary>
    public Result<DerivedQuantity, ErrorCode, Error<ErrorCode>> InferDimension(
        ISymbol symbol1, ISymbol symbol2, Operation operation)
    {
        if (!_symbolDimensions.TryGetValue(symbol1, out var dim1) || dim1 is null)
        {
            return Results.Failed<DerivedQuantity>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Symbol {symbol1.Name} not registered"));
        }

        if (!_symbolDimensions.TryGetValue(symbol2, out var dim2) || dim2 is null)
        {
            return Results.Failed<DerivedQuantity>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Symbol {symbol2.Name} not registered"));
        }

        return operation switch
        {
            Operation.Add or Operation.Subtract => !dim1.Quantity.Equals(dim2.Quantity)
                ? Results.Failed<DerivedQuantity>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    $"Dimension mismatch for {operation.ToString().ToLowerInvariant()}: expected {dim1.Quantity.DimensionSymbol()}, got {dim2.Quantity.DimensionSymbol()}"))
                : Results.Ok(dim1.Quantity),

            Operation.Multiply => Results.Ok(dim1.Quantity * dim2.Quantity),
            Operation.Divide => Results.Ok(dim1.Quantity / dim2.Quantity),

            _ => Results.Failed<DerivedQuantity>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Unknown operation: {operation}")),
        };
    }

    /// <summary>检查符号是否已注册 / Check if symbol is registered.</summary>
    public bool IsRegistered(ISymbol symbol) => _symbolDimensions.ContainsKey(symbol);

    /// <summary>移除符号注册 / Remove symbol registration.</summary>
    public bool Unregister(ISymbol symbol) => _symbolDimensions.TryRemove(symbol, out _);

    /// <summary>清空所有注册 / Clear all registrations.</summary>
    public void Clear() => _symbolDimensions.Clear();
}

/// <summary>
/// Symbol equality comparer by Name / 按名称比较符号相等性.
/// ConcurrentDictionary needs a stable key; ISymbol.Name is the natural key.
/// </summary>
internal sealed class SymbolNameComparer : IEqualityComparer<ISymbol>
{
    public static readonly SymbolNameComparer Instance = new();

    public bool Equals(ISymbol? x, ISymbol? y) =>
        x is not null && y is not null && x.Name == y.Name;

    public int GetHashCode(ISymbol obj) => obj.Name.GetHashCode();
}
