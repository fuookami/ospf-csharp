// 二维离散装载几何适配 / Two-dimensional discrete-loading geometry adapter.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using GeometryConflict = Fuookami.Ospf.Framework.Bpp.Infrastructure.PositionConflict;
// **`PositionConflict` 有两个同名类型**：几何侧是有向边，业务侧是无序对。这里只用业务侧那个。
// **`PositionConflict` exists twice**: the geometry side is a directed edge, the business side an unordered pair. Only the business one is used here.
using PositionConflict = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.DiscreteLoading;

/// <summary>
/// 显式二维舱位边界 / An explicit two-dimensional position boundary.
/// </summary>
/// <param name="Id">舱位标识 / Position identifier.</param>
/// <param name="SpaceId">甲板或独立空间 / Deck or independent space.</param>
/// <param name="Bounds">共享几何包围盒 / The shared geometry box.</param>
public sealed record LoadingRectangle<V>(string Id, string SpaceId, Box2Geometry<V> Bounds)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 二维离散装载几何适配 / Two-dimensional discrete-loading geometry adapter.
/// </summary>
/// <remarks>
/// 对齐的 `Bpp2dDiscreteLoading.context`。**与一维的差别只有一处**：
/// 判交从"区间重叠"换成"矩形精确相交"，其余（一一对应校验、合并业务互斥、`validate`）逐字相同。
///
/// **为什么值得单独存在**：Demo2 的 `LoadingContextFactory` 一直只走一维内核，原因是"Bpp2d 没有 `Context` 工厂"
/// （那句话写在那个文件的类注释里，是本仓记在案的真实缺口）。**补上它，二维装载才不是一句空话。**
///
/// **两处与差异，都是"条做不到或有歧义"**：
///
/// 1. **用 `Box2Geometry.Overlapped` 而不是另写一套判交**。`normalized.getValue(a).overlapped(...)` 走的是
///    共享几何的 `Box2Geometry`，本仓的同名类型**直接委托** `QuantityBox2.Overlapped`，所以这里也走它——
///    **再写一份相交算法等于给同一个语义留两份实现**。
/// 2. **`validate` 在 `DiscreteLoadingContext` 构造里已经跑过**（本仓的构造即校验），所以这里不重复调用；
///    `context.validate` 是因为它的构造不做校验。**沿用一次多余的校验不会更安全，只会让失败点变两个。**
///
/// Aligned with its `Bpp2dDiscreteLoading.context`. **The only difference from 1-D is the intersection test** — rectangles instead of intervals;
/// everything else (the two-way correspondence check, merging business conflicts, validation) is identical. **Why it earns a file**: Demo2's `LoadingContextFactory`
/// has only ever used the 1-D kernel because "Bpp2d has no `Context` factory" — a real gap recorded in that file's remarks. **With this in place, 2-D loading stops
/// being a slogan.** Two deliberate differences, both because its line cannot be honoured or is ambiguous: the intersection test **reuses**
/// `Box2Geometry.Overlapped` (whose C# counterpart delegates to `QuantityBox2.Overlapped`) rather than growing a second algorithm for the same semantics, and
/// **`validate` is not called again** since this implementation's `DiscreteLoadingContext` constructor already validates — the code calls it because its constructor does
/// not. **Copying a redundant check does not make things safer; it only creates a second failure point.**
/// </remarks>
public static class Bpp2dDiscreteLoading
{
    /// <summary>
    /// 合并二维几何冲突与业务互斥，构造上下文 / Merge 2-D geometric conflicts with business conflicts and build the context.
    /// </summary>
    /// <typeparam name="V">领域数值 / Domain number.</typeparam>
    /// <param name="problem">离散输入 / Discrete input.</param>
    /// <param name="geometry">每个舱位的显式矩形 / Explicit rectangle for every position.</param>
    /// <param name="adapter">数值边界 / Numeric adapter.</param>
    /// <returns>上下文或失败 / Context or failure.</returns>
    public static Result<DiscreteLoadingContext<V>, ErrorCode, Error<ErrorCode>> Context<V>(
        DiscreteLoadingProblem<V> problem,
        IReadOnlyList<LoadingRectangle<V>> geometry,
        IPackingValueAdapter<V> adapter)
        where V : struct, IFloatingNumber<V>
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(adapter);

