#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;

/// <summary>
/// 拖车类型 / The trailer type.
/// </summary>
public enum TrailerType
{
    /// <summary>硬座 / Hardstand</summary>
    Hardstand,

    /// <summary>中转 / Transit</summary>
    Transit,

    /// <summary>仓库 / Warehouse</summary>
    Warehouse,
}

/// <summary>
/// 拖车 / A trailer.
/// </summary>
/// <remarks>
/// 与对齐。**`ToString` 只返回名字**——基线覆写了它，
/// 而这个 `ToString` 直接进约束名与中间符号名；用默认的记录输出会让名字里塞进整段类型dump。
///
/// **`ToString` returns the name alone** — the code overrides it, and this `ToString` goes straight into constraint and symbol names; the default record
/// output would paste a whole type dump into them.
/// </remarks>
public sealed record Trailer(
    TrailerType Type,
    byte Order,
    string Name,
    IReadOnlyList<Item> Items)
{
    /// <inheritdoc />
    public override string ToString() => Name;
}
