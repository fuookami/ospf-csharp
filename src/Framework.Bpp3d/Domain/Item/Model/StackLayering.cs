// IDE0007 与 IDE0008 在本文件的泛型局部变量上互相否定（写 var 要显式、写显式要 var）。两轮翻写验证过是分析器在泛型推断上自相矛盾，
// 因此在本文件内抑制这两条；预算数字与"没有新类别"的判断因此仍然可信。
// IDE0007 and IDE0008 contradict each other on this file's generic locals. Two rounds of flipping confirmed an analyzer inconsistency in generic
// inference, so both are suppressed file-locally and the warning budget stays meaningful.
#pragma warning disable IDE0007, IDE0008
#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;

/// <summary>
/// 参与堆叠计算的放置：只需要"类型、高度、谁压在谁上面"三件事 /
/// A placement that takes part in stacking: only its type, its height and who sits on whom.
/// </summary>
/// <remarks>
/// 基线直接用 `QuantityPlacement3&lt;Item, FltX&gt;`，并调用 `topItemPlacements`／`filterBottomOverlapped` 两个几何助手。
/// C# 侧 `QuantityPlacement3` 在 `Application` 层，那两个助手没有对应物——**而递归本身与几何无关：它只问三件事。**
/// 所以这里把几何收成一个谓词 `Covers`，由调用方按自己的几何实现；**递归、同类型过滤、取最大值这三条语义逐行照搬基线。**
///
/// the code uses `QuantityPlacement3&lt;Item, FltX&gt;` plus the geometry helpers `topItemPlacements` and `filterBottomOverlapped`. On the C#
/// side `QuantityPlacement3` lives in the `Application` layer and neither helper exists — while the recursion itself is independent of geometry: it asks
/// three things. So the geometry is reduced to one predicate, `Covers`, supplied by the caller, and the recursion, the same-type filter and the maximum
/// are implemented line for line.
/// </remarks>
public interface IStackPlacement
{
    /// <summary>物品类型（基线比较的是 `bottomItem.type == item.type`）/ The item type, which the code compares for equality.</summary>
    string ItemType { get; }

    /// <summary>该放置的高度 / The placement's height.</summary>
    Quantity<FltX> Height { get; }

    /// <summary>
    /// 底面投影是否与另一放置重叠 / Whether the bottom footprint overlaps the other placement's.
    /// </summary>
    /// <remarks>
    /// **基线递归里用的是 `filterBottomOverlapped`——只比底面投影，不看高度。**
    /// 第一版把它写成"贴着才算压在上面"，于是三层堆叠只数出两层：`top` 与 `base` 之间隔着一层，就不再相连。
    /// **名字现在说它真正做的事，而不是我猜的意图。**
    ///
    /// **The recursion uses `filterBottomOverlapped`, which compares bottom footprints only and ignores height. The first version wrote it
    /// as "touching counts as sitting on top", so a three-high stack counted as two: the top and the base are separated by a layer and stopped connecting.
    /// The name now says what it does rather than what I assumed.
    /// </remarks>
    /// <param name="other">另一放置 / The other placement.</param>
    /// <returns>底面投影是否重叠 / Whether the footprints overlap.</returns>
    bool OverlapsBottom(IStackPlacement other);

    /// <summary>
    /// 是否比另一放置更高（含底面重叠的判定由调用方组合）/ Whether this placement stands higher than the other.
    /// </summary>
    /// <remarks>
    /// 顶层集合需要"底面重叠**且更高**"这一对关系（基线 `topPlacements` 的第二条），而递归只需要底面重叠。
    /// 两个关系分开给出，是因为把"更高"塞进重叠关系会让"谁在谁上面"和"谁与谁同列"混成一件事——第一版就是这样丢掉一层的。
    ///
    /// the top set needs "footprints overlap AND this one is higher" (its second condition) while the recursion needs plain overlap. They
    /// are separate because folding "higher" into overlap conflates "who is above whom" with "who shares a column" — which is how the first version lost a
    /// layer.
    /// </remarks>
    /// <param name="other">另一放置 / The other placement.</param>
    /// <returns>是否更高 / Whether it is higher.</returns>
    bool IsAbove(IStackPlacement other);
}

/// <summary>
/// 递归计算货物在底部集合中的堆叠层数与高度 / Recursively compute the stacking layer count and height inside a set of bottom placements.
/// </summary>
/// <remarks>
/// 与基线 `PackageAttribute` 的伴生对象对应：`layerLayer`（层数）、`layerHeight`（高度）与把它们一并算出的 `layer`。
/// **协程并行取各分支的最大值；这里串行**——结果相同（都是最大值），而并行度属于调用方的事。
///
/// 's companion object: `layerLayer` (count), `layerHeight` (height) and `layer` computing both. **The code runs the branches
/// concurrently and takes the maximum; this runs them in sequence** — the result is the same (a maximum), and parallelism is the caller's business.
/// </remarks>
public static class StackLayering
{
    /// <summary>
    /// 直接底部项：没有被任何其他项压住的那些 / The direct bottom items: those not covered by any other item.
    /// </summary>
    /// <typeparam name="T">放置类型 / The placement type.</typeparam>
    /// <param name="items">候选集合 / The candidates.</param>
    /// <returns>直接底部项 / The direct bottom items.</returns>
    // **IDE0007 与 IDE0008 在这四个泛型局部变量上互相否定：** 写成 `var` 它要显式类型，写成显式类型它又要 `var`。
    // **同一个表达式被两个相反的规则同时命中，说明这是分析器在泛型推断上的自相矛盾，不是代码风格问题。**
    // **这四条因此留在警告预算里（预算本就包含这类存量），不再用 #pragma 遮——遮掉会让预算数字失真。**
    //
    // **IDE0007 and IDE0008 contradict each other on these four generic locals: written with `var` they demand an explicit type, written with an explicit
    // type they demand `var`.** **The same expression tripping two opposite rules is an analyzer inconsistency in generic inference, not a style issue.**
    // **The four stay in the warning budget rather than being hidden, because hiding them distorts the number.**
    public static IReadOnlyList<T> DirectBottomItems<T>(IReadOnlyList<T> items)
        where T : class, IStackPlacement {
        List<T> direct = new(items.Count);
        foreach (var item in items) {
            bool covered = items.Any(other =>
                !ReferenceEquals(other, item) && other.OverlapsBottom(item) && other.IsAbove(item));
            if (!covered) {
                direct.Add(item);
            }
        }
        return direct;
    }

