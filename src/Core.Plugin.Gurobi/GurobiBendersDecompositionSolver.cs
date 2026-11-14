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
using Gurobi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using FrameworkSolveOptions = Fuookami.Ospf.Framework.Solver.FrameworkSolveOptions;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi Benders 分解求解器实现。
/// Gurobi Benders decomposition solver implementation.
///
/// 使用 Gurobi 求解器实现线性 Benders 分解策略，支持主问题和子问题求解及行乘子提取。
/// Implements linear Benders decomposition strategy using Gurobi solver, supporting master problem solving
/// and sub-problem solving with row multiplier extraction.
/// </summary>
public sealed class GurobiBendersDecompositionSolver : Framework.Solver.IBendersDecompositionSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly GurobiLinearSolverCallBack? _callBack;
    private readonly GurobiBackendIdentity _identity;

    /// <summary>
    /// 创建 Gurobi Benders 分解求解器 / Create Gurobi Benders decomposition solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">回调管理器 / Callback manager.</param>
    public GurobiBendersDecompositionSolver(
        SolverConfig? config = null,
        GurobiLinearSolverCallBack? callBack = null)
        : this(config, callBack, GurobiBackendIdentity.Gurobi) {
    }

    internal GurobiBendersDecompositionSolver(
        SolverConfig? config,
        GurobiLinearSolverCallBack? callBack,
        GurobiBackendIdentity identity) {
        Config = config ?? new SolverConfig();
        _callBack = callBack;
        _identity = identity;
    }

    /// <inheritdoc/>
    public Framework.Solver.IBendersDecompositionSolver WithTimeLimit(TimeSpan timeLimit) =>
        WithTimeLimitCore(timeLimit);

    internal GurobiBendersDecompositionSolver WithTimeLimitCore(TimeSpan timeLimit) =>
        new(Config with { TimeLimit = timeLimit }, _callBack, _identity);

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
            await SolveTriadModelAsync(mechanismModel, _callBack, solvingStatusCallBack);

        if (solveResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return Results.Ok<SolverOutput>(ok.Value);
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
            return Propagate<SolveReport<Flt64>, LinearMechanismModel<Flt64>>(mechanismResult);
        }

        using LinearMechanismModel<Flt64> mechanismModel = mechanismOk.Value;
        GurobiLinearSolver solver = CreateSolver(_callBack);
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult =
            await solver.DumpAsync(mechanismModel);
        if (triadResult is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadOk) {
            return Propagate<SolveReport<Flt64>, LinearTriadModel>(triadResult);
        }

        using LinearTriadModel triadModel = triadOk.Value;
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> reportResult =
            await solver.InvokeReportAsync(
                model: triadModel,
                solvingStatusCallBack: solvingStatusCallBack);
        if (reportResult is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> reportOk) {
            return Propagate<SolveReport<Flt64>, SolveReport<Flt64>>(reportResult);
        }
        if (reportOk.Value.Solution is { } solution) {
            Try applied = mechanismModel.ApplySolverSolution(
                matrix: triadModel,
                solution: solution.Values.Values);
            if (!applied.IsOk) {
                return Propagate<SolveReport<Flt64>, Success>(applied);
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
    /// 线性松弛子问题，提取对偶值或 Farkas 行乘子，不生成割。
    /// Solves the linear-relaxed subproblem and extracts duals or Farkas row multipliers without generating cuts.
    /// </summary>
    /// <remarks>
    /// 行乘子未作为含变量界的完整证明独立验证；构建和转储成功后求解失败仍可能保留辅助 token。
    /// Row multipliers are not independently verified as a complete certificate including variable bounds; a later solver failure may retain committed auxiliary tokens.
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
        if (!built.IsOk) {
            return built.Bind<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(_ => Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(new Err<ErrorCode>(
                ErrorCode.OREngineModelingException,
                "线性模型构建结果无效 / Invalid linear model build result")));
        }

        using LinearMechanismModel<Flt64> mechanismModel = built.Value.Model;
        using LinearTriadModel triadModel = built.Value.Matrix;

        // Step 2: Build constraint origin map for dual-to-MathConstraint mapping
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> mechConstraints = ((IMechanismModel<Flt64>)mechanismModel).Constraints;
        var constraintOriginMap = new MathConstraint?[mechConstraints.Count];
        for (int i = 0; i < mechConstraints.Count; i++) {
            constraintOriginMap[i] = mechConstraints[i].Origin;
        }

        // 固定列代换后松弛其余列 / Substitute fixed columns, then relax the remaining columns.
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> relaxation = triadModel.RelaxToContinuous();
        if (!relaxation.IsOk) {
            return relaxation.Bind<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(_ =>
                Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(new Err<ErrorCode>(
                    ErrorCode.OREngineModelingException,
                    "松弛模型转换结果无效 / Invalid relaxation result")));
        }
        LinearTriadModel subModel = relaxation.Value;

        using (subModel) {
            // Step 5: Set up dual/Farkas extraction callbacks
            var dualSolution = new Dictionary<MathConstraint, Flt64>(ReferenceEqualityComparer.Instance);
            var farkasSolution = new Dictionary<MathConstraint, Flt64>(ReferenceEqualityComparer.Instance);
            bool farkasCaptured = false;

            GurobiLinearSolverCallBack subCallBack = (_callBack?.Copy() ?? new GurobiLinearSolverCallBack())
                .Configuration(async (_, grbModel, _, _) => {
                    // Enable Farkas dual extraction for infeasible case
                    grbModel.Set(GRB.IntParam.InfUnbdInfo, 1);
                    grbModel.Set(GRB.IntParam.DualReductions, 0);
                    return Results.OkInstance;
                })
                .AnalyzingSolution(async (_, _, _, grbConstrs) => {
                    // Extract dual solution (Pi values) on optimal
                    dualSolution.Clear();
                    for (int i = 0; i < grbConstrs.Count; i++) {
                        double pi = grbConstrs[i].Get(GRB.DoubleAttr.Pi);
                        if (System.Math.Abs(pi) > 1e-12) {
                            MathConstraint key = MathConstraintOrigins.ResolveOriginKey(i, constraintOriginMap);
                            dualSolution[key] = new Flt64(pi);
                        }
                    }
                    return Results.OkInstance;
                })
                .AfterFailure(async (status, _, _, grbConstrs) => {
                    // Extract Farkas dual on infeasible
                    if (status == SolverStatus.Infeasible) {
                        farkasSolution.Clear();
                        for (int i = 0; i < grbConstrs.Count; i++) {
                            double farkas = grbConstrs[i].Get(GRB.DoubleAttr.FarkasDual);
                            if (!double.IsFinite(farkas)) {
                                return Results.Failed<Success>(new Err<ErrorCode>(
                                    ErrorCode.OREngineSolvingException,
                                    "Farkas 对偶值无效 / Invalid Farkas dual value"));
                            }
                            if (farkas != 0.0) {
                                MathConstraint key = MathConstraintOrigins.ResolveOriginKey(i, constraintOriginMap);
                                farkasSolution[key] = new Flt64(farkas);
                            }
                        }
                        if (farkasSolution.Count == 0) {
                            return Results.Failed<Success>(new Err<ErrorCode>(
                                ErrorCode.OREngineSolvingException,
                                "Farkas 对偶结果不可用 / Farkas dual result is unavailable"));
                        }
                        farkasCaptured = true;
                    }
                    return Results.OkInstance;
                });

            // Step 6: Solve the sub-problem
            var subSolver = new GurobiLinearSolver(Config, subCallBack, _identity);
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> reportResult =
                await subSolver.InvokeReportAsync(
                    model: subModel,
                    solvingStatusCallBack: solvingStatusCallBack);
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solveResult =
                reportResult is Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> reportOk
                    ? GurobiReportSupport.ToLegacy(Results.Ok(reportOk.Value))
                    : Propagate<FeasibleSolverOutput<Flt64>, SolveReport<Flt64>>(reportResult);

            // Step 7: Build result
            if (solveResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
                SolveReport<Flt64> report =
                    ((Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>)reportResult).Value;
                Flt64[] reducedSolution = ok.Value.Solution.Values.ToArray();
                return mechanismModel.RestoreSolverSolution(
                    matrix: triadModel,
                    solvedMatrix: subModel,
                    solution: reducedSolution
                )
                    .Bind(values => mechanismModel.ApplySolverSolution(
                        matrix: triadModel,
                        solvedMatrix: subModel,
                        solution: reducedSolution
                    )
                        .Map<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(_ =>
                            new Framework.Solver.IBendersDecompositionSolver.LinearFeasibleResult(
                                Result: ok.Value with { Solution = new Solution<Flt64>(values) },
                                DualSolution: dualSolution,
                                Cuts: null) {
                                Report = report,
                            }));
            }

            if (solveResult is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f) {
                if (f.Error.Code == ErrorCode.ORModelInfeasible && farkasCaptured) {
                    // Return infeasible result with Farkas duals
                    return mechanismModel.ValidateSolverTokenSnapshot(triadModel, subModel)
                        .Map<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(_ =>
                            new Framework.Solver.IBendersDecompositionSolver.LinearInfeasibleResult(
                                FarkasDualSolution: farkasSolution,
                                Cuts: null));
                }
                return Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(f.Error);
            }

            if (solveResult is Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fat) {
                return new Fatal<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>(fat.Errors);
            }

            return Results.Failed<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(
                new Err<ErrorCode>(ErrorCode.OREngineSolvingException));
        }
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
    /// 创建 Gurobi 线性求解器 / Create Gurobi linear solver.
    /// </summary>
    private GurobiLinearSolver CreateSolver(GurobiLinearSolverCallBack? callBack) =>
        new(Config, callBack?.Copy(), _identity);

    /// <summary>
    /// 转储元模型为机制模型 / Dump meta model to mechanism model.
    /// </summary>
    private async Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpMechanismModelAsync(
        LinearMetaModel<Flt64> metaModel,
        RegistrationStatusCallBack? registrationStatusCallBack) {
        return await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: metaModel,
            concurrent: Config.DumpMechanismModelConcurrent,
            blocking: Config.DumpMechanismModelBlocking,
            registrationStatusCallBack: registrationStatusCallBack
        );
    }

    /// <summary>
    /// 通过三元组模型求解 / Solve via triad model.
    /// </summary>
    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveTriadModelAsync(
        LinearMechanismModel<Flt64> mechanismModel,
        GurobiLinearSolverCallBack? callBack,
        SolvingStatusCallBack? solvingStatusCallBack) {
        GurobiLinearSolver solver = CreateSolver(callBack);
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult =
            await solver.DumpAsync(mechanismModel);
        if (triadResult is not Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadOk) {
            return triadResult.Bind<FeasibleSolverOutput<Flt64>>(_ => Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
                ErrorCode.OREngineModelingException,
                "线性模型转换结果无效 / Invalid linear model conversion result")));
        }
        LinearTriadModel triadModel = triadOk.Value;

        using (triadModel) {
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
                await solver.InvokeAsync((ILinearTriadModelView)triadModel, solvingStatusCallBack);
            return result is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok
                ? mechanismModel.ApplySolverSolution(
                    matrix: triadModel,
                    solution: ok.Value.Solution.Values
                ).Bind(_ => result)
                : result;
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

    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, S>(
        Result<S, ErrorCode, Error<ErrorCode>> result) {
        return result switch {
            Failed<S, ErrorCode, Error<ErrorCode>> failed =>
                Results.Failed<T>(failed.Error),
            Fatal<S, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<T>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException)),
        };
    }
}
