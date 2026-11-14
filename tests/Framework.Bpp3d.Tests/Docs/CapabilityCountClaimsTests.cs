#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **"N 条判据"里的 N 是执行用例数，不是方法数** /
/// The N in "N criteria" counts executed cases, not methods.
/// </summary>
/// <remarks>
/// **B1285 按计划的清单逐条核对矩阵里的"N 条判据"。第一批核了六处，而<b>头两处当场就对不上**：
///
/// | 文档 | 方法数 | 我的第一遍结论 |
/// | --- | --- | --- |
/// | `MongoScalarTranslator` **11** | 7 | **"多写了 4"** |
/// | `MongoBooleanTranslator` **14** | 9 | **"多写了 5"** |
///
/// **而把 `[Theory]` 的 `[InlineData]` 也算进去之后：** **6 + 5 = 11、8 + 6 = 14——<b>两处都是对的**。
///
/// **错的不是文档，是我数的东西。** **而这件事必须写进判据里，
/// 否则下一个人还会用方法数去核，然后得到同一个假结论。**
///
/// ### 判据做两件事
///
/// 1. **按"用例数"（`[Fact]` + `[InlineData]`）数出真实数量**，与下面表里的数字比；
/// 2. **矩阵里那个数字必须与它相邻出现**——**两处数字对不上时，
/// 无论谁错都会被这条判据抓住。**
///
/// **B1285 walked the plan's checklist and verified six "N criteria" claims. The first two looked wrong** — the document said 11
/// and 14 while the files had 7 and 9 methods — **but counting `[Theory]`'s `[InlineData]` made them 6+5=11 and 8+6=14: both were
/// right, and what was wrong was what I counted.** **That has to live in the criterion, or the next person counts methods again and
/// reaches the same false conclusion.**
///
/// **Two jobs: count real cases and compare, and require the number to appear next to the name in the matrix — so whichever side
/// drifts, the criterion reddens.**
/// </remarks>
public class CapabilityCountClaimsTests
{
    /// <summary>**六处"N 条判据"与实际用例数一致** / Six claims match the real case count.</summary>
    [Fact]
    public void ThePluginCounts_ShouldMatchTheirTestFiles() {
        (string Type, string File, int Expected)[] claims = [
            ("MongoScalarTranslator", "MongoScalarTranslatorTests.cs", 11),
            ("MongoOrderByTranslator", "MongoOrderByTranslatorTests.cs", 5),
            ("MongoUpdateTranslator", "MongoUpdateTranslatorTests.cs", 6),
            ("MongoBooleanTranslator", "MongoBooleanTranslatorTests.cs", 14),
            ("KafkaInterfaceTests", "KafkaInterfaceTests.cs", 8),
            ("RedisInterfaceTests", "RedisInterfaceTests.cs", 11),
        ];

        string root = RepositoryRoot();
        string[] testFiles = Directory
            .EnumerateFiles(System.IO.Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        string matrix = File.ReadAllText(System.IO.Path.Combine(
            root, "docs", "kotlin-capability-matrix.md"));

        foreach ((string type, string file, int expected) in claims) {
            string path = testFiles.Single(candidate =>
                System.IO.Path.GetFileName(candidate) == file);
            int actual = CaseCount(File.ReadAllText(path));

            actual.Should().Be(
                expected,
                $"{file} holds the criteria the matrix attributes to {type}; the count is executed cases, not methods");

            int index = matrix.IndexOf(type, StringComparison.Ordinal);
            index.Should().BeGreaterThanOrEqualTo(0, $"the matrix mentions {type}");
            string surrounding = matrix.Substring(index, System.Math.Min(240, matrix.Length - index));
            // **不强求加粗：** **矩阵里这些行有的写 `**N 条判据**`、有的写 `N 条判据`，
            // 而"加粗与否"是排版，不是事实。** **要求加粗会让一条正确的行变红——
            // 这与 B1264／B1270／B1274 记过的"判据被散文弄红"是同一类。**
            //
            // **连"条判据"三个字也不强求：** **`RedisInterfaceTests` 那行写的是"（11 条）"，
            // 而 `KafkaInterfaceTests` 那行写的是"8 条判据"。** **两种写法，同一个事实。**
            //
            // **Nor are the words after the number: the Redis row says "（11 条）" while the Kafka row says "8 条判据" — two
            // spellings of one fact.**
            //
            // **Bold is not required either: some rows write `**N 条判据**` and others `N 条判据`, and bolding is typography, not fact.**
            // **Requiring it would redden a correct row — the same family as the "criteria reddened by prose" incidents.**
            Regex.IsMatch(surrounding, $@"{expected}\s*条").Should().BeTrue(
                $"the matrix must state {expected} next to {type}; that number is what a reader uses to judge whether this piece is finished. "
                + $"Found: {surrounding.Substring(0, System.Math.Min(80, surrounding.Length))}");
        }
    }


    /// <summary>
    /// **两处 P4 的能力计数同样与实际用例数一致** / Two P4 capability counts match their real case count.
    /// </summary>
    /// <remarks>
    /// **这两处与上面那六处不同：** **上面是插件层，这两处是<b>P4 的核心能力**——
    /// 而"这块做完了吗"正是靠这两个数字回答的。**
    ///
    /// **而这里有一个第 1285 批教训<b>不可用**的情形：** **这两个文件没有任何 `[Theory]`，
    /// 所以"方法数"与"用例数"恰好相同。** **恰好相同不等于同一个概念**——
    /// **判据仍然按用例数（`[Fact]` + `[InlineData]`）数，只是这次两者没有差别。**
    ///
    /// **These two differ from the six above: those are the plugin layer, these are <b>P4's core capabilities</b>, and these numbers
    /// are how a reader decides whether that piece is done.** **One B1285 lesson does not apply here: neither file has any
    /// `[Theory]`, so methods and cases happen to be equal — and happening to be equal is not the same as being the same concept.**
    /// **The criterion still counts cases; this time the two just agree.**
    /// </remarks>
    [Fact]
    public void TheP4Counts_ShouldMatchTheirTestFiles() {
        (string Symbol, string File, int Expected)[] claims = [
            ("TidyDualSolutionByIdentity", "LinearTriadDualTest.cs", 23),
            ("NativeConstraintEvaluations", "NativeConstraintEvaluationsTests.cs", 7),
        ];

        string root = RepositoryRoot();
        string[] testFiles = Directory
            .EnumerateFiles(System.IO.Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        string matrix = File.ReadAllText(System.IO.Path.Combine(
            root, "docs", "kotlin-capability-matrix.md"));

        foreach ((string symbol, string file, int expected) in claims) {
            string path = testFiles.Single(candidate =>
                System.IO.Path.GetFileName(candidate) == file);
            CaseCount(File.ReadAllText(path)).Should().Be(
                expected,
                $"{file} holds the criteria the matrix attributes to {symbol}");
        }

        // **`NativeConstraintEvaluations` 那两个数字分散在同一行里**，所以按符号名找不到唯一的"N 条判据"；
        // **这里只核 `TidyDualSolutionByIdentity` 的邻接数字。**
        //
        // **The two `NativeConstraintEvaluations` numbers share one row, so no unique "N criteria" sits next to the symbol; only the
        // `TidyDualSolutionByIdentity` adjacency is checked here.**
        // **按整行取，不按固定字符数取。**
        //
        // **第一版取了符号之后 400 个字符，而那一行很长，"23 条判据"在更靠后的位置——
        // 判据红在"没找到 23"，而矩阵里明明写着 23。**
        // **固定窗口宽度是一种隐形假设：它假设文字会在那么长以内出现。**
        //
        // **Take the whole line, not a fixed number of characters: the first version sliced 400 chars after the symbol while the claim
        // sat further along that long row, so the criterion reddened on "no 23" while the matrix plainly said 23.** **A fixed window
        // width is a hidden assumption that the text appears within that distance.**
        // **计数声明在提到 `LinearTriadDualTest` 的那一行，不是在提到方法名的那一行。**
        //
        // **第一版在 `TidyDualSolutionByIdentity` 那一行里找 23，而那一行写的是变异故事里的另一个数。**
        // **而这一批真正找到的错，就在那个正确的行里：文档写的是 24。**
        //
        // **The count is stated in the row that names `LinearTriadDualTest`, not in the row that names the method.** **The first version
        // looked in the wrong row — and the real error this batch found sat in the right one: the document said 24.**
        Match row = Regex.Match(
            matrix,
            @"^.*LinearTriadDualTest.*$",
            RegexOptions.Multiline);
        row.Success.Should().BeTrue("the matrix states the count for LinearTriadDualTest");
        Regex.IsMatch(row.Value, @"23\s*条").Should().BeTrue(
            "the matrix must state 23 for LinearTriadDualTest; B1286 found it said 24 while the file held 23 cases");
    }

    /// <summary>
    /// **框架层的两处计数与"影子检查"的那句断言** / Two framework counts, and the shadow-check claim they support.
    /// </summary>
    /// <remarks>
    /// **B1287 核的两处都是"用数字支撑一个结论"的写法：**
    ///
    /// - **`SqlRelationalQueryCompiler` 的 14 条**支撑"谓词翻译能力在 C# 侧是有的，只是换了机制"
    /// - **三个接口测试文件的合计 33 条**支撑"这些测试全是影子检查"
    ///
    /// **而第二处还有一句更强的话：** **"那三个文件<b>都不构造** `RedisClient`"**——
    /// **这句是"Redis 可用"没有依据的**全部论据**，而它此前只是一句散文。** **这一批把它变成可核对的。**
    ///
    /// **Both are "a number supporting a conclusion": the compiler's 14 back "predicate translation exists here, just via another
    /// mechanism", and the 33 back "these tests are all shadow checks".** **The second carries a stronger sentence — none of the three
    /// files constructs `RedisClient` — which is the <b>entire</b> argument that "Redis is usable" has no basis, and it was prose
    /// only.** **This batch makes it checkable.**
    /// </remarks>
    [Fact]
    public void TheFrameworkCounts_AndTheShadowCheckClaim_ShouldHold() {
        string root = RepositoryRoot();
        string[] testFiles = Directory
            .EnumerateFiles(System.IO.Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(static path => !path.Contains($"{System.IO.Path.DirectorySeparatorChar}bin{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        int Compiler() => CaseCount(File.ReadAllText(
            testFiles.Single(path => System.IO.Path.GetFileName(path) == "SqlRelationalQueryCompilerTest.cs")));
        int Interface() => CaseCount(File.ReadAllText(
            testFiles.Single(path => System.IO.Path.GetFileName(path) == "InterfaceVerificationTests.cs")));

        Compiler().Should().Be(
            14,
            "the matrix backs \"predicate translation exists, via another mechanism\" with 14 criteria");
        Interface().Should().Be(14, "the matrix states 14 for InterfaceVerificationTests");

        // **合计必须是三份的和，而不是第三个数。**
        // **The total must be the sum of the three, not a fourth number.**
        int total = Interface()
            + CaseCount(File.ReadAllText(testFiles.Single(path => System.IO.Path.GetFileName(path) == "RedisInterfaceTests.cs")))
            + CaseCount(File.ReadAllText(testFiles.Single(path => System.IO.Path.GetFileName(path) == "KafkaInterfaceTests.cs")));
        total.Should().Be(33, "the matrix says these three files add up to 33 criteria");

        // **当然也要断言文档确实这么写。**
        //
        // **第一版只算了真实值、没查文档，于是把文档里的 33 改成 32 判据照样全绿——**
        // **那意味着它只能发现代码变了，发现不了文档变了。**
        //
        // **The document has to be asserted too: the first version computed the real value and never read the document, so changing
        // 33 to 32 in the matrix left everything green — it could notice code moving and not the document moving.**
        string matrix = File.ReadAllText(System.IO.Path.Combine(root, "docs", "kotlin-capability-matrix.md"));
        Regex.IsMatch(matrix, @"合计 33\s*条判据").Should().BeTrue(
            "the matrix must state that these three files add up to 33 criteria");

        // **"三个文件都不构造 `RedisClient`"** —— 这句是"Redis 可用没有依据"的全部论据。
        // **"None of the three constructs `RedisClient`"** — the entire argument behind "Redis usability has no basis".
        foreach (string file in new[] {
            "InterfaceVerificationTests.cs", "RedisInterfaceTests.cs", "KafkaInterfaceTests.cs",
        }) {
            string source = File.ReadAllText(testFiles.Single(path => System.IO.Path.GetFileName(path) == file));
            source.Should().NotContain(
                "new RedisClient(",
                file + " must stay a shadow check; the moment one of them constructs the client, \"nothing ever creates it\" stops being true");
        }
    }

    /// <summary>按执行用例数计数：`[Fact]` 一个，`[InlineData]` 一个。</summary>
    /// <remarks>
    /// **不数 `[Theory]` 本身：** **一个 `[Theory]` 不产生一条用例，
    /// 它产生的条数等于它的 `[InlineData]` 个数。** **数它会把每条理论多加一。**
    ///
    /// **`[Theory]` itself is not counted: a theory produces no case of its own; it produces one per `[InlineData]`.**
    /// **Counting it would add one per theory.**
    /// </remarks>
    private static int CaseCount(string source) {
        int facts = Regex.Matches(source, @"^\s*\[Fact\]", RegexOptions.Multiline).Count;
        int inline = Regex.Matches(source, @"^\s*\[InlineData", RegexOptions.Multiline).Count;
        return facts + inline;
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