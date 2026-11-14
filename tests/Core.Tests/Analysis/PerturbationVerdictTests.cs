#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 扰动分析的两条判定 / Two verdicts of the perturbation analysis.
/// </summary>
/// <remarks>
/// **任一侧解缺失时 `IntegerPatternChanged` 返回 `null`，不是 `false`。**
///
/// **`IntegerPatternChanged` returns `null`, not `false`, when either solution is missing.**
/// </remarks>
public class PerturbationVerdictTests {
    private static ConstraintProgrammingSolution Solution(Dictionary<VariableId, Int64> values) =>
        new(values);

    private static Dictionary<VariableId, Int64> Values(params (string Id, long Value)[] entries) {
        var dictionary = new Dictionary<VariableId, Int64>();
        foreach ((string id, long value) in entries) {
            dictionary[new VariableId(id)] = new Int64(value);
        }
        return dictionary;
    }

    /// <summary>**任一侧缺失就是"没测"，不是"没变"** / A missing side is "not measured", not "unchanged".</summary>
    /// <remarks>
    /// 写成 `false` 会让报告声称"扰动之后整数解的模式没变"——**而那份基线解根本没拿到，结论是从空气里来的。**
    ///
    /// Returning `false` would let the report claim "the integer pattern did not change after the perturbation" — **while the baseline solution was never obtained.**
    /// </remarks>
    [Fact]
    public void TheMissingSolution_ShouldGiveNull() {
        ConstraintProgrammingSolution present = Solution(Values(("source:x", 3)));

        ConstraintPerturbationSupport.IntegerPatternChanged(null, present)
            .Should().BeNull("there is no baseline to compare against");
        ConstraintPerturbationSupport.IntegerPatternChanged(present, null)
            .Should().BeNull("there is no current solution to compare against");
        ConstraintPerturbationSupport.IntegerPatternChanged(null, null)
            .Should().BeNull();
    }

    /// <summary>**两侧都在时才是真判定** / A real verdict needs both sides.</summary>
    [Fact]
    public void TheChangedPattern_ShouldBeTrue() {
        ConstraintPerturbationSupport.IntegerPatternChanged(
                Solution(Values(("source:x", 3))),
                Solution(Values(("source:x", 5))))
            .Should().BeTrue("the value moved");
    }

    /// <summary>**没变就是没变** / Unchanged is unchanged.</summary>
    /// <remarks>
    /// **反向对照**：只查"变了给 true"，一个永远返回 true 的实现照样通过。
    ///
    /// **The reverse control**: with only "changed gives true", an implementation that always returns true would pass.
    /// </remarks>
    [Fact]
    public void TheUnchangedPattern_ShouldBeFalse() {
        ConstraintPerturbationSupport.IntegerPatternChanged(
                Solution(Values(("source:x", 3))),
                Solution(Values(("source:x", 3))))
            .Should().BeFalse("nothing moved");

        ConstraintPerturbationSupport.IntegerPatternChanged(
                Solution(Values()),
                Solution(Values()))
            .Should().BeFalse("two empty solutions are equal, which is a real verdict rather than a missing one");
    }

    /// <summary>**少了一个变量也是"变了"** / A missing variable counts as a change.</summary>
    [Fact]
    public void TheMissingVariable_ShouldCountAsAChange() {
        ConstraintPerturbationSupport.IntegerPatternChanged(
                Solution(Values(("source:x", 3), ("source:y", 4))),
                Solution(Values(("source:x", 3))))
            .Should().BeTrue("the second solution lost a variable, so the pattern is not the same");
    }

    /// <summary>**显式声明优先于矩阵推断** / An explicit declaration outranks the matrix.</summary>
    /// <remarks>
    /// 声明里说"不支持"，**哪怕矩阵别处看起来支持，也必须判不支持**——否则这份声明形同虚设。
    ///
    /// When the override says unsupported, **that is the answer even if the matrix elsewhere looks like it supports it**, or the override means nothing.
    /// </remarks>
    [Fact]
    public void TheExplicitDeclaration_ShouldOutrankTheMatrix() {
        var declared = new CapabilityMatrix(
            ModelTypes: new HashSet<SolverModelType>(),
            AnalysisCapabilities: new Dictionary<AnalysisCapability, CapabilitySupport> {
                [AnalysisCapability.Conflict] = CapabilitySupport.Unsupported,
            });
        var inferred = new CapabilityMatrix(ModelTypes: new HashSet<SolverModelType>());

        ConstraintPerturbationSupport.CapabilityAllowed(declared, AnalysisCapability.Conflict)
            .Should().BeFalse("the backend explicitly declared it cannot do this");

        ConstraintPerturbationSupport.CapabilityAllowed(inferred, AnalysisCapability.Conflict)
            .Should().BeTrue("with no declaration and no model-type restriction, nothing forbids it");
    }

