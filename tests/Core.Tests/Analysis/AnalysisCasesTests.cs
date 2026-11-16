#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 跨语言纯函数用例 / The cross-language pure-function cases.
/// </summary>
/// <remarks>
/// `Fixtures/analysis-cases.tsv` 是从固定基线 `12fdb587` **逐字复制**来的，
/// **Kotlin 侧断言同一份文件。** 这些函数不依赖 backend，**所以契约要求两端逐字一致。**
///
/// **每一节要么有判据，要么被 `TheUncoveredSections_ShouldBeNamed` 点名。**
/// **"没断言"与"断言过"必须看得出区别**——**否则一张越来越短的用例表和一张全绿的表无法区分。**
///
/// `Fixtures/analysis-cases.tsv` is **copied verbatim** from the pinned baseline, **and the Kotlin side asserts the same file.** These functions need no backend, **so the contract requires both sides to agree
/// exactly.** **Every section either has a criterion or is named by `TheUncoveredSections_ShouldBeNamed`.** **"Not asserted" has to look different from "asserted"** — **otherwise a shrinking case table is
/// indistinguishable from a fully green one.**
/// </remarks>
public class AnalysisCasesTests
{
    /// <summary>尚未断言的节，连同原因 / Sections not yet asserted, with the reason.</summary>
    /// <remarks>
    /// **这份名单是判据的一部分，不是注释。** 每搬一节就从这里删一项，
    /// **而 `TheUncoveredSections_ShouldBeNamed` 会因为名单和文件对不上而红。**
    ///
    /// **This list is part of the criterion, not a comment.** Porting a section removes one entry, **and the coverage criterion goes red when the list and the file disagree.**
    /// </remarks>
    private static readonly string[] NotYetAsserted = [];

