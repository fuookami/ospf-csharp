#nullable enable

namespace Fuookami.Ospf.Framework.Csp1d.Infrastructure;
/// <summary>
/// 切割方案产品排序方式 / Cutting plan product ordering strategy.
/// </summary>
public enum CuttingPlanProductOrder {
    /// <summary>升序 / Ascending.</summary>
    Asc,
    /// <summary>降序 / Descending.</summary>
    Desc,
    /// <summary>用户自定义 / User defined.</summary>
    UserDefined
}
