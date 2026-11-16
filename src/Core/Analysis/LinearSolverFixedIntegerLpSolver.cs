#nullable enable

using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 用现有线性求解器求解固定整数 LP，并按原始约束 ID 取回对偶值 /
/// Solve a fixed-integer LP with an existing linear solver and return duals keyed by original constraint ID.
/// </summary>
/// <remarks>
/// **这是 `FixedIntegerLpSensitivity.kt` 里 `LinearSolverFixedIntegerLpBackend` 的核心那一步，
/// 而此前 C# 侧&lt;b&gt;一个生产实现都没有**：&lt;/b&gt;
///
/// - `IFixedIntegerLpBackend`、`LoweredFixedIntegerLpBackend`、`ILoweredFixedIntegerLpSolver` **都在**
/// - `IAbstractLinearSolver`、`ConstraintProgrammingToLinearModelLowerer` **都在**
/// - **缺的正是把这几样接起来的那个实现**——而两个插件后端（Gurobi／SCIP）都依赖它
///
/// **所以"框架齐了、后端空着"要说得更准一层：**
/// **空的不止是 6 个插件函数，中间还缺这一层。补上它，两个插件就只剩"接哪根线"的问题。**
///
/// ### 语义边界（照基线，不自行放宽）
///
/// - **对偶只按&lt;b&gt;原始约束稳定 ID** 暴露；对不上唯一原始约束的行一律不出现**
/// - **对偶是 incumbent 作用域下的局部边际价值，不是 MILP 全局影子价格**
/// - **只有&lt;b&gt;已证最优**才升格为 `Reachable`；incumbent 与无解都保持 `Unknown`**
///
/// **This is the core step of the baseline's `LinearSolverFixedIntegerLpBackend`, and it had &lt;b&gt;no production implementation
/// at all&lt;/b&gt; here: the interface, the lowering adapter and the delegate all exist, the linear solver and the lowerer all
/// exist, and what was missing is exactly the piece that ties them together — the piece both plugin backends depend on.**
/// **So the earlier "framework complete, backends empty" can be stated one layer more precisely: besides the six plugin functions,
/// this middle layer was missing too.**
///
/// **Semantics copied from the baseline rather than loosened: duals are exposed only by original stable constraint ID; rows that
/// do not map to an original are omitted; the dual is a local marginal value under the incumbent, not a global MILP shadow
/// price; and only a &lt;b&gt;proven optimum&lt;/b&gt; rises to `Reachable`, while an incumbent or no solution stays `Unknown`.**
/// </remarks>
public sealed class LinearSolverFixedIntegerLpSolver : ILoweredFixedIntegerLpSolver
{
    private readonly IAbstractLinearSolver _linearSolver;
    private readonly Func<IReadOnlyList<ConstraintId>, IReadOnlyDictionary<ConstraintId, SensitivityRange?>?>? _sensitivityRanges;

    /// <summary>构造 / Construct.</summary>
    /// <param name="linearSolver">现有线性后端 / An existing linear backend.</param>
    /// <param name="sensitivityRanges">
    /// **可选的范围钩子：给它行序，它按行序回区间。**
    /// **行序只有这一层知道，所以配对放在这里；而"区间从哪来"由调用方决定**——
    /// **于是本层仍然不认识任何后端专有的 ranging 属性。**
    ///
    /// **An optional range hook: give it the row order, it returns ranges in that order.** **Only this layer knows the row order, so
    /// the pairing lives here; where the ranges come from is the caller's business, which keeps this layer ignorant of any backend's
    /// ranging attributes.**
    /// </param>
    public LinearSolverFixedIntegerLpSolver(
        IAbstractLinearSolver linearSolver,
        Func<IReadOnlyList<ConstraintId>, IReadOnlyDictionary<ConstraintId, SensitivityRange?>?>? sensitivityRanges = null) {
        ArgumentNullException.ThrowIfNull(linearSolver);
        _linearSolver = linearSolver;
        _sensitivityRanges = sensitivityRanges;
    }

    /// <summary>求解并按原始约束 ID 取回对偶 / Solve and return duals by original constraint ID.</summary>
    /// <param name="lowered">降阶模型 / The lowered model.</param>
    /// <param name="request">原始请求 / The original request.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>结果或错误 / The result, or an error.</returns>
    public async Task<Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ConstraintProgrammingLoweredLinearModel lowered,
        FixedIntegerLpRequest request,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(lowered);

