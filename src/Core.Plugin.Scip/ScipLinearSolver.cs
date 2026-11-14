#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
namespace Fuookami.Ospf.Core.Plugin.Scip;

/// <summary>SCIP 线性求解器 / SCIP linear solver.</summary>
public sealed class ScipLinearSolver : ILinearSolver {
    /// <inheritdoc/>
    public string Name => "scip";
    /// <inheritdoc/>
    public SolverDescriptor? Descriptor { get; } = new(
        SolverId: "scip",
        BackendName: "SCIP",
        Capabilities: ScipLinearNativeSolver.LinearCapabilities);
    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    /// <summary>创建 SCIP 线性求解器 / Create a SCIP linear solver.</summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public ScipLinearSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) => InvokeReportCore(
            model: model,
            solutionAmount: 0,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    private Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportCore(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack,
        CancellationToken cancellationToken,
        ScipDualTargets? duals = null,
        SolverConfig? config = null,
        ScipLinearSnapshot? captured = null) {
        // 诊断运行下发覆盖配置，并复用已经捕获的快照，避免二次读取模型。 / Diagnostic runs override the configuration and reuse an already captured snapshot.
        SolverConfig effective = config ?? Config;
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromResult(Results.Ok(ScipLinearNativeSolver.Cancelled()));
        }
        if (solutionAmount > int.MaxValue) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "解池容量超出 SCIP 整数范围 / Solution pool capacity exceeds the SCIP integer range")));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> validation = effective.ValidateRuntime();
        if (validation is Failed<Success, ErrorCode, Error<ErrorCode>> invalid) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(invalid.Error));
        }
        if (validation is Fatal<Success, ErrorCode, Error<ErrorCode>> invalidMany) {
            return Task.FromResult<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(
                new Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>(invalidMany.Errors));
        }
        IBackendConfiguration? backend = effective.BackendConfiguration;
        if (backend is not null and not ScipSolverConfig) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"SCIP 求解器不能使用 {backend.Type} backend 配置 / " +
                    $"SCIP solver cannot use backend configuration {backend.Type}")));
        }
        if (backend is ScipSolverConfig { Deterministic: true } && effective.NativeThreadCount > 1) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "SCIP deterministic 配置必须使用单线程 / SCIP deterministic configuration requires one thread")));
        }
        if (backend is ScipSolverConfig { RandomSeed: { } seed } && (seed < int.MinValue || seed > int.MaxValue)) {
            return Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "SCIP randomSeed 超出原生整数范围 / SCIP randomSeed exceeds the native integer range")));
        }
        Result<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> snapshot = captured is { } preCaptured
            ? Results.Ok(preCaptured)
            : ScipLinearSnapshot.Capture(model, cancellationToken);
        if (snapshot is Failed<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> { Code: ErrorCode.ApplicationStopped }
            && cancellationToken.IsCancellationRequested) {
            return Task.FromResult(Results.Ok(ScipLinearNativeSolver.Cancelled()));
        }
        return Task.FromResult(snapshot.Bind(value => ScipLinearNativeSolver.Solve(
            model: value,
            config: effective,
            sourceModel: model,
            statusCallBack: solvingStatusCallBack,
            solutionAmount: (int)solutionAmount,
            cancellationToken: cancellationToken,
            duals: duals)));
    }

    /// <summary>
    /// 以 LP 模式求解并提取行对偶与 Farkas 乘子；无 incumbent 时 <see cref="ScipLpOutcome.Output"/> 为空。
    /// Solve in LP mode and extract row duals plus Farkas multipliers; the outcome output is null without an incumbent.
    /// </summary>
    /// <remarks>
    /// LP 模式会强制关闭预处理与启发式并固定单线程，因此要求模型全部列为连续类型；求解器层的失败原样传播。
    /// LP mode forces presolving and heuristics off with a single thread, so every column must be continuous; solver
    /// failures propagate unchanged.
    /// </remarks>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>含对偶信息的求解结果或错误 / Solving outcome with dual information, or an error.</returns>
    internal async Task<Result<ScipLpOutcome, ErrorCode, Error<ErrorCode>>> InvokeLpAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        var targets = new ScipDualTargets();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await InvokeReportCore(
            model: model,
            solutionAmount: 0,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken,
            duals: targets);
        if (report is Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<ScipLpOutcome>(failed.Error);
        }
        if (report is Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<ScipLpOutcome, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy = ToLegacy(report);
        return Results.Ok(new ScipLpOutcome(
            Report: report.Value,
            Output: legacy is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> output ? output.Value : null,
            RowDuals: targets.Duals));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// SCIP 只提供连续 LP 的 Farkas 分析器，后端没有原生 IIS；归约后的 IIS 模型由 Core 的 legacy 弹性与删除过滤补齐。
    /// SCIP offers only the continuous-LP Farkas analyzer and has no native IIS; the reduced IIS model comes from the Core
    /// legacy elastic and deletion filtering.
    /// </remarks>
    public IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> DiagnosticAnalyzers(IisConfig config) =>
        new IInfeasibilityAnalyzer<ILinearTriadModelView>[] { new ScipFarkasInfeasibilityAnalyzer(this, config) };

    /// <summary>
    /// 在隔离的 LP 实例中重放模型并读取逐行 Farkas 乘子；模型含整数列时直接拒绝。
    /// Replay the model in an isolated LP instance and read the per-row Farkas multipliers; models with integer columns
    /// are rejected immediately.
    /// </summary>
    /// <remarks>
    /// 诊断运行使用 <see cref="IisConfig.Time"/> 作为时间限制，并固定单线程，因为 LP 模式必须保证行序与原始约束对齐；
    /// <see cref="IisConfig.ThreadCount"/> 与 <see cref="IisConfig.BackendConfiguration"/> 因此不下发。
    /// The diagnostic run takes its time limit from <see cref="IisConfig.Time"/> and pins a single thread because LP mode
    /// must keep rows aligned with the original constraints, so <see cref="IisConfig.ThreadCount"/> and
    /// <see cref="IisConfig.BackendConfiguration"/> are not applied.
    /// </remarks>
    /// <param name="model">线性三元模型视图 / Linear triad model view.</param>
    /// <param name="iisConfig">诊断配置 / Diagnostic configuration.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>诊断结果或错误 / Diagnostic outcome or an error.</returns>
    internal async Task<Result<ScipFarkasOutcome, ErrorCode, Error<ErrorCode>>> RunFarkasDiagnosticAsync(
        ILinearTriadModelView model,
        IisConfig iisConfig,
        CancellationToken cancellationToken) {
        Result<Success, ErrorCode, Error<ErrorCode>> validation = iisConfig.ValidateRuntime();
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return ScipLpDualSupport.Propagate<ScipFarkasOutcome, Success>(
                validation,
                "SCIP 诊断配置无效 / The SCIP diagnostic configuration is invalid");
        }
        Result<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> snapshot =
            ScipLinearSnapshot.Capture(model, cancellationToken);
        if (snapshot is not Ok<ScipLinearSnapshot, ErrorCode, Error<ErrorCode>> captured) {
            return ScipLpDualSupport.Propagate<ScipFarkasOutcome, ScipLinearSnapshot>(
                snapshot,
                "SCIP 诊断快照失败 / The SCIP diagnostic snapshot failed");
        }
        // 3 = SCIP_VARTYPE_CONTINUOUS；整数列的 LP 证书不能证明整数模型不可行。
        if (captured.Value.Columns.Any(column => column.Type != 3)) {
            return Results.Failed<ScipFarkasOutcome>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "SCIP Farkas 证书仅适用于连续线性模型 / SCIP Farkas certificates require a continuous linear model"));
        }
        var targets = new ScipDualTargets { Diagnostic = true };
        var diagnostic = new SolverConfig {
            TimeLimit = iisConfig.Time,
            ThreadCount = 1UL,
            BackendConfiguration = Config.BackendConfiguration,
        };
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await InvokeReportCore(
            model: model,
            solutionAmount: 0,
            solvingStatusCallBack: null,
            cancellationToken: cancellationToken,
            duals: targets,
            config: diagnostic,
            captured: captured.Value);
        if (report is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return ScipLpDualSupport.Propagate<ScipFarkasOutcome, SolveReport<Flt64>>(
                report,
                "SCIP Farkas 诊断求解失败 / The SCIP Farkas diagnostic solve failed");
        }
        return Results.Ok(new ScipFarkasOutcome(
            Report: ok.Value,
            Snapshot: captured.Value,
            Farkas: targets.Farkas));
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        return ToLegacy(report);
    }

    internal static Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ToLegacy(
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report) {
        if (report is Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(failed.Error);
        }
        if (report is Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        SolveReport<Flt64> value = report.Value;
        if (value.ProblemStatus == ProblemStatus.Feasible && value.Solution is { Objective: { } objective } solution
            && value.Statistics.SolveTime is { } time) {
            Flt64? bound = value.Statistics.BestBound ?? (value.SolutionPresence == SolutionPresence.Optimal ? objective : null);
            Flt64? gap = value.Statistics.Gap ?? (value.SolutionPresence == SolutionPresence.Optimal ? Flt64.Zero : null);
            if (bound is not null && gap is not null) {
                return Results.Ok(new FeasibleSolverOutput<Flt64>(
                    Obj: objective,
                    Solution: solution.Values,
                    Time: time,
                    PossibleBestObj: bound.Value,
                    Gap: gap.Value,
                    Iterations: value.Statistics.Iterations,
                    NodeCount: value.Statistics.Nodes,
                    BestBound: bound,
                    MipGap: gap,
                    SolveTime: time,
                    ObjValueOrNull: objective,
                    PossibleBestObjValueOrNull: bound,
                    BestBoundValueOrNull: bound));
            }
        }
        ErrorCode code = value.ProblemStatus switch {
            ProblemStatus.Infeasible => ErrorCode.ORModelInfeasible,
            ProblemStatus.Unbounded => ErrorCode.ORModelUnbounded,
            ProblemStatus.InfeasibleOrUnbounded => ErrorCode.ORModelInfeasibleOrUnbounded,
            _ when value.TerminationReason == TerminationReason.Cancelled => ErrorCode.ApplicationStopped,
            _ => ErrorCode.OREngineSolvingException,
        };
        // **消息随码走，不再一律说"无法表达"**：不可行、无界都是 SCIP 明确报告过的结论，说"无法表达"会让调用方以为
        // 只是接口局限、问题本身未知——**而它其实已经被证明没有可行解**。只有码确实说不清时才用"无法表达"那句。
        // **The message follows the code instead of always saying "cannot represent"**: infeasibility and unboundedness are conclusions SCIP explicitly reported, and
        // claiming "cannot represent" makes the caller read it as an interface limitation with the problem unresolved — **while it has in fact been proved infeasible**.
        // The "cannot represent" wording is kept only where the code really cannot say.
        string message = code switch {
            ErrorCode.ORModelInfeasible => $"SCIP 报告不可行：{value.TerminationReason} / SCIP reports infeasible: {value.TerminationReason}",
            ErrorCode.ORModelUnbounded => $"SCIP 报告无界：{value.TerminationReason} / SCIP reports unbounded: {value.TerminationReason}",
            ErrorCode.ORModelInfeasibleOrUnbounded => $"SCIP 报告不可行或无界：{value.TerminationReason} / SCIP reports infeasible or unbounded: {value.TerminationReason}",
            ErrorCode.ApplicationStopped => $"SCIP 求解被取消：{value.TerminationReason} / SCIP solve was cancelled: {value.TerminationReason}",
            _ => $"旧接口无法表达 SCIP 报告：{value.TerminationReason} / Legacy entry cannot represent the SCIP report: {value.TerminationReason}",
        };
        return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(code, message));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 不可行时执行诊断编排，结构化证据与归约后的子系统一起写入 <see cref="LinearInfeasibleSolverOutput"/>；
    /// 诊断失败只记录错误，不改变不可行结论，此时 <c>IisAvailable</c> 为 false 且 <c>Iis</c> 退回原模型快照。
    /// Infeasible models go through the diagnostic orchestration and both the structured evidence and the reduced subsystem
    /// land in <see cref="LinearInfeasibleSolverOutput"/>; a failed diagnosis only records errors and never changes the
    /// infeasible conclusion, and then <c>IisAvailable</c> is false with <c>Iis</c> falling back to the model snapshot.
    /// </remarks>
    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default) {
        if (iisConfig is null) {
            return (await InvokeAsync(
                model: (ILinearTriadModelView)model,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken)).Map<SolverOutput>(output => output);
        }
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        if (report is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok
            || ok.Value.ProblemStatus != ProblemStatus.Infeasible) {
            return ToLegacy(report).Map<SolverOutput>(output => output);
        }
        SolveReport<Flt64> value = ok.Value;
        Result<LinearIisResult, ErrorCode, Error<ErrorCode>> computed = await this.ComputeIISAsync(
            model: model,
            config: iisConfig,
            cancellationToken: cancellationToken);
        if (computed is Ok<LinearIisResult, ErrorCode, Error<ErrorCode>> outcome) {
            return Results.Ok<SolverOutput>(new LinearInfeasibleSolverOutput(
                Status: SolverStatus.Infeasible,
                Iterations: value.Statistics.Iterations,
                NodeCount: value.Statistics.Nodes,
                SolveTime: value.Statistics.SolveTime,
                Iis: outcome.Value.Model,
                IisAvailable: true,
                Diagnostics: value.Diagnostics with {
                    InfeasibilityEvidence = outcome.Value.Evidence,
                }) {
                BestBound = value.Statistics.BestBound,
                MipGap = value.Statistics.Gap,
            });
        }
        SolveIssue[] errors = computed switch {
            Failed<LinearIisResult, ErrorCode, Error<ErrorCode>> failed => [ToIssue(failed.Error)],
            Fatal<LinearIisResult, ErrorCode, Error<ErrorCode>> fatal => [.. fatal.Errors.Select(ToIssue)],
            _ => [],
        };
        return Results.Ok<SolverOutput>(new LinearInfeasibleSolverOutput(
            Status: SolverStatus.Infeasible,
            Iterations: value.Statistics.Iterations,
            NodeCount: value.Statistics.Nodes,
            SolveTime: value.Statistics.SolveTime,
            Iis: model,
            IisAvailable: false,
            Diagnostics: value.Diagnostics with {
                Errors = [.. value.Diagnostics.Errors, .. errors],
            }) {
            BestBound = value.Statistics.BestBound,
            MipGap = value.Statistics.Gap,
        });
    }

    /// <inheritdoc/>
    /// <remarks>主解单独返回；附加解来自 SCIP 已存解且可能不足请求数量。数量不超过 1 时附加列表为空。 / The main solution is separate; alternatives come from SCIP's stored solutions and may be fewer than requested. The alternatives are empty when the amount is at most 1.</remarks>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await InvokeReportAsync(
            model: model,
            solutionAmount: solutionAmount,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy = ToLegacy(report);
        return legacy switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok => Results.Ok((
                ok.Value,
                report.Value.Solution?.Pool.Select(solution => solution.Values.ToList()).ToList() ?? new List<List<Flt64>>())),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> failed => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(failed.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fatal => new Fatal<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)),
        };
    }

    /// <inheritdoc/>
    /// <remarks>解池最多检查所请求数量的原生存储项，并排除原生主解；不保证完整枚举或全局前 N 个解。 / The pool inspects at most the requested number of native entries and excludes the native best solution; it does not guarantee enumeration or globally best N solutions.</remarks>
    public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) => InvokeReportCore(
            model: model,
            solutionAmount: solutionAmount,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default) {
        if (Config.DumpIntermediateModelBounds.GetValueOrDefault() ||
            Config.DumpIntermediateModelForceBounds.GetValueOrDefault() ||
            Config.DumpIntermediateModelConcurrent.GetValueOrDefault()) {
            return Task.FromResult(Unsupported<LinearTriadModel>(
                "暂不支持指定的中间模型转储配置 / The requested intermediate-model dump configuration is not yet supported"));
        }
        return Task.FromResult(model.ToLinearTriadModel(
            cancellationToken: cancellationToken,
            configPolicy: Config.FunctionExpansionPolicy));
    }

    /// <inheritdoc/>
    public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? rcb,
        MechanismModelDumpingStatusCallBack? dcb,
        CancellationToken cancellationToken = default) {
        if (cancellationToken.IsCancellationRequested) {
            return Task.FromResult(Results.Failed<LinearMechanismModel<Flt64>>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "模型转换已取消 / Model conversion was cancelled")));
        }
        if (cancellationToken.CanBeCanceled) {
            return Task.FromResult(Unsupported<LinearMechanismModel<Flt64>>(
                "暂不支持机制模型转储期间取消 / Cancellation during mechanism-model dumping is not yet supported"));
        }
        if (dcb is not null ||
            Config.DumpMechanismModelConcurrent.GetValueOrDefault() ||
            Config.DumpMechanismModelBlocking.GetValueOrDefault()) {
            return Task.FromResult(Unsupported<LinearMechanismModel<Flt64>>(
                "暂不支持指定的机制模型转储配置或回调 / The requested mechanism-model dump configuration or callback is not yet supported"));
        }
        return LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: model,
            concurrent: Config.DumpMechanismModelConcurrent,
            blocking: Config.DumpMechanismModelBlocking,
            registrationStatusCallBack: rcb,
            dumpingStatusCallBack: dcb);
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Unsupported<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.Other, message));

    // 诊断失败以 issue 形式记录，不影响不可行结论本身。 / A failed diagnosis is recorded as an issue and never changes the infeasible conclusion.
    private static SolveIssue ToIssue(Error<ErrorCode> error) => new(
        Code: error.Code.ToString() ?? "",
        Category: SolveIssueCategory.Backend,
        Message: error.Message);
}

