#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 目标可行性报告 / The target feasibility report.
/// </summary>
/// <remarks>
/// 只搬报告，不搬分析器**：基线第 673 行有一处真正的 `solver.solve(model, options)`，
/// 之前还有重建可变模型的 builder。**那部分需要真实 CP 后端，没搬也没空跑。**
///
/// 判据钉的是**四个状态不许互相冒充**：`Reachable` / `Unreachable` 是结论，
/// `Unknown` / `Unsupported` 是"没结论"的两种原因。
///
/// **The report and not the analyzer is ported**: baseline line 673 has a real `solver.solve(model, options)` and a builder that rebuilds a mutable model before it.
/// **That needs a real CP backend, so it was neither ported nor stubbed.** The criteria pin that **the four statuses may not impersonate one another**.
/// </remarks>
public class TargetFeasibilityReportTests {
    private static ObjectiveTarget Target() =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:main"), new Flt64(10.0));

    /// <summary>**只有两个状态给出布尔值** / Only two statuses produce a boolean.</summary>
    /// <remarks>
    /// 让 `Unknown` / `Unsupported` 各自返回 `false`，等于**把"没结论"报成"不可达"**。
    /// 而 `Unsupported` 更糟：它意味着**换个后端就能解**，不是目标不可达成。
    ///
    /// Returning `false` for `Unknown` / `Unsupported` would **report "no conclusion" as "unreachable"**. `Unsupported` is worse still: it means **another backend could solve
    /// this**, not that the target cannot be met.
    /// </remarks>
    [Fact]
    public void TheReachable_ShouldBeNullWhenThereIsNoConclusion() {
        new TargetFeasibilityReport(Target(), Status: AnalysisStatus.Reachable)
            .Reachable.Should().BeTrue();
        new TargetFeasibilityReport(Target(), Status: AnalysisStatus.Unreachable)
            .Reachable.Should().BeFalse();

        new TargetFeasibilityReport(Target(), Status: AnalysisStatus.Unknown)
            .Reachable.Should().BeNull("unknown is not the same as unreachable");
        new TargetFeasibilityReport(Target(), Status: AnalysisStatus.Unsupported)
            .Reachable.Should().BeNull(
                "unsupported means another backend could try, which is the opposite of 'cannot be met'");
    }

    /// <summary>**`Reachable` 不等于已证明** / `Reachable` is not the same as proven.</summary>
    /// <remarks>
    /// 求解器在时间上限内找到一个解，只说明目标**可能**可达。证明与否由 `ProofStatus` 说。
    ///
    /// A solver that finds a solution within a time limit only shows the target **may** be reachable. Whether it is proven is said by `ProofStatus`.
    /// </remarks>
    [Fact]
    public void TheProven_ShouldNeedBothStatusAndProof() {
        var foundButUnproven = new TargetFeasibilityReport(
            Target(), Status: AnalysisStatus.Reachable, ProofStatus: ProofStatus.None);
        foundButUnproven.IsProven.Should().BeFalse(
            "finding a witness under a time limit does not prove the target reachable");

        var proven = new TargetFeasibilityReport(
            Target(), Status: AnalysisStatus.Reachable, ProofStatus: ProofStatus.Verified);
        proven.IsProven.Should().BeTrue();

        // **`Unknown` 即使带着证明状态也不算已证明**：结论本身没有。
        // **`Unknown` is not proven even with a proof status**: there is no conclusion to prove.
        var unknownWithProof = new TargetFeasibilityReport(
            Target(), Status: AnalysisStatus.Unknown, ProofStatus: ProofStatus.Verified);
        unknownWithProof.IsProven.Should().BeFalse(
            "a proof status on a status-less record claims something that is not there");
    }

