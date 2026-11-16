// 空间切分搜索与块装载上下文 / Space-splitting search and the block-loading context.
// 与固定 Kotlin 基线 `block_loading/service/SpaceSplitAlgorithms.kt`（293 行）与 `block_loading/BlockLoadingContext.kt`
// （131 行）对应。
// Mirrors the pinned baseline's `SpaceSplitAlgorithms.kt` and `BlockLoadingContext.kt`.
//
// 至此 `block_loading` 域在 C# 侧闭合（模型、旋转守卫、简单/复杂块、两种空间切分、装配上下文）。后续可把三个块装载
// 层生成器接进 `LayerGenerationContext.DefaultGenerators`，消除 earlier 记下的缺口。
// The `block_loading` domain is now closed on the C# side (model, rotation guard, simple/complex blocks, both space
// splits, assembly context). The next batch can wire the three block-loading layer generators into
// `LayerGenerationContext.DefaultGenerators` and close the gap recorded in earlier.

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
/// 空间切分搜索结果 / Space-splitting search result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Placements">放置列表 / Placement list.</param>
/// <param name="PlacedAmounts">各物品已放置件数 / Placed amount per item.</param>
/// <param name="RestAmounts">各物品剩余件数 / Remaining amount per item.</param>
/// <param name="Source">算法来源标记 / Algorithm source marker.</param>
public sealed record SpaceSplit2Result<V>(
    IReadOnlyList<Placement2<V>> Placements,
    IReadOnlyDictionary<string, int> PlacedAmounts,
    IReadOnlyDictionary<string, int> RestAmounts,
    string Source) where V : struct, IFloatingNumber<V>;

/// <summary>
/// 空间切分公共辅助 / Shared space-split helpers.
/// </summary>
public static class SpaceSplitSupport {
    /// <summary>
    /// 由剩余件数推导已放置件数 / Derive placed amounts from the remaining ones.
    /// </summary>
    /// <remarks>
    /// 只记录**真正放置过**的物品：剩余件数等于初始件数的物品不出现在结果里。这样下游按 `PlacedAmounts` 遍历时不必
    /// 再过滤零项。
    /// Only items that were **actually placed** are recorded: an item whose remaining amount still equals its initial
    /// amount does not appear, so downstream need not filter zeros when iterating `PlacedAmounts`.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <param name="rest">剩余件数 / Remaining amounts.</param>
    /// <returns>已放置件数 / Placed amounts.</returns>
    public static IReadOnlyDictionary<string, int> PlacedAmountsOf<V>(
        IReadOnlyList<(ActualItem<V> Item, int Amount)> items,
        IReadOnlyDictionary<string, int> rest) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rest);

        var placed = new Dictionary<string, int>();
        foreach ((ActualItem<V> item, int amount) in items) {
            int left = rest.TryGetValue(item.Id.Value, out int value) ? value : 0;
            if (left < amount) {
                placed[item.Id.Value] = amount - left;
            }
        }
        return placed;
    }
}

/// <summary>
/// 深度优先空间切分算法 / Depth-first space-splitting algorithm.
/// </summary>
/// <remarks>
/// 每次取剩余区域，放入**包围面积最大**的可放物品，然后沿放置块的外边界切分剩余空间（右侧与上方两块）并递归。
/// 包围面积优先意味着先放大件：小件留到最后填缝更容易，反过来则会在大件到来前把区域切碎。
/// Each step takes the remaining region and places the **largest-placeable** item by bounding area, then splits the
/// remainder along the block boundary (right and top) and recurses. Preferring larger bounding area means big pieces go
/// first: small pieces fit into gaps more easily at the end, whereas the opposite order fragments the region before the big
/// piece arrives.
/// </remarks>
public static class DepthFirstSearchAlgorithm2D {
    /// <summary>
    /// 执行深度优先空间切分 / Run the depth-first space split.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="space">初始区域 / Initial region.</param>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <param name="maxDepth">最大递归深度 / Maximum recursion depth.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>搜索结果 / The search result.</returns>
    public static Result<SpaceSplit2Result<V>, ErrorCode, Error<ErrorCode>> Invoke<V>(
        Space2<V> space,
        IReadOnlyList<(ActualItem<V> Item, int Amount)> items,
        int maxDepth = 8,
        Quantity<V>? kerf = null) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(items);

