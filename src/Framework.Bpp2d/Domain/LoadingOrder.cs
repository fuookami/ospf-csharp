// 装箱上下文与装载顺序 / Packing context and loading order.
// 对应。
// And.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 装箱上下文 / Packing context.
/// </summary>
/// <param name="RestItems">剩余未装箱物品及其件数 / Remaining unpacked items with their amounts.</param>
/// <param name="RestMaterials">剩余未分配物料及其件数 / Remaining unassigned materials with their amounts.</param>
/// <param name="Info">附加键值信息 / Additional key-value information.</param>
public sealed record PackingContext(
    IReadOnlyDictionary<ItemId, ulong>? RestItems = null,
    IReadOnlyDictionary<MaterialKey, ulong>? RestMaterials = null,
    IReadOnlyDictionary<string, string>? Info = null) {
    /// <summary>剩余未装箱物品，永不为 null / Remaining items, never null.</summary>
    public IReadOnlyDictionary<ItemId, ulong> RestItemMap =>
        RestItems ?? new Dictionary<ItemId, ulong>();

    /// <summary>剩余未分配物料，永不为 null / Remaining materials, never null.</summary>
    public IReadOnlyDictionary<MaterialKey, ulong> RestMaterialMap =>
        RestMaterials ?? new Dictionary<MaterialKey, ulong>();

    /// <summary>附加键值信息，永不为 null / Additional info, never null.</summary>
    public IReadOnlyDictionary<string, string> ExtraInfo =>
        Info ?? new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// 装载顺序计算器 / Loading order calculator.
/// </summary>
/// <remarks>
/// 按「先下后上、先左后右」给出装载顺序，二维排序键为 y 坐标再 x 坐标，与 BLA 的落点优先序一致。
/// 量纲不可比时**不参与比较**（视为相等）并落到下一个键：一个无法比较的坐标不该让整趟排序失败，但也不该
/// 悄悄决定顺序，所以最后还有包围面积作为稳定兜底。
///
/// Orders items "bottom first, then left to right"; the 2D sort key is y then x, matching BLA's placement priority.
/// Incomparable dimensions are **skipped as equal** and fall through to the next key: one incomparable coordinate
/// should not fail the whole sort, nor silently decide the order, so the bounding area is the final stable tie-break.
/// </remarks>
public static class LoadingOrderCalculator {
    /// <summary>
    /// 计算放置的装载顺序 / Compute the loading order of placements.
    /// </summary>
    /// <param name="placements">放置列表 / Placement list.</param>
    /// <returns>与输入同序的装载序号，首个装载的序号为 1 / Loading orders aligned with the input, the first being 1.</returns>
    public static IReadOnlyList<ulong> Invoke(
        IReadOnlyList<Placement2<FltX>> placements) {
        ArgumentNullException.ThrowIfNull(placements);
        var indexed = placements
            .Select((placement, index) => (Placement: placement, Index: index)).ToList();
        indexed.Sort((left, right) => {
            int comparison = Compare(left.Placement, right.Placement);
            return comparison != 0 ? comparison : left.Index.CompareTo(right.Index);
        });
        // 用**下标**回填而不是用放置对象做字典键：`Placement2` 是含抽象基类与量值记录的深层值对象，
        // C# record 的哈希/相等在这种结构上既昂贵又容易因某一层没有值语义而错判，直接按下标对齐没有这个风险。
        // Fill by **index** rather than keying a dictionary by the placement: `Placement2` is a deep value object over
        // an abstract base and quantity records, and C# record hashing/equality over that is both expensive and easy to
        // get wrong when one layer lacks value semantics. Index alignment has no such risk.
        ulong[] orders = new ulong[placements.Count];
        for (int index = 0; index < indexed.Count; index++) {
            orders[indexed[index].Index] = (ulong)(index + 1);
        }
        return orders;
    }

    /// <summary>
    /// 比较两个放置的装载优先级：先 y 再 x，最后按包围面积大者优先 / Compare by y, then x, then larger bounding
    /// area first.
    /// </summary>
    /// <param name="lhs">左放置 / Left placement.</param>
    /// <param name="rhs">右放置 / Right placement.</param>
    /// <returns>负数表示左放置先装载 / Negative means the left loads first.</returns>
    private static int Compare(Placement2<FltX> lhs, Placement2<FltX> rhs) {
        int byY = OrderToComparison(QuantityCore.OrdOrNull(lhs.Y, rhs.Y));
        if (byY != 0) {
            return byY;
        }
        int byX = OrderToComparison(QuantityCore.OrdOrNull(lhs.X, rhs.X));
        if (byX != 0) {
            return byX;
        }
        // 面积大者优先：同样靠左下时，先放大件通常能给后续留出更连续的余料。
        // Larger area first: among equally bottom-left placements, starting with the big piece usually leaves more
        // contiguous remnants.
        return OrderToComparison(
            QuantityCore.OrdOrNull(lhs.Shape.BoundingArea, rhs.Shape.BoundingArea)) switch {
            > 0 => -1,
            < 0 => 1,
            _ => 0,
        };
    }

    /// <summary>把三路比较转成排序器需要的整数 / Convert a three-way order into a comparer integer.</summary>
    /// <param name="order">比较结果 / The order.</param>
    /// <returns>负数、零或正数 / Negative, zero or positive.</returns>
    private static int OrderToComparison(Order? order) => order switch {
        Order.Less => -1,
        Order.Greater => 1,
        _ => 0,
    };
}