    private static string[] CaseLines()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }

        string path = Path.Combine(
            directory!.FullName, "tests", "Core.Tests", "Analysis", "Fixtures", "analysis-cases.tsv");

        return File.ReadAllLines(path)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal) && line.Length > 0)
            .ToArray();
    }

    /// <summary>一格数据行 / The data rows of one section, split into cells.</summary>
    private static string[][] Section(string name)
    {
        string[] lines = CaseLines();
        int start = Array.FindIndex(lines, line => line == "[" + name + "]");
        start.Should().BeGreaterThanOrEqualTo(0, "the case table has to declare [" + name + "]");

        int end = start + 1;
        while (end < lines.Length && !lines[end].StartsWith("[", StringComparison.Ordinal))
        {
            end++;
        }

        return lines[(start + 1)..end].Select(line => line.Split('\t')).ToArray();
    }

    /// <summary>用例表宣告的全部节 / Every section the case table declares.</summary>
    private static string[] Sections() => CaseLines()
        .Where(line => line.StartsWith("[", StringComparison.Ordinal))
        .Select(line => line.Trim('[', ']'))
        .ToArray();

    /// <summary>**problem status 到分析状态的映射** / Problem status to analysis status.</summary>
    /// <remarks>
    /// **`Feasible` 但没有证明，只能算 `Unknown` 而不是 `Reachable`。**
    /// **一个没证明的可行解不是结论**——把它当结论会让"找到了解"和"证明了最优"看起来一样。
    ///
    /// **`Feasible` without a proof is `Unknown`, not `Reachable`.** **An unproven feasible point is not a conclusion** — treating it as one makes "found a solution" and "proved optimality" look alike.
    /// </remarks>
    [Fact]
    public void TheStatusMap_ShouldMatchEveryCase()
    {
        foreach (string[] cells in Section("status-map"))
        {
            cells.Should().HaveCount(3, "status-map rows are status, proven, expected");

            ProblemStatus status = Enum.Parse<ProblemStatus>(cells[0]);
            bool proven = bool.Parse(cells[1]);
            AnalysisStatus expected = Enum.Parse<AnalysisStatus>(cells[2]);

            status.ToAnalysisStatus(proven).Should().Be(expected,
                $"{cells[0]} with proven={cells[1]} maps to {cells[2]}");
        }
    }

    /// <summary>**候选分层** / Candidate tiering.</summary>
    /// <remarks>
    /// **`Inactive` 与 `SatisfiedWithoutSlackMetric` 直接落 Tier C，不看对偶**——
    /// **它们的对偶即使存在也不说明任何事，所以拿对偶去分档会分出一个假的 Tier A。**
    ///
    /// **`Inactive` and `SatisfiedWithoutSlackMetric` fall straight to Tier C without consulting the dual** — **their dual, even when present, says nothing, so bucketing by it would invent a Tier A that is not there.**
    /// </remarks>
    [Fact]
    public void TheClassifyTable_ShouldMatchEveryCase()
    {
        foreach (string[] cells in Section("classify"))
        {
            cells.Should().HaveCount(5, "classify rows are activity, has-dual, dual, threshold, expected");

            ActivityStatus activity = Enum.Parse<ActivityStatus>(cells[0]);
            bool hasDual = bool.Parse(cells[1]);
            double dual = double.Parse(cells[2], System.Globalization.CultureInfo.InvariantCulture);
            double threshold = double.Parse(cells[3], System.Globalization.CultureInfo.InvariantCulture);
            CandidateTier expected = Enum.Parse<CandidateTier>(cells[4]);

            CriticalCandidateFunnel.ClassifyCandidate(
                    activity,
                    hasDual ? new Flt64(dual) : null,
                    threshold)
                .Should().Be(expected,
                    $"{cells[0]} with dual={cells[1]}/{cells[2]} at {cells[3]} classifies as {cells[4]}");
        }
    }

    /// <summary>**状态合并** / Status combination.</summary>
    /// <remarks>
    /// **`Unsupported` 会吞掉一切**：一个做不到的步骤让整条链做不到，
    /// **不管别的步骤成没成**——**所以它不是"最坏的那个"，而是"有否决权的那个"。**
    ///
    /// **`Unsupported` swallows everything**: one impossible step makes the chain impossible, **whatever the others did** — **so it is not "the worst one", it is "the one with a veto".**
    /// </remarks>
    [Fact]
    public void TheCombineTable_ShouldMatchEveryCase()
    {
        foreach (string[] cells in Section("combine"))
        {
            cells.Should().HaveCount(3, "combine rows are current, next, expected");

            AnalysisStatus current = Enum.Parse<AnalysisStatus>(cells[0]);
            AnalysisStatus next = Enum.Parse<AnalysisStatus>(cells[1]);
            AnalysisStatus expected = Enum.Parse<AnalysisStatus>(cells[2]);

            CriticalConstraintAnalysisReport.CombineStatus(current, next).Should().Be(expected,
                $"{cells[0]} combined with {cells[1]} is {cells[2]}");
        }
    }

    /// <summary>**短名单的补位规则** / The shortlist backfill rule.</summary>
    /// <remarks>
    /// **Tier A/B 先占名额；名额没满时 Tier C 与 Unclassified 按候选顺序补位，
    /// 而且它们只会"这次没拿到名额"，不会从候选集合里消失。**
    ///
    /// **这条最容易被写成"只返回 preferred"**，那样 Tier C 的约束在短名单里永不出现——
    /// **而"没被分析"和"分析过没问题"在报告里长得一样。**
    ///
    /// **Tier A/B take the slots first; while slots remain, Tier C and Unclassified backfill in candidate order,
    /// and they only miss this round's slots, never the candidate set itself.**
    /// **This is the rule most easily implemented as "return preferred only"**, which drops every Tier C constraint from analysis forever —
    /// **and "not analysed" looks exactly like "analysed and fine" in a report.**
    /// </remarks>
    [Fact]
    public void TheShortlistTable_ShouldMatchEveryCase()
    {
        foreach (string[] cells in Section("shortlist"))
        {
            cells.Should().HaveCount(8, "shortlist rows carry tier counts, the limit, and the returned counts");

            int tierA = int.Parse(cells[0]);
            int tierB = int.Parse(cells[1]);
            int tierC = int.Parse(cells[2]);
            int limit = int.Parse(cells[3]);
            int returned = int.Parse(cells[4]);
            int tierCReturned = int.Parse(cells[5]);
            int unclassified = int.Parse(cells[6]);
            int unclassifiedReturned = int.Parse(cells[7]);

            CandidateFunnelRanking funnel = Funnel(tierA, tierB, tierC, unclassified);
            ConstraintCandidate[] shortlist = funnel.Shortlist(limit).ToArray();

            string row = string.Join(" ", cells);
            shortlist.Should().HaveCount(returned, $"[{row}] returns {returned}");
            shortlist.Count(candidate => candidate.Tier == CandidateTier.TierC)
                .Should().Be(tierCReturned, $"[{row}] keeps {tierCReturned} Tier C candidates");
            shortlist.Count(candidate => candidate.Tier == CandidateTier.Unclassified)
                .Should().Be(unclassifiedReturned, $"[{row}] keeps {unclassifiedReturned} unclassified candidates");
        }
    }

    /// <summary>按层级计数造一个漏斗 / Build a funnel from per-tier counts.</summary>
    /// <remarks>
    /// **顺序按 A、B、C、Unclassified 排**，因为补位是按候选顺序取的——
    /// **换个顺序，同样四条候选会返回不同的四条。**
    ///
    /// **Ordered A, B, C, Unclassified**, because backfill takes candidates in order —
    /// **reorder and the same four candidates return a different four.**
    /// </remarks>
    private static CandidateFunnelRanking Funnel(int tierA, int tierB, int tierC, int unclassified)
    {
        var candidates = new List<ConstraintCandidate>();
        void Add(int count, CandidateTier tier)
        {
            for (int index = 0; index < count; index++)
            {
                candidates.Add(new ConstraintCandidate(
                    new ConstraintId($"constraint:{tier}:{index}"),
                    null,
                    tier,
                    ActivityStatus.Active,
                    null,
                    null,
                    null,
                    candidates.Count));
            }
        }

        Add(tierA, CandidateTier.TierA);
        Add(tierB, CandidateTier.TierB);
        Add(tierC, CandidateTier.TierC);
        Add(unclassified, CandidateTier.Unclassified);

        return new CandidateFunnelRanking(
            0.0, candidates, tierA, tierB, tierC, unclassified);
    }

    /// <summary>**三层重要性的判定** / The three criticality levels.</summary>
    /// <remarks>
    /// **只有 `Unreachable` 算已证明目标，而只有"已证明且已验证"的才携带阻塞证据。**
    /// **`proven_target_count` 与 `verified` 无关**——它是一个分母，不是一个过滤器。
    ///
    /// **`Structural` 还要求 `proven > 1`**：一个目标上的阻塞只是局部观测，
    /// **把它报成"结构性"等于用一次实验宣称一个规律。**
    ///
    /// **Only `Unreachable` counts as a proven target, and only "proven and verified" carries blocking evidence.**
    /// **`proven_target_count` ignores `verified`** — it is a denominator, not a filter.
    /// **`Structural` additionally requires `proven > 1`**: blocking on one target is a local observation,
    /// **and calling it structural claims a law from a single trial.**
    /// </remarks>
    [Fact]
    public void TheCriticalityTable_ShouldMatchEveryCase()
    {
        foreach (string[] cells in Section("criticality"))
        {
            cells.Should().HaveCount(6, "criticality rows carry observations, three id lists, and the proven count");

            var observations = new List<CriticalityObservation>();
            foreach (string encoded in cells[1].Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = encoded.Split('|');
                AnalysisStatus status = Enum.Parse<AnalysisStatus>(parts[0]);
                bool verified = bool.Parse(parts[1]);
                var blocking = new HashSet<ConstraintId>();
                // **未验证的观测即使写了 id 也不携带证据。**
                // **An unverified observation carries no evidence even when ids are listed.**
                if (verified && parts[2] != "-")
                {
                    foreach (string id in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        blocking.Add(new ConstraintId("constraint:" + id));
                    }
                }

                observations.Add(new CriticalityObservation(
                    ObjectiveTarget.CreateAtLeast(new ObjectiveId("objective:" + cells[0]), new Flt64(10.0)),
                    status,
                    blocking));
            }

            CriticalityProfile profile = new(observations);
            string row = string.Join(" ", cells);

            Names(profile, CriticalityKind.LocalBottleneck).Should().Equal(Expect(cells[2]), $"[{row}] local");
            Names(profile, CriticalityKind.PersistentBottleneck).Should().Equal(Expect(cells[3]), $"[{row}] persistent");
            Names(profile, CriticalityKind.StructuralBottleneck).Should().Equal(Expect(cells[4]), $"[{row}] structural");
            // **这一条查的是用例行自己的编码，不是实现。**
            // `proven_target_count` 在 C# 侧没有公开访问器，**所以这里只能确认
            // 用例行与它自己的写法自洽**——分母写错了会让上面三条一起变宽松，而它们不会报错。
            //
            // **This checks the row's own encoding, not the implementation.** `proven_target_count` has no public accessor on the C# side, **so all this can confirm is that the row agrees with itself** — a wrong
            // denominator would quietly loosen the three assertions above without failing any of them.
            observations.Count(observation => observation.Status == AnalysisStatus.Unreachable)
                .Should().Be(int.Parse(cells[5]), $"[{row}] the row's own encoding has to agree with its proven count");
        }
    }

    /// <summary>某一层的约束名，去掉前缀并按契约的写法排序 / The names in one level.</summary>
    private static string[] Names(CriticalityProfile profile, CriticalityKind kind) => profile
        .Classifications
        .Where(pair => pair.Value.Contains(kind))
        .Select(pair => pair.Key.Value.Replace("constraint:", string.Empty, StringComparison.Ordinal))
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToArray();

    /// <summary>
    /// 保持契约里的顺序读成名字表 / Read the cell preserving the contract's order.
    /// </summary>
    /// <remarks>
    /// **`[correction]` 的成员顺序本身就是契约的一部分**（按权重×松弛升序、同成本按 stableId 升序），
    /// **所以这里绝不能排序**——**排一次序，判据就再也看不出排序错了。**
    ///
    /// **The member order in `[correction]` is itself the contract** (ascending weight times relaxation, ties by stable id),
    /// **so this must not sort** — **one sort and the criterion can never see a wrong order again.**
    /// </remarks>
    private static string[] ExpectOrdered(string cell) => cell == "-"
        ? Array.Empty<string>()
        : cell.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>把契约里的 "-" 读成空集 / Read the contract's "-" as the empty set.</summary>
    private static string[] Expect(string cell) => cell == "-"
        ? Array.Empty<string>()
        : cell.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>**修正集合的两个构造器与它们的边界** / The two correction builders and their boundaries.</summary>
    /// <remarks>
    /// `weighted` 走**单数**构造器（一个集合装全部成员，按权重×松弛升序，同成本按 stableId 升序），
    /// `minimal` 走最小性构造器。**两者都不是"每个成员一组"。**
    ///
    /// **`minimal` 的 `minimal` 标志只看"候选数有没有被 `max_candidates` 截断"**，
    /// **而 `weighted` 永远是 `false`**——**它从未做过删除验证，凭什么声称最小。**
    ///
    /// `weighted` takes the **singular** builder (one set holding every member, ordered by weight times relaxation, ties by stable id);
    /// `minimal` takes the minimality builder. **Neither is "one set per member".**
    /// **`minimal`'s flag only reflects whether `max_candidates` truncated the candidates**, **while `weighted` is always `false`** — **it never ran a deletion check, so it has no ground to claim minimality.**
    /// </remarks>
    [Fact]
    public void TheCorrectionTable_ShouldMatchEveryCase()
    {
        foreach (string[] cells in Section("correction"))
        {
            cells.Should().HaveCount(11, "correction rows carry the operation, policy, candidates, and expectations");

            var policy = new RelaxabilityPolicy(
                MaxCandidates: int.Parse(cells[2]),
                MaxPlans: int.Parse(cells[3]),
                RequirePositiveWeight: bool.Parse(cells[4]));
            CorrectionCandidate[] candidates = ParseCandidates(cells[6]);
            bool accepted = bool.Parse(cells[10]);
            string row = string.Join(" ", cells);

            Func<CorrectionSet> build = cells[1] switch {
                "weighted" => () => CorrectionRecommendations.WeightedCorrectionSet(candidates, policy),
                "minimal" => () => CorrectionRecommendations.MinimalCorrectionSet(
                    candidates, bool.Parse(cells[5]), policy),
                _ => throw new InvalidOperationException($"unknown correction operation {cells[1]}"),
            };

            CorrectionSet? set = null;
            try
            {
                set = build();
            }
            catch (ArgumentException)
            {
                // **拒绝也是契约的一部分。**
                // **Refusing is part of the contract too.**
            }

            (set is not null).Should().Be(accepted, $"[{row}] accepted");
            if (set is null)
            {
                continue;
            }

            set.Members.Select(member => member.Source.StableId.Replace("constraint:", string.Empty, StringComparison.Ordinal))
                .Should().Equal(ExpectOrdered(cells[7]), $"[{row}] member order");
            set.TotalCost.Should().BeApproximately(
                double.Parse(cells[8], System.Globalization.CultureInfo.InvariantCulture), 1e-9, $"[{row}] cost");
            set.Minimal.Should().Be(bool.Parse(cells[9]), $"[{row}] minimality");
        }
    }

    /// <summary>**同成本时按 stableId 排序，而不是按输入顺序** / Ties break on stable id, not input order.</summary>
    /// <remarks>
    /// **契约里那条 `weighted-tie-breaks-on-stable-id` 区分不了这件事**：它的输入是 `c10,c2`，
    /// **而序数序恰好也是 `c10,c2`**——**`'1' < '2'`。去掉 `ThenBy`，`OrderBy` 是稳定排序，照样给出同样的答案。**
    ///
    /// **所以这条判据反过来喂**：输入 `c2,c10`，期望 `c10,c2`。**只有真的排序才能得到它。**
    /// **用例文件必须逐字保真，所以这条补在它旁边，而不是改它。**
    ///
    /// **The contract row `weighted-tie-breaks-on-stable-id` cannot distinguish this**: its input is `c10,c2`,
    /// **and ordinal order is also `c10,c2`** — **`'1' < '2'`.** Drop the `ThenBy` and a stable `OrderBy` still returns the same answer.
    /// **So this one feeds the reverse**: input `c2,c10`, expected `c10,c2`. **Only a real sort produces that.**
    /// **The case file has to stay verbatim, so this sits beside it rather than editing it.**
    /// </remarks>
    [Fact]
    public void TheTieBreak_ShouldIgnoreInputOrder()
    {
        CorrectionCandidate[] candidates =
        [
            new(new DiagnosticSource.Constraint(new ConstraintId("c2")), 1.0, 1.0),
            new(new DiagnosticSource.Constraint(new ConstraintId("c10")), 1.0, 1.0),
        ];

        CorrectionRecommendations.WeightedCorrectionSet(candidates)
            .Members.Select(member => member.Source.StableId)
            .Should().Equal(new[] { "constraint:c10", "constraint:c2" },
                "equal costs order by stable id, so the input order must not leak through");
    }

    /// <summary>解析候选列 / Parse the candidate column.</summary>
    /// <remarks>
    /// 两种写法：`约束id:weight:relaxation` 与 `变量来源:变量id:weight:relaxation`。
    /// **四段的那种第一个词是来源种类，第二个是变量名。**
    ///
    /// Two shapes: `constraint-id:weight:relaxation` and `source-kind:variable-id:weight:relaxation`.
    /// **In the four-part shape the first field is the source kind and the second the variable name.**
    /// </remarks>
    private static CorrectionCandidate[] ParseCandidates(string cell)
    {
        if (cell.Length == 0)
        {
            return [];
        }

        return cell.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry =>
            {
                string[] parts = entry.Split(':');
                DiagnosticSource source = parts.Length switch
                {
                    3 => new DiagnosticSource.Constraint(new ConstraintId("constraint:" + parts[0])),
                    4 => parts[0] switch
                    {
                        "variable-lower-bound" => new DiagnosticSource.VariableLowerBound(new VariableId(parts[1])),
                        "variable-upper-bound" => new DiagnosticSource.VariableUpperBound(new VariableId(parts[1])),
                        "sparse-domain" => new DiagnosticSource.SparseDomain(new VariableId(parts[1])),
                        _ => throw new InvalidOperationException($"unknown source kind {parts[0]}"),
                    },
                    _ => throw new InvalidOperationException($"unreadable candidate {entry}"),
                };

                int offset = parts.Length - 2;
                return new CorrectionCandidate(
                    source,
                    double.Parse(parts[offset], System.Globalization.CultureInfo.InvariantCulture),
                    double.Parse(parts[offset + 1], System.Globalization.CultureInfo.InvariantCulture));
            })
            .ToArray();
    }

    /// <summary>**没断言的节必须被点名** / Unasserted sections have to be named.</summary>
    /// <remarks>
    /// **一张越来越短的用例表和一张全绿的表，从测试结果上看不出来。**
    /// 这条判据把差距写成名单，**并让名单与文件不一致时变红**——
    /// **搬完一节就得改名单，这是唯一能强制"覆盖在长"的机制。**
    ///
    /// **A shrinking case table and a fully green one look identical in the results.** This criterion writes the gap as a list **and goes red when the list and the file disagree** — **porting a section forces an edit,
    /// and that is the only mechanism that makes coverage grow.**
    /// </remarks>
    [Fact]
    public void TheUncoveredSections_ShouldBeNamed()
    {
        string[] sections = Sections();
        sections.Should().NotBeEmpty("the case table declares its sections");

        string[] covered = sections.Except(NotYetAsserted, StringComparer.Ordinal).ToArray();
        string[] named = NotYetAsserted.Intersect(sections, StringComparer.Ordinal).ToArray();

        // **名单里的每一项都必须真的是这个文件的一节**；不在文件里的名字说明名单过期了。
        // **Every name in the list has to be a section of this file**; a name that is not means the list went stale.
        named.Should().Equal(NotYetAsserted,
            "the uncovered list may only name sections this file actually declares");

        covered.Should().Equal(
            new[] { "status-map", "classify", "combine", "shortlist", "criticality", "correction" },
            "these three are asserted above; a fourth appearing here means it was added without a criterion");
    }
}
