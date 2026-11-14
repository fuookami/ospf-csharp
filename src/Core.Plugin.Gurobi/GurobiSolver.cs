#nullable enable

using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Gurobi;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 求解器抽象基类，提供环境初始化、求解和状态分析的通用实现。
/// Gurobi solver abstract base, owns the native GRBEnv/GRBModel lifecycle.
/// </summary>
public abstract class GurobiSolver : IDisposable {
    private bool _disposed;
    private GRBEnv? _env;
    private GRBModel? _grbModel;
    private SolverStatus _status;
    private int _nativeStatus;
    private bool _hasSolution;
    private bool _cancelledDuringSolve;
    private bool _terminationFailed;

    /// <summary>后端解算器身份 / Backend solver identity.</summary>
    internal GurobiBackendIdentity Identity { get; }

    /// <summary>Gurobi 环境 / Gurobi environment (lazy-initialized).</summary>
    protected GRBEnv Env => _env ?? throw new InvalidOperationException("GRBEnv not initialized.");
    /// <summary>Gurobi 模型 / Gurobi model (lazy-initialized).</summary>
    protected GRBModel GrbModel => _grbModel ?? throw new InvalidOperationException("GRBModel not initialized.");
    /// <summary>求解状态 / Solver status.</summary>
    protected SolverStatus Status { get => _status; set => _status = value; }
    /// <summary>原生解是否存在 / Whether a native solution exists.</summary>
    protected bool HasSolution => _hasSolution;

    /// <summary>创建共享 Gurobi 实现 / Create a shared Gurobi implementation.</summary>
    protected GurobiSolver() : this(GurobiBackendIdentity.Gurobi) { }

    internal GurobiSolver(GurobiBackendIdentity identity) {
        Identity = identity;
    }

    /// <summary>
    /// 关闭 Gurobi 模型和环境，释放资源。
    /// Close Gurobi model and environment, release native resources.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }

