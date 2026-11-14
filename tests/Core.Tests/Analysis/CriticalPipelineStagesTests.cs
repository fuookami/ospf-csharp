#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 流水线的阶段决策 / The pipeline's stage decisions.
/// </summary>
/// <remarks>
/// **两条不同的"不跑"理由混成一条，读者就无法区分"我关了它"和"环境里没有它"。**
///
/// **Merged skip reasons leave the reader unable to tell "I switched it off" from "this build cannot do it".**
/// </remarks>
public class CriticalPipelineStagesTests {
    /// <summary>**"关掉"优先于"没后端"** / Disabled outranks a missing backend.</summary>
    /// <remarks>
    /// 先判后端的话，一个"关掉扰动且没有后端"的分析会报"后端未提供"——
    /// **而真正的原因是调用方根本没要它，于是读者会去找一个不需要的后端。**
    ///
    /// Checking the backend first would make a "perturbation off, no backend" run report "no backend was supplied" — **while the real reason is that the caller never asked for it,
    /// sending the reader after a backend they do not need.**
    /// </remarks>
    [Fact]
    public void TheDisabledStage_ShouldWinOverAMissingBackend() {
        CriticalPipelineStages.Perturbation(enabled: false, backendAvailable: false, candidateCount: 5, candidateLimit: 3)
            .Should().Be(StageDecision.Skip(CriticalPipelineStages.PerturbationDisabled),
                "the caller's own switch is the most upstream reason");

        // **反向对照**：开着但没后端，必须报"没后端"。
        CriticalPipelineStages.Perturbation(enabled: true, backendAvailable: false, candidateCount: 5, candidateLimit: 3)
            .Reason.Should().Be(CriticalPipelineStages.PerturbationBackendMissing,
                "with the stage on, the missing backend is the reason");
    }

    /// <summary>**"关掉"优先于"没目标"** / Disabled outranks a missing target.</summary>
    [Fact]
    public void TheTargetStage_ShouldReportTheCallersSwitchFirst() {
        CriticalPipelineStages.Target(enabled: false, targetSupplied: false).Reason
            .Should().Be(CriticalPipelineStages.TargetDisabled,
                "saying 'no target was supplied' would read like a forgotten argument");

        CriticalPipelineStages.Target(enabled: true, targetSupplied: false).Reason
            .Should().Be(CriticalPipelineStages.TargetMissing);

        CriticalPipelineStages.Target(enabled: true, targetSupplied: true).Runs.Should().BeTrue();
    }

    /// <summary>**没有短名单就不跑扰动** / No shortlist means no perturbation.</summary>
    /// <remarks>
    /// 上限为 0 与候选为空是两回事，**但都意味着"没有可分析的短名单"。**
    ///
    /// A zero limit and an empty candidate list are different things, **but both mean there is no shortlist to analyse.**
    /// </remarks>
    [Fact]
    public void TheEmptyShortlist_ShouldSkipPerturbation() {
        CriticalPipelineStages.Perturbation(enabled: true, backendAvailable: true, candidateCount: 5, candidateLimit: 0)
            .Reason.Should().Be(CriticalPipelineStages.ShortlistEmpty, "the limit zeroes out the shortlist");

        CriticalPipelineStages.Perturbation(enabled: true, backendAvailable: true, candidateCount: 0, candidateLimit: 5)
            .Reason.Should().Be(CriticalPipelineStages.ShortlistEmpty, "there was nothing to shortlist");

        CriticalPipelineStages.Perturbation(enabled: true, backendAvailable: true, candidateCount: 2, candidateLimit: 5)
            .Runs.Should().BeTrue("there are candidates within the limit");
    }

    /// <summary>**局部敏感度的两条理由分开** / The two sensitivity reasons stay apart.</summary>
    [Fact]
    public void TheSensitivityStage_ShouldKeepItsTwoReasons() {
        CriticalPipelineStages.LocalSensitivity(enabled: false, backendAvailable: false).Reason
            .Should().Be(CriticalPipelineStages.SensitivityDisabled);
        CriticalPipelineStages.LocalSensitivity(enabled: true, backendAvailable: false).Reason
            .Should().Be(CriticalPipelineStages.SensitivityBackendMissing);
        CriticalPipelineStages.LocalSensitivity(enabled: true, backendAvailable: true).Runs.Should().BeTrue();
    }

    /// <summary>**八条理由两两不同** / The eight reasons are pairwise distinct.</summary>
    /// <remarks>
    /// **合并其中任意两条，这条就红。** 这是这批唯一的"结构性"判据：
    /// 它不测某个分支对不对，**测的是"这些理由还是八条"。**
    ///
    /// **Merging any two turns this red.** It is the only structural criterion here: it does not test a branch, **it tests that there are still eight reasons.**
    /// </remarks>
    [Fact]
    public void TheSkipReasons_ShouldAllBeDistinct() {
        CriticalPipelineStages.AreReasonsDistinct().Should().BeTrue(
            "each stage has its own wording, so a reader can tell a configuration choice from a capability gap");
    }

    /// <summary>**跑的时候没有理由** / A running stage carries no reason.</summary>
    [Fact]
    public void TheRunningStage_ShouldCarryNoReason() {
        StageDecision decision = CriticalPipelineStages.Target(enabled: true, targetSupplied: true);
        decision.Runs.Should().BeTrue();
        decision.Reason.Should().BeNull("a stage that ran needs no excuse");
    }
}