        var placements = new List<Placement2<V>>();
        var rest = new Dictionary<string, int>();
        foreach ((ActualItem<V> item, int amount) in items) {
            rest[item.Id.Value] = amount;
        }

        if (!Descend(space, items, rest, placements, 0, maxDepth, kerf)) {
            return Results.Failed<SpaceSplit2Result<V>>(new Bpp2dInternalError(
                "空间切分内部失败 / Space split failed internally"));
        }
        return Results.Ok(new SpaceSplit2Result<V>(
            Placements: placements,
            PlacedAmounts: SpaceSplitSupport.PlacedAmountsOf(items, rest),
            RestAmounts: rest.Where(pair => pair.Value > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            Source: "dfs"));
    }

    /// <summary>
    /// 递归放置 / Recursive placement step.
    /// </summary>
    /// <remarks>
    /// 返回 `false` 表示发生了内部错误（量纲不符等），让调用方转成失败结果；正常的"放不下"返回 `true` 并原地结束。
    /// Returns `false` only on an internal error (dimension mismatch and the like) so the caller can fail properly;
    /// the normal "nothing fits" case returns `true` and stops in place.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="space">当前区域 / Current region.</param>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <param name="rest">剩余件数，原地更新 / Remaining amounts, updated in place.</param>
    /// <param name="placements">放置列表，原地追加 / Placement list, appended in place.</param>
    /// <param name="depth">当前深度 / Current depth.</param>
    /// <param name="maxDepth">最大深度 / Maximum depth.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>是否无内部错误 / Whether no internal error occurred.</returns>
    private static bool Descend<V>(
        Space2<V> space,
        IReadOnlyList<(ActualItem<V> Item, int Amount)> items,
        Dictionary<string, int> rest,
        List<Placement2<V>> placements,
        int depth,
        int maxDepth,
        Quantity<V>? kerf) where V : struct, IFloatingNumber<V> {
        if (depth >= maxDepth || space.IsDegenerate()) {
            return true;
        }
        // 用 `TryMaxByQuantity` 而不是 `MaxByQuantityOrNull`：这里选的是值类型元组，后者在空序列上会返回
        // `default(T)`（成员全为默认值）而不是 null。
        // Uses `TryMaxByQuantity` rather than `MaxByQuantityOrNull`: the selection here is a value-type tuple, and the
        // latter returns `default(T)` (all-default members) instead of null on an empty sequence.
        if (!items
            .Where(entry => (rest.TryGetValue(entry.Item.Id.Value, out int left) ? left : 0) > 0
                && !entry.Item.IsCircleItem())
            .TryMaxByQuantity(entry => entry.Item.Shape.BoundingArea, out (ActualItem<V> Item, int Amount) chosenItem)) {
            return true;
        }

        ActualItem<V> item = chosenItem.Item;
        Shape2Candidate<V>? candidate = item.Shape
            .CandidateShapes(item.AllowRotate)
            .Where(entry => space.Enabled(entry.Shape))
            .MaxByQuantityOrNull(entry => entry.Shape.BoundingWidth);
        if (candidate is not { } chosen) {
            return true;
        }

        PackingShape2<V> shape = chosen.Shape;
        if (TryPitch(shape, kerf) is not { } pitch) {
            return false;
        }
        int cols = QuantityCore.CountFitting(space.Width, pitch.X);
        int rows = QuantityCore.CountFitting(space.Height, pitch.Y);
        if (cols <= 0 || rows <= 0) {
            return true;
        }

        int remaining = rest.TryGetValue(item.Id.Value, out int initial) ? initial : 0;
        int placedCols = 0;
        int placedRows = 0;
        bool exhausted = false;
        for (int row = 0; row < rows && !exhausted; row++) {
            for (int col = 0; col < cols; col++) {
                if (remaining <= 0) {
                    exhausted = true;
                    break;
                }
                if (LayerGenerationSupport.AddOrNull(space.X, QuantityCore.ScaledQuantity(pitch.X, col))
                        is not { } x
                    || LayerGenerationSupport.AddOrNull(space.Y, QuantityCore.ScaledQuantity(pitch.Y, row))
                        is not { } y) {
                    return false;
                }
                placements.Add(new Placement2<V>(
                    Shape: shape,
                    Position: new QuantityPoint2<V>(x, y),
                    Rotated: chosen.Rotated,
                    ItemId: item.Id.Value));
                remaining--;
                placedCols = System.Math.Max(placedCols, col + 1);
                placedRows = System.Math.Max(placedRows, row + 1);
            }
        }
        rest[item.Id.Value] = remaining;

        Quantity<V> usedWidth = QuantityCore.ScaledQuantity(pitch.X, placedCols);
        Quantity<V> usedHeight = QuantityCore.ScaledQuantity(pitch.Y, placedRows);
        if (space.SplitX(usedWidth) is not { IsOk: true } split) {
            return false;
        }
        if (LayerGenerationSupport.AddOrNull(space.Y, usedHeight) is not { } topY
            || LayerGenerationSupport.SubtractOrNull(space.Height, usedHeight) is not { } topHeight) {
            return false;
        }
        var topSpace = new Space2<V>(space.X, topY, usedWidth, topHeight);

        return Descend(split.Value.Right, items, rest, placements, depth + 1, maxDepth, kerf)
            && Descend(topSpace, items, rest, placements, depth + 1, maxDepth, kerf);
    }

