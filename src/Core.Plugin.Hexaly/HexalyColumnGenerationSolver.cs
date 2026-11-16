#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Plugin.Hexaly;

/// <summary>
/// Hexaly 列生成求解器 / Hexaly column generation solver.
/// </summary>
public sealed class HexalyColumnGenerationSolver : IColumnGenerationSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "hexaly";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly HexalySolverCallBack? _callBack;

    /// <summary>
    /// 创建 Hexaly 列生成求解器 / Create a Hexaly column-generation solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">回调管理器 / Callback manager.</param>
    public HexalyColumnGenerationSolver(
        SolverConfig? config = null,
        HexalySolverCallBack? callBack = null) {
        Config = config ?? new SolverConfig();
        _callBack = callBack;
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<(LinearMechanismModel<Flt64> Mechanism, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            await BuildAsync(
                metaModel: metaModel,
                registrationStatusCallBack: registrationStatusCallBack,
                cancellationToken: cancellationToken);
        if (built is not Ok<(LinearMechanismModel<Flt64> Mechanism, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> builtOk) {
            return Propagate<FeasibleSolverOutput<Flt64>, (LinearMechanismModel<Flt64> Mechanism, LinearTriadModel Matrix)>(built);
        }

        using LinearMechanismModel<Flt64> mechanismModel = builtOk.Value.Mechanism;
        using LinearTriadModel matrix = builtOk.Value.Matrix;
        Result<Success, ErrorCode, Error<ErrorCode>> exported = await ExportIfRequested(
            metaModel: metaModel,
            model: matrix,
            name: name,
            toLogModel: toLogModel);
        if (!exported.IsOk) {
            return Propagate<FeasibleSolverOutput<Flt64>>(exported);
        }

        var solver = new HexalyLinearSolver(Config, _callBack?.Copy());
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report =
            await solver.InvokeReportAsync(
                model: matrix,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy =
            HexalyReportSupport.ToLegacy(report);
        if (legacy is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved) {
            return Propagate<FeasibleSolverOutput<Flt64>, FeasibleSolverOutput<Flt64>>(legacy);
        }
        return mechanismModel.ApplySolverSolution(
            matrix: matrix,
            solution: solved.Value.Solution.Values).Map(_ => solved.Value);
    }

    /// <inheritdoc/>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default) =>
        await SolveMILPAsync(
            name: Name,
            metaModel: metaModel,
            toLogModel: options.LogModel,
            registrationStatusCallBack: null,
            solvingStatusCallBack: options.SolveOptions?.SolvingStatusCallBack,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPBackgroundAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) =>
        Task.Run(() => SolveMILPAsync(
            name: name,
            metaModel: metaModel,
            toLogModel: toLogModel,
            registrationStatusCallBack: registrationStatusCallBack,
            solvingStatusCallBack: solvingStatusCallBack));

    /// <inheritdoc/>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        if (amount > 1) {
            return Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                ErrorCode.Other,
                "Hexaly 适配器尚未实现解池 / The Hexaly adapter does not yet implement a solution pool"));
        }
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await SolveMILPAsync(
                name: name,
                metaModel: metaModel,
                toLogModel: toLogModel,
                registrationStatusCallBack: registrationStatusCallBack,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken);
        return solved switch {
            Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok((
                    ok.Value,
                    new List<List<Flt64>> {
                        new(ok.Value.Solution.Values),
                    })),
            Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(failed.Error),
            Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "未知求解结果 / Unknown solve result")),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> SolveMILPWithSolutionPoolAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options,
        CancellationToken cancellationToken = default) =>
        await SolveMILPAsync(
            name: Name,
            metaModel: metaModel,
            amount: options.SolveOptions?.SolutionAmount ?? 1,
            toLogModel: options.LogModel,
            registrationStatusCallBack: null,
            solvingStatusCallBack: options.SolveOptions?.SolvingStatusCallBack,
            cancellationToken: cancellationToken);

    /// <inheritdoc/>
    public async Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null,
        CancellationToken cancellationToken = default) {
        Result<(LinearMechanismModel<Flt64> Mechanism, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            await BuildAsync(
                metaModel: metaModel,
                registrationStatusCallBack: registrationStatusCallBack,
                cancellationToken: cancellationToken);
        if (built is not Ok<(LinearMechanismModel<Flt64> Mechanism, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> builtOk) {
            return Propagate<IColumnGenerationSolver.LpResult, (LinearMechanismModel<Flt64> Mechanism, LinearTriadModel Matrix)>(built);
        }

        using LinearMechanismModel<Flt64> mechanismModel = builtOk.Value.Mechanism;
        using LinearTriadModel matrix = builtOk.Value.Matrix;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> relaxed =
            matrix.RelaxToContinuous();
        if (relaxed is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> relaxedOk) {
            return Propagate<IColumnGenerationSolver.LpResult, LinearTriadModel>(relaxed);
        }

        using LinearTriadModel relaxedModel = relaxedOk.Value;
        Result<Success, ErrorCode, Error<ErrorCode>> exported = await ExportIfRequested(
            metaModel: metaModel,
            model: relaxedModel,
            name: name,
            toLogModel: toLogModel);
        if (!exported.IsOk) {
            return Propagate<IColumnGenerationSolver.LpResult>(exported);
        }

        var solver = new HexalyLinearSolver(Config, _callBack?.Copy());
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> primal =
            await solver.InvokeAsync(
                model: (ILinearTriadModelView)relaxedModel,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken);
        if (primal is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> primalOk) {
            return Propagate<IColumnGenerationSolver.LpResult, FeasibleSolverOutput<Flt64>>(primal);
        }

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> dual =
            await TriadDualSolverSupport.SolveDualAsync(
                model: relaxedModel,
                solver: solver,
                solvingStatusCallBack: solvingStatusCallBack,
                cancellationToken: cancellationToken);
        if (dual is not Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> dualOk) {
            return Propagate<IColumnGenerationSolver.LpResult, IReadOnlyList<Flt64>>(dual);
        }

        IReadOnlyList<IConstraint<Flt64, LinearCategory>> constraints =
            ((IMechanismModel<Flt64>)mechanismModel).Constraints;
        if (dualOk.Value.Count != constraints.Count) {
            return Results.Failed<IColumnGenerationSolver.LpResult>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                $"对偶值数量与约束数量不一致 / Dual count {dualOk.Value.Count} does not match constraint count {constraints.Count}"));
        }
        // **来源映射与缺源键都走共享实现，不再内联一次。**
        // **the origin map and the no-origin key both go through the shared implementation.**
        MathConstraint?[] hexalyOriginMap = MathConstraintOrigins.OriginMap(mechanismModel);
        var dualSolution = new Dictionary<MathConstraint, Flt64>(constraints.Count);
        for (int index = 0; index < constraints.Count; index++) {
            dualSolution[MathConstraintOrigins.ResolveOriginKey(index, hexalyOriginMap)] =
                dualOk.Value[index];
        }

        return mechanismModel.ApplySolverSolution(
                matrix: matrix,
                solvedMatrix: relaxedModel,
                solution: primalOk.Value.Solution.Values)
            .Map(_ => new IColumnGenerationSolver.LpResult(
                Result: primalOk.Value,
                DualSolution: dualSolution));
    }

    private async Task<Result<(LinearMechanismModel<Flt64> Mechanism, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>>> BuildAsync(
        LinearMetaModel<Flt64> metaModel,
        RegistrationStatusCallBack? registrationStatusCallBack,
        CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return Results.Failed<(LinearMechanismModel<Flt64>, LinearTriadModel)>(new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "列生成模型构建已取消 / Column-generation model build was cancelled"));
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanism =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: metaModel,
                concurrent: Config.DumpMechanismModelConcurrent,
                blocking: Config.DumpMechanismModelBlocking,
                registrationStatusCallBack: registrationStatusCallBack);
        if (mechanism is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismOk) {
            return Propagate<(LinearMechanismModel<Flt64> Mechanism, LinearTriadModel Matrix), LinearMechanismModel<Flt64>>(mechanism);
        }

        var solver = new HexalyLinearSolver(Config, _callBack?.Copy());
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> matrix =
            await solver.DumpAsync(
                model: mechanismOk.Value,
                cancellationToken: cancellationToken);
        if (matrix is Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> matrixOk) {
            return Results.Ok((mechanismOk.Value, matrixOk.Value));
        }
        mechanismOk.Value.Dispose();
        return Propagate<(LinearMechanismModel<Flt64> Mechanism, LinearTriadModel Matrix), LinearTriadModel>(matrix);
    }

    private static async Task<Result<Success, ErrorCode, Error<ErrorCode>>> ExportIfRequested(
        LinearMetaModel<Flt64> metaModel,
        LinearTriadModel model,
        string name,
        bool toLogModel) {
        if (!toLogModel) {
            return Results.OkInstance;
        }
        string fileName = string.IsNullOrWhiteSpace(name) ? model.Name : name;
        Result<Success, ErrorCode, Error<ErrorCode>> opm = await metaModel.ExportAsync(
            $"{fileName}.opm");
        if (!opm.IsOk) {
            return opm;
        }
        return model.Export(
            path: $"{fileName}.lp",
            format: ModelFileFormat.LP);
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T>(
        Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "Hexaly 操作结果无效 / Invalid Hexaly operation result")),
        };

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "Hexaly 操作结果无效 / Invalid Hexaly operation result")),
        };
}