    /// <summary>**显式声明"支持"也压过矩阵的推断** / An explicit "supported" also outranks the inference.</summary>
    /// <remarks>
    /// **这是三态里最容易漏的一态。** B1007 已经覆盖了"显式不支持"和"空列表"，
    /// **但没覆盖"显式支持"**——于是一个只实现了 `return declared != Unsupported` 却把
    /// `Supported` 当 `Partial` 处理的实现照样通过。
    ///
    /// 这里构造一个**模型类型受限**的矩阵，而显式声明说"支持"：**声明必须赢，否则这份声明毫无用处。**
    ///
    /// **This is the state easiest to miss.** B1007 covered "explicitly unsupported" and "empty list" but **not "explicitly supported"** — so an implementation that maps `Supported` to
    /// `Partial` would still pass. The matrix here is model-type-restricted while the declaration says supported: **the declaration has to win, or it is pointless.**
    /// </remarks>
    [Fact]
    public void TheExplicitSupport_ShouldAlsoOutrankTheMatrix() {
        var declared = new CapabilityMatrix(
            ModelTypes: new HashSet<SolverModelType> { SolverModelType.Mip },
            AnalysisCapabilities: new Dictionary<AnalysisCapability, CapabilitySupport> {
                [AnalysisCapability.RhsPerturbation] = CapabilitySupport.Supported,
            });

        ConstraintPerturbationSupport.CapabilityAllowed(declared, AnalysisCapability.RhsPerturbation)
            .Should().BeTrue("the backend explicitly declared it can do this");

        // **反向对照**：同一份矩阵，不声明这个能力时结果由矩阵推断决定。
        // **The reverse control**: without the declaration the matrix alone decides.
        var undeclared = new CapabilityMatrix(ModelTypes: new HashSet<SolverModelType> { SolverModelType.Mip });
        bool inferred = ConstraintPerturbationSupport.CapabilityAllowed(undeclared, AnalysisCapability.RhsPerturbation);
        inferred.Should().BeTrue("an unrelated declaration must not deny this capability");
    }

    /// <summary>**模型类型列表为空是"没有限制"** / An empty model-type list means unrestricted.</summary>
    /// <remarks>
    /// 读反了，**一个没声明模型类型的后端会被判成什么都不会做**，而它其实没有限制。
    ///
    /// Read the other way, **a backend that declared no model types would be judged unable to do anything**, when in fact it is unrestricted.
    /// </remarks>
    [Fact]
    public void TheEmptyModelTypeList_ShouldNotDenyAnything() {
        var restricted = new CapabilityMatrix(ModelTypes: new HashSet<SolverModelType>());

        ConstraintPerturbationSupport.CapabilityAllowed(restricted, AnalysisCapability.RhsPerturbation)
            .Should().BeTrue("an empty list is no restriction at all");
    }

    /// <summary>**"有后端但两项都不允许"也要拒绝** / A backend that allows neither stage is still refused.</summary>
    /// <remarks>
    /// 只看"没后端"会把这个漏过去，**于是分析照样开始，跑完给出一份什么都没有的报告。**
    ///
    /// Checking only for a missing backend lets this through, **so the analysis starts and finishes with a report that says nothing.**
    /// </remarks>
    [Fact]
    public void TheUnusableBothStages_ShouldRefuseTheWholeReport() {
        ConstraintPerturbationSupport.Preflight(
                backendAvailable: true, needsRhsPerturbation: true, rhsAllowed: false,
                removalRequested: true, removalAllowed: false)
            .Should().Be(ConstraintPerturbationSupport.PreflightVerdict.WholeReportUnsupported,
                "a backend present is not enough when neither requested stage can run");
    }

    /// <summary>**没后端就整份拒绝** / No backend refuses the whole report.</summary>
    [Fact]
    public void TheMissingBackend_ShouldRefuseTheWholeReport() {
        ConstraintPerturbationSupport.Preflight(
                backendAvailable: false, needsRhsPerturbation: false, rhsAllowed: true,
                removalRequested: false, removalAllowed: true)
            .Should().Be(ConstraintPerturbationSupport.PreflightVerdict.WholeReportUnsupported);
    }