    /// <summary>
    /// 计算节距，量纲不符时返回 null / Compute the pitch, returning null on dimension mismatch.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>节距或 null / The pitch, or null.</returns>
    private static QuantityVector2<V>? TryPitch<V>(PackingShape2<V> shape, Quantity<V>? kerf)
        where V : struct, IFloatingNumber<V> {
        Quantity<V> spacing = kerf is { } explicitKerf && QuantityCore.IsPositive(explicitKerf)
            ? explicitKerf
            : QuantityOps.QuantityZeroOf(shape.BoundingWidth);
        if (LayerGenerationSupport.AddOrNull(shape.BoundingWidth, spacing) is not { } x
            || LayerGenerationSupport.AddOrNull(shape.BoundingHeight, spacing) is not { } y) {
            return null;
        }
        return new QuantityVector2<V>(x, y);
    }
}

/// <summary>
/// 多行启发式搜索算法 / Multi-row heuristic search algorithm.
/// </summary>
/// <remarks>
/// 按行推进：每行选择**能放入剩余宽度中数量最多**的物品，放满一行后按行高向上推进。与 DFS 的取舍不同——DFS 按面积
/// 深度优先切碎空间，多行启发式坚持整齐的行，因此在长条形板材上通常更整齐、余料更整块，但装不下异形组合。
/// Advances row by row: each row picks the item fitting **the most copies into the remaining width**, then moves up by
/// that row's height. The trade-off differs from DFS: DFS fragments space depth-first by area while this keeps tidy rows,
/// which tends to leave more contiguous remnants on long sheets but cannot pack awkward mixes.
/// </remarks>
public static class MultiRowHeuristicAlgorithm2D {
    /// <summary>
    /// 执行多行启发式搜索 / Run the multi-row heuristic search.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="space">初始区域 / Initial region.</param>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <param name="maxRows">最大行数 / Maximum row count.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>搜索结果 / The search result.</returns>
    public static Result<SpaceSplit2Result<V>, ErrorCode, Error<ErrorCode>> Invoke<V>(
        Space2<V> space,
        IReadOnlyList<(ActualItem<V> Item, int Amount)> items,
        int maxRows = 32,
        Quantity<V>? kerf = null) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(items);

        var placements = new List<Placement2<V>>();
        var rest = new Dictionary<string, int>();
        foreach ((ActualItem<V> item, int amount) in items) {
            rest[item.Id.Value] = amount;
        }

