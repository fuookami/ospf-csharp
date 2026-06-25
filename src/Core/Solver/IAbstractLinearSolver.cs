#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

namespace Fuookami.Ospf.Core.Solver
{
    /// <summary>
    /// 线性求解器的抽象接口，定义求解、异步求解和泛型求解能力。
    /// Abstract interface for linear solvers: solve, async solve, and generic solve.
    /// </summary>
    public interface IAbstractLinearSolver
    {
        /// <summary>求解器名称 / Solver name</summary>
        string Name { get; }

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
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            var result = await InvokeAsync(model, solvingStatusCallBack, cancellationToken);
            return result switch
            {
                Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok =>
                    Results.Ok(ok.Value.ConvertTo<V>(v => converter.IntoValue(v))),
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
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            var result = await InvokeAsync(model, solutionAmount, solvingStatusCallBack, cancellationToken);
            return result switch
            {
                Ok<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> ok =>
                    Results.Ok((ok.Value.Output.ConvertTo<V>(v => converter.IntoValue(v)),
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
        Task<LinearTriadModel> DumpAsync(LinearMechanismModel<Flt64> model, CancellationToken cancellationToken = default);

        /// <summary>转储线性元模型为机制模型 / Dump linear meta model to mechanism model</summary>
        Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMetaModel<Flt64> model,
            RegistrationStatusCallBack? registrationStatusCallBack,
            MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
            CancellationToken cancellationToken = default);
    }
}
