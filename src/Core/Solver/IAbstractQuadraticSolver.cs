#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
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
    Task<QuadraticTetradModel> DumpAsync(QuadraticMechanismModel<Flt64> model, CancellationToken cancellationToken = default);

    /// <summary>转储二次元模型为机制模型 / Dump quadratic meta model to mechanism model</summary>
    Task<Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
        QuadraticMetaModel<Flt64> model,
        RegistrationStatusCallBack? registrationStatusCallBack,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
        CancellationToken cancellationToken = default);
}
