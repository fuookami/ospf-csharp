#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 求解器输出到目标状态的两处判定 / The two verdicts from solver output to a target status.
/// </summary>
/// <remarks>
/// **`Infeasible` ≠ `Unreachable`。** 求解器说"不可行"可能只是"我没找到解"，
/// **而 `Unreachable` 正是冲突分析去声称 MUS 的前提。**
///
/// **`Infeasible` is not `Unreachable`.** "The solver says infeasible" may only mean "I did not find a solution", **and `Unreachable` is exactly what conflict analysis requires before claiming a MUS.**
/// </remarks>
public class TargetOutputStatusTests {
    private static ObjectiveTarget Target() =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(1.0));

    /// <summary>**没有证明的不可行只能是 `Unknown`** / Unproven infeasibility is only Unknown.</summary>
    /// <remarks>
    /// **这是本模块最贵的错误。** 把"没找到解"报成"达不到"，
    /// **然后冲突分析在上面建一个 MUS。**
    ///
    /// **This is the most expensive mistake in the module.** Reporting "did not find a solution" as "cannot be done", **and then conflict analysis builds a MUS on top of it.**
    /// </remarks>
    [Fact]
    public void TheUnprovenInfeasibility_ShouldStayUnknown() {
        TargetOutputStatus.FromInfeasible(outputProofVerified: false, reportProofVerified: false)
            .Should().Be(AnalysisStatus.Unknown,
                "a solver that gave up is not a proof that nothing exists");
    }

    /// <summary>**两个证明来源都要看** / Both proof sources count.</summary>
    /// <remarks>
    /// **只查一个会漏掉"证明挂在 report 上"的那一类后端**，
    /// 代价是一个本来能拿到的 MUS 拿不到了。
    ///
    /// **Checking only one misses backends that attach the proof to the report**, at the cost of losing a MUS that was there to be had.
    /// </remarks>
    [Fact]
    public void TheProvenInfeasibility_ShouldBeUnreachableFromEitherSource() {
        TargetOutputStatus.FromInfeasible(outputProofVerified: true, reportProofVerified: false)
            .Should().Be(AnalysisStatus.Unreachable, "the output's own proof is enough");

        TargetOutputStatus.FromInfeasible(outputProofVerified: false, reportProofVerified: true)
            .Should().Be(AnalysisStatus.Unreachable,
                "a proof attached to the report is just as good, and ignoring it loses real results");
    }

    /// <summary>**只有证明才把 `TargetFixed` 置真** / Only a proof fixes the target.</summary>
    /// <remarks>
    /// **`Unknown` 时 `TargetFixed` 必须为假**：没有证明，目标值就没有被定下来。
    ///
    /// **With `Unknown`, `TargetFixed` has to be false**: without a proof the objective value is not pinned down.
    /// </remarks>
    [Fact]
    public void TheInfeasibleReport_ShouldFixTheTargetOnlyWhenProven() {
        TargetFeasibilityReport unproven = TargetOutputStatus.Infeasible(
            Target(), new Int64(1), outputProofVerified: false, reportProofVerified: false,
            ProofStatus.None);

        unproven.Status.Should().Be(AnalysisStatus.Unknown);
        unproven.TargetFixed.Should().BeFalse("nothing was proven, so nothing is fixed");

        TargetFeasibilityReport proven = TargetOutputStatus.Infeasible(
            Target(), new Int64(1), outputProofVerified: true, reportProofVerified: false,
            ProofStatus.Verified);

        proven.Status.Should().Be(AnalysisStatus.Unreachable);
        proven.TargetFixed.Should().BeTrue("a proven infeasibility really does settle the target");
    }

    /// <summary>**无效见证是 `Unknown` 加原因，不是失败** / An invalid witness is Unknown with the reason.</summary>
    /// <remarks>
    /// 求解器的说法被推翻了，**但我们的结论也还没出来。** 失败意味着"这次分析出错了"，
    /// 而这次分析完全正常：它只是验出后端给的东西不对。
    ///
    /// The solver's claim is refuted, **but ours is not established either.** A failure means "this analysis went wrong", while this analysis went exactly right: it caught the backend.
    /// </remarks>
    [Fact]
    public void TheInvalidWitness_ShouldBeUnknownAndCarryTheReason() {
        TargetFeasibilityReport report = TargetOutputStatus.InvalidWitness(
            Target(), new Int64(1), "见证解不满足目标边界", ProofStatus.None);

        report.Status.Should().Be(AnalysisStatus.Unknown);
        report.TargetFixed.Should().BeFalse("an invalid witness fixes nothing");
        report.Message.Should().Contain("见证解不满足目标边界",
            "the caller has to see which check failed, not just that one did");
        report.Message.Should().Contain("无效", "and that it was the witness that was wrong");
    }

    /// <summary>**`Incumbent` 必须带上"未证明最优"** / An incumbent must carry the caveat.</summary>
    /// <remarks>
    /// **`Reachable` 是可行性结论，不是最优性结论。** 少了这一句，
    /// **读者会把"可达"读成"这就是最优"。**
    ///
    /// **`Reachable` is a feasibility claim, not an optimality claim.** Without this sentence, **the reader takes "reachable" for "optimal".**
    /// </remarks>
    [Fact]
    public void TheIncumbent_ShouldCarryTheOptimalityCaveat() {
        string? caveat = TargetOutputStatus.FeasibleCaveat(SolutionPresence.Incumbent);

        caveat.Should().NotBeNull("an incumbent is not a proven optimum");
        caveat!.Should().Contain("未证明最优", "the caveat has to say which of the two it is");
    }

    /// <summary>**`Optimal` 不该带那个保留** / An optimal result gets no caveat.</summary>
    /// <remarks>
    /// **反向对照**：只测上一条会让"恒给保留"通过，
    /// **而"有保留"说多了就等于没有保留。**
    ///
    /// **The reverse control**: testing only the case above lets "always hedge" pass, **and a hedge that always fires is not a hedge.**
    /// </remarks>
    [Fact]
    public void TheOptimal_ShouldNotCarryIt() {
        TargetOutputStatus.FeasibleCaveat(SolutionPresence.Optimal)
            .Should().BeNull("a proven optimum needs no hedge, and hedging it dilutes the ones that matter");
    }

    /// <summary>**没有存在性信息时不凭空安结论** / No presence information means no invented conclusion.</summary>
    /// <remarks>
    /// 一份"可行但没有解存在性信息"的输出**不该被安上一句"未证明最优"**——
    /// **那是把缺失当结论。**
    ///
    /// A feasible output with no presence information **must not be handed a "not proven optimal"** — **that turns an absence into a conclusion.**
    /// </remarks>
    [Fact]
    public void TheMissingPresence_ShouldInventNothing() {
        TargetOutputStatus.FeasibleCaveat(SolutionPresence.None).Should().BeNull();
        TargetOutputStatus.FeasibleCaveat(presence: null).Should().BeNull();
    }
}
