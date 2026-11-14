#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
namespace Fuookami.Ospf.Framework.Solver;

internal static class CombinatorialDumpSupport {
    internal static async Task<Result<T, ErrorCode, Error<ErrorCode>>> DumpAsync<TSolver, T>(
        IReadOnlyList<Func<TSolver>> factories,
        Func<TSolver, Task<Result<T, ErrorCode, Error<ErrorCode>>>> dump,
        ILogger logger,
        CancellationToken cancellationToken)
        where TSolver : class {
        if (factories.Count == 0) {
            return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.SolverNotFound,
                "没有可用的组合求解器 / No combinatorial solver was available"));
        }
        try {
            TSolver? solver = factories[0]();
            if (solver is null) {
                return InvalidResult<T>();
            }
            Result<T, ErrorCode, Error<ErrorCode>>? result = await dump(solver);
            return result ?? InvalidResult<T>();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ApplicationStopped,
                "模型转储已取消 / Model dump was cancelled"));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            ParallelCombinatorialReportSupport.LogFailure(logger, error);
            return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException,
                "组合求解器转储失败 / Combinatorial solver dump failed"));
        }
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> InvalidResult<T>() =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException,
            "组合求解器转储结果无效 / Invalid combinatorial solver dump result"));
}
