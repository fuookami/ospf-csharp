// 层生成器与组合上下文 / Layer generators and the composite context.
// 与固定 Kotlin 基线 `bpp2d/domain/layer_generation/LayerGenerationContext.kt`（597 行）对应。
// Mirrors the pinned baseline's `LayerGenerationContext.kt`.
//
// earlier 闭合了 `block_loading` 域，这里随即补上三个块装载生成器（`BlLocalLayerGenerator`／
// `BlGlobalLayerGenerator`／`SpaceSplitLayerGenerator`），`DefaultGenerators` 至此与基线一致（八个）。
// earlier closed the `block_loading` domain, so the three block-loading generators
// (`BlLocalLayerGenerator` / `BlGlobalLayerGenerator` / `SpaceSplitLayerGenerator`); `DefaultGenerators` now matches the
// baseline (eight).

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Geometry;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 层生成公共辅助 / Shared layer-generation helpers.
/// </summary>
public static class LayerGenerationSupport {
    /// <summary>
    /// 由放置列表与板材构造候选层 / Build a candidate layer from placements and a sheet.
    /// </summary>
    /// <remarks>
    /// 层的包围形状取所有放置的最大右边界与最大上边界，因此**层形状总是紧贴内容**。这意味着层形状可能小于板材，
    /// 下游按层的包围形状复用余料时才不会把空区域算进去。
    /// The layer's bounding shape uses the maximum right and top edges over all placements, so **the shape always hugs
    /// its content**. A layer may therefore be smaller than the sheet, which keeps empty area out of remnant reuse that
    /// works off the layer's bounding shape.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="iteration">生成迭代 / Generation iteration.</param>
    /// <param name="from">生成来源类型 / Generation source type.</param>
    /// <param name="sheet">板材类型 / Sheet type.</param>
    /// <param name="placements">放置列表 / Placement list.</param>
    /// <returns>候选层 / The candidate layer.</returns>
    public static Layer<V> LayerOf<V>(
        int iteration,
        Type? from,
        SheetType<V> sheet,
        IReadOnlyList<Placement2<V>> placements) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(placements);

        Quantity<V> width = QuantityOps.QuantityZeroOf(sheet.Width);
        Quantity<V> height = QuantityOps.QuantityZeroOf(sheet.Height);
        foreach (Placement2<V> placement in placements) {
            if (placement.MaxX() is { IsOk: true } maxX) {
                width = QuantityCore.MaxOf(width, maxX.Value);
            }
            if (placement.MaxY() is { IsOk: true } maxY) {
                height = QuantityCore.MaxOf(height, maxY.Value);
            }
        }
        return new Layer<V>(
            iteration,
            from,
            sheet,
            new Sheet2Shape<V>(width, height),
            placements);
    }

    /// <summary>
    /// 构造初始空间 / Build the initial space of a sheet.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="sheet">板材类型 / Sheet type.</param>
    /// <returns>初始空间 / The initial space.</returns>
    public static Space2<V> InitialSpaceOf<V>(SheetType<V> sheet) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(sheet);
        return new Space2<V>(
            QuantityOps.QuantityZeroOf(sheet.Width),
            QuantityOps.QuantityZeroOf(sheet.Height),
            sheet.Width,
            sheet.Height);
    }

    /// <summary>量值相加，量纲不符时返回 null / Add quantities, returning null on dimension mismatch.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左值 / Left value.</param>
    /// <param name="rhs">右值 / Right value.</param>
    /// <returns>和或 null / The sum, or null.</returns>
    public static Quantity<V>? AddOrNull<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> =>
        QuantityOperators.Add<V>(lhs, rhs) is { IsOk: true } sum ? sum.Value : null;

    /// <summary>量值相减，量纲不符时返回 null / Subtract quantities, returning null on dimension mismatch.</summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="lhs">左值 / Left value.</param>
    /// <param name="rhs">右值 / Right value.</param>
    /// <returns>差或 null / The difference, or null.</returns>
    public static Quantity<V>? SubtractOrNull<V>(Quantity<V> lhs, Quantity<V> rhs)
        where V : struct, IFloatingNumber<V> =>
        QuantityOperators.Subtract<V>(lhs, rhs) is { IsOk: true } difference ? difference.Value : null;
}

