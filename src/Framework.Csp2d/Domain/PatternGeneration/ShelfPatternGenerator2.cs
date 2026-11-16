// 货架式图案生成器 / The shelf pattern generator.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.PatternGeneration;

/// <summary>
/// 货架式图案生成器 / The shelf pattern generator.
/// </summary>
/// <remarks>
/// 逐行铺满宽度：一件定下该行高度，再往这一行里塞更多件。**行内是否允许混高决定了工艺**：
/// 1. **行内等高**：每件都撑满行高，于是"横切分行、竖切分件"恰好是**两级断头台**，可以输出切分树。
/// 2. **行内混高**：混高更省料，但每列还需多一刀把件与上方余料分开，所以只能用于**任意排样**（无刀序）或三级断头台生成器。
///
/// **这正是"随便排排"与"按工艺排样"的分界线**：工艺约束改变的是**合法排样的族**，不是一个开关。
///
/// Fill the width row by row: one piece fixes the row height, then more pieces are added to that row. **Whether mixed heights are allowed decides the process**:
/// uniform rows are exactly a **two-stage guillotine** and can emit a cut tree, while mixed rows waste less but need a third cut per column and therefore only suit
/// **arbitrary nesting** or the three-stage generator. **This is the line between "just nest something" and "nest for a process"**: the process constraint changes
/// the **family of legal layouts**, not merely a switch.
/// </remarks>
public class ShelfPatternGenerator2 : IPatternGenerator2 {
    /// <summary>构造生成器 / Build the generator.</summary>
    /// <param name="sameHeightRows">是否要求行内等高 / Whether rows must have a uniform height.</param>
    /// <param name="source">生成器名称 / The generator name.</param>
    public ShelfPatternGenerator2(bool sameHeightRows = false, string source = "shelf") {
        SameHeightRows = sameHeightRows;
        Source = source;
    }

    /// <summary>是否要求行内等高 / Whether rows must have a uniform height.</summary>
    public bool SameHeightRows { get; }

    /// <summary>生成器名称 / The generator name.</summary>
    public string Source { get; }

    /// <summary>
    /// 一行排样 / One layout row.
    /// </summary>
    /// <param name="Y">行底部 y 坐标 / The row bottom y coordinate.</param>
    /// <param name="Height">行高 / The row height.</param>
    /// <param name="Pieces">行内放置 / The placements inside the row.</param>
    public sealed record Row(Quantity<Flt64> Y, Quantity<Flt64> Height, IReadOnlyList<Placement2<Flt64>> Pieces);

