#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Progress;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Progress;

/// <summary>
/// 进度上下文必须真的做单调化、真的做本地化、真的检查取消。
/// The progress context must really monotonise, really localize, and really check cancellation.
/// </summary>
/// <remarks>
/// 基线 `progress/SolverProgress.kt`（224 行）+ `ProgressAdapters.kt`（127 行）是一整套上报系统。
/// C# 侧**完全没有**：`grep ProgressReporter` 只命中 Benders 的一个局部委托字段，与这套无关。
///
/// 三条判据分别钉住 `Report` 里的三个环节。**它们之所以必须分开**：`Report` 的顺序是
/// "取消 → 本地化 → 单调 → 上报"，任何一环写错，只要其余环正确，**只测"上报成功"的判据依然会绿**。
///
/// The baseline's `progress/SolverProgress.kt` (224 lines) plus `ProgressAdapters.kt` (127 lines) is a whole reporting system. **The C# side had none**:
/// `grep ProgressReporter` only matched a local delegate field in Benders, unrelated to this.
///
/// The three criteria pin the three stages inside `Report`. **They must be separate** because `Report` runs "cancel → localize → monotonise → report", and if any one
/// stage is wrong while the others are right, **a criterion that only checks "reporting succeeded" still passes**.
/// </remarks>
public class SolverProgressContextTests {
    private sealed class RecordingReporter : IProgressReporter {
        public List<SolverProgressSnapshot> Snapshots { get; } = new();

        public Result<Success, ErrorCode, Error<ErrorCode>> Report(SolverProgressSnapshot snapshot) {
            Snapshots.Add(snapshot);
            return Results.Ok(Results.SuccessInstance);
        }
    }

    private sealed class UpperCaseResolver : IProgressLabelResolver {
        public string Resolve(string key, string defaultText, string locale) =>
            $"{locale}:{defaultText.ToUpperInvariant()}";
    }

    private static SolverProgressSnapshot Snapshot(int inStage, int overall, SolverStage? stage = null) =>
        new(
            Stage: stage ?? SolverStages.ColumnGeneration,
            ProgressInStage: inStage,
            OverallProgress: overall);

