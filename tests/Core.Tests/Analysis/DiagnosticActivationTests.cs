#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 诊断激活集合与冲突提取层级 / The diagnostic activation set and the conflict-extraction tier.
/// </summary>
/// <remarks>
/// **B968 补 `DiagnosticActivation`（基线 316 行）**：`ActivationState`、`DiagnosticActivation`、
/// `ConflictExtractionTier` 与 `DiagnosticActivationSet`。它只依赖 B964 与 B966 已补的类型。
///
/// 判据钉的是**证据边界**：哪些来源被认为"在场"、后端返回的成员里哪些能穿过这层。
/// **这层漏一个，求解器生成的辅助元素就会出现在公开报告里。**
///
/// **B968 ported `DiagnosticActivation` (316 baseline lines)**. The criteria pin the **evidence boundary**: which sources count as present, and which backend members may cross
/// it. **One leak here and a solver-generated auxiliary element shows up in a public report.**
/// </remarks>
public class DiagnosticActivationTests {
    private static DiagnosticSource.Constraint Constraint(string id) => new(new ConstraintId(id));

    /// <summary>**`Conditional` 不许冒充原生层级** / `Conditional` never masquerades as a native tier.</summary>
    /// <remarks>
    /// 三档顺序固定：原生 unsat core → assumption 提取 → 重复求解。**一个"可能支持"的声明如果进了前两级，
    /// 上层就会走一条实际没实现的路径。**
    ///
    /// The order is fixed. **A "might support" declaration reaching the first two tiers sends the layer above down a path that is not implemented.**
    /// </remarks>
    [Fact]
    public void TheTierSelection_ShouldNeverPromoteConditional() {
        var conditionalCore = new CapabilityMatrix(
            new System.Collections.Generic.HashSet<SolverModelType>(),
            AnalysisCapabilities: new System.Collections.Generic.Dictionary<AnalysisCapability, CapabilitySupport> {
                [AnalysisCapability.NativeUnsatCore] = CapabilitySupport.Conditional,
            });
        ConflictExtractionTiers.Select(conditionalCore).Should().Be(ConflictExtractionTier.Unavailable,
            "Conditional is not native, so it cannot reach any tier on its own");

        var nativeCore = new CapabilityMatrix(new System.Collections.Generic.HashSet<SolverModelType>(),
            NativeUnsatCore: true);
        ConflictExtractionTiers.Select(nativeCore).Should().Be(ConflictExtractionTier.NativeUnsatCore,
            "native unsat core is the first tier");

        var nativeCoreDeclared = new CapabilityMatrix(new System.Collections.Generic.HashSet<SolverModelType>(),
            AnalysisCapabilities: new System.Collections.Generic.Dictionary<AnalysisCapability, CapabilitySupport> {
                [AnalysisCapability.NativeUnsatCore] = CapabilitySupport.Supported,
            });
        ConflictExtractionTiers.Select(nativeCoreDeclared)
            .Should().Be(ConflictExtractionTier.NativeUnsatCore,
                "an explicit Supported is the other way into the first tier");

        var assumptions = new CapabilityMatrix(new System.Collections.Generic.HashSet<SolverModelType>(),
            NativeAssumptionSolving: true);
        ConflictExtractionTiers.Select(assumptions).Should().Be(ConflictExtractionTier.AssumptionExtraction,
            "with no native core, assumption extraction is the next tier");

        var fallback = new CapabilityMatrix(new System.Collections.Generic.HashSet<SolverModelType>(),
            FallbackSatisfactionOnly: true,
            AnalysisCapabilities: new System.Collections.Generic.Dictionary<AnalysisCapability, CapabilitySupport> {
                [AnalysisCapability.Conflict] = CapabilitySupport.Conditional,
            });
        ConflictExtractionTiers.Select(fallback).Should().Be(ConflictExtractionTier.RepeatedSolving,
            "a declared fallback reaches the last tier");

        ConflictExtractionTiers.Select(new CapabilityMatrix(new System.Collections.Generic.HashSet<SolverModelType>()))
            .Should().Be(ConflictExtractionTier.Unavailable);
    }

    /// <summary>**层级的三个谓词与稳定名称** / The tier's three predicates and its stable name.</summary>
    [Fact]
    public void TheTierPredicates_ShouldAgreeWithTheEnum() {
        ConflictExtractionTier.NativeUnsatCore.IsNative().Should().BeTrue();
        ConflictExtractionTier.AssumptionExtraction.IsNative().Should().BeFalse(
            "assumption extraction still avoids repeated solving but it is not the native core");
        ConflictExtractionTier.Unavailable.IsAvailable().Should().BeFalse();
        ConflictExtractionTier.RepeatedSolving.IsAvailable().Should().BeTrue();

        ConflictExtractionTier.NativeUnsatCore.TierName().Should().Be("native-unsat-core");
        ConflictExtractionTier.AssumptionExtraction.TierName().Should().Be("assumption-extraction");
        ConflictExtractionTier.RepeatedSolving.TierName().Should().Be("repeated-solving");
        ConflictExtractionTier.Unavailable.TierName().Should().Be("unavailable");
    }

