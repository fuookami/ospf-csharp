#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 证明判定与分析侧投影 / Proof gating and the analysis-side projections.
/// </summary>
/// <remarks>
/// 补了（270 行）里不需要求解器的部分**：
/// 两个证明判定、assumption 映射、固定值投影、后端失败传播。
/// **没搬的是 `ConstraintProgrammingPerturbationBackend` 本身**——它要 `solver.CreateSession` 与 `session.Solve`。
///
/// **The solver-free parts of is implemented**; **what did not come over is the backend itself.**
/// </remarks>
public class ConstraintProgrammingAnalysisBackendsTests {
    private static ConstraintProgrammingFeasibleOutput Feasible(ProofStatus proofStatus) =>
        new(new ConstraintProgrammingSolution(new Dictionary<VariableId, Int64>()),
            objective: null, bestBound: null, status: SolverStatus.Feasible, proofStatus: proofStatus);

    private static ConstraintProgrammingInfeasibleOutput Infeasible(ProofStatus proofStatus) =>
        new(conflict: null, proofStatus: proofStatus);

    /// <summary>**最优判定刻意不要求 `Verified`** / The optimality gate deliberately does not require `Verified`.</summary>
    /// <remarks>
    /// 真实后端对最优解上报的是 `Claimed`。要求 `Verified` 会让**所有真实后端的有效性阶段静默退化为"无结论"**——
    /// **这是"把可用能力误判为不可用"，与"Timeout 当成 UNSAT"是同一类错误，只是方向相反。**
    ///
    /// Real backends report `Claimed` for optimal solutions. Requiring `Verified` would **silently degrade the effectiveness stage to "no conclusion" on every real backend** —
    /// **"treating an available capability as unavailable", the same class of error as "Timeout → UNSAT", only in the opposite direction.**
    /// </remarks>
    [Fact]
    public void TheOptimalityGate_ShouldAcceptClaimed() {
        Feasible(ProofStatus.Claimed)
            .IsProvenOptimal().Should().BeTrue(
                "a real backend's optimal solution carries Claimed, and rejecting it would disable the stage");

        Feasible(ProofStatus.Verified)
            .IsProvenOptimal().Should().BeTrue();
        Feasible(ProofStatus.None)
            .IsProvenOptimal().Should().BeFalse("no proof claim is no proof");
    }

    /// <summary>**不可行判定要求 `Verified`** / The infeasibility gate requires `Verified`.</summary>
    /// <remarks>
    /// **两条判定的门槛刻意不同**：最优解有 incumbent 可以复核，不可行结论没有任何东西可以复核，**只能靠证书。**
    ///
    /// **The two thresholds differ on purpose**: an optimal solution has an incumbent that can be re-checked, while an infeasibility conclusion has nothing to re-check — **only the
    /// certificate.**
    /// </remarks>
    [Fact]
    public void TheInfeasibilityGate_ShouldRequireVerified() {
        Infeasible(ProofStatus.Verified)
            .IsProvenInfeasible().Should().BeTrue();
        Infeasible(ProofStatus.Claimed)
            .IsProvenInfeasible().Should().BeFalse(
                "a claimed infeasibility cannot support 'the target is unreachable'");
        Infeasible(ProofStatus.None)
            .IsProvenInfeasible().Should().BeFalse();

        // **两道门槛的方向必须相反**，否则上面两条只是同一件事写了两遍。
        // **The two gates have to lean opposite ways**, or the two criteria above are the same check written twice.
        Feasible(ProofStatus.Claimed).IsProvenOptimal()
            .Should().BeTrue();
        Infeasible(ProofStatus.Claimed).IsProvenInfeasible()
            .Should().BeFalse("Claimed is enough for optimality and not enough for infeasibility");
    }

    /// <summary>**assumption 只为能表达的约束生成** / Assumptions are only produced for expressible constraints.</summary>
    /// <remarks>
    /// 数值约束必须通过移除约束的派生模型表达。**为它伪造一个 assumption，等于让后端去解一个没被改过的模型。**
    ///
    /// Numeric constraints must be expressed by deriving a model without them. **Fabricating an assumption for one would have the backend solve an unmodified model.**
    /// </remarks>
    [Fact]
    public void TheAssumptions_ShouldOnlyCoverExpressibleConstraints() {
        var set = DiagnosticActivationSet.Empty();
        var withLiteral = new ConstraintId("c1");
        var withoutLiteral = new ConstraintId("c2");
        set.Activate(new DiagnosticSource.Constraint(withLiteral));
        set.Activate(new DiagnosticSource.Constraint(withoutLiteral));

        var literal = new BooleanLiteral.VariableLiteral(new BinVar("b"), negated: true);
        var literals = new Dictionary<ConstraintId, BooleanLiteral> { [withLiteral] = literal };

        IReadOnlyList<BooleanLiteral> assumptions =
            ConstraintProgrammingAnalysisBackends.Assumptions(set, literals);
        assumptions.Should().ContainSingle("only the constraint with a literal can become an assumption");
        assumptions[0].Should().Be(literal);

        // **停用的来源不生成 assumption**：它已经不在派生模型里。
        // **A deactivated source produces no assumption**: it is no longer in the derived model.
        set.Deactivate(new DiagnosticSource.Constraint(withLiteral));
        ConstraintProgrammingAnalysisBackends.Assumptions(set, literals).Should().BeEmpty(
            "a deactivated source is not part of this solve");
    }

