#nullable enable

using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Framework.Solver.Remote.Port;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Solver.Remote.Client;
/// <summary>
/// 远程求解器客户端 / Remote solver client.
/// </summary>
public sealed class RemoteSolverClient {
    private readonly ISolverExecutionPort _executionPort;

    public RemoteSolverClient(ISolverExecutionPort executionPort) {
        _executionPort = executionPort;
    }

    /// <summary>
    /// 执行远程求解 / Execute remote solve.
    /// </summary>
    public async Task<Result<SolveResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        SolvePayload payload,
        TaskId taskId,
        SliceId sliceId,
        NodeId nodeId,
        TenantId tenantId,
        TimeSpan quantum,
        ulong maxRounds = 64,
        bool exportCheckpointEachRound = true,
        CancellationToken cancellationToken = default) {
        if (quantum <= TimeSpan.Zero) {
            return new Failed<SolveResult, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "quantum must be positive."));
        }
        if (maxRounds <= 0) {
            return new Failed<SolveResult, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "maxRounds must be positive."));
        }

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> start;
        if (payload.SnapshotRef is { } snapshot) {
            start = await _executionPort.ResumeAsync(
                payload, snapshot, taskId, sliceId, nodeId, tenantId, cancellationToken);
        }
        else {
            start = await _executionPort.StartAsync(
                payload, taskId, sliceId, nodeId, tenantId, cancellationToken);
        }
        if (start is not Ok<ExecutionHandle, ErrorCode, Error<ErrorCode>> okHandle) {
            return start.Map(_ => default(SolveResult)!);
        }
        ExecutionHandle handle = okHandle.Value;

        TimeSpan totalElapsed = TimeSpan.Zero;
        ObjectRef? latestCheckpoint = payload.SnapshotRef;
        ulong rounds = 0UL;
        SolveResult? finalResult = null;

        while (rounds < maxRounds) {
            rounds++;
            Result<SliceResult, ErrorCode, Error<ErrorCode>> slice =
                await _executionPort.AwaitSliceEndAsync(handle, quantum, cancellationToken);
            if (slice is not Ok<SliceResult, ErrorCode, Error<ErrorCode>> sliceOk) {
                return slice.Map(_ => default(SolveResult)!);
            }
            totalElapsed += sliceOk.Value.Elapsed;

            if (exportCheckpointEachRound) {
                Result<ObjectRef?, ErrorCode, Error<ErrorCode>> cp =
                    await _executionPort.ExportCheckpointAsync(handle, cancellationToken);
                if (cp is not Ok<ObjectRef?, ErrorCode, Error<ErrorCode>> cpOk) {
                    return cp.Map(_ => default(SolveResult)!);
                }
                latestCheckpoint = cpOk.Value ?? latestCheckpoint;
            }

            if (sliceOk.Value.Completed) {
                Result<SolveResult?, ErrorCode, Error<ErrorCode>> fetched = await _executionPort.FetchFinalResultAsync(handle);
                if (fetched is not Ok<SolveResult?, ErrorCode, Error<ErrorCode>> fetchedOk) {
                    return fetched.Map(_ => default(SolveResult)!);
                }
                finalResult = fetchedOk.Value ?? new SolveResult(
                    Feasible: sliceOk.Value.Feasible,
                    Optimal: false,
                    ObjectiveValue: sliceOk.Value.ObjectiveValue,
                    Gap: sliceOk.Value.Gap,
                    Elapsed: totalElapsed,
                    CheckpointRef: latestCheckpoint,
                    Message: sliceOk.Value.Message);
                break;
            }
        }

        await _executionPort.StopAsync(handle, cancellationToken);

        return finalResult is { } r
            ? new Ok<SolveResult, ErrorCode, Error<ErrorCode>>(r)
            : new Failed<SolveResult, ErrorCode, Error<ErrorCode>>(new ExErr<ErrorCode, object>(
                ErrorCode.ApplicationFailed,
                $"Remote solve does not complete within maxRounds={maxRounds} (taskId={taskId}, sliceId={sliceId}).",
                new RemoteSolverFailureDetail(
                    Code: RemoteSolverErrorCode.RemoteSolveNotCompletedWithinMaxRounds,
                    Message: $"Remote solve does not complete within maxRounds={maxRounds}.",
                    Metadata: new() { ["taskId"] = taskId.Value, ["sliceId"] = sliceId.Value, ["maxRounds"] = maxRounds.ToString() },
                    TaskId: taskId.Value,
                    SliceId: sliceId.Value)));
    }

    /// <summary>
    /// 求解并返回**未压缩**的终态切片结果 / Solve and return the **unreduced** terminal slice result.
    /// </summary>
    /// <remarks>
    /// 与 <see cref="SolveAsync"/> 的唯一差别是终态处理：那条路径把切片结果压缩成字段精简的
    /// <see cref="SolveResult"/>，而精简类型没有 <c>ResultRef</c>／<c>ArtifactDigest</c>／<c>ProblemStatus</c>
    /// ／<c>TerminationReason</c>／<c>SolutionPresence</c>／<c>ProofStatus</c>／<c>RunId</c>，因此任何需要
    /// **物化稳定解或组装审计报告**的调用方都无法基于它工作。本方法直接返回
    /// <c>AwaitSliceEndAsync</c> 给出的富切片结果——它已带上终态工件引用与摘要。
    ///
    /// 这不是新增一套采集逻辑：观测路径仍然只有 `GET /api/v1/tasks/{taskId}`，只是不再提前丢掉字段。
    /// The only difference from <see cref="SolveAsync"/> is terminal handling: that path reduces the slice
    /// result to the small <see cref="SolveResult"/>, which lacks `ResultRef`/`ArtifactDigest`/
    /// `ProblemStatus`/`TerminationReason`/`SolutionPresence`/`ProofStatus`/`RunId`, so any caller that must
    /// **materialize a stable solution or assemble an audit report** cannot work from it. This method returns
    /// the rich slice result from <c>AwaitSliceEndAsync</c> directly, which already carries the
    /// terminal artifact reference and digest. This adds no second observation path — it is still only
    /// `GET /api/v1/tasks/{taskId}` — it merely stops discarding fields early.
    /// </remarks>
    /// <param name="payload">求解载荷 / Solve payload.</param>
    /// <param name="taskId">任务标识 / Task identifier.</param>
    /// <param name="sliceId">切片标识 / Slice identifier.</param>
    /// <param name="nodeId">节点标识 / Node identifier.</param>
    /// <param name="tenantId">租户标识 / Tenant identifier.</param>
    /// <param name="quantum">切片时间片 / Slice quantum.</param>
    /// <param name="maxRounds">最大轮数 / Maximum rounds.</param>
    /// <param name="exportCheckpointEachRound">每轮是否导出检查点 / Whether to export a checkpoint each round.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>终态富切片结果 / The terminal rich slice result.</returns>
    public async Task<Result<SliceResult, ErrorCode, Error<ErrorCode>>> SolveSliceAsync(
        SolvePayload payload,
        TaskId taskId,
        SliceId sliceId,
        NodeId nodeId,
        TenantId tenantId,
        TimeSpan quantum,
        ulong maxRounds = 64,
        bool exportCheckpointEachRound = true,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(payload);
        if (quantum <= TimeSpan.Zero) {
            return new Failed<SliceResult, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "quantum must be positive."));
        }
        if (maxRounds <= 0) {
            return new Failed<SliceResult, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.IllegalArgument, "maxRounds must be positive."));
        }

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> start;
        if (payload.SnapshotRef is { } snapshot) {
            start = await _executionPort.ResumeAsync(
                payload, snapshot, taskId, sliceId, nodeId, tenantId, cancellationToken);
        }
        else {
            start = await _executionPort.StartAsync(
                payload, taskId, sliceId, nodeId, tenantId, cancellationToken);
        }
        if (start is not Ok<ExecutionHandle, ErrorCode, Error<ErrorCode>> okHandle) {
            return start.Map(_ => default(SliceResult)!);
        }
        ExecutionHandle handle = okHandle.Value;

        TimeSpan totalElapsed = TimeSpan.Zero;
        ulong rounds = 0UL;
        SliceResult? terminal = null;
        while (rounds < maxRounds) {
            rounds++;
            Result<SliceResult, ErrorCode, Error<ErrorCode>> slice =
                await _executionPort.AwaitSliceEndAsync(handle, quantum, cancellationToken);
            if (slice is not Ok<SliceResult, ErrorCode, Error<ErrorCode>> sliceOk) {
                return slice;
            }
            totalElapsed += sliceOk.Value.Elapsed;

            if (exportCheckpointEachRound) {
                Result<ObjectRef?, ErrorCode, Error<ErrorCode>> checkpoint =
                    await _executionPort.ExportCheckpointAsync(handle, cancellationToken);
                if (checkpoint is not Ok<ObjectRef?, ErrorCode, Error<ErrorCode>> checkpointOk) {
                    return checkpoint.Map(_ => default(SliceResult)!);
                }
            }

            if (sliceOk.Value.Completed) {
                // 累计耗时由本循环维护，切片自身只带单轮耗时。
                // The cumulative elapsed time is maintained here; the slice only carries one round's.
                terminal = sliceOk.Value with { Elapsed = totalElapsed };
                break;
            }
        }

        await _executionPort.StopAsync(handle, cancellationToken);

        return terminal is { } result
            ? new Ok<SliceResult, ErrorCode, Error<ErrorCode>>(result)
            : new Failed<SliceResult, ErrorCode, Error<ErrorCode>>(new ExErr<ErrorCode, object>(
                ErrorCode.ApplicationFailed,
                $"Remote solve does not complete within maxRounds={maxRounds} (taskId={taskId}, sliceId={sliceId}).",
                new RemoteSolverFailureDetail(
                    Code: RemoteSolverErrorCode.RemoteSolveNotCompletedWithinMaxRounds,
                    Message: $"Remote solve does not complete within maxRounds={maxRounds}.",
                    Metadata: new() { ["taskId"] = taskId.Value, ["sliceId"] = sliceId.Value, ["maxRounds"] = maxRounds.ToString() },
                    TaskId: taskId.Value,
                    SliceId: sliceId.Value)));
    }
}
