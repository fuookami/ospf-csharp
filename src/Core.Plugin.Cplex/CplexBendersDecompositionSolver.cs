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

namespace Fuookami.Ospf.Core.Plugin.Cplex;

/// <summary>
/// Cplex Benders 分解求解器实现。
/// Cplex Benders decomposition solver implementation.
///
/// 使用 Cplex 求解器实现线性 Benders 分解策略，支持主问题求解和子问题求解。
/// 运行时需加载 Cplex 托管 DLL，否则返回 OREngineEnvironmentLost。
/// Implements linear Benders decomposition strategy using Cplex solver, supporting master
/// and sub-problem solving. Requires Cplex managed DLL at runtime.
/// </summary>
public sealed class CplexBendersDecompositionSolver : Framework.Solver.IBendersDecompositionSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "Cplex";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    /// <summary>
    /// 创建 Cplex Benders 分解求解器 / Create Cplex Benders decomposition solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    public CplexBendersDecompositionSolver(SolverConfig? config = null) {
        Config = config ?? new SolverConfig();
    }

    /// <inheritdoc/>
    public Framework.Solver.IBendersDecompositionSolver WithTimeLimit(TimeSpan timeLimit) =>
        new CplexBendersDecompositionSolver(Config with { TimeLimit = timeLimit });

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
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await DumpMechanismModelAsync(metaModel, registrationStatusCallBack);
        if (mechanismResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismOk) {
            return CplexDualSupport.Propagate<SolveReport<Flt64>, LinearMechanismModel<Flt64>>(
                mechanismResult,
                "CPLEX 主问题模型转储失败 / CPLEX master model dump failed");
        }

        using LinearMechanismModel<Flt64> mechanismModel = mechanismOk.Value;
        CplexLinearSolver solver = CreateSolver();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult =
            await solver.DumpAsync(mechanismModel);
        if (triadResult is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadOk) {
            return CplexDualSupport.Propagate<SolveReport<Flt64>, LinearTriadModel>(
                triadResult,
                "CPLEX 主问题矩阵转换失败 / CPLEX master matrix conversion failed");
        }

        using LinearTriadModel triadModel = triadOk.Value;
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> reportResult =
            await solver.InvokeReportAsync(
                model: triadModel,
                solvingStatusCallBack: solvingStatusCallBack);
        if (reportResult is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> reportOk) {
            return CplexDualSupport.Propagate<SolveReport<Flt64>, SolveReport<Flt64>>(
                reportResult,
                "CPLEX 主问题求解失败 / CPLEX master solve failed");
        }
        if (reportOk.Value.Solution is { } solution) {
            Result<Success, ErrorCode, Error<ErrorCode>> applied =
                mechanismModel.ApplySolverSolution(solution.Values.Values);
            if (!applied.IsOk) {
                return CplexDualSupport.Propagate<SolveReport<Flt64>, Success>(
                    applied,
                    "CPLEX 主问题解回填失败 / CPLEX master solution writeback failed");
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
    /// 可行子问题返回 Cplex 原生行对偶，不可行子问题返回原生 Farkas 乘子；cuts 仍由调用方生成。
    /// Feasible sub-problems return Cplex row duals and infeasible ones return native Farkas multipliers;
    /// cuts remain the caller's responsibility.
    /// </summary>
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
        // Step 1: Dump meta model to mechanism model
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await DumpMechanismModelAsync(metaModel, registrationStatusCallBack);
        if (mechanismResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>) {
            return PropagateSubModelResult(mechanismResult);
        }

        using LinearMechanismModel<Flt64> mechanismModel = mechanismResult.Value;

        // Step 2: Build fixed variable index map (IVariableItem -> triad model column index)
        var fixedIndexMap = new Dictionary<int, Flt64>();
        foreach (KeyValuePair<IVariableItem, Flt64> kv in fixedVariables) {
            int? colIndex = mechanismModel.Tokens.IndexOf(kv.Key);
            if (colIndex is { } ci) {
                fixedIndexMap[ci] = kv.Value;
            }
        }

        // Step 3: Dump mechanism model to triad model, then fix and relax
        CplexLinearSolver solver = CreateSolver();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult =
            await solver.DumpAsync(mechanismModel);
        if (triadResult is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadOk) {
            return triadResult.Bind<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(_ => Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(new Err<ErrorCode>(
                ErrorCode.OREngineModelingException,
                "线性模型转换结果无效 / Invalid linear model conversion result")));
        }
        LinearTriadModel triadModel = triadOk.Value;
        LinearTriadModel subModel = FixAndRelaxToLP(triadModel, fixedIndexMap);

        using (triadModel)
        using (subModel) {
            MathConstraint?[] originMap = MathConstraintOrigins.OriginMap(mechanismModel);
            Result<CplexLinearOutcome, ErrorCode, Error<ErrorCode>> solved =
                await solver.InvokeReportWithDualsAsync(
                    model: subModel,
                    solvingStatusCallBack: solvingStatusCallBack,
                    requireFarkas: true);
            if (solved is not Ok<CplexLinearOutcome, ErrorCode, Error<ErrorCode>> solvedOk) {
                return CplexDualSupport.Propagate<
                    Framework.Solver.IBendersDecompositionSolver.LinearSubResult,
                    CplexLinearOutcome>(
                    solved,
                    "Cplex Benders 子问题求解失败 / Cplex Benders sub-problem solving failed");
            }

            CplexLinearOutcome outcome = solvedOk.Value;
            if (outcome.Report.ProblemStatus == ProblemStatus.Feasible
                && outcome.RowDuals is { } nativeDuals) {
                Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> tidyDuals =
                    subModel.TidyDualSolution(nativeDuals);
                if (tidyDuals is not Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> dualsOk) {
                    return CplexDualSupport.Propagate<
                        Framework.Solver.IBendersDecompositionSolver.LinearSubResult,
                        IReadOnlyList<Flt64>>(
                        tidyDuals,
                        "Cplex 对偶值与子问题行数不一致 / Cplex duals do not align with the sub-problem rows");
                }
                Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report =
                    Results.Ok(outcome.Report);
                Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy =
                    CplexReportSupport.ToLegacy(report);
                if (legacy is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> output) {
                    return CplexDualSupport.Propagate<
                        Framework.Solver.IBendersDecompositionSolver.LinearSubResult,
                        FeasibleSolverOutput<Flt64>>(
                        legacy,
                        "Cplex Benders 子问题无可返回的可行解 / Cplex Benders sub-problem has no feasible output");
                }
                IReadOnlyDictionary<MathConstraint, Flt64> dualSolution =
                    MathConstraintOrigins.ToDualSolution(dualsOk.Value, originMap);
                return mechanismModel.ApplySolverSolution(output.Value.Solution.Values)
                    .Map<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(_ =>
                        new Framework.Solver.IBendersDecompositionSolver.LinearFeasibleResult(
                            Result: output.Value,
                            DualSolution: dualSolution,
                            Cuts: null) {
                            Report = outcome.Report,
                        });
            }

            if (outcome.Report.ProblemStatus == ProblemStatus.Infeasible
                && outcome.FarkasDuals is { } nativeFarkas) {
                Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> tidyFarkas =
                    subModel.TidyDualSolution(nativeFarkas);
                if (tidyFarkas is not Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> farkasOk) {
                    return CplexDualSupport.Propagate<
                        Framework.Solver.IBendersDecompositionSolver.LinearSubResult,
                        IReadOnlyList<Flt64>>(
                        tidyFarkas,
                        "Cplex Farkas 值与子问题行数不一致 / Cplex Farkas values do not align with the sub-problem rows");
                }
                Result<IReadOnlyDictionary<MathConstraint, Flt64>, ErrorCode, Error<ErrorCode>> farkas =
                    MathConstraintOrigins.ToFarkasSolution(farkasOk.Value, originMap, "Cplex");
                if (farkas is not Ok<IReadOnlyDictionary<MathConstraint, Flt64>, ErrorCode, Error<ErrorCode>> farkasResult) {
                    return CplexDualSupport.Propagate<
                        Framework.Solver.IBendersDecompositionSolver.LinearSubResult,
                        IReadOnlyDictionary<MathConstraint, Flt64>>(
                        farkas,
                        "Cplex Benders 子问题没有可用的 Farkas 证书 / Cplex Benders sub-problem has no usable Farkas certificate");
                }
                return Results.Ok<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(
                    new Framework.Solver.IBendersDecompositionSolver.LinearInfeasibleResult(
                        FarkasDualSolution: farkasResult.Value,
                        Cuts: null));
            }

            return Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(
                new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    $"Cplex Benders 子问题未给出可用对偶或 Farkas 证据：{outcome.Report.ProblemStatus} / " +
                    $"Cplex Benders sub-problem returned neither usable duals nor Farkas evidence: {outcome.Report.ProblemStatus}"));
        }
    }

    /// <summary>
    /// 使用值转换器求解线性 Benders 子问题 / Solve linear Benders sub with value converter.
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
    /// 固定变量并松弛为 LP / Fix specified variables and relax all to LP.
    /// </summary>
    /// <param name="model">原始三元组模型 / Original triad model.</param>
    /// <param name="fixedVarMap">固定变量映射（列索引 -> 值）/ Fixed variable map (col index -> value).</param>
    /// <returns>松弛后的模型 / Relaxed model with fixed variables.</returns>
    private static LinearTriadModel FixAndRelaxToLP(
        LinearTriadModel model,
        IReadOnlyDictionary<int, Flt64> fixedVarMap) {
        var vars = new List<ModelViewVariable>(model.Variables.Count);
        foreach (ModelViewVariable v in model.Variables) {
            if (fixedVarMap.ContainsKey(v.Index)) {
                // Fix variable: LB = UB = fixed value, relax to continuous
                Flt64 fixedVal = fixedVarMap[v.Index];
                vars.Add(v.WithDomain(
                    lowerBound: fixedVal,
                    upperBound: fixedVal,
                    type: Continuous.Instance));
            }
            else {
                // Relax to continuous
                vars.Add(v.WithDomain(
                    lowerBound: v.LowerBound,
                    upperBound: v.UpperBound,
                    type: Continuous.Instance));
            }
        }

        return new LinearTriadModel(
            variables: vars,
            constraints: (LinearConstraintBatch)model.Constraints.Copy(),
            objective: model.Objective,
            name: model.Name);
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
    /// 传播机制模型错误为子问题结果类型 / Propagate mechanism model error to sub-result type.
    /// </summary>
    private static Result<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> PropagateSubModelResult(
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> r) {
        if (r is Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(f.Error);
        }
        if (r is Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> fat) {
            return new Fatal<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>(fat.Errors);
        }
        return Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(
            new Err<ErrorCode>(ErrorCode.OREngineSolvingException));
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
