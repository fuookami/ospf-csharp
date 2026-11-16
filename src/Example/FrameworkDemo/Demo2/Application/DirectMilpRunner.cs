// 直连 MILP 求解的共享实现 / Shared implementation of the direct MILP solve.

using System;
using System.Collections.Generic;
using System.Globalization;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

/// <summary>
/// 直连 MILP：装配五个域并求解 / Direct MILP: assemble the five domains and solve.
/// </summary>
/// <remarks>
/// **一处实现，三个调用点**：全配载、预配载、建议打板三条 `Execute` 的收尾完全相同（装配 → 建三元模型 → 求解 → 回填响应），
/// 差别只有 `StowageMode`。各写一份的后果是**三份会慢慢分叉**—— 接线全配载时另外两条还在报"未实现"，
/// 就是分叉的第一天。
///
/// 装配顺序：配载 → 重心 → 适航 → 软安全 → 重心优化。后面的域引用前面的符号，顺序错了**不会在这里报错**，
/// 只会以"引用了不存在的 token"在下游炸开。求解失败不吞掉——把**错误码**写进备注，**消息文本随后端措辞变，码不会**。
///
/// **One implementation, three call sites**: the tails of the full-load, predistribution and weight-recommendation `Execute` paths are identical (assemble,
/// build the triad, solve, fill the response); the only difference is `StowageMode`. Writing it three times means **the three slowly diverge** — which is
/// exactly what happened when wired full load while the other two still reported "not implemented".
///
/// Assembly order mirrors the implementation: stowage → MAC → airworthiness → soft security → MAC optimisation. Later domains reference earlier symbols, and a wrong
/// order raises nothing here but explodes downstream as "a referenced token does not exist". A solve failure is not swallowed — the **error code** goes into the
/// notes, **the message text varies with backend wording but this implementation does not**.
/// </remarks>
internal static class DirectMilpRunner
{
    /// <summary>
    /// 装配并求解 / Assemble and solve.
    /// </summary>
    /// <param name="register">把各域注册进模型的回调 / Callback registering each domain into the model.</param>
    /// <param name="solver">线性求解器 / The linear solver.</param>
    /// <param name="notes">备注 / Notes.</param>
    /// <param name="render">可选的渲染回调，收到解值与模型 / Optional render callback receiving the values and the model.</param>
    /// <returns>响应 / Response.</returns>
    /// <remarks>
    /// **装配由调用方给**：三条路径的域不同（建议打板用载荷最大化而不是软安全 + 重心优化），所以这里收一个回调，
    /// 而不是写死五个上下文的参数表。**每个域的顺序由调用方保证**，本方法只负责"注册 → 建三元模型 → 求解 → 回填"。
    ///
    /// **Assembly comes from the caller**: the three paths carry different domains (weight recommendation uses payload maximisation instead of soft security plus MAC
    /// optimisation), so this takes a callback rather than a fixed list of five contexts. **The caller owns each domain's order**; this method owns "register, build the
    /// triad, solve, fill the response".
    /// </remarks>
    public static (ResponseDTO Response, RenderDTO? Render) Run(
        Func<LinearMetaModel<Flt64>, Try> register,
        ILinearSolver solver,
        List<string> notes,
        Func<IReadOnlyList<Flt64>, LinearMetaModel<Flt64>, RenderDTO?>? render = null) {
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(solver);

        using var model = new LinearMetaModel<Flt64>();
        Try registered = register(model);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
            notes.Add("Direct solve failed: " + failed.Error.Code + " " + failed.Error.Message);
            return (SolverFailureResponse(notes, failed.Error.Code, failed.Error.Message), null);
        }

        notes.Add($"direct_model=rows{model.Constraints.Count},symbols{model.Tokens.Symbols.Count},objective{model.MetaSubObjects.Count}");

