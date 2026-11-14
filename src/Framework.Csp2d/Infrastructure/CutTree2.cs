// 断头台切分树 / Guillotine cut tree.
// 与固定 Kotlin 基线 `csp2d/infrastructure/CutTree2.kt` 对应（`CutOrientation2` 已在 `Region2.cs` 中定义）。
// Mirrors the pinned Kotlin baseline's `CutTree2.kt` (`CutOrientation2` already lives in `Region2.cs`).

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Infrastructure;

/// <summary>
/// 一条贯穿刀线 / A single through cut.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Orientation">刀线方向 / The cut line orientation.</param>
/// <param name="Position">刀缝起始坐标（竖刀为 x，横刀为 y） / The coordinate where the kerf strip starts (x for a vertical cut, y for a horizontal one).</param>
/// <param name="Stage">切割层号：1 为切条、2 为切块、3 为切件 / The cut level: 1 strips, 2 blocks, 3 pieces.</param>
public sealed record CutLine2<V>(CutOrientation2 Orientation, Quantity<V> Position, int Stage = 1)
    where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 断头台切分树 / Guillotine cut tree.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// 二维套裁必须支持两种工艺：**任意排样**（只给坐标，设备按轮廓切）与**断头台**（每一刀必须贯穿当前料块、
/// 把它分成两块，递归进行）。断头台模式下排样**必须由切分树产生**，而这棵树本身就是交给车间的下刀顺序，
/// 所以它要被显式建模，而不是只存坐标。
///
/// **树与 `Region2.Cut` 必须一致**，否则叶子区域不是真正能由贯穿刀得到的——`Split2Of` 就是为此存在的推荐构造方式。
///
/// 2D cutting stock must support two processes: **arbitrary nesting** (coordinates only, cut along contours) and **guillotine** (every cut runs through the current
/// piece and splits it in two, recursively). In guillotine mode a layout **must be produced by a cut tree**, and that tree is itself the cut sequence handed to the
/// workshop, so it is modelled explicitly rather than storing coordinates only. **The tree must stay consistent with `Region2.Cut`**, or the leaf regions are not
/// really obtainable by through cuts — which is exactly what `Split2Of` exists to guarantee.
/// </remarks>
public abstract record CutTree2<V>
    where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V> {
    /// <summary>本结点对应的区域 / The region of this node.</summary>
    public abstract Region2<V> Region { get; }

    /// <summary>本结点的工序级数 / The process stage of this node.</summary>
    public abstract int Stage { get; }

    /// <summary>收集全部叶子 / Collect every leaf.</summary>
    /// <returns>叶子列表 / The leaf list.</returns>
    public abstract IReadOnlyList<Leaf2> Leaves();

    /// <summary>收集全部刀线，按深度优先顺序，即下刀顺序 / Collect every cut line in depth-first order, i.e. the cut sequence.</summary>
    /// <returns>刀线列表 / The cut line list.</returns>
    public abstract IReadOnlyList<CutLine2<V>> CutLines();

    /// <summary>本子树的最大工序级数 / The maximum process stage inside this subtree.</summary>
    /// <returns>最大级数 / The maximum stage.</returns>
    public abstract int MaxStage();

    /// <summary>校验结构与本工艺的一致性 / Validate the structure against the given process.</summary>
    /// <param name="process">裁切工艺 / The cutting process.</param>
    /// <param name="kerf">刀缝宽度 / The kerf width.</param>
    /// <returns>校验结果 / The validation result.</returns>
    public abstract Result<Success, ErrorCode, Error<ErrorCode>> Validate(CuttingProcess2 process, Quantity<V> kerf);

    /// <summary>
    /// 叶子：一块最终的料，可对应一个成品件，也可为余料 / A leaf: one final piece of material, either a finished part or a remnant.
    /// </summary>
    /// <param name="Region">区域 / The region.</param>
    /// <param name="Stage">切割层号 / The cut level.</param>
    /// <param name="PieceId">成品件标识，为空表示为余料 / The finished part identifier; null means a remnant.</param>
    /// <param name="Rotated">该件是否旋转 90° / Whether the piece is rotated 90 degrees.</param>
    public sealed record Leaf2(
        Region2<V> Region,
        int Stage = 1,
        string? PieceId = null,
        bool Rotated = false) : CutTree2<V> {
        /// <inheritdoc />
        public override Region2<V> Region { get; } = Region;

        /// <inheritdoc />
        public override int Stage { get; } = Stage;

        /// <inheritdoc />
        public override IReadOnlyList<Leaf2> Leaves() => new List<Leaf2> { this };

        /// <inheritdoc />
        public override IReadOnlyList<CutLine2<V>> CutLines() => new List<CutLine2<V>>();

        /// <inheritdoc />
        public override int MaxStage() => Stage;

        /// <inheritdoc />
        public override Result<Success, ErrorCode, Error<ErrorCode>> Validate(
            CuttingProcess2 process,
            Quantity<V> kerf) {
            if (Region.IsDegenerate()) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "叶子区域退化 / Degenerate leaf region: " + Region));
            }
            if (Stage < 1) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "叶子工序级数非法 / Invalid leaf stage: " + Stage));
            }
            if (!process.AllowsStage(Stage)) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "叶子工序级数 " + Stage + " 超出工艺上限 / Leaf stage exceeds the process limit: " + process));
            }
            return Results.OkInstance;
        }
    }

    /// <summary>
    /// 内部结点：一刀与它切出的两个子树 / An internal node: one cut together with the two subtrees it produces.
    /// </summary>
    /// <param name="Region">被切的区域 / The region being cut.</param>
    /// <param name="Line">刀线 / The cut line.</param>
    /// <param name="First">左/下侧子树 / The left or bottom subtree.</param>
    /// <param name="Second">右/上侧子树 / The right or top subtree.</param>
    public sealed record Split2(
        Region2<V> Region,
        CutLine2<V> Line,
        CutTree2<V> First,
        CutTree2<V> Second) : CutTree2<V> {
        /// <inheritdoc />
        public override Region2<V> Region { get; } = Region;

        /// <inheritdoc />
        public override int Stage => Line.Stage;

        /// <inheritdoc />
        public override IReadOnlyList<Leaf2> Leaves() {
            var leaves = new List<Leaf2>();
            leaves.AddRange(First.Leaves());
            leaves.AddRange(Second.Leaves());
            return leaves;
        }

        /// <inheritdoc />
        public override IReadOnlyList<CutLine2<V>> CutLines() {
            var lines = new List<CutLine2<V>> { Line };
            lines.AddRange(First.CutLines());
            lines.AddRange(Second.CutLines());
            return lines;
        }

        /// <inheritdoc />
        public override int MaxStage() {
            int first = First.MaxStage();
            int second = Second.MaxStage();
            return Line.Stage > first
                ? (Line.Stage > second ? Line.Stage : second)
                : (first > second ? first : second);
        }

        /// <inheritdoc />
        public override Result<Success, ErrorCode, Error<ErrorCode>> Validate(
            CuttingProcess2 process,
            Quantity<V> kerf) {
            if (Region.IsDegenerate()) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "结点区域退化 / Degenerate split region: " + Region));
            }
            if (!process.AllowsStage(Line.Stage)) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "刀线级数 " + Line.Stage + " 超出工艺上限 / Cut stage exceeds the process limit: " + process));
            }

            // k 级断头台的定义：级数是「切割层号」，根刀线为第 1 级，其下恰为第 2 级，依此类推。
            // 级数与刀线方向联动：**同向刀属于同一级**（同一批平行刀），换向才进入下一级。
            // 一律 +1 会让"把整板切成多条同向条带"无法表达——而那正是 2 级工艺的第一步。
            // Definition of a k-stage guillotine: the stage is the cut level, the root cut is level 1, cuts below it are level 2, and so on.
            // The stage is coupled to the cut orientation: **same-direction cuts stay on the same stage** (one family of parallel cuts), and only a direction change
            // advances the stage. Always adding one would make "cut the sheet into several parallel strips" inexpressible — and that is the first step of a two-stage
            // process.
            foreach ((string name, CutTree2<V> child) in NamedChildren()) {
                if (child is not Split2 split) {
                    continue;
                }
                int expected = split.Line.Orientation == Line.Orientation ? Line.Stage : Line.Stage + 1;
                if (split.Line.Stage != expected) {
                    return Results.Failed<Success>(new Csp2dValidationError(
                        name + " 子刀线级数应为 " + expected + "，实际 " + split.Line.Stage
                        + " / A child cut must use the expected stage for its orientation"));
                }
            }

            Quantity<V> start = StartOf(Region, Line.Orientation);
            if (Line.Position.LessThanOrEqual(start) is not false) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "刀线不在区域内部 / The cut line lies outside the region: " + Line));
            }

            Result<(Region2<V> Left, Region2<V> Right), ErrorCode, Error<ErrorCode>> cut =
                Region.Cut(Line.Orientation, Line.Position, kerf);
            if (cut is not Ok<(Region2<V> Left, Region2<V> Right), ErrorCode, Error<ErrorCode>> cutOk) {
                return Propagate<Success, (Region2<V> Left, Region2<V> Right)>(cut);
            }

            if (!SameRegion(cutOk.Value.Left, First.Region)) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "左/下侧子区域与切分结果不一致 / First child region does not match the split result"));
            }
            if (!SameRegion(cutOk.Value.Right, Second.Region)) {
                return Results.Failed<Success>(new Csp2dValidationError(
                    "右/上侧子区域与切分结果不一致 / Second child region does not match the split result"));
            }

            foreach ((string _, CutTree2<V> child) in NamedChildren()) {
                Result<Success, ErrorCode, Error<ErrorCode>> result = child.Validate(process, kerf);
                if (result is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                    return result;
                }
            }
            return Results.OkInstance;
        }

        /// <summary>带名字的两个子结点，用于失败文案 / The two named children, for failure messages.</summary>
        /// <returns>子结点对 / The child pairs.</returns>
        private IEnumerable<(string Name, CutTree2<V> Child)> NamedChildren() {
            yield return ("左/下侧 / first", First);
            yield return ("右/上侧 / second", Second);
        }
    }

    /// <summary>
    /// 由刀线构造内部结点，子区域按刀缝自动推导 / Build an internal node from a cut line, deriving the child regions from the kerf automatically.
    /// </summary>
    /// <param name="region">被切的区域 / The region being cut.</param>
    /// <param name="orientation">刀线方向 / The cut line orientation.</param>
    /// <param name="position">刀缝起始坐标 / The coordinate where the kerf strip starts.</param>
    /// <param name="stage">工序级数 / The process stage.</param>
    /// <param name="first">左/下侧子树 / The left or bottom subtree.</param>
    /// <param name="second">右/上侧子树 / The right or top subtree.</param>
    /// <param name="kerf">刀缝宽度，默认零 / The kerf width, defaulting to zero.</param>
    /// <returns>内部结点或失败 / The internal node, or a failure.</returns>
    /// <remarks>
    /// **这是推荐的构造方式**：它保证树与 `Region2.Cut` 一致，避免手搓子区域时与刀线矛盾。
    /// 手搓很容易写出"刀线在 2.0、子区域却从 1.5 开始"的树，而那棵树**校验能过、车间切不出来**。
    ///
    /// **This is the recommended construction**: it guarantees consistency with `Region2.Cut` and avoids hand-built mismatches between the cut line and the child
    /// regions. Hand-building easily produces a tree whose cut sits at 2.0 while the child region starts at 1.5 — a tree that **passes validation and cannot be cut in
    /// the workshop**.
    /// </remarks>
    public static Result<Split2, ErrorCode, Error<ErrorCode>> Split2Of(
        Region2<V> region,
        CutOrientation2 orientation,
        Quantity<V> position,
        int stage,
        CutTree2<V> first,
        CutTree2<V> second,
        Quantity<V>? kerf = null) {
        Quantity<V> actualKerf = kerf ?? new Quantity<V>(region.Width.Value.Constants.Zero, region.Width.Unit);

        Result<(Region2<V> Left, Region2<V> Right), ErrorCode, Error<ErrorCode>> cut =
            region.Cut(orientation, position, actualKerf);
        if (cut is not Ok<(Region2<V> Left, Region2<V> Right), ErrorCode, Error<ErrorCode>> cutOk) {
            return Propagate<Split2, (Region2<V> Left, Region2<V> Right)>(cut);
        }
        if (!SameRegion(cutOk.Value.Left, first.Region)) {
            return Results.Failed<Split2>(new Csp2dValidationError(
                "左/下侧子区域与刀线矛盾 / First child region contradicts the cut line"));
        }
        if (!SameRegion(cutOk.Value.Right, second.Region)) {
            return Results.Failed<Split2>(new Csp2dValidationError(
                "右/上侧子区域与刀线矛盾 / Second child region contradicts the cut line"));
        }
        return Results.Ok(new Split2(region, new CutLine2<V>(orientation, position, stage), first, second));
    }

    /// <summary>
    /// 读取区域在指定方向上的起始坐标 / Read the starting coordinate of a region along the given orientation.
    /// </summary>
    /// <param name="region">区域 / The region.</param>
    /// <param name="orientation">刀线方向 / The cut line orientation.</param>
    /// <returns>起始坐标 / The starting coordinate.</returns>
    private static Quantity<V> StartOf(Region2<V> region, CutOrientation2 orientation) =>
        orientation == CutOrientation2.Vertical ? region.X : region.Y;

    /// <summary>
    /// 两个区域是否在数值与量纲上一致 / Whether two regions agree in value and dimension.
    /// </summary>
    /// <param name="lhs">区域 / A region.</param>
    /// <param name="rhs">区域 / The other region.</param>
    /// <returns>是否一致 / Whether they agree.</returns>
    /// <remarks>
    /// **不用 record 的结构相等**：`Region2` 的字段是 `Quantity`，而单位对象在派生运算后是**新实例**，
    /// 结构相等会按引用比单位而被判成不等。这里比**数值（相对 1e-12）与量纲**。
    ///
    /// **Deliberately not record equality**: `Region2`'s fields are `Quantity`s, and unit objects are **fresh instances** after derived operations, so structural equality
    /// compares units by reference and reports a mismatch. This compares **values (relative 1e-12) and dimensions** instead.
    /// </remarks>
    private static bool SameRegion(Region2<V> lhs, Region2<V> rhs) =>
        SameValue(lhs.X, rhs.X) && SameValue(lhs.Y, rhs.Y)
        && SameValue(lhs.Width, rhs.Width) && SameValue(lhs.Height, rhs.Height);

    /// <summary>两个量是否在数值与量纲上一致 / Whether two quantities agree in value and dimension.</summary>
    /// <param name="lhs">量 / A quantity.</param>
    /// <param name="rhs">量 / The other quantity.</param>
    /// <returns>是否一致 / Whether they agree.</returns>
    private static bool SameValue(Quantity<V> lhs, Quantity<V> rhs) {
        if (!lhs.Unit.Quantity.Equals(rhs.Unit.Quantity)) {
            return false;
        }
        double left = lhs.Value.ToFlt64().ToDouble();
        double right = rhs.Value.ToFlt64().ToDouble();
        double scale = System.Math.Max(1.0, System.Math.Max(System.Math.Abs(left), System.Math.Abs(right)));
        return System.Math.Abs(left - right) <= 1e-12 * scale;
    }

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<S, ErrorCode, Error<ErrorCode>> Propagate<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<S>(new Err<ErrorCode>(ErrorCode.ApplicationError, "unreachable")),
        };
}

