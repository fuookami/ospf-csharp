// 底部向上左对齐算法 / Bottom-up left-justified algorithm.
// 收敛为二维：在板材平面上
// 依次放置物品，每次都选择最低且最靠左的可行点。
// Items are placed on the sheet plane, always choosing the lowest then leftmost
// feasible point.
//
// 输入为 `IAbstractItem`；圆形件不参与 BLA（圆形没有"向左下推挤"的语义），会被**显式跳过**并记入
// `SkippedCircleItems`。earlier 之前 C# 侧只有矩形物料，跳过列表恒为空；现在物料模型已能表达圆形件，
// 跳过语义生效。
// Input is `IAbstractItem`. Circle items do not participate in BLA (circles have no "push to bottom-left" semantics);
// they are **explicitly skipped** and recorded in `SkippedCircleItems`. Before earlier C# only had rectangle items
// and the skip list was always empty; the item model can now express circles, so the skip semantics apply.

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
/// BLA 排样结果 / BLA layout result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Placements">放置列表 / Placement list.</param>
/// <param name="SkippedCircleItems">被跳过的圆形件编号 / Identifiers of skipped circle items.</param>
/// <param name="SkippedUnfittedItems">无法放置的物品编号 / Identifiers of items that could not be placed.</param>
public sealed record Bla2dLayoutResult<V>(
    IReadOnlyList<Placement2<V>> Placements,
    IReadOnlyList<string> SkippedCircleItems,
    IReadOnlyList<string> SkippedUnfittedItems)
    where V : struct, IFloatingNumber<V> {
    /// <summary>是否全部放置成功 / Whether everything was placed successfully.</summary>
    public bool FullyPlaced =>
        SkippedUnfittedItems.Count == 0 && SkippedCircleItems.Count == 0;
}