        Result<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            model.BuildLinearTriadModelAsync<Flt64>().GetAwaiter().GetResult();
        if (built is Failed<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> buildFailed) {
            notes.Add("Direct solve failed: " + buildFailed.Error.Code + " " + buildFailed.Error.Message);
            return (SolverFailureResponse(notes, buildFailed.Error.Code, buildFailed.Error.Message), null);
        }
        LinearTriadModel matrix =
            ((Ok<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>>)built).Value.Matrix;

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            solver.SolveAsync(matrix).GetAwaiter().GetResult();
        if (solved is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solveFailed) {
            notes.Add("Direct solve failed: " + solveFailed.Error.Code + " " + solveFailed.Error.Message);
            return (SolverFailureResponse(notes, solveFailed.Error.Code, solveFailed.Error.Message), null);
        }
        FeasibleSolverOutput<Flt64> output =
            ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Value;

        // **赋值只报"非零"的那些**：把整张解表塞进响应会让调用方分不清哪条是装载决策。
        // **Only the non-zero entries are reported**: dumping the whole solution table obscures which entries are actual loading decisions.
        List<string> assignments = new();
        foreach (Flt64 value in output.Solution.Values) {
            if (value.ToDouble() != 0.0) {
                assignments.Add(value.ToDouble().ToString(CultureInfo.InvariantCulture));
            }
        }
        notes.Add("direct_solution=nonzero" + assignments.Count);
        // **渲染只在调用方要的时候算**：`render` 为 null 时连方案分析都不做——完整方案要遍历全部变量，
        // 而多数调用方只关心目标值与赋值。
        //
        // **Rendering only happens when the caller asks for it**: with `render` null not even the solution analysis runs — a full solution walks every variable,
        // and most callers only care about the objective and the assignments.
        RenderDTO? rendered = render?.Invoke(output.Solution.Values, model);
        return (ResponseDTO.Optimal(output.Obj.ToDouble(), assignments, notes), rendered);
    }

    /// <summary>
    /// 把求解失败映射成公开响应 / Map a solver failure onto the public response.
    /// </summary>
    /// <param name="notes">诊断信息 / The diagnostics.</param>
 /// <param name="code">求解器返回的错误码 / The implementation the solver returned.</param>
    /// <param name="message">求解器返回的错误文字 / The message the solver returned.</param>
    /// <returns>公开响应 / The public response.</returns>
    /// <remarks>
    /// **`solverFailureResponse` 里的那条分支，此前本仓没有。**
    /// 原来的代码不看错误码，**一律报 `DirectSolveFailed`**——于是调用方看到的是"求解出错了"，
    /// 而**真相是"这个模型不可行"**：**前者让人去查后端，后者让人去查模型。**
    ///
    /// **照措辞**：状态为 `NoSolution`，并追加一句说明**已经映射过**，
    /// 否则同一个响应里的 `DirectSolveFailed` 诊断会让人以为失败没有被解释。
    ///
    /// **The branch `solverFailureResponse` needs was missing here.**
 /// The old code never looked at the implementation and **reported `DirectSolveFailed` for everything** — so the caller saw "the solve went wrong",
    /// **when the truth was "this model is infeasible"**: **the first sends somebody to the backend, the second to the model.**
    ///
 /// **The wording follows the implementation**: the status becomes `NoSolution` and one sentence says the mapping happened,
    /// otherwise the `DirectSolveFailed` diagnostic in the same response suggests the failure went unexplained.
    /// </remarks>
    internal static ResponseDTO SolverFailureResponse(IReadOnlyList<string> notes, ErrorCode code, string message) =>
        code is ErrorCode.ORModelInfeasible or ErrorCode.ORModelInfeasibleOrUnbounded
            ? ResponseDTO.NoSolution(
                "NoSolution",
                [.. notes, $"求解器未返回可行解，已映射为 NoSolution：{code} / Solver returned no feasible solution and was mapped to NoSolution: {code}"])
            : new ResponseDTO(Succeed: false, Status: "Error", Notes: [message],
                Diagnostics: DiagnosticsHelper.BuildStructured([message]));
}
