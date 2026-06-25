#nullable enable

using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Expression.Parser;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Expression.Tests
{
    /// <summary>
    /// 词法分析器测试 / Lexer Tests.
    /// </summary>
    public class LexerTests
    {
        [Fact]
        public void TokenizeSimple_shouldProduceTokens()
        {
            var tokens = "age > 18".Tokenize();
            Assert.Equal(4, tokens.Count); // age, >, 18, EOF
            Assert.Equal(TokenType.Identifier, tokens[0].Type);
            Assert.Equal(TokenType.Gt, tokens[1].Type);
            Assert.Equal(TokenType.Number, tokens[2].Type);
            Assert.Equal(TokenType.Eof, tokens[3].Type);
        }

        [Fact]
        public void TokenizePath_shouldProduceSingleIdentifier()
        {
            var tokens = "user.address.city = 'NYC'".Tokenize();
            Assert.Equal(TokenType.Identifier, tokens[0].Type);
            Assert.Equal("user.address.city", tokens[0].Value);
        }

        [Fact]
        public void TokenizeKeywords_shouldRecognizeAll()
        {
            var tokens = "and or not in is like".Tokenize();
            Assert.Equal(TokenType.And, tokens[0].Type);
            Assert.Equal(TokenType.Or, tokens[1].Type);
            Assert.Equal(TokenType.Not, tokens[2].Type);
            Assert.Equal(TokenType.In, tokens[3].Type);
            Assert.Equal(TokenType.Is, tokens[4].Type);
            Assert.Equal(TokenType.Like, tokens[5].Type);
        }

        [Fact]
        public void TokenizeComparisonOps_shouldRecognize()
        {
            var tokens = "= <> != < <= > >=".Tokenize();
            Assert.Equal(TokenType.Eq, tokens[0].Type);
            Assert.Equal(TokenType.Ne, tokens[1].Type);
            Assert.Equal(TokenType.Ne, tokens[2].Type);
            Assert.Equal(TokenType.Lt, tokens[3].Type);
            Assert.Equal(TokenType.Le, tokens[4].Type);
            Assert.Equal(TokenType.Gt, tokens[5].Type);
            Assert.Equal(TokenType.Ge, tokens[6].Type);
        }
    }

    /// <summary>
    /// 解析器测试 / Parser Tests.
    /// </summary>
    public class ParserTests
    {
        [Fact]
        public void ParseSimpleComparison_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("age > 18");
            Assert.True(result.IsOk);
            Assert.IsType<Comparison<object>>(result.Value);
        }

        [Fact]
        public void ParseStringComparison_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("name = 'John'");
            Assert.True(result.IsOk);
            Assert.IsType<Comparison<object>>(result.Value);
        }

        [Fact]
        public void ParseAndExpression_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("age > 18 and status = 'active'");
            Assert.True(result.IsOk);
            Assert.IsType<AndExpression>(result.Value);
        }

        [Fact]
        public void ParseOrExpression_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("age > 18 or status = 'active'");
            Assert.True(result.IsOk);
            Assert.IsType<OrExpression>(result.Value);
        }

        [Fact]
        public void ParseNotExpression_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("not age > 18");
            Assert.True(result.IsOk);
            Assert.IsType<NotExpression>(result.Value);
        }

        [Fact]
        public void ParseInExpression_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("status in ('active', 'pending')");
            Assert.True(result.IsOk);
            Assert.IsType<InExpression<object>>(result.Value);
        }

        [Fact]
        public void ParseNotInExpression_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("status not in ('deleted')");
            Assert.True(result.IsOk);
            Assert.IsType<InExpression<object>>(result.Value);
        }

        [Fact]
        public void ParseIsNull_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("email is null");
            Assert.True(result.IsOk);
            Assert.IsType<NullCheck>(result.Value);
        }

        [Fact]
        public void ParseIsNotNull_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("email is not null");
            Assert.True(result.IsOk);
            Assert.IsType<NullCheck>(result.Value);
        }

        [Fact]
        public void ParseLikeExpression_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("name like 'John%'");
            Assert.True(result.IsOk);
            Assert.IsType<PatternMatch<object>>(result.Value);
        }

        [Fact]
        public void ParseComplexExpression_shouldWork()
        {
            var result = BooleanExpressionParser.Parse("(age > 18 and status = 'active') or role = 'admin'");
            Assert.True(result.IsOk);
            Assert.IsType<OrExpression>(result.Value);
        }

        [Fact]
        public void OperatorPrecedence_andBeforeOr()
        {
            var result = BooleanExpressionParser.Parse("a > 1 or b < 2 and c = 3");
            Assert.True(result.IsOk);
            // Should parse as: a > 1 or (b < 2 and c = 3)
            Assert.IsType<OrExpression>(result.Value);
            var orExpr = (OrExpression)result.Value;
            Assert.Equal(2, orExpr.Operands.Count);
            Assert.IsType<AndExpression>(orExpr.Operands[1]);
        }

        [Fact]
        public void ParseFlattenedAnd_shouldMerge()
        {
            var result = BooleanExpressionParser.Parse("a > 1 and b < 2 and c = 3");
            Assert.True(result.IsOk);
            Assert.IsType<AndExpression>(result.Value);
            Assert.Equal(3, ((AndExpression)result.Value).Operands.Count);
        }
    }

    /// <summary>
    /// 错误处理测试 / Error Handling Tests.
    /// </summary>
    public class ErrorHandlingTests
    {
        [Fact]
        public void ParseInvalid_shouldReturnFailed()
        {
            var result = BooleanExpressionParser.Parse(">>>");
            Assert.True(result.IsFailed);
        }

        [Fact]
        public void ParseEmpty_shouldReturnFailed()
        {
            var result = BooleanExpressionParser.Parse("");
            Assert.True(result.IsFailed);
        }

        [Fact]
        public void ParseOrNull_shouldReturnNullOnFailure()
        {
            Assert.Null(BooleanExpressionParser.ParseOrNull(">>>"));
            Assert.Null(BooleanExpressionParser.ParseOrNull(""));
        }

        [Fact]
        public void ParseOrNull_shouldReturnExpressionOnSuccess()
        {
            Assert.NotNull(BooleanExpressionParser.ParseOrNull("age > 18"));
        }
    }
}