/// <summary>
/// 底部向上左对齐算法 / Bottom-up left-justified algorithm.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class BottomUpLeftJustifiedAlgorithm<V>
    where V : struct, IFloatingNumber<V> {
    private readonly Sheet2Shape<V> _sheet;
    private readonly Config _config;
    private readonly Quantity<V>? _kerf;

    /// <summary>
    /// 构造算法 / Construct the algorithm.
    /// </summary>
    /// <param name="sheet">板材形状 / Sheet shape.</param>
    /// <param name="config">算法配置 / Algorithm configuration.</param>
    /// <param name="kerf">刀缝间距，null 表示不考虑 / Kerf spacing; null means none.</param>
    public BottomUpLeftJustifiedAlgorithm(
        Sheet2Shape<V> sheet,
        Config? config = null,
        Quantity<V>? kerf = null) {
        _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        _config = config ?? new Config();
        _kerf = kerf;
    }

    /// <summary>
    /// 算法配置 / Algorithm configuration.
    /// </summary>
    /// <param name="WithDisplacementX">是否允许沿 X 轴向左滑动 / Whether sliding left along X is allowed.</param>
    /// <param name="WithDisplacementY">是否允许沿 Y 轴向下滑动 / Whether sliding down along Y is allowed.</param>
    /// <param name="WithWeightedDistance">是否按加权距离排序候选点 / Whether candidates are ordered by weighted distance.</param>
    public sealed record Config(
        bool WithDisplacementX = true,
        bool WithDisplacementY = true,
        bool WithWeightedDistance = true) {
        /// <summary>是否启用任一方向的滑动 / Whether sliding is enabled in either direction.</summary>
        public bool WithDisplacement => WithDisplacementX || WithDisplacementY;
    }

    /// <summary>
    /// 执行排样 / Execute the layout.
    /// </summary>
    /// <param name="items">物品与件数 / Items and their amounts.</param>
    /// <returns>放置结果 / The layout result.</returns>
    public Result<Bla2dLayoutResult<V>, ErrorCode, Error<ErrorCode>> Invoke(
        IEnumerable<(IAbstractItem<V> Item, int Amount)> items) {
        ArgumentNullException.ThrowIfNull(items);

        var placements = new List<Placement2<V>>();
        var skippedCircles = new List<string>();
        var skippedUnfitted = new List<string>();

        foreach ((IAbstractItem<V> item, int amount) in items) {
            // 圆形件整批跳过（一次登记，不按件数重复）。/ Circle items are skipped per item, not once per piece.
            if (item.IsCircleItem()) {
                skippedCircles.Add(item.Id.Value);
                continue;
            }
            for (int repeat = 0; repeat < amount; repeat++) {
                Placement2<V>? placed = PlaceItem(item, placements);
                if (placed is not null) {
                    placements.Add(placed);
                }
                else {
                    skippedUnfitted.Add(item.Id.Value);
                }
            }
        }

        return Results.Ok(new Bla2dLayoutResult<V>(
            Placements: placements,
            SkippedCircleItems: skippedCircles,
            SkippedUnfittedItems: skippedUnfitted));
    }

    /// <summary>
    /// 比较两个落点的放置优先级：先比较"先下后左"的加权距离，再比较 y，最后比较 x
    /// / Compare two positions: weighted bottom-left distance first, then y, then x.
    /// </summary>
    /// <param name="lhs">左侧落点 / Left-hand position.</param>
    /// <param name="rhs">右侧落点 / Right-hand position.</param>
    /// <param name="withWeightedDistance">是否先按加权距离排序 / Whether to order by weighted distance first.</param>
    /// <returns>比较结果，不可比较时返回 null / The order, or null when incomparable.</returns>
    public static Order? ComparePlacementPosition(
        QuantityPoint2<V> lhs,
        QuantityPoint2<V> rhs,
        bool withWeightedDistance = true) {
        ArgumentNullException.ThrowIfNull(lhs);
        ArgumentNullException.ThrowIfNull(rhs);
        if (withWeightedDistance) {
            Order? distance = QuantityCore.OrdOrNull(
                QuantityOps.PlusSafe(lhs.X, lhs.Y).Value,
                QuantityOps.PlusSafe(rhs.X, rhs.Y).Value);
            if (distance is null || distance is not Order.Equal) {
                return distance;
            }
        }
        Order? y = QuantityCore.OrdOrNull(lhs.Y, rhs.Y);
        if (y is null || y is not Order.Equal) {
            return y;
        }
        return QuantityCore.OrdOrNull(lhs.X, rhs.X);
    }

    /// <summary>
    /// 放置单个物品 / Place a single item.
    /// </summary>
    /// <param name="item">物品 / Item.</param>
    /// <param name="placed">已放置列表 / Already placed items.</param>
    /// <returns>放置，若无可行点则返回 null / The placement, or null when no feasible point exists.</returns>
    private Placement2<V>? PlaceItem(IAbstractItem<V> item, List<Placement2<V>> placed) {
        // 候选形状由物品给出（圆形件与正方形恒为单个），顺序固定以保证同一输入得到同一结果。
        // Candidates come from the item (circles and squares always yield one), in a fixed order so the same input
        // gives the same output.
        foreach (Shape2Candidate<V> candidate in item.CandidateShapes()) {
            foreach (QuantityPoint2<V> point in FeasiblePoints(placed)) {
                var placement = new Placement2<V>(
                    Shape: candidate.Shape,
                    Position: point,
                    Rotated: candidate.Rotated,
                    ItemId: item.Id.Value);
                if (IsFeasible(placement, placed)) {
                    return placement;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 计算所有候选落点 / Compute all candidate positions.
    /// </summary>
    /// <remarks>
    /// 候选点由已放置矩形的"左上角"与"右下角"派生；为空时给出原点。过滤到板材范围内的点，若一个都不在范围内也
    /// 回落到原点——否则第一个件将没有任何候选点，永远放不下。
    ///
    /// Candidates derive from the upper-left and lower-right corners of placed rectangles, falling back to the
    /// origin. Points outside the sheet are filtered out, and if none remain the origin is used as well: otherwise
    /// the very first item would have no candidate at all and could never be placed.
    /// </remarks>
    /// <param name="placed">已放置列表 / Already placed items.</param>
    /// <returns>排序后的候选点 / Sorted candidate positions.</returns>
    private List<QuantityPoint2<V>> FeasiblePoints(List<Placement2<V>> placed) {
        var points = new List<QuantityPoint2<V>>();
        var seen = new HashSet<(string, string)>();
        foreach (Placement2<V> placement in placed) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxX = placement.MaxX();
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> maxY = placement.MaxY();
            if (!maxX.IsOk || !maxY.IsOk) {
                continue;
            }
            AddUnique(points, seen, new QuantityPoint2<V>(placement.X, maxY.Value));
            AddUnique(points, seen, new QuantityPoint2<V>(maxX.Value, placement.Y));
            if (_config.WithDisplacement) {
                foreach (QuantityPoint2<V> displaced in
                    DisplacedPoints(placement, maxX.Value, maxY.Value, placed)) {
                    AddUnique(points, seen, displaced);
                }
            }
        }

        QuantityPoint2<V> origin = OriginPoint();
        if (points.Count == 0) {
            return new List<QuantityPoint2<V>> { origin };
        }

        var inside = points.Where(point =>
            QuantityCore.GreaterOrEqual(point.X, origin.X) &&
            QuantityCore.GreaterOrEqual(point.Y, origin.Y) &&
            QuantityCore.LessOrEqual(point.X, _sheet.Width) &&
            QuantityCore.LessOrEqual(point.Y, _sheet.Height)).ToList();
        List<QuantityPoint2<V>> usable = inside.Count == 0
            ? new List<QuantityPoint2<V>> { origin }
            : inside;

        // 稳定排序：不可比较的点保持原有相对顺序，而不是随机换位。
        // Stable sort: incomparable points keep their relative order instead of being permuted.
        var indexed = usable.Select((point, index) => (Point: point, Index: index)).ToList();
        indexed.Sort((left, right) => {
            int comparison = ComparePlacementPosition(
                left.Point, right.Point, _config.WithWeightedDistance) switch {
                Order.Less => -1,
                Order.Greater => 1,
                _ => 0,
            };
            return comparison != 0 ? comparison : left.Index.CompareTo(right.Index);
        });
        return indexed.Select(pair => pair.Point).ToList();
    }

    /// <summary>
    /// 去重加入候选点 / Add a candidate point without duplicates.
    /// </summary>
    /// <param name="points">候选点列表 / Candidate list.</param>
    /// <param name="seen">去重键集合 / Seen keys.</param>
    /// <param name="point">候选点 / The point.</param>
    private static void AddUnique(
        List<QuantityPoint2<V>> points,
        HashSet<(string, string)> seen,
        QuantityPoint2<V> point) {
        // 量值以字符串作为去重键：Quantity 记录本身不可哈希，且这里只需要"同值即同点"。
        // Quantities are keyed by string because the record is not a usable hash key and "same value means same
        // point" is all this needs.
        (string X, string Y) key = (
            point.X.ToString() ?? string.Empty,
            point.Y.ToString() ?? string.Empty);
        if (seen.Add(key)) {
            points.Add(point);
        }
    }

    /// <summary>
    /// 由既有放置沿 X/Y 轴滑动得到额外候选点 / Derive extra candidates by sliding along X and Y.
    /// </summary>
    /// <param name="placement">参考放置 / Reference placement.</param>
    /// <param name="maxX">右边界 / Right edge.</param>
    /// <param name="maxY">上边界 / Top edge.</param>
    /// <param name="placed">已放置列表 / Already placed items.</param>
    /// <returns>额外候选点 / Extra candidate positions.</returns>
    private List<QuantityPoint2<V>> DisplacedPoints(
        Placement2<V> placement,
        Quantity<V> maxX,
        Quantity<V> maxY,
        List<Placement2<V>> placed) {
        var points = new List<QuantityPoint2<V>>();
        if (_config.WithDisplacementX) {
            points.Add(new QuantityPoint2<V>(PointOnX(placement, placed), maxY));
        }
        if (_config.WithDisplacementY) {
            points.Add(new QuantityPoint2<V>(maxX, PointOnY(placement, placed)));
        }
        return points;
    }

    /// <summary>
    /// 沿 X 轴向左滑动到最近的可行位置 / Slide left along X to the nearest feasible position.
    /// </summary>
    /// <param name="placement">参考放置 / Reference placement.</param>
    /// <param name="placed">已放置列表 / Already placed items.</param>
    /// <returns>滑动后的 x 坐标 / x coordinate after sliding.</returns>
    private Quantity<V> PointOnX(Placement2<V> placement, List<Placement2<V>> placed) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ownMaxY = placement.MaxY();
        Quantity<V> placementMaxY = ownMaxY.IsOk ? ownMaxY.Value : placement.Y;
        Quantity<V> best = QuantityCompare.ZeroOf(placement.X);
        foreach (Placement2<V> other in placed) {
            if (ReferenceEquals(other, placement)) {
                continue;
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> otherMaxX = other.MaxX();
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> otherMaxY = other.MaxY();
            if (!otherMaxX.IsOk || !otherMaxY.IsOk) {
                continue;
            }
            // 仅考虑在 Y 方向与本放置重叠、且完全位于其左侧的放置。
            // Only placements overlapping in Y and lying fully to the left.
            bool overlapsInY =
                QuantityCore.Less(other.Y, placementMaxY) &&
                QuantityCore.Greater(otherMaxY.Value, placement.Y);
            bool liesLeft = QuantityCore.LessOrEqual(otherMaxX.Value, placement.X);
            if (overlapsInY && liesLeft && QuantityCore.Greater(otherMaxX.Value, best)) {
                best = otherMaxX.Value;
            }
        }
        return best;
    }

    /// <summary>
    /// 沿 Y 轴向下滑动到最近的可行位置 / Slide down along Y to the nearest feasible position.
    /// </summary>
    /// <param name="placement">参考放置 / Reference placement.</param>
    /// <param name="placed">已放置列表 / Already placed items.</param>
    /// <returns>滑动后的 y 坐标 / y coordinate after sliding.</returns>
    private Quantity<V> PointOnY(Placement2<V> placement, List<Placement2<V>> placed) {
        Quantity<V> best = QuantityCompare.ZeroOf(placement.Y);
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> ownMaxX = placement.MaxX();
        Quantity<V> placementMaxX = ownMaxX.IsOk ? ownMaxX.Value : placement.X;
        foreach (Placement2<V> other in placed) {
            if (ReferenceEquals(other, placement)) {
                continue;
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> otherMaxX = other.MaxX();
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> otherMaxY = other.MaxY();
            if (!otherMaxX.IsOk || !otherMaxY.IsOk) {
                continue;
            }
            // 仅考虑在 X 方向与本放置重叠、且完全位于其下方的放置。
            // Only placements overlapping in X and lying fully below.
            bool overlapsInX =
                QuantityCore.Less(other.X, placementMaxX) &&
                QuantityCore.Greater(otherMaxX.Value, placement.X);
            bool liesBelow = QuantityCore.LessOrEqual(otherMaxY.Value, placement.Y);
            if (overlapsInX && liesBelow && QuantityCore.Greater(otherMaxY.Value, best)) {
                best = otherMaxY.Value;
            }
        }
        return best;
    }

    /// <summary>
    /// 判断放置是否可行：在板材内且与既有放置不重叠 / Check feasibility: inside the sheet and not overlapping.
    /// </summary>
    /// <param name="placement">待检查放置 / Placement to check.</param>
    /// <param name="placed">已放置列表 / Already placed items.</param>
    /// <returns>是否可行 / Whether feasible.</returns>
    private bool IsFeasible(Placement2<V> placement, List<Placement2<V>> placed) {
        Result<bool, ErrorCode, Error<ErrorCode>> inside = placement.InsideSheet(_sheet);
        if (!inside.IsOk || !inside.Value) {
            return false;
        }
        bool hasKerf = _kerf is { } kerf && QuantityCore.IsPositive(kerf);
        foreach (Placement2<V> other in placed) {
            // 刀缝双向检查：任一侧膨胀后相交，说明真实间隙小于 kerf。
            // Kerf is checked in both directions: intersecting after inflating either side means the real gap is
            // below the kerf.
            bool overlapped;
            if (hasKerf) {
                Placement2<V> inflated = placement.InflatedBy(_kerf!);
                Result<bool, ErrorCode, Error<ErrorCode>> forward = inflated.Overlapped(other);
                overlapped = forward.IsOk && forward.Value;
                if (!overlapped) {
                    Result<bool, ErrorCode, Error<ErrorCode>> backward =
                        placement.Overlapped(other.InflatedBy(_kerf!));
                    overlapped = backward.IsOk && backward.Value;
                }
            }
            else {
                Result<bool, ErrorCode, Error<ErrorCode>> direct = placement.Overlapped(other);
                overlapped = direct.IsOk && direct.Value;
            }
            if (overlapped) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 板材原点 / The sheet origin.
    /// </summary>
    /// <returns>原点坐标 / Origin point.</returns>
    private QuantityPoint2<V> OriginPoint() => new(
        QuantityCompare.ZeroOf(_sheet.Width),
        QuantityCompare.ZeroOf(_sheet.Height));
}