        Quantity<V> cursorY = space.Y;
        int rows = 0;
        while (rows < maxRows) {
            if (LayerGenerationSupport.AddOrNull(space.Y, space.Height) is not { } spaceTop
                || LayerGenerationSupport.SubtractOrNull(spaceTop, cursorY) is not { } remainingHeight) {
                return Results.Failed<SpaceSplit2Result<V>>(new Bpp2dInternalError(
                    "空间切分内部失败 / Space split failed internally"));
            }
            if (!QuantityCore.IsPositive(remainingHeight)) {
                break;
            }

            RowPick<V>? pick = null;
            foreach ((ActualItem<V> item, int _) in items) {
                if ((rest.TryGetValue(item.Id.Value, out int left) ? left : 0) <= 0 || item.IsCircleItem()) {
                    continue;
                }
                Shape2Candidate<V>? candidate = item.Shape
                    .CandidateShapes(item.AllowRotate)
                    .Where(entry => space.Enabled(entry.Shape))
                    .Where(entry => QuantityCore.LessOrEqual(entry.Shape.BoundingHeight, remainingHeight))
                    .MaxByQuantityOrNull(entry => entry.Shape.BoundingWidth);
                if (candidate is not { } chosen) {
                    continue;
                }
                if (TryPitch(chosen.Shape, kerf) is not { } pitch) {
                    continue;
                }
                int fitCols = QuantityCore.CountFitting(space.Width, pitch.X);
                if (fitCols <= 0) {
                    continue;
                }
                if (pick is null || fitCols > pick.Cols) {
                    pick = new RowPick<V>(item, chosen, fitCols, pitch);
                }
            }
            if (pick is not { } rowPick) {
                break;
            }

            int remaining = rest.TryGetValue(rowPick.Item.Id.Value, out int initial) ? initial : 0;
            int placed = 0;
            for (int col = 0; col < rowPick.Cols; col++) {
                if (remaining <= 0) {
                    break;
                }
                if (LayerGenerationSupport.AddOrNull(space.X, QuantityCore.ScaledQuantity(rowPick.Pitch.X, col))
                    is not { } x) {
                    return Results.Failed<SpaceSplit2Result<V>>(new Bpp2dInternalError(
                        "空间切分内部失败 / Space split failed internally"));
                }
                placements.Add(new Placement2<V>(
                    Shape: rowPick.Candidate.Shape,
                    Position: new QuantityPoint2<V>(x, cursorY),
                    Rotated: rowPick.Candidate.Rotated,
                    ItemId: rowPick.Item.Id.Value));
                remaining--;
                placed++;
            }
            rest[rowPick.Item.Id.Value] = remaining;
            if (placed == 0) {
                break;
            }
            if (LayerGenerationSupport.AddOrNull(cursorY, rowPick.Pitch.Y) is not { } nextCursor) {
                return Results.Failed<SpaceSplit2Result<V>>(new Bpp2dInternalError(
                    "空间切分内部失败 / Space split failed internally"));
            }
            cursorY = nextCursor;
            rows++;
        }

        return Results.Ok(new SpaceSplit2Result<V>(
            Placements: placements,
            PlacedAmounts: SpaceSplitSupport.PlacedAmountsOf(items, rest),
            RestAmounts: rest.Where(pair => pair.Value > 0)
                .ToDictionary(pair => pair.Key, pair => pair.Value),
            Source: "multi-row"));
    }

    /// <summary>
    /// 一行的候选选择结果 / Candidate choice for one row.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="Item">物品 / Item.</param>
    /// <param name="Candidate">形状候选 / Shape candidate.</param>
    /// <param name="Cols">该行件数 / Amount in the row.</param>
    /// <param name="Pitch">该行的节距 / Pitch of the row.</param>
    private sealed record RowPick<V>(
        ActualItem<V> Item,
        Shape2Candidate<V> Candidate,
        int Cols,
        QuantityVector2<V> Pitch) where V : struct, IFloatingNumber<V>;

    /// <summary>
    /// 计算节距，量纲不符时返回 null / Compute the pitch, returning null on dimension mismatch.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="shape">装载形状 / Packing shape.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>节距或 null / The pitch, or null.</returns>
    private static QuantityVector2<V>? TryPitch<V>(PackingShape2<V> shape, Quantity<V>? kerf)
        where V : struct, IFloatingNumber<V> {
        Quantity<V> spacing = kerf is { } explicitKerf && QuantityCore.IsPositive(explicitKerf)
            ? explicitKerf
            : QuantityOps.QuantityZeroOf(shape.BoundingWidth);
        if (LayerGenerationSupport.AddOrNull(shape.BoundingWidth, spacing) is not { } x
            || LayerGenerationSupport.AddOrNull(shape.BoundingHeight, spacing) is not { } y) {
            return null;
        }
        return new QuantityVector2<V>(x, y);
    }
}

