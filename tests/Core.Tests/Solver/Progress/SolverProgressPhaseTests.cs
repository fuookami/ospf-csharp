#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Progress;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Progress;

/// <summary>
/// `Phase(start, end)` 必须把子上下文的**阶段内**进度重映射到父级的带里。
/// `Phase(start, end)` must remap the sub-context's **in-stage** progress into the parent's band.
/// </summary>
/// <remarks>
/// 重映射的公式是
/// `overallProgress = start + clamp(progressInStage, 0, 100) × (end − start) / 100`。
///
/// **重映射的输入是 `progressInStage`，不是子上下文的 `overallProgress`。**
/// 这一条是整批最容易写错的地方：用 `overallProgress` 也能算出"看起来合理"的数字，
/// 但那是**二次挤压**——子求解器本来就按 0—100 报的阶段内进度，被当成总体进度又压一遍。
///
/// 判据特意构造**两者不相等**的快照（`progressInStage = 50`、`overallProgress = 90`），
/// 这样两种实现给出**不同的结果**，才能区分。
///
/// Its `phase(start, end)` computes `overallProgress = start + progressInStage * (end - start) / 100` (snippet above).
///
/// **The remap input is `progressInStage`, not the sub-context's `overallProgress`.** This is the easiest thing to get wrong in the whole batch: using
/// `overallProgress` also produces a **plausible-looking** number, but it is a **double squash** — the sub-solver's in-stage progress (already 0—100) is treated as
/// overall progress and squashed again.
///
/// The criteria deliberately build snapshots where **the two differ** (`progressInStage = 50`, `overallProgress = 90`) so the two implementations produce
/// **different results** and can be told apart.
/// </remarks>
public class SolverProgressPhaseTests {
    private sealed class RecordingReporter : IProgressReporter {
        public List<SolverProgressSnapshot> Snapshots { get; } = new();

        public Result<Success, ErrorCode, Error<ErrorCode>> Report(SolverProgressSnapshot snapshot) {
            Snapshots.Add(snapshot);
            return Results.Ok(Results.SuccessInstance);
        }
    }

    private static SolverProgressSnapshot MakeSnapshot(int progressInStage, int overallProgress) =>
        new(
            Stage: SolverStages.ColumnGeneration,
            ProgressInStage: progressInStage,
            OverallProgress: overallProgress);

    /// <summary>**重映射的输入必须是 `progressInStage`** / The remap input must be `progressInStage`.</summary>
    [Fact]
    public void Phase_ShouldRemapFromInStageProgressNotOverallProgress() {
        var reporter = new RecordingReporter();
        var parent = new SolverProgressContext(reporter);
        SolverProgressContext band = parent.Phase(20, 75);

        // 阶段内 50 → 20 + 50*55/100 = 20 + 27 = 47
        // 若误用 overallProgress 90 → 20 + 90*55/100 = 20 + 49 = 69
        band.Report(MakeSnapshot(progressInStage: 50, overallProgress: 90));

        reporter.Snapshots[0].OverallProgress.Should().Be(47,
            "the remap must use progressInStage (50), not overallProgress (90)");
    }

    /// <summary>**区间两端必须精确落位** / Both band ends must land exactly.</summary>
    [Fact]
    public void Phase_ShouldHitBothBandEndsExactly() {
        var reporter = new RecordingReporter();
        var parent = new SolverProgressContext(reporter);
        SolverProgressContext band = parent.Phase(20, 75);

        band.Report(MakeSnapshot(progressInStage: 0, overallProgress: 0));
        band.Report(MakeSnapshot(progressInStage: 100, overallProgress: 0));

        reporter.Snapshots[0].OverallProgress.Should().Be(20);
        reporter.Snapshots[1].OverallProgress.Should().Be(75);
    }

    /// <summary>**阶段内进度越界必须夹取到 0—100** / Out-of-range in-stage progress must be clamped to 0—100.</summary>
    /// <remarks>
    /// **上界与下界必须用两个独立的父上下文测。** 首版把两次上报放进同一个父上下文：
    /// 上界那次把父级单调化状态推到 75，下界那次即使正确算出 20，**也会被单调化抬回 75**——
    /// 判据看到的是 75，误以为是夹取失效。
    ///
    /// **这又是"判据作用域比它想守的东西宽"**：我要测的是 `Phase` 的夹取，
    /// 却让父上下文的单调化参与了结果。隔离后两条各自成立。
    ///
    /// **The upper and lower bounds must use two independent parent contexts.** The first version put both reports in one parent: the upper-bound report pushed the
    /// parent's monotonic state to 75, so the lower-bound report — even computing 20 correctly — **was lifted back to 75 by the monotonic guard**, and the criterion
    /// read 75 as if the clamp had failed.
    ///
    /// **This is again "the criterion's scope is wider than the thing it protects"** (same shape as ): the intent was to test `Phase`'s clamp, but the parent's
    /// monotonic guard took part in the result. Isolated, both hold.
    ///
    /// **这条判据的另一处能力边界**：`Phase` 里的 `Math.Clamp` **没有独立可证伪性**。
    /// 变异（去掉 `Phase` 的夹取）**存活**——因为**子上下文自己的 `AcceptMonotonic` 已经夹过**
    /// `ProgressInStage`（`SolverProgressContext.AcceptMonotonic` 里对 `inStage` 做了 `Clamp(..., 0, 100)`），
    /// 走到 `Phase` 的上报器时值已在区间内。**冗余**，与 `Normalized`、取消检查点同型。
    ///
    /// **Another limit of this criterion**: the `Math.Clamp` inside `Phase` **has no independent falsifiability**. The mutation (removing `Phase`'s clamp)
    /// **survives** — the sub-context's own `AcceptMonotonic` already clamps `ProgressInStage` (`Clamp(..., 0, 100)` on `inStage`), so the value is already in range by
    /// the time it reaches `Phase`'s reporter. **Redundant**, the same shape as the earlier `Normalized` and the earlier cancellation checkpoints.
    /// </remarks>
    [Fact]
    public void Phase_ShouldClampOutOfRangeInStageProgress() {
        var upperReporter = new RecordingReporter();
        new SolverProgressContext(upperReporter).Phase(20, 75)
            .Report(MakeSnapshot(progressInStage: 250, overallProgress: 0));
        upperReporter.Snapshots[0].OverallProgress.Should().Be(75, "250 clamps to 100 → the band end");

        var lowerReporter = new RecordingReporter();
        new SolverProgressContext(lowerReporter).Phase(20, 75)
            .Report(MakeSnapshot(progressInStage: -40, overallProgress: 0));
        lowerReporter.Snapshots[0].OverallProgress.Should().Be(20, "-40 clamps to 0 → the band start");
    }

    /// <summary>**重映射后仍走父上下文的上报器** / The remapped snapshot must still reach the parent's reporter.</summary>
    [Fact]
    public void Phase_ShouldForwardToTheParentReporter() {
        var reporter = new RecordingReporter();
        var parent = new SolverProgressContext(reporter);

        parent.Phase(0, 10).Report(MakeSnapshot(progressInStage: 100, overallProgress: 0));

        reporter.Snapshots.Should().ContainSingle("the band context must not swallow the snapshot");
    }
}
