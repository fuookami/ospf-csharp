// 裁切工艺判据 / Criteria for the cutting process.

using FluentAssertions;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp2d.Tests.Infrastructure;

/// <summary>
/// **工艺与工序级数** / The process and its stage limits.
/// </summary>
/// <remarks>
/// 对应。四条都是纯判断，**不需要几何、不需要求解器**。
///
///. All four are pure predicates: **no geometry, no solver**.
/// </remarks>
public class CuttingProcess2Tests {
    /// <summary>**任意排样不受级数约束** / Arbitrary nesting has no stage limit.</summary>
    /// <remarks>
    /// 返回 0 或 -1 会让**一级工序都被判成越界**，于是"激光切"连最简单的排样都过不了校验。
    ///
    /// Returning 0 or -1 would make **even a single-stage process out of bounds**, so "laser cutting" would fail validation on the simplest nesting.
    /// </remarks>
    [Fact]
    public void MaxStage_ShouldTreatFreeformAsUnbounded() {
        CuttingProcess2.Freeform.MaxStage().Should().Be(int.MaxValue);
        CuttingProcess2.GuillotineTwoStage.MaxStage().Should().Be(2);
        CuttingProcess2.GuillotineThreeStage.MaxStage().Should().Be(3);
    }

    /// <summary>**断头台工艺要求切分树，任意排样不要求** / Guillotine requires a cut tree; arbitrary nesting does not.</summary>
    [Fact]
    public void RequiresCutTree_ShouldOnlyHoldForGuillotine() {
        CuttingProcess2.Freeform.RequiresCutTree().Should().BeFalse(
            "a contour-cutting machine needs coordinates, not a cut tree");
        CuttingProcess2.GuillotineTwoStage.RequiresCutTree().Should().BeTrue();
        CuttingProcess2.GuillotineThreeStage.RequiresCutTree().Should().BeTrue();
    }

    /// <summary>**级数从 1 起算，0 与负数一律非法** / Stages start at 1; zero and negatives are illegal.</summary>
    [Fact]
    public void AllowsStage_ShouldRejectNonPositiveStages() {
        CuttingProcess2.Freeform.AllowsStage(0).Should().BeFalse();
        CuttingProcess2.Freeform.AllowsStage(-1).Should().BeFalse();
        CuttingProcess2.Freeform.AllowsStage(1).Should().BeTrue();
        CuttingProcess2.Freeform.AllowsStage(99).Should().BeTrue("arbitrary nesting has no limit");

        CuttingProcess2.GuillotineTwoStage.AllowsStage(2).Should().BeTrue();
        CuttingProcess2.GuillotineTwoStage.AllowsStage(3).Should().BeFalse(
            "a two-stage guillotine cannot do a third stage");
        CuttingProcess2.GuillotineThreeStage.AllowsStage(3).Should().BeTrue();
    }

    /// <summary>**标签覆盖三级，越界时带上级数** / The label covers three stages and names the stage when out of range.</summary>
    [Fact]
    public void StageLabel_ShouldNameKnownStagesAndFallBackForOthers() {
        CuttingProcess2.GuillotineThreeStage.StageLabel(1).Should().Contain("strip");
        CuttingProcess2.GuillotineThreeStage.StageLabel(2).Should().Contain("block");
        CuttingProcess2.GuillotineThreeStage.StageLabel(3).Should().Contain("piece");
        CuttingProcess2.Freeform.StageLabel(7).Should().Contain("7",
            "an unnamed stage must still be identifiable in the message");
    }
}