/// <summary>
/// 历史层生成器：回放请求中已有的层 / Historical layer generator: replays the layers already in the request.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class HistoricalLayerGenerator<V> : IBpp2dLayerGenerator<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public IReadOnlyList<Bpp2dLayerGenerationResult<V>> Generate(
        Bpp2dLayerGenerationRequest<V> request) {
        ArgumentNullException.ThrowIfNull(request);
        return request.LayerList
            .Select(layer => new Bpp2dLayerGenerationResult<V>(
                Layer: layer,
                Source: "historical"))
            .ToList();
    }
}

/// <summary>
/// BLA 生成器：在板材上执行底部向上左对齐排样 / BLA generator: runs bottom-up left-justified layout on the sheet.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Config">BLA 配置 / BLA configuration.</param>
public sealed record BlaLayerGenerator<V>(
    BottomUpLeftJustifiedAlgorithm<V>.Config? Config = null) : IBpp2dLayerGenerator<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public IReadOnlyList<Bpp2dLayerGenerationResult<V>> Generate(
        Bpp2dLayerGenerationRequest<V> request) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Sheet is not { } sheet) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }

        var algorithm = new BottomUpLeftJustifiedAlgorithm<V>(
            sheet: new Sheet2Shape<V>(sheet.Width, sheet.Height),
            config: Config,
            kerf: request.Kerf);
        Result<Bla2dLayoutResult<V>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> layout =
            algorithm.Invoke(request.ItemList.Select(entry => ((IAbstractItem<V>)entry.Item, entry.Amount)));
        if (!layout.IsOk) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }
        if (layout.Value.Placements.Count == 0) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }
        return new[] {
            new Bpp2dLayerGenerationResult<V>(
                Layer: LayerGenerationSupport.LayerOf(
                    request.Iteration, typeof(BlaLayerGenerator<V>), sheet, layout.Value.Placements),
                Source: "bla"),
        };
    }
}

/// <summary>
/// 图样生成器：按「每行件数 × 行数」的图样铺满 / Pattern generator: fills by a "per-row count × row count" pattern.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class PatternLayerGenerator<V> : IBpp2dLayerGenerator<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public IReadOnlyList<Bpp2dLayerGenerationResult<V>> Generate(
        Bpp2dLayerGenerationRequest<V> request) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Sheet is not { } sheet) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }

        var results = new List<Bpp2dLayerGenerationResult<V>>();
        foreach ((ActualItem<V> item, int _) in request.ItemList) {
            if (item.IsCircleItem()) {
                continue;
            }
            foreach (Shape2Candidate<V> candidate in item.Shape.CandidateShapes(item.AllowRotate)) {
                PackingShape2<V> shape = candidate.Shape;
                // 刀缝通过节距体现：相邻件按「包围尺寸 + kerf」推进，真实间隙恰为 kerf。
                // The kerf is expressed through the pitch: neighbours advance by bounding size plus kerf, so the real
                // gap is exactly the kerf.
                Quantity<V> spacing = request.KerfSpacing(shape.BoundingWidth);
                if (LayerGenerationSupport.AddOrNull(shape.BoundingWidth, spacing) is not { } pitchX
                    || LayerGenerationSupport.AddOrNull(shape.BoundingHeight, spacing) is not { } pitchY) {
                    continue;
                }
                int cols = QuantityCore.CountFitting(sheet.Width, pitchX);
                int rows = QuantityCore.CountFitting(sheet.Height, pitchY);
                if (cols <= 0 || rows <= 0) {
                    continue;
                }

                var placements = new List<Placement2<V>>(cols * rows);
                for (int row = 0; row < rows; row++) {
                    Quantity<V> y = QuantityCore.ScaledQuantity(pitchY, row);
                    for (int col = 0; col < cols; col++) {
                        placements.Add(new Placement2<V>(
                            Shape: shape,
                            Position: new QuantityPoint2<V>(QuantityCore.ScaledQuantity(pitchX, col), y),
                            Rotated: candidate.Rotated,
                            ItemId: item.Id.Value));
                    }
                }
                results.Add(new Bpp2dLayerGenerationResult<V>(
                    Layer: LayerGenerationSupport.LayerOf(
                        request.Iteration, typeof(PatternLayerGenerator<V>), sheet, placements),
                    Source: candidate.Rotated ? "pattern-rotated" : "pattern"));

                // 行宽小于板材宽度时，给出「整行但减少列数」的替代图样。
                // When the row is narrower than the sheet, also emit an alternative with fewer columns.
                if (QuantityCore.LessOrEqual(
                        QuantityCore.ScaledQuantity(pitchX, cols), sheet.Width) && cols > 1) {
                    results.Add(new Bpp2dLayerGenerationResult<V>(
                        Layer: LayerGenerationSupport.LayerOf(
                            request.Iteration, typeof(PatternLayerGenerator<V>), sheet, placements),
                        Source: "pattern-narrow"));
                }
            }
        }
        return results;
    }
}