    /// <summary>**进度只增不减** / Progress never decreases.</summary>
    /// <remarks>
    /// 基线 `MonotonicProgressState.accept` 取 `max(上次值, 本次值)`。若实现直接透传快照，
    /// 进度条会**倒退**——这在长时间求解里是明显可见的错误，但"上报成功"的判据不会发现。
    ///
    /// The baseline's `MonotonicProgressState.accept` takes `max(last, current)`. If the implementation passed the snapshot through unchanged, the progress bar would
    /// **go backwards** — an obvious error in long solves that a "reporting succeeded" criterion would never catch.
    /// </remarks>
    [Fact]
    public void Report_ShouldNeverDecreaseProgress() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);

        context.Report(Snapshot(60, 40));
        context.Report(Snapshot(20, 10));

        reporter.Snapshots.Should().HaveCount(2);
        reporter.Snapshots[1].ProgressInStage.Should().Be(60, "in-stage progress must not regress");
        reporter.Snapshots[1].OverallProgress.Should().Be(40, "overall progress must not regress");
    }

    /// <summary>**阶段进度按阶段分别单调** / In-stage progress is monotonic per stage.</summary>
    /// <remarks>
    /// 基线用 `stageProgress` 按 `stage.id` **分开记账**。若实现只保留一个全局阶段进度，
    /// 换阶段后新阶段的进度会被上一阶段的高值压住。
    ///
    /// The baseline keeps **separate accounts** in `stageProgress` keyed by `stage.id`. If the implementation kept one global in-stage value, a new stage's progress
    /// would be pinned by the previous stage's high-water mark.
    /// </remarks>
    [Fact]
    public void Report_ShouldTrackInStageProgressPerStage() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);

        context.Report(Snapshot(90, 50, SolverStages.ColumnGeneration));
        context.Report(Snapshot(10, 60, SolverStages.Milp));

        reporter.Snapshots[1].ProgressInStage.Should().Be(10, "a different stage starts from its own value");
        reporter.Snapshots[1].OverallProgress.Should().Be(60, "overall is still monotonic");
    }

    /// <summary>**有解析器时必须真的本地化** / With a resolver, localization must really happen.</summary>
    [Fact]
    public void Report_ShouldResolveLabelsWhenAResolverIsPresent() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(
            reporter,
            labelResolver: new UpperCaseResolver(),
            locale: "en-US");

        context.Report(Snapshot(10, 10));

        reporter.Snapshots.Should().ContainSingle();
        reporter.Snapshots[0].ResolvedStageLabel.Should().Be("en-US:列生成");
    }

    /// <summary>**无解析器时不得伪造已解析标签** / Without a resolver, resolved labels must not be faked.</summary>
    /// <remarks>
    /// 基线在无解析器时**直接 `Normalized`**，不动两个已解析标签。若实现无条件填上默认标签，
    /// 调用方就无法区分"本地化过"与"没本地化"。
    ///
    /// The baseline calls **`Normalized` directly** when there is no resolver and leaves the two resolved labels alone. If the implementation unconditionally filled
    /// in the defaults, callers could not tell "localized" from "not localized".
    /// </remarks>
    [Fact]
    public void Report_ShouldNotFakeResolvedLabelsWithoutAResolver() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);

        context.Report(Snapshot(10, 10));

        reporter.Snapshots[0].ResolvedStageLabel.Should().BeNull();
        reporter.Snapshots[0].EffectiveStageLabel.Should().Be(SolverStages.ColumnGeneration.FallbackLabel);
    }

    /// <summary>**已取消时一次都不上报** / When cancelled, nothing is reported at all.</summary>
    /// <remarks>
    /// 基线 `report` 的第一行就是 `throwIfCancelled`。**顺序很重要**：若先上报再检查取消，
    /// 已取消之后仍会有快照流出去，下游会看到本不该发生的进度。
    ///
    /// The baseline's `report` starts with `throwIfCancelled`. **Order matters**: if it reported before checking, snapshots would still flow out after cancellation and
    /// downstream would see progress that should not have happened.
    /// </remarks>
    [Fact]
    public void Report_ShouldThrowAndReportNothingWhenCancelled() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter, isCancelled: static () => true);

        Action report = () => context.Report(Snapshot(10, 10));

        report.Should().Throw<OperationCanceledException>();
        reporter.Snapshots.Should().BeEmpty("a cancelled context must not emit a snapshot");
    }

    /// <summary>**子阶段模板必须用参数渲染** / A sub-stage template must be rendered with its arguments.</summary>
    [Fact]
    public void SubStage_ShouldRenderItsTemplateWithArguments() {
        var subStage = new SolverSubStage(
            Key: "iteration",
            DefaultTemplate: "第 {index} 轮 / 共 {total} 轮",
            MessageKey: "i18n.ospf.substage.iteration",
            Args: new Dictionary<string, string> { ["index"] = "3", ["total"] = "16" });

        subStage.RenderDefault().Should().Be("第 3 轮 / 共 16 轮");
    }

    /// <summary>**内置阶段目录必须与基线逐项一致** / The built-in stage catalog must match the baseline item by item.</summary>
    /// <remarks>
    /// 基线 `SolverStages` 有 9 项。少一项或 id 拼错，下游的 `messageKey` 就会指向不存在的键。
    ///
    /// The baseline `SolverStages` has 9 entries. A missing entry or a misspelled id would make downstream `messageKey`s point at keys that do not exist.
    /// </remarks>
    [Fact]
    public void SolverStages_ShouldMatchTheBaselineCatalog() {
        SolverStages.Entries.Should().HaveCount(9);
        SolverStages.Entries[0].Id.Value.Should().Be("MODEL_BUILDING");
        SolverStages.Entries[3].Id.Value.Should().Be("COLUMN_GENERATION");
        SolverStages.Entries[8].Id.Value.Should().Be("POST_PROCESSING");
        SolverStages.Entries.Should().OnlyContain(stage => stage.MessageKey == "i18n.ospf.stage." + stage.Id.Value);
    }

    /// <summary>**百分比必须收敛到 0..100** / Percentages must clamp to 0..100.</summary>
    /// <remarks>
    /// **这条判据要绕开单调化**：首版直接走 `Report`，结果"不收敛"的变异**存活**——
    /// 因为 `AcceptMonotonic` 里也有 `Math.Clamp`，它把 `Normalized` 的错误兜住了。
    ///
    /// 判据的**作用域比它想守的东西宽**，于是守不住。改成直接调 `Normalized`，
    /// 隔离出这一层。
    ///
    /// **This criterion must bypass monotonisation.** The first version went through `Report` and the "not clamped" mutation **survived** — because
    /// `AcceptMonotonic` also clamps, masking the error in `Normalized`.
    ///
    /// The criterion's **scope was wider than the thing it meant to guard**, so it guarded nothing. It now calls `Normalized` directly to isolate that layer.
    /// </remarks>
    [Fact]
    public void Normalized_ShouldClampPercentages() {
        SolverProgressSnapshot snapshot = Snapshot(150, -20);

        SolverProgressSnapshot normalized = snapshot.Normalized();

        normalized.ProgressInStage.Should().Be(100);
        normalized.OverallProgress.Should().Be(0);
    }
}