    /// <summary>**缺省来源从目标条件派生，且不含数值位置** / The default source derives from the target and carries no numeric position.</summary>
    [Fact]
    public void TheSource_ShouldDefaultToTheTarget() {
        var report = new TargetFeasibilityReport(Target());
        report.EvidenceSource.Should().BeOfType<DiagnosticSource.ObjectiveTargetSource>(
            "an objective target maps to the objective-target source kind");
        // **目标条件的稳定身份确实带数值**——因为目标本身就是"某个值"，那是它的定义，不是行号。
        // **An objective target's stable identity does carry a value**, because the target *is* a value; that is its definition, not a row number.
        report.EvidenceSource.StableId.Should().Contain("objective:main",
            "the identity names the objective it belongs to");
        report.EvidenceSource.Kind.Should().Be("objective-target");

        var explicitSource = new DiagnosticSource.Constraint(new ConstraintId("c1"));
        new TargetFeasibilityReport(Target(), Source: explicitSource)
            .EvidenceSource.Should().BeSameAs(explicitSource,
                "an explicit source wins, or a caller could never override it");
    }

    /// <summary>**目标未插入派生模型时必须说清楚** / A target that was not inserted has to say so.</summary>
    /// <remarks>
    /// `TargetFixed` 默认 `true`。**如果实现者在"没插进去"时忘了置 `false`，一份没有目标约束的求解结果
    /// 会被读成"这个目标可达"。**
    ///
    /// `TargetFixed` defaults to `true`. **If an implementation forgets to set it `false` when the target was not inserted, a solve that never contained the target constraint
    /// gets read as "the target is reachable".**
    /// </remarks>
    [Fact]
    public void TheTargetFixed_ShouldBeReported() {
        new TargetFeasibilityReport(Target()).TargetFixed.Should().BeTrue(
            "the default assumes the target was inserted");
        new TargetFeasibilityReport(Target(), TargetFixed: false).TargetFixed.Should().BeFalse(
            "and the caller can say it was not");

        // **精确整数与浮点目标值并存**：整数目标条件不该在报告里变成浮点。
        // **The exact integer and the float coexist**: an integer target should not become a float in the report.
        var report = new TargetFeasibilityReport(
            Target(),
            EffectiveIntegerBound: new Fuookami.Ospf.Math.Algebra.Number.Int64(9007199254740993L),
            ExactObjective: new Fuookami.Ospf.Math.Algebra.Number.Int64(9007199254740993L),
            ObjectiveValue: new Flt64(9007199254740992.0));
        report.EffectiveIntegerBound!.Value.ToLong().Should().Be(9007199254740993L,
            "the exact integer bound survives, or a target silently moves by one");
        report.ExactObjective!.Value.ToLong().Should().Be(9007199254740993L);
        // **浮点确实表示不了它，所以落在 9007199254740992**——这正是精确字段存在的理由。
        // **The float genuinely cannot represent it and lands on 9007199254740992** — which is exactly why the exact field exists.
        report.ObjectiveValue!.Value.ToDouble().Should().Be(9007199254740992.0,
            "the float rounds down by one, and the exact field is the only place the true bound survives");
        // **不能拿 `(double)long` 比**：那个转换本身就会舍入到同一个值，比出来永远是相等——
        // 一条永远成立的断言什么也没证明。改用字符串比较精确字段。
        // **Do not compare against `(double)long`**: that cast rounds to the same value, so the comparison is always equal — an assertion that always holds proves nothing.
        report.EffectiveIntegerBound!.Value.ToLong().ToString().Should().Be("9007199254740993",
            "the exact field is the only place the true bound survives");
    }

    /// <summary>**四个状态各自独立，没有两个相同** / The four statuses are distinct.</summary>
    [Fact]
    public void TheStatuses_ShouldStayDistinct() {
        AnalysisStatus[] statuses = [
            AnalysisStatus.Reachable,
            AnalysisStatus.Unreachable,
            AnalysisStatus.Unknown,
            AnalysisStatus.Unsupported,
        ];

        statuses.Should().OnlyHaveUniqueItems("merging any two would collapse two different answers");
        statuses.Should().HaveCount(4);
        statuses.Count(status => status.IsProven()).Should().Be(2,
            "reachable and unreachable are conclusions; unknown and unsupported are not");
    }
}
