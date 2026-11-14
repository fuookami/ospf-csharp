#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 最小阻塞集合 / A minimal blocking set.
/// </summary>
/// <remarks>
/// **`Verified` 只认 `Irreducible`。** 把 `NotChecked` 也当成"已验证"会让**没验过的东西看起来验过了**；
/// 把 `Partial` 当成"已验证"更糟——**只验了一部分不等于验完。**
///
/// **`Verified` accepts only `Irreducible`.** Treating `NotChecked` as verified makes **unchecked look checked**; treating `Partial` as verified is worse — **checking part of it is not checking all of it.**
/// </remarks>
public class MinimalBlockingSetTests {
    private static readonly ObjectiveTarget Target = ObjectiveTarget.CreateAtLeast(
        new Fuookami.Ospf.Core.Solver.Report.ObjectiveId("obj"),
        new Fuookami.Ospf.Math.Algebra.Number.Flt64(1.0));

    private static ConflictExplanation Explanation(
        ConflictMinimality minimality,
        AnalysisStatus status = AnalysisStatus.Reachable,
        ConflictValidity validity = ConflictValidity.Verified,
        IReadOnlyList<ConflictVerification>? verifications = null) =>
        new(
            Target: Target,
            TargetFeasibility: new TargetFeasibilityReport(Target),
            ExtractionTier: ConflictExtractionTier.Unavailable,
            Status: status,
            AvailableEvidence: Array.Empty<DiagnosticSource>(),
            Members: Array.Empty<DiagnosticSource>(),
            TargetSource: null,
            Validity: validity,
            Minimality: minimality,
            Verifications: verifications);

    /// <summary>**只有 `Irreducible` 算已验证** / Only `Irreducible` counts as verified.</summary>
    /// <remarks>
    /// **三态都要测**：只测 `Irreducible` 为真，会让"恒返回 `true`"的实现通过。
    ///
    /// **All three states**: testing only that `Irreducible` is true lets a "always true" implementation pass.
    /// </remarks>
    [Fact]
    public void TheVerifiedFlag_ShouldAcceptOnlyIrreducible() {
        MinimalBlockingSet.From(Explanation(ConflictMinimality.Irreducible))
            .Verified.Should().BeTrue();

        MinimalBlockingSet.From(Explanation(ConflictMinimality.NotChecked))
            .Verified.Should().BeFalse("never checked is not verified");

        MinimalBlockingSet.From(Explanation(ConflictMinimality.Partial))
            .Verified.Should().BeFalse("a partial irreducibility check is not an irreducibility proof");
    }

    /// <summary>**投影不升格最小性** / The projection never promotes minimality.</summary>
    /// <remarks>
    /// 一个把 `NotChecked` 升格成 `Irreducible` 的投影会让**每一份报告都自称已验证**。
    ///
    /// A projection that promotes `NotChecked` to `Irreducible` would make **every report claim to be verified.**
    /// </remarks>
    [Fact]
    public void TheProjection_ShouldNotPromoteMinimality() {
        MinimalBlockingSet.From(Explanation(ConflictMinimality.NotChecked))
            .Minimality.Should().Be(ConflictMinimality.NotChecked);

        MinimalBlockingSet.From(Explanation(ConflictMinimality.Partial))
            .Minimality.Should().Be(ConflictMinimality.Partial);
    }

    /// <summary>**状态与有效性也原样带出** / Status and validity come through unchanged.</summary>
    /// <remarks>
    /// **"提取到"与"只提取了一部分"是两种结论。** 把它们统一成"成功"会让部分提取冒充完整提取。
    ///
    /// **"extracted" and "only partly extracted" are two different conclusions.** Merging them into "succeeded" lets a partial extraction impersonate a complete one.
    /// </remarks>
    [Fact]
    public void TheStatusAndValidity_ShouldComeThroughUnchanged() {
        var partial = MinimalBlockingSet.From(
            Explanation(ConflictMinimality.Irreducible, AnalysisStatus.Unsupported, ConflictValidity.Unknown));

        partial.Status.Should().Be(AnalysisStatus.Unsupported, "an unsupported extraction is not a complete one");
        partial.Validity.Should().Be(ConflictValidity.Unknown, "an unknown premise stays unknown");
        partial.Verified.Should().BeTrue("minimality is independent of how much was extracted");
    }

    /// <summary>**缺省证据变成空表，不是 `null`** / Absent evidence becomes an empty list, not `null`.</summary>
    /// <remarks>
    /// 留 `null` 会让每一个消费者都要写一次空判断——**而其中总会有人写成"没有证据就算验证通过"。**
    ///
    /// Leaving `null` makes every consumer write a null check — **and someone will write "no evidence means verified".**
    /// </remarks>
    [Fact]
    public void TheAbsentVerifications_ShouldBecomeEmpty() {
        var set = MinimalBlockingSet.From(Explanation(ConflictMinimality.Irreducible));

        set.Verifications.Should().NotBeNull("an empty list is not a missing one");
        set.Verifications.Should().BeEmpty();
    }
}
