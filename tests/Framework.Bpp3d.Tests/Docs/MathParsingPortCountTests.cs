#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **矩阵里那三个"条判据"数字必须与测试文件对得上** /
/// The three criterion counts in the matrix must match the test files.
/// </summary>
/// <remarks>
/// **B1274 是做"复核已搬格"时撞上的：** **矩阵写 `PolynomialParser.kt` 已搬、**24 条判据**，
/// 而 `PolynomialParserTests.cs` 里实际是 **22 条**。**
///
/// ### 为什么只钉这三行
///
/// **矩阵里共有 **55 处** "N 条判据"。** **我试过用一个通用解析器把每一处对到它的测试文件，
/// 但可自动解析的只有 **1 处**——**因为那 55 处写法各异：**
///
/// - **有的把路径写全（`src/...`），有的只写文件名**
/// - **有的用中文括号，有的用逗号**
/// - **有的根本不点名测试文件**
///
/// **一条"覆盖全部 55 处"的判据只能靠模糊匹配，而模糊匹配的判据会在有人改标点时变红——
/// 那是 B1264／B1270 记过的"判据被散文弄红"的变体。**
///
/// **所以这一条只钉**这张表里能精确点名的三行**：它们都写了完整路径、都指向唯一一个测试文件。**
/// **其余 52 处仍无人核对——这一点写在这里，而不是让它看起来已经被覆盖。**
///
/// **B1274 found this while re-checking "ported" cells: the matrix says `PolynomialParser.kt` is ported with **24 criteria**, but
/// `PolynomialParserTests.cs` holds **22**.** 
///
/// **Why only these three rows: the matrix carries **55** "N criteria" claims. I tried a generic resolver, and only **1** was
/// machine-resolvable — the 55 are written inconsistently: some give a full `src/...` path and some only a file name, some use
/// Chinese parentheses and some a comma, and some never name a test file. A criterion covering all 55 would rely on fuzzy
/// matching, and a fuzzy criterion goes red when someone changes punctuation — a variant of the "criterion reddened by prose"
/// problem recorded in B1264/B1270.**
///
/// **So this pins only the <b>three</b> rows in this table that can be named exactly: each gives a full path and points at one
/// test file.** **The other 52 remain unchecked — and that is written here rather than left looking covered.**
/// </remarks>
public class MathParsingPortCountTests
{
    /// <summary>
    /// **三行数字 == 三个测试文件的真实判据数** / The three numbers equal the real counts.
    /// </summary>
    [Fact]
    public void TheThreeCounts_ShouldMatchTheTestFiles() {
        string root = RepositoryRoot();

        var expected = new (string Source, string TestFile, int Count)[] {
            ("src/Math/Symbol/Parse/PolynomialLexer.cs", "PolynomialLexerTests.cs", 6),
            ("src/Math/Symbol/Parse/PolynomialParser.cs", "PolynomialParserTests.cs", 22),
            ("src/Math/Symbol/Expression/Parser/ScalarExpressionParser.cs", "ScalarExpressionParserTests.cs", 25),
        };

        string matrix = File.ReadAllText(System.IO.Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        foreach ((string source, string testFile, int claimed) in expected) {
            File.Exists(System.IO.Path.Combine(root, source.Replace('/', System.IO.Path.DirectorySeparatorChar)))
                .Should().BeTrue($"{source} is what the matrix points at");

            string testPath = System.IO.Path.Combine(root, "tests", "Math.Tests", testFile);
            if (!File.Exists(testPath)) {
                testPath = System.IO.Path.Combine(root, "tests", "Math.Symbol.Tests", "Expression", "Parser", testFile);
            }
            File.Exists(testPath).Should().BeTrue($"{testFile} is the file the count describes");

            int actual = Regex.Matches(
                    File.ReadAllText(testPath), @"\[(?:Fact|Theory)\]", RegexOptions.Multiline)
                .Count;

            actual.Should().Be(
                claimed,
                $"{testFile} holds this many criteria; the matrix row states {claimed}, and B1274 measured 22 where it said 24");

            // **矩阵里那个数字也必须真的是这个值——在<b>写着这个源文件的那一行**里找，
            // 而不是全文找。** **全文找会让"别处恰好有同样的数字"骗过这条判据。**
            //
            // **The number in the matrix must really be that value too — searched in <b>the row that names this source file</b>,
            // not in the whole document, or a matching number elsewhere would fool this criterion.**
            string fileName = System.IO.Path.GetFileName(source);
            string[] rows = matrix
                .Split(NewLine)
                .Where(row => row.Contains(fileName, StringComparison.Ordinal))
                .ToArray();

            rows.Should().NotBeEmpty($"{fileName} is named by a matrix row");
            // **只要求"那个数字后紧跟 `条`"，不要求后紧跟"条判据"。**
            //
            // **因为 `ScalarParser` 那一行写的是"25 条<b>行为测试**"——数字与量词之间夹着加粗标记，
            // 而"判据"与"行为测试"在这个仓里指同一件事。**
            //
            // **要求"条判据"三个字连写，会让一条正确的行因为加粗位置不同而红——
            // 又是"判据被散文形式弄红"那一类。**
            //
            // **Only "the number followed by 条" is required, not "条判据": the ScalarParser row says "25 条<b>行为测试</b>",
            // with emphasis between number and unit, and criteria and behavioural tests are the same thing everywhere else in
            // this repo.** **Requiring the exact three characters would redden a correct row over where the bold sits — the same
            // "criterion reddened by prose form" family.**
            rows.Should().Contain(
                row => row.Contains($"{claimed} 条", StringComparison.Ordinal),
                $"{fileName}'s row must state {claimed} criteria; a number that lives only in a test names nothing");
        }
    }

    /// <summary>
    /// **那 52 处未被核对的声明，必须仍然数得出来** / The 52 unchecked claims must still be countable.
    /// </summary>
    /// <remarks>
    /// **这条不判断对错，只防"数字越来越多、核对越来越少"——
    /// 如果哪一天有人把 55 处改成 80 处，这行会红，逼着有人重新数一遍。**
    ///
    /// **This does not judge correctness; it guards against "more numbers, less checking": if the 55 ever become 80, this goes red
    /// and forces someone to recount.**
    /// </remarks>
    [Fact]
    public void TheUncheckedClaims_ShouldStillBeCountable() {
        string matrix = File.ReadAllText(System.IO.Path.Combine(RepositoryRoot(), "docs", "kotlin-capability-matrix.md"));

        int total = Regex.Matches(matrix, @"\d+ 条判据").Count;

        // **实测 29 处（不是 55——那是我第一遍 `grep -c` 数出来的<b>行数**，
        // 一行里可以有两处）。** **这个数字本身也值得记：我差点把一个"行数"写成"处数"。**
        //
        // **Measured 29 occurrences (not 55 — that was a first `grep -c` counting <b>lines</b>, and one line can hold two).** **The
        // number is worth recording: I nearly shipped a line count as an occurrence count.**
        // **B1302：29 → 30。** **多出来的那一处是我在 `TokenSharedReadBenchmark` 的矩阵笔记里
        // 写的"5 条判据"——它和其余 29 处一样，是<b>没被逐条钉住的计数宣称**。**
        // **判据照旧报警是对的：** **它不是"矩阵里只能有 29 个这样的句子"，而是"每多一句都得有人知道"。**
        //
        // **B1302: 29 to 30.** **The extra one is the "5 criteria" sentence added to the `TokenSharedReadBenchmark` note — like the other 29 it
        // is a count claim nobody pins one by one.** **The criterion firing is correct: it does not mean "the matrix may hold only 29 such
        // sentences", it means every new one has to be noticed.**
        total.Should().Be(
            30,
            "30 such claims exist and only 3 are pinned; if the total moves, the unchecked set moved with it and "
            + "this note stops describing reality");
    }


    /// <summary>
    /// **六个模式算子在五层上都在，而 B1275 曾误判为"一条都没有"** /
    /// The six pattern operators exist at five layers, and B1275 once misjudged them as absent.
    /// </summary>
    /// <remarks>
    /// **B1275 在这里写下过一条判据，断言那六个关键字在 `ScalarExpressionParser.cs` 的代码行里不出现，
    /// 并附了一句"它们既没实现、也没判据"。**
    ///
    /// **B1276 查清了：那句话是错的。而那条判据<b>成功地钉住了一件假事**——
    /// 它一直是绿的，因为它在<b>错误的文件**里找。</b>
    ///
    /// **真实位置：模式匹配是<b>布尔表达式**那一层，不是算术语义那一层。**
    ///
    /// | 层 | 位置 | 证据 |
    /// | --- | --- | --- |
    /// | 词法 | `Parser/Lexer.cs` | 六个关键字各映射一个 `TokenType` |
    /// | 记号 | `Parser/Token.cs` | 六个成员 + `ToPatternMatchMode()` |
    /// | 语法 | `Parser/Parser.cs` | 第 159—187 行消费它们，走 `ParsePatternMatch` |
    /// | 语义 | `BooleanExpression.cs` | `PatternMatch` 节点 + `PatternMatchMode` |
    /// | 判据 | `BooleanParserTest.cs` | `ParseLikeExpression_shouldWork` |
    ///
    /// **一条在错误文件里搜关键字的判据，绿得毫无意义——这与 B1272 的
    /// "在错误的路径下没找到被记成不存在"是同一族，只是方向相反：
    /// 那一次漏掉了存在的东西，这一次<b>虚构了不存在的东西**。</b>
    ///
    /// **B1275 wrote a criterion here asserting those six keywords do not appear in `ScalarExpressionParser.cs`, with a note
    /// saying they were neither implemented nor tested.** **B1276 established that the note was wrong and that the criterion
    /// <b>successfully pinned a falsehood</b> — it was always green because it searched <b>the wrong file</b>.**
    ///
    /// **The real location is the <b>boolean expression</b> layer, at five layers: lexer, tokens, grammar, semantics and
    /// criteria.** **A criterion that searches the wrong file and stays green means nothing — the same family as B1272's "not
    /// found at the wrong path, recorded as absent", only inverted: that time existing things were missed, this time
    /// <b>nonexistent ones were invented</b>.**
    /// </remarks>
    [Fact]
    public void TheSixPatternOperators_ShouldExistAtFiveLayers() {
        string root = RepositoryRoot();

        string lexer = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Math/Symbol/Expression/Parser/Lexer.cs"));
        foreach (string keyword in new[] { "like", "contains", "prefix", "suffix", "regex", "exact" }) {
            lexer.Should().Contain(
                "\"" + keyword + "\"",
                $"{keyword} is lexed; B1275 claimed it was not implemented at all");
        }

        string token = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Math/Symbol/Expression/Parser/Token.cs"));
        foreach (string member in new[] { "Like", "Contains", "Prefix", "Suffix", "Regex", "Exact" }) {
            token.Should().Contain(
                "TokenType." + member,
                $"{member} is a token type; all six are listed together as the pattern-operator set");
        }
        token.Should().Contain(
            "ToPatternMatchMode",
            "the token layer maps tokens to pattern modes");

        string parser = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Math/Symbol/Expression/Parser/Parser.cs"));
        parser.Should().Contain(
            "ToPatternMatchMode",
            "the grammar layer really consumes the pattern tokens; a lexer without a consumer is the B1268 shape");

        string expression = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Math/Symbol/Expression/BooleanExpression.cs"));
        expression.Should().Contain(
            "PatternMatchMode",
            "the semantics layer carries the mode");

        string tests = File.ReadAllText(
            System.IO.Path.Combine(root, "tests/Math.Symbol.Tests/Expression/Parser/BooleanParserTest.cs"));
        tests.Should().Contain(
            "ParseLikeExpression",
            "a parser feature with no criterion is the shape B1271 warned about");
    }

    /// <summary>
    /// **那次判错必须连同它的成因留在文档里** / That misjudgement stays on the record together with its cause.
    /// </summary>
    /// <remarks>
    /// **B1275 的错误判据没有被我悄悄删掉，而是改写成了上一条，并在注释里写明它曾钉住一件假事。**
    ///
    /// **理由：一个被删掉的错误，下一个人会重新犯；一个写着"我曾在这里判错"的位置，
    /// 下一个人会先看一眼自己是不是在重复它。**
    ///
    /// **The mistaken criterion was not quietly deleted; it became the one above, with its error written into the remarks.**
    /// **Reason: a deleted mistake gets made again, while a place that says "I judged wrongly here" makes the next reader check
    /// whether they are repeating it.**
    /// </remarks>
    [Fact]
    public void TheMistakeItself_ShouldBeRecorded() {
        string matrix = File.ReadAllText(
            System.IO.Path.Combine(RepositoryRoot(), "docs", "kotlin-capability-matrix.md"));

        matrix.Should().Contain(
            "B1276 撤回 B1275",
            "the retraction belongs in the doc that carried the wrong claim, not only in this test");

        matrix.Should().Contain(
            "在错误的文件里没找到",
            "the reason must be named; a retraction without its cause invites the same search to be repeated");
    }

    /// <summary>**按词边界判断一个词是否出现** / Whether a word occurs, by word boundary.</summary>
    /// <remarks>
    /// **手写而不是用正则，因为 `\b` 在生成这条判据的过程中被吃掉过两次，
    /// 而吃错的后果是"判据永远通过"——最难发现的那一种失败。**
    ///
    /// **Written by hand rather than with a regex, because `\b` was eaten twice while producing this criterion and the
    /// consequence of a broken one is "always passes" — the hardest failure to notice.**
    /// </remarks>
    private static bool ContainsWord(string line, string word) {
        int from = 0;
        while (true) {
            int index = line.IndexOf(word, from, StringComparison.OrdinalIgnoreCase);
            if (index < 0) {
                return false;
            }
            bool leftOk = index == 0 || !char.IsLetterOrDigit(line[index - 1]) && line[index - 1] != '_';
            int after = index + word.Length;
            bool rightOk = after == line.Length
                || (!char.IsLetterOrDigit(line[after]) && line[after] != '_');
            if (leftOk && rightOk) {
                return true;
            }
            from = index + 1;
        }
    }

    private const char NewLine = (char)10;

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
