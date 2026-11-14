#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FrameworkSolveOptions = Fuookami.Ospf.Framework.Solver.FrameworkSolveOptions;

namespace Fuookami.Ospf.Core.Plugin.Cplex;

/// <summary>
/// Cplex 列生成求解器实现。
/// Cplex column generation solver implementation.
///
/// 使用 Cplex 求解器实现列生成策略，支持 MILP 求解、多解求解和 LP 松弛求解。
/// 运行时需加载 Cplex 托管 DLL，否则返回 OREngineEnvironmentLost。
/// Implements column generation strategy using Cplex solver, supporting MILP solving,
/// multi-solution solving, and LP relaxation solving.
/// Requires Cplex managed DLL at runtime; returns OREngineEnvironmentLost otherwise.
/// </summary>
public sealed class CplexColumnGenerationSolver : Framework.Solver.IColumnGenerationSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "Cplex";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建 Cplex 列生成求解器 / Create Cplex column generation solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public CplexColumnGenerationSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <summary>
    /// 求解 MILP 主问题 / Solve MILP master problem.
    /// </summary>
    /// <param name="name">模型名称 / Model name.</param>
    /// <param name="metaModel">线性元模型 / Linear meta model.</param>
    /// <param name="toLogModel">是否记录模型日志 / Whether to log model.</param>
    /// <param name="registrationStatusCallBack">注册状态回调 / Registration status callback.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <returns>求解结果 / Solving result.</returns>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await DumpMechanismModelAsync(metaModel, registrationStatusCallBack);
        if (mechanismResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>) {
            return PropagateModelResult<FeasibleSolverOutput<Flt64>>(mechanismResult);
        }

        using LinearMechanismModel<Flt64> mechanismModel = mechanismResult.Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solveResult =
            await SolveTriadModelAsync(mechanismModel, solvingStatusCallBack);

        if (solveResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return mechanismModel.ApplySolverSolution(ok.Value.Solution.Values).Bind(_ => solveResult);
        }

        return solveResult;
    }

    /// <summary>
    /// 使用选项求解 MILP 问题 / Solve MILP problem with options.
    /// </summary>
    public async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options) {
        ulong? solutionAmount = options.SolutionAmount;
        if (solutionAmount is null) {
            return await SolveMILPAsync(
                options.SolveName(metaModel.Name),
                metaModel,
                options.ToLogModel,
                options.RegistrationStatusCallBack,
                options.SolvingStatusCallBack);
        }

        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>> poolResult =
            await SolveMILPAsync(
                options.SolveName(metaModel.Name),
                metaModel,
                solutionAmount.Value,
                options.ToLogModel,
                options.RegistrationStatusCallBack,
                options.SolvingStatusCallBack);
        return poolResult.Map(r => r.Output);
    }

    /// <summary>
    /// 求解 MILP 主问题并获取多个解 / Solve MILP master problem and obtain multiple solutions.
    /// </summary>
    /// <param name="name">模型名称 / Model name.</param>
    /// <param name="metaModel">线性元模型 / Linear meta model.</param>
    /// <param name="amount">期望解的数量 / Desired number of solutions.</param>
    /// <param name="toLogModel">是否记录模型日志 / Whether to log model.</param>
    /// <param name="registrationStatusCallBack">注册状态回调 / Registration status callback.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <returns>求解结果及多个解 / Solving result with multiple solutions.</returns>
    public async Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await DumpMechanismModelAsync(metaModel, registrationStatusCallBack);
        if (mechanismResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>) {
            return PropagateModelResult<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(mechanismResult);
        }

        using LinearMechanismModel<Flt64> mechanismModel = mechanismResult.Value;
        CplexLinearSolver solver = CreateSolver();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult =
            await solver.DumpAsync(mechanismModel);
        if (triadResult is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadOk) {
            return triadResult.Bind<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(_ => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                ErrorCode.OREngineModelingException,
                "线性模型转换结果无效 / Invalid linear model conversion result")));
        }
        LinearTriadModel triadModel = triadOk.Value;

        using (triadModel) {
            Result<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>> solveResult =
                await solver.InvokeAsync(triadModel, amount, solvingStatusCallBack);

            if (solveResult is Ok<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>> ok) {
                return mechanismModel.ApplySolverSolution(ok.Value.Item1.Solution.Values).Bind(_ => solveResult);
            }

            return solveResult;
        }
    }

    /// <summary>
    /// 使用值转换器求解 MILP 问题 / Solve MILP problem with value converter.
    /// </summary>
    public async Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> flt64Result =
            await SolveMILPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

        return flt64Result.Map(output => ConvertOutput(output, converter));
    }

    /// <summary>
    /// 求解 LP 松弛问题 / Solve LP relaxation problem.
    ///
    /// LP 松弛使用 Cplex 原生逐行对偶并按机制约束身份返回。
    /// The LP relaxation returns native Cplex row duals keyed by mechanism-constraint identity.
    /// </summary>
    /// <param name="name">模型名称 / Model name.</param>
    /// <param name="metaModel">线性元模型 / Linear meta model.</param>
    /// <param name="toLogModel">是否记录模型日志 / Whether to log model.</param>
    /// <param name="registrationStatusCallBack">注册状态回调 / Registration status callback.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <returns>LP 求解结果（含逐行对偶）/ LP solving result with row duals.</returns>
    public async Task<Result<Framework.Solver.IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await DumpMechanismModelAsync(metaModel, registrationStatusCallBack);
        if (mechanismResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>) {
            return PropagateModelResult<Framework.Solver.IColumnGenerationSolver.LpResult>(mechanismResult);
        }

        using LinearMechanismModel<Flt64> mechanismModel = mechanismResult.Value;
        CplexLinearSolver solver = CreateSolver();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult =
            await solver.DumpAsync(mechanismModel);
        if (triadResult is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadOk) {
            return triadResult.Bind<Framework.Solver.IColumnGenerationSolver.LpResult>(_ => Results.Failed<Framework.Solver.IColumnGenerationSolver.LpResult>(new Err<ErrorCode>(
                ErrorCode.OREngineModelingException,
                "线性模型转换结果无效 / Invalid linear model conversion result")));
        }
        LinearTriadModel triadModel = triadOk.Value;
        LinearTriadModel relaxedModel = RelaxToLP(triadModel);

        using (triadModel)
        using (relaxedModel) {
            MathConstraint?[] originMap = MathConstraintOrigins.OriginMap(mechanismModel);
            Result<CplexLinearOutcome, ErrorCode, Error<ErrorCode>> solved =
                await solver.InvokeReportWithDualsAsync(
                    model: relaxedModel,
                    solvingStatusCallBack: solvingStatusCallBack);
            if (solved is not Ok<CplexLinearOutcome, ErrorCode, Error<ErrorCode>> solvedOk) {
                return CplexDualSupport.Propagate<Framework.Solver.IColumnGenerationSolver.LpResult, CplexLinearOutcome>(
                    solved,
                    "Cplex LP 求解失败 / Cplex LP solving failed");
            }
            if (solvedOk.Value.Report.ProblemStatus != ProblemStatus.Feasible
                || solvedOk.Value.RowDuals is null) {
                return Results.Failed<Framework.Solver.IColumnGenerationSolver.LpResult>(new Err<ErrorCode>(
                    ErrorCode.Other,
                    "Cplex LP 未找到可用于定价的 incumbent 或对偶 / " +
                    $"Cplex LP produced no pricing incumbent or duals (problemStatus={solvedOk.Value.Report.ProblemStatus})"));
            }

            Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> rowDuals =
                relaxedModel.TidyDualSolution(solvedOk.Value.RowDuals);
            if (rowDuals is not Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> dualsOk) {
                return CplexDualSupport.Propagate<Framework.Solver.IColumnGenerationSolver.LpResult, IReadOnlyList<Flt64>>(
                    rowDuals,
                    "Cplex 对偶值与 LP 行数不一致 / Cplex duals do not align with the LP rows");
            }
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report =
                Results.Ok(solvedOk.Value.Report);
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy =
                CplexReportSupport.ToLegacy(report);
            if (legacy is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> output) {
                return CplexDualSupport.Propagate<
                    Framework.Solver.IColumnGenerationSolver.LpResult,
                    FeasibleSolverOutput<Flt64>>(
                    legacy,
                    "Cplex LP 没有可返回的可行解 / Cplex LP has no feasible output");
            }
            IReadOnlyDictionary<MathConstraint, Flt64> dualSolution =
                MathConstraintOrigins.ToDualSolution(dualsOk.Value, originMap);
            return mechanismModel.ApplySolverSolution(output.Value.Solution.Values)
                .Map(_ => new Framework.Solver.IColumnGenerationSolver.LpResult(output.Value, dualSolution));
        }
    }

    /// <summary>
    /// 使用值转换器求解 LP 问题 / Solve LP problem with value converter.
    /// </summary>
    public async Task<Result<Framework.Solver.IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<Framework.Solver.IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lpResult =
            await SolveLPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

        return lpResult.Map(lp => {
            FeasibleSolverOutput<V> convertedOutput = ConvertOutput(lp.Result, converter);
            return new Framework.Solver.IColumnGenerationSolver.LpResultOf<V>(convertedOutput, lp.DualSolution);
        });
    }

    // ===== Private helpers =====

    /// <summary>
    /// 创建 Cplex 线性求解器 / Create Cplex linear solver.
    /// </summary>
    private CplexLinearSolver CreateSolver() =>
        new CplexLinearSolver(Config);

    /// <summary>
    /// 转储元模型为机制模型 / Dump meta model to mechanism model.
    /// </summary>
    private async Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpMechanismModelAsync(
        LinearMetaModel<Flt64> metaModel,
        RegistrationStatusCallBack? registrationStatusCallBack) {
        CplexLinearSolver solver = CreateSolver();
        return await solver.DumpAsync(metaModel, registrationStatusCallBack, null);
    }

    /// <summary>
    /// 通过三元组模型求解 / Solve via triad model.
    /// </summary>
    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveTriadModelAsync(
        LinearMechanismModel<Flt64> mechanismModel,
        SolvingStatusCallBack? solvingStatusCallBack) {
        CplexLinearSolver solver = CreateSolver();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult =
            await solver.DumpAsync(mechanismModel);
        if (triadResult is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadOk) {
            return triadResult.Bind<FeasibleSolverOutput<Flt64>>(_ => Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
                ErrorCode.OREngineModelingException,
                "线性模型转换结果无效 / Invalid linear model conversion result")));
        }
        LinearTriadModel triadModel = triadOk.Value;

        using (triadModel) {
            return await solver.InvokeAsync((ILinearTriadModelView)triadModel, solvingStatusCallBack);
        }
    }

    /// <summary>
    /// 将 Flt64 求解输出转换为泛型值类型 / Convert Flt64 solver output to generic value type.
    /// </summary>
    private static FeasibleSolverOutput<V> ConvertOutput<V>(
        FeasibleSolverOutput<Flt64> output,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        IReadOnlyList<Flt64> flt64Values = output.Solution.Values;
        var convertedValues = new List<V>(flt64Values.Count);
        foreach (Flt64 v in flt64Values) {
            convertedValues.Add(converter.IntoValue(v));
        }

        return new FeasibleSolverOutput<V>(
            Obj: output.Obj,
            Solution: new Solution<V>(convertedValues),
            Time: output.Time,
            PossibleBestObj: output.PossibleBestObj,
            Gap: output.Gap);
    }

    /// <summary>
    /// 将三元组模型松弛为 LP / Relax a triad model to LP (all variables become continuous).
    /// </summary>
    private static LinearTriadModel RelaxToLP(LinearTriadModel model) {
        var relaxedVars = new List<ModelViewVariable>(model.Variables.Count);
        foreach (ModelViewVariable v in model.Variables) {
            relaxedVars.Add(v.WithDomain(
                lowerBound: v.LowerBound,
                upperBound: v.UpperBound,
                type: Continuous.Instance));
        }

        return new LinearTriadModel(
            variables: relaxedVars,
            constraints: (LinearConstraintBatch)model.Constraints.Copy(),
            objective: model.Objective,
            name: model.Name);
    }

    /// <summary>
    /// 将模型转储结果传播为指定类型的错误结果 / Propagate model dump error to a target result type.
    /// </summary>
    private static Result<T, ErrorCode, Error<ErrorCode>> PropagateModelResult<T>(
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> r) {
        if (r is Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<T>(f.Error);
        }
        if (r is Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> fat) {
            return new Fatal<T, ErrorCode, Error<ErrorCode>>(fat.Errors);
        }
        return Results.Failed<T>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException));
    }
}
