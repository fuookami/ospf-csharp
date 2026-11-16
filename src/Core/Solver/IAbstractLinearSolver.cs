#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 线性求解器的抽象接口，定义求解、异步求解和泛型求解能力。
/// Abstract interface for linear solvers: solve, async solve, and generic solve.
/// </summary>
public interface IAbstractLinearSolver {
    /// <summary>求解器名称 / Solver name</summary>
    string Name { get; }

    /// <summary>
    /// 后端能力描述符，供不可行诊断编排器过滤不可用的分析器。
    /// Backend descriptor used by the infeasibility diagnostic orchestrator to filter unavailable analyzers.
    /// </summary>
    /// <remarks>默认不提供描述符，此时编排器只做分析器自身的能力声明检查。 / Absent by default, and then the orchestrator only checks each analyzer's own capability declaration.</remarks>
    SolverDescriptor? Descriptor => null;

    /// <summary>
    /// 返回该后端可用的不可行诊断分析器，按声明顺序参与编排器选择。
    /// Return the infeasibility diagnostic analyzers offered by this backend, in declaration order for the orchestrator.
    /// </summary>
    /// <remarks>默认没有后端分析器，旧入口因此不能物化 IIS。 / No backend analyzers by default, so legacy entries cannot materialize an IIS.</remarks>
    /// <param name="config">诊断配置 / Diagnostic configuration.</param>
    /// <returns>候选分析器 / Candidate analyzers.</returns>
    IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> DiagnosticAnalyzers(IisConfig config) =>
        Array.Empty<IInfeasibilityAnalyzer<ILinearTriadModelView>>();

    /// <summary>求解并保留后端终态 / Solve and retain the backend termination state.</summary>
    /// <remarks>过渡期间，尚未迁移的后端返回不支持；此入口不保证后台执行。 / Unimplemented backends return unsupported; this entry does not guarantee background execution.</remarks>
    /// <param name="model">线性矩阵模型 / Linear matrix model.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>报告或错误 / Report or error.</returns>
    Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
            ErrorCode.Other,
            "求解器尚未实现统一报告 / Solver does not yet implement unified reports")));

    /// <summary>求解并保留解池与后端终态 / Solve and retain the solution pool and backend state.</summary>
    /// <remarks>过渡期间，尚未迁移的后端返回不支持；此入口不保证后台执行。 / Unimplemented backends return unsupported; this entry does not guarantee background execution.</remarks>
    /// <param name="model">线性矩阵模型 / Linear matrix model.</param>
    /// <param name="solutionAmount">解池容量 / Solution pool capacity.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>报告或错误 / Report or error.</returns>
    Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
            ErrorCode.Other,
            "求解器尚未实现统一报告解池 / Solver does not yet implement unified report pools")));

    /// <summary>求解并安全转换报告数值 / Solve and safely convert report values.</summary>
    /// <typeparam name="V">目标数值类型 / Target numeric type.</typeparam>
    /// <param name="model">线性矩阵模型 / Linear matrix model.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>转换后的报告或错误 / Converted report or error.</returns>
    async Task<Result<SolveReport<V>, ErrorCode, Error<ErrorCode>>> SolveReportAsync<V>(
        ILinearTriadModelView model,
        IIntoValue<V> converter,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await InvokeReportAsync(
            model: model,
            solvingStatusCallBack: solvingStatusCallBack,
            cancellationToken: cancellationToken);
        return result switch {
            Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok => ok.Value.ConvertTo(converter),
            Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed => Results.Failed<SolveReport<V>>(failed.Error),
            Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> fatal => new Fatal<SolveReport<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<SolveReport<V>>(new Err<ErrorCode>(ErrorCode.Other, "未知求解结果 / Unknown solve result")),
        };
    }

    // ===== Solver boundary (was Kotlin `suspend operator fun invoke`) =====

    /// <summary>
    /// 求解线性模型（阻塞边界）。
    /// Solve linear model (blocking boundary).
    /// </summary>
    Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 求解线性模型并启用 IIS 诊断（阻塞）。
    /// Solve linear model with IIS diagnostics (blocking).
    /// </summary>
    Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearTriadModel model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        IisConfig? iisConfig = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 求解线性模型获取多个解（阻塞边界）。
    /// Solve linear model for multiple solutions (blocking boundary).
    /// </summary>
    Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        ILinearTriadModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default);

    // ===== Async-over-Task (was Kotlin non-suspend `solveAsync` returning CompletableFuture) =====

    /// <summary>
    /// 异步求解线性模型。
    /// Solve linear model asynchronously (fire-and-forget Task).
    /// </summary>
    Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ILinearTriadModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => InvokeAsync(model, solvingStatusCallBack, cancellationToken), cancellationToken);

    // ===== Generic primary entry (was Kotlin `suspend fun <V> solve`) =====

    /// <summary>
    /// 泛型求解线性模型。
    /// Solve linear model with generic value conversion.
    /// </summary>
    async Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveAsync<V>(
        ILinearTriadModelView model,
        IIntoValue<V> converter,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = await InvokeAsync(model, solvingStatusCallBack, cancellationToken);
        return result switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok(ok.Value.ConvertTo<V>(converter.IntoValue)),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f =>
                Results.Failed<FeasibleSolverOutput<V>>(f.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fat =>
                new Fatal<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>(fat.Errors),
            _ => throw new InvalidOperationException()
        };
    }

    /// <summary>
    /// 泛型求解线性模型获取多个解。
    /// Solve linear model with generic value conversion for multiple solutions.
    /// </summary>
    async Task<Result<(FeasibleSolverOutput<V> Output, List<Solution<V>> Solutions), ErrorCode, Error<ErrorCode>>> SolveAsync<V>(
        ILinearTriadModelView model,
        ulong solutionAmount,
        IIntoValue<V> converter,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> result = await InvokeAsync(model, solutionAmount, solvingStatusCallBack, cancellationToken);
        return result switch {
            Ok<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok((ok.Value.Output.ConvertTo<V>(converter.IntoValue),
                            ok.Value.Solutions.Select(s => new Solution<V>(s.Select(converter.IntoValue).ToList())).ToList())),
            Failed<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> f =>
                Results.Failed<(FeasibleSolverOutput<V>, List<Solution<V>>)>(f.Error),
            Fatal<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> fat =>
                new Fatal<(FeasibleSolverOutput<V>, List<Solution<V>>), ErrorCode, Error<ErrorCode>>(fat.Errors),
            _ => throw new InvalidOperationException()
        };
    }

    // ===== Dump (was Kotlin `suspend fun dump`) =====

    /// <summary>转储线性机制模型为三元组模型 / Dump linear mechanism model to triad model</summary>
    Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default);

    /// <summary>转储线性元模型为机制模型 / Dump linear meta model to mechanism model</summary>
    Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        LinearMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default);
}
