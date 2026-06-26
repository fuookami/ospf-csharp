#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// 线性 IIS 模型 / Linear IIS (Irreducible Infeasible Subsystem) model.
/// </summary>
/// <param name="Variables">IIS 涉及的变量 / Variables involved in the IIS</param>
/// <param name="GuardConstraints">保护约束（不可行来源）/ Guard constraints (infeasibility source)</param>
/// <param name="Name">模型名称 / Model name</param>
public sealed record LinearIisModel(
    IReadOnlyList<ModelViewVariable> Variables,
    LinearConstraintBatch? GuardConstraints,
    string Name) {
    /// <summary>
    /// 是否为松弛可行（无需保护约束）/ Whether relaxed-feasible (no guard constraints needed).
    /// </summary>
    public bool RelaxedFeasible => GuardConstraints is null;
}

/// <summary>
/// 线性 IIS 计算扩展 / Linear IIS computation extensions.
/// </summary>
public static class LinearIisCompute {
    /// <summary>
    /// 计算线性模型的 IIS / Compute IIS for a linear model.
    /// </summary>
    /// <remarks>
    /// 此方法为存桩实现。完整的 IIS 计算需要访问求解器内部模型成员
    /// （弹性模型创建、松弛变量、约束过滤等），这些成员在当前公共 API 中不可用。
    /// 请使用求解器适配器内置的 IIS 支持。
    /// This method is a stub. Full IIS computation requires access to solver-internal
    /// model members (elastic model creation, slack variables, constraint filtering),
    /// which are not available through the current public API.
    /// Use the solver adapter's built-in IIS support instead.
    /// </remarks>
    /// <param name="solver">线性求解器实例 / Linear solver instance</param>
    /// <param name="config">IIS 计算配置 / IIS computation configuration</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>IIS 模型或错误 / IIS model or error</returns>
    public static Task<Result<LinearIisModel, ErrorCode, Error<ErrorCode>>> ComputeIIS(
        this IAbstractLinearSolver solver,
        IisConfig config,
        CancellationToken cancellationToken = default) {

        return Task.FromResult(
            Results.Failed<LinearIisModel>(
                new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    "IIS computation requires solver-specific implementation. Use the solver adapter's built-in IIS support.")));
    }
}
