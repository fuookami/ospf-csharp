#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 用于测试的列生成求解器模拟实现。
/// Mock column generation solver for testing.
/// </summary>
/// <remarks>
/// 返回全零解和零对偶值，仅用于验证端到端流程编排。
/// Returns all-zero solutions and zero dual values; only for verifying end-to-end flow wiring.
/// </remarks>
public sealed class MockColumnGenerationSolver : IColumnGenerationSolver {
    /// <inheritdoc/>
    public string Name => "mock-test-solver";

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        return Task.FromResult(MakeFeasibleOutput());
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options) {
        return await SolveMILPAsync(
            options.SolveName(metaModel.Name),
            metaModel,
            options.ToLogModel,
            options.RegistrationStatusCallBack,
            options.SolvingStatusCallBack);
    }

    /// <inheritdoc/>
    public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        var output = MakeFeasibleOutput();
        if (output is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            var solutions = new List<List<Flt64>> { ok.Value.Solution.Values.ToList() };
            return Task.FromResult(Results.Ok<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool)>((ok.Value, solutions)));
        }
        return Task.FromResult(Results.Failed<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool)>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "MILP solve failed")));
    }

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        return Task.FromResult(Results.Failed<FeasibleSolverOutput<V>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "MILP as not supported in mock")));
    }

    /// <inheritdoc/>
    public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        var milpResult = MakeFeasibleOutput();
        if (milpResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            var duals = new Dictionary<MathConstraint, Flt64>();
            foreach (var constraint in metaModel.Constraints) {
                duals[constraint] = Flt64.Zero;
            }
            var lpResult = new IColumnGenerationSolver.LpResult(ok.Value, duals);
            return Task.FromResult(Results.Ok(lpResult));
        }
        return Task.FromResult(Results.Failed<IColumnGenerationSolver.LpResult>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "LP solve failed")));
    }

    /// <inheritdoc/>
    public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        return Task.FromResult(Results.Failed<IColumnGenerationSolver.LpResultOf<V>>(new Err<ErrorCode>(ErrorCode.SolverNotFound, "LP as not supported in mock")));
    }

    private static Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> MakeFeasibleOutput() {
        var solution = new Solution<Flt64>(new List<Flt64>());
        var output = new FeasibleSolverOutput<Flt64>(
            Obj: Flt64.Zero,
            Solution: solution,
            Time: TimeSpan.FromMilliseconds(1),
            PossibleBestObj: Flt64.Zero,
            Gap: Flt64.Zero);
        return Results.Ok(output);
    }
}