/// <summary>
/// 圆形网格生成器：直角网格与六角网格 / Circle grid generator: square and hexagonal grids.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class CircleGridLayerGenerator<V> : IBpp2dLayerGenerator<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public IReadOnlyList<Bpp2dLayerGenerationResult<V>> Generate(
        Bpp2dLayerGenerationRequest<V> request) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Sheet is not { } sheet) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }

        var results = new List<Bpp2dLayerGenerationResult<V>>();
        foreach ((ActualItem<V> item, int _) in request.ItemList) {
            if (!item.IsCircleItem()) {
                continue;
            }
            foreach (CircleGridLayout<V> layout in CircleGridGenerator.Invoke(item, sheet, request.Kerf)) {
                if (layout.Placements.Count == 0) {
                    continue;
                }
                results.Add(new Bpp2dLayerGenerationResult<V>(
                    Layer: LayerGenerationSupport.LayerOf(
                        request.Iteration, typeof(CircleGridLayerGenerator<V>), sheet, layout.Placements),
                    Source: layout.Mode == CircleGridMode.SquareGrid
                        ? "circle-rect-grid"
                        : "circle-hex-grid"));
            }
        }
        return results;
    }
}

/// <summary>
/// 混合形状生成器：先生成规则层，再用底层几何逐对复核合法性 / Mixed shape generator: builds a regular layer then
/// re-validates every pair against the underlying geometry.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class MixedShapeLayerGenerator<V> : IBpp2dLayerGenerator<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public IReadOnlyList<Bpp2dLayerGenerationResult<V>> Generate(
        Bpp2dLayerGenerationRequest<V> request) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Sheet is not { } sheet) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }

        var circles = request.ItemList.Where(entry => entry.Item.IsCircleItem()).ToList();
        var rectangles = request.ItemList.Where(entry => !entry.Item.IsCircleItem()).ToList();
        if (circles.Count == 0 || rectangles.Count == 0) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }

        var results = new List<Bpp2dLayerGenerationResult<V>>();
        foreach ((ActualItem<V> rectangle, int _) in rectangles) {
            foreach (Shape2Candidate<V> candidate in rectangle.Shape.CandidateShapes(rectangle.AllowRotate)) {
                PackingShape2<V> shape = candidate.Shape;
                int cols = QuantityCore.CountFitting(sheet.Width, shape.BoundingWidth);
                if (cols <= 0) {
                    continue;
                }
                Quantity<V> rowWidth = QuantityCore.ScaledQuantity(shape.BoundingWidth, cols);

                NorthBoundary? upper = UpperRegionOf(sheet, shape, rowWidth);
                if (upper is not { } region) {
                    continue;
                }

                // 先在底部铺一行矩形件，再用圆形网格填充其上方区域。
                // First lay one row of rectangles, then fill the area above them with a circle grid.
                var baseRow = new List<Placement2<V>>(cols);
                for (int col = 0; col < cols; col++) {
                    baseRow.Add(new Placement2<V>(
                        Shape: shape,
                        Position: new QuantityPoint2<V>(
                            QuantityCore.ScaledQuantity(shape.BoundingWidth, col),
                            QuantityOps.QuantityZeroOf(sheet.Height)),
                        Rotated: candidate.Rotated,
                        ItemId: rectangle.Id.Value));
                }

                foreach ((ActualItem<V> circle, int _) in circles) {
                    foreach (CircleGridLayout<V> layout
                        in CircleGridGenerator.Invoke(circle, region.Sheet, request.Kerf)) {
                        var combined = new List<Placement2<V>>(baseRow.Count + layout.Placements.Count);
                        combined.AddRange(baseRow);
                        foreach (Placement2<V> placement in layout.Placements) {
                            if (ShiftUp(placement, sheet, shape.BoundingHeight) is not { } shifted) {
                                continue;
                            }
                            combined.Add(shifted);
                        }
                        if (ValidateNoOverlap(combined) is not { } validated) {
                            continue;
                        }
                        results.Add(new Bpp2dLayerGenerationResult<V>(
                            Layer: LayerGenerationSupport.LayerOf(
                                request.Iteration, typeof(MixedShapeLayerGenerator<V>), sheet, validated),
                            Source: "mixed-shape"));
                    }
                }
            }
        }
        return results;
    }

    /// <summary>
    /// 用底层几何逐对复核放置是否互不重叠 / Re-validate pairwise non-overlap with the underlying geometry.
    /// </summary>
    /// <remarks>
    /// 这一步**不是 positional 冗余校验**：圆形网格是在"上移后的子板材"里生成的，上移后可能与底部那行矩形件相撞。
    /// 逐对复核是这里唯一能证明整层合法的办法，因此宁可 O(n²) 也不省。
    /// This step is **not redundant**: circle grids are generated inside a shifted sub-sheet and may collide with the
    /// bottom rectangle row once shifted. A pairwise sweep is the only way to prove the whole layer legal here, so the
    /// O(n²) cost is accepted.
    /// </remarks>
    /// <param name="placements">放置列表 / Placement list.</param>
    /// <returns>合法放置列表，存在重叠时返回 null / Validated placements, or null on any overlap.</returns>
    private static IReadOnlyList<Placement2<V>>? ValidateNoOverlap(IReadOnlyList<Placement2<V>> placements) {
        for (int i = 0; i < placements.Count; i++) {
            for (int j = i + 1; j < placements.Count; j++) {
                if (placements[i].Overlapped(placements[j]) is not { IsOk: true } overlapped
                    || overlapped.Value) {
                    return null;
                }
            }
        }
        return placements;
    }

    /// <summary>圆网格可用的上方区域 / The upper region available to the circle grid.</summary>
    /// <param name="Sheet">上方区域对应的板材类型 / Sheet type of the upper region.</param>
    private sealed record NorthBoundary(SheetType<V> Sheet);

    /// <summary>由板材与矩形行高求上方区域 / Derive the upper region from the sheet and the row height.</summary>
    /// <param name="sheet">板材类型 / Sheet type.</param>
    /// <param name="shape">矩形形状 / Rectangle shape.</param>
    /// <param name="rowWidth">行宽 / Row width.</param>
    /// <returns>上方区域或 null / The upper region, or null.</returns>
    private static NorthBoundary? UpperRegionOf(
        SheetType<V> sheet,
        PackingShape2<V> shape,
        Quantity<V> rowWidth) {
        if (LayerGenerationSupport.SubtractOrNull(sheet.Height, shape.BoundingHeight) is not { } upperHeight
            || !QuantityCore.IsPositive(upperHeight)) {
            return null;
        }
        return new NorthBoundary(new SheetType<V>(
            rowWidth,
            upperHeight,
            sheet.MaxLoad,
            sheet.TypeCode,
            sheet.IsMain));
    }

    /// <summary>把放置上移并确认仍位于板材内 / Shift a placement up and confirm it is still inside the sheet.</summary>
    /// <param name="placement">放置 / Placement.</param>
    /// <param name="sheet">板材类型 / Sheet type.</param>
    /// <param name="offset">上移量 / Upward offset.</param>
    /// <returns>上移后的放置或 null / The shifted placement, or null.</returns>
    private static Placement2<V>? ShiftUp(
        Placement2<V> placement,
        SheetType<V> sheet,
        Quantity<V> offset) {
        Result<Placement2<V>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> shifted =
            placement.Translate(new QuantityVector2<V>(QuantityOps.QuantityZeroOf(sheet.Width), offset));
        if (!shifted.IsOk) {
            return null;
        }
        Result<bool, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> inside =
            shifted.Value.InsideSheet(new Sheet2Shape<V>(sheet.Width, sheet.Height));
        return inside.IsOk && inside.Value ? shifted.Value : null;
    }
}

