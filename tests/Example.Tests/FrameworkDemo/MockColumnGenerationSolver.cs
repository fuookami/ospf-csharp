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
    /// <summary>
    /// 前几次 LP 求解返回「模型不可行」，之后正常求解 / The first N LP solves return "model infeasible", then solve normally.
    /// </summary>
    /// <remarks>
    /// **这是"两阶段可行性"判据的唯一入口。** `Csp1dColumnGeneration` 的第一阶段只在
    /// **LP 以「模型不可行」这个具体错误码失败**时才触发；此前的 mock **只会成功或返回通用的 `SolverNotFound`**，
    /// 所以 B351 明说"两阶段切换无判据"。
    ///
    /// 计数是**按 LP 调用递减**的：`1` 表示"第一次 LP 不可行，之后都成功"——
    /// 正好触发"不可行 → 建可行性主问题 → 再解一次"。
    ///
    /// **This is the only entry point for the two-phase feasibility criteria.** `Csp1dColumnGeneration`'s phase one fires only when **the LP fails with the specific
    /// "model infeasible" error code**; the previous mock **only succeeded or returned a generic `SolverNotFound`**, which is why B351 stated the switch had no
    /// criterion.
    ///
    /// The counter **decrements per LP call**: `1` means "the first LP is infeasible, every later one succeeds" — exactly what triggers
    /// "infeasible → build the feasibility master → solve again".
    /// </remarks>
    public int InfeasibleLpSolves { get; set; }

    /// <summary>
    /// 前几次 LP 求解以**通用失败**（`SolverNotFound`）结束，而不是「模型不可行」。
    /// The first N LP solves fail with a **generic** `SolverNotFound`, not "model infeasible".
    /// </summary>
    /// <remarks>
    /// **这条用来钉住"必须检查错误码"**：若实现只看"失败了"就回退到可行性主问题，
    /// 这个通用失败会被误判成"模型不可行"，**真正的错误被掩盖**。
    /// 与 `InfeasibleLpSolves` **分开计数**，两者不同时生效。
    ///
    /// **This pins "the error code must be checked"**: if the implementation falls back on "it failed", this generic failure is misread as "model infeasible" and
    /// **the real error is masked**. It counts **separately** from `InfeasibleLpSolves`; they never apply at the same time.
    /// </remarks>
    public int GenericLpFailures { get; set; }

    /// <summary>LP 求解被调用的总次数 / Total number of LP solve calls.</summary>
    /// <remarks>
    /// **用于证明某个分支真的执行过**：例如"可行性阶段建了主问题"没有可观测的状态，
    /// 但它**必然多出一次 LP 调用**。数次数比断言"结果如何"更难被别的路径掩盖。
    ///
    /// **Used to prove a branch really ran**: "the feasibility phase built a master" has no observable state, but it **necessarily adds one LP call**. Counting calls is
    /// harder for another path to mask than asserting "what the result was".
    /// </remarks>
    public int LpSolveCount { get; private set; }

    /// <summary>
    /// LP 返回的目标值；默认 0 / The objective value returned by LP solves; default 0.
    /// </summary>
    /// <remarks>
    /// **这是钉住 `1e-7` 阈值的唯一入口。** 第二阶段只在"可行性 LP 的目标值 ≤ 1e-7"时才恢复原始主问题；
    /// 目标值恒为 0 时**任何非负阈值都会通过**，所以 B353 明说"阈值具体取值没有判据"。
    ///
    /// 把它做成可配之后，就能构造"**目标值略大于阈值**"的中间态，
    /// 从而证明恢复是**被阈值触发的**，而不是"只要进入可行性阶段就恢复"。
    ///
    /// **This is the only entry point for pinning the `1e-7` threshold.** Phase two restores the original master only when "the feasibility LP's objective ≤ 1e-7"; with a
    /// constant objective of 0 **any non-negative threshold passes**, which is why B353 stated the threshold's value had no criterion.
    ///
    /// Making it configurable allows the intermediate state "**objective slightly above the threshold**", proving the restore is **triggered by the threshold** rather
    /// than by "entering the feasibility phase".
    /// </remarks>
    public double LpObjectiveValue { get; set; }

    /// <inheritdoc/>
    public string Name => "mock-test-solver";

    /// <inheritdoc/>
    public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
        string name,
        LinearMetaModel<Flt64> metaModel,
        bool toLogModel = false,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        SolvingStatusCallBack? solvingStatusCallBack = null) {
        FireCallbacks(registrationStatusCallBack, solvingStatusCallBack, name);
        return Task.FromResult(MakeFeasibleOutput());
    }

    /// <summary>
    /// 触发一次注册与求解回调（模拟真实求解器）/ Fire the registration and solving callbacks once (mimicking a real solver).
    /// </summary>
    /// <remarks>
    /// **真实求解器会在求解过程中触发回调**；这个替身原先**从不触发**，于是任何依赖回调的接线
    /// （如进度上报）**都无法被观测**。补上触发让替身更接近真实行为。
    ///
    /// 只在回调非空时触发，**既有调用方（传 null）行为完全不变**。
    ///
    /// **Real solvers fire callbacks during a solve**; this double **never did**, so any wiring that depends on callbacks (such as progress reporting) **could not be
    /// observed**. Firing them makes the double closer to real behaviour.
    ///
    /// It fires only when a callback is non-null, so **existing callers (passing null) are unchanged**.
    /// </remarks>
    /// <param name="registrationStatusCallBack">注册回调 / Registration callback.</param>
    /// <param name="solvingStatusCallBack">求解回调 / Solving callback.</param>
    /// <param name="name">求解名称 / Solve name.</param>
    public static void FireCallbacks(
        RegistrationStatusCallBack? registrationStatusCallBack,
        SolvingStatusCallBack? solvingStatusCallBack,
        string name) {
        registrationStatusCallBack?.Invoke(new RegistrationStatus(
            EmptySymbolAmount: default,
            ReadySymbolAmount: default,
            TotalSymbolAmount: default));
        solvingStatusCallBack?.Invoke(new SolvingStatus(
            Solver: name,
            SolverIndex: 0,
            SolverConfig: new Fuookami.Ospf.Core.Solver.Config.SolverConfig(),
            Gap: Flt64.Zero));
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
        ++LpSolveCount;
        if (InfeasibleLpSolves > 0) {
            --InfeasibleLpSolves;
            return Task.FromResult(Results.Failed<IColumnGenerationSolver.LpResult>(
                new Err<ErrorCode>(ErrorCode.ORModelInfeasible, "model infeasible")));
        }
        if (GenericLpFailures > 0) {
            --GenericLpFailures;
            return Task.FromResult(Results.Failed<IColumnGenerationSolver.LpResult>(
                new Err<ErrorCode>(ErrorCode.SolverNotFound, "generic lp failure")));
        }
        var milpResult = MakeFeasibleOutput(LpObjectiveValue);
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

    private static Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> MakeFeasibleOutput(
        double objective = 0.0) {
        var solution = new Solution<Flt64>(new List<Flt64>());
        var output = new FeasibleSolverOutput<Flt64>(
            Obj: new Flt64(objective),
            Solution: solution,
            Time: TimeSpan.FromMilliseconds(1),
            PossibleBestObj: Flt64.Zero,
            Gap: Flt64.Zero);
        return Results.Ok(output);
    }
}
