#nullable enable

using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using System;
using System.Collections.Generic;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Core.Token;
/// <summary>
/// 泛型 Token，提供双视图访问变量求解结果
/// Generic Token with dual-view access to solve results
/// </summary>
/// <typeparam name="V">数值类型 / The number type</typeparam>
public sealed class Token<V> where V : struct, IRealNumber<V> {
    private readonly IFlt64ValueConverter<V>? _converter;
    private Flt64? _result;

    /// <summary>变量项 / Variable item</summary>
    public IVariableItem Variable { get; }
    /// <summary>求解器索引 / Solver index</summary>
    public int SolverIndex { get; }
    /// <summary>刷新回调 / Refresh callbacks</summary>
    internal Dictionary<object, Action<bool>> RefreshCallbacks { get; }

    public Token(
        IVariableItem variable,
        int solverIndex,
        Dictionary<object, Action<bool>> refreshCallbacks,
        IFlt64ValueConverter<V>? converter = null) {
        Variable = variable;
        SolverIndex = solverIndex;
        RefreshCallbacks = refreshCallbacks;
        _converter = converter;
    }

    /// <summary>键 / Key</summary>
    public VariableItemKey Key => Variable.Key;

    /// <summary>结果的 Flt64 视图（求解器边界，内部）/ Flt64 view of result (solver-boundary, internal)</summary>
    internal Flt64? ResultFlt64 {
        get => _result;
        set {
            _result = value;
            foreach (Action<bool> cb in RefreshCallbacks.Values) {
                cb(value is not null);
            }
        }
    }

    /// <summary>结果的 Double 视图 / Double view of result</summary>
    public double? DoubleResult => _result?.ToDouble();

    /// <summary>结果的 V 类型视图（主要公开 API）/ V-type view of result (primary public API)</summary>
    public V? Result =>
        _converter is not null
            ? (_result is { } r ? _converter.IntoValue(r) : default)
            : (V?)(object?)_result;

    /// <summary>设置泛型结果值 / Set result from the generic value</summary>
    public void SetResult(V value) => ResultFlt64 = value.ToFlt64();

    /// <summary>通过给定转换器显式获取类型化结果 / Explicit typed result via the supplied converter</summary>
    public V? ResultWith(IFlt64ValueConverter<V> converter) =>
        _result is { } r ? converter.IntoValue(r) : default;

    /// <summary>变量名称 / Variable name</summary>
    public string Name => Variable.Name;
    /// <summary>变量类型 / Variable type</summary>
    public IVariableTypeKind Type => Variable.TypeKind;

    /// <summary>值范围的 Flt64 视图 / Flt64 view of range</summary>
    public ValueRange<Flt64>? Range =>
        LowerBound is not null && UpperBound is not null
            ? ValueRange<Flt64>.Of(LowerBound.Value, UpperBound.Value, LowerBound.Interval, UpperBound.Interval).Value
            : null;

    /// <summary>下界 / Lower bound</summary>
    public Bound<Flt64>? LowerBound => Variable.LowerBound;
    /// <summary>上界 / Upper bound</summary>
    public Bound<Flt64>? UpperBound => Variable.UpperBound;

    /// <summary>下界（V 类型视图）/ Lower bound (V-type view)</summary>
    public V? LowerBoundV(IFlt64ValueConverter<V> converter) =>
        LowerBound?.Value?.ToFlt64() is { } f ? converter.IntoValue(f) : default;
    /// <summary>上界（V 类型视图）/ Upper bound (V-type view)</summary>
    public V? UpperBoundV(IFlt64ValueConverter<V> converter) =>
        UpperBound?.Value?.ToFlt64() is { } f ? converter.IntoValue(f) : default;

    /// <summary>检查 V 类型值是否在边界范围内 / Check whether a V-type value is within bounds</summary>
    public bool ContainsInBounds(V value, IFlt64ValueConverter<V> converter) {
        ValueRange<Flt64>? r = Range;
        if (r is null) {
            return true;
        }

        return r.Contains(converter.FromValue(value));
    }

    /// <summary>是否属于指定变量项 / Whether belongs to the specified variable item</summary>
    public bool BelongsTo(IVariableItem item) => Variable.BelongsTo(item);
    /// <summary>是否属于指定变量组合 / Whether belongs to the specified variable combination</summary>
    public bool BelongsTo(IVariableCombination combination) => Variable.BelongsTo(combination);

    /// <summary>在边界范围内生成随机值 / Generate a random value within bounds</summary>
    public Flt64 Random(Random rng) {
        if (Variable.TypeKind.IsUnsignedIntegerType) {
            long lower = (long)System.Math.Round(LowerBound!.Value.Unwrap().ToDouble());
            long upper = (long)System.Math.Round(UpperBound!.Value.Unwrap().ToDouble());
            return new UInt64((ulong)rng.NextInt64(lower, upper)).ToFlt64();
        }
        else if (Variable.TypeKind.IsIntegerType) {
            long lower = (long)System.Math.Round(LowerBound!.Value.Unwrap().ToDouble());
            long upper = (long)System.Math.Round(UpperBound!.Value.Unwrap().ToDouble());
            return new Int64(rng.NextInt64(lower, upper)).ToFlt64();
        }
        else {
            double lo = LowerBound!.Value.Unwrap().ToDouble();
            double hi = UpperBound!.Value.Unwrap().ToDouble();
            return new Flt64(lo + rng.NextDouble() * (hi - lo));
        }
    }

    public override int GetHashCode() => Key.GetHashCode();
    public override bool Equals(object? obj) => obj is Token<V> other && Key == other.Key;
    public override string ToString() => $"{Name}: {Result?.ToString() ?? "?"}";
}