        try { _grbModel?.Dispose(); } catch { /* swallow on dispose */ }
        try { _env?.Dispose(); } catch { /* swallow on dispose */ }
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 使用远程服务器初始化 Gurobi 环境和模型。
    /// Initialize Gurobi environment and model using a remote compute server.
    /// </summary>
    protected async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InitAsync(
        string server, string password, TimeSpan connectionTime, string name,
        CreatingEnvironmentFunction? callBack = null) {
        try {
            _env = new GRBEnv(true);
            Env.Set(GRB.IntParam.ServerTimeout, (int)connectionTime.TotalSeconds);
            Env.Set(GRB.DoubleParam.CSQueueTimeout, connectionTime.TotalSeconds);
            Env.Set(GRB.StringParam.ComputeServer, server);
            Env.Set(GRB.StringParam.ServerPassword, password);
            if (callBack is { } cb) {
                Result<Success, ErrorCode, Error<ErrorCode>> r = cb(Env);
                if (r is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return r;
                }
            }
            Env.Start();
            _grbModel = new GRBModel(Env);
            GrbModel.Set(GRB.StringAttr.ModelName, name);
            return Results.OkInstance;
        }
        catch (GRBException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost)); }
        finally { await Task.CompletedTask.ConfigureAwait(false); }
    }

    /// <summary>
    /// 使用本地环境初始化 Gurobi 模型。
    /// Initialize Gurobi model using a local environment.
    /// </summary>
    protected async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InitAsync(string name, CreatingEnvironmentFunction? callBack = null) {
        try {
            _env = new GRBEnv();
            if (callBack is { } cb) {
                Result<Success, ErrorCode, Error<ErrorCode>> r = cb(Env);
                if (r is Failed<Success, ErrorCode, Error<ErrorCode>> or Fatal<Success, ErrorCode, Error<ErrorCode>>) {
                    return r;
                }
            }
            _grbModel = new GRBModel(Env);
            GrbModel.Set(GRB.StringAttr.ModelName, name);
            return Results.OkInstance;
        }
        catch (GRBException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineEnvironmentLost)); }
        finally { await Task.CompletedTask.ConfigureAwait(false); }
    }

    /// <summary>执行 Gurobi 求解 / Execute Gurobi optimize.</summary>
    protected Result<bool, ErrorCode, Error<ErrorCode>> Solve(CancellationToken cancellationToken) {
        int active = 1;
        int cancelled = 0;
        Exception? terminationError = null;
        using CancellationTokenRegistration registration = cancellationToken.Register(() => {
            if (Volatile.Read(ref active) == 0) {
                return;
            }
            Interlocked.Exchange(ref cancelled, 1);
            try {
                GrbModel.Terminate();
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
                Interlocked.CompareExchange(ref terminationError, error, null);
            }
        });
        if (cancellationToken.IsCancellationRequested) {
            Volatile.Write(ref active, 0);
            return Results.Ok(false);
        }
        try {
            GrbModel.Optimize();
        }
        catch (GRBException error) {
            return Results.Failed<bool>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, error.Message));
        }
        catch {
            return Results.Failed<bool>(new Err<ErrorCode>(ErrorCode.OREngineTerminated));
        }
        finally {
            Volatile.Write(ref active, 0);
            registration.Dispose();
        }
        _cancelledDuringSolve = Volatile.Read(ref cancelled) != 0;
        _terminationFailed = terminationError is not null;
        return Results.Ok(true);
    }

    /// <summary>分析 Gurobi 求解状态 / Analyze Gurobi status into SolverStatus.</summary>
    protected async Task<Result<Success, ErrorCode, Error<ErrorCode>>> AnalyzeStatusAsync() {
        try {
            _nativeStatus = GrbModel.Get(GRB.IntAttr.Status);
            // 零列最优模型可能没有 SolCount；其解向量为空。 / An optimal zero-column model can have no SolCount; its solution vector is empty.
            _hasSolution = GrbModel.Get(GRB.IntAttr.SolCount) > 0 ||
                (_nativeStatus == GRB.Status.OPTIMAL && GrbModel.Get(GRB.IntAttr.NumVars) == 0);
            _status = _nativeStatus switch {
                var s when s == GRB.Status.OPTIMAL => SolverStatus.Optimal,
                var s when s == GRB.Status.INFEASIBLE => SolverStatus.Infeasible,
                var s when s == GRB.Status.UNBOUNDED => SolverStatus.Unbounded,
                var s when s == GRB.Status.INF_OR_UNBD => SolverStatus.InfeasibleOrUnbounded,
                _ => _hasSolution
                    ? SolverStatus.Feasible
                    : SolverStatus.SolvingException,
            };
            return Results.OkInstance;
        }
        catch (GRBException e) { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, e.Message)); }
        catch { return new Failed<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)); }
        finally { await Task.CompletedTask.ConfigureAwait(false); }
    }

    /// <summary>读取原生报告统计 / Read native report statistics.</summary>
    protected SolveStatistics Statistics(Flt64? objective) {
        double seconds = GrbModel.Get(GRB.DoubleAttr.Runtime);
        bool isMip = GrbModel.Get(GRB.IntAttr.IsMIP) != 0;
        double? bound = isMip ? TryGetDouble(GRB.DoubleAttr.ObjBound) :
            _nativeStatus == GRB.Status.OPTIMAL ? objective?.ToDouble() : null;
        double? gap = isMip && _hasSolution ? TryGetDouble(GRB.DoubleAttr.MIPGap) :
            !isMip && _nativeStatus == GRB.Status.OPTIMAL ? 0.0 : null;
        return new SolveStatistics(
            SolveTime: double.IsFinite(seconds) && seconds >= 0 && seconds < TimeSpan.MaxValue.TotalSeconds
                ? TimeSpan.FromSeconds(seconds) : null,
            Iterations: ToCount(TryGetDouble(GRB.DoubleAttr.IterCount)),
            Nodes: isMip ? ToCount(TryGetDouble(GRB.DoubleAttr.NodeCount)) : null,
            BestBound: ToFiniteValue(bound),
            Gap: ToFiniteValue(gap));
    }

    /// <summary>组装原生求解报告 / Assemble a native solve report.</summary>
    protected Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Report(
        SolveSolution<Flt64>? solution,
        SolveStatistics statistics,
        bool quadratic,
        SolverConfig config) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = GurobiStatusMapping.Create(
            nativeStatus: _nativeStatus,
            solution: solution,
            statistics: statistics,
            cancelledDuringSolve: _cancelledDuringSolve);
        if (result is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return result;
        }
        (SolverProvenance provenance, SolveFingerprints fingerprints) =
            GurobiReportSupport.Metadata(config, quadratic, Identity, NativeVersion);
        SolveDiagnostics<Flt64> diagnostics = _terminationFailed
            ? ok.Value.Diagnostics with {
                Warnings = Array.AsReadOnly(ok.Value.Diagnostics.Warnings.Concat([new SolveIssue(
                    Code: "gurobi-terminate-failed",
                    Category: SolveIssueCategory.Backend,
                    Message: "Gurobi 原生终止请求失败 / Gurobi native termination request failed")]).ToArray()),
            }
            : ok.Value.Diagnostics;
        return Results.Ok(ok.Value with {
            Diagnostics = diagnostics,
            Provenance = provenance,
            Fingerprints = fingerprints,
        });
    }

    private double? TryGetDouble(GRB.DoubleAttr attribute) {
        try { return GrbModel.Get(attribute); }
        catch (GRBException) { return null; }
    }

    private static ulong? ToCount(double? value) =>
        value is { } count && double.IsFinite(count) && count >= 0 && count < 18446744073709551616.0
            ? (ulong)count : null;

    private static Flt64? ToFiniteValue(double? value) =>
        value is { } number && double.IsFinite(number) && System.Math.Abs(number) < GRB.INFINITY
            ? new Flt64(number) : null;

    internal static readonly string NativeVersion =
        typeof(GRBEnv).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
        ?? typeof(GRBEnv).Assembly.GetName().Version?.ToString(3)
        ?? "unknown";
}

