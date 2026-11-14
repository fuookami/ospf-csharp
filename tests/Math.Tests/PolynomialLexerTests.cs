#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Symbol.Parse;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Math.Tests;

/// <summary>
/// 多项式词法分析器 / The polynomial lexer.
/// </summary>
/// <remarks>
/// **移植自固定基线 `12fdb587` 的 `PolynomialLexer.kt`（281 行）。**
/// 三条行为各一条判据，**因为它们是"能编译但语义丢了"的那几种**：
/// 末尾 `End`、孤立小数点、孤立 `!`。
///
/// **Ported from `PolynomialLexer.kt` at the pinned baseline.** Three behaviours get a criterion each, **because these are the ones that compile fine with the meaning gone**: the trailing `End`, a lone decimal point,
/// a lone `!`.
/// </remarks>
public class PolynomialLexerTests
{
    private static List<PolynomialToken> Tokens(string input)
    {
        Result<List<PolynomialToken>, ErrorCode, Error<ErrorCode>> result = PolynomialLexer.Lex(input);

        // **不写裸 `IsOk`**：一个只断言"没失败"的结果，没说它到底是什么。
        // **这里连内容一起断言——每个输入唯一的合法切法就是调用方接下来要比的那串单元。**
        // **No bare `IsOk`**: a result asserted only as "did not fail" says nothing about what it is.
        // **This asserts the content too, and the caller then compares against the token list it expects.**
        result.Value.Should().NotBeEmpty("every input ends with at least one token, and the empty input ends with End");
        return result.Value;
    }

    /// <summary>**数字、标识符、运算符与括号** / Numbers, identifiers, operators and parentheses.</summary>
    [Fact]
    public void TheLexer_ShouldSplitAQuadratic()
    {
        List<PolynomialToken> tokens = Tokens("2x^2 + 3.5y - (z_1)");

        tokens.Select(token => token.Type).Should().Equal(
        [
            PolynomialTokenType.Number, PolynomialTokenType.Identifier, PolynomialTokenType.Caret,
            PolynomialTokenType.Number, PolynomialTokenType.Plus,
            PolynomialTokenType.Number, PolynomialTokenType.Identifier, PolynomialTokenType.Minus,
            PolynomialTokenType.LeftParen, PolynomialTokenType.Identifier, PolynomialTokenType.RightParen,
            PolynomialTokenType.End,
        ]);

        tokens[0].Text.Should().Be("2", "the number's raw text is part of the token");
        tokens[2].Text.Should().Be("^");
    }

    /// <summary>**位置是原文下标，不是序号** / Positions are source offsets, not ordinals.</summary>
    /// <remarks>
    /// **两者在简单输入上会碰巧相同**（`"2x"` 的 `x` 序号 1、下标 1）。
    /// **所以这条用带空格的输入**：跳过空白之后，序号与下标就分开了。
    ///
    /// **The two coincide on simple input** (`x` in `"2x"` is index 1 either way).
    /// **So this uses input with gaps**: after skipping whitespace the ordinal and the offset diverge.
    /// </remarks>
    [Fact]
    public void TheTokenPositions_ShouldBeSourceOffsets()
    {
        List<PolynomialToken> tokens = Tokens("  2   +   x");

        tokens.Should().HaveCount(4);
        tokens[0].Position.Should().Be(2, "the first token starts after two spaces");
        tokens[1].Position.Should().Be(6);
        tokens[2].Position.Should().Be(10);
        tokens[3].Position.Should().Be(11, "End sits at the end of the input");
    }

    /// <summary>**空输入也以一个 `End` 收尾** / Empty input still ends with one `End`.</summary>
    [Fact]
    public void TheEmptyInput_ShouldProduceOneEndToken()
    {
        Tokens(string.Empty).Should().ContainSingle()
            .Which.Type.Should().Be(PolynomialTokenType.End);
        Tokens("   ").Should().ContainSingle()
            .Which.Type.Should().Be(PolynomialTokenType.End, "whitespace is skipped, not tokenised");
    }

    /// <summary>**孤立的小数点不是数字** / A lone decimal point is not a number.</summary>
    /// <remarks>
    /// **它很容易被写成"容忍一下就过去了"**，而一旦容忍，`.5` 与 `5.` 的语义就要靠下游去猜。
    /// **基线把它判成词法错误，这里跟着判。**
    ///
    /// **It is tempting to let it slide**, and once it slides, the meaning of `.5` and `5.` has to be guessed downstream.
    /// **The baseline calls it a lexical error, and so does this.**
    /// </remarks>
    [Fact]
    public void TheLoneDecimalPoint_ShouldFail()
    {
        PolynomialLexer.Lex("x + .").IsOk.Should().BeFalse("a trailing point has no digits");
        PolynomialLexer.Lex(". + 1").IsOk.Should().BeFalse("a leading point has no digits");
        Tokens("1.5")[0].Text.Should().Be("1.5", "a point between digits is part of the number");
    }

    /// <summary>**`!` 只有接 `=` 才合法** / `!` is only legal before `=`.</summary>
    [Fact]
    public void TheLoneExclamation_ShouldFail()
    {
        Tokens("a != b").Should().HaveCount(4, "a, !=, b, End");
        Tokens("a != b")[1].Type.Should().Be(PolynomialTokenType.NotEqual, "!= is one token, not two");
        Tokens("a != b")[1].Text.Should().Be("!=");

        PolynomialLexer.Lex("!a").IsOk.Should().BeFalse("a lone ! could be read as negation, and there is no such meaning here");
    }

    /// <summary>**两个字符的运算符是一个单元** / Two-character operators are single tokens.</summary>
    [Fact]
    public void TheComparisonOperators_ShouldNotSplit()
    {
        List<PolynomialToken> tokens = Tokens("a <= b >= c < d > e = f");

        tokens.Where(token => token.Type is PolynomialTokenType.Less
            or PolynomialTokenType.LessEqual
            or PolynomialTokenType.Equal
            or PolynomialTokenType.GreaterEqual
            or PolynomialTokenType.Greater)
            .Select(token => token.Text)
            .Should().Equal(["<=", ">=", "<", ">", "="]);
    }
}
