// 分区列生成的分派 / Partition column-generation dispatch.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Application;
using Fuookami.Ospf.Framework.Bpp.Domain.PartitionLoading;
using Fuookami.Ospf.Framework.Bpp1d.Application;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

/// <summary>
/// 把分区列生成接进 demo2 的分派 / Wire partition column generation into demo2's dispatch.
/// </summary>
/// <remarks>
/// **三块拼起来就是整条链**，一块都不新写：
///
/// | 块 | 来自 |
/// | --- | --- |
/// | 注册 | `Stowage.LoadingContext.Register(model)`（一直都在） |
/// | 分区配置 | `PartitionConfiguration.Build` |
/// | 循环 | `PartitionColumnGenerationApplicationService`（框架层，一直都在） |
///
/// **的拒绝理由是错的**：它说"注册与分区配置没接起来"。注册早就在，分区配置在 补齐。
/// 剩下的只是**没人把这两块交给那个服务**——就是这个文件在做的事。
///
/// **模型不是新建的**：分派拿到的 `LinearMetaModel` 必须是**已经注册过七个域的那一张**，
/// 因为列生成要在完整模型上做投影。新建一张空模型会得到一个"列生成跑完、主问题零行"的结果。
///
/// **Three pieces clicked together, none of them new**: the registration has always been there, the partition configuration landed, and the loop lives in the
/// framework. **the earlier refusal reason was wrong** — it claimed "the registration and the partition configuration are not wired"; the registration always was, and the
/// configuration arrived. What was missing is exactly what this file does: hand the two to that service. **The model is not a new one**: the dispatch must be
/// handed the `LinearMetaModel` that **already has the seven domains registered**, because column generation projects against the full model. A fresh empty model
/// yields "column generation finished, master problem has zero rows".
/// </remarks>
internal static class PartitionColumnGenerationRunner
{
    /// <summary>
    /// 跑一次根节点列生成并把结果翻成响应 / Run root column generation once and turn the outcome into a response.
    /// </summary>
    /// <param name="request">请求 / The request.</param>
    /// <param name="aggregation">配载聚合（已装配） / The assembled stowage aggregation.</param>
    /// <param name="registered">已注册的完整模型 / The full model, already registered.</param>
    /// <param name="solver">列生成求解器 / The column-generation solver.</param>
    /// <param name="notes">备注 / Notes.</param>
    /// <returns>响应与渲染 / The response and the render.</returns>
    public static (ResponseDTO Response, RenderDTO? Render) Run(
        RequestDTO request,
        StowageAggregation aggregation,
        LinearMetaModel<Flt64> registered,
        IColumnGenerationSolver solver,
        List<string> notes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(aggregation);
        ArgumentNullException.ThrowIfNull(registered);
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(notes);

        if (aggregation.Stowage.LoadingContext is not { } loading)
        {
            notes.Add("partition_column_generation_failed: the loading context is not initialised");
            return (ResponseDTO.NoSolution("LoadingContextNotInitialised", notes), null);
        }

        Result<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>> configured =
            PartitionConfiguration.Build(request, aggregation);
        if (configured is not Ok<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>> configuredOk)
        {
            notes.Add("partition_column_generation_failed: " + ((Failed<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>>)configured).Error);
            return (ResponseDTO.NoSolution("PartitionConfigurationFailed", notes), null);
        }

        var registration = new RegisteredDiscreteLoading<Flt64>(loading, registered);
        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            new PartitionColumnGenerationApplicationService<Flt64>(solver)
                .SolveAsync(
                    registration,
                    configuredOk.Value.Partitions,
                    config: null,
                    extraDecisions: configuredOk.Value.Decisions)
                .GetAwaiter()
                .GetResult();

        if (solved is not Ok<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> solvedOk)
        {
            Error<ErrorCode> error = ((Failed<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Error;
            notes.Add("partition_column_generation_failed: " + error);
            return (ResponseDTO.NoSolution("PartitionColumnGenerationFailed", notes), null);
        }

        PartitionColumnGenerationOutcome<Flt64> outcome = solvedOk.Value;
        // 备注格式照做：`partition_columns={n};iterations={m};root_lp_converged={bool}`。
        // The note follows the code verbatim.
        notes.Add($"partition_columns={outcome.ColumnCount};iterations={outcome.Trace.Count};root_lp_converged={outcome.LpConverged}");
        notes.Add($"partition_solver={solver.Name}");

        // **没收敛也要说清楚**：`LpConverged` 为假时交回去的整数解**不是全局最优**，
        // 状态写 `RestrictedFeasible`，别让人以为拿的是最优解。
        //
        // **A non-converged run says so**: when `LpConverged` is false the integer solution is **not globally optimal**, so the status reads
        // `RestrictedFeasible` rather than letting anyone read the answer as optimal.
        string status = outcome.LpConverged ? outcome.Loading.Status : "RestrictedFeasible";
        if (!outcome.LpConverged)
        {
            notes.Add("root_lp_not_converged: the result is restricted, not a proven optimum");
        }

        return (ResponseDTO.Optimal(outcome.Loading.Objective.ToDouble(), [], notes), null);
    }

    /// <summary>
    /// **给分派用的一行判断**：请求要不要走列生成 / The one-liner the dispatch uses: does the request want column generation?
    /// </summary>
    /// <param name="request">请求 / The request.</param>
    /// <returns>是否要走 / Whether to take that path.</returns>
    public static bool Wants(RequestDTO request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.SolvePolicy?.PartitionColumnGeneration == true;
    }
}
