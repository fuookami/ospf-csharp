#nullable enable

using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// 二次 IIS 计算扩展 / Quadratic IIS computation extensions.
/// </summary>
public static class QuadraticIisCompute {
    /// <summary>
    /// 计算二次模型的 IIS / Compute IIS for a quadratic model.
    /// </summary>
    /// <remarks>
    /// 此方法为存桩实现。完整的二次 IIS 计算需要访问求解器内部模型成员
    /// （弹性模型创建、松弛变量、约束过滤等），这些成员在当前公共 API 中不可用。
    /// 请使用求解器适配器内置的 IIS 支持。
    /// This method is a stub. Full quadratic IIS computation requires access to solver-internal
    /// model members (elastic model creation, slack variables, constraint filtering),
    /// which are not available through the current public API.
    /// Use the solver adapter's built-in IIS support instead.
    /// </remarks>
    /// <param name="solver">二次求解器实例 / Quadratic solver instance</param>
    /// <param name="config">IIS 计算配置 / IIS computation configuration</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>线性 IIS 模型或错误 / Linear IIS model or error</returns>
    public static Task<Result<LinearIisModel, ErrorCode, Error<ErrorCode>>> ComputeIIS(
        this IAbstractQuadraticSolver solver,
        IisConfig config,
        CancellationToken cancellationToken = default) {

        return Task.FromResult(
            Results.Failed<LinearIisModel>(
                new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    "Quadratic IIS computation requires solver-specific implementation. Use the solver adapter's built-in IIS support.")));
    }
}
