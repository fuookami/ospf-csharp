// 舱位类型码与舱位类型集合 / Position type codes and the position type set.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 舱位类型码 / Position type code.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `demo2/domain/stowage/model/Position.kt` 的 `PositionTypeCode`（7 项）。**名字必须一一对上**：
/// `enabled()` 的十几条规则全是"这个码在不在集合里"，改一个名字就等于悄悄关掉一条规则。
///
/// Mirrors the baseline `PositionTypeCode` member for member (7 entries). **The names must match exactly**: a dozen of `enabled()`'s rules are "is this code in the set", so renaming
/// one silently disables a rule.
/// </remarks>
public enum PositionTypeCode
{
    /// <summary>禁止放空 / Empty stowage is forbidden</summary>
    EmptyForbidden,

    /// <summary>厌恶放空 / Empty stowage is discouraged</summary>
    EmptyHated,

    /// <summary>禁止装载空集装器 / Empty item is forbidden</summary>
    EmptyItemForbidden,

    /// <summary>禁止装载易碎货物 / Crush-sensitive item is forbidden</summary>
    CrushItemForbidden,

    /// <summary>禁止装载刚硬性货物 / Stiff cargo is forbidden</summary>
    StiffCargoForbidden,

    /// <summary>航材指定舱位 / AOG or MAT appointed position</summary>
    AOGMATAppointed,

    /// <summary>普通散货指定舱位 / Normal bulk appointed position</summary>
    NormalBulkAppointed,
}

/// <summary>
/// 舱位类型，包含一组舱位类型码 / Position type, a set of position type codes.
/// </summary>
public sealed class PositionType
{
    /// <summary>空类型 / The empty type.</summary>
    public static PositionType Empty { get; } = new(Array.Empty<PositionTypeCode>());

    /// <summary>类型码集合 / The set of type codes.</summary>
    public IReadOnlySet<PositionTypeCode> Codes { get; }

    /// <summary>
    /// 构造舱位类型 / Construct a position type.
    /// </summary>
    /// <param name="codes">类型码集合 / The type codes.</param>
    /// <exception cref="ArgumentNullException">集合为空引用 / The set is null.</exception>
    public PositionType(IEnumerable<PositionTypeCode> codes) {
        ArgumentNullException.ThrowIfNull(codes);
        Codes = codes.ToHashSet();
    }

    /// <summary>
    /// 是否包含给定类型码 / Whether the given code is present.
    /// </summary>
    /// <param name="code">类型码 / The code.</param>
    /// <returns>是否包含 / Whether present.</returns>
    public bool Contains(PositionTypeCode code) => Codes.Contains(code);
}
