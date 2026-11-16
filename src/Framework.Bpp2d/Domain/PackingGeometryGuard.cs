// 装箱几何守卫 / Packing geometry guard.
// 与固定 Kotlin 对应：校验放置集合是否全部位于板材内、
// 两两不重叠（可含刀缝），以及旋转是否合法。所有方法返回结果类型，不抛出、不使用非空断言。
// Validates that placements lie inside the sheet, are pairwise disjoint (optionally with
// a kerf) and are legally rotated. Every method returns a result type with no throwing and no non-null assertions.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 判断形状旋转 90 度后是否保持不变 / Check whether a shape is unchanged by a 90-degree rotation.
/// </summary>
/// <remarks>
/// 包围宽高相等的形状（圆形件与正方形件）旋转后不变，因此任何旋转都合法；宽高不等时旋转会改变占用区域，
/// 需要物品显式允许。
/// A shape whose bounding width equals its bounding height (circle or square) is invariant under rotation, so any
/// rotation is legal; otherwise rotation changes the occupied region and needs the item's explicit allowance.
/// </remarks>
public static class PackingShape2Rotation {
    /// <summary>
    /// 判断形状是否旋转不变 / Check whether the shape is rotation invariant.
    /// </summary>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <returns>是否旋转不变 / Whether rotation invariant.</returns>
    public static bool IsInvariant(PackingShape2<FltX> shape) {
        ArgumentNullException.ThrowIfNull(shape);
        return QuantityCore.Equal(shape.BoundingWidth, shape.BoundingHeight);
    }
}

/// <summary>
/// 旋转合法性规则 / Rotation legality rule.
/// </summary>
/// <remarks>
/// 几何层无法判断"物品是否允许旋转"，因此由调用方以本规则注入该知识。
/// 缺省 <see cref="PackingRotationRule.AllowAll"/> 是**宽松**默认：调用方已在上游校验过时才可用它，否则非法旋转不会被拦下。
/// The geometry layer cannot know whether an item allows rotation, so callers inject that knowledge through this
/// rule. The default <see cref="PackingRotationRule.AllowAll"/> is the **permissive** choice: use it only when the caller has already
/// validated upstream, otherwise illegal rotations slip through.
/// </remarks>
public interface IPackingRotationRule {
    /// <summary>
    /// 判断单个放置的旋转是否合法 / Check whether the rotation of one placement is legal.
    /// </summary>
    /// <param name="placement">待检查的放置 / Placement to check.</param>
    /// <returns>是否合法 / Whether legal.</returns>
    bool Allow(Placement2<FltX> placement);
}

/// <summary>
/// 旋转合法性规则实现 / Rotation legality rule implementations.
/// </summary>
public static class PackingRotationRule {
    /// <summary>始终允许旋转：调用方已在上游校验过 / Always allow rotation.</summary>
    public static IPackingRotationRule AllowAll { get; } = new AllowAllRule();

    /// <summary>仅允许旋转不变的形状（圆形件、正方形件）/ Allow only rotation-invariant shapes.</summary>
    public static IPackingRotationRule SymmetricOnly { get; } = new SymmetricOnlyRule();

    /// <summary>
    /// 依据物品目录判断旋转合法性 / Build a rule from the item catalogue.
    /// </summary>
    /// <remarks>
    /// 目录中缺失的物品视为允许旋转。这是**宽松**取舍：宁可在上游漏登记一个物品时放行，也不要因为目录不全
    /// 而把合法排样判成非法——但调用方因此有义务把用到的物品都登记进来。
    /// Items missing from the catalogue are treated as allowing rotation. That is the **permissive** trade-off:
    /// better to let a placement through when the catalogue forgot an item than to reject a legal layout because
    /// the catalogue is incomplete — but the caller is then obliged to register every item it uses.
    /// </remarks>
    /// <param name="items">物品编号到物品的映射 / Item identifier to item mapping.</param>
    /// <returns>旋转合法性规则 / Rotation legality rule.</returns>
    public static IPackingRotationRule ByItemCatalog(IReadOnlyDictionary<string, IAbstractItem<FltX>> items) {
        ArgumentNullException.ThrowIfNull(items);
        return new CatalogRule(items);
    }

