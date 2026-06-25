#nullable enable

using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Framework.Solver.Remote.Port;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
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
        bool exportCheckpointEachRound = true) {
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
            start = await _executionPort.ResumeAsync(payload, snapshot, taskId, sliceId, nodeId, tenantId);
        }
        else {
            start = await _executionPort.StartAsync(payload, taskId, sliceId, nodeId, tenantId);
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
            Result<SliceResult, ErrorCode, Error<ErrorCode>> slice = await _executionPort.AwaitSliceEndAsync(handle, quantum);
            if (slice is not Ok<SliceResult, ErrorCode, Error<ErrorCode>> sliceOk) {
                return slice.Map(_ => default(SolveResult)!);
            }
            totalElapsed += sliceOk.Value.Elapsed;

            if (exportCheckpointEachRound) {
                Result<ObjectRef?, ErrorCode, Error<ErrorCode>> cp = await _executionPort.ExportCheckpointAsync(handle);
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
                    Optimal: (sliceOk.Value.Gap ?? Flt64.One).ToDouble() <= 0.0,
                    ObjectiveValue: sliceOk.Value.ObjectiveValue,
                    Gap: sliceOk.Value.Gap,
                    Elapsed: totalElapsed,
                    CheckpointRef: latestCheckpoint,
                    Message: sliceOk.Value.Message);
                break;
            }
        }

        await _executionPort.StopAsync(handle);

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
}
