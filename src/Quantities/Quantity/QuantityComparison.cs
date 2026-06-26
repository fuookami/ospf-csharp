#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Quantities.Quantity;

/// <summary>
/// 物理量比较扩展 / Quantity comparison extensions.
/// 支持同量纲异单位自动转换和仿射单位容差比较。
/// Supports same-dimension different-unit auto-conversion and affine tolerance comparison.
/// </summary>
public static class QuantityComparison {
    /// <summary>
    /// 判断两个物理量是否相等 / Check if two quantities are equal.
    /// 同单位比较值；同量纲异单位转换后比较；异量纲返回 false。
    /// </summary>
    public static bool AreEqual<V>(this Quantity<V> lhs, Quantity<V> rhs) {
        if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity)) {
            return false;
        }

        if (lhs.Unit.Equals(rhs.Unit)) {
            return CompareValuesEqual(lhs.Value, rhs.Value);
        }

        // Try affine-aware comparison first
        int? affineResult = TryAffineCompare(lhs, rhs);
        if (affineResult.HasValue) {
            return affineResult.Value == 0;
        }

        // Standard conversion comparison
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted = rhs.To(lhs.Unit);
        if (converted.IsFailed) {
            return false;
        }

        return CompareValuesEqual(lhs.Value, converted.Value.Value);
    }

    /// <summary>
    /// 判断两个物理量是否不相等 / Check if two quantities are not equal.
    /// </summary>
    public static bool AreNotEqual<V>(this Quantity<V> lhs, Quantity<V> rhs) => !lhs.AreEqual(rhs);

    /// <summary>
    /// 比较两个物理量的部分序关系 / Compare partial order of two quantities.
    /// 返回 Order.Less、Order.Equal、Order.Greater 或 null（无法比较）。
    /// </summary>
    public static Order? PartialCompare<V>(this Quantity<V> lhs, Quantity<V> rhs) {
        if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity)) {
            return null;
        }

        if (lhs.Unit.Equals(rhs.Unit)) {
            return CompareValuesOrd(lhs.Value, rhs.Value);
        }

        // Try affine-aware comparison first
        int? affineResult = TryAffineCompare(lhs, rhs);
        if (affineResult.HasValue) {
            return OrderHelpers.OrderOf(affineResult.Value);
        }

        // Standard conversion comparison
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> converted = rhs.To(lhs.Unit);
        if (converted.IsFailed) {
            return null;
        }

        return CompareValuesOrd(lhs.Value, converted.Value.Value);
    }

    /// <summary>
    /// 判断是否小于 / Check if less than.
    /// 返回 null 表示无法比较（量纲不匹配或单位转换失败）。
    /// </summary>
    public static bool? LessThan<V>(this Quantity<V> lhs, Quantity<V> rhs) {
        Order? order = lhs.PartialCompare(rhs);
        return order is Order.Less ? true : order is null ? null : false;
    }

    /// <summary>
    /// 判断是否小于等于 / Check if less than or equal.
    /// </summary>
    public static bool? LessThanOrEqual<V>(this Quantity<V> lhs, Quantity<V> rhs) {
        Order? order = lhs.PartialCompare(rhs);
        return order is null ? null : order is Order.Less or Order.Equal;
    }

    /// <summary>
    /// 判断是否大于 / Check if greater than.
    /// </summary>
    public static bool? GreaterThan<V>(this Quantity<V> lhs, Quantity<V> rhs) {
        Order? order = lhs.PartialCompare(rhs);
        return order is Order.Greater ? true : order is null ? null : false;
    }

    /// <summary>
    /// 判断是否大于等于 / Check if greater than or equal.
    /// </summary>
    public static bool? GreaterThanOrEqual<V>(this Quantity<V> lhs, Quantity<V> rhs) {
        Order? order = lhs.PartialCompare(rhs);
        return order is null ? null : order is Order.Greater or Order.Equal;
    }

    // ---- Private helpers ----

    /// <summary>仿射感知比较 / Affine-aware comparison.</summary>
    private static int? TryAffineCompare<V>(Quantity<V> lhs, Quantity<V> rhs) {
        if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity)) {
            return null;
        }

        bool lhsAffine = lhs.Unit.IsAffine;
        bool rhsAffine = rhs.Unit.IsAffine;
        if (!lhsAffine && !rhsAffine) {
            return null; // Not affine, use standard path
        }

        // Both affine or one affine: convert both to standard unit and compare
        if (lhs.Value is Flt64 f64lhs && rhs.Value is Flt64 f64rhs) {
            return TryAffineCompareFlt64(f64lhs, lhs.Unit, f64rhs, rhs.Unit);
        }

        if (lhs.Value is FltX fxlhs && rhs.Value is FltX fxrhs) {
            return TryAffineCompareFltX(fxlhs, lhs.Unit, fxrhs, rhs.Unit);
        }

        return null;
    }

    private static int? TryAffineCompareFlt64(Flt64 lhsValue, PhysicalUnit lhsUnit, Flt64 rhsValue, PhysicalUnit rhsUnit) {
        Flt64? lhsStandard = ConvertToStandardValueFlt64(lhsValue, lhsUnit);
        Flt64? rhsStandard = ConvertToStandardValueFlt64(rhsValue, rhsUnit);
        if (lhsStandard == null || rhsStandard == null) {
            return null;
        }

        Flt64 diff = lhsStandard.Value - rhsStandard.Value;
        Flt64 tolerance = new(1e-10);

        // Use CompareTo to avoid direct Value access
        if (diff.CompareTo(-tolerance) >= 0 && diff.CompareTo(tolerance) <= 0) {
            return 0; // Equal within tolerance
        }

        return diff.CompareTo(new Flt64(0)) < 0 ? -1 : 1;
    }

    private static int? TryAffineCompareFltX(FltX lhsValue, PhysicalUnit lhsUnit, FltX rhsValue, PhysicalUnit rhsUnit) {
        FltX? lhsStandard = ConvertToStandardValueFltX(lhsValue, lhsUnit);
        FltX? rhsStandard = ConvertToStandardValueFltX(rhsValue, rhsUnit);
        if (lhsStandard == null || rhsStandard == null) {
            return null;
        }

        FltX diff = lhsStandard.Value - rhsStandard.Value;
        FltX absDiff = diff.Abs();
        FltX tolerance = new("1e-12");

        if (absDiff.CompareTo(tolerance) <= 0) {
            return 0; // Equal within tolerance
        }

        return diff.CompareTo(new FltX(0)) < 0 ? -1 : 1;
    }

    private static Flt64? ConvertToStandardValueFlt64(Flt64 value, PhysicalUnit unit) {
        UnitConversionRule rule = unit.ConversionRule;
        if (rule is UnitConversionRule.Linear lin) {
            FltX? scale = lin.Scale.Value;
            if (scale == null) {
                return null;
            }

            return value * scale.Value.ToFlt64();
        }
        if (rule is UnitConversionRule.Affine aff) {
            FltX? scale = aff.Scale.Value;
            if (scale == null) {
                return null;
            }

            return value * scale.Value.ToFlt64() + aff.Offset.ToFlt64();
        }
        return null;
    }

    private static FltX? ConvertToStandardValueFltX(FltX value, PhysicalUnit unit) {
        UnitConversionRule rule = unit.ConversionRule;
        if (rule is UnitConversionRule.Linear lin) {
            FltX? scale = lin.Scale.Value;
            return scale == null ? null : value * scale.Value;
        }
        if (rule is UnitConversionRule.Affine aff) {
            FltX? scale = aff.Scale.Value;
            return scale == null ? null : value * scale.Value + aff.Offset;
        }
        return null;
    }

    private static bool CompareValuesEqual<V>(V lhs, V rhs) {
        if (lhs is Flt64 f64l && rhs is Flt64 f64r) {
            return f64l.CompareTo(f64r) == 0;
        }
        if (lhs is FltX fxl && rhs is FltX fxr) {
            return fxl.CompareTo(fxr) == 0;
        }
        if (lhs is Int64 i64l && rhs is Int64 i64r) {
            return i64l.CompareTo(i64r) == 0;
        }
        if (lhs is IntX ixl && rhs is IntX ixr) {
            return ixl.CompareTo(ixr) == 0;
        }
        if (lhs is UInt64 u64l && rhs is UInt64 u64r) {
            return u64l.CompareTo(u64r) == 0;
        }
        return EqualityComparer<V>.Default.Equals(lhs, rhs);
    }

    private static Order? CompareValuesOrd<V>(V lhs, V rhs) {
        if (lhs is Flt64 f64l && rhs is Flt64 f64r) {
            return f64l.Ord(f64r);
        }
        if (lhs is FltX fxl && rhs is FltX fxr) {
            return fxl.Ord(fxr);
        }
        if (lhs is Int64 i64l && rhs is Int64 i64r) {
            return i64l.Ord(i64r);
        }
        if (lhs is IntX ixl && rhs is IntX ixr) {
            return ixl.Ord(ixr);
        }
        if (lhs is UInt64 u64l && rhs is UInt64 u64r) {
            return u64l.Ord(u64r);
        }
        if (lhs is IComparable<V> comparable) {
            return OrderHelpers.OrderOf(comparable.CompareTo(rhs));
        }
        return null;
    }
}