    /// <inheritdoc />
    public virtual Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> Generate(
        PatternGenerationRequest2 request) {
        Result<Success, ErrorCode, Error<ErrorCode>> validated = request.Validate();
        if (validated is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return ReShape<IReadOnlyList<PatternGenerationResult2>, Success>(validated);
        }
        if (request.Process == CuttingProcess2.GuillotineTwoStage && !SameHeightRows) {
            return Results.Failed<IReadOnlyList<PatternGenerationResult2>>(new Csp2dCapabilityError(
                "行内混高的货架排样不是两级断头台 / A mixed-height shelf layout is not a two-stage guillotine"));
        }

        Material2<Flt64> material = request.Material;
        Quantity<Flt64> kerf = request.Kerf;
        Quantity<Flt64> zero = new(Flt64.Zero, material.Width.Unit);
        Quantity<Flt64> regionHeight = request.TargetRegionHeight();
        if (regionHeight.Value.ToDouble() <= 0.0) {
            return Results.Ok<IReadOnlyList<PatternGenerationResult2>>(new List<PatternGenerationResult2>());
        }

        Dictionary<string, ulong> remaining = new();
        Dictionary<string, Product2<Flt64>> productById = new();
        foreach ((Product2<Flt64> product, ulong amount) in request.Products) {
            productById[product.Id.Value] = product;
            remaining[product.Id.Value] = amount;
        }
        List<Product2<Flt64>> ordered = new(productById.Values);
        ordered.Sort((lhs, rhs) => {
            int byHeight = rhs.Height.Value.ToDouble().CompareTo(lhs.Height.Value.ToDouble());
            return byHeight != 0 ? byHeight : rhs.Width.Value.ToDouble().CompareTo(lhs.Width.Value.ToDouble());
        });

        List<Row> rows = new();
        Quantity<Flt64> cursorY = zero;
        Quantity<Flt64> usedHeight = zero;
        while (cursorY.Value.ToDouble() < regionHeight.Value.ToDouble()) {
            Row? row = BuildRow(ordered, remaining, kerf, cursorY, material.Width, regionHeight);
            if (row is null) {
                break;
            }
            rows.Add(row);
            usedHeight = Add(row.Y, row.Height) ?? row.Height;
            cursorY = Add(Add(row.Y, row.Height) ?? row.Height, kerf) ?? regionHeight;
            // `MaxCandidates` 限制的是**产出图案数**而不是行数：行仍须铺满料域，
            // 否则"最大候选数"这个名字与行为不符，列生成也拿不到足够的候选。
            // `MaxCandidates` bounds the number of **emitted patterns**, not the rows: rows must still fill the region.
        }
        if (rows.Count == 0) {
            return Results.Ok<IReadOnlyList<PatternGenerationResult2>>(new List<PatternGenerationResult2>());
        }

        // 卷材模式把料域收缩到实际消耗的段高；定尺模式保留整块板材，下方与右侧为余料。
        // Coil mode shrinks the region to the consumed segment height; sheet mode keeps the whole sheet.
        Quantity<Flt64> actualHeight = material.Height ?? usedHeight;
        Region2<Flt64> region = Region2Factory.Region2Of(zero, zero, material.Width, actualHeight);
        List<Placement2<Flt64>> placements = new();
        foreach (Row row in rows) {
            placements.AddRange(row.Pieces);
        }
        if (placements.Count == 0) {
            return Results.Ok<IReadOnlyList<PatternGenerationResult2>>(new List<PatternGenerationResult2>());
        }

        // 每个前缀行数产出一个候选图案，便于主问题按覆盖量取舍。候选过多时**保留末尾**——
        // 前缀越长覆盖越多，丢掉长的等于丢掉覆盖最大的候选。
        // Emit one candidate per prefix row count; when there are too many, **keep the tail** — a longer prefix covers
        // more, so dropping it would drop the most covering candidate.
        int limit = request.MaxCandidates < 1 ? 1 : request.MaxCandidates;
        int firstPrefix = rows.Count - limit < 0 ? 0 : rows.Count - limit;
        List<PatternGenerationResult2> results = new();
        for (int prefix = firstPrefix + 1; prefix <= rows.Count; prefix++) {
            List<Row> selected = rows.GetRange(0, prefix);
            Quantity<Flt64> prefixHeight = Add(selected[^1].Y, selected[^1].Height) ?? usedHeight;
            Region2<Flt64> prefixRegion = material.Height is not null
                ? region
                : Region2Factory.Region2Of(zero, zero, material.Width, prefixHeight);

            List<Placement2<Flt64>> prefixPlacements = new();
            foreach (Row row in selected) {
                prefixPlacements.AddRange(row.Pieces);
            }

            CutTree2<Flt64>? prefixTree = null;
            if (request.Process.RequiresCutTree()) {
                Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> tree =
                    BuildCutTree(prefixRegion, selected, kerf);
                if (tree is not Ok<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> treeOk) {
                    return ReShape<IReadOnlyList<PatternGenerationResult2>, CutTree2<Flt64>>(tree);
                }
                prefixTree = treeOk.Value;
            }

            var prefixPattern = new Pattern2<Flt64>(
                Csp2dIds.PatternId2Of(Source + "-" + request.Iteration + "-" + prefix),
                material,
                prefixRegion,
                prefixPlacements,
                null,
                prefixTree,
                request.Process);
            Result<Success, ErrorCode, Error<ErrorCode>> checked_ = prefixPattern.Validate(kerf);
            if (checked_ is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return ReShape<IReadOnlyList<PatternGenerationResult2>, Success>(checked_);
            }
            results.Add(new PatternGenerationResult2(prefixPattern, Source));
        }
        return Results.Ok<IReadOnlyList<PatternGenerationResult2>>(results);
    }

