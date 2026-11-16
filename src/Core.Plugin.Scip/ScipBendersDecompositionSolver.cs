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
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FrameworkSolveOptions = Fuookami.Ospf.Framework.Solver.FrameworkSolveOptions;

namespace Fuookami.Ospf.Core.Plugin.Scip;

/// <summary>
/// SCIP Benders 求解入口，子问题提供行对偶与 Farkas 证书。
/// SCIP Benders entry; the sub-problem provides row duals and Farkas certificates.
/// </summary>
public sealed class ScipBendersDecompositionSolver : Framework.Solver.IBendersDecompositionSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "scip";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建 SCIP Benders 分解求解器 / Create SCIP Benders decomposition solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public ScipBendersDecompositionSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public Framework.Solver.IBendersDecompositionSolver WithTimeLimit(TimeSpan timeLimit) =>
        new ScipBendersDecompositionSolver(Config with { TimeLimit = timeLimit });

    // ===== IBendersDecompositionSolver: Master problem =====

    /// <summary>
    /// 求解线性 Benders 主问题 / Solve linear Benders master problem.
    /// </summary>
    /// <param name="name">模型名称 / Model name.</param>
    /// <param name="metaModel">线性元模型 / Linear meta model.</param>
    /// <param name="toLogModel">是否记录模型日志 / Whether to log model.</param>
    /// <param name="registrationStatusCallBack">注册状态回调 / Registration status callback.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <returns>求解结果 / Solving result.</returns>
    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        if (toLogModel) {
            return Results.Failed<SolverOutput>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持 SCIP 模型日志导出 / SCIP model logging export is not yet supported"));
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await DumpMechanismModelAsync(metaModel, registrationStatusCallBack);
        if (mechanismResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>) {
            return PropagateModelResult<SolverOutput>(mechanismResult);
        }

        using LinearMechanismModel<Flt64> mechanismModel = mechanismResult.Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solveResult =
            await SolveTriadModelAsync(mechanismModel, solvingStatusCallBack);

        if (solveResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return mechanismModel.ApplySolverSolution(ok.Value.Solution.Values).Map<SolverOutput>(_ => ok.Value);
        }

        if (solveResult is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<SolverOutput>(f.Error);
        }

        if (solveResult is Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fat) {
            return new Fatal<SolverOutput, ErrorCode, Error<ErrorCode>>(fat.Errors);
        }

        return Results.Failed<SolverOutput>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException));
    }

    /// <summary>
    /// 求解线性 Benders 主问题并保留统一报告。
    /// Solve a linear Benders master problem while retaining the unified report.
    /// </summary>
    public async Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMasterReportAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        if (toLogModel) {
            return Results.Failed<SolveReport<Flt64>>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持 SCIP 模型日志导出 / SCIP model logging export is not yet supported"));
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await DumpMechanismModelAsync(metaModel, registrationStatusCallBack);
        if (mechanismResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismOk) {
            return ScipLpDualSupport.Propagate<SolveReport<Flt64>, LinearMechanismModel<Flt64>>(
                mechanismResult,
                "SCIP 主问题模型转储失败 / SCIP master model dump failed");
        }

        using LinearMechanismModel<Flt64> mechanismModel = mechanismOk.Value;
        ScipLinearSolver solver = CreateSolver();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult =
            await solver.DumpAsync(mechanismModel);
        if (triadResult is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadOk) {
            return ScipLpDualSupport.Propagate<SolveReport<Flt64>, LinearTriadModel>(
                triadResult,
                "SCIP 主问题矩阵转换失败 / SCIP master matrix conversion failed");
        }

        using LinearTriadModel triadModel = triadOk.Value;
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> reportResult =
            await solver.InvokeReportAsync(
                model: triadModel,
                solvingStatusCallBack: solvingStatusCallBack);
        if (reportResult is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> reportOk) {
            return ScipLpDualSupport.Propagate<SolveReport<Flt64>, SolveReport<Flt64>>(
                reportResult,
                "SCIP 主问题求解失败 / SCIP master solve failed");
        }
        if (reportOk.Value.Solution is { } solution) {
            Result<Success, ErrorCode, Error<ErrorCode>> applied =
                mechanismModel.ApplySolverSolution(solution.Values.Values);
            if (!applied.IsOk) {
                return ScipLpDualSupport.Propagate<SolveReport<Flt64>, Success>(
                    applied,
                    "SCIP 主问题解回填失败 / SCIP master solution writeback failed");
            }
        }
        return Results.Ok(reportOk.Value);
    }

    /// <summary>
    /// 使用值转换器求解线性 Benders 主问题 / Solve linear Benders master with value converter.
    /// </summary>
    public async Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<SolverOutput, ErrorCode, Error<ErrorCode>> result =
            await SolveMasterAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack);

        if (result is Ok<SolverOutput, ErrorCode, Error<ErrorCode>> ok) {
            return Results.Ok(ConvertSolverOutput(ok.Value, converter));
        }

        if (result is Failed<SolverOutput, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<SolverOutput>(f.Error);
        }

        if (result is Fatal<SolverOutput, ErrorCode, Error<ErrorCode>> fat) {
            return new Fatal<SolverOutput, ErrorCode, Error<ErrorCode>>(fat.Errors);
        }

        return Results.Failed<SolverOutput>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException));
    }

    // ===== IBendersDecompositionSolver: Sub problem =====

    /// <summary>
    /// 求解线性 Benders 子问题 / Solve linear Benders sub problem.
    ///
    /// 固定列代换后以 LP 模式求解线性松弛子问题，逐行提取对偶解；不可行时提取 Farkas 乘子，不生成割。
    /// Substitutes fixed columns, solves the linear-relaxed sub-problem in LP mode and extracts row duals; on
    /// infeasibility it extracts Farkas multipliers, without generating cuts.
    /// </summary>
    /// <remarks>
    /// 行乘子未作为含变量界的完整证明独立验证，仅在过滤零值后非空时接受；Farkas 全零视为后端未提供证据并返回失败。
    /// Row multipliers are not independently verified as a complete certificate including variable bounds and are
    /// accepted only when non-empty after dropping zeros; an all-zero Farkas vector is reported as a missing
    /// certificate instead of a fabricated cut.
    /// </remarks>
    /// <param name="name">模型名称 / Model name.</param>
    /// <param name="metaModel">线性元模型 / Linear meta model.</param>
    /// <param name="objectVariable">目标变量 / Objective variable (for cut generation context).</param>
    /// <param name="fixedVariables">固定变量映射 / Fixed variable mapping.</param>
    /// <param name="toLogModel">是否记录模型日志 / Whether to log model.</param>
    /// <param name="registrationStatusCallBack">注册状态回调 / Registration status callback.</param>
    /// <param name="solvingStatusCallBack">求解状态回调 / Solving status callback.</param>
    /// <returns>线性子问题求解结果 / Linear sub-problem result.</returns>
    public async Task<Result<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        if (toLogModel) {
            return Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(new Err<ErrorCode>(
                ErrorCode.Other,
                "暂不支持 SCIP 模型日志导出 / SCIP model logging export is not yet supported"));
        }
        if (fixedVariables is null) {
            return Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "固定变量映射为空 / Fixed variable map is null"));
        }

        Result<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            await metaModel.BuildLinearTriadModelAsync(
                fixedVariables: fixedVariables,
                registrationStatusCallBack: registrationStatusCallBack
            );
        if (built is not Ok<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> builtOk) {
            return ScipLpDualSupport.Propagate<Framework.Solver.IBendersDecompositionSolver.LinearSubResult,
                (LinearMechanismModel<Flt64>, LinearTriadModel)>(
                built, "线性模型构建结果无效 / Invalid linear model build result");
        }

        using LinearMechanismModel<Flt64> mechanismModel = builtOk.Value.Model;
        using LinearTriadModel triadModel = builtOk.Value.Matrix;
        MathConstraint?[] originMap = MathConstraintOrigins.OriginMap(mechanismModel);

        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> relaxation = triadModel.RelaxToContinuous();
        if (relaxation is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> relaxOk) {
            return relaxation.Bind<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(_ => Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(
                new Err<ErrorCode>(
                    ErrorCode.OREngineModelingException,
                    "松弛模型转换结果无效 / Invalid relaxation result")));
        }
        using LinearTriadModel lpModel = relaxOk.Value;

        Result<ScipLpOutcome, ErrorCode, Error<ErrorCode>> lp = await CreateSolver().InvokeLpAsync(
            model: lpModel,
            solvingStatusCallBack: solvingStatusCallBack);
        if (lp is not Ok<ScipLpOutcome, ErrorCode, Error<ErrorCode>> lpOk) {
            return ScipLpDualSupport.Propagate<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ScipLpOutcome>(
                lp, "SCIP 子问题求解失败 / SCIP sub-problem solving failed");
        }
        ScipLpOutcome outcome = lpOk.Value;

        if (outcome.Output is { } output) {
            Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> rowDuals = await ScipLpDualSupport.RowDualsAsync(
                model: lpModel,
                outcome: outcome,
                dualSolver: new ScipLinearSolver(Config),
                cancellationToken: default);
            if (rowDuals is not Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> dualsOk) {
                return ScipLpDualSupport.Propagate<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, IReadOnlyList<Flt64>>(
                    rowDuals, "SCIP 对偶提取失败 / SCIP dual extraction failed");
            }
            IReadOnlyDictionary<MathConstraint, Flt64> dualSolution =
                MathConstraintOrigins.ToDualSolution(dualsOk.Value, originMap);
            return mechanismModel.RestoreSolverSolution(
                matrix: triadModel,
                solvedMatrix: lpModel,
                solution: output.Solution.Values)
                .Bind(values => mechanismModel.ApplySolverSolution(
                    matrix: triadModel,
                    solvedMatrix: lpModel,
                    solution: output.Solution.Values)
                    .Map<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(_ =>
                        new Framework.Solver.IBendersDecompositionSolver.LinearFeasibleResult(
                            Result: output with { Solution = new Solution<Flt64>(values) },
                            DualSolution: dualSolution,
                            Cuts: null) {
                            Report = outcome.Report,
                        }));
        }

        if (outcome.Report.ProblemStatus == ProblemStatus.Infeasible) {
            // 不可行子问题的 Farkas 乘子来自显式 Farkas 对偶模型，而不是原生逐行乘子。
            // Like Kotlin: Farkas multipliers for the infeasible sub-problem come from the explicit Farkas dual model,
            // not from native per-row multipliers.
            Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> farkasDuals =
                await TriadDualSolverSupport.SolveFarkasDualAsync(
                    model: lpModel,
                    solver: new ScipLinearSolver(Config),
                    cancellationToken: default);
            if (farkasDuals is not Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> farkasDualsOk) {
                return ScipLpDualSupport.Propagate<Framework.Solver.IBendersDecompositionSolver.LinearSubResult,
                    IReadOnlyList<Flt64>>(
                    farkasDuals, "SCIP 未提供可用的 Farkas 证书 / SCIP did not provide a usable Farkas certificate");
            }
            Result<IReadOnlyDictionary<MathConstraint, Flt64>, ErrorCode, Error<ErrorCode>> farkas =
                MathConstraintOrigins.ToFarkasSolution(farkasDualsOk.Value, originMap, "SCIP");
            if (farkas is not Ok<IReadOnlyDictionary<MathConstraint, Flt64>, ErrorCode, Error<ErrorCode>> farkasOk) {
                return ScipLpDualSupport.Propagate<Framework.Solver.IBendersDecompositionSolver.LinearSubResult,
                    IReadOnlyDictionary<MathConstraint, Flt64>>(
                    farkas, "SCIP 未提供可用的 Farkas 证书 / SCIP did not provide a usable Farkas certificate");
            }
            return mechanismModel.ValidateSolverTokenSnapshot(triadModel, lpModel)
                .Map<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(_ =>
                    new Framework.Solver.IBendersDecompositionSolver.LinearInfeasibleResult(
                        FarkasDualSolution: farkasOk.Value,
                        Cuts: null));
        }

        // 其他终态（无界、未知、被取消）沿用报告映射出的错误，不伪造对偶或 Farkas 证据。
        // Other terminal states (unbounded, unknown, cancelled) reuse the error mapped from the report instead of
        // fabricating dual or Farkas evidence.
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy =
            ScipLinearSolver.ToLegacy(Results.Ok(outcome.Report));
        return ScipLpDualSupport.Propagate<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, FeasibleSolverOutput<Flt64>>(
            legacy, "SCIP 子问题既无 incumbent 也无可用的不可行证据 / SCIP sub-problem has neither an incumbent nor usable infeasibility evidence");
    }

    /// <summary>
    /// 使用值转换器求解线性 Benders 子问题 / Solve linear Benders sub with value converter.
    ///
    /// 当前实现仅支持 V = Flt64（恒等转换）。泛型值转换需要带 converter 参数的重载。
    /// Current implementation supports V = Flt64 (identity conversion) only.
    /// Generic value conversion requires an overload with a converter parameter.
    /// </summary>
    public async Task<Result<Framework.Solver.IBendersDecompositionSolver.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveSubAsAsync<V>(
        LinearMetaModel<Flt64> metaModel,
        IVariableItem objectVariable,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        FrameworkSolveOptions options)
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await SolveSubAsync(
                options.SolveName(metaModel.Name),
                metaModel,
                objectVariable,
                fixedVariables,
                options.ToLogModel,
                options.RegistrationStatusCallBack,
                options.SolvingStatusCallBack);

        return result switch {
            Ok<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> ok =>
                Results.Ok(ConvertSubResult<V>(ok.Value)),
            Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> f =>
                Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResultOf<V>>(f.Error),
            Fatal<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> fat =>
                new Fatal<Framework.Solver.IBendersDecompositionSolver.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>(fat.Errors),
            _ => throw new InvalidOperationException()
        };
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
    /// 转换 SolverOutput 值类型 / Convert SolverOutput value type.
    /// </summary>
    private static SolverOutput ConvertSolverOutput<V>(SolverOutput output, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (output is FeasibleSolverOutput<Flt64> flt64Output) {
            IReadOnlyList<Flt64> flt64Values = flt64Output.Solution.Values;
            var convertedValues = new List<V>(flt64Values.Count);
            foreach (Flt64 val in flt64Values) {
                convertedValues.Add(converter.IntoValue(val));
            }

            return new FeasibleSolverOutput<V>(
                Obj: flt64Output.Obj,
                Solution: new Solution<V>(convertedValues),
                Time: flt64Output.Time,
                PossibleBestObj: flt64Output.PossibleBestObj,
                Gap: flt64Output.Gap);
        }
        return output;
    }

    /// <summary>
    /// 转换 LinearSubResult 值类型 / Convert LinearSubResult value type.
    ///
    /// 仅支持 V = Flt64（恒等转换）。其他类型需要通过带 converter 参数的重载。
    /// Supports V = Flt64 (identity) only. Other types require an overload with converter.
    /// </summary>
    private static Framework.Solver.IBendersDecompositionSolver.LinearSubResultOf<V> ConvertSubResult<V>(
        Framework.Solver.IBendersDecompositionSolver.LinearSubResult subResult)
        where V : struct, IRealNumber<V>, INumberField<V> {
        return subResult switch {
            Framework.Solver.IBendersDecompositionSolver.LinearFeasibleResult feasible =>
                new Framework.Solver.IBendersDecompositionSolver.LinearFeasibleResultOf<V>(
                    Result: ConvertFeasibleOutput<V>(feasible.Result),
                    DualSolution: feasible.DualSolution,
                    Cuts: feasible.Cuts) {
                    Report = feasible.Report,
                },
            Framework.Solver.IBendersDecompositionSolver.LinearInfeasibleResult infeasible =>
                new Framework.Solver.IBendersDecompositionSolver.LinearInfeasibleResultOf<V>(
                    FarkasDualSolution: infeasible.FarkasDualSolution,
                    Cuts: infeasible.Cuts),
            _ => throw new InvalidOperationException($"Unknown LinearSubResult type: {subResult.GetType()}")
        };
    }

    /// <summary>
    /// 转换 FeasibleSolverOutput 值类型 / Convert FeasibleSolverOutput value type.
    /// </summary>
    private static FeasibleSolverOutput<V> ConvertFeasibleOutput<V>(FeasibleSolverOutput<Flt64> output)
        where V : struct, IRealNumber<V>, INumberField<V> {
        IReadOnlyList<Flt64> flt64Values = output.Solution.Values;
        var convertedValues = new List<V>(flt64Values.Count);
        foreach (Flt64 val in flt64Values) {
            convertedValues.Add(Flt64ToV<V>(val));
        }
        return new FeasibleSolverOutput<V>(
            Obj: output.Obj,
            Solution: new Solution<V>(convertedValues),
            Time: output.Time,
            PossibleBestObj: output.PossibleBestObj,
            Gap: output.Gap);
    }

    /// <summary>
    /// Flt64 到泛型值类型的转换 / Convert Flt64 to generic value type.
    ///
    /// 当 V = Flt64 时为恒等转换，其他类型通过反射调用构造函数。
    /// Identity when V = Flt64; other types via reflection constructor.
    /// </summary>
    private static V Flt64ToV<V>(Flt64 value) where V : struct, IRealNumber<V>, INumberField<V> {
        if (typeof(V) == typeof(Flt64)) {
            return (V)(object)value;
        }
        throw new NotSupportedException(
            $"Value conversion from Flt64 to {typeof(V).Name} requires an explicit IIntoValue<V> converter. " +
            "Use SolveSubAsync directly and convert at the caller level.");
    }

    /// <summary>
    /// 传播模型错误为通用结果类型 / Propagate model error to generic result type.
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
