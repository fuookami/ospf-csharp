#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
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

namespace Fuookami.Ospf.Core.Plugin.Scip;

/// <summary>
/// SCIP 列生成求解器入口，LP 模式提供行对偶与强对偶校验回退。
/// SCIP column-generation entry; the LP mode provides row duals with a strong-duality fallback.
/// </summary>
public sealed class ScipColumnGenerationSolver : Framework.Solver.IColumnGenerationSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "scip";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建 SCIP 列生成求解器 / Create SCIP column generation solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public ScipColumnGenerationSolver(SolverConfig? config = null) {
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
        if (toLogModel) {
            return Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持 SCIP 模型日志导出 / SCIP model logging export is not yet supported"));
        }
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
        Framework.Solver.FrameworkSolveOptions options) {
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
    /// <remarks>返回的列表以主解开头，即使数量不超过 1；附加解可能不足请求数量且不保证完整枚举。所有向量按机制模型列顺序排列。 / The list starts with the main solution even when the amount is at most 1; alternatives may be fewer than requested and are not an exhaustive enumeration. All vectors follow mechanism-model column order.</remarks>
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
        if (toLogModel) {
            return Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持 SCIP 模型日志导出 / SCIP model logging export is not yet supported"));
        }
        if (amount > int.MaxValue) {
            return Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "解池容量超出 SCIP 整数范围 / Solution pool capacity exceeds the SCIP integer range"));
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await DumpMechanismModelAsync(metaModel, registrationStatusCallBack);
        if (mechanismResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>) {
            return PropagateModelResult<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(mechanismResult);
        }
        using LinearMechanismModel<Flt64> mechanismModel = mechanismResult.Value;
        ScipLinearSolver solver = CreateSolver();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult = await solver.DumpAsync(mechanismModel);
        if (triadResult is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadOk) {
            return triadResult.Bind<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(_ => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(
                ErrorCode.OREngineModelingException,
                "线性模型转换结果无效 / Invalid linear model conversion result")));
        }
        using LinearTriadModel triadModel = triadOk.Value;
        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> solveResult = await solver.InvokeAsync(
            model: triadModel,
            solutionAmount: amount,
            solvingStatusCallBack: solvingStatusCallBack);
        if (solveResult is not Ok<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> ok) {
            return solveResult;
        }
        var pool = new List<List<Flt64>>();
        foreach (IReadOnlyList<Flt64> values in new[] { ok.Value.Output.Solution.Values }.Concat(ok.Value.Solutions)) {
            Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> restored = mechanismModel.RestoreSolverSolution(triadModel, values);
            if (!restored.IsOk) {
                return restored.Bind<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(_ => Results.Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>)>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)));
            }
            pool.Add(restored.Value.ToList());
        }
        return mechanismModel.ApplySolverSolution(triadModel, ok.Value.Output.Solution.Values).Map(_ => (
            ok.Value.Output with { Solution = new Solution<Flt64>(Array.AsReadOnly(pool[0].ToArray())) },
            pool));
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
    /// 求解 LP 松弛问题（含对偶解提取）/ Solve LP relaxation problem (with dual solution extraction).
    ///
    /// LP 以关闭预处理与启发式的单线程模式求解，逐行读取原生对偶解；当强对偶等式不成立时改求解显式对偶模型。
    /// The LP is solved in single-threaded mode with presolving and heuristics off, and row duals are read natively;
    /// when the strong-duality equality fails, the explicit dual model is solved instead.
    /// </summary>
    /// <remarks>无 incumbent 时返回失败而不是空对偶；行到机制约束的映射按行索引完成，缺少 Origin 的行使用占位键。 / Missing incumbents fail instead of returning empty duals; rows map to mechanism constraints by index, and rows without an Origin get a placeholder key.</remarks>
    /// <param name="name">模型名称 / Model name.</param>
    /// <param name="metaModel">线性元模型 / Linear meta model.</param>
    /// <param name="toLogModel">是否记录模型日志 / Whether to log model.</param>
    /// <param name="registrationStatusCallBack">注册状态回调 / Registration status callback.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <returns>LP 求解结果（含对偶解）/ LP solving result with dual solution.</returns>
    public async Task<Result<Framework.Solver.IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        if (toLogModel) {
            return Results.Failed<Framework.Solver.IColumnGenerationSolver.LpResult>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持 SCIP 模型日志导出 / SCIP model logging export is not yet supported"));
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await DumpMechanismModelAsync(metaModel, registrationStatusCallBack);
        if (mechanismResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismOk) {
            return PropagateModelResult<Framework.Solver.IColumnGenerationSolver.LpResult>(mechanismResult);
        }
        using LinearMechanismModel<Flt64> mechanismModel = mechanismOk.Value;
        MathConstraint?[] originMap = MathConstraintOrigins.OriginMap(mechanismModel);

        ScipLinearSolver solver = CreateSolver();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult = await solver.DumpAsync(mechanismModel);
        if (triadResult is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadOk) {
            return triadResult.Bind<Framework.Solver.IColumnGenerationSolver.LpResult>(_ => Results.Failed<Framework.Solver.IColumnGenerationSolver.LpResult>(
                new Err<ErrorCode>(
                    ErrorCode.OREngineModelingException,
                    "线性模型转换结果无效 / Invalid linear model conversion result")));
        }
        using LinearTriadModel triadModel = triadOk.Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> relaxation = triadModel.RelaxToContinuous();
        if (relaxation is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> relaxOk) {
            return relaxation.Bind<Framework.Solver.IColumnGenerationSolver.LpResult>(_ => Results.Failed<Framework.Solver.IColumnGenerationSolver.LpResult>(
                new Err<ErrorCode>(
                    ErrorCode.OREngineModelingException,
                    "松弛模型转换结果无效 / Invalid relaxation result")));
        }
        using LinearTriadModel lpModel = relaxOk.Value;

        Result<ScipLpOutcome, ErrorCode, Error<ErrorCode>> lp = await solver.InvokeLpAsync(
            model: lpModel,
            solvingStatusCallBack: solvingStatusCallBack);
        if (lp is not Ok<ScipLpOutcome, ErrorCode, Error<ErrorCode>> lpOk) {
            return ScipLpDualSupport.Propagate<Framework.Solver.IColumnGenerationSolver.LpResult, ScipLpOutcome>(
                lp, "SCIP LP 求解失败 / SCIP LP solving failed");
        }
        ScipLpOutcome outcome = lpOk.Value;
        if (outcome.Output is not { } output) {
            return Results.Failed<Framework.Solver.IColumnGenerationSolver.LpResult>(new Err<ErrorCode>(
                ErrorCode.Other,
                "SCIP LP 未找到 incumbent，不能回填解或生成定价对偶 / " +
                    "SCIP LP produced no incumbent; solution values and pricing duals are unavailable " +
                    $"(problemStatus={outcome.Report.ProblemStatus}, terminationReason={outcome.Report.TerminationReason})"));
        }

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> rowDuals = await ScipLpDualSupport.RowDualsAsync(
            model: lpModel,
            outcome: outcome,
            dualSolver: new ScipLinearSolver(Config),
            cancellationToken: default);
        if (rowDuals is not Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> dualsOk) {
            return ScipLpDualSupport.Propagate<Framework.Solver.IColumnGenerationSolver.LpResult, IReadOnlyList<Flt64>>(
                rowDuals, "SCIP 对偶提取失败 / SCIP dual extraction failed");
        }
        IReadOnlyDictionary<MathConstraint, Flt64> dualSolution =
            MathConstraintOrigins.ToDualSolution(dualsOk.Value, originMap);

        return mechanismModel.ApplySolverSolution(
            matrix: triadModel,
            solvedMatrix: lpModel,
            solution: output.Solution.Values)
            .Map(_ => new Framework.Solver.IColumnGenerationSolver.LpResult(output, dualSolution));
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
    /// 创建 SCIP 线性求解器 / Create SCIP linear solver.
    /// </summary>
    private ScipLinearSolver CreateSolver() =>
        new ScipLinearSolver(Config);

    /// <summary>
    /// 转储元模型为机制模型 / Dump meta model to mechanism model.
    /// </summary>
    private async Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpMechanismModelAsync(
        LinearMetaModel<Flt64> metaModel,
        RegistrationStatusCallBack? registrationStatusCallBack) {
        ScipLinearSolver solver = CreateSolver();
        return await solver.DumpAsync(metaModel, registrationStatusCallBack, null);
    }

    /// <summary>
    /// 通过三元组模型求解 / Solve via triad model.
    /// </summary>
    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveTriadModelAsync(
        LinearMechanismModel<Flt64> mechanismModel,
        SolvingStatusCallBack? solvingStatusCallBack) {
        ScipLinearSolver solver = CreateSolver();
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