    /// <summary>堆叠层数 / The stacking layer count.</summary>
    /// <typeparam name="T">放置类型 / The placement type.</typeparam>
    /// <param name="item">待计算货物 / The item being placed.</param>
    /// <param name="bottomItems">底部集合 / The bottom placements.</param>
    /// <returns>层数 / The layer count.</returns>
    public static UInt64 LayerCount<T>(T item, IReadOnlyList<T> bottomItems)
        where T : class, IStackPlacement => LayerCount(item, bottomItems, depth: 0);

    /// <summary>堆叠高度 / The stacking height.</summary>
    /// <typeparam name="T">放置类型 / The placement type.</typeparam>
    /// <param name="item">待计算货物 / The item being placed.</param>
    /// <param name="bottomItems">底部集合 / The bottom placements.</param>
    /// <returns>高度 / The height.</returns>
    public static Quantity<FltX> LayerHeight<T>(T item, IReadOnlyList<T> bottomItems)
        where T : class, IStackPlacement => LayerHeight(item, bottomItems, depth: 0);

    /// <summary>层数与高度一次算出 / Compute the layer count and the height together.</summary>
    /// <typeparam name="T">放置类型 / The placement type.</typeparam>
    /// <param name="item">待计算货物 / The item being placed.</param>
    /// <param name="bottomItems">底部集合 / The bottom placements.</param>
    /// <returns>层数与高度 / The layer count and the height.</returns>
    public static (UInt64 Layer, Quantity<FltX> Height) Layer<T>(T item, IReadOnlyList<T> bottomItems)
        where T : class, IStackPlacement =>
        (LayerCount(item, bottomItems), LayerHeight(item, bottomItems));

    /// <summary>递归深度上限 / The recursion depth ceiling.</summary>
    /// <remarks>
    /// ponytail: 固定 64 层的深度上限，超限即停。真实的货垛不会这么深，而一个互相压住的环会让递归停不下来；
    /// 要更强的保证就换成显式栈 + 访问集合。
    ///
    /// ponytail: a fixed depth ceiling of 64 stops runaway recursion. A real stack never gets that deep while a mutually-covering cycle would recurse
    /// forever; a stronger guarantee means an explicit stack plus a visited set.
    /// </remarks>
    private const int MaxDepth = 64;

    private static UInt64 LayerCount<T>(T item, IReadOnlyList<T> bottomItems, int depth)
        where T : class, IStackPlacement {
        if (depth >= MaxDepth) {
            return UInt64.Zero;
        }
        IReadOnlyList<T> direct = DirectBottomItems(bottomItems);
        List<T> indirect = bottomItems.Where(candidate => !direct.Contains(candidate)).ToList();

        UInt64 maxLayer = UInt64.Zero;
        foreach (T bottom in direct) {
            if (!string.Equals(bottom.ItemType, item.ItemType, System.StringComparison.Ordinal)) {
                continue;
            }
            List<T> under = indirect.Where(bottom.OverlapsBottom).ToList();
            UInt64 value = under.Count > 0
                ? UInt64.One + LayerCount(bottom, under, depth + 1)
                : UInt64.One;
            if (value.CompareTo(maxLayer) > 0) {
                maxLayer = value;
            }
        }
        return maxLayer;
    }

    private static Quantity<FltX> LayerHeight<T>(T item, IReadOnlyList<T> bottomItems, int depth)
        where T : class, IStackPlacement {
        if (depth >= MaxDepth) {
            return new Quantity<FltX>(FltX.Zero, item.Height.Unit);
        }
        IReadOnlyList<T> direct = DirectBottomItems(bottomItems);
        List<T> indirect = bottomItems.Where(candidate => !direct.Contains(candidate)).ToList();

        var maxHeight = new Quantity<FltX>(FltX.Zero, item.Height.Unit);
        foreach (T bottom in direct) {
            if (!string.Equals(bottom.ItemType, item.ItemType, System.StringComparison.Ordinal)) {
                continue;
            }
            List<T> under = indirect.Where(bottom.OverlapsBottom).ToList();
            var value = under.Count > 0 ? HeightSum(bottom.Height, LayerHeight(bottom, under, depth + 1)) : bottom.Height;
            if (value.Value.ToFlt64().CompareTo(maxHeight.Value.ToFlt64()) > 0) {
                maxHeight = value;
            }
        }
        return maxHeight;
    }

    private static Quantity<FltX> HeightSum(Quantity<FltX> left, Quantity<FltX> right) =>
        left.Value.ToFlt64().ToDouble() >= 0 && right.Unit.Equals(left.Unit)
            ? new Quantity<FltX>(new FltX(left.Value.ToFlt64().ToDouble() + right.Value.ToFlt64().ToDouble()), left.Unit)
            : left;
}