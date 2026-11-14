#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 报告与观测必须逐位配对 / Reports and observations must pair element by element.
/// </summary>
/// <remarks>
/// **分类、阻塞摘要、修复建议全部按位置把报告和观测配在一起。**
/// **错位一位不会抛异常，只会让结论看着依然合理**：
/// **状态来自 A 号目标，阻塞集合来自 B 号目标，而两者都合法。**
///
/// **Classification, the blocking summary, and the repair advice pair reports and observations by position.**
/// **One step of misalignment raises nothing; it only makes the conclusion look reasonable**: **status from target A, blocking set from target B, both valid.**
/// </remarks>
public class CriticalityResultPairingTests {
    private static ObjectiveTarget Target(string name) =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId(name), new Flt64(10.0));

    private static CriticalityObservation Observation(ObjectiveTarget target, AnalysisStatus status) =>
        new(target, status);

    private static CriticalConstraintAnalysisReport Report(ObjectiveTarget target, AnalysisStatus status) =>
        new(
            Baseline: new AnalysisBaselineSummary(
                ObjectiveId: "objective:main",
                Sense: "Minimize",
                ObjectiveValue: null,
                Status: status,
                ProvenOptimal: false),
            Activity: new ConstraintActivityReport(
                Activities: Array.Empty<ConstraintActivity>(),
                GroupSummaries: Array.Empty<ActivityGroupSummary>(),
                Tolerance: 1e-9,
                NearlyActiveTolerance: 1e-6),
            LocalSensitivity: null,
            Funnel: new CandidateFunnelRanking(0.0, Array.Empty<ConstraintCandidate>(), 0, 0, 0, 0),
            Effectiveness: null,
            Target: new TargetFeasibilityReport(target, Status: status),
            Conflict: null,
            GroupSummaries: Array.Empty<AnalysisGroupSummary>(),
            Status: status,
            UnavailableReasons: Array.Empty<string>());

    private static CriticalityProfileAnalysisResult Result(
        IReadOnlyList<CriticalConstraintAnalysisReport> reports,
        IReadOnlyList<CriticalityObservation> observations) =>
        new(reports, new CriticalityProfile(observations), Array.Empty<AlternativeImprovementPlan>(),
            AnalysisStatus.Unreachable);

    /// <summary>**配对正确时通过** / A correct pairing validates.</summary>
    [Fact]
    public void TheMatchingPairing_ShouldValidate() {
        ObjectiveTarget target = Target("objective:a");

        Result<Success, ErrorCode, Error<ErrorCode>> validation = Result(new[] { Report(target, AnalysisStatus.Unreachable) },
            new[] { Observation(target, AnalysisStatus.Unreachable) }).Validate();

        // **查的是"通过时到底返回了什么"**，不是"没失败"——**后者只说明断言跑到了这里。**
        // **The assertion is on what success carries**, not on the absence of failure — **the latter only proves the line was reached.**
        validation.IsOk.Should().BeTrue("the two lists describe the same analysis");
        validation.Value.Should().BeOfType<Success>("a passing validation yields the success instance");
        // **这一条不用空泛的 `IsOk`：** 它要证明的是"配对通过"这件事本身，
        // **而 `IsOk` 只能说明没失败。** 上面那句 `because` 已经把意思说全了，
        // **所以这里不再另加一条只查 `IsOk` 的断言。**
        // **This one deliberately avoids a bare `IsOk`**: the point is that the pairing passes, **and `IsOk` only says nothing failed.**
    }

    /// <summary>**条数不等时拒绝** / A length mismatch is refused.</summary>
    [Fact]
    public void TheLengthMismatch_ShouldBeRefused() {
        ObjectiveTarget target = Target("objective:a");

        Result(new[] { Report(target, AnalysisStatus.Unreachable), Report(target, AnalysisStatus.Unreachable) },
                new[] { Observation(target, AnalysisStatus.Unreachable) })
            .Validate()
            .IsOk.Should().BeFalse("two reports cannot share one observation");
    }

    /// <summary>**错位一位必须被抓住** / A one-position misalignment has to be caught.</summary>
    /// <remarks>
    /// **这是这个类型唯一能错得悄无声息的方式**：两条观测、两条报告、条数正确、
    /// **每条目标和状态单独看都合法——只是配错了。**
    ///
    /// **This is the one way the type can be silently wrong**: two observations, two reports, right count, **every target and status valid in isolation — just paired wrong.**
    /// </remarks>
    [Fact]
    public void TheOneStepMisalignment_ShouldBeCaught() {
        ObjectiveTarget first = Target("objective:a");
        ObjectiveTarget second = Target("objective:b");

        // **报告是 a、b；观测是 b、a。** 条数对，各自合法，配对错。
        // **Reports are a, b; observations are b, a.** Right count, each valid, wrong pairing.
        Result(
                new[] { Report(first, AnalysisStatus.Unreachable), Report(second, AnalysisStatus.Unreachable) },
                new[] { Observation(second, AnalysisStatus.Unreachable), Observation(first, AnalysisStatus.Unreachable) })
            .Validate()
            .IsOk.Should().BeFalse("a's status must not be read as b's");
    }

    /// <summary>**目标对而状态不同也必须拒绝** / The right target with a different status is still wrong.</summary>
    /// <remarks>
    /// **反向对照**：只查目标会让"同名目标的两次不同运行"通过——
    /// **而那正是最容易被放过去的一种错位：目标能对上，所以看着像对上了。**
    ///
    /// **The reverse control**: checking only the target lets "two different runs of the same-named target" pass — **and that is the misalignment most easily waved through: the target matches, so it looks matched.**
    /// </remarks>
    [Fact]
    public void TheStatusMismatch_ShouldBeRefused() {
        ObjectiveTarget target = Target("objective:a");

        Result(new[] { Report(target, AnalysisStatus.Unreachable) },
                new[] { Observation(target, AnalysisStatus.Reachable) })
            .Validate()
            .IsOk.Should().BeFalse("the same target solved twice gives two answers, and they are not interchangeable");
    }

    /// <summary>**空 schema 版本被拒绝** / A blank schema version is refused.</summary>
    [Fact]
    public void TheBlankSchemaVersion_ShouldBeRefused() {
        ObjectiveTarget target = Target("objective:a");

        new CriticalityProfileAnalysisResult(
                new[] { Report(target, AnalysisStatus.Unreachable) },
                new CriticalityProfile(new[] { Observation(target, AnalysisStatus.Unreachable) }),
                Array.Empty<AlternativeImprovementPlan>(),
                AnalysisStatus.Unreachable,
                SchemaVersion: "  ")
            .Validate()
            .IsOk.Should().BeFalse("a result with no schema version cannot be published");
    }
}
