#nullable enable

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 需求口径标签，仅用于决策/约束/分析标识 / Demand mode label for decision/constraint/analyzer semantics only
/// </summary>
public enum DemandMode {
    /// <summary>卷数需求 / Roll demand.</summary>
    Roll,

    /// <summary>重量需求 / Weight demand.</summary>
    Weight,

    /// <summary>张数需求 / Sheet demand.</summary>
    Sheet
}