        // **双向包含才叫一一对应**，与一维那份同一个理由：只查一个方向，另一侧的舱位或矩形会被静默忽略。
        // **The correspondence must hold both ways**, for the same reason as the 1-D version: checking one direction silently ignores positions or rectangles on the other side.
        var geometryIds = new HashSet<string>(geometry.Count);
        var normalized = new Dictionary<string, Box2Geometry<V>>(geometry.Count);
        var intervals = new List<LoadingInterval<V>>(geometry.Count);
        foreach (LoadingRectangle<V> rectangle in geometry)
        {
            if (string.IsNullOrWhiteSpace(rectangle.Id))
            {
                return Results.Failed<DiscreteLoadingContext<V>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "二维舱位 ID 为空 / A 2-D position has a blank ID"));
            }
            if (!geometryIds.Add(rectangle.Id))
            {
                return Results.Failed<DiscreteLoadingContext<V>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "二维舱位 ID 重复 / Duplicate 2-D position IDs: " + rectangle.Id));
            }
            normalized[rectangle.Id] = rectangle.Bounds;
            // 纵向区间只用来**剪枝**：扫描时按 x 方向排一遍，判交仍由 `Overlapped` 决定。
            // **不拿它替代判交**——一维区间重叠会把"x 重叠但 y 不重叠"的舱位错判成冲突。
            //
            // The longitudinal interval only **prunes**: the sweep sorts by x, and `Overlapped` still decides. **It never replaces the intersection test** — interval
            // overlap alone would mark positions that overlap in x but not in y as conflicting.
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> end = rectangle.Bounds.MaxX();
            if (end is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> endOk)
            {
                return Results.Failed<DiscreteLoadingContext<V>>(
                    ((Failed<Quantity<V>, ErrorCode, Error<ErrorCode>>)end).Error);
            }
            intervals.Add(new LoadingInterval<V>(
                Id: rectangle.Id,
                SpaceId: rectangle.SpaceId,
                Start: rectangle.Bounds.X,
                End: endOk.Value));
        }

        var positionIds = new HashSet<string>(problem.Positions.Count);
        foreach (LoadingPosition<V> position in problem.Positions)
        {
            positionIds.Add(position.Id);
        }
        if (!geometryIds.SetEquals(positionIds))
        {
            return Results.Failed<DiscreteLoadingContext<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "舱位与几何不匹配 / Position/geometry mismatch"));
        }

        // **合并而不是替换**：`Conflicts` 的 `intersects` 回调只决定几何那一半，业务互斥边由它自己保留。
        // **Merge, never replace**: the `intersects` callback decides only the geometric half, and `Conflicts` keeps the caller's business edges itself.
        // **这里的翻译是真实需要的，不是噪音**（`Bpp1dDiscreteLoading` 里同一处也这么写）：几何侧给的是**有向边**
        // （`GeometryConflict`，字段 `Earlier`/`Later`），业务侧要的是**无序对**（`Model.PositionConflict`，
        // 字段 `First`/`Second`）。**同名不同类型**，直接赋值编译不过——而这正是它该编译不过的地方。
        //
        // **The translation is real, not noise** (the 1-D kernel says the same at the same seam): the geometry side yields a **directed edge**
        // (`GeometryConflict`, fields `Earlier`/`Later`) while the business side wants an **unordered pair** (`Model.PositionConflict`, fields
        // `First`/`Second`). **Same name, different types** — direct assignment does not compile, and that is exactly where it should not.
        Result<IReadOnlySet<GeometryConflict>, ErrorCode, Error<ErrorCode>> swept =
            LoadingGeometry.Conflicts(intervals, (first, second) =>
                normalized[first].Overlapped(normalized[second]));
        if (swept is not Ok<IReadOnlySet<GeometryConflict>, ErrorCode, Error<ErrorCode>> sweptOk)
        {
            return Results.Failed<DiscreteLoadingContext<V>>(
                ((Failed<IReadOnlySet<GeometryConflict>, ErrorCode, Error<ErrorCode>>)swept).Error);
        }

        var conflicts = new HashSet<PositionConflict>(problem.Conflicts);
        foreach (GeometryConflict conflict in sweptOk.Value)
        {
            conflicts.Add(new PositionConflict(conflict.Earlier, conflict.Later));
        }

        var merged = new DiscreteLoadingProblem<V>(
            Items: problem.Items,
            Positions: problem.Positions,
            AllowedAssignments: problem.AllowedAssignments,
            Conflicts: conflicts,
            LayoutGroups: problem.LayoutGroups,
            WeightUnit: problem.WeightUnit);
        return Results.Ok(new DiscreteLoadingContext<V>(merged, adapter));
    }
}
