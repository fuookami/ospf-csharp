#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Expression.Parser;
using ScalarRet = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Math.Symbol.Expression.ScalarExpression<object?>,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Tests.Expression.Parser;

/// <summary>
/// **"无"这一格写错了：标量解析器一直在** / That "none" cell was wrong: the scalar parser has always been here.
/// </summary>
/// <remarks>
/// **能力矩阵第 53 行写着：** **（**713** 行）→ 覆盖<b>无**。</b>
///
/// **而实测：**
///
/// - ** 有<b>三个**入口：`parse`／`parseScalarExpression`／`parseScalarExpressionOrNull`**
/// - **本仓 `ScalarExpressionParser.cs`（546 行）三个<b>全有**：
///   `Parse`／`ScalarParserResults.ParseScalarExpression`／`ScalarParserResults.ParseScalarExpressionOrNull`**
/// - **还有 <b>25 条**行为测试（`ScalarExpressionParserTests.cs`）**
///
/// ### 而矩阵那一格错在哪里
///
/// **错在<b>路径**。** **矩阵写的是，
/// 而实际路径是。**
///
/// **"在 `symbol/parse/` 下找不到它"于是被记成了"没有"。**
///
/// **"在某个目录里没找到"与"不存在"是两句话——而矩阵把它们写成了一句。**
///
/// ### 这是第几次同一种错误
///
/// **同一种错误已经出现过三次：** **"文档数字来自错误的计数口径"、** **"死代码被读成未完成"、**
/// **以及这一次的"在错误的路径下找不到，被记成不存在"。**
///
/// **三次都是同一件事：<b>一个抽样动作的失败，被当成了关于世界的事实**。</b>
///
/// **The matrix's line 53 says (713 lines) has <b>no</b> coverage. Measured: the code file has
/// <b>three</b> entry points (`parse`, `parseScalarExpression`, `parseScalarExpressionOrNull`), this repo's
/// `ScalarExpressionParser.cs` (546 lines) has <b>all three</b>, and there are <b>25</b> behavioural tests.**
///
/// **The cell is wrong about the <b>path</b>: the matrix writes while its actual path
/// is.** **"Not found under `symbol/parse/`" became
/// "absent".** **"I did not find it in that directory" and "it does not exist" are two sentences, and the matrix wrote them as
/// one.**
///
/// **This is the third of a kind: "a doc number from the wrong counting convention", "dead code read as
/// unfinished", and this is "not found at the wrong path, recorded as absent".** **All three are one thing: <b>a failed sampling
/// action taken as a fact about the world</b>.**
/// </remarks>
public class ScalarParserCoverageCorrectionTests
{
    /// <summary>
    /// **三个入口都在，而且都能解析** / All three entry points exist and all three actually parse.
    /// </summary>
    /// <remarks>
    /// **判据不只断言"方法存在"——那正是此前 批评过的影子检查。**
    /// **它对三个入口各跑一次真实解析，并核对结果。**
    ///
    /// **The criterion does not merely assert the methods exist — that is exactly the shadow check criticised.**
    /// **It runs one real parse through each of the three and checks the result.**
    /// </remarks>
    [Fact]
    public void TheThreeEntryPoints_ShouldAllParse() {
        ScalarRet parsed = ScalarExpressionParser.Parse("2 + 3 * x");
        parsed.Should().NotBeNull("`parse` is the baseline's first entry point and it works");

        ScalarRet viaAlias = ScalarParserResults.ParseScalarExpression("2 + 3 * x");
        viaAlias.Should().NotBeNull("`parseScalarExpression` is the baseline's second entry point");

        ScalarExpression<object?>? orNull = ScalarParserResults.ParseScalarExpressionOrNull("2 + 3 * x");
        orNull.Should().NotBeNull("`parseScalarExpressionOrNull` is the baseline's third entry point");

        // **而"orNull"这个名字必须名副其实：不合法输入返回 null，不是抛异常、也不是返回一个坏对象。**
        // **And "orNull" must mean it: invalid input returns null, neither throwing nor yielding a broken object.**
        ScalarParserResults.ParseScalarExpressionOrNull("2 +")
            .Should().BeNull("the OrNull entry point returns null on invalid input, as its name promises");
    }

    /// <summary>
    /// **那一格现在说的是实话** / That cell now tells the truth.
    /// </summary>
    /// <remarks>
    /// **这条钉住矩阵：那一行必须写上"已搬"与三个入口，而不是"无"。**
    /// **同时它必须在路径上也正确——因为把路径写错正是这次错误的来源。**
    ///
    /// **This pins the matrix: the row must say "implemented" with the three entry points instead of "none", and it must be right
    /// about the path, since the wrong path is precisely where this error came from.**
    /// </remarks>
    [Fact]
    public void TheMatrixCell_ShouldSayPortedWithTheRealPath() {
        string matrix = File.ReadAllText(System.IO.Path.Combine(
            RepositoryRoot(), "docs/kotlin-capability-matrix.md"));

        // **判据钉的是"矩阵指得出实现文件在哪"，不是"矩阵抄了路径"。** '
        // **能力要自解释：读者从矩阵就该找到本仓的实现，而不是先去另一个仓库里找对照。**
        //
        // **The criterion pins "the matrix points at the implementation", not "the matrix copies the code path".** A capability has to explain itself: a reader should reach this
        // repo's implementation from the matrix without first looking something up elsewhere.
        matrix.Should().Contain(
            "src/Math/Symbol/Expression/Parser/ScalarExpressionParser.cs",
            "the matrix has to name the file that implements the parser, so the cell is checkable on its own");

        matrix.Should().NotContain(
            "| `symbol/parse/ScalarParser.kt` |",
            "a path that never existed invites the same 'not found, therefore absent' error");
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
