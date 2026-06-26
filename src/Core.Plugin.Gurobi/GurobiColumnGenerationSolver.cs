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
using Gurobi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using SolverConfigGurobi = Fuookami.Ospf.Core.Solver.Config.GurobiSolverConfig;

namespace Fuookami.Ospf.Core.Plugin.Gurobi;

/// <summary>
/// Gurobi 列生成求解器实现。
/// Gurobi column generation solver implementation.
///
/// 使用 Gurobi 求解器实现列生成策略，支持 MILP 求解、多解求解和 LP 松弛求解（含对偶解提取）。
/// Implements column generation strategy using Gurobi solver, supporting MILP solving,
/// multi-solution solving, and LP relaxation solving (with dual solution extraction).
/// </summary>
public sealed class GurobiColumnGenerationSolver : Framework.Solver.IColumnGenerationSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    public string Name => "gurobi";

    /// <summary>求解器配置 / Solver configuration.</summary>
    public SolverConfig Config { get; }

    private readonly GurobiLinearSolverCallBack? _callBack;
    private readonly GurobiSolverConfig? _connectionConfig;

    /// <summary>
    /// 创建 Gurobi 列生成求解器 / Create Gurobi column generation solver.
    /// </summary>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <param name="callBack">回调管理器 / Callback manager.</param>
    /// <param name="connectionConfig">远程连接配置 / Remote connection configuration.</param>
    public GurobiColumnGenerationSolver(
        SolverConfig? config = null,
        GurobiLinearSolverCallBack? callBack = null,
        GurobiSolverConfig? connectionConfig = null) {
        Config = config ?? new SolverConfigGurobi();
        _callBack = callBack;
        _connectionConfig = connectionConfig;
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
            await SolveTriadModelAsync(mechanismModel, _callBack, solvingStatusCallBack);

        if (solveResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            metaModel.Tokens.SetSolution(ok.Value.Solution.Values);
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
        GurobiLinearSolver solver = CreateSolver(_callBack);
        LinearTriadModel triadModel = await solver.DumpAsync(mechanismModel);

        using (triadModel) {
            Result<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>> solveResult =
                await solver.InvokeAsync(triadModel, amount, solvingStatusCallBack);

            if (solveResult is Ok<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>> ok) {
                metaModel.Tokens.SetSolution(ok.Value.Item1.Solution.Values);
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
    /// 求解 LP 松弛问题（含对偶解提取）/ Solve LP relaxation problem (with dual solution extraction).
    /// </summary>
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
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult =
            await DumpMechanismModelAsync(metaModel, registrationStatusCallBack);
        if (mechanismResult is not Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>) {
            return PropagateModelResult<Framework.Solver.IColumnGenerationSolver.LpResult>(mechanismResult);
        }

        using LinearMechanismModel<Flt64> mechanismModel = mechanismResult.Value;

        // Build constraint index → MathConstraint mapping from mechanism model
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> mechConstraints = ((IMechanismModel<Flt64>)mechanismModel).Constraints;
        var constraintOriginMap = new MathConstraint?[mechConstraints.Count];
        for (int i = 0; i < mechConstraints.Count; i++) {
            constraintOriginMap[i] = mechConstraints[i].Origin;
        }

        // Dual solution dictionary populated by the AnalyzingSolution callback
        var dualSolution = new Dictionary<MathConstraint, Flt64>();

        GurobiLinearSolverCallBack lpCallBack = (_callBack?.Copy() ?? new GurobiLinearSolverCallBack())
            .AnalyzingSolution(async (_, _, _, grbConstrs) => {
                for (int i = 0; i < grbConstrs.Count; i++) {
                    var pi = new Flt64(grbConstrs[i].Get(GRB.DoubleAttr.Pi));
                    MathConstraint key = GetMathConstraintKey(i, constraintOriginMap);
                    dualSolution[key] = pi;
                }
                return Results.OkInstance;
            });

        GurobiLinearSolver solver = CreateSolver(lpCallBack);
        LinearTriadModel triadModel = await solver.DumpAsync(mechanismModel);
        LinearTriadModel relaxedModel = RelaxToLP(triadModel);

        using (triadModel)
        using (relaxedModel) {
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solveResult =
                await solver.InvokeAsync((ILinearTriadModelView)relaxedModel, solvingStatusCallBack);

            if (solveResult is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
                metaModel.Tokens.SetSolution(ok.Value.Solution.Values);
                return Results.Ok(new Framework.Solver.IColumnGenerationSolver.LpResult(ok.Value, dualSolution));
            }

            if (solveResult is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f) {
                return Results.Failed<Framework.Solver.IColumnGenerationSolver.LpResult>(f.Error);
            }

            if (solveResult is Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> fat) {
                return new Fatal<Framework.Solver.IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>(fat.Errors);
            }

            return Results.Failed<Framework.Solver.IColumnGenerationSolver.LpResult>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException));
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
            relaxedVars.Add(new ModelViewVariable(
                v.Index,
                v.LowerBound,
                v.UpperBound,
                Continuous.Instance,
                v.Name,
                v.InitialResult,
                v.Slack));
        }

        return new LinearTriadModel(
            relaxedVars,
            (LinearConstraintBatch)model.Constraints,
            model.Objective,
            model.Name);
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

    /// <summary>
    /// 根据索引获取对偶解的 MathConstraint 键 / Get MathConstraint key for dual solution by index.
    /// </summary>
    private static MathConstraint GetMathConstraintKey(int index, MathConstraint?[] originMap) {
        if (index >= 0 && index < originMap.Length && originMap[index] is { } mc) {
            return mc;
        }
        return new FallbackMathConstraint(index);
    }
}

/// <summary>
/// 后备数学约束（用于对偶解映射中无 Origin 时的占位）。
/// Fallback math constraint (placeholder when Origin is absent in dual mapping).
/// </summary>
internal sealed record FallbackMathConstraint(int Index) : MathConstraint {
    /// <inheritdoc/>
    public IMetaConstraintGroup? Group => null;
    /// <inheritdoc/>
    public bool Lazy => false;
    /// <inheritdoc/>
    public object? Args => null;
    /// <inheritdoc/>
    public int Priority => 0;
    /// <inheritdoc/>
    public string Name => $"__fallback_{Index}";
    /// <inheritdoc/>
    public string? DisplayName => null;
}