/// <summary>
/// 块装载局部生成器：每个物品单独形成规则块 / Block-loading local generator: one regular block per item.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class BlLocalLayerGenerator<V> : IBpp2dLayerGenerator<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public IReadOnlyList<Bpp2dLayerGenerationResult<V>> Generate(
        Bpp2dLayerGenerationRequest<V> request) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Sheet is not { } sheet) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }

        Result<IReadOnlyList<Block2<V>>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>
            blocks = new BlockLoadingContext<V>().LocalBlocks(
                request.ItemList,
                LayerGenerationSupport.InitialSpaceOf(sheet),
                kerf: request.Kerf);
        if (!blocks.IsOk) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }
        return blocks.Value
            .Select(block => new Bpp2dLayerGenerationResult<V>(
                Layer: LayerGenerationSupport.LayerOf(
                    request.Iteration, typeof(BlLocalLayerGenerator<V>), sheet, block.Placements),
                Source: "block-local"))
            .ToList();
    }
}

/// <summary>
/// 块装载全局生成器：多物品拼接为异构块 / Block-loading global generator: heterogeneous blocks from multiple items.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class BlGlobalLayerGenerator<V> : IBpp2dLayerGenerator<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public IReadOnlyList<Bpp2dLayerGenerationResult<V>> Generate(
        Bpp2dLayerGenerationRequest<V> request) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Sheet is not { } sheet) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }

        Result<IReadOnlyList<Block2<V>>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>>
            blocks = new BlockLoadingContext<V>().GlobalBlocks(
                request.ItemList,
                LayerGenerationSupport.InitialSpaceOf(sheet),
                kerf: request.Kerf);
        if (!blocks.IsOk) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }
        return blocks.Value
            .Select(block => new Bpp2dLayerGenerationResult<V>(
                Layer: LayerGenerationSupport.LayerOf(
                    request.Iteration, typeof(BlGlobalLayerGenerator<V>), sheet, block.Placements),
                Source: "block-global"))
            .ToList();
    }
}