    /// <summary>
    /// 构造一行 / Build one row.
    /// </summary>
    /// <param name="ordered">按高度降序的产品 / The products ordered by descending height.</param>
    /// <param name="remaining">剩余件数，就地扣减 / The remaining counts, decremented in place.</param>
    /// <param name="kerf">刀缝 / The kerf.</param>
    /// <param name="y">行底部坐标 / The row bottom coordinate.</param>
    /// <param name="regionWidth">料域宽度 / The region width.</param>
    /// <param name="regionHeight">料域高度 / The region height.</param>
    /// <returns>行，无法放置时为空 / The row, or null when nothing fits.</returns>
    /// <remarks>
    /// 判定必须按**行距 = 行高 + 刀缝**而非裸行高：一行占用它自己的刀缝，只用行高比较会把最后一行挤出料域
    /// （料域只装得下 6 件时却产出 9 件）。
    ///
    /// The check must use the **row pitch (height plus kerf)** rather than the bare height: a row occupies its own kerf, and comparing the height alone pushes the last row
    /// out of the region (producing 9 pieces in a region that only holds 6).
    /// </remarks>
    internal Row? BuildRow(
        IReadOnlyList<Product2<Flt64>> ordered,
        Dictionary<string, ulong> remaining,
        Quantity<Flt64> kerf,
        Quantity<Flt64> y,
        Quantity<Flt64> regionWidth,
        Quantity<Flt64> regionHeight) {
        Product2<Flt64>? seed = null;
        foreach (Product2<Flt64> product in ordered) {
            if (Left(product, remaining) > 0) {
                seed = product;
                break;
            }
        }
        if (seed is null) {
            return null;
        }
        Quantity<Flt64> rowHeight = seed.Height;
        Quantity<Flt64>? pitch = Add(Add(y, rowHeight) ?? rowHeight, kerf);
        if (pitch is null || pitch.Value.ToDouble() > regionHeight.Value.ToDouble()) {
            return null;
        }

        List<Placement2<Flt64>> pieces = new();
        Quantity<Flt64> cursorX = new(Flt64.Zero, regionWidth.Unit);
        while (true) {
            Product2<Flt64>? candidate = null;
            foreach (Product2<Flt64> product in ordered) {
                bool heightFits = SameHeightRows
                    ? product.Height.Value.ToDouble() == rowHeight.Value.ToDouble()
                    : product.Height.Value.ToDouble() <= rowHeight.Value.ToDouble();
                Quantity<Flt64> right = Add(cursorX, product.Width) ?? cursorX;
                if (Left(product, remaining) > 0
                    && heightFits
                    && product.Height.Unit.Equals(rowHeight.Unit)
                    && right.Value.ToDouble() <= regionWidth.Value.ToDouble()) {
                    candidate = product;
                    break;
                }
            }
            if (candidate is null) {
                break;
            }
            pieces.Add(new Placement2<Flt64>(
                new RectanglePackingShape2<Flt64>(candidate.Width, candidate.Height),
                new QuantityPoint2<Flt64>(cursorX, y),
                false,
                candidate.Id.Value));
            ulong left = (ulong)Left(candidate, remaining);
            remaining[candidate.Id.Value] = left > 0UL ? left - 1UL : 0UL;
            cursorX = Add(Add(cursorX, candidate.Width) ?? cursorX, kerf) ?? cursorX;
        }
        if (pieces.Count == 0) {
            return null;
        }
        return new Row(y, rowHeight, pieces);
    }

    /// <summary>
    /// 读取剩余件数 / Read the remaining piece count.
    /// </summary>
    /// <param name="product">产品 / The product.</param>
    /// <param name="remaining">剩余件数映射 / The remaining count map.</param>
    /// <returns>件数 / The count.</returns>
    internal int Left(Product2<Flt64> product, IDictionary<string, ulong> remaining) =>
        remaining.TryGetValue(product.Id.Value, out ulong count) ? (int)count : 0;

    /// <summary>
    /// 由行构造两级断头台切分树 / Build a two-stage guillotine cut tree from the rows.
    /// </summary>
    /// <param name="region">料域 / The region.</param>
    /// <param name="rows">行列表 / The row list.</param>
    /// <param name="kerf">刀缝 / The kerf.</param>
    /// <returns>切分树 / The cut tree.</returns>
    /// <remarks>
    /// 第 1 级横切分行（最后一刀分出下方余料），第 2 级竖切分件（最后一刀分出右侧余料）。
    ///
    /// Stage 1 cuts rows horizontally (the last cut separates the remnant below); stage 2 cuts pieces vertically (the last cut separates the remnant to the right).
    /// </remarks>
    internal virtual Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> BuildCutTree(
        Region2<Flt64> region,
        IReadOnlyList<Row> rows,
        Quantity<Flt64> kerf) =>
        BuildRowsTree(region, rows, 0, kerf);

    /// <summary>递归构造行层切分 / Recursively build the row-level splits.</summary>
    /// <param name="region">当前区域 / The current region.</param>
    /// <param name="rows">全部行 / All rows.</param>
    /// <param name="index">当前行下标 / The current row index.</param>
    /// <param name="kerf">刀缝 / The kerf.</param>
    /// <returns>子树 / The subtree.</returns>
    private Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> BuildRowsTree(
        Region2<Flt64> region,
        IReadOnlyList<Row> rows,
        int index,
        Quantity<Flt64> kerf) {
        if (index >= rows.Count) {
            return Results.Ok<CutTree2<Flt64>>(new CutTree2<Flt64>.Leaf2(region, 1));
        }
        Row row = rows[index];
        Quantity<Flt64> cutPosition = Add(row.Y, row.Height) ?? row.Y;
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> maxY = region.MaxY();
        if (maxY is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> maxYOk
            && maxYOk.Value.Value.ToDouble() <= cutPosition.Value.ToDouble()) {
            return BuildRowPiecesTree(region, row.Pieces, 2, kerf);
        }
        Result<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode, Error<ErrorCode>> cut =
            region.Cut(CutOrientation2.Horizontal, cutPosition, kerf);
        if (cut is not Ok<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode, Error<ErrorCode>> cutOk) {
            return ReShape<CutTree2<Flt64>, (Region2<Flt64> Left, Region2<Flt64> Right)>(cut);
        }
        Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> rowTree =
            BuildRowPiecesTree(cutOk.Value.Left, row.Pieces, 2, kerf);
        if (rowTree is not Ok<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> rowTreeOk) {
            return rowTree;
        }
        Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> restTree =
            BuildRowsTree(cutOk.Value.Right, rows, index + 1, kerf);
        if (restTree is not Ok<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> restTreeOk) {
            return restTree;
        }
        return ReShape<CutTree2<Flt64>, CutTree2<Flt64>.Split2>(CutTree2<Flt64>.Split2Of(
            region, CutOrientation2.Horizontal, cutPosition, 1, rowTreeOk.Value, restTreeOk.Value, kerf));
    }

