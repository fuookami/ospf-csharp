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
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 二次求解器的抽象接口，定义求解、异步求解和泛型求解能力。
/// Abstract interface for quadratic solvers: solve, async solve, and generic solve.
/// </summary>
public interface IAbstractQuadraticSolver {
    /// <summary>求解器名称 / Solver name</summary>
    string Name { get; }

    /// <summary>
    /// 返回该后端可用的不可行诊断分析器，按声明顺序参与编排器选择。
    /// Return the infeasibility diagnostic analyzers offered by this backend, in declaration order for the orchestrator.
    /// </summary>
    /// <remarks>默认没有后端分析器，旧入口因此不能物化 IIS。 / No backend analyzers by default, so legacy entries cannot materialize an IIS.</remarks>
    /// <param name="config">诊断配置 / Diagnostic configuration.</param>
    /// <returns>候选分析器 / Candidate analyzers.</returns>
    IReadOnlyList<IInfeasibilityAnalyzer<IQuadraticTetradModelView>> DiagnosticAnalyzers(IisConfig config) =>
        Array.Empty<IInfeasibilityAnalyzer<IQuadraticTetradModelView>>();

    /// <summary>求解并保留后端终态 / Solve and retain the backend termination state.</summary>
    /// <remarks>过渡期间，尚未迁移的后端返回不支持；此入口不保证后台执行。 / Unimplemented backends return unsupported; this entry does not guarantee background execution.</remarks>
    /// <param name="model">二次矩阵模型 / Quadratic matrix model.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>报告或错误 / Report or error.</returns>
    Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
            ErrorCode.Other,
            "求解器尚未实现统一报告 / Solver does not yet implement unified reports")));

    /// <summary>求解并保留解池与后端终态 / Solve and retain the solution pool and backend state.</summary>
    /// <remarks>过渡期间，尚未迁移的后端返回不支持；此入口不保证后台执行。 / Unimplemented backends return unsupported; this entry does not guarantee background execution.</remarks>
    /// <param name="model">二次矩阵模型 / Quadratic matrix model.</param>
    /// <param name="solutionAmount">解池容量 / Solution pool capacity.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>报告或错误 / Report or error.</returns>
    Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
            ErrorCode.Other,
            "求解器尚未实现统一报告解池 / Solver does not yet implement unified report pools")));

    /// <summary>求解并安全转换报告数值 / Solve and safely convert report values.</summary>
    /// <typeparam name="V">目标数值类型 / Target numeric type.</typeparam>
    /// <param name="model">二次矩阵模型 / Quadratic matrix model.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>转换后的报告或错误 / Converted report or error.</returns>
    async Task<Result<SolveReport<V>, ErrorCode, Error<ErrorCode>>> SolveReportAsync<V>(
        IQuadraticTetradModelView model,
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

    /// <summary>求解二次模型（阻塞边界）/ Solve quadratic model (blocking boundary)</summary>
    Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default);

    /// <summary>求解二次模型并启用 IIS 诊断 / Solve quadratic model with IIS diagnostics</summary>
    Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack,
        IisConfig? iisConfig,
        CancellationToken cancellationToken = default);

    /// <summary>求解二次模型获取多个解 / Solve quadratic model for multiple solutions</summary>
    Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IQuadraticTetradModelView model,
        ulong solutionAmount,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default);

    /// <summary>异步求解二次模型 / Solve quadratic model asynchronously</summary>
    Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveAsync(
        IQuadraticTetradModelView model,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => InvokeAsync(model, solvingStatusCallBack, cancellationToken), cancellationToken);

    /// <summary>泛型求解二次模型 / Solve quadratic model with generic value conversion</summary>
    async Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveAsync<V>(
        IQuadraticTetradModelView model,
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

    /// <summary>转储二次机制模型为四元组模型 / Dump quadratic mechanism model to tetrad model</summary>
    /// <param name="model">机制模型 / Mechanism model.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>矩阵模型或转换错误 / Matrix model or a conversion error.</returns>
    Task<Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMechanismModel<Flt64> model,
        CancellationToken cancellationToken = default);

    /// <summary>转储二次元模型为机制模型 / Dump quadratic meta model to mechanism model</summary>
    Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default);
}
