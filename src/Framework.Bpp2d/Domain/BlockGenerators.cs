// 简单块与复杂块生成 / Simple and complex block generation.
// 与固定 Kotlin 基线 `bpp2d/domain/block_loading/service/SimpleBlockGenerator.kt`（118 行）与
// `ComplexBlockGenerator.kt`（106 行）对应。
// Mirrors the pinned baseline's `SimpleBlockGenerator.kt` and `ComplexBlockGenerator.kt`.
//
// `BlockLoadingContext` 与 `SpaceSplitAlgorithms`（293 行）留待后续：它们是装配层与空间切分搜索，与这两个
// 生成器没有反向依赖。
// `BlockLoadingContext` and `SpaceSplitAlgorithms` (293 lines) come next batch: they are the assembly layer and the space
// split search, and neither is a dependency of these two generators.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 简单块生成器配置 / Simple block generator configuration.
/// </summary>
/// <param name="WithRotation">是否同时生成旋转排列 / Whether rotated layouts are also generated.</param>
/// <param name="WithRemainder">是否在剩余空间继续生成块 / Whether blocks continue to be generated in the remainder.</param>
public sealed record SimpleBlockConfig(bool WithRotation = true, bool WithRemainder = false);

/// <summary>
/// 简单块生成器 / Simple block generator.
/// </summary>
/// <remarks>
/// 为单个物品在给定区域内铺出规则网格块。旋转候选**同时受配置与物品自身许可约束**——只检查配置会让未允许旋转的
/// 物品产出旋转块，与 <see cref="RotationGuard.RequireRotationAllowed"/> 的守卫语义自相矛盾。
/// Lays a regular grid block for one item inside a region. Rotated candidates require **both the config and the item's
/// own allowance**: checking only the config would let an item that disallows rotation yield rotated blocks, contradicting
/// the guard in <see cref="RotationGuard.RequireRotationAllowed"/>.
/// </remarks>
public static class SimpleBlockGenerator {
    /// <summary>
    /// 为单个物品在给定区域生成规则块 / Generate regular blocks for a single item inside the region.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <param name="space">目标区域 / Target region.</param>
    /// <param name="config">配置 / Configuration.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>生成的块列表 / Generated blocks.</returns>
    public static Result<IReadOnlyList<Block2<V>>, ErrorCode, Error<ErrorCode>> Invoke<V>(
        IAbstractItem<V> item,
        Space2<V> space,
        SimpleBlockConfig? config = null,
        Quantity<V>? kerf = null) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(space);
        SimpleBlockConfig effective = config ?? new SimpleBlockConfig();

        if (space.IsDegenerate() || item.IsCircleItem()) {
            return Results.Ok<IReadOnlyList<Block2<V>>>(Array.Empty<Block2<V>>());
        }

        var blocks = new List<Block2<V>>();
        foreach (Shape2Candidate<V> candidate
            in item.Shape.CandidateShapes(effective.WithRotation && item.AllowRotate)) {
            PackingShape2<V> shape = candidate.Shape;
            // 刀缝通过节距体现：相邻件按「包围尺寸 + kerf」推进。
            // The kerf is expressed through the pitch: neighbours advance by bounding size plus kerf.
            Quantity<V> spacing = kerf is { } explicitKerf && QuantityCore.IsPositive(explicitKerf)
                ? explicitKerf
                : QuantityOps.QuantityZeroOf(shape.BoundingWidth);
            QuantityVector2<V> pitch = Kerf.PitchOf(shape, spacing);
            int cols = QuantityCore.CountFitting(space.Width, pitch.X);
            int rows = QuantityCore.CountFitting(space.Height, pitch.Y);
            if (cols <= 0 || rows <= 0) {
                continue;
            }

            Quantity<V> blockWidth = QuantityCore.ScaledQuantity(pitch.X, cols);
            Quantity<V> blockHeight = QuantityCore.ScaledQuantity(pitch.Y, rows);
            var placements = new List<Placement2<V>>(cols * rows);
            for (int row = 0; row < rows; row++) {
                if (LayerGenerationSupport.AddOrNull(space.Y, QuantityCore.ScaledQuantity(pitch.Y, row))
                    is not { } y) {
                    return FailedInternal<V>();
                }
                for (int col = 0; col < cols; col++) {
                    if (LayerGenerationSupport.AddOrNull(space.X, QuantityCore.ScaledQuantity(pitch.X, col))
                        is not { } x) {
                        return FailedInternal<V>();
                    }
                    placements.Add(new Placement2<V>(
                        Shape: shape,
                        Position: new QuantityPoint2<V>(x, y),
                        Rotated: candidate.Rotated,
                        ItemId: item.Id.Value));
                }
            }
            blocks.Add(new Block2<V>(
                new Space2<V>(space.X, space.Y, blockWidth, blockHeight),
                placements,
                candidate.Rotated ? "simple-block-rotated" : "simple-block"));

            if (!effective.WithRemainder) {
                continue;
            }
            if (space.SplitX(blockWidth) is not { IsOk: true } split) {
                return FailedInternal<V>();
            }
            Space2<V> remainingRight = split.Value.Right;
            if (remainingRight.IsDegenerate()) {
                continue;
            }
            Result<IReadOnlyList<Block2<V>>, ErrorCode, Error<ErrorCode>> remainder =
                Invoke(item, remainingRight, effective, kerf);
            if (!remainder.IsOk) {
                return remainder;
            }
            blocks.AddRange(remainder.Value);
        }
        return Results.Ok<IReadOnlyList<Block2<V>>>(blocks);
    }

    /// <summary>
    /// 构造内部错误 / Build an internal error.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <returns>失败结果 / A failed result.</returns>
    private static Result<IReadOnlyList<Block2<V>>, ErrorCode, Error<ErrorCode>> FailedInternal<V>()
        where V : struct, IFloatingNumber<V> =>
        Results.Failed<IReadOnlyList<Block2<V>>>(new Bpp2dInternalError(
            "块生成内部失败 / Block generation failed internally"));
}