        Result<(LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
            await lowered.Model
                .BuildLinearTriadModelAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        if (built is not Ok<
                (LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix),
                ErrorCode,
                Error<ErrorCode>> mechanism) {
            return Fail(
                ((Failed<
                    (LinearMechanismModel<Flt64> Model, LinearTriadModel Matrix),
                    ErrorCode,
                    Error<ErrorCode>>)built).Error);
        }

        LinearMechanismModel<Flt64> mechanismModel = mechanism.Value.Model;
        try {
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> reported =
                await _linearSolver
                    .InvokeReportAsync(mechanism.Value.Matrix, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            if (reported is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
                return Fail(((Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>)reported).Error);
            }

            SolveReport<Flt64> report = ok.Value;

            // **只有"已证最优"才给 `Reachable`。**
            //
            // **基线原话是"只有真正形成最优解时才升格为 `Reachable`，受限求解的 incumbent 保持 `Unknown`"。**
            // **所以这里<b>不能**用 `ToAnalysisStatus` 单独决定——它不看解的存在性与最优性证明，
            // 而 `ProblemStatus.Feasible` 在有 incumbent 时也会是 Feasible。**
            //
            // **判据：`SolutionPresence.Optimal` 才查 `ToAnalysisStatus`，其余一律 `Unknown`。**
            //
            // **Only a proven optimum becomes `Reachable`: the baseline says the status rises only when an optimum is really
            // formed, while a restricted-solve incumbent stays `Unknown`.** **So `ToAnalysisStatus` cannot decide alone — it
            // ignores solution presence and optimality, and `ProblemStatus.Feasible` is also returned when only an incumbent
            // exists.** **The rule here: only `SolutionPresence.Optimal` consults `ToAnalysisStatus`; everything else is
            // `Unknown`.**
            AnalysisStatus status = MapStatus(report.SolutionPresence, report.ProblemStatus);

            IReadOnlyList<ConstraintId> rowOrder = report.Diagnostics.ConstraintEvaluations
                .Select(static evaluation => evaluation.ConstraintId)
                .ToArray();

            return Results.Ok(new FixedIntegerLpSolveResult(
                Status: status,
                ObjectiveValue: report.Solution?.Objective,
                Duals: ExtractDuals(report, lowered.ConstraintProvenance),
                SensitivityRanges: _sensitivityRanges?.Invoke(rowOrder),
                // **报告本身不带耗时；`Elapsed` 在 `SolveStatistics` 里，而不是 `SolveReport` 上。**
                // **The report carries no elapsed time of its own; it lives on the statistics, not on `SolveReport`.**
                SolveTime: null,
                Message: null));
        } finally {
            mechanismModel.Dispose();
        }
    }

    /// <summary>把报告里的约束求值折算成按原始约束 ID 的对偶 / Fold the report's evaluations into duals by original ID.</summary>
    /// <remarks>
    /// **`SolveReport.ConstraintEvaluations` 用的是&lt;b&gt;降阶后**的 `ConstraintId`；
    /// 只有写进 `lowered.ConstraintProvenance` 的那些才是原始约束。**
    ///
    /// **所以这里做的是"取交集"，不是"翻译"：对不上的一律不出现，而不是猜一个。**
    /// **这是基线的红线之一——"降阶辅助行及无法唯一归属原始来源的行不会进入返回的对偶映射"。**
    ///
    /// **The evaluations use the &lt;b&gt;lowered&lt;/b&gt; constraint IDs; only those recorded in `lowered.ConstraintProvenance` are
    /// original constraints.** **So this is an intersection, not a translation: anything that does not match is omitted rather
    /// than guessed — one of the baseline's red lines.**
    /// </remarks>
    /// <summary>**解的存在性与最优性证明共同决定状态** / Presence and proof together decide the status.</summary>
    /// <remarks>
    /// **抽成独立方法是为了可测：这条规则是基线的红线，而它只有一个判断，
    /// 埋在 `SolveAsync` 里面就只能靠一个完整的假求解器去测它。**
    ///
    /// **Extracted so it can be tested directly: this rule is the baseline's red line and only one judgement, and buried inside
    /// `SolveAsync` it could only be reached through a full fake solver.**
    /// </remarks>
    /// <remarks>
    /// **规则不是"只有 Optimal 才算数"——那样会把&lt;b&gt;已证不可行**也压成 `Unknown`，
    /// 等于丢掉一个可证明的结论。**
    ///
    /// **真正的规则是：&lt;b&gt;incumbent 一律不算数**（它没有最优性证明），其余照 `ProblemStatus` 直译。**
    /// **于是三种情形各自正确：** **Optimal+Feasible → Reachable；None+Infeasible → Unreachable；
    /// Incumbent+Feasible → Unknown。**
    ///
    /// **写第一版时我只写了'只有 Optimal 才查映射'，而测试立刻用 None+Infeasible 把它推翻了——
    /// 那条用例是我为了覆盖"无解"随手加的，结果它抓到的是一个真的语义漏洞。**
    ///
    /// **The rule is not "only Optimal counts" — that would flatten a &lt;b&gt;proven infeasibility&lt;/b&gt; into `Unknown`, discarding a
    /// provable conclusion.** **The real rule is: an &lt;b&gt;incumbent never counts&lt;/b&gt; (it carries no optimality proof), and
    /// everything else follows `ProblemStatus` directly — giving all three cases correctly: Optimal+Feasible → Reachable,
    /// None+Infeasible → Unreachable, Incumbent+Feasible → Unknown.**
    ///
    /// **The first version only said "consult the mapping when Optimal", and the test refuted it at once with None+Infeasible —
    /// a case I had added casually to cover "no solution", which turned out to catch a real semantic hole.**
    /// </remarks>
    internal static AnalysisStatus MapStatus(SolutionPresence presence, ProblemStatus problemStatus) =>
        presence == SolutionPresence.Incumbent
            ? AnalysisStatus.Unknown
            : problemStatus.ToAnalysisStatus();

    internal static IReadOnlyDictionary<ConstraintId, Flt64?> ExtractDuals(
        SolveReport<Flt64> report,
        IReadOnlyDictionary<MathConstraint, ConstraintId> provenance) =>
        ExtractDuals(report, (IEnumerable<ConstraintId>)provenance.Values);

    /// <summary>按"原始约束 ID 集合"折算对偶 / Fold duals given the set of original constraint IDs.</summary>
    /// <remarks>
    /// **分成两个重载，只为了让这条规则可测：** **映射逻辑只用到原始 ID 的&lt;b&gt;集合**，
    /// 与来源键的类型无关。** **而 `MathConstraint` 是一个成员很多的接口——
    /// 为了一条判据去实现它，只会把测试写成一个假模型。**
    ///
    /// **Split into two overloads purely so the rule can be tested: the mapping only needs the &lt;b&gt;set&lt;/b&gt; of original IDs and is
    /// independent of the provenance key's type.** **`MathConstraint` is a wide interface, and implementing it just for a
    /// criterion would turn the test into a fake model.**
    /// </remarks>
    internal static IReadOnlyDictionary<ConstraintId, Flt64?> ExtractDuals(
        SolveReport<Flt64> report,
        IEnumerable<ConstraintId> originalIds) {
        var originals = new HashSet<ConstraintId>(originalIds);
        var seen = new Dictionary<ConstraintId, int>();
        var values = new Dictionary<ConstraintId, Flt64?>();
        foreach (ConstraintEvaluation<Flt64> evaluation in report.Diagnostics.ConstraintEvaluations) {
            if (!originals.Contains(evaluation.ConstraintId)) {
                continue;
            }
            seen[evaluation.ConstraintId] = seen.TryGetValue(evaluation.ConstraintId, out int count)
                ? count + 1
                : 1;
            values[evaluation.ConstraintId] = evaluation.Dual;
        }

        // **只保留"恰好对应唯一一行"的那些。**
        //
        // **基线的原话是"仅当某个原始约束恰好对应**唯一**降阶行时才给出值**——
        // 而第一版实现是"后写覆盖先写"，于是<b>对应两行的约束会拿到第二行的对偶**，
        // 那个数字看起来完全合法，而它归属错误。**
        //
        // **这是我自己的判据抓到的：** **夹具里第三个求值与第一个同 ID，
        // 于是结果只剩一条——而我要的是"要么一条、要么明确丢掉"。**
        //
        // **Keep only those mapping to exactly one row: the baseline says a value is given only when an original constraint
        // corresponds to a <b>unique</b> lowered row.** **The first implementation let later writes overwrite earlier ones, so a
        // constraint spanning two rows silently received the second row's dual — a perfectly plausible number with the wrong
        // owner.** **My own criterion caught it: the fixture's third evaluation shares the first one's ID, and the result held
        // one entry where the rule requires either one or an explicit drop.**
        var duals = new Dictionary<ConstraintId, Flt64?>();
        foreach (KeyValuePair<ConstraintId, int> pair in seen) {
            if (pair.Value == 1) {
                duals[pair.Key] = values[pair.Key];
            }
        }
        return duals;
    }

    private static Result<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>> Fail(Error<ErrorCode> error) =>
        new Failed<FixedIntegerLpSolveResult, ErrorCode, Error<ErrorCode>>(error);
}
