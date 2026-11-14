#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 批次里最弱的状态 / The weakest status in a batch.
/// </summary>
/// <remarks>
/// **取最后一个会让批次末尾的一个成功盖住前面某个 `Unknown`**——而 `Unknown` 恰恰是那份报告唯一想让人看见的东西。
///
/// **Taking the last lets a success at the end of the batch cover an earlier `Unknown`** — and that `Unknown` is the one thing the report exists to show.
/// </remarks>
public class CriticalityBatchStatusTests {
    /// <summary>**最弱的那个说了算，与位置无关** / The weakest wins, regardless of position.</summary>
    /// <remarks>
    /// **两种顺序都测**：`Unknown` 在前和在后的结果必须相同。
    /// 只测一种顺序会让"取最后一个"通过一半的用例。
    ///
    /// **Both orders**: `Unknown` first and `Unknown` last must give the same answer. Testing one order lets "take the last" pass half the cases.
    /// </remarks>
    [Fact]
    public void TheWeakestStatus_ShouldNotDependOnOrder() {
        CriticalityProfileAnalysisResult.WeakestStatus([AnalysisStatus.Unknown, AnalysisStatus.Reachable])
            .Should().Be(AnalysisStatus.Unknown, "an unknown early in the batch is not covered by a later success");

        CriticalityProfileAnalysisResult.WeakestStatus([AnalysisStatus.Reachable, AnalysisStatus.Unknown])
            .Should().Be(AnalysisStatus.Unknown, "an unknown at the end is not covered either");

        CriticalityProfileAnalysisResult.WeakestStatus([AnalysisStatus.Reachable])
            .Should().Be(AnalysisStatus.Reachable, "a batch of successes is a success");
    }

    /// <summary>**`Unsupported` 比 `Unknown` 更弱** / Unsupported is weaker than unknown.</summary>
    /// <remarks>
    /// 这个次序容易被写反，因为 `Unknown` 读起来"更不知道"。
    /// **但 `Unsupported` 说的是"这条路走不通"，`Unknown` 说的是"这次没测出来"——前者更接近放弃。**
    ///
    /// The order is easy to invert because `Unknown` reads as "less known". **But `Unsupported` says "this route is closed", `Unknown` says "not measured this time" — the former is closer to
    /// giving up.**
    /// </remarks>
    [Fact]
    public void TheUnsupportedStatus_ShouldBeWeakerThanUnknown() {
        CriticalityProfileAnalysisResult.WeakestStatus([AnalysisStatus.Unknown, AnalysisStatus.Unsupported])
            .Should().Be(AnalysisStatus.Unsupported, "a closed route outranks a missing measurement");
    }

    /// <summary>**空批次给 `Unknown`，不是 `Reachable`** / An empty batch yields unknown, not reachable.</summary>
    /// <remarks>
    /// 没有报告就没有结论。**返回 `Reachable` 会让一个什么都没跑的批次自称全部可达。**
    ///
    /// No report means no conclusion. **Returning `Reachable` would let a batch that ran nothing claim everything is reachable.**
    /// </remarks>
    [Fact]
    public void TheEmptyBatch_ShouldBeUnknown() {
        CriticalityProfileAnalysisResult.WeakestStatus(Array.Empty<AnalysisStatus>())
            .Should().Be(AnalysisStatus.Unknown, "nothing measured is not everything reachable");
    }

    /// <summary>**两个别名指向同一批对象** / Both aliases point at the same objects.</summary>
    /// <remarks>
    /// 别名如果各自复制一份，**调用方改了一边另一边不会变**——而它们名字暗示是同一个列表。
    ///
    /// If each alias copied the list, **a change on one side would not show on the other** — while the names imply one list.
    /// </remarks>
    [Fact]
    public void TheAliases_ShouldPointAtTheSameObjects() {
        var reports = new List<CriticalConstraintAnalysisReport>();
        var profile = new CriticalityProfile(Array.Empty<CriticalityObservation>());
        var plans = new List<AlternativeImprovementPlan>();

        var result =
            CriticalityProfileAnalysisResult.Build(reports, profile, plans);

        ReferenceEquals(result.TargetReports, result.Reports).Should().BeTrue();
        ReferenceEquals(result.Recommendations, result.ImprovementPlans).Should().BeTrue();
        result.Status.Should().Be(AnalysisStatus.Unknown, "an empty batch has no conclusion");
    }
}
