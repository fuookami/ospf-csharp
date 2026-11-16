#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using NumUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 装载禁忌限制 / Stowage taboo restrictions.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `demo2/domain/stowage/model/Position.kt` 的 `PositionStowageTaboo`：一组"某 ULD 代码 + 某编号区间 + 商品说明"
/// 的禁忌，命中即禁止在该舱位装载该集装器。
///
/// 编号是**从集装器名称里切出来的第 4—8 个字符**（基线的 `item.uld.name.substring(3, 8)`）。这不是随手写的：
/// ULD 编号的标准写法是"三字母代码 + 4 位数字 + 1 位字母"，比如 `AKE12345MU`——所以位置 3..8 正好是那 4 位数字
/// （外加一位字母被 `toULong` 丢弃前要先截断）。切片失败（名称太短或不是数字）时基线会抛异常，C# 这里返回 `null`（不命中），
/// 因为**一条读不懂的编号不应该让整份配载崩掉**，而"不命中"与"名字不合规范"在禁忌检查上是同一个结果。
///
/// Mirrors the baseline's `PositionStowageTaboo`: a list of "ULD code + number range + commodity description" restrictions; a hit forbids that ULD at that position. The number is
/// **characters 4..8 of the ULD name** (the baseline's `item.uld.name.substring(3, 8)`). That is deliberate: a ULD name is "three-letter code + 4 digits + 1 letter", e.g.
/// `AKE12345MU`, so positions 3..8 are exactly those four digits. Where the baseline would throw on a short or non-numeric name, this returns `null` (no hit): an unreadable number
/// should not bring the whole stowage run down, and "no hit" is the same outcome as "the name is malformed" for taboo purposes.
/// </remarks>
public sealed class PositionStowageTaboo
{
    /// <summary>禁忌范围 / The taboo ranges</summary>
    public IReadOnlyList<TabooRange> Taboos { get; }

    /// <summary>
    /// 构造禁忌限制 / Construct the taboo restrictions.
    /// </summary>
    /// <param name="taboos">禁忌范围 / The taboo ranges.</param>
    public PositionStowageTaboo(IReadOnlyList<TabooRange> taboos) {
        Taboos = taboos;
    }

    /// <summary>无禁忌 / No taboo</summary>
    public static PositionStowageTaboo Empty { get; } = new(Array.Empty<TabooRange>());

    /// <summary>
    /// 一条禁忌范围 / One taboo range.
    /// </summary>
    /// <param name="Uld">ULD 代码 / The ULD code.</param>
    /// <param name="NoRange">禁止的编号范围 / The prohibited number range.</param>
    /// <param name="Commodity">商品说明 / The commodity description.</param>
    public sealed record TabooRange(ULDCode Uld, ValueRange<NumUInt64> NoRange, string Commodity);

    /// <summary>
    /// 取命中该货物的禁忌 / The taboo that hits this item, if any.
    /// </summary>
    /// <param name="item">货物 / The cargo item.</param>
    /// <returns>命中的禁忌；不命中为 `null` / The hit, or `null`.</returns>
    public TabooRange? this[Item item] => Find(item);

    /// <summary>
    /// 取命中该货物的禁忌 / The taboo that hits this item, if any.
    /// </summary>
    /// <param name="item">货物 / The cargo item.</param>
    /// <returns>命中的禁忌；不命中为 `null` / The hit, or `null`.</returns>
    public TabooRange? Find(Item item) {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Uld?.Code is not { } code) {
            return null;
        }
        ulong? number = UldNumber(item.Uld.Name);
        if (number is null) {
            return null;
        }
        foreach (TabooRange taboo in Taboos) {
            if (taboo.Uld == code && taboo.NoRange.Contains(new NumUInt64(number.Value))) {
                return taboo;
            }
        }
        return null;
    }

    /// <summary>
    /// 从集装器名称里取编号 / Extract the ULD number from the name.
    /// </summary>
    /// <param name="name">集装器名称 / The ULD name.</param>
    /// <returns>编号；名称不合规范时为 `null` / The number, or `null` when the name does not fit.</returns>
    private static ulong? UldNumber(string name) {
        if (name.Length < 8) {
            return null;
        }
        string slice = name.Substring(3, 5);
        if (!ulong.TryParse(slice, out ulong number)) {
            return null;
        }
        return number;
    }
}
