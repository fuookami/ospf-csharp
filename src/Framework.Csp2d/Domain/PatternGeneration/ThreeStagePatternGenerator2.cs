// 三级断头台图案生成器 / The three-stage guillotine pattern generator.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.PatternGeneration;

/// <summary>
/// 三级断头台图案生成器 / The three-stage guillotine pattern generator.
/// </summary>
/// <remarks>
/// 与两级生成器的差别在于**允许行内混高**：混高更省料，但每列里还需第 3 刀把件与上方余料分开。
/// 因此刀序是「横切分行 → 竖切分列 → 列内横切成件」，恰好三级，与剪板机「切条、切块、切件」一致。
///
/// The difference from the two-stage generator is that **mixed row heights are allowed**: mixed heights waste less, but each column needs a third cut to separate the
/// piece from the waste above it. The cut sequence is "rows horizontally, columns vertically, pieces horizontally inside a column", exactly three stages.
/// </remarks>
public sealed class ThreeStagePatternGenerator2 : ShelfPatternGenerator2 {
    /// <summary>构造三级断头台生成器 / Build the three-stage generator.</summary>
    /// <param name="source">生成器名称 / The generator name.</param>
    public ThreeStagePatternGenerator2(string source = "three-stage")
        : base(sameHeightRows: false, source: source) { }

    /// <inheritdoc />
    public override Result<IReadOnlyList<PatternGenerationResult2>, ErrorCode, Error<ErrorCode>> Generate(
        PatternGenerationRequest2 request) =>
        // 工艺由生成器决定而不是由调用方传入：三级生成器**只产出三级图案**，
        // 跟着请求里的工艺走会产出一棵与 `Process` 不符的树。
        // The process is decided by the generator rather than by the caller: the three-stage generator **only emits
        // three-stage patterns**, and following the request's process would emit a tree that contradicts `Process`.
        base.Generate(request with { Process = CuttingProcess2.GuillotineThreeStage });

    /// <inheritdoc />
    internal override Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> BuildCutTree(
        Region2<Flt64> region,
        IReadOnlyList<Row> rows,
        Quantity<Flt64> kerf) =>
        BuildRowsTree3(region, rows, 0, kerf);

    /// <summary>递归构造行层切分（第 1 级，横切）/ Recursively build the row-level splits (stage 1, horizontal).</summary>
    /// <param name="region">当前区域 / The current region.</param>
    /// <param name="rows">全部行 / All rows.</param>
    /// <param name="index">当前行下标 / The current row index.</param>
    /// <param name="kerf">刀缝 / The kerf.</param>
    /// <returns>子树 / The subtree.</returns>
    private Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> BuildRowsTree3(
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
            return BuildRowColumnsTree(region, row.Pieces, 2, kerf);
        }
        Result<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode, Error<ErrorCode>> cut =
            region.Cut(CutOrientation2.Horizontal, cutPosition, kerf);
        if (cut is not Ok<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode, Error<ErrorCode>> cutOk) {
            return ReShape<CutTree2<Flt64>, (Region2<Flt64> Left, Region2<Flt64> Right)>(cut);
        }
        Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> rowTree =
            BuildRowColumnsTree(cutOk.Value.Left, row.Pieces, 2, kerf);
        if (rowTree is not Ok<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> rowTreeOk) {
            return rowTree;
        }
        Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> restTree =
            BuildRowsTree3(cutOk.Value.Right, rows, index + 1, kerf);
        if (restTree is not Ok<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> restTreeOk) {
            return restTree;
        }
        return ReShape<CutTree2<Flt64>, CutTree2<Flt64>.Split2>(CutTree2<Flt64>.Split2Of(
            region, CutOrientation2.Horizontal, cutPosition, 1, rowTreeOk.Value, restTreeOk.Value, kerf));
    }

    /// <summary>构造行内列切分（第 2 级，竖切）/ Build the column splits inside a row (stage 2, vertical).</summary>
    /// <param name="region">行区域 / The row region.</param>
    /// <param name="pieces">行内放置 / The placements inside the row.</param>
    /// <param name="stage">当前级数 / The current stage.</param>
    /// <param name="kerf">刀缝 / The kerf.</param>
    /// <returns>子树 / The subtree.</returns>
    private Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> BuildRowColumnsTree(
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
            // 最后一列占满剩余宽度：直接进入第 3 级。 / The last column fills the width; go straight to stage 3.
            return BuildColumnTree(region, piece, stage + 1, kerf);
        }
        Result<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode, Error<ErrorCode>> cut =
            region.Cut(CutOrientation2.Vertical, cutPosition, kerf);
        if (cut is not Ok<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode, Error<ErrorCode>> cutOk) {
            return ReShape<CutTree2<Flt64>, (Region2<Flt64> Left, Region2<Flt64> Right)>(cut);
        }
        Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> columnTree =
            BuildColumnTree(cutOk.Value.Left, piece, stage + 1, kerf);
        if (columnTree is not Ok<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> columnTreeOk) {
            return columnTree;
        }
        var rest = new List<Placement2<Flt64>>();
        for (int index = 1; index < pieces.Count; index++) {
            rest.Add(pieces[index]);
        }
        Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> restTree =
            BuildRowColumnsTree(cutOk.Value.Right, rest, stage, kerf);
        if (restTree is not Ok<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> restTreeOk) {
            return restTree;
        }
        return ReShape<CutTree2<Flt64>, CutTree2<Flt64>.Split2>(CutTree2<Flt64>.Split2Of(
            region, CutOrientation2.Vertical, cutPosition, stage, columnTreeOk.Value, restTreeOk.Value, kerf));
    }

    /// <summary>构造列内切分（第 3 级，横切分离件与上方余料）/ Build the split inside a column (stage 3, separating the piece from the waste above it).</summary>
    /// <param name="region">列区域 / The column region.</param>
    /// <param name="piece">该列内的件 / The piece inside the column.</param>
    /// <param name="stage">当前级数 / The current stage.</param>
    /// <param name="kerf">刀缝 / The kerf.</param>
    /// <returns>子树 / The subtree.</returns>
    private Result<CutTree2<Flt64>, ErrorCode, Error<ErrorCode>> BuildColumnTree(
        Region2<Flt64> region,
        Placement2<Flt64> piece,
        int stage,
        Quantity<Flt64> kerf) {
        Quantity<Flt64> cutPosition = Add(piece.Y, piece.Height) ?? piece.Y;
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> maxY = region.MaxY();
        if (maxY is Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> maxYOk
            && maxYOk.Value.Value.ToDouble() <= cutPosition.Value.ToDouble()) {
            return Results.Ok<CutTree2<Flt64>>(new CutTree2<Flt64>.Leaf2(region, stage, piece.ItemId));
        }
        Result<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode, Error<ErrorCode>> cut =
            region.Cut(CutOrientation2.Horizontal, cutPosition, kerf);
        if (cut is not Ok<(Region2<Flt64> Left, Region2<Flt64> Right), ErrorCode, Error<ErrorCode>> cutOk) {
            return ReShape<CutTree2<Flt64>, (Region2<Flt64> Left, Region2<Flt64> Right)>(cut);
        }
        return ReShape<CutTree2<Flt64>, CutTree2<Flt64>.Split2>(CutTree2<Flt64>.Split2Of(
            region, CutOrientation2.Horizontal, cutPosition, stage,
            new CutTree2<Flt64>.Leaf2(cutOk.Value.Left, stage, piece.ItemId),
            new CutTree2<Flt64>.Leaf2(cutOk.Value.Right, stage),
            kerf));
    }
}
