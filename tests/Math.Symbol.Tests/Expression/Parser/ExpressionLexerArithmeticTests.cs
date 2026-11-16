#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Symbol.Expression.Parser;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Tests.Expression.Parser;

/// <summary>
/// 表达式词法器补齐的算术/条件/逻辑运算符 / The arithmetic, conditional and logical tokens added to the expression lexer.
/// </summary>
/// <remarks>
/// **这批的起点是一次误判。** 我用文件名差集看到 `ScalarParser` 不在，**就以为整条表达式解析栈都不在**，
/// 于是照着基线写了一份 `Token`/`Lexer`——**而 `Token.cs`/`Lexer.cs`/`Parser.cs` 一直就躺在同一个目录里。**
///
/// **真正缺的是标量那一层，以及词法器里的算术运算符。** 这批补后者。
///
/// ** starts from a misjudgement.** The filename diff said `ScalarParser` was absent, **and I took that to mean the whole expression stack was absent**,
/// so I wrote a `Token`/`Lexer` from the baseline — **while `Token.cs`/`Lexer.cs`/`Parser.cs` had been sitting in that same directory all along.**
/// **What is actually missing is the scalar layer, plus the arithmetic operators in the lexer.** adds the latter.
/// </remarks>
public class ExpressionLexerArithmeticTests
{
    private static TokenType[] Types(string input) =>
        input.Tokenize().Select(token => token.Type).ToArray();

    /// <summary>**六个算术符号各自成一个单元** / The six arithmetic symbols each become one token.</summary>
    [Fact]
    public void TheArithmeticSymbols_ShouldTokenise()
    {
        Types("a + b - c * d / e % f ^ g").Should().Equal(
        [
            TokenType.Identifier, TokenType.Plus,
            TokenType.Identifier, TokenType.Minus,
            TokenType.Identifier, TokenType.Star,
            TokenType.Identifier, TokenType.Slash,
            TokenType.Identifier, TokenType.Percent,
            TokenType.Identifier, TokenType.Caret,
            TokenType.Identifier, TokenType.Eof,
        ]);
    }

    /// <summary>**`**` 是一个单元，不是两个 `*`** / `**` is one token, not two stars.</summary>
    /// <remarks>
    /// **拆成两个 `Star` 之后，`2**3` 与 `2* *3` 在文法上就没区别了，而幂和连乘不是一回事。**
    ///
    /// **Split into two `Star`s, `2**3` and `2* *3` become grammatically identical, and exponentiation is not chained multiplication.**
    /// </remarks>
    [Fact]
    public void TheDoubleStar_ShouldNotSplit() =>
        Types("2 ** 3").Should().Equal(
        [
            TokenType.Number, TokenType.DoubleStar,
            TokenType.Number, TokenType.Eof,
        ]);

    /// <summary>**单个 `&` 或 `|` 认不出来，两个才是逻辑运算符** / A lone `&amp;` or `|` is unrecognised; two make the operator.</summary>
    /// <remarks>
    /// **认下来会让它走到文法层才被拒，那时的报错位置已经不是真位置了。**
    ///
    /// **Recognising it would let it reach the grammar layer before being refused, and the reported position would no longer be the real one.**
    /// </remarks>
    [Fact]
    public void TheLoneAmpersandOrPipe_ShouldBeUnknown()
    {
        Types("a & b").Should().Equal(
        [
            TokenType.Identifier, TokenType.Unknown,
            TokenType.Identifier, TokenType.Eof,
        ]);
        Types("a && b").Should().Contain(TokenType.AmpersandAmpersand);
        Types("a || b").Should().Contain(TokenType.PipePipe);
        Types("a | b").Should().Contain(TokenType.Unknown);
    }

    /// <summary>**`?` 与 `:` 是条件表达式的两个符号** / `?` and `:` are the two conditional symbols.</summary>
    [Fact]
    public void TheConditionalSymbols_ShouldTokenise() =>
        Types("a ? b : c").Should().ContainInOrder(
            TokenType.Question, TokenType.Colon);

    /// <summary>**`if`/`then`/`else`/`fi` 是关键字，不是标识符** / They are keywords, not identifiers.</summary>
    /// <remarks>
    /// **关键字折进类型之后，`if` 就不能再当变量名用**——**这正是关键字的意义，也是这条判据守的东西。**
    ///
    /// **Once a keyword folds into a type, `if` can no longer be used as a variable name** — **which is what a keyword means and what this criterion guards.**
    /// </remarks>
    [Fact]
    public void TheConditionalKeywords_ShouldNotBeIdentifiers()
    {
        TokenType[] types = Types("if a then b else c fi");

        types.Should().Contain(TokenType.If);
        types.Should().Contain(TokenType.Then);
        types.Should().Contain(TokenType.Else);
        types.Should().Contain(TokenType.Fi);
        types.Should().NotContain(TokenType.Unknown, "all four are known keywords");
    }
}
