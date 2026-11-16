#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 交接区的"从这里开始"必须是活的 / The hand-off "start here" has to be alive.
/// </summary>
/// <remarks>
/// **一个过期的"从这里开始"比没有更糟**：下一个人会先去做一件已经做完的事，
/// **然后在 diff 里发现它做完了，再回头重新判断从哪里开始。**
///
/// **A stale "start here" is worse than none**: the next person does finished work first, **then discovers it in the diff and has to re-decide where to start.**
/// </remarks>
public class HandoffFreshnessTests {
    /// <summary>
    /// **交接状态表的数字不能比计划本身旧** / The hand-off table must not be older than the plan itself.
    /// </summary>
    /// <remarks>
    /// **B1284 收口时看到 `### 下一批（B1284）的唯一入口` 下面那张表：**
    /// **写着"状态文档 `上次更新 Batch 1274`，集成 11425、默认 11261"、"文档判据 229 条"、
    /// "最近归档日志 `p8-batch1275-*`"——<b>而实际已经是 1283 / 11450 / 11286 / 236</b>。**
    ///
    /// **这一节是交接的唯一入口，也是下一个人最先读的地方，而它<b>已经落后九批**。
    /// 更糟的是：它<b>自称**是"B1274 把其中的数字更新到最新"的那张表。**
    ///
    /// **B1273 的教训是"过期 1062 批的文字"换成了真实状态——
    /// 而 B1274 之后它又开始过期，这次每收口一批就更旧一点，没有任何东西发现。**
    ///
    /// **判据只查能被机器核对的两格：**
    /// **① `最新收口批次` 必须等于计划里最新的 `### Pn Batch N`；**
    /// **② `状态文档` 那一格引用的 `上次更新 Batch N` 必须等于状态文档里真实写的那个数。**
    /// **其余数字（集成／默认／警告）不在此列：** **它们由门禁产生，改文档时手抄一次就够了，
    /// 而<b>批次号是"这一节属于哪一批"的唯一标识**，错一次就整节失效。**
    ///
    /// **B1284 saw the table under `### 下一批（B1284）的唯一入口` still saying "status doc: Batch 1274, integration 11425,
    /// default 11261", "docs criteria 229" and "latest archive p8-batch1275-*" while reality was 1283 / 11450 / 11286 / 236.**
    /// **That section is the only entry point for a hand-off and the first thing the next person reads, and it had fallen nine
    /// batches behind — while calling itself the table B1274 brought up to date.** **The criterion checks only the two cells a machine
    /// can verify: the newest closed batch equals the newest `### Pn Batch N` in the plan, and the batch quoted for the status
    /// document equals the one that document really states.** **The other numbers come out of the gate and are transcribed once per
    /// batch; the batch number is the only identifier of "which batch this section belongs to", and one stale value voids the whole
    /// section.**
    /// </remarks>
    [Fact]
    public void TheHandoffTable_ShouldNameTheNewestBatch() {
        string plan = File.ReadAllText(PlanPath());
        // 阶段号必须允许多位：**`P[4-9]` 只认个位数阶段，而计划已经走到 P10——**
        // **那条正则会把 P10 的批次整条漏掉，于是"最新批次"停在上一个阶段，而比较照样通过。**
        //
        // The phase number has to allow more than one digit: **P[4-9] only matches single-digit phases and the plan has reached P10 —** the regex would drop every P10 entry, leaving
        // "newest batch" at the previous phase while the comparison still passed.
        int newest = Regex.Matches(plan, @"^### P[1-9]\d* Batch (\d+)", RegexOptions.Multiline)
            .Select(match => int.Parse(match.Groups[1].Value))
            .Max();

        // **必须锚定行首标题，不能用 `IndexOf`。**
        //
        // **第一版用 `plan.IndexOf("### 下一批（")`，它命中的是<b>散文里的一次引用**（对象在 183928），
        // **而真正的标题在 203820。于是切片取到的是一段没有表格的散文，
        // 判据报"表格没说最新批次"——而表格说了，只是没被读到。**
        //
        // **这是 B1274 记过的那个坑的第二次：** **一条把"引述"当成"断言"的判据。**
        //
        // **The heading must be matched at line start: the first version used `IndexOf`, which hit a <b>quotation in prose</b> and
        // produced a slice with no table at all.** **Second occurrence of the B1274 trap: a criterion mistaking a quotation for an
        // assertion.**
        Match heading = Regex.Match(plan, @"^### 下一批（B\d+）的唯一入口", RegexOptions.Multiline);
        heading.Success.Should().BeTrue("the hand-off pointer section exists as a line-initial heading");
        int start = heading.Index;
        // **表格在 `#### ①` 之后、`#### ②` 之前。** **第一版切到 `#### ①`，切片里根本没有表格。**
        // **The table sits after `#### ①` and before `#### ②`; cutting at ① left no table in the slice.**
        int end = plan.IndexOf("#### ②", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the hand-off table sits between the first two subsections");
        string handoff = plan[start..end];

        Match closed = Regex.Match(handoff, @"\| 最新收口批次 \| \*\*Batch (\d+)\*\* \|");
        closed.Success.Should().BeTrue("the hand-off table states the newest closed batch");
        int.Parse(closed.Groups[1].Value).Should().Be(
            newest,
            "the entry point of a hand-off must point at the batch that just closed");

        Match quoted = Regex.Match(handoff, @"`上次更新 Batch (\d+)`");
        quoted.Success.Should().BeTrue("the hand-off table quotes the status document's marker");
        int quotedBatch = int.Parse(quoted.Groups[1].Value);

        string status = File.ReadAllText(System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(PlanPath())!, "..", "docs", "kotlin-sync-status.md"));
        Match actual = Regex.Match(status, @"上次更新 Batch (\d+)");
        actual.Success.Should().BeTrue("the status document still carries the marker");
        quotedBatch.Should().Be(
            int.Parse(actual.Groups[1].Value),
            "the hand-off must quote what the status document really says, not what it said nine batches ago");
    }