    /// <summary>**固定值投影不丢精度** / The fixed-value projection loses no precision.</summary>
    [Fact]
    public void TheFixedValues_ShouldProjectWithoutRounding() {
        var id = new VariableId("source:x");
        var solution = new ConstraintProgrammingSolution(
            new Dictionary<VariableId, Int64> { [id] = new Int64(9007199254740993L) });

        ConstraintProgrammingAnalysisBackends.FixedValues(solution)[id].ToLong()
            .Should().Be(9007199254740993L, "the Int64 reaches the backend unchanged");

        ConstraintProgrammingAnalysisBackends.FixedValues(null).Should().BeEmpty(
            "a null solution projects to nothing, not to an exception");
    }

    /// <summary>**任一侧为 `null` 都判为不一致** / A `null` on either side is "not agreeing".</summary>
    /// <remarks>
    /// 返回 `true` 会让一份**没有可比对象**的报告声称"派生目标"。
    ///
    /// Returning `true` would let a report **with nothing to compare** claim the derived objective matches the code.
    /// </remarks>
    [Fact]
    public void TheObjectivesAgree_ShouldNeedBothSides() {
        ConstraintProgrammingAnalysisBackends.ObjectivesAgree(null, new Flt64(1.0), 1e-9)
            .Should().BeFalse("there is no baseline to agree with");
        ConstraintProgrammingAnalysisBackends.ObjectivesAgree(new Flt64(1.0), null, 1e-9)
            .Should().BeFalse("there is no derived objective to agree with");
        ConstraintProgrammingAnalysisBackends.ObjectivesAgree(null, null, 1e-9).Should().BeFalse();

        ConstraintProgrammingAnalysisBackends.ObjectivesAgree(new Flt64(1.0), new Flt64(1.0), 1e-9)
            .Should().BeTrue();
        ConstraintProgrammingAnalysisBackends.ObjectivesAgree(new Flt64(1.0), new Flt64(2.0), 1e-9)
            .Should().BeFalse();

        // **容差按量级缩放**：1e6 量级上绝对 1e-9 严苛到永不成立。
        // **The tolerance scales with magnitude**: at 1e6 an absolute 1e-9 would never hold.
        ConstraintProgrammingAnalysisBackends.ObjectivesAgree(new Flt64(1e6), new Flt64(1e6 + 1e-4), 1e-9)
            .Should().BeTrue("1e-9 relative at magnitude 1e6 is a 1e-3 absolute window");

        // **`max(1.0, |left|)` 的下界是 1，不是 0。**
        // **The `max(1.0, |left|)` floor is 1, not 0.**
        //
        // 少了这个下界，量级接近 0 的两个目标会让容差窗口退化成 0，
        // **于是"两个都是 0"被判成不相等**——而它们完全相同。
        //
        // Without the floor, two objectives near zero collapse the window to zero, **so "both are zero" reads as disagreeing** — while they are identical.
        ConstraintProgrammingAnalysisBackends.ObjectivesAgree(new Flt64(0.0), new Flt64(0.0), 1e-9)
            .Should().BeTrue("identical zero objectives agree, and the scale floor is what makes the window non-zero");

        ConstraintProgrammingAnalysisBackends.ObjectivesAgree(new Flt64(0.0), new Flt64(1e-12), 1e-9)
            .Should().BeTrue("with the floor at 1, the window is a full 1e-9 absolute at magnitude zero");

        // **负容差这条分支没法用返回值区分**：`difference` 永远非负，
        // 所以 `difference <= tolerance * scale` 在负容差下自然为假——**删掉那道校验，行为不变。**
        //
        // **写在这里，是因为我试过这条变异：它没红。** 与其留一条"看起来测了"的断言，
        // **不如把"这条测不了"写下来。**
        //
        // **The negative-tolerance branch cannot be distinguished by the return value**: `difference` is never negative, so the comparison is false anyway — **removing the check changes
        // nothing.** This note exists because I tried that mutation and **it did not go red.** Rather than keep an assertion that only looks like coverage, **the gap is written down.**

        ConstraintProgrammingAnalysisBackends.ObjectivesAgree(new Flt64(1.0), new Flt64(1.0), double.NaN)
            .Should().BeFalse("a NaN tolerance would make every comparison false, which is not 'agreeing'");
    }

    /// <summary>**后端"成功"也要被映射成失败** / A backend "success" is mapped to a failure too.</summary>
    /// <remarks>
    /// 调用方是在"这条路径本该失败"的位置调用它的，**原样返回成功会让一个不该继续的流程继续下去**。
    ///
    /// Callers invoke this where the path was supposed to fail, and **passing a success through would let a flow continue that must not**.
    /// </remarks>
    [Fact]
    public void TheBackendFailure_ShouldTurnSuccessIntoFailure() {
        Result<object, ErrorCode, Error<ErrorCode>> ok = Results.Ok<object>("unexpected");
        Result<string, ErrorCode, Error<ErrorCode>> mapped =
            ConstraintProgrammingAnalysisBackends.PropagateBackendFailure<string>(ok);
        mapped.IsOk.Should().BeFalse("a success here means the backend returned an invalid state");
        ((Failed<string, ErrorCode, Error<ErrorCode>>)mapped).Error.Code.Should().Be(ErrorCode.ApplicationError,
            "it is an invalid result state, not a backend failure");

        Result<object, ErrorCode, Error<ErrorCode>> failed =
            Results.Failed<object>(new Err<ErrorCode>(ErrorCode.DataNotFound, "missing"));
        Result<string, ErrorCode, Error<ErrorCode>> propagated =
            ConstraintProgrammingAnalysisBackends.PropagateBackendFailure<string>(failed);
        propagated.IsOk.Should().BeFalse();
        ((Failed<string, ErrorCode, Error<ErrorCode>>)propagated).Error.Code.Should().Be(ErrorCode.DataNotFound,
            "the original failure code survives the mapping");
    }
}