/// <summary>
/// 空间切分生成器：调用 DFS 与多行启发式搜索 / Space-splitting generator: runs the DFS and multi-row searches.
/// </summary>
/// <remarks>
/// 两种搜索**各自独立成败**：一个失败或返回空不影响另一个被采纳。它们在长条板材与异形组合上的表现互补，任何一方
/// 出问题都不该把另一方的候选一起丢掉。
/// The two searches **succeed or fail independently**: one failing or returning nothing does not stop the other's result
/// from being kept. They complement each other on long sheets versus awkward mixes, and a problem in one must not discard
/// the other's candidate.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class SpaceSplitLayerGenerator<V> : IBpp2dLayerGenerator<V>
    where V : struct, IFloatingNumber<V> {
    /// <inheritdoc/>
    public IReadOnlyList<Bpp2dLayerGenerationResult<V>> Generate(
        Bpp2dLayerGenerationRequest<V> request) {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Sheet is not { } sheet) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }

        var context = new BlockLoadingContext<V>();
        Space2<V> space = LayerGenerationSupport.InitialSpaceOf(sheet);
        var results = new List<Bpp2dLayerGenerationResult<V>>();

        Result<SpaceSplit2Result<V>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> dfs =
            context.DepthFirst(space, request.ItemList, kerf: request.Kerf);
        if (dfs.IsOk && dfs.Value.Placements.Count > 0) {
            results.Add(new Bpp2dLayerGenerationResult<V>(
                Layer: LayerGenerationSupport.LayerOf(
                    request.Iteration, typeof(SpaceSplitLayerGenerator<V>), sheet, dfs.Value.Placements),
                Source: "space-split-dfs"));
        }

        Result<SpaceSplit2Result<V>, Utils.Error.ErrorCode, Utils.Error.Error<Utils.Error.ErrorCode>> multiRow =
            context.MultiRow(space, request.ItemList, kerf: request.Kerf);
        if (multiRow.IsOk && multiRow.Value.Placements.Count > 0) {
            results.Add(new Bpp2dLayerGenerationResult<V>(
                Layer: LayerGenerationSupport.LayerOf(
                    request.Iteration, typeof(SpaceSplitLayerGenerator<V>), sheet, multiRow.Value.Placements),
                Source: "space-split-multi-row"));
        }
        return results;
    }
}