    /// <summary>
    /// 构造行内切分：竖切成件，最后一刀分出右侧余料 / Build the splits inside a row.
    /// </summary>
    /// <param name="region">行区域 / The row region.</param>
    /// <param name="pieces">行内放置 / The placements inside the row.</param>
    /// <param name="stage">当前级数 / The current stage.</param>
    /// <param name="kerf">刀缝 / The kerf.</param>
    /// <returns>子树 / The subtree.</returns>
    private Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> BuildRowPiecesTree(
        Region2<Flt64> region,
        IReadOnlyList<Placement2<Flt64>> pieces,
        int stage,
        Quantity<Flt64> kerf) {
        if (pieces.Count == 0) {
            return Results.Ok<CutTree2<Flt64>>(new CutTree2<Flt64>.Leaf2(region, stage));
        }
        Placement2<Flt64> piece = pieces[0];
        Quantity<Flt64> cutPosition = Add(piece.X, piece.Width) ?? piece.X;
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> maxX = region.MaxX();
        if (maxX is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> maxXOk
            && maxXOk.Value.Value.ToDouble() <= cutPosition.Value.ToDouble()) {
            // 该件占满剩余宽度。 / The piece fills the remaining width.
            return Results.Ok<CutTree2<Flt64>>(
                new CutTree2<Flt64>.Leaf2(region, stage, piece.ItemId));
        }
        Result<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode, Error<ErrorCode>> cut =
            region.Cut(CutOrientation2.Vertical, cutPosition, kerf);
        if (cut is not Ok<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode, Error<ErrorCode>> cutOk) {
            return ReShape<CutTree2<Flt64>, (Region2<Flt64> Left, Region2<Flt64> Right)>(cut);
        }
        List<Placement2<Flt64>> rest = new();
        for (int index = 1; index < pieces.Count; index++) {
            rest.Add(pieces[index]);
        }
        Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> restTree =
            BuildRowPiecesTree(cutOk.Value.Right, rest, stage, kerf);
        if (restTree is not Ok<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> restTreeOk) {
            return restTree;
        }
        return ReShape<CutTree2<Flt64>, CutTree2<Flt64>.Split2>(CutTree2<Flt64>.Split2Of(
            region, CutOrientation2.Vertical, cutPosition, stage,
            new CutTree2<Flt64>.Leaf2(cutOk.Value.Left, stage, piece.ItemId),
            restTreeOk.Value,
            kerf));
    }

    /// <summary>两个长度相加，单位不一致时返回空 / Add two lengths, returning null on a unit mismatch.</summary>
    /// <param name="lhs">左 / The left operand.</param>
    /// <param name="rhs">右 / The right operand.</param>
    /// <returns>和或空 / The sum or null.</returns>
    /// <remarks>
    /// **单位不一致按"放不下"处理**：排样里的长度本该同单位，混进异单位说明输入本身有问题，
    /// 而**在这里静默给出一个和才是真正危险的**——那会产出一个几何上无意义的图案。
    ///
    /// **A unit mismatch counts as "does not fit"**: the lengths in a nesting run share a unit, so a foreign unit means the input itself is wrong, and **silently producing
    /// a sum here would be the dangerous choice** — it would emit a geometrically meaningless pattern.
    /// </remarks>
    protected static Quantity<Flt64>? Add(Quantity<Flt64> lhs, Quantity<Flt64> rhs) =>
        lhs.Add(rhs) is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> ok ? ok.Value : null;

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    protected static Result<S, ErrorCode, Error<ErrorCode>> ReShape<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal => new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            Ok<TSource, ErrorCode, Error<ErrorCode>> ok => Results.Ok((S)(object)ok.Value),
            _ => throw new System.InvalidOperationException("unreachable result case"),
        };
}