/// <summary>
/// 复杂块生成器 / Complex block generator.
/// </summary>
/// <remarks>
/// 按给定物品顺序**逐行拼接**异构块：每行只用一种物品，行高由该物品的包围高度决定，行内按节距铺满列数。这样得到的块
/// 是同质的行叠加，既能被下层几何逐对复核，也能在候选生成里代表"多种物品混排"的一种可行形态。
/// Stitches a heterogeneous block **row by row** in the given item order: each row uses one item, its height comes from
/// that item's bounding height and its columns are filled by pitch. The result is a stack of homogeneous rows that the
/// underlying geometry can re-validate pairwise and that represents one feasible mixed-item layout for candidate
/// generation.
/// </remarks>
public static class ComplexBlockGenerator {
    /// <summary>
    /// 按给定物品顺序逐行拼接异构块 / Stitch a heterogeneous block row by row in the given item order.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="items">按优先级排序的物品 / Items ordered by priority.</param>
    /// <param name="space">目标区域 / Target region.</param>
    /// <param name="withRotation">是否允许旋转候选 / Whether rotated candidates are allowed.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>生成的块列表 / Generated blocks.</returns>
    public static Result<IReadOnlyList<Block2<V>>, ErrorCode, Error<ErrorCode>> Invoke<V>(
        IReadOnlyList<IAbstractItem<V>> items,
        Space2<V> space,
        bool withRotation = true,
        Quantity<V>? kerf = null) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(space);

        if (space.IsDegenerate() || items.Count == 0) {
            return Results.Ok<IReadOnlyList<Block2<V>>>(Array.Empty<Block2<V>>());
        }

        var placements = new List<Placement2<V>>();
        var sources = new List<string>();
        Quantity<V> cursorY = space.Y;
        Quantity<V> usedWidth = QuantityOps.QuantityZeroOf(space.Width);

        foreach (IAbstractItem<V> item in items) {
            if (item.IsCircleItem()) {
                continue;
            }
            if (LayerGenerationSupport.AddOrNull(space.Y, space.Height) is not { } spaceTop
                || LayerGenerationSupport.SubtractOrNull(spaceTop, cursorY) is not { } remainingHeight) {
                return Results.Failed<IReadOnlyList<Block2<V>>>(new Bpp2dInternalError(
                    "块生成内部失败 / Block generation failed internally"));
            }
            if (!QuantityCore.IsPositive(remainingHeight)) {
                break;
            }

            // 与 SimpleBlockGenerator 一致：配置与物品许可必须同时成立。
            // Consistent with SimpleBlockGenerator: the config and the item's allowance must both hold.
            Shape2Candidate<V>? candidate = item.Shape
                .CandidateShapes(withRotation && item.AllowRotate)
                .Where(entry => space.Enabled(entry.Shape))
                .Where(entry => QuantityCore.LessOrEqual(entry.Shape.BoundingHeight, remainingHeight))
                .MaxByQuantityOrNull(entry => entry.Shape.BoundingWidth);
            if (candidate is not { } chosen) {
                continue;
            }

            PackingShape2<V> shape = chosen.Shape;
            // 刀缝通过节距体现。 / The kerf is expressed through the pitch.
            Quantity<V> spacing = kerf is { } explicitKerf && QuantityCore.IsPositive(explicitKerf)
                ? explicitKerf
                : QuantityOps.QuantityZeroOf(shape.BoundingWidth);
            QuantityVector2<V> pitch = Kerf.PitchOf(shape, spacing);
            int cols = QuantityCore.CountFitting(space.Width, pitch.X);
            if (cols <= 0) {
                continue;
            }
            Quantity<V> rowWidth = QuantityCore.ScaledQuantity(pitch.X, cols);
            for (int col = 0; col < cols; col++) {
                if (LayerGenerationSupport.AddOrNull(space.X, QuantityCore.ScaledQuantity(pitch.X, col))
                    is not { } x) {
                    return Results.Failed<IReadOnlyList<Block2<V>>>(new Bpp2dInternalError(
                        "块生成内部失败 / Block generation failed internally"));
                }
                placements.Add(new Placement2<V>(
                    Shape: shape,
                    Position: new QuantityPoint2<V>(x, cursorY),
                    Rotated: chosen.Rotated,
                    ItemId: item.Id.Value));
            }
            usedWidth = QuantityCore.MaxOf(usedWidth, rowWidth);
            string source = chosen.Rotated ? "complex-block-rotated" : "complex-block";
            if (!sources.Contains(source)) {
                sources.Add(source);
            }
            if (LayerGenerationSupport.AddOrNull(cursorY, pitch.Y) is not { } nextCursor) {
                return Results.Failed<IReadOnlyList<Block2<V>>>(new Bpp2dInternalError(
                    "块生成内部失败 / Block generation failed internally"));
            }
            cursorY = nextCursor;
        }

        if (placements.Count == 0) {
            return Results.Ok<IReadOnlyList<Block2<V>>>(Array.Empty<Block2<V>>());
        }
        if (LayerGenerationSupport.SubtractOrNull(cursorY, space.Y) is not { } blockHeight) {
            return Results.Failed<IReadOnlyList<Block2<V>>>(new Bpp2dInternalError(
                "块生成内部失败 / Block generation failed internally"));
        }
        var block = new Block2<V>(
            new Space2<V>(space.X, space.Y, usedWidth, blockHeight),
            placements,
            string.Join("+", sources));
        return Results.Ok<IReadOnlyList<Block2<V>>>(new[] { block });
    }
}