internal static class GurobiStatusMapping {
    internal static Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> Create(
        int nativeStatus,
        SolveSolution<Flt64>? solution,
        SolveStatistics statistics,
        bool cancelledDuringSolve) {
        if (solution is not null &&
            (solution.Objective is not { } objective || !double.IsFinite(objective.ToDouble()) ||
                solution.Values.Values.Any(value => !double.IsFinite(value.ToDouble())))) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "Gurobi 解或目标值不是有限数 / Gurobi solution or objective is not finite"));
        }
        if ((nativeStatus == GRB.Status.OPTIMAL && solution is null) ||
            ((nativeStatus == GRB.Status.INFEASIBLE || nativeStatus == GRB.Status.INF_OR_UNBD) && solution is not null)) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "Gurobi 状态与解数量矛盾 / Gurobi status contradicts the solution count"));
        }
        ProblemStatus problem = nativeStatus switch {
            var status when status == GRB.Status.INFEASIBLE => ProblemStatus.Infeasible,
            var status when status == GRB.Status.UNBOUNDED => ProblemStatus.Unbounded,
            var status when status == GRB.Status.INF_OR_UNBD => ProblemStatus.InfeasibleOrUnbounded,
            _ when solution is not null => ProblemStatus.Feasible,
            _ => ProblemStatus.Unknown,
        };
        TerminationReason termination = nativeStatus switch {
            var status when status == GRB.Status.OPTIMAL || status == GRB.Status.INFEASIBLE ||
                status == GRB.Status.UNBOUNDED || status == GRB.Status.INF_OR_UNBD => TerminationReason.Completed,
            var status when status == GRB.Status.TIME_LIMIT => TerminationReason.TimeLimit,
            var status when status == GRB.Status.NODE_LIMIT => TerminationReason.NodeLimit,
            var status when status == GRB.Status.ITERATION_LIMIT => TerminationReason.IterationLimit,
            var status when status == GRB.Status.SOLUTION_LIMIT => TerminationReason.SolutionLimit,
            var status when status == GRB.Status.USER_OBJ_LIMIT || status == GRB.Status.CUTOFF => TerminationReason.ObjectiveLimit,
            var status when status == GRB.Status.INTERRUPTED => cancelledDuringSolve ? TerminationReason.Cancelled : TerminationReason.Interrupted,
            var status when status == GRB.Status.NUMERIC || status == GRB.Status.SUBOPTIMAL => TerminationReason.NumericalFailure,
            _ => TerminationReason.BackendFailure,
        };
        bool completed = termination == TerminationReason.Completed;
        return Results.Ok(new SolveReport<Flt64> {
            ProblemStatus = problem,
            TerminationReason = termination,
            SolutionPresence = solution is null ? SolutionPresence.None :
                nativeStatus == GRB.Status.OPTIMAL ? SolutionPresence.Optimal : SolutionPresence.Incumbent,
            Solution = solution,
            Proof = new SolveProof(completed ? ProofStatus.Claimed : ProofStatus.None, completed ? "backend-status" : null),
            Statistics = statistics,
            Diagnostics = termination == TerminationReason.BackendFailure
                ? new SolveDiagnostics<Flt64>(Errors: [new SolveIssue(
                    Code: "gurobi-native-status",
                    Category: SolveIssueCategory.Backend,
                    Message: $"未识别的 Gurobi 状态：{nativeStatus} / Unrecognized Gurobi status: {nativeStatus}")])
                : new SolveDiagnostics<Flt64>(),
        });
    }
}
