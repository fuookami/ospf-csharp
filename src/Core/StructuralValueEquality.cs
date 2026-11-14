#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Symbol.Function;
namespace Fuookami.Ospf.Core;

internal static class StructuralValueEquality<V> where V : struct {
    public static bool Same(V first, V second) {
        if (typeof(V) == typeof(RtnX)) {
            // shortcut: RtnX 按存储表示保守比较；布局变化时改用显式表示 API / Compare RtnX storage conservatively; use an explicit representation API if its layout changes.
            return MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref first, 1))
                .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref second, 1)));
        }
        return EqualityComparer<V>.Default.Equals(first, second);
    }

    public static bool Same(V? first, V? second) =>
        first.HasValue ? second.HasValue && Same(first.Value, second.Value) : !second.HasValue;
}

internal static class NumericValueValidation {
    public static bool IsExactIntegerConversion<V>(V value, Flt64 source) where V : struct, IRealNumber<V> {
        // shortcut: 未内建精确读取的整数类型会被拒绝；新增类型时扩展 ExactBoundValue / Reject integer types without exact readers; extend ExactBoundValue for new types.
        return ExactBoundValue.TryRead(value, out ExactBoundValue actual) &&
            ExactBoundValue.TryRead(source, out ExactBoundValue expected) &&
            actual.CompareTo(expected) == 0;
    }

    public static bool IsValid<V>(V value) where V : struct, IRealNumber<V> {
        if (!value.IsSelfWithinBounds() || !value.Eq(value)) {
            return false;
        }
        if (typeof(V) == typeof(RtnX)) {
            // shortcut: 检查 RtnX 的两个 double 字段；布局变化时改用显式表示 API / Check the two RtnX doubles; use an explicit representation API if its layout changes.
            ReadOnlySpan<double> parts = MemoryMarshal.Cast<V, double>(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
            return parts.Length == 2 && double.IsFinite(parts[0]) && double.IsFinite(parts[1]) &&
                parts[1] != 0 && double.IsFinite(parts[0] / parts[1]);
        }
        return true;
    }
}