    /// <summary>**别过度拒绝：只要求删除且删除可用就要开始** / Do not over-reject a pure removal test.</summary>
    /// <remarks>
    /// **反向对照**：只看上一条，一个"只要有一项不支持就拒绝"的实现照样通过，
    /// **而它会把纯删除测试无理由地拒掉。**
    ///
    /// **The reverse control**: with only the previous criterion, an implementation that refuses whenever any single stage is disallowed would pass, **while it refuses a pure removal
    /// test for no reason.**
    /// </remarks>
    [Fact]
    public void ThePureRemovalTest_ShouldProceed() {
        ConstraintPerturbationSupport.Preflight(
                backendAvailable: true, needsRhsPerturbation: false, rhsAllowed: false,
                removalRequested: true, removalAllowed: true)
            .Should().Be(ConstraintPerturbationSupport.PreflightVerdict.Proceed,
                "only the removal test was asked for, and it is allowed");

        ConstraintPerturbationSupport.Preflight(
                backendAvailable: true, needsRhsPerturbation: true, rhsAllowed: true,
                removalRequested: false, removalAllowed: false)
            .Should().Be(ConstraintPerturbationSupport.PreflightVerdict.Proceed,
                "only RHS perturbation was asked for, and it is allowed");

        // **这一条才是"恰好一项不可用"**：前一版两个用例都是两项可用，
        // 于是"任一项不可用就拒绝"的变异照样通过——**判据没在测它声称要测的东西。**
        //
        // **This is the "exactly one unusable" case.** The earlier version had both usable in both cases, so a "refuse whenever any stage is unusable" mutation still passed — **the
        // criterion was not testing what it claimed.**
        ConstraintPerturbationSupport.Preflight(
                backendAvailable: true, needsRhsPerturbation: true, rhsAllowed: false,
                removalRequested: true, removalAllowed: true)
            .Should().Be(ConstraintPerturbationSupport.PreflightVerdict.Proceed,
                "RHS is disallowed but the removal test is allowed, so there is work to do");
    }

    /// <summary>**首个有效就停，后面的不覆盖它** / The first effective delta stops the sweep.</summary>
    [Fact]
    public void TheFirstEffective_ShouldStopTheSweep() {
        var probed = new List<double>();
        ConstraintPerturbationSupport.DeltaSweepResult result = ConstraintPerturbationSupport.RunDeltas(
            [new Flt64(1.0), new Flt64(2.0), new Flt64(4.0), new Flt64(8.0)],
            delta => {
                probed.Add(delta.ToDouble());
                return delta.ToDouble() >= 2.0
                    ? PerturbationOutcome.Effective
                    : PerturbationOutcome.NoObservedEffect;
            });

        result.FirstEffective!.Value.ToDouble().Should().Be(2.0, "the first effective delta is the answer");
        probed.Should().BeEquivalentTo([1.0, 2.0], "the sweep stops as soon as it is effective");
        result.PreviousNoEffect!.Value.ToDouble().Should().Be(1.0, "the last no-effect delta is kept as the lower side");
    }

    /// <summary>**`Unknown` 既不记也不停** / Unknown neither records nor stops.</summary>
    /// <remarks>
    /// **这是那个家族的第六次。** 把"没测出来"当成"无效果"，`PreviousNoEffect` 会指向一个根本没测的 delta；
    /// 而停在它上面会让扫描提前结束。
    ///
    /// **This is the sixth appearance of that family.** Treating "not measured" as "no effect" makes `PreviousNoEffect` point at a delta that measured nothing, and stopping on it
    /// ends the sweep early.
    /// </remarks>
    [Fact]
    public void TheUnknownDelta_ShouldNeitherRecordNorStop() {
        ConstraintPerturbationSupport.DeltaSweepResult result = ConstraintPerturbationSupport.RunDeltas(
            [new Flt64(1.0), new Flt64(2.0), new Flt64(4.0)],
            delta => delta.ToDouble() switch {
                1.0 => PerturbationOutcome.Unknown,
                2.0 => PerturbationOutcome.NoObservedEffect,
                _ => PerturbationOutcome.Effective,
            });

        result.PreviousNoEffect!.Value.ToDouble().Should().Be(2.0,
            "the undecided delta must not become the no-effect side");
        result.FirstEffective!.Value.ToDouble().Should().Be(4.0,
            "the sweep kept going past the undecided delta and found the real effect");
        result.Deltas.Should().HaveCount(3, "every delta that was probed is recorded");

        // **这一条才是"末尾是 Unknown"**：上面那条里后面还有一条无效果 delta，
        // **把被污染的 `PreviousNoEffect` 覆盖掉了**，于是变异照样通过。
        //
        // **This is the "Unknown comes last" case.** In the case above a later no-effect delta **overwrote the tainted `PreviousNoEffect`**, so the mutation still passed.
        ConstraintPerturbationSupport.DeltaSweepResult trailing = ConstraintPerturbationSupport.RunDeltas(
            [new Flt64(1.0), new Flt64(2.0)],
            delta => delta.ToDouble() < 2.0
                ? PerturbationOutcome.NoObservedEffect
                : PerturbationOutcome.Unknown);

        trailing.PreviousNoEffect!.Value.ToDouble().Should().Be(1.0,
            "the last probed delta was undecided, so it must not become the no-effect side");
    }

    /// <summary>**没有有效结论时两边都可以是 null** / No effective delta leaves it open.</summary>
    [Fact]
    public void TheAllNoEffectSweep_ShouldHaveNoEffective() {
        ConstraintPerturbationSupport.DeltaSweepResult result = ConstraintPerturbationSupport.RunDeltas(
            [new Flt64(1.0), new Flt64(2.0)],
            _ => PerturbationOutcome.NoObservedEffect);

        result.FirstEffective.Should().BeNull("nothing was ever effective");
        result.PreviousNoEffect!.Value.ToDouble().Should().Be(2.0, "the last no-effect delta is kept");
    }
}
