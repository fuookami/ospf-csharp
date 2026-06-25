#nullable enable

using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Solver.Remote.Port;
/// <summary>
/// 求解器执行端口接口 / Solver execution port interface.
/// </summary>
public interface ISolverExecutionPort {
    /// <summary>启动求解执行 / Start solve execution.</summary>
    Task<Result<ExecutionHandle, ErrorCode, Error<ErrorCode>>> StartAsync(
        SolvePayload payload,
        TaskId taskId,
        SliceId sliceId,
        NodeId nodeId,
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>恢复求解执行 / Resume solve execution.</summary>
    Task<Result<ExecutionHandle, ErrorCode, Error<ErrorCode>>> ResumeAsync(
        SolvePayload payload,
        ObjectRef snapshot,
        TaskId taskId,
        SliceId sliceId,
        NodeId nodeId,
        TenantId tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>等待切片结束 / Await slice end.</summary>
    Task<Result<SliceResult, ErrorCode, Error<ErrorCode>>> AwaitSliceEndAsync(
        ExecutionHandle handle,
        TimeSpan quantum,
        CancellationToken cancellationToken = default);

    /// <summary>导出检查点 / Export checkpoint.</summary>
    Task<Result<ObjectRef?, ErrorCode, Error<ErrorCode>>> ExportCheckpointAsync(
        ExecutionHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>获取最终结果 / Fetch final result.</summary>
    Task<Result<SolveResult?, ErrorCode, Error<ErrorCode>>> FetchFinalResultAsync(
        ExecutionHandle handle,
        CancellationToken cancellationToken = default);

    /// <summary>停止执行 / Stop execution.</summary>
    Task<Result<bool, ErrorCode, Error<ErrorCode>>> StopAsync(
        ExecutionHandle handle,
        CancellationToken cancellationToken = default);
}
