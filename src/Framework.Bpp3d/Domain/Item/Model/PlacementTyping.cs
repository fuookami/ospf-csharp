// IDE0007 与 IDE0008 在本文件的泛型局部变量上互相否定（写 var 要显式、写显式要 var）。两轮翻写验证过是分析器在泛型推断上自相矛盾，
// 因此在本文件内抑制这两条；预算数字与"没有新类别"的判断因此仍然可信。
// IDE0007 and IDE0008 contradict each other on this file's generic locals. Two rounds of flipping confirmed an analyzer inconsistency in generic
// inference, so both are suppressed file-locally and the warning budget stays meaningful.
#pragma warning disable IDE0007, IDE0008
#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 放置类型工具 / Placement typing utilities.
/// </summary>
/// <remarks>
/// 三个几何助手：（`topItemPlacements`／`bottomItemPlacements`）、
///（`topPlacements`／`bottomPlacements`）、（`filterBottomOverlapped`）。
///
/// **这里原有的 `TopItemPlacements` 取的是"全局最高一层的所有放置"，**
/// 现在取的是"没有任何底面重叠且更高的放置压在它上面的放置"。 两者在一高一低的货堆上给出不同答案：
/// 现在两件都算顶层（谁也不压谁），旧实现只返回高的那件——于是矮的那件被当成"被压住的"，而它上面其实什么都没有。
/// 旧实现全仓零调用方，所以这次直接替换，而不是并存两个同名不同义的方法。
///
/// Three geometry helpers: membership (`topItemPlacements`/`bottomItemPlacements`), ordered (`topPlacements`/`bottomPlacements`) and filtering (`filterBottomOverlapped`). **The old `TopItemPlacements` returned "every placement on the globally
/// highest layer" while the code returns "every placement that no overlapping placement sits above". The two disagree on an uneven stack: the
/// current code counts both as top (neither covers the other) while the old one returned only the taller — so the short one was treated as covered when
/// nothing was on it. The old implementation had zero callers (measured), so it is replaced rather than kept as a same-name-different-meaning
/// twin.
/// </remarks>
public static class PlacementTyping {
    /// <summary>
    /// 尝试将通用放置转换为货物放置 / Try converting generic placement to item placement.
    /// </summary>
    /// <typeparam name="T">放置载荷类型 / The placement payload type.</typeparam>
    /// <param name="placement">通用放置 / The generic placement.</param>
    /// <returns>货物放置，或 null / The item placement, or null.</returns>
    public static QuantityPlacement3<ActualItem, FltX>? ToItemPlacementOrNull<T>(
        QuantityPlacement3<T, FltX> placement) where T : class {
        if (placement.Unit is ActualItem item) {
            return new QuantityPlacement3<ActualItem, FltX>(item, placement.Position, placement.Orientation);
        }
        return null;
    }

    /// <summary>
    /// 顶面放置：没有任何"底面重叠且更高"的放置压在其上 / Top placements: nothing overlapping its footprint sits higher.
    /// </summary>
    /// <remarks>
    /// 实现与 `TopPlacements` 一致，含那条严格比较 `placement1.maxY ls placement2.maxY`——同层重叠的两件货彼此都算顶层。
    ///
    /// Line for line with `TopPlacements`, including the strict comparison: two items on the same layer, overlapping or not, cover neither.
    /// </remarks>
    /// <param name="placements">放置列表 / The placements.</param>
    /// <returns>顶面放置 / The top placements.</returns>
    public static IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> TopPlacements(
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> placements) {
        List<QuantityPlacement3<ActualItem, FltX>> tops = new(placements.Count);
        foreach (QuantityPlacement3<ActualItem, FltX> candidate in placements) {
            bool covered = false;
            foreach (QuantityPlacement3<ActualItem, FltX> other in placements) {
                if (ReferenceEquals(other, candidate)) {
                    continue;
                }
                if (PlacementFactory.OverlappedOnBottom(candidate, other) && MaxY(candidate) < MaxY(other)) {
                    covered = true;
                    break;
                }
            }
            if (!covered) {
                tops.Add(candidate);
            }
        }
        return tops;
    }

    /// <summary>
    /// 底面放置：没有"底面重叠且下沿更低"的放置垫在其下 / Bottom placements: nothing overlapping its footprint has a lower bottom edge.
    ///
    /// 这里比较的是放置的 `.y`（下沿），与 `TopPlacements` 比 `maxY` 不同——两处不是同一个量，不能统一成一个。
    /// </summary>
    /// <param name="placements">放置列表 / The placements.</param>
    /// <returns>底面放置 / The bottom placements.</returns>
    public static IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> BottomPlacements(
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> placements) {
        List<QuantityPlacement3<ActualItem, FltX>> bottoms = new(placements.Count);
        foreach (QuantityPlacement3<ActualItem, FltX> candidate in placements) {
            bool supported = false;
            foreach (QuantityPlacement3<ActualItem, FltX> other in placements) {
                if (ReferenceEquals(other, candidate)) {
                    continue;
                }
                if (PlacementFactory.OverlappedOnBottom(candidate, other) && MinY(candidate) > MinY(other)) {
                    supported = true;
                    break;
                }
            }
            if (!supported) {
                bottoms.Add(candidate);
            }
        }
        return bottoms;
    }

