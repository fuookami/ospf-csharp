#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.ConstraintProgramming;

/// <summary>
/// CP 到线性模型的精确降阶规模与能力门禁。
/// Size and capability gates for exact CP-to-linear lowering.
/// </summary>
/// <remarks>
/// 仅允许双向等价的线性化；无法证明等价或超出预算时返回结构化失败。
/// Only bidirectionally equivalent linearizations are allowed; unsupported or over-budget
/// transformations return a structured failure.
/// </remarks>
/// <param name="SparseDomainLimit">稀疏值域展开上限 / Sparse-domain expansion limit.</param>
/// <param name="DecompositionLimit">分解规模上限 / Decomposition size limit.</param>
/// <param name="AuxiliaryVariableLimit">辅助变量上限 / Auxiliary-variable limit.</param>
/// <param name="AllowForbiddenAssignments">是否允许禁止赋值表 / Whether forbidden tables are allowed.</param>
/// <param name="AllowCumulative">是否允许 Cumulative / Whether Cumulative is allowed.</param>
/// <param name="MaxCumulativeTimeSlots">Cumulative 最大整数时隙数 / Maximum Cumulative time slots.</param>
/// <param name="MaxCumulativeWork">Cumulative 最大 interval-slot 工作量 / Maximum Cumulative interval-slot work.</param>
public sealed record ConstraintProgrammingLoweringPolicy(
    int SparseDomainLimit = 128,
    int DecompositionLimit = 256,
    int AuxiliaryVariableLimit = 4096,
    bool AllowForbiddenAssignments = true,
    bool AllowCumulative = false,
    int MaxCumulativeTimeSlots = 4096,
    int MaxCumulativeWork = 65_536) {
    /// <summary>严格精确策略 / Strict exact policy.</summary>
    public static ConstraintProgrammingLoweringPolicy Strict { get; } = new();

    /// <summary>校验策略参数 / Validate policy parameters.</summary>
    /// <returns>成功或非法参数错误 / Success or an illegal-argument error.</returns>
    public Try Validate() {
        if (SparseDomainLimit <= 0 ||
            DecompositionLimit <= 0 ||
            AuxiliaryVariableLimit <= 0 ||
            MaxCumulativeTimeSlots <= 0 ||
            MaxCumulativeWork <= 0) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(
                    code: ErrorCode.IllegalArgument,
                    message: "CP 降阶规模上限必须为正 / CP lowering limits must be positive"));
        }
        return Results.Ok(Results.SuccessInstance);
    }
}
