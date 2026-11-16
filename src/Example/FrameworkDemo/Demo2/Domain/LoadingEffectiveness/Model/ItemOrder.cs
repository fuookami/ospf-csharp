#nullable enable

using System;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;

/// <summary>
/// 货物作业顺序 / The item's operational sequence.
/// </summary>
/// <remarks>
/// 对应 `Item.order` 的三个时刻（`loading_effectiveness` 侧读取）。三个时刻**各自可空**，
/// 而不是合成一个"阶段"枚举——就是三个独立可空字段，且**它们的出现顺序有业务含义**：
/// 站坪 → 上车 → 复重。
///
/// 三条限制按这个顺序做**前缀判定**：
///
/// 1. `ItemAheadLoadLimit`：只要任一货物满足"已站坪 / 已站坪或已上车 / 三者任一"就生效。
/// 2. `ItemReweighNeededLimit`：**还没有复重时刻**的货物才进目标。
///
/// 三个字段**必须保持可空**：把它们改成非空布尔会让"尚未发生"与"发生在第 0 秒"混为一谈。
///
/// Three nullable instants. They are **independently nullable** rather than collapsed into one stage enum — three
/// separate nullable fields and **their order carries business meaning**: hardstand → car board → reweighed. The two relevant limits then test them as **prefixes**:
/// `ItemAheadLoadLimit` fires when any item satisfies "already on the hardstand", "already on the hardstand or boarded", or "any of the three"; and `ItemReweighNeededLimit` puts
/// into the objective only items **without a reweigh instant**. All three fields **must stay nullable**: making them non-nullable booleans would conflate "has not happened" with
/// "happened at time zero".
/// </remarks>
public sealed record ItemOrder
{
    /// <summary>站坪时刻 / The hardstand instant</summary>
    public uint? Hardstand { get; init; }

    /// <summary>上车时刻 / The car-board instant</summary>
    public uint? CarBoard { get; init; }

    /// <summary>复重时刻 / The reweigh instant</summary>
    public uint? Reweighed { get; init; }

    /// <summary>
    /// 装载顺序 / The loading sequence.
    /// </summary>
    /// <remarks>
    /// `SequentialLoading` 读的是 `order.order`，与上面三个时刻**分开**：时刻是"什么时候做的"，顺序是"该按什么次序装"。
    ///
    /// `SequentialLoading` reads `order.order`, **separate** from the three instants above: the instants say when something happened, the sequence says what order it should be loaded in.
    /// </remarks>
    public byte? Sequence { get; init; }
}
