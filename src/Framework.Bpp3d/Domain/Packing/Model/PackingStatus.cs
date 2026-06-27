#nullable enable

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;

/// <summary>装箱状态 / Packing status.</summary>
public enum PackingStatus {
    /// <summary>最优 / Optimal.</summary>
    Optimal,
    /// <summary>不可行 / Infeasible.</summary>
    Infeasible,
    /// <summary>未知 / Unknown.</summary>
    Unknown
}
