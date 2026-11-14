#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 未知观察的构造 / Building an unknown observation.
/// </summary>
/// <remarks>
/// **`Status` 与 `Outcome` 必须同时是 `Unknown`。** 只改一个会造出一种自相矛盾的观察：
/// 状态说"不知道"，结果说"没效果"——**而"没效果"是一个结论。**
///
/// **`Status` and `Outcome` must both be `Unknown`.** Changing only one produces a self-contradictory observation: the status says "unknown" while the outcome says "no effect" — **and "no effect" is a
/// conclusion.**
/// </remarks>
public class PerturbationObservationFactoryTests {
    private static readonly ConstraintId C = new("constraint:c1");

    /// <summary>**两个"未知"必须同时出现** / Both unknowns appear together.</summary>
    /// <remarks>
    /// **反向对照**：只断言 `Status` 会让"状态未知、结果没效果"通过，
    /// **而那条观察在对账时会被读成"扰动确实没影响"——一个没测过的结论。**
    ///
    /// **The reverse control**: asserting only `Status` lets "unknown status, no effect" pass, **and that observation reads as "the perturbation really had no effect" in a reconciliation — a
    /// conclusion nobody measured.**
    /// </remarks>
    [Fact]
    public void TheUnknownObservation_ShouldCarryBothUnknowns() {
        ConstraintPerturbationObservation observation =
            PerturbationObservationFactory.Unknown(C, ConstraintPerturbationKind.Removal, "backend refused");

        observation.Status.Should().Be(AnalysisStatus.Unknown);
        observation.Outcome.Should().Be(PerturbationOutcome.Unknown,
            "an unknown status with a definite outcome is a conclusion nobody measured");
        observation.Message.Should().Be("backend refused");
    }

    /// <summary>**没测过的字段一律留空，不填 0** / Unmeasured fields stay empty, never zero.</summary>
    /// <remarks>
    /// 填 0 会让下游的"改善为零"与"没测过"无法区分——**而这两件事在对账里是完全相反的。**
    ///
    /// Filling zero makes "zero improvement" indistinguishable from "not measured" — **and those two are opposites in a reconciliation.**
    /// </remarks>
    [Fact]
    public void TheUnmeasuredFields_ShouldStayNull() {
        ConstraintPerturbationObservation observation =
            PerturbationObservationFactory.Unknown(C, ConstraintPerturbationKind.Rhs, "no dual");

        observation.PerturbedRhs.Should().BeNull("no right-hand side was measured");
        observation.BaselineObjective.Should().BeNull();
        observation.NewObjective.Should().BeNull();
        observation.ObjectiveImprovement.Should().BeNull("zero improvement is a measurement, not an absence");
        observation.IntegerPatternChanged.Should().BeNull();
        observation.SolveTime.Should().BeNull("a solve that did not happen has no duration");
    }

    /// <summary>**步长与种类原样带出** / The delta and the kind are carried through.</summary>
    /// <remarks>
    /// 这条防的是"工厂把参数吞了"——**一条丢了步长的观察没法放回扫描序列里。**
    ///
    /// This guards against a factory that swallows its arguments — **an observation without its delta cannot go back into the sweep.**
    /// </remarks>
    [Fact]
    public void TheArguments_ShouldBeCarriedThrough() {
        var delta = new Fuookami.Ospf.Math.Algebra.Number.Flt64(0.25);

        ConstraintPerturbationObservation withDelta =
            PerturbationObservationFactory.Unknown(C, ConstraintPerturbationKind.Rhs, "budget", delta);
        withDelta.Delta.Should().NotBeNull();
        withDelta.Delta!.Value.ToDouble().Should().Be(0.25);
        withDelta.Kind.Should().Be(ConstraintPerturbationKind.Rhs);

        ConstraintPerturbationObservation withoutDelta =
            PerturbationObservationFactory.Unknown(C, ConstraintPerturbationKind.Removal, "budget");
        withoutDelta.Delta.Should().BeNull("removal has no delta, and null is not zero");
    }
}
