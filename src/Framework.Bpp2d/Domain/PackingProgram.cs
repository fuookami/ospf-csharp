// 物品域：排样程序 / Item domain: packing programs.
//
// **这里不含 `ItemContext`**：它的 `aggregate` 依赖`Aggregation`（需求 + 板材上限的**入参**容器），而 C# 侧同名
// 概念 `PackingAggregation` 是**出参**结果类型（装 `PackedSheet` 的容器），两者语义不同。实现 `ItemContext` 需要先补齐
// `Aggregation` 输入模型，留下批，不在这里硬套一个语义错位的类型。
// **`ItemContext` is not covered here**: its `aggregate` needs its `Aggregation` (an **input** container of
// demands and sheet limits), whereas the C# type of that name is an **output** result holding `PackedSheet`s — different
// semantics. Porting `ItemContext` requires the `Aggregation` input model first, so it waits; not force a
// semantically mismatched type.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain;

/// <summary>
/// 排样程序：描述一种在板材上重复排布的形状组合 / Packing program: a repeated shape combination on a sheet.
/// </summary>
/// <remarks>
/// `Amount` 在是**构造时算好的值**而非属性：程序一旦建成就不可变，件数不该随访问而重算，也不该因为
/// `PerRowAmount` 被改而与实际排布脱节。
/// In the code `Amount` is **computed once at construction** rather than a property: a program is immutable once built,
/// its amount should not be recomputed on each access, nor drift from the real layout if `PerRowAmount` changed.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">程序编号 / Program identifier.</param>
/// <param name="Item">参与排布的物品 / Item taking part in the layout.</param>
/// <param name="PerRowAmount">每行件数 / Amount per row.</param>
/// <param name="RowAmount">行数 / Row count.</param>
/// <param name="Rotated">是否旋转 90 度排布 / Whether the layout is rotated by 90 degrees.</param>
public sealed record PackingProgram<V>(
    string Id,
    IAbstractItem<V> Item,
    ulong PerRowAmount,
    ulong RowAmount,
    bool Rotated = false) where V : struct, IFloatingNumber<V> {
    /// <summary>程序总件数 / Total amount of the program.</summary>
    public ulong Amount { get; } = PerRowAmount * RowAmount;

    /// <inheritdoc/>
    public override string ToString() => $"{Id}: {PerRowAmount}×{RowAmount}";
}

/// <summary>
/// 排样程序构造 / Packing program construction.
/// </summary>
public static class PackingProgramFactory {
    /// <summary>
    /// 由物品生成基础排样程序 / Build the base packing programs of an item.
    /// </summary>
    /// <remarks>
    /// 对允许旋转的矩形件同时给出不旋转与旋转两种程序，供候选生成对比；不允许旋转时只给不旋转的一种。**不判断形状是否
    /// 圆形**——圆形件的旋转无意义，但由调用方在候选筛选阶段排除，此处多做一次判断只会让两条路径的规则分叉。
    /// Rotatable rectangles yield both upright and rotated programs so candidate generation can compare them; a
    /// non-rotatable item yields only the upright one. **Shape circularity is not checked here** — rotating a circle is
    /// meaningless, but callers already exclude circles while filtering candidates, and a second check here would only
    /// fork the rules between two paths.
    /// </remarks>
    /// <typeparam name="V">数值类型 / Numeric type.</typeparam>
    /// <param name="item">物品 / Item.</param>
    /// <param name="perRowAmount">每行件数 / Amount per row.</param>
    /// <param name="rowAmount">行数 / Row count.</param>
    /// <returns>排样程序列表 / Packing program list.</returns>
    public static IReadOnlyList<PackingProgram<V>> PackingProgramsOf<V>(
        IAbstractItem<V> item,
        ulong perRowAmount,
        ulong rowAmount) where V : struct, IFloatingNumber<V> {
        ArgumentNullException.ThrowIfNull(item);

        var upright = new PackingProgram<V>(
            $"{item.Id}-upright", item, perRowAmount, rowAmount, Rotated: false);
        if (!item.AllowRotate) {
            return new[] { upright };
        }
        return new[] {
            upright,
            new PackingProgram<V>($"{item.Id}-rotated", item, perRowAmount, rowAmount, Rotated: true),
        };
    }
}