/// <summary>区域与坐标的独立判断 / Standalone region and coordinate checks.</summary>
public static class CutTree2Checks {
    /// <summary>
    /// 判断坐标是否严格落在区域内部 / Check whether a coordinate lies strictly inside a region.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="region">区域 / The region.</param>
    /// <param name="orientation">刀线方向 / The cut line orientation.</param>
    /// <param name="position">坐标 / The coordinate.</param>
    /// <returns>是否在内部 / Whether it is strictly inside.</returns>
    /// <remarks>
    /// **两侧都严格**：正好落在边界上的刀线切出一块零宽的料，那不是一刀，是一次剪切报废。
    ///
    /// **Strict on both sides**: a cut sitting exactly on the boundary yields a zero-width piece — that is not a cut, it is a shearing scrap.
    /// </remarks>
    public static bool IsInside<V>(Region2<V> region, CutOrientation2 orientation, Quantity<V> position)
        where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V> {
        if (orientation == CutOrientation2.Vertical) {
            if (region.MaxX() is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> maxX) {
                return false;
            }
            return region.X.LessThan(position) is true && position.LessThan(maxX.Value) is true;
        }
        if (region.MaxY() is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> maxY) {
            return false;
        }
        return region.Y.LessThan(position) is true && position.LessThan(maxY.Value) is true;
    }

    /// <summary>
    /// 判断区域是否非退化且面积为正 / Check whether a region is non-degenerate with a positive area.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="region">区域 / The region.</param>
    /// <param name="sample">面积样本，用于提供零值单位 / An area sample providing the zero unit.</param>
    /// <returns>是否有效 / Whether it is valid.</returns>
    /// <remarks>
    /// 参数 `sample` 只提供**零值的单位**；基线保留它是因为 Kotlin 侧需要一个同量纲的零来比较。
    ///
    /// The `sample` parameter only supplies **the unit of the zero value**; the baseline keeps it because the Kotlin side needs a same-dimension zero to compare.
    /// </remarks>
    public static bool HasPositiveArea<V>(Region2<V> region, Quantity<V> sample)
        where V : struct, IFloatingNumber<V>, IRealNumber<V>, INumberField<V> {
        if (region.IsDegenerate()) {
            return false;
        }
        Quantity<V> zero = new(sample.Value.Constants.Zero, sample.Unit);
        return region.Area.GreaterThanOrEqual(zero) is true
            && region.Area.Value.ToFlt64().ToDouble() > 0.0;
    }
}