    /// <summary>**未出现的来源按未激活处理** / An absent source is inactive.</summary>
    /// <remarks>
    /// 这是这一层最容易被"优化"掉的一条：写成"不认识的按激活"，会让调用方**忘了声明证据全集也不会出问题**——
    /// 直到某天后端返回一个它从没见过的成员。
    ///
    /// This is the one most likely to be "optimised" away: reading unknown as active means **forgetting to declare the evidence universe never shows up as a problem** — until
    /// the day a backend returns a member nobody has seen.
    /// </remarks>
    [Fact]
    public void TheAbsentSource_ShouldBeInactive() {
        var set = DiagnosticActivationSet.Empty();
        set.IsActive(Constraint("c1")).Should().BeFalse("nothing is declared yet");

        set.Activate(Constraint("c1"));
        set.IsActive(Constraint("c1")).Should().BeTrue();
        set.IsActive(Constraint("c2")).Should().BeFalse("a source that was never mentioned stays inactive");

        set.Deactivate(Constraint("c1"));
        set.IsActive(Constraint("c1")).Should().BeFalse("a deactivation overrides the earlier activation");
        set.Size.Should().Be(1, "put replaces, it does not append");
    }

    /// <summary>**后端返回的未知成员被丢弃** / An unrecognised backend member is dropped.</summary>
    /// <remarks>
    /// 回映以**本激活集合为唯一来源**：先枚举激活的原始证据，再看后端成员是否指向其中之一。
    /// 反过来的写法（遍历后端成员、反查）会把**求解器自造的辅助元素**放进结果。
    ///
    /// Remapping is driven **only by this activation set**. The other direction — walking the backend members and looking them up — lets **a solver-made auxiliary element**
    /// into the result.
    /// </remarks>
    [Fact]
    public void TheRemap_ShouldDropUnknownBackendMembers() {
        DiagnosticSource.Constraint active = Constraint("c1");
        var set = DiagnosticActivationSet.Empty();
        set.Activate(active);
        set.Activate(Constraint("c2"));
        set.Deactivate(Constraint("c2"));

        InfeasibilityMember known = active.AsInfeasibilityMember()
            ?? throw new Xunit.Sdk.XunitException("a constraint source must map to an infeasibility member");
        // **一个本仓没有对应原始来源的后端成员**：它必须被丢掉。
        // **A backend member with no matching original source**: it has to be dropped.
        var unknown = new InfeasibilityMember.ConstraintMember(new ConstraintId("backend-auxiliary-7"));

        set.RemapBackendEvidence([known, unknown]).Should().ContainSingle()
            .Which.Should().Be(active, "only active, recognised evidence comes back");
        set.RemapBackendEvidence([unknown]).Should().BeEmpty(
            "an unknown member must never survive the remap");
    }

    /// <summary>**状态进稳定身份** / The state is part of the stable identity.</summary>
    [Fact]
    public void TheStableId_ShouldCarryTheState() {
        DiagnosticSource.Constraint source = Constraint("c1");
        DiagnosticActivation.Activate(source).StableId
            .Should().Be("activated:" + source.StableId);
        DiagnosticActivation.Deactivate(source).StableId
            .Should().Be("deactivated:" + source.StableId);

        DiagnosticActivation.Activate(source).StableId
            .Should().NotBe(DiagnosticActivation.Deactivate(source).StableId,
                "two records that differ only in state must not share a cache key");
    }

    /// <summary>**`put` 覆盖而不是追加，所以重复来源根本构造不出来** / `put` replaces, so a duplicate cannot be built at all.</summary>
    /// <remarks>
    /// 基线用"构造后再校验"来防重复。这里 `Put` 先删同源再追加，**重复在入口就不可能存在**——
    /// 校验因此永远通过。**这一点要说清楚，否则那条校验看起来像在防一件不可能发生的事。**
    ///
    /// The baseline guards duplicates by validating after the fact. Here `Put` removes the same source first, so **a duplicate cannot exist at the entry point** and the
    /// validation therefore always passes. **Say that out loud, or the check looks like it guards something that cannot happen.**
    /// </remarks>
    [Fact]
    public void ThePut_ShouldReplaceRatherThanAppend() {
        var set = DiagnosticActivationSet.Empty();
        set.Activate(Constraint("c1"));
        set.Size.Should().Be(1);

        set.Put(DiagnosticActivation.Deactivate(Constraint("c1")));
        set.Size.Should().Be(1, "put must replace, so the size stays the same");
        set.IsActive(Constraint("c1")).Should().BeFalse("the later deactivation wins");
        set.DeactivatedSources().Should().ContainSingle();
        // **校验通过是结论，不是"没红就算过"**：要说明它通过是因为集合里确实没有重复。
        // **The passing validation is the conclusion, not "green is good enough"**: it passes because there really is no duplicate.
        // **说清结果是什么，而不是只说"没失败"**：`Validate` 成功时携带的是 `Success` 值。
        // **Say what the result is, not just that nothing failed**: a successful `Validate` carries a `Success` value.
        set.Validate().Value.Should().BeOfType<Fuookami.Ospf.Utils.Functional.Success>(
            "a set built through put cannot hold a duplicate source, so validation returns the success value itself");
    }
}