    private static string PlanPath() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "logs", "kotlin-sync-plan.md");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("logs/kotlin-sync-plan.md not found");
    }

    /// <summary>**交接区里点名的批次号不得比最新的还旧** / A batch named in the hand-off is not older than the newest.</summary>
    /// <remarks>
    /// 实测发现："下一批（B643）的唯一入口"这句话在 B1024 时还在，
    /// **而 B643 自己的标题就写着它做了什么**——它是 380 个批次前的入口。
    ///
    /// Measured: "the only entry point for the next batch (B643)" was still there at B1024, **while B643's own title says what it did** — an entry point 380 batches old.
    /// </remarks>
    [Fact]
    public void TheNextBatchPointer_ShouldNotLagBehind() {
        string plan = File.ReadAllText(PlanPath());

        // 阶段号必须允许多位：**`P[4-9]` 只认个位数阶段，而计划已经走到 P10——**
        // **那条正则会把 P10 的批次整条漏掉，于是"最新批次"停在上一个阶段，而比较照样通过。**
        //
        // The phase number has to allow more than one digit: **P[4-9] only matches single-digit phases and the plan has reached P10 —** the regex would drop every P10 entry, leaving
        // "newest batch" at the previous phase while the comparison still passed.
        int newest = Regex.Matches(plan, @"^### P[1-9]\d* Batch (\d+)", RegexOptions.Multiline)
            .Select(match => int.Parse(match.Groups[1].Value))
            .Max();

        // **交接区在"## 零、"到"## 一、"之间。**
        // **The hand-off sits between "## 零、" and "## 一、".**
        int start = plan.IndexOf("## 零、", StringComparison.Ordinal);
        int end = plan.IndexOf("## 一、", start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the hand-off section exists");
        end.Should().BeGreaterThan(start);

        string handoff = plan[start..end];

        // **只认行首的 `### 下一批（BNNNN）` 标题，不认正文里的引用。**
        //
        // **B1274 踩到的正是这个：** **B1273 的事故叙述里三次引用了
        // `### 下一批（B1273）的唯一入口` 这句话（那时它是真的标题），
        // 于是判据把三处引用都当成了指针，报"指针指向 B1273 而最新是 B1274"。**
        //
        // **而那段叙述里<b>必须**能引用旧标题——否则没法说清自己删掉了什么。**
        // **一条会把"引述"当成"断言"的判据，会逼着人改写历史。**
        //
        // **Only a line-initial `### 下一批（BNNNN）` heading counts; a quotation in prose does not.** **B1274 hit exactly this: the
        // B1273 incident narrative quotes that heading three times (it really was the heading then), and the criterion read all
        // three as pointers, reporting "points at B1273 while the newest is B1274".** **The narrative <b>must</b> be able to quote
        // the old heading, or it cannot say what it deleted — and a criterion that mistakes a quotation for an assertion forces
        // history to be rewritten.**
        // **再收紧一层：标题后面必须紧跟 `的唯一入口`，因为被引述的那句是
        // `### 下一批（B1273）的唯一入口` 这一节里……
        // 也就是说引述句在标题后多了一个反引号。**
        //
        // **Tightened once more: the heading must be followed directly by 的唯一入口, because the quoted line continues with a
        // backtick after the heading.**
        MatchCollection pointers = Regex.Matches(
            handoff, @"^### 下一批（B(\d+)）的唯一入口", RegexOptions.Multiline);
        pointers.Should().NotBeEmpty("the hand-off names where the next batch starts");

        int[] lagging = pointers
            .Select(match => int.Parse(match.Groups[1].Value))
            // **允许"下一批"就是最新号或更新**，不允许它落在后面很远。
            // **The pointer may be the newest number or beyond, never far behind.**
            .Where(number => number < newest)
            .ToArray();

        lagging.Should().BeEmpty(
            $"the hand-off points at B{string.Join(", B", lagging)} while the newest batch is B{newest}; "
            + "a pointer that old sends the reader to finished work");
    }

    /// <summary>**交接区不得把已完成的批次当成"待做"** / The hand-off does not call a finished batch pending.</summary>
    /// <remarks>
    /// 这是上一条的另一种形状：不写"下一批（B643）"，而是正文里说"B643 还没做"。
    ///
    /// A different shape of the same defect: instead of "next batch (B643)", the body says "B643 is not done".
    /// </remarks>
    [Fact]
    public void TheHandoff_ShouldNotCallAFinishedBatchPending() {
        string plan = File.ReadAllText(PlanPath());

        int start = plan.IndexOf("## 零、", StringComparison.Ordinal);
        int end = plan.IndexOf("## 一、", start, StringComparison.Ordinal);
        string handoff = plan[start..end];

        // **凡是交接区里被点名的批次号，都必须在正文里真的有一条对应条目。**
        // **Every batch number named in the hand-off must have a real entry in the body.**
        // **"下一批（Bx）"指向的批次按定义还没写**，把它从"必须存在"里排除——
        // **否则两条判据会互相打架，而它们本来该各自成立。**
        //
        // **The "next batch (Bx)" pointer names a batch that by definition is not written yet**, so it is excluded from "must exist" — **otherwise the two criteria fight each other
        // while both should hold.**
        // **与上一条同样只认行首标题**：引述旧标题是叙述事故所必需的，
        // **而把引述当成"指向哪里"的声明，会逼着人改写历史。**
        //
        // **Line-initial headings only, for the same reason as above: quoting an old heading is how the incident is narrated, and
        // reading a quotation as a "starts here" declaration forces history to be rewritten.**
        int[] pointers = Regex.Matches(
                handoff, @"^### 下一批（B(\d+)）的唯一入口", RegexOptions.Multiline)
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToArray();

        int[] named = Regex.Matches(handoff, @"\bB(\d{3,4})\b")
            .Select(match => int.Parse(match.Groups[1].Value))
            .Where(number => !pointers.Contains(number))
            .Distinct()
            .ToArray();

        named.Should().NotBeEmpty("the hand-off cites batch numbers");

        // **B1273 的 891—1268 是已声明丢失区间：判据不为它开脱，只为它附一句理由。**
        //
        // **这不是放宽——`PhaseRangeTests.TheBatchNumbers_ShouldBeGloballyContiguous` 仍然红着，
        // 断号确实存在。这里做的只是把失败信息从"B891 不见了"补成
        // "B891 不见了，而原因写在 B1273 的事故记录里"。**
        //
        // **一条红判据若说不出原因，读它的人只能去猜；而这次的答案就在 `0.5` 节。**
        //
        // **B1273's 891—1268 is a declared lost range; this does not excuse it, only attaches the reason.** **This is not a
        // relaxation — `PhaseRangeTests` still fails and the hole is real. It upgrades the message from "B891 is missing" to
        // "B891 is missing, and the reason is written in B1273's incident record".** **A failing criterion that cannot state its
        // cause leaves the reader guessing, and this time the answer is in section 0.5.**
        const int DeclaredLossFirst = 891;
        const int DeclaredLossLast = 1268;

        string[] missing = named
            // 两处假设都过时了：**阶段号已经到两位数，而标题也早就不用"数字+冒号"的写法，** **于是它会把真实存在的批次判成"只出现在交接里"。**
            // Both assumptions were stale: **phases have reached two digits and headings no longer use number-plus-colon, so a batch that really exists got judged as "cited only in the hand-off".**
            .Where(number => !Regex.IsMatch(plan, $@"^### P[1-9]\d* Batch {number}(?:[：:（(]|$)", RegexOptions.Multiline))
            .Select(number => $"B{number}")
            .ToArray();

        string[] outsideDeclaredLoss = missing
            .Select(static text => int.Parse(text[1..], System.Globalization.CultureInfo.InvariantCulture))
            .Where(number => number < DeclaredLossFirst || number > DeclaredLossLast)
            .Select(static number => $"B{number}")
            .ToArray();

        outsideDeclaredLoss.Should().BeEmpty(
            "a batch number the hand-off cites has to exist in the body: " + string.Join(", ", outsideDeclaredLoss));

        if (missing.Length > 0) {
            plan.Should().Contain(
                "891—1268",
                "the missing numbers fall inside a lost range, so that range must stay declared in the plan");
        }
    }

    /// <summary>**"从这里开始"的标题不得比它的内容旧** / A "start here" heading is not older than its own body.</summary>
    /// <remarks>
    /// 实测：`### 0.0 下一会话从这里开始（Batch 441 收口时实测）` 的内容一路写到 B467。
    /// **标题说 441，内容到 467——读标题的人会以为这一节在 441 就停了。**
    ///
    /// Measured: the heading said "the next session starts here (measured when Batch 441 closed)" while the body ran to B467. **A reader trusting the heading would think this section
    /// stopped at 441.**
    /// </remarks>
    [Fact]
    public void TheStartHereHeading_ShouldNotBeOlderThanItsBody() {
        string plan = File.ReadAllText(PlanPath());

        // **只查自称"从这里开始"的标题。**
        // **Only headings that call themselves "start here".**
        // **只认 `### ` 开头的标题**，正文里的引用不算——B1025 时就是被正文引用骗过一次。
        // **Only a real `### ` heading counts**; a quotation in prose does not, and B1025 was fooled by exactly that.
        // **要求同一行里既有"从这里开始"又有 `Batch \d+`**：否则会匹配到把这句话当标题词的批次条目，**而那一节下面没有 B 编号，判据会提前返回。**
        // **The line needs both "start here" and `Batch \d+`**; otherwise it matches a batch entry that merely uses the phrase in its title, **and that section has no batch numbers below, so the
        // criterion returns early.**
        Match heading = Regex.Match(plan, @"^### [^\n]*?从这里开始[^\n]*?Batch (\d+)", RegexOptions.Multiline);
        if (!heading.Success) {
            return;
        }

        int declared = int.Parse(heading.Groups[1].Value);

        // **这一节的正文：从这个标题到下一个 `###` 或 `## `。**
        int start = heading.Index + heading.Length;
        Match next = Regex.Match(plan[start..], @"^(?:## |### )", RegexOptions.Multiline);
        string body = next.Success ? plan[start..(start + next.Index)] : plan[start..];

        int[] cited = Regex.Matches(body, @"\bB(\d{3,4})\b")
            .Select(match => int.Parse(match.Groups[1].Value))
            .ToArray();

        if (cited.Length == 0) {
            return;
        }

        cited.Max().Should().BeLessThanOrEqualTo(declared,
            $"the \"start here\" heading declares B{declared} while its body runs to B{cited.Max()}; "
            + "a heading older than its own body is a heading nobody can trust");
    }
}