/// <summary>
/// 组合式层生成上下文 / Composite layer generation context.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Generators">生成器列表 / Generator list.</param>
public sealed record LayerGenerationContext<V>(
    IReadOnlyList<IBpp2dLayerGenerator<V>>? Generators = null) : IBpp2dLayerGenerator<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>生成器列表，永不为 null / Generators, never null.</summary>
    public IReadOnlyList<IBpp2dLayerGenerator<V>> GeneratorList =>
        Generators ?? DefaultGenerators();

    /// <summary>
    /// 默认生成器集合 / Default generator set.
    /// </summary>
    /// <remarks>
    /// 顺序与基线一致（八个）。**顺序即优先级，只在 `maxCandidates` 截断时可见**：先生成的候选先占额度，因此块装载族
    /// 放在 BLA 之前、历史层放在最后——历史层只是回放，不该挤掉新生成的候选。
    /// The order matches the baseline (eight). **The order is priority, visible only when `maxCandidates` truncates**:
    /// earlier generators claim the budget first, so the block-loading family precedes BLA and the historical generator
    /// goes last — it merely replays and should not crowd out freshly generated candidates.
    /// </remarks>
    /// <returns>生成器列表 / Generator list.</returns>
    public static IReadOnlyList<IBpp2dLayerGenerator<V>> DefaultGenerators() => new IBpp2dLayerGenerator<V>[] {
        new BlLocalLayerGenerator<V>(),
        new BlGlobalLayerGenerator<V>(),
        new SpaceSplitLayerGenerator<V>(),
        new BlaLayerGenerator<V>(),
        new PatternLayerGenerator<V>(),
        new CircleGridLayerGenerator<V>(),
        new MixedShapeLayerGenerator<V>(),
        new HistoricalLayerGenerator<V>(),
    };

    /// <inheritdoc/>
    public IReadOnlyList<Bpp2dLayerGenerationResult<V>> Generate(
        Bpp2dLayerGenerationRequest<V> request) {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<IBpp2dLayerGenerator<V>> generators = GeneratorList;
        if (generators.Count == 0) {
            return Array.Empty<Bpp2dLayerGenerationResult<V>>();
        }

        // 多板材类型：对每种板材分别生成候选列，列自带 `Layer.Sheet`，目标函数按类型加权。未指定任何板材类型时保持
        // 单次直通，使历史层等不依赖板材的生成器仍可工作。
        // Multiple sheet types: candidates are generated per sheet type, each column carrying `Layer.Sheet`, and the
        // objective weights by type. With no sheet type the request passes through once so sheet-independent
        // generators such as the historical one keep working.
        IReadOnlyList<Bpp2dLayerGenerationRequest<V>> sheetRequests =
            request.Participants.Count == 0
                ? new[] { request }
                : request.Participants
                    .Select(sheet => request with { Sheet = sheet })
                    .ToList();

        IReadOnlyList<IBpp2dLayerGenerator<V>> selected = SelectGenerators(generators, request);

        var all = new List<Bpp2dLayerGenerationResult<V>>();
        foreach (Bpp2dLayerGenerationRequest<V> sheetRequest in sheetRequests) {
            if (all.Count >= request.MaxCandidates) {
                break;
            }
            foreach (IBpp2dLayerGenerator<V> generator in selected) {
                all.AddRange(generator.Generate(sheetRequest));
                if (all.Count >= request.MaxCandidates) {
                    break;
                }
            }
        }

        return RankByScore(
            request,
            DistinctByLayer(all).Take(request.MaxCandidates).ToList());
    }

    /// <summary>
    /// 按请求的形状构成挑选生成器 / Select generators by the request's shape composition.
    /// </summary>
    /// <param name="generators">候选生成器 / Candidate generators.</param>
    /// <returns>选中的生成器 / Selected generators.</returns>
    private static IReadOnlyList<IBpp2dLayerGenerator<V>> SelectGenerators(
        IReadOnlyList<IBpp2dLayerGenerator<V>> generators) => generators.ToList();

    /// <summary>
    /// 按请求的形状构成挑选生成器 / Select generators by the request's shape composition.
    /// </summary>
    /// <remarks>
    /// 三条规则：**纯圆形件**只留圆网格与历史生成器；**纯矩形件**排除圆网格与混合生成器；**混合**全留。
    /// 纯矩形请求仍会跑混合生成器以外的一切，跑不了的生成器自行返回空列表——这比在此处逐一预判更稳，因为
    /// 生成器自己最清楚能否处理该请求。
    /// Three rules: **pure circles** keep only the circle grid and the historical generator; **pure rectangles** exclude
    /// the circle grid and the mixed generator; **mixed** keeps everything. Rectangle requests still pass through every
    /// non-excluded generator and the inapplicable ones return empty lists themselves — safer than predicting here, since
    /// a generator knows best whether it can handle a request.
    /// </remarks>
    /// <param name="generators">候选生成器 / Candidate generators.</param>
    /// <param name="request">层生成请求 / Layer generation request.</param>
    /// <returns>选中的生成器 / Selected generators.</returns>
    private static IReadOnlyList<IBpp2dLayerGenerator<V>> SelectGenerators(
        IReadOnlyList<IBpp2dLayerGenerator<V>> generators,
        Bpp2dLayerGenerationRequest<V> request) {
        if (request.HasCircleItems() && !request.HasRectangleItems()) {
            return generators
                .Where(generator => generator is CircleGridLayerGenerator<V>
                    or HistoricalLayerGenerator<V>)
                .ToList();
        }
        if (request.HasCircleItems()) {
            return generators.ToList();
        }
        return generators
            .Where(generator => generator is not CircleGridLayerGenerator<V>
                and not MixedShapeLayerGenerator<V>)
            .ToList();
    }

    /// <summary>
    /// 按层去重，保持首次出现顺序 / Deduplicate by layer, keeping first-seen order.
    /// </summary>
    /// <param name="results">候选结果 / Candidate results.</param>
    /// <returns>去重后的结果 / Deduplicated results.</returns>
    private static List<Bpp2dLayerGenerationResult<V>> DistinctByLayer(
        IReadOnlyList<Bpp2dLayerGenerationResult<V>> results) {
        var seen = new HashSet<Layer<V>>();
        var kept = new List<Bpp2dLayerGenerationResult<V>>(results.Count);
        foreach (Bpp2dLayerGenerationResult<V> result in results) {
            if (seen.Add(result.Layer)) {
                kept.Add(result);
            }
        }
        return kept;
    }

    /// <summary>
    /// 按影子价格评分排序候选层 / Rank candidate layers by the shadow-price score.
    /// </summary>
    /// <param name="request">层生成请求 / Layer generation request.</param>
    /// <param name="results">候选结果 / Candidate results.</param>
    /// <returns>排序后的候选结果 / Ranked candidate results.</returns>
    private static IReadOnlyList<Bpp2dLayerGenerationResult<V>> RankByScore(
        Bpp2dLayerGenerationRequest<V> request,
        IReadOnlyList<Bpp2dLayerGenerationResult<V>> results) {
        if (request.ScoreByShadowPrice is not { } scorer) {
            return results.OrderByDescending(result => result.Layer.Area.Value).ToList();
        }
        return results
            .Select(result => result with { Score = scorer(result.Layer, request) })
            .OrderByDescending(result => result.Score ?? result.Layer.Area.Value)
            .ToList();
    }
}