    private sealed class AllowAllRule : IPackingRotationRule {
        public bool Allow(Placement2<FltX> placement) {
            ArgumentNullException.ThrowIfNull(placement);
            return true;
        }
    }

    private sealed class SymmetricOnlyRule : IPackingRotationRule {
        public bool Allow(Placement2<FltX> placement) {
            ArgumentNullException.ThrowIfNull(placement);
            return !placement.Rotated || PackingShape2Rotation.IsInvariant(placement.Shape);
        }
    }

    private sealed class CatalogRule : IPackingRotationRule {
        private readonly IReadOnlyDictionary<string, IAbstractItem<FltX>> _items;

        public CatalogRule(IReadOnlyDictionary<string, IAbstractItem<FltX>> items) => _items = items;

        public bool Allow(Placement2<FltX> placement) {
            ArgumentNullException.ThrowIfNull(placement);
            if (!placement.Rotated || PackingShape2Rotation.IsInvariant(placement.Shape)) {
                return true;
            }
            if (placement.ItemId is not { } itemId) {
                return true;
            }
            return !_items.TryGetValue(itemId, out IAbstractItem<FltX>? item) || item.AllowRotate;
        }
    }
}

/// <summary>
/// 装箱几何守卫 / Packing geometry guard.
/// </summary>
public static class PackingGeometryGuard {
    /// <summary>
    /// 校验放置集合的纯几何约束：全部位于板材内且两两不重叠 / Validate the pure geometric constraints.
    /// </summary>
    /// <param name="sheet">板材几何 / Sheet geometry.</param>
    /// <param name="placements">放置列表 / Placement list.</param>
    /// <param name="sheetName">板材实例名称，用于错误信息 / Sheet instance name used in error messages.</param>
    /// <param name="source">调用来源 / Call source.</param>
    /// <param name="kerf">刀缝间距，非空时要求相邻件间隙不小于该值 / Kerf spacing.</param>
    /// <returns>校验结果 / Validation result.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> RequireSheetGeometry(
        ISheet2Geometry<FltX> sheet,
        IReadOnlyList<Placement2<FltX>> placements,
        string sheetName = "sheet",
        string source = "PackingGeometryGuard.requireSheetGeometry",
        Quantity<FltX>? kerf = null) {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(placements);

        string mixedDiagnostic = PackingGeometryContract.MixedShapeDiagnostic(
            placements.GroupBy(placement => placement.Shape.ShapeType)
                .ToDictionary(group => group.Key, group => group.Count()));

        for (int index = 0; index < placements.Count; index++) {
            Result<bool, ErrorCode, Error<ErrorCode>> inside = placements[index].InsideSheet(sheet);
            if (!inside.IsOk) {
                return Fail(inside);
            }
            if (!inside.Value) {
                return Results.Failed<Success>(new Bpp2dValidationError(
                    PackingGeometryContract.UnsupportedOutsideSheetGeometryMessage(
                        source: source,
                        sheetName: sheetName,
                        itemIndex: index,
                        diagnostic: PackingGeometryContract.PlacementShapeDiagnostic(
                            placements[index].Shape, placements[index].Rotated),
                        mixedDiagnostic: mixedDiagnostic)));
            }
        }

        bool hasKerf = kerf is { } spacing && QuantityCore.IsPositive(spacing);
        for (int lhsIndex = 0; lhsIndex < placements.Count; lhsIndex++) {
            for (int rhsIndex = lhsIndex + 1; rhsIndex < placements.Count; rhsIndex++) {
                // 刀缝必须双向检查：只有把任一侧膨胀后相交，才说明真实间隙小于 kerf。
                // 只查一侧会在"右侧件更大"时漏判。
                // The kerf is checked in both directions: only an intersection after inflating either side proves
                // the real gap is below the kerf, and checking one side alone misses when the right piece is larger.
                bool overlapped;
                if (hasKerf) {
                    Result<bool, ErrorCode, Error<ErrorCode>> forward =
                        placements[lhsIndex].InflatedBy(kerf!).Overlapped(placements[rhsIndex]);
                    if (!forward.IsOk) {
                        return Fail(forward);
                    }
                    if (forward.Value) {
                        overlapped = true;
                    }
                    else {
                        Result<bool, ErrorCode, Error<ErrorCode>> backward =
                            placements[lhsIndex].Overlapped(placements[rhsIndex].InflatedBy(kerf!));
                        if (!backward.IsOk) {
                            return Fail(backward);
                        }
                        overlapped = backward.Value;
                    }
                }
                else {
                    Result<bool, ErrorCode, Error<ErrorCode>> direct =
                        placements[lhsIndex].Overlapped(placements[rhsIndex]);
                    if (!direct.IsOk) {
                        return Fail(direct);
                    }
                    overlapped = direct.Value;
                }
                if (overlapped) {
                    return Results.Failed<Success>(new Bpp2dValidationError(
                        PackingGeometryContract.UnsupportedPlacementOverlapMessage(
                            source: source,
                            sheetName: sheetName,
                            lhsIndex: lhsIndex,
                            lhsDiagnostic: PackingGeometryContract.PlacementShapeDiagnostic(
                                placements[lhsIndex].Shape, placements[lhsIndex].Rotated),
                            rhsIndex: rhsIndex,
                            rhsDiagnostic: PackingGeometryContract.PlacementShapeDiagnostic(
                                placements[rhsIndex].Shape, placements[rhsIndex].Rotated),
                            mixedDiagnostic: mixedDiagnostic)));
                }
            }
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 完整校验入口：先做几何校验，再按旋转规则校验旋转合法性 / Full validation: geometry first, then rotation.
    /// </summary>
    /// <param name="sheet">板材几何 / Sheet geometry.</param>
    /// <param name="placements">放置列表 / Placement list.</param>
    /// <param name="sheetName">板材实例名称 / Sheet instance name.</param>
    /// <param name="source">调用来源 / Call source.</param>
    /// <param name="rotationRule">旋转合法性规则 / Rotation legality rule.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>校验结果 / Validation result.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Validate(
        ISheet2Geometry<FltX> sheet,
        IReadOnlyList<Placement2<FltX>> placements,
        string sheetName = "sheet",
        string source = "PackingGeometryGuard.validate",
        IPackingRotationRule? rotationRule = null,
        Quantity<FltX>? kerf = null) {
        ArgumentNullException.ThrowIfNull(placements);

        Result<Success, ErrorCode, Error<ErrorCode>> geometry = RequireSheetGeometry(
            sheet, placements, sheetName, source, kerf);
        if (!geometry.IsOk) {
            return geometry;
        }

        IPackingRotationRule rule = rotationRule ?? PackingRotationRule.AllowAll;
        string mixedDiagnostic = PackingGeometryContract.MixedShapeDiagnostic(
            placements.GroupBy(placement => placement.Shape.ShapeType)
                .ToDictionary(group => group.Key, group => group.Count()));

        for (int index = 0; index < placements.Count; index++) {
            if (rule.Allow(placements[index])) {
                continue;
            }
            return Results.Failed<Success>(new Bpp2dValidationError(
                PackingGeometryContract.UnsupportedIllegalRotationMessage(
                    source: source,
                    sheetName: sheetName,
                    itemIndex: index,
                    diagnostic: PackingGeometryContract.PlacementShapeDiagnostic(
                        placements[index].Shape, placements[index].Rotated),
                    itemId: placements[index].ItemId,
                    mixedDiagnostic: mixedDiagnostic)));
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 把布尔或数量失败原样转成成功类型失败 / Re-type a failed intermediate result.
    /// </summary>
    /// <typeparam name="T">中间结果类型 / Intermediate result type.</typeparam>
    /// <param name="failed">失败的中间结果 / The failed intermediate result.</param>
    /// <returns>同因失败 / A failure carrying the same cause.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Fail<T>(
        Result<T, ErrorCode, Error<ErrorCode>> failed) =>
        failed switch {
            Failed<T, ErrorCode, Error<ErrorCode>> failure =>
                Results.Failed<Success>(failure.Error),
            Fatal<T, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<Success>(new Bpp2dInternalError(
                "几何校验内部失败 / Geometry validation failed internally")),
        };
}
