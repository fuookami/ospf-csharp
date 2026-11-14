// 舱位几何与冲突扫描 / Position geometry and conflict sweep.
// 与固定 Kotlin 基线 `bpp/infrastructure/LoadingGeometry.kt` 对应。
// Mirrors the pinned Kotlin baseline's `bpp/infrastructure/LoadingGeometry.kt`.

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp.Infrastructure;

/// <summary>
/// 舱位的纵向区间；空间 ID 隔离不同甲板 / Longitudinal interval scoped to a physical space.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">舱位标识 / Position identity.</param>
/// <param name="SpaceId">甲板/独立空间 / Deck or independent space.</param>
/// <param name="Start">起点 / Start.</param>
/// <param name="End">终点 / End.</param>
public sealed record LoadingInterval<V>(string Id, string SpaceId, Quantity<V> Start, Quantity<V> End)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 舱位冲突（有向，按扫描顺序） / Position conflict, directed and in sweep order.
/// </summary>
/// <param name="Earlier">先开始的舱位 / The position that starts earlier.</param>
/// <param name="Later">后开始的舱位 / The position that starts later.</param>
public sealed record PositionConflict(string Earlier, string Later);

/// <summary>
/// 扫描纵向候选，仅对活跃区间执行可选二维精确判交 / Sweep intervals, refining active candidates with an optional 2D test.
/// </summary>
public static class LoadingGeometry {
    /// <summary>
    /// 计算内部重叠边 / Compute the interior-overlap edges.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="intervals">区间 / Intervals.</param>
    /// <param name="intersects">可选精确判交 / Optional exact intersection test.</param>
    /// <returns>内部重叠边或失败 / Interior-overlap edges or failure.</returns>
    /// <remarks>
    /// **扫描线，不是两两比较**：按起点排序，维护"仍然活跃"的集合，
    /// 每次先剔除已经结束的，再与剩余活跃区间配对。复杂度从 `O(n²)` 降到接近 `O(n log n + 冲突数)`。
    ///
    /// **三个容易写错的边界**：
    ///
    /// | 点 | 错法 | 后果 |
    /// | --- | --- | --- |
    /// | 剔除条件用 `&lt;` 而非 `&lt;=` | `it.End &lt; current.Start` | **首尾相接**（`end == start`）被判成重叠，多报冲突 |
    /// | 先配对后剔除 | 顺序颠倒 | 已结束的区间仍参与配对 |
    /// | 只在同一 `SpaceId` 内比较 | 跨空间混扫 | 不同甲板被判成冲突 |
    ///
    /// **Sweep line, not pairwise**: sort by start, keep an active set, evict finished intervals first, then pair against what remains — close to `O(n log n + conflicts)`
    /// instead of `O(n²)`. **Three easy boundary mistakes** are in the table: using `&lt;` instead of `&lt;=` would flag **touching** intervals (`end == start`) as
    /// overlapping; pairing before eviction would keep finished intervals in play; and sweeping across spaces would report different decks as conflicting.
    /// </remarks>
    public static Result<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>> Conflicts<V>(
        IReadOnlyList<LoadingInterval<V>> intervals,
        global::System.Func<string, string, Result<bool, ErrorCode, Error<ErrorCode>>>? intersects = null)
        where V : struct, IFloatingNumber<V> {
        var seen = new HashSet<string>(intervals.Count);
        foreach (LoadingInterval<V> interval in intervals) {
            if (!seen.Add(interval.Id)) {
                return Results.Failed<IReadOnlySet<PositionConflict>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "舱位 ID 重复 / Duplicate position IDs"));
            }
        }

        var normalized = new List<LoadingInterval<V>>(intervals.Count);
        foreach (LoadingInterval<V> interval in intervals) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> start = interval.Start.To(SIBaseUnits.Meter);
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> end = interval.End.To(SIBaseUnits.Meter);
            if (start is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> startOk
                || end is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> endOk) {
                return Results.Failed<IReadOnlySet<PositionConflict>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, $"舱位区间无效 / Invalid loading interval: {interval.Id}"));
            }
            V startValue = startOk.Value.Value;
            V endValue = endOk.Value.Value;
            if (string.IsNullOrWhiteSpace(interval.Id)
                || string.IsNullOrWhiteSpace(interval.SpaceId)
                || !startValue.IsFinite()
                || !endValue.IsFinite()
                || startValue.CompareTo(endValue) >= 0) {
                return Results.Failed<IReadOnlySet<PositionConflict>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, $"舱位区间无效 / Invalid loading interval: {interval.Id}"));
            }
            normalized.Add(interval with { Start = startOk.Value, End = endOk.Value });
        }

        var conflicts = new List<PositionConflict>();
        var dedup = new HashSet<PositionConflict>();
        var bySpace = new Dictionary<string, List<LoadingInterval<V>>>();
        var spaceOrder = new List<string>();
        foreach (LoadingInterval<V> interval in normalized) {
            if (!bySpace.TryGetValue(interval.SpaceId, out List<LoadingInterval<V>>? bucket)) {
                bucket = new List<LoadingInterval<V>>();
                bySpace[interval.SpaceId] = bucket;
                spaceOrder.Add(interval.SpaceId);
            }
            bucket.Add(interval);
        }

        foreach (string spaceId in spaceOrder) {
            List<LoadingInterval<V>> space = bySpace[spaceId];
            space.Sort((left, right) => left.Start.Value.CompareTo(right.Start.Value));
            var active = new List<LoadingInterval<V>>();
            foreach (LoadingInterval<V> current in space) {
                // **先剔除已结束的**（`End &lt;= Start` 即不重叠），再配对。
                // **Evict finished intervals first** (`End &lt;= Start` means no overlap), then pair.
                active.RemoveAll(other => other.End.Value.CompareTo(current.Start.Value) <= 0);
                foreach (LoadingInterval<V> other in active) {
                    bool overlap = true;
                    if (intersects is not null) {
                        Result<bool, ErrorCode, Error<ErrorCode>> probed = intersects(other.Id, current.Id);
                        if (probed is Failed<bool, ErrorCode, Error<ErrorCode>> failed) {
                            return Results.Failed<IReadOnlySet<PositionConflict>>(failed.Error);
                        }
                        if (probed is Fatal<bool, ErrorCode, Error<ErrorCode>> fatal) {
                            return new Fatal<IReadOnlySet<PositionConflict>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
                        }
                        overlap = ((Ok<bool, ErrorCode, Error<ErrorCode>>)probed).Value;
                    }
                    if (overlap) {
                        var edge = new PositionConflict(other.Id, current.Id);
                        if (dedup.Add(edge)) {
                            conflicts.Add(edge);
                        }
                    }
                }
                active.Add(current);
            }
        }

        return Results.Ok<IReadOnlySet<PositionConflict>>(new HashSet<PositionConflict>(conflicts));
    }
}
