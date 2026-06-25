#nullable enable

namespace Fuookami.Ospf.Core.Solver.Value;
/// <summary>
/// 求解值转换策略 / Solve value conversion policy.
/// </summary>
public enum SolveValueConversionPolicy {
    /// <summary>严格模式（不允许精度损失）/ Strict mode (no precision loss allowed)</summary>
    Strict,
    /// <summary>允许四舍五入 / Allow rounding</summary>
    AllowRounding,
}
