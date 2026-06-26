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
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Gurobi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using SolverConfigGurobi = Fuookami.Ospf.Core.Solver.Config.GurobiSolverConfig;
using FrameworkSolveOptions = Fuookami.Ospf.Framework.Solver.FrameworkSolveOptions;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi Benders 分解求解器实现。
/// Gurobi Benders decomposition solver implementation.
///
/// 使用 Gurobi 求解器实现线性 Benders 分解策略，支持主问题求解和子问题求解（含对偶解和 Farkas 证明提取）。
/// Implements linear Benders decomposition strategy using Gurobi solver, supporting master problem solving
/// and sub-problem solving (with dual solution and Farkas proof extraction).
/// </summary>
public sealed class GurobiBendersDecompositionSolver : Framework.Solver.IBendersDecompositionSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly GurobiLinearSolverCallBack? _callBack;
    private readonly GurobiSolverConfig? _connectionConfig;

    /// <summary>
    /// 创建 Gurobi Benders 分解求解器 / Create Gurobi Benders decomposition solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">回调管理器 / Callback manager.</param>
    /// <param name="connectionConfig">远程连接配置 / Remote connection configuration.</param>
    public GurobiBendersDecompositionSolver(
        SolverConfig? config = null,
        GurobiLinearSolverCallBack? callBack = null,
        GurobiSolverConfig? connectionConfig = null) {
        Config = config ?? new SolverConfigGurobi();
        _callBack = callBack;
        _connectionConfig = connectionConfig;
    }

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
            metaModel.Tokens.SetSolution(ok.Value.Solution.Values);
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
    /// 线性松弛子问题，提取对偶解或 Farkas 证明用于生成 Benders 割平面。
    /// Solves linear-relaxed sub-problem, extracting dual solution or Farkas proof for Benders cut generation.
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

        // Step 2: Build constraint origin map for dual-to-MathConstraint mapping
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> mechConstraints = ((IMechanismModel<Flt64>)mechanismModel).Constraints;
        var constraintOriginMap = new MathConstraint?[mechConstraints.Count];
        for (int i = 0; i < mechConstraints.Count; i++) {
            constraintOriginMap[i] = mechConstraints[i].Origin;
        }

        // Step 3: Build fixed variable index map (IVariableItem -> triad model column index)
        var fixedIndexMap = new Dictionary<int, Flt64>();
        foreach (KeyValuePair<IVariableItem, Flt64> kv in fixedVariables) {
            int? colIndex = metaModel.Tokens.IndexOf(kv.Key);
            if (colIndex is { } ci) {
                fixedIndexMap[ci] = kv.Value;
            }
        }

        // Step 4: Dump mechanism model to triad model, then fix and relax
        GurobiLinearSolver solver = CreateSolver(null);
        LinearTriadModel triadModel = await solver.DumpAsync(mechanismModel);
        LinearTriadModel subModel = FixAndRelaxToLP(triadModel, fixedIndexMap);

        using (triadModel)
        using (subModel) {
            // Step 5: Set up dual/Farkas extraction callbacks
            var dualSolution = new Dictionary<MathConstraint, Flt64>();
            var farkasSolution = new Dictionary<MathConstraint, Flt64>();

            GurobiLinearSolverCallBack subCallBack = (_callBack?.Copy() ?? new GurobiLinearSolverCallBack())
                .Configuration(async (_, grbModel, _, _) => {
                    // Enable Farkas dual extraction for infeasible case
                    grbModel.Set(GRB.IntParam.InfUnbdInfo, 1);
                    return Results.OkInstance;
                })
                .AnalyzingSolution(async (_, _, _, grbConstrs) => {
                    // Extract dual solution (Pi values) on optimal
                    dualSolution.Clear();
                    for (int i = 0; i < grbConstrs.Count; i++) {
                        double pi = grbConstrs[i].Get(GRB.DoubleAttr.Pi);
                        if (System.Math.Abs(pi) > 1e-12) {
                            MathConstraint key = GetMathConstraintKey(i, constraintOriginMap);
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
                            if (System.Math.Abs(farkas) > 1e-12) {
                                MathConstraint key = GetMathConstraintKey(i, constraintOriginMap);
                                farkasSolution[key] = new Flt64(farkas);
                            }
                        }
                    }
                    return Results.OkInstance;
                });

            // Step 6: Solve the sub-problem
            var subSolver = new GurobiLinearSolver(Config, subCallBack, _connectionConfig);
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solveResult =
                await subSolver.InvokeAsync((ILinearTriadModelView)subModel, solvingStatusCallBack);

            // Step 7: Build result
            if (solveResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
                metaModel.Tokens.SetSolution(ok.Value.Solution.Values);
                return Results.Ok<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(
                    new Framework.Solver.IBendersDecompositionSolver.LinearFeasibleResult(
                        Result: ok.Value,
                        DualSolution: dualSolution,
                        Cuts: null));
            }

            if (solveResult is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f) {
                if (f.Error.Code == ErrorCode.ORModelInfeasible) {
                    // Return infeasible result with Farkas duals
                    return Results.Ok<Framework.Solver.IBendersDecompositionSolver.LinearSubResult>(
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
        new GurobiLinearSolver(Config, callBack?.Copy(), _connectionConfig);

    /// <summary>
    /// 转储元模型为机制模型 / Dump meta model to mechanism model.
    /// </summary>
    private async Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpMechanismModelAsync(
        LinearMetaModel<Flt64> metaModel,
        RegistrationStatusCallBack? registrationStatusCallBack) {
        GurobiLinearSolver solver = CreateSolver(_callBack);
        return await solver.DumpAsync(metaModel, registrationStatusCallBack, null);
    }

    /// <summary>
    /// 通过三元组模型求解 / Solve via triad model.
    /// </summary>
    private async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveTriadModelAsync(
        LinearMechanismModel<Flt64> mechanismModel,
        GurobiLinearSolverCallBack? callBack,
        SolvingStatusCallBack? solvingStatusCallBack) {
        GurobiLinearSolver solver = CreateSolver(callBack);
        LinearTriadModel triadModel = await solver.DumpAsync(mechanismModel);

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
                vars.Add(new ModelViewVariable(
                    v.Index, fixedVal, fixedVal,
                    Continuous.Instance, v.Name, v.InitialResult, v.Slack));
            }
            else {
                // Relax to continuous
                vars.Add(new ModelViewVariable(
                    v.Index, v.LowerBound, v.UpperBound,
                    Continuous.Instance, v.Name, v.InitialResult, v.Slack));
            }
        }

        return new LinearTriadModel(vars, (LinearConstraintBatch)model.Constraints, model.Objective, model.Name);
    }

    /// <summary>
    /// 根据索引获取对偶解的 MathConstraint 键 / Get MathConstraint key for dual solution by index.
    /// </summary>
    private static MathConstraint GetMathConstraintKey(int index, MathConstraint?[] originMap) {
        if (index >= 0 && index < originMap.Length && originMap[index] is { } mc) {
            return mc;
        }
        return new FallbackMathConstraint(index);
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
                    Cuts: feasible.Cuts),
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
