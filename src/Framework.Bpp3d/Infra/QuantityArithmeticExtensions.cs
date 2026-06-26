#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Bpp3d.Infra;

/// <summary>
/// Quantity 算术扩展方法集合 / Quantity arithmetic extension methods.
/// 提供集合操作、比较和实用方法。
/// Provides collection operations, comparisons, and utility methods.
/// </summary>
public static class QuantityArithmeticExtensions {
    /// <summary>集合中 Quantity 求和 / Sum of Quantity in collection.</summary>
    public static Quantity<FltX> SumOfQuantity(this IEnumerable<Quantity<FltX>> source) {
        FltX sum = FltX.Zero;
        PhysicalUnit? unit = null;
        foreach (Quantity<FltX> q in source) {
            unit ??= q.Unit;
            sum = sum.Plus(q.Value);
        }
        return new Quantity<FltX>(sum, unit!);
    }

    /// <summary>集合中 Quantity 最大值 / Max of Quantity in collection.</summary>
    public static Quantity<FltX> MaxOfQuantity(this IEnumerable<Quantity<FltX>> source) {
        Quantity<FltX>? max = null;
        foreach (Quantity<FltX> q in source) {
            if (max is null || q.Value.ToFlt64().ToDouble() > max.Value.ToFlt64().ToDouble()) {
                max = q;
            }
        }
        return max ?? throw new InvalidOperationException("Sequence contains no elements.");
    }

    /// <summary>集合中 Quantity 最大值或默认 / Max of Quantity or default.</summary>
    public static Quantity<FltX>? MaxOfOrNullQuantity(this IEnumerable<Quantity<FltX>> source) {
        Quantity<FltX>? max = null;
        foreach (Quantity<FltX> q in source) {
            if (max is null || q.Value.ToFlt64().ToDouble() > max.Value.ToFlt64().ToDouble()) {
                max = q;
            }
        }
        return max;
    }

    /// <summary>集合中 Quantity 最小值 / Min of Quantity in collection.</summary>
    public static Quantity<FltX> MinOfQuantity(this IEnumerable<Quantity<FltX>> source) {
        Quantity<FltX>? min = null;
        foreach (Quantity<FltX> q in source) {
            if (min is null || q.Value.ToFlt64().ToDouble() < min.Value.ToFlt64().ToDouble()) {
                min = q;
            }
        }
        return min ?? throw new InvalidOperationException("Sequence contains no elements.");
    }

    /// <summary>按 Quantity 值升序排序 / Sort ascending by Quantity value.</summary>
    public static IEnumerable<T> SortedByQuantity<T>(this IEnumerable<T> source, Func<T, Quantity<FltX>> selector) =>
        source.OrderBy(x => selector(x).Value.ToFlt64().ToDouble());

    /// <summary>按 Quantity 值降序排序 / Sort descending by Quantity value.</summary>
    public static IEnumerable<T> SortedByDescendingQuantity<T>(this IEnumerable<T> source, Func<T, Quantity<FltX>> selector) =>
        source.OrderByDescending(x => selector(x).Value.ToFlt64().ToDouble());

    /// <summary>Quantity 比较: 小于 / Quantity comparison: less than.</summary>
    public static bool IsLessThan(this Quantity<FltX> lhs, Quantity<FltX> rhs) =>
        lhs.Value.ToFlt64().ToDouble() < rhs.Value.ToFlt64().ToDouble();

    /// <summary>Quantity 比较: 大于 / Quantity comparison: greater than.</summary>
    public static bool IsGreaterThan(this Quantity<FltX> lhs, Quantity<FltX> rhs) =>
        lhs.Value.ToFlt64().ToDouble() > rhs.Value.ToFlt64().ToDouble();

    /// <summary>Quantity 比较: 小于等于 / Quantity comparison: less than or equal.</summary>
    public static bool IsLessThanOrEqual(this Quantity<FltX> lhs, Quantity<FltX> rhs) =>
        lhs.Value.ToFlt64().ToDouble() <= rhs.Value.ToFlt64().ToDouble();

    /// <summary>Quantity 比较: 大于等于 / Quantity comparison: greater than or equal.</summary>
    public static bool IsGreaterThanOrEqual(this Quantity<FltX> lhs, Quantity<FltX> rhs) =>
        lhs.Value.ToFlt64().ToDouble() >= rhs.Value.ToFlt64().ToDouble();

    /// <summary>Quantity 加法（FltX 特化）/ Quantity addition (FltX specialisation).</summary>
    public static Quantity<FltX> Add(this Quantity<FltX> lhs, Quantity<FltX> rhs) =>
        new(lhs.Value.Plus(rhs.Value), lhs.Unit);

    /// <summary>Quantity 减法（FltX 特化）/ Quantity subtraction (FltX specialisation).</summary>
    public static Quantity<FltX> SubtractFltX(this Quantity<FltX> lhs, Quantity<FltX> rhs) =>
        new(lhs.Value.Minus(rhs.Value), lhs.Unit);

    /// <summary>Quantity 取绝对值 / Quantity absolute value.</summary>
    public static Quantity<FltX> Abs(this Quantity<FltX> q) =>
        new(new FltX(global::System.Math.Abs(q.Value.ToFlt64().ToDouble())), q.Unit);

    /// <summary>Quantity 转 double / Quantity to double.</summary>
    public static double ToDouble(this Quantity<FltX> q) => q.Value.ToFlt64().ToDouble();

    /// <summary>创建零 Quantity / Create zero Quantity.</summary>
    public static Quantity<FltX> ZeroQuantity(PhysicalUnit unit) => new(FltX.Zero, unit);
}
