#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Progress;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Solver;
/// <summary>
/// 列生成求解器接口 / Column generation solver interface.
/// </summary>
public interface IColumnGenerationSolver {
    /// <summary>求解器名称 / Solver name.</summary>
    string Name { get; }

    /// <summary>
    /// 求解 MILP 问题 / Solve MILP problem.
    /// </summary>
    Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null);

    /// <summary>
    /// 使用选项求解 MILP 问题 / Solve MILP problem with options.
    /// </summary>
    async Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options) {
        // 有进度上下文时，用适配器**包装**既有回调：先上报进度，再调用原回调。
        // 没有上下文时原样传下去，行为完全不变。
        // With a progress context, the adapters **wrap** the existing callbacks: report progress first, then invoke the original. Without a context, callbacks pass
        // through unchanged.
        RegistrationStatusCallBack? registration = options.ProgressContext is null
            ? options.RegistrationStatusCallBack
            : options.ProgressContext.RegistrationCallback(options.RegistrationStatusCallBack);
        SolvingStatusCallBack? solving = options.ProgressContext is null
            ? options.SolvingStatusCallBack
            : options.ProgressContext.SolvingCallback(
                Fuookami.Ospf.Core.Solver.Progress.SolverStages.Milp,
                options.SolvingStatusCallBack);
        // **求解前后各报一次进度**：0% 与 100%。只在成功后报 100%——失败时进度停在中途，
        // 调用方能从"进度没走完"判断这次没跑完，而不是看到一个假的完成。
        // **Progress is reported before and after the solve**, at 0% and 100%. The 100% report happens only on success, so a caller can read an incomplete bar as an incomplete solve rather
        // than a false completion.
        ReportProgress(options, metaModel, 0, 30);
        ulong? solutionAmount = options.SolutionAmount;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result;
        if (solutionAmount is null) {
            result = await SolveMILPAsync(
                options.SolveName(metaModel.Name),
                metaModel,
                options.ToLogModel,
                registration,
                solving);
        } else {
            Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>> poolResult = await SolveMILPAsync(
                options.SolveName(metaModel.Name),
                metaModel,
                solutionAmount.Value,
                options.ToLogModel,
                registration,
                solving);
            result = poolResult.Map(r => r.Output);
        }
        if (result is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>) {
            ReportProgress(options, metaModel, 100, 100);
        }
        return result;
    }

    /// <summary>
    /// 上报一次进度 / Report one progress snapshot.
    /// </summary>
    /// <param name="options">求解选项 / The solve options.</param>
    /// <param name="metaModel">线性元模型 / The linear meta model.</param>
    /// <param name="progressInStage">阶段内进度 / Progress within the stage.</param>
    /// <param name="overallProgress">总进度 / Overall progress.</param>
    /// <remarks>
    /// **没有进度上下文时什么都不做**：上报本身不是求解的一部分，缺了它求解结果不变。
    /// 诊断里带模型名与求解器名——多求解器并行时，日志里只有百分比无法区分是谁在跑。
    ///
    /// **Does nothing without a progress context**: reporting is not part of solving and omitting it changes no result. The diagnostics carry the model and solver names, since with several
    /// solvers running a bare percentage cannot tell them apart.
    /// </remarks>
    void ReportProgress(
        FrameworkSolveOptions options,
        LinearMetaModel<Flt64> metaModel,
        int progressInStage,
        int overallProgress) {
        options.ProgressContext?.Report(new Fuookami.Ospf.Core.Solver.Progress.SolverProgressSnapshot(
            Stage: Fuookami.Ospf.Core.Solver.Progress.SolverStages.Milp,
            ProgressInStage: progressInStage,
            OverallProgress: overallProgress,
            Diagnostics: new Dictionary<string, string> {
                ["model"] = metaModel.Name,
                ["solver"] = Name,
            }));
    }

    /// <summary>
    /// 求解 MILP 问题并返回解池 / Solve MILP problem and return a solution pool.
    /// </summary>
    Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        ulong amount,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null);

    /// <summary>
    /// 使用值转换器求解 MILP 问题 / Solve MILP problem with value converter.
    /// </summary>
    Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V>;

    /// <summary>
    /// 求解 LP 问题 / Solve LP problem.
    /// </summary>
    Task<Result<LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null);

    /// <summary>
    /// 使用选项求解 LP 问题（便捷重载）/ Solve LP problem with options (convenience overload).
    /// </summary>
    /// <param name="metaModel">线性元模型 / Linear meta model.</param>
    /// <param name="options">框架求解选项 / Framework solve options.</param>
    /// <returns>LP 求解结果 / LP solve result.</returns>
    /// <remarks>
    /// **`solveLP(metaModel, options)` 是主问题阶段的入口**，它在求解**前后各上报一次**进度，
    /// 并用适配器把回调包装成 `MasterLP` 阶段：
    ///
    /// ```
    /// progress?.report(MasterLP, progressInStage = 0, overallProgress = 30, ...)   ← 求解前
    /// registrationStatusCallBack = progress?.registrationCallback(...)
    /// solvingStatusCallBack = progress?.solvingCallback(SolverStages.MasterLP, ...)
    /// ...
    /// progress?.report(...)                                                        ← 求解后
    /// ```
    ///
    /// C# 侧原先**没有这个重载**，只有带 `name` 的那个——所以主问题阶段
    /// **既没有阶段快照，也没有 `MasterLP` 的适配包装**。这是 只接了一条路径的直接原因。
    ///
    /// **The code's `solveLP(metaModel, options)` is the master-problem entry point**; it reports progress **once before and once after** the solve and wraps the
    /// callbacks with the adapters under the `MasterLP` stage (snippet above).
    ///
    /// The C# side **had no such overload**, only the one taking `name` — so the master-problem stage had **neither a stage snapshot nor `MasterLP` adapter
    /// wrapping**. That is the direct reason wired only one path.
    /// </remarks>
    async Task<Result<LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
        LinearMetaModel<Flt64> metaModel,
        FrameworkSolveOptions options) {
        SolverProgressContext? progress = options.ProgressContext;
        progress?.Report(new SolverProgressSnapshot(
            Stage: SolverStages.MasterLp,
            ProgressInStage: 0,
            OverallProgress: 30,
            Diagnostics: new Dictionary<string, string> {
                ["model"] = metaModel.Name,
                ["solver"] = Name,
            }));
        Result<LpResult, ErrorCode, Error<ErrorCode>> result = await SolveLPAsync(
            name: options.SolveName(metaModel.Name),
            metaModel: metaModel,
            toLogModel: options.ToLogModel,
            registrationStatusCallBack: progress?.RegistrationCallback(options.RegistrationStatusCallBack)
                ?? options.RegistrationStatusCallBack,
            solvingStatusCallBack: progress?.SolvingCallback(SolverStages.MasterLp, options.SolvingStatusCallBack)
                ?? options.SolvingStatusCallBack);
        if (result.IsOk) {
            progress?.Report(new SolverProgressSnapshot(
                Stage: SolverStages.MasterLp,
                ProgressInStage: 100,
                OverallProgress: 40,
                Diagnostics: new Dictionary<string, string> {
                    ["model"] = metaModel.Name,
                    ["solver"] = Name,
                }));
        }
        return result;
    }

    /// <summary>
    /// 求解 MILP 并区分可行与不可行 / Solve MILP and distinguish feasible from infeasible.
    /// </summary>
    /// <param name="name">求解名 / The solve name.</param>
    /// <param name="metaModel">线性元模型 / The linear meta model.</param>
    /// <param name="toLogModel">是否记录模型 / Whether to log the model.</param>
    /// <param name="registrationStatusCallBack">注册回调 / The registration callback.</param>
    /// <param name="solvingStatusCallBack">求解回调 / The solving callback.</param>
    /// <returns>带状态的结果 / The result with status.</returns>
    /// <remarks>
    /// **默认实现只在可行时包一层**：默认实现也是"调 `solveMILP`，成功包 `Feasible`"，
    /// 它**不产生 `Infeasible`**——那要具体求解器报告不可行性才有意义。所以这里如实沿用，
    /// 而不是假装能检测不可行。
    ///
    /// **The default implementation only wraps a feasible outcome**: its default also calls `solveMILP` and wraps success in `Feasible`, and it **never produces `Infeasible`** — that
    /// requires a concrete solver reporting infeasibility. This copies that honestly rather than pretending to detect infeasibility.
    /// </remarks>
    async Task<Result<MilpSolveResult, ErrorCode, Error<ErrorCode>>> SolveMILPWithStatusAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await SolveMILPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack);
        return result is Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ok
            ? Results.Ok<MilpSolveResult>(new MilpSolveResult.Feasible(ok.Value))
            : result is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> failed
                ? Results.Failed<MilpSolveResult>(failed.Error)
                : throw new InvalidOperationException("unreachable");
    }

    /// <summary>
    /// 求解 LP 并区分可行与不可行 / Solve LP and distinguish feasible from infeasible.
    /// </summary>
    /// <param name="name">求解名 / The solve name.</param>
    /// <param name="metaModel">线性元模型 / The linear meta model.</param>
    /// <param name="toLogModel">是否记录模型 / Whether to log the model.</param>
    /// <param name="registrationStatusCallBack">注册回调 / The registration callback.</param>
    /// <param name="solvingStatusCallBack">求解回调 / The solving callback.</param>
    /// <returns>带状态的结果 / The result with status.</returns>
    /// <remarks>同 <see cref="SolveMILPWithStatusAsync"/>：默认实现不产生 `Infeasible` / As with `SolveMILPWithStatusAsync`, the default never yields `Infeasible`.</remarks>
    async Task<Result<LpResultWithStatus, ErrorCode, Error<ErrorCode>>> SolveLPWithStatusAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        Result<LpResult, ErrorCode, Error<ErrorCode>> result =
            await SolveLPAsync(name, metaModel, toLogModel, registrationStatusCallBack, solvingStatusCallBack);
        return result is Ok<LpResult, ErrorCode, Error<ErrorCode>> ok
            ? Results.Ok<LpResultWithStatus>(new LpResultWithStatus.Feasible(ok.Value))
            : result is Failed<LpResult, ErrorCode, Error<ErrorCode>> failed
                ? Results.Failed<LpResultWithStatus>(failed.Error)
                : throw new InvalidOperationException("unreachable");
    }

    /// <summary>
    /// 使用值转换器求解 LP 问题 / Solve LP problem with value converter.
    /// </summary>
    Task<Result<LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
        string name,
        LinearMetaModel<Flt64> metaModel,
        IIntoValue<V> converter,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null)
        where V : struct, IRealNumber<V>, INumberField<V>;

    /// <summary>
    /// LP 求解结果 / LP solve result.
    /// </summary>
    sealed record LpResult(
        FeasibleSolverOutput<Flt64> Result,
        IReadOnlyDictionary<MathConstraint, Flt64> DualSolution) {
        /// <summary>目标值 / Objective value</summary>
        public Flt64 Obj => Result.Obj;
        /// <summary>解向量 / Solution vector</summary>
        public IReadOnlyList<Flt64> Solution => Result.Solution.Values;
        /// <summary>求解时间 / Solve time</summary>
        public TimeSpan Time => Result.Time;
        /// <summary>可能的最优目标值 / Possible best objective</summary>
        public Flt64? PossibleBestObj => Result.PossibleBestObj;
        /// <summary>间隙 / Gap</summary>
        public Flt64 Gap => Result.Gap;
    }

    /// <summary>
    /// MILP 求解结果（区分可行与不可行）/ MILP solve result, distinguishing feasible from infeasible.
    /// </summary>
    /// <remarks>
    /// **分成两个分支而不是"结果 + 布尔标志"**：不可行时根本没有解向量，
    /// 用标志位就要么给一个空解（下游会当成"零解"读）要么让字段可空（下游每处都要判空）。
    ///
    /// **Two branches rather than "a result plus a boolean flag"**: an infeasible solve has no solution vector at all, so a flag would either
    /// supply an empty solution that downstream reads as "all zeros" or force every downstream site to null-check.
    /// </remarks>
    interface MilpSolveResult {
        /// <summary>可行解 / A feasible solution.</summary>
        /// <param name="Output">求解报告 / The solve report.</param>
        record Feasible(FeasibleSolverOutput<Flt64> Output) : MilpSolveResult;

        /// <summary>不可行 / Infeasible.</summary>
        /// <param name="Output">不可行输出 / The infeasible output.</param>
        record Infeasible(LinearInfeasibleSolverOutput Output) : MilpSolveResult;
    }

    /// <summary>
    /// LP 求解结果（区分可行与不可行）/ LP solve result, distinguishing feasible from infeasible.
    /// </summary>
    /// <remarks>
    /// 理由同 <see cref="MilpSolveResult"/>。
    ///
    /// For the same reason as <see cref="MilpSolveResult"/>.
    /// </remarks>
    interface LpResultWithStatus {
        /// <summary>可行解 / A feasible solution.</summary>
        /// <param name="Result">LP 结果 / The LP result.</param>
        record Feasible(LpResult Result) : LpResultWithStatus;

        /// <summary>不可行 / Infeasible.</summary>
        /// <param name="Output">不可行输出 / The infeasible output.</param>
        record Infeasible(LinearInfeasibleSolverOutput Output) : LpResultWithStatus;
    }

    /// <summary>
    /// 带值转换的 LP 求解结果 / LP solve result with value conversion.
    /// </summary>
    sealed record LpResultOf<V>(
        FeasibleSolverOutput<V> Result,
        IReadOnlyDictionary<MathConstraint, Flt64> DualSolution)
        where V : struct, IRealNumber<V>, INumberField<V> {
        /// <summary>目标值 / Objective value</summary>
        public Flt64 Obj => Result.Obj;
        /// <summary>解向量 / Solution vector</summary>
        public IReadOnlyList<V> Solution => Result.Solution.Values;
        /// <summary>求解时间 / Solve time</summary>
        public TimeSpan Time => Result.Time;
        /// <summary>可能的最优目标值 / Possible best objective</summary>
        public Flt64? PossibleBestObj => Result.PossibleBestObj;
        /// <summary>间隙 / Gap</summary>
        public Flt64 Gap => Result.Gap;
    }
}
