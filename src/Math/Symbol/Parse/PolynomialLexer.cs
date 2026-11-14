#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Symbol.Parse;

/// <summary>多项式词法单元类型 / Token types for polynomial lexical analysis.</summary>
public enum PolynomialTokenType
{
    /// <summary>数字字面量 / Numeric literal.</summary>
    Number,
    /// <summary>标识符 / Identifier.</summary>
    Identifier,
    /// <summary>加号 / Plus.</summary>
    Plus,
    /// <summary>减号 / Minus.</summary>
    Minus,
    /// <summary>乘号 / Star.</summary>
    Star,
    /// <summary>幂符号 / Caret.</summary>
    Caret,
    /// <summary>左圆括号 / Left parenthesis.</summary>
    LeftParen,
    /// <summary>右圆括号 / Right parenthesis.</summary>
    RightParen,
    /// <summary>小于 / Less than.</summary>
    Less,
    /// <summary>小于等于 / Less than or equal.</summary>
    LessEqual,
    /// <summary>等于 / Equal.</summary>
    Equal,
    /// <summary>不等于 / Not equal.</summary>
    NotEqual,
    /// <summary>大于等于 / Greater than or equal.</summary>
    GreaterEqual,
    /// <summary>大于 / Greater than.</summary>
    Greater,
    /// <summary>输入结束 / End of input.</summary>
    End,
}

/// <summary>一个词法单元 / A token produced by the polynomial lexer.</summary>
/// <param name="Type">类型 / The token type.</param>
/// <param name="Text">原始文本 / The raw text.</param>
/// <param name="Position">起始位置 / The starting position.</param>
public readonly record struct PolynomialToken(PolynomialTokenType Type, string Text, int Position);

/// <summary>
/// 多项式词法分析器 / Polynomial expression lexer.
/// </summary>
/// <remarks>
/// 从固定基线 `12fdb587` 的 `symbol/parse/PolynomialLexer.kt`（281 行）移植。
/// **基线有三个行为容易在移植时丢掉，测试各钉一条：**
///
/// 1. **末尾一定有一个 `End` 单元。** 空输入不是空列表，是「`End` 一个」。
/// 2. **单独一个小数点成不了数字。** `.` 后面没有数字就是词法错误，不是 `0.`。
/// 3. **`!` 只有接 `=` 才合法。** 单独的 `!` 是错误，**因为它可能被写成"取反"而那个语义这里没有。**
///
/// Ported from `symbol/parse/PolynomialLexer.kt` (281 lines) at the pinned baseline.
/// **Three baseline behaviours are easy to lose in a port, and each has a test:**
/// **1. There is always a trailing `End` token.** Empty input is not an empty list but one `End`.
/// **2. A lone decimal point cannot start a number.** A `.` with no digit after it is a lexical error, not `0.`.
/// **3. `!` is only legal when followed by `=`.** A lone `!` is an error, **because it could be written as negation and that meaning does not exist here.**
/// </remarks>
public static class PolynomialLexer
{
    /// <summary>
    /// 把输入切成词法单元 / Decompose the input into tokens.
    /// </summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <returns>词法单元，或错误 / The tokens, or an error.</returns>
    /// <exception cref="ArgumentNullException">输入为空引用 / The input is a null reference.</exception>
    public static Result<List<PolynomialToken>, ErrorCode, Error<ErrorCode>> Lex(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tokens = new List<PolynomialToken>();
        int index = 0;

        while (true)
        {
            while (index < input.Length && char.IsWhiteSpace(input[index]))
            {
                index++;
            }

            if (index >= input.Length)
            {
                // **空输入也以一个 `End` 收尾，而不是空列表。**
                // **Empty input still ends with one `End`, not with an empty list.**
                tokens.Add(new PolynomialToken(PolynomialTokenType.End, string.Empty, index));
                return ParseResult.Ok(tokens);
            }

            int start = index;
            char current = input[index];

            if (char.IsDigit(current) || current == '.')
            {
                Result<PolynomialToken, ErrorCode, Error<ErrorCode>> number = ReadNumber(input, ref index, start);
                if (!number.IsOk)
                {
                    return ParseResult.Failed<List<PolynomialToken>>(
                        ErrorCode.IllegalArgument,
                        "多项式词法错误，位置 " + start + " / polynomial lexical error at " + start);
                }
                tokens.Add(number.Value);
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                tokens.Add(ReadIdentifier(input, ref index));
                continue;
            }

            switch (current)
            {
                case '+':
                    index++;
                    tokens.Add(new PolynomialToken(PolynomialTokenType.Plus, "+", start));
                    break;
                case '-':
                    index++;
                    tokens.Add(new PolynomialToken(PolynomialTokenType.Minus, "-", start));
                    break;
                case '*':
                    index++;
                    tokens.Add(new PolynomialToken(PolynomialTokenType.Star, "*", start));
                    break;
                case '^':
                    index++;
                    tokens.Add(new PolynomialToken(PolynomialTokenType.Caret, "^", start));
                    break;
                case '(':
                    index++;
                    tokens.Add(new PolynomialToken(PolynomialTokenType.LeftParen, "(", start));
                    break;
                case ')':
                    index++;
                    tokens.Add(new PolynomialToken(PolynomialTokenType.RightParen, ")", start));
                    break;
                case '<':
                    if (index + 1 < input.Length && input[index + 1] == '=')
                    {
                        index += 2;
                        tokens.Add(new PolynomialToken(PolynomialTokenType.LessEqual, "<=", start));
                    }
                    else
                    {
                        index++;
                        tokens.Add(new PolynomialToken(PolynomialTokenType.Less, "<", start));
                    }
                    break;
                case '>':
                    if (index + 1 < input.Length && input[index + 1] == '=')
                    {
                        index += 2;
                        tokens.Add(new PolynomialToken(PolynomialTokenType.GreaterEqual, ">=", start));
                    }
                    else
                    {
                        index++;
                        tokens.Add(new PolynomialToken(PolynomialTokenType.Greater, ">", start));
                    }
                    break;
                case '=':
                    index++;
                    tokens.Add(new PolynomialToken(PolynomialTokenType.Equal, "=", start));
                    break;
                case '!':
                    if (index + 1 < input.Length && input[index + 1] == '=')
                    {
                        index += 2;
                        tokens.Add(new PolynomialToken(PolynomialTokenType.NotEqual, "!=", start));
                    }
                    else
                    {
                        return ParseResult.Failed<List<PolynomialToken>>(
                            ErrorCode.IllegalArgument,
                            "多项式词法错误，位置 " + start + " / polynomial lexical error at " + start);
                    }
                    break;
                default:
                    return ParseResult.Failed<List<PolynomialToken>>(
                        ErrorCode.IllegalArgument,
                        "多项式词法错误，位置 " + start + " / polynomial lexical error at " + start);
            }
        }
    }

    /// <summary>读一个数字 / Read one number.</summary>
    private static Result<PolynomialToken, ErrorCode, Error<ErrorCode>> ReadNumber(string input, ref int index, int start)
    {
        bool hasDot = false;

        while (index < input.Length)
        {
            char c = input[index];
            if (char.IsDigit(c))
            {
                index++;
            }
            else if (c == '.' && !hasDot)
            {
                hasDot = true;
                index++;
                // **小数点后面必须还有数字。** `.` 单独立着是错误，不是 `0.`。
                // **A decimal point has to be followed by a digit.** A lone `.` is an error, not `0.`.
                if (index >= input.Length || !char.IsDigit(input[index]))
                {
                    return ParseResult.Failed<PolynomialToken>(
                        ErrorCode.IllegalArgument,
                        "数字格式错误，位置 " + start + " / invalid number at " + start);
                }
            }
            else
            {
                break;
            }
        }

        return ParseResult.Ok(new PolynomialToken(
            PolynomialTokenType.Number, input[start..index], start));
    }

    /// <summary>读一个标识符 / Read one identifier.</summary>
    private static PolynomialToken ReadIdentifier(string input, ref int index)
    {
        int start = index;
        while (index < input.Length && (char.IsLetterOrDigit(input[index]) || input[index] == '_'))
        {
            index++;
        }

        return new PolynomialToken(PolynomialTokenType.Identifier, input[start..index], start);
    }
}
