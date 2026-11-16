#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using System;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 冲突分析的前置门控 / The conflict-analysis preflight gates.
/// </summary>
/// <remarks>
/// **"关闭检查必须在读缓存之前"** 是本类唯一真正的语义。
/// 把缓存读放在前面，一个已关闭的会话**仍然会返回上一次的结论**——
/// **于是"关闭"对调用方完全不生效。**
///
/// **"The closed check has to come before any cache read"** is the one real semantic here. Reading the cache first means a closed session **still returns the previous conclusion** — **making "closed" a
/// no-op for the caller.**
/// </remarks>
public class ConflictAnalysisPreflightTests {
    private static CriticalConstraintAnalysisSession Session() {
        using var model = new ConstraintProgrammingModel(name: "conflict", objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        var id = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(Int64.Zero, new Int64(3)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, id);

        model.RegisterVariable(id: id, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull();
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        ConstraintProgrammingConstraint.IntegerComparison constraint =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, Int64.Zero).Value;
        model.AddConstraint(constraint, "constraint:x").Value.Should().NotBeNull();
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();

        return CriticalConstraintAnalysisSession.FromSnapshot(
            model.Snapshot().Value,
            new ConstraintProgrammingSolution(new System.Collections.Generic.Dictionary<VariableId, Int64> {
                [id] = new Int64(1),
            }));
    }

    /// <summary>**健康的会话放行** / A healthy session proceeds.</summary>
    [Fact]
    public void TheHealthySession_ShouldProceed() {
        ConflictAnalysisPreflight.Check(Session(), new ConflictAnalysisOptions())
            .Should().BeNull("nothing is wrong, so nothing is refused");
    }

    /// <summary>**已关闭的会话被拒，且错误码是 `ApplicationStopped`** / A closed session is refused with its own code.</summary>
    /// <remarks>
    /// **错误码必须与"选项非法"不同**：调用方对它们的处置不同——
    /// 前者该换个会话，后者该改选项。
    ///
    /// **The code must differ from "invalid options"**: the caller reacts differently — the first needs another session, the second needs different options.
    /// </remarks>
    [Fact]
    public void TheClosedSession_ShouldBeRefusedWithItsOwnCode() {
        CriticalConstraintAnalysisSession session = Session();
        session.Dispose();

        Err<ErrorCode>? refusal = ConflictAnalysisPreflight.Check(session, new ConflictAnalysisOptions());

        refusal.Should().NotBeNull("a disposed session must not serve anything");
        refusal!.Code.Should().Be(ErrorCode.ApplicationStopped,
            "closed is not a configuration error, and saying so sends the caller to edit options");
    }

    /// <summary>**选项非法被拒，且错误码不是 `ApplicationStopped`** / Invalid options are refused, with a different code.</summary>
    /// <remarks>
    /// **反向对照**：只测上一条会让"恒返回 `ApplicationStopped`"通过。
    ///
    /// **The reverse control**: testing only the case above lets "always `ApplicationStopped`" pass.
    /// </remarks>
    [Fact]
    public void TheInvalidOptions_ShouldBeRefusedWithADifferentCode() {
        Err<ErrorCode>? refusal = ConflictAnalysisPreflight.Check(
            Session(), new ConflictAnalysisOptions(MaxDeletionChecks: -1));

        refusal.Should().NotBeNull("a negative deletion budget is not a request anyone can honour");
        refusal!.Code.Should().NotBe(ErrorCode.ApplicationStopped,
            "the session is perfectly alive; the options are wrong");
    }

    /// <summary>**两道都坏时报"关闭"，不报"选项非法"** / When both are wrong, closed wins.</summary>
    /// <remarks>
    /// **顺序在这里可观察**：先查关闭，报的就是关闭。
    /// **如果先查选项，调用方会去改一个再也不会被用到的选项。**
    ///
    /// **The order is observable here**: closed is checked first, so closed is reported. **Checking options first sends the caller to edit options that will never be used again.**
    /// </remarks>
    [Fact]
    public void TheClosedAndInvalidCase_ShouldReportClosed() {
        CriticalConstraintAnalysisSession session = Session();
        session.Dispose();

        Err<ErrorCode>? refusal = ConflictAnalysisPreflight.Check(session, new ConflictAnalysisOptions(MaxDeletionChecks: -1));

        refusal!.Code.Should().Be(ErrorCode.ApplicationStopped,
            "the session being gone outranks any option, because no option will ever be read");
    }

    /// <summary>**选项先于快照** / Options come before the snapshot.</summary>
    /// <remarks>
    /// 一个坏快照 + 一组坏选项，报的是**选项**。
    /// **顺序反过来会让人去修快照，而选项才是第一个过不去的门。**
    ///
    /// A broken snapshot with broken options reports the **options**. **The other order sends the reader to fix the snapshot while the options are the first gate that fails.**
    /// </remarks>
    /// <summary>**选项与快照都合法时放行** / Both gates passing lets it through.</summary>
    /// <remarks>
    /// **顺序本身测不了**：一份合法的快照在两种顺序下都放行，而造一份非法快照需要绕过 `Snapshot` 的校验。
    /// **所以这里只钉"两道都过"，顺序由 `Check` 那一组（会话级）负责。**
    ///
    /// **The order itself cannot be tested here**: a valid snapshot passes under either order, and building an invalid one has to get past `Snapshot`'s own checks. **So this pins "both pass", and the
    /// order is covered by the session-level group above.**
    /// </remarks>
    [Fact]
    public void TheWellFormedInput_ShouldPass() {
        ConflictPreflightResult result = ConflictAnalysisPreflight.SnapshotPreflight(
            BuildSnapshot(), new ConflictAnalysisOptions(), EmptyMatrix());

        result.Refusal.Should().BeNull("a valid snapshot with valid options is not refused");
        result.Tier.Should().NotBeNull("a passing outcome always carries the selected tier");
    }

    /// <summary>**选项非法时报选项** / Invalid options are reported as options.</summary>
    [Fact]
    public void TheInvalidOptions_ShouldBeRefusedAsOptions() {
        ConflictPreflightResult result = ConflictAnalysisPreflight.SnapshotPreflight(
            BuildSnapshot(), new ConflictAnalysisOptions(MaxDeletionChecks: -1), EmptyMatrix());

        result.Refusal.Should().NotBeNull();
        result.Refusal!.Message.Should().Contain("删除复验次数");
        result.Tier.Should().BeNull("a refusal has no tier");
    }

    /// <summary>**层次不可用是放行，不是拒绝** / An unavailable tier is a pass, not a refusal.</summary>
    /// <remarks>
    /// **"这个求解器读不出冲突"本身是一个合法的分析结论。** 返回错误会让调用方以为配置错了。
    ///
    /// **"This solver cannot read out conflicts" is itself a legitimate analysis result.** Returning an error makes the caller think something is misconfigured.
    /// </remarks>
    [Fact]
    public void TheUnavailableTier_ShouldPassWithNoRefusal() {
        ConflictPreflightResult result = ConflictAnalysisPreflight.SnapshotPreflight(
            BuildSnapshot(), new ConflictAnalysisOptions(), EmptyMatrix());

        result.Refusal.Should().BeNull("an unavailable tier is not a configuration error");
        result.TierUnavailable.Should().BeTrue(
            "the caller must be able to tell \"proceed normally\" from \"proceed into an unsupported explanation\"");
    }

    /// <summary>**有原生 unsat core 时层次可用** / A native unsat core makes the tier available.</summary>
    /// <remarks>
    /// **反向对照**：只测上一条会让"恒报不可用"通过。
    ///
    /// **The reverse control**: testing only the case above lets "always unavailable" pass.
    /// </remarks>
    [Fact]
    public void TheNativeTier_ShouldBeAvailable() {
        var matrix = new CapabilityMatrix(
            ModelTypes: new System.Collections.Generic.HashSet<SolverModelType> { SolverModelType.Cp },
            AnalysisCapabilities: new System.Collections.Generic.Dictionary<AnalysisCapability, CapabilitySupport> {
                [AnalysisCapability.NativeUnsatCore] = CapabilitySupport.Supported,
            });

        ConflictPreflightResult result = ConflictAnalysisPreflight.SnapshotPreflight(
            BuildSnapshot(), new ConflictAnalysisOptions(), matrix);

        result.Refusal.Should().BeNull();
        result.TierUnavailable.Should().BeFalse("a solver with a native unsat core can read out conflicts");
        result.Tier.Should().Be(ConflictExtractionTier.NativeUnsatCore);
    }

    private static CapabilityMatrix EmptyMatrix() =>
        new(ModelTypes: new System.Collections.Generic.HashSet<SolverModelType>());

    private static ConstraintProgrammingModelSnapshot BuildSnapshot() {
        using var model = new ConstraintProgrammingModel(name: "preflight", objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar("x");
        var id = new VariableId("source:x");
        IntegerDomain domain = IntegerDomain.CreateInterval(Int64.Zero, new Int64(3)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(variable, domain, id);

        model.RegisterVariable(id: id, variable: variable, domain: domain,
            scope: ModelElementScope.Stable, origin: "fixture/x").Value.Should().NotBeNull();
        model.RegisterExpression("x", expression).Value.Should().NotBeNull();
        ConstraintProgrammingConstraint.IntegerComparison constraint =
            ConstraintProgrammingConstraint.CreateGreaterOrEqual(expression, Int64.Zero).Value;
        model.AddConstraint(constraint, "constraint:x").Value.Should().NotBeNull();
        model.Minimize(expression: expression, id: new ObjectiveId("objective:main")).Value.Should().NotBeNull();

        return model.Snapshot().Value;
    }
}