/// <summary>
/// LP 模式求解结果：统一报告、可选 incumbent 输出与行对偶（与约束行同序）。
/// LP-mode solving outcome: the unified report, an optional incumbent output and row duals aligned with the constraint
/// rows.
/// </summary>
/// <param name="Report">统一求解报告 / Unified solving report.</param>
/// <param name="Output">旧接口可用的可行输出，无 incumbent 时为空 / Legacy feasible output, null without an incumbent.</param>
/// <param name="RowDuals">行对偶解 / Row dual solution.</param>
internal sealed record ScipLpOutcome(
    SolveReport<Flt64> Report,
    FeasibleSolverOutput<Flt64>? Output,
    IReadOnlyList<Flt64> RowDuals);

/// <summary>
/// Farkas 诊断运行结果：不可行报告、诊断快照与逐行 Farkas 乘子。
/// Farkas diagnostic run outcome: the infeasible report, the diagnostic snapshot and the per-row Farkas multipliers.
/// </summary>
/// <param name="Report">诊断 LP 的统一报告 / Unified report of the diagnostic LP.</param>
/// <param name="Snapshot">诊断 LP 使用的模型快照 / Model snapshot used by the diagnostic LP.</param>
/// <param name="Farkas">与原始约束行同序的 Farkas 乘子 / Farkas multipliers aligned with the original constraint rows.</param>
internal sealed record ScipFarkasOutcome(
    SolveReport<Flt64> Report,
    ScipLinearSnapshot Snapshot,
    IReadOnlyList<Flt64> Farkas);
