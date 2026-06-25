#nullable enable

namespace Fuookami.Ospf.Framework.Csp1d.Application.Service;
/// <summary>
/// 最终 MILP 求解状态 / Final MILP solve status.
/// </summary>
public enum Csp1dFinalMilpStatus {
    /// <summary>尚未尝试最终 MILP / Final MILP is not attempted.</summary>
    NotAttempted,

    /// <summary>最终 MILP 已求解 / Final MILP is solved.</summary>
    Solved,

    /// <summary>最终 MILP 失败 / Final MILP failed.</summary>
    Failed
}