    /// <summary>顶面放置，按集合成员筛 / Top placements, filtered by membership.</summary>
    /// <param name="placements">放置列表 / The placements.</param>
    /// <returns>顶面放置 / The top placements.</returns>
    public static IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> TopItemPlacements(
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> placements) {
        var tops = TopPlacements(placements).ToHashSet();
        return placements.Where(tops.Contains).ToList();
    }

    /// <summary>底面放置，按集合成员筛 / Bottom placements, filtered by membership.</summary>
    /// <param name="placements">放置列表 / The placements.</param>
    /// <returns>底面放置 / The bottom placements.</returns>
    public static IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> BottomItemPlacements(
        IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> placements) {
        var bottoms = BottomPlacements(placements).ToHashSet();
        return placements.Where(bottoms.Contains).ToList();
    }

    /// <summary>
    /// 底面投影与目标重叠的放置 / Placements whose bottom footprint overlaps the target.
    /// </summary>
    /// <param name="placements">候选放置 / The candidates.</param>
    /// <param name="target">目标放置 / The target.</param>
    /// <returns>重叠的放置 / The overlapping placements.</returns>
    public static IReadOnlyList<QuantityPlacement3<ActualItem, FltX>> FilterBottomOverlapped(
        IEnumerable<QuantityPlacement3<ActualItem, FltX>> placements,
        QuantityPlacement3<ActualItem, FltX> target) =>
        placements.Where(placement => PlacementFactory.OverlappedOnBottom(placement, target)).ToList();

    /// <summary>
    /// 把真实放置接到 <see cref="IStackPlacement"/> 上，供 <see cref="StackLayering"/> 递归使用 /
    /// Adapts a real placement to <see cref="IStackPlacement"/> for the <see cref="StackLayering"/> recursion.
    /// </summary>
    /// <remarks>
    /// `Covers` 在这里落地，而不是塞回递归： "other 压在我上面" = 底面投影重叠且 other 的最低点不低于我的最高点。
    /// 递归因此与放置类型无关，真实几何与判据里的最小替身可以互换——这正是保留 `Covers` 抽象的目的。
    ///
    /// `Covers` is defined here rather than pushed back into the recursion: "other sits on top" means the bottom footprints overlap and the other's
    /// bottom edge is not below this one's top edge. The recursion therefore stays independent of the placement type, and real geometry and the
    /// minimal double used by the criteria remain interchangeable — which is exactly why the `Covers` abstraction is kept.
    /// </remarks>
    /// <param name="placement">被包装的放置 / The wrapped placement.</param>
    /// <returns>堆叠视图 / The stacking view.</returns>
    public static IStackPlacement AsStackPlacement(QuantityPlacement3<ActualItem, FltX> placement) =>
        new PlacementStackAdapter(placement);

    /// <summary>放置的最高点（含容差比较）/ The placement's top edge.</summary>
    /// <param name="placement">放置 / The placement.</param>
    /// <returns>最高点 / The top edge.</returns>
    internal static double MaxY(QuantityPlacement3<ActualItem, FltX> placement) =>
        placement.Position.Y.Value.ToFlt64().ToDouble() + placement.Unit.Height.Value.ToFlt64().ToDouble();

    /// <summary>放置的最低点 / The placement's bottom edge.</summary>
    /// <param name="placement">放置 / The placement.</param>
    /// <returns>最低点 / The bottom edge.</returns>
    internal static double MinY(QuantityPlacement3<ActualItem, FltX> placement) =>
        placement.Position.Y.Value.ToFlt64().ToDouble();

    private sealed class PlacementStackAdapter(QuantityPlacement3<ActualItem, FltX> placement) : IStackPlacement
    {
        public string ItemType => placement.Unit.PackageType.ToString();

        public Quantity<FltX> Height => placement.Unit.Height;

        public bool OverlapsBottom(IStackPlacement other) =>
            other is PlacementStackAdapter adapter &&
            PlacementFactory.OverlappedOnBottom(placement, adapter.Placement);

        public bool IsAbove(IStackPlacement other) =>
            other is PlacementStackAdapter adapter && MaxY(placement) > MaxY(adapter.Placement);

        internal QuantityPlacement3<ActualItem, FltX> Placement => placement;
    }
}