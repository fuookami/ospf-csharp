#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol.Parse;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using RetBool = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Math.Symbol.Expression.BooleanExpression, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using RetScalar = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Math.Symbol.Expression.ScalarExpression<object?>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using RetPath = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Math.Symbol.Expression.PropertyPath, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using RetToken = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Math.Symbol.Expression.Parser.Token, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Math.Symbol.Expression.Parser
{
    /// <summary>
    /// 布尔表达式解析器 / Boolean Expression Parser (recursive descent).
    /// 优先级 / precedence (high→low): not, parens → and → or.
    /// 解析失败返回 Result，不抛异常 / parse failures return Result, never throw.
    /// </summary>
    public sealed class Parser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private readonly string? _input;
        private int _position;

        public Parser(IReadOnlyList<Token> tokens, string? input = null)
        {
            _tokens = tokens;
            _input = input;
            _position = 0;
        }

        /// <summary>解析布尔表达式 / Parse boolean expression.</summary>
        public RetBool Parse()
        {
            if (_tokens.Count == 0 || (_tokens.Count == 1 && _tokens[0].Type == TokenType.Eof))
                return ParseFailedBool("Empty expression");

            var orResult = ParseOrExpression();
            if (orResult.IsFailed) return orResult;
            var expr = orResult.Value;

            if (CurrentToken().Type != TokenType.Eof)
                return ParseFailedBool(
                    $"Unexpected token: {CurrentToken().Value}",
                    CurrentToken().Position);

            return Results.Ok(expr);
        }

        private RetBool ParseOrExpression()
        {
            var leftResult = ParseAndExpression();
            if (leftResult.IsFailed) return leftResult;
            var left = leftResult.Value;

            while (CurrentToken().Type == TokenType.Or)
            {
                Advance();
                var rightResult = ParseAndExpression();
                if (rightResult.IsFailed) return rightResult;
                left = MergeOr(left, rightResult.Value);
            }

            return Results.Ok(left);
        }

        private RetBool ParseAndExpression()
        {
            var leftResult = ParseNotExpression();
            if (leftResult.IsFailed) return leftResult;
            var left = leftResult.Value;

            while (CurrentToken().Type == TokenType.And)
            {
                Advance();
                var rightResult = ParseNotExpression();
                if (rightResult.IsFailed) return rightResult;
                left = MergeAnd(left, rightResult.Value);
            }

            return Results.Ok(left);
        }

        private RetBool ParseNotExpression()
        {
            if (CurrentToken().Type == TokenType.Not)
            {
                Advance();
                var operandResult = ParseNotExpression();
                if (operandResult.IsFailed) return operandResult;
                return Results.Ok((BooleanExpression)new NotExpression(operandResult.Value));
            }
            return ParsePrimaryExpression();
        }

        private RetBool ParsePrimaryExpression()
        {
            var tokenType = CurrentToken().Type;
            if (tokenType == TokenType.LParen)
                return ParseParenthesized();
            if (tokenType == TokenType.True)
            {
                Advance();
                return Results.Ok((BooleanExpression)BooleanConstant.True());
            }
            if (tokenType == TokenType.False)
            {
                Advance();
                return Results.Ok((BooleanExpression)BooleanConstant.False());
            }
            if (tokenType == TokenType.Identifier)
                return ParsePathExpression();
            return ParseFailedBool($"Unexpected token: {CurrentToken().Value}", CurrentToken().Position);
        }

        private RetBool ParseParenthesized()
        {
            Advance(); // skip (
            var exprResult = ParseOrExpression();
            if (exprResult.IsFailed) return exprResult;

            var expectResult = Expect(TokenType.RParen, "Expected ')'");
            if (expectResult.IsFailed) return PropagateError<Token, BooleanExpression>(expectResult);

            return Results.Ok(exprResult.Value);
        }

        private RetBool ParsePathExpression()
        {
            var pathResult = ParsePath();
            if (pathResult.IsFailed) return PropagateError<PropertyPath, BooleanExpression>(pathResult);
            var path = pathResult.Value;

            if (CurrentToken().Type == TokenType.Is)
                return ParseNullCheck(path);

            if (CurrentToken().Type == TokenType.Not)
            {
                Advance();
                if (CurrentToken().Type == TokenType.In)
                {
                    Advance();
                    return ParseInExpression(path, negated: true);
                }
                if (CurrentToken().IsPatternOperator())
                {
                    var mode = CurrentToken().Type.ToPatternMatchMode();
                    if (mode is null)
                        return ParseFailedBool("Expected pattern operator after 'not'", CurrentToken().Position);
                    Advance();
                    return ParsePatternMatch(path, mode.Value, negated: true);
                }
                return ParseFailedBool("Expected 'in' or pattern operator after 'not'", CurrentToken().Position);
            }

            if (CurrentToken().Type == TokenType.In)
            {
                Advance();
                return ParseInExpression(path, negated: false);
            }

            if (CurrentToken().IsPatternOperator())
            {
                var mode = CurrentToken().Type.ToPatternMatchMode();
                if (mode is null)
                    return ParseFailedBool("Expected pattern operator", CurrentToken().Position);
                Advance();
                return ParsePatternMatch(path, mode.Value, negated: false);
            }

            if (CurrentToken().IsComparisonOperator())
                return ParseComparison(path);

            return ParseFailedBool(
                $"Expected comparison operator, 'in', 'is' after '{path.Value}'",
                CurrentToken().Position);
        }

        private RetBool ParseNullCheck(PropertyPath path)
        {
            Advance(); // skip 'is'
            var notNull = false;
            if (CurrentToken().Type == TokenType.Not)
            {
                Advance();
                notNull = true;
            }

            var expectResult = Expect(TokenType.Null, "Expected 'null' after 'is'");
            if (expectResult.IsFailed) return PropagateError<Token, BooleanExpression>(expectResult);

            return Results.Ok((BooleanExpression)new NullCheck(path, notNull ? NullCheckType.IsNotNull : NullCheckType.IsNull));
        }

        private RetBool ParseInExpression(PropertyPath path, bool negated)
        {
            var expectLp = Expect(TokenType.LParen, "Expected '(' after 'in'");
            if (expectLp.IsFailed) return PropagateError<Token, BooleanExpression>(expectLp);

            var candidates = new List<ScalarExpression<object?>>();
            while (true)
            {
                var candResult = ParseScalarValue();
                if (candResult.IsFailed) return PropagateError<ScalarExpression<object?>, BooleanExpression>(candResult);
                candidates.Add(candResult.Value);
                if (CurrentToken().Type != TokenType.Comma) break;
                Advance();
            }

            var expectRp = Expect(TokenType.RParen, "Expected ')' after 'in' list");
            if (expectRp.IsFailed) return PropagateError<Token, BooleanExpression>(expectRp);

            return Results.Ok((BooleanExpression)new InExpression<object?>(new ScalarReference<object?>(path), candidates, negated));
        }

        private RetBool ParsePatternMatch(PropertyPath path, PatternMatchMode mode, bool negated)
        {
            var patternResult = ParseScalarValue();
            if (patternResult.IsFailed) return PropagateError<ScalarExpression<object?>, BooleanExpression>(patternResult);
            return Results.Ok((BooleanExpression)new PatternMatch<object?>(new ScalarReference<object?>(path), patternResult.Value, mode, negated));
        }

        private RetBool ParseComparison(PropertyPath leftPath)
        {
            var op = CurrentToken().Type.ToComparisonOperator();
            if (op is null)
                return ParseFailedBool("Expected comparison operator", CurrentToken().Position);
            Advance();

            var rightResult = ParseScalarValue();
            if (rightResult.IsFailed) return PropagateError<ScalarExpression<object?>, BooleanExpression>(rightResult);

            return Results.Ok((BooleanExpression)new Comparison<object?>(op.Value, new ScalarReference<object?>(leftPath), rightResult.Value));
        }

        private RetScalar ParseScalarValue()
        {
            var tt = CurrentToken().Type;

            if (tt == TokenType.String)
            {
                var v = CurrentToken().Value;
                Advance();
                return Results.Ok((ScalarExpression<object?>)new ScalarConstant<object?>(v));
            }
            if (tt == TokenType.Number)
            {
                var v = CurrentToken().Value;
                Advance();
                var parsed = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d);
                return Results.Ok((ScalarExpression<object?>)new ScalarConstant<object?>(parsed ? d : v));
            }
            if (tt == TokenType.True)
            {
                Advance();
                return Results.Ok((ScalarExpression<object?>)new ScalarConstant<object?>(true));
            }
            if (tt == TokenType.False)
            {
                Advance();
                return Results.Ok((ScalarExpression<object?>)new ScalarConstant<object?>(false));
            }
            if (tt == TokenType.Null)
            {
                Advance();
                return Results.Ok((ScalarExpression<object?>)new ScalarConstant<object?>(null));
            }
            if (tt == TokenType.Identifier)
            {
                var pathResult = ParsePath();
                if (pathResult.IsFailed) return PropagateError<PropertyPath, ScalarExpression<object?>>(pathResult);
                return Results.Ok((ScalarExpression<object?>)new ScalarReference<object?>(pathResult.Value));
            }
            return ParseFailedScalar($"Expected scalar value, got: {CurrentToken().Value}", CurrentToken().Position);
        }

        private RetPath ParsePath()
        {
            if (CurrentToken().Type != TokenType.Identifier)
                return ParseFailedPath("Expected identifier", CurrentToken().Position);

            var path = PropertyPath.Parse(CurrentToken().Value);
            Advance();
            return Results.Ok(path);
        }

        private Token CurrentToken() =>
            _position < _tokens.Count ? _tokens[_position] : Token.Eof(_position);

        private Token Advance()
        {
            var token = CurrentToken();
            _position++;
            return token;
        }

        private RetToken Expect(TokenType type, string message)
        {
            if (CurrentToken().Type != type)
                return ParseFailedToken(message, CurrentToken().Position);
            return Results.Ok(Advance());
        }

        private OrExpression MergeOr(BooleanExpression left, BooleanExpression right)
        {
            var operands = new List<BooleanExpression>();
            if (left is OrExpression lo) operands.AddRange(lo.Operands); else operands.Add(left);
            if (right is OrExpression ro) operands.AddRange(ro.Operands); else operands.Add(right);
            return new OrExpression(operands);
        }

        private AndExpression MergeAnd(BooleanExpression left, BooleanExpression right)
        {
            var operands = new List<BooleanExpression>();
            if (left is AndExpression la) operands.AddRange(la.Operands); else operands.Add(left);
            if (right is AndExpression ra) operands.AddRange(ra.Operands); else operands.Add(right);
            return new AndExpression(operands);
        }

        private Result<T, ErrorCode, Error<ErrorCode>> ParseFailed<T>(string message, int position = 0)
        {
            var issue = new ParseIssue(ParseIssueType.Syntax, message, _input, position);
            return new Failed<T, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, $"{message} at position {position}", issue);
        }

        // Type-specific wrappers to avoid generic inference issues
        private RetBool ParseFailedBool(string message, int position = 0) => ParseFailed<BooleanExpression>(message, position);
        private RetScalar ParseFailedScalar(string message, int position = 0) => ParseFailed<ScalarExpression<object?>>(message, position);
        private RetPath ParseFailedPath(string message, int position = 0) => ParseFailed<PropertyPath>(message, position);
        private RetToken ParseFailedToken(string message, int position = 0) => ParseFailed<Token>(message, position);

        // Extract error from a failed result for propagation
        private static Result<TResult, ErrorCode, Error<ErrorCode>> PropagateError<T, TResult>(
            Result<T, ErrorCode, Error<ErrorCode>> failed)
        {
            var f = (Failed<T, ErrorCode, Error<ErrorCode>>)failed;
            return new Failed<TResult, ErrorCode, Error<ErrorCode>>(f.Error);
        }
    }

    /// <summary>布尔表达式解析入口 / Boolean expression parse entry.</summary>
    public static class BooleanExpressionParser
    {
        /// <summary>解析布尔表达式字符串 / Parse boolean expression string.</summary>
        public static RetBool Parse(string input)
        {
            var tokens = new Lexer(input).Tokenize();
            return new Parser(tokens, input).Parse();
        }

        /// <summary>尝试解析，失败返回 null / Try parse, null on failure.</summary>
        public static BooleanExpression? ParseOrNull(string input)
        {
            var result = Parse(input);
            return result.IsOk ? result.Value : null;
        }
    }
}
