#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **终态核对：基线是固定的、数字是实测的、结论不被删掉** /
/// Terminal check: the baseline is fixed, the numbers are measured, the conclusion is not deleted.
/// </summary>
/// <remarks>
/// **B1295 把整条链的三个锚点钉在一起：**
///
/// 1. **扫描脚本的默认提交** 必须是固定基线 `12fdb587` —— **否则"对基线的全量同步"这句话就没有主语**；
/// 2. **状态文档里的门禁数字** 必须是真实跑出来的（这一条已由 `GateNumbersTests` 覆盖，这里只钉它确实在场）；
/// 3. **计划里的终态核对一节** 不许被删 —— **它是"还剩什么"的唯一入口**。
///
/// ### 为什么第 1 条最要紧
///
/// **"全量同步"永远是对某个基线的全量同步。** **如果脚本的默认提交漂了（`HEAD`、`master`、或者某个新 tag），
/// 那么每次门禁都在拿一个<b>移动的</b>基线去比，而所有历史结论会悄悄变成"对某个当时的提交成立"。**
/// **这条判据把默认值钉在常量上：换基线必须改这一行，而改它就会红。**
///
/// **B1295 pins the chain's three anchors: the scanner defaults to the fixed baseline `12fdb587` (otherwise "full sync" has no
/// subject), the status document states the measured gate numbers (already covered by `GateNumbersTests`, pinned here for presence),
/// and the plan's terminal section may not be deleted (it is the only entry point to "what is left").** **The first matters most:
/// a full sync is always a full sync against <b>some</b> baseline, and a default that drifts to `HEAD` or a moving tag makes every
/// gate compare against a moving target while every historical conclusion quietly becomes "true for whatever commit it was then".**
/// </remarks>
public class TerminalStateTests
{
    /// <summary>**扫描脚本默认对 `966cafbc`** / The scanner defaults to `966cafbc`.</summary>
    /// <remarks>
    /// B1314：扫描目标从 `12fdb587` 迁到 `966cafbc`（最新状态）。**这里改的只是"拿哪个提交当对比对象"，**
    /// 而夹具出处里那些 `12fdb587` 是历史事实，另由 `TheFixtureProvenance_ShouldKeepTheOldBaseline` 钉住不动。
    ///
    /// B1314: the scan target moves from `12fdb587` to `966cafbc`. **Only "which commit is compared against" changes here**, while the
    /// `12fdb587` references in fixture provenance are historical facts, pinned separately by the criterion named above.
    /// </remarks>
    [Fact]
    public void TheScanner_ShouldStillDefaultToTheFixedBaseline() {
        string root = RepositoryRoot();
        string scanner = File.ReadAllText(System.IO.Path.Combine(root, "scripts", "scan-kotlin-baseline.py"));

        Regex.IsMatch(scanner, @"--commit""\s*,\s*default=""966cafbc""").Should().BeTrue(
            "the sync is measured against 966cafbc; a floating default would make every gate compare against a moving target and every past "
            + "conclusion quietly conditional");
    }

    /// <summary>
    /// **夹具出处仍然写 `12fdb587`：历史不能被"迁移"顺手改掉** /
    /// Fixture provenance still says `12fdb587`: history must not be rewritten by a migration.
    /// </summary>
    /// <remarks>
    /// **B1314 迁的是"拿哪个提交当对比对象"，不是"这些夹具来自哪个提交"。** **两者在仓里长得一模一样（都是 40 位提交号），
    /// 而语料、远端线协议、各演示的对齐事实都产自 `12fdb587`——**把它们一起改掉等于伪造出处。** **这条判据钉住这个区分：
    /// 扫描目标可以往前走，出处不跟着动。**
    ///
    /// **What B1314 migrates is "which commit to compare against", not "which commit these fixtures came from".** **The two look identical in the repository
    /// (both are 40-hex commit ids) while the corpora, remote wire contracts and demo alignment facts were all produced at `12fdb587` — rewriting them would
    /// forge provenance.** **This criterion pins the distinction: the scan target moves forward, provenance does not follow it.**
    /// </remarks>
    [Fact]
    public void TheFixtureProvenance_ShouldKeepTheOldBaseline() {
        string root = RepositoryRoot();
        string provenance = File.ReadAllText(System.IO.Path.Combine(
            root, "tests", "Framework.Tests", "RemoteFixtures", "README.md"));

        provenance.Should().Contain(
            "12fdb587",
            "the remote fixtures were captured at 12fdb587; a provenance note that follows the scan target would be describing a corpus it never had");
    }

    /// <summary>**计划里的终态核对一节必须还在，且带着实测数字** / The terminal section must still exist with its measured numbers.</summary>
    [Fact]
    public void TheTerminalSection_ShouldStillExist() {
        string root = RepositoryRoot();
        string plan = File.ReadAllText(System.IO.Path.Combine(root, "logs", "kotlin-sync-plan.md"));

        int section = plan.IndexOf("#### ⑤ 终态核对", StringComparison.Ordinal);
        section.Should().BeGreaterThanOrEqualTo(
            0,
            "the terminal section is the only place that answers \"what is left\"; deleting it loses that answer");

        string slice = plan.Substring(section, System.Math.Min(2000, plan.Length - section));
        slice.Should().Contain("966cafbc", "the terminal state names the baseline it is terminal against");
        slice.Should().Contain("0 unexplained", "the scanner's four dimensions each resolve");
        slice.Should().Contain("1316", "the warning count is the measured one");
        slice.Should().Contain(
            "Mosek",
            "what remains must be named, not implied — the external blockers are the reason the state is terminal");
    }

    /// <summary>**状态文档仍写着实测数字** / The status document still states measured numbers.</summary>
    [Fact]
    public void TheStatusDoc_ShouldStillStateMeasuredNumbers() {
        string root = RepositoryRoot();
        string status = File.ReadAllText(System.IO.Path.Combine(root, "docs", "kotlin-sync-status.md"));

        Regex.Match(status, @"`(\d+) passed`").Success.Should().BeTrue(
            "the status document states a measured passed count; a document without one is a claim without evidence");
        status.Should().Contain("1316", "the warning count is the repository's baseline and is stated where the numbers live");
    }

    /// <summary>**终态一节的数字必须与状态文档一致** / The terminal numbers must agree with the status document.</summary>
    /// <remarks>
    /// **B1295 就犯过这个：** **终态一节里写的是上一批的 `11464`。**
    /// **与状态文档对不上时，两处数字就有一个是旧的——而读者不知道哪个。**
    /// </remarks>
    [Fact]
    public void TheTerminalNumbers_ShouldMatchTheStatusDoc() {
        string root = RepositoryRoot();
        string plan = File.ReadAllText(System.IO.Path.Combine(root, "logs", "kotlin-sync-plan.md"));
        string status = File.ReadAllText(System.IO.Path.Combine(root, "docs", "kotlin-sync-status.md"));

        int section = plan.IndexOf("#### ⑤ 终态核对", StringComparison.Ordinal);
        section.Should().BeGreaterThanOrEqualTo(0);
        string slice = plan.Substring(section, System.Math.Min(2000, plan.Length - section));

        Match stated = Regex.Match(slice, @"集成 \| \*\*`(\d+) passed`\*\* \|");
        stated.Success.Should().BeTrue("the terminal section states the integration count");
        status.Should().Contain(
            $"`{stated.Groups[1].Value} passed`",
            "the terminal section and the status document must state the same measured count; when they differ, one is stale and the reader cannot tell which");
    }

    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}