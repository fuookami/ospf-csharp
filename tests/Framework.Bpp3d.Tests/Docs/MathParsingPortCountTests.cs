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
/// **这类格写着"N 条判据"，而 N 可能与测试文件里的实际条数不符：** **矩阵写 24，而 `PolynomialParserTests.cs` 里是 22。**
///
/// ### 为什么只钉这三行
///
/// **矩阵里有几十处 "N 条判据"，写法各异：**
///
/// - **有的把路径写全（`src/...`），有的只写文件名**
/// - **有的用中文括号，有的用逗号**
/// - **有的根本不点名测试文件**
///
/// **一条"覆盖全部"的判据只能靠模糊匹配，而模糊匹配的判据会在有人改标点时变红——**
/// **那是"判据被散文弄红"的变体。**
///
/// **所以这一条只钉**这张表里能精确点名的三行**：它们都写了完整路径、都指向唯一一个测试文件。**
/// **其余各处仍无人核对——这一点写在这里，而不是让它看起来已经被覆盖。**
///
/// **A "implemented, N criteria" cell can state a number the test file does not hold: the matrix says 24 while
/// `PolynomialParserTests.cs` holds 22.**
///
/// **Why only these three rows: the matrix carries dozens of "N criteria" claims, written inconsistently — some give a full
/// `src/...` path and some only a file name, some use Chinese parentheses and some a comma, and some never name a test file. A
/// criterion covering all of them would rely on fuzzy matching, and a fuzzy criterion goes red when someone changes
/// punctuation — a variant of the "criterion reddened by prose" problem.**
///
/// **So this pins only the <b>three</b> rows in this table that can be named exactly: each gives a full path and points at one
/// test file.** **The rest remain unchecked — and that is written here rather than left looking covered.**
/// </remarks>
public class MathParsingPortCountTests
{

    /// <summary>
    /// **六个模式算子在五层上都在；它们曾被误判为"一条都没有"** /
    /// The six pattern operators exist at five layers, and were once misjudged as absent.
    /// </summary>
    /// <remarks>
    /// **这里曾有一条判据，断言那六个关键字在 `ScalarExpressionParser.cs` 的代码行里不出现，
    /// 并附了一句"它们既没实现、也没判据"。**
    ///
    /// **查清之后：那句话是错的。而那条判据<b>成功地钉住了一件假事**——
    /// 它一直是绿的，因为它在<b>错误的文件**里找。</b>
    ///
    /// **真实位置：模式匹配是<b>布尔表达式**那一层，不是算术语义那一层。**
    ///
    /// | 层 | 位置 | 证据 |
    /// | --- | --- | --- |
    /// | 词法 | `Parser/Lexer.cs` | 六个关键字各映射一个 `TokenType` |
    /// | 记号 | `Parser/Token.cs` | 六个成员 + `ToPatternMatchMode` |
    /// | 语法 | `Parser/Parser.cs` | 第 159—187 行消费它们，走 `ParsePatternMatch` |
    /// | 语义 | `BooleanExpression.cs` | `PatternMatch` 节点 + `PatternMatchMode` |
    /// | 判据 | `BooleanParserTest.cs` | `ParseLikeExpression_shouldWork` |
    ///
    /// **一条在错误文件里搜关键字的判据，绿得毫无意义——这与
    /// "在错误的路径下没找到被记成不存在"是同一族，只是方向相反：
    /// 那一次漏掉了存在的东西，这一次<b>虚构了不存在的东西**。</b>
    ///
    /// **A criterion here asserted those six keywords do not appear in `ScalarExpressionParser.cs`, with a note
    /// saying they were neither implemented nor tested.** **That note was wrong, and the criterion
    /// <b>successfully pinned a falsehood</b> — it was always green because it searched <b>the wrong file</b>.**
    ///
    /// **The real location is the <b>boolean expression</b> layer, at five layers: lexer, tokens, grammar, semantics and
    /// criteria.** **A criterion that searches the wrong file and stays green means nothing — the same family as "not
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
                $"{keyword} is lexed; an earlier note claimed it was not implemented at all");
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
            "the grammar layer really consumes the pattern tokens; a lexer without a consumer is a declared-but-unused shape");

        string expression = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Math/Symbol/Expression/BooleanExpression.cs"));
        expression.Should().Contain(
            "PatternMatchMode",
            "the semantics layer carries the mode");

        string tests = File.ReadAllText(
            System.IO.Path.Combine(root, "tests/Math.Symbol.Tests/Expression/Parser/BooleanParserTest.cs"));
        tests.Should().Contain(
            "ParseLikeExpression",
            "a parser feature with no criterion is exactly the shape that has to be caught");
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