/// <summary>
/// 块装载领域上下文 / Block loading domain context.
/// </summary>
/// <remarks>
/// 无状态装配层：四个组件都是纯函数式的（输入区域与物品，输出块或搜索结果），因此上下文本身不持可变状态，可安全复用。
/// A stateless assembly layer: all four components are functional (region plus items in, blocks or search results out),
/// so the context holds no mutable state and is safe to reuse.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class BlockLoadingContext<V> where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 生成局部块候选：对每个物品单独生成规则块 / Generate local block candidates: regular blocks per item.
    /// </summary>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <param name="space">目标区域 / Target region.</param>
    /// <param name="config">简单块配置 / Simple block configuration.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>块候选列表 / Block candidates.</returns>
    public Result<IReadOnlyList<Block2<V>>, ErrorCode, Error<ErrorCode>> LocalBlocks(
        IReadOnlyList<(ActualItem<V> Item, int Amount)> items,
        Space2<V> space,
        SimpleBlockConfig? config = null,
        Quantity<V>? kerf = null) {
        ArgumentNullException.ThrowIfNull(items);

        var blocks = new List<Block2<V>>();
        foreach ((ActualItem<V> item, int _) in items) {
            Result<IReadOnlyList<Block2<V>>, ErrorCode, Error<ErrorCode>> result =
                SimpleBlockGenerator.Invoke(item, space, config, kerf);
            if (!result.IsOk) {
                return result;
            }
            blocks.AddRange(result.Value);
        }
        return Results.Ok<IReadOnlyList<Block2<V>>>(blocks);
    }

    /// <summary>
    /// 生成全局块候选：多种物品拼接为异构块 / Generate global block candidates: heterogeneous blocks.
    /// </summary>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <param name="space">目标区域 / Target region.</param>
    /// <param name="withRotation">是否允许旋转候选 / Whether rotated candidates are allowed.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>块候选列表 / Block candidates.</returns>
    public Result<IReadOnlyList<Block2<V>>, ErrorCode, Error<ErrorCode>> GlobalBlocks(
        IReadOnlyList<(ActualItem<V> Item, int Amount)> items,
        Space2<V> space,
        bool withRotation = true,
        Quantity<V>? kerf = null) {
        ArgumentNullException.ThrowIfNull(items);
        return ComplexBlockGenerator.Invoke(
            items.Select(entry => (IAbstractItem<V>)entry.Item).ToList(),
            space,
            withRotation,
            kerf);
    }

    /// <summary>
    /// 执行 DFS 空间切分 / Run the DFS space split.
    /// </summary>
    /// <param name="space">初始区域 / Initial region.</param>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <param name="maxDepth">最大递归深度 / Maximum recursion depth.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>搜索结果 / The search result.</returns>
    public Result<SpaceSplit2Result<V>, ErrorCode, Error<ErrorCode>> DepthFirst(
        Space2<V> space,
        IReadOnlyList<(ActualItem<V> Item, int Amount)> items,
        int maxDepth = 8,
        Quantity<V>? kerf = null) =>
        DepthFirstSearchAlgorithm2D.Invoke(space, items, maxDepth, kerf);

    /// <summary>
    /// 执行多行启发式搜索 / Run the multi-row heuristic search.
    /// </summary>
    /// <param name="space">初始区域 / Initial region.</param>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <param name="maxRows">最大行数 / Maximum row count.</param>
    /// <param name="kerf">刀缝间距 / Kerf spacing.</param>
    /// <returns>搜索结果 / The search result.</returns>
    public Result<SpaceSplit2Result<V>, ErrorCode, Error<ErrorCode>> MultiRow(
        Space2<V> space,
        IReadOnlyList<(ActualItem<V> Item, int Amount)> items,
        int maxRows = 32,
        Quantity<V>? kerf = null) =>
        MultiRowHeuristicAlgorithm2D.Invoke(space, items, maxRows, kerf);
}
