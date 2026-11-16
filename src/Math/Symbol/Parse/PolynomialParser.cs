#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Math.Operator;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Fuookami.Ospf.Math.Symbol.Parse;

/// <summary>一个已经解析出来的项：系数加各符号的幂次 / One parsed term: a coefficient and per-symbol powers.</summary>
/// <param name="Coefficient">系数 / The coefficient.</param>
/// <param name="Powers">符号名到幂次 / Symbol name to exponent.</param>
public sealed record ParsedTerm<T>(T Coefficient, IReadOnlyDictionary<string, int> Powers);

/// <summary>一个已经解析出来的多项式：若干项加常数项 / A parsed polynomial: terms plus a constant.</summary>
/// <param name="Terms">非常数项 / The non-constant terms.</param>
/// <param name="Constant">常数项 / The constant term.</param>
public sealed record ParsedPolynomial<T>(IReadOnlyList<ParsedTerm<T>> Terms, T Constant);

/// <summary>一个已经解析出来的不等式 / A parsed inequality.</summary>
/// <param name="Lhs">左边 / The left-hand side.</param>
/// <param name="Rhs">右边 / The right-hand side.</param>
/// <param name="Comparison">比较符 / The comparison operator.</param>
public sealed record ParsedInequality<T>(
    ParsedPolynomial<T> Lhs,
    ParsedPolynomial<T> Rhs,
    Comparison Comparison);

/// <summary>把解析结果落到规范多项式上 / Lower a parse result onto the canonical polynomial.</summary>
public static class ParsedPolynomialLowering
{
    /// <summary>
    /// 落到 `CanonicalPolynomial` / Lower onto `CanonicalPolynomial`.
    /// </summary>
    /// <param name="parsed">解析结果 / The parse result.</param>
    /// <typeparam name="T">数值类型 / The numeric type.</typeparam>
    /// <returns>规范多项式 / The canonical polynomial.</returns>
    /// <remarks>
    /// **记号是"按名字"的，所以这里把名字包成 `NamedSymbol`。**
    /// **这不是权宜：** 这一步也是 `symbolOf(name)`，**名字是唯一身份，来源不参与。**
    /// **唯一的差别是 C# 之前没有任何按名字标识的符号类型，`SymbolOfSerializedIdentifier` 于是对所有名字抛异常。**
    ///
    /// **Terms are name-keyed, so this wraps each name in a `NamedSymbol`.** **That is not a stopgap:** the code does `symbolOf(name)` at this same step, **and the name is the whole identity; provenance takes no part.**
    /// **The only difference is that C# had no name-identified symbol at all, so `SymbolOfSerializedIdentifier` threw for every name.**
    /// </remarks>
    public static CanonicalPolynomial<T> ToCanonicalPolynomial<T>(this ParsedPolynomial<T> parsed)
        where T : struct, IRing<T>
    {
        ArgumentNullException.ThrowIfNull(parsed);

        CanonicalMonomial<T>[] monomials = parsed.Terms
            .Select(term => new CanonicalMonomial<T>(
                term.Coefficient,
                (IReadOnlyDictionary<ISymbol, int>)term.Powers.ToDictionary(
                    pair => (ISymbol)NamedSymbol.Of(pair.Key),
                    pair => pair.Value)))
            .ToArray();

        return new CanonicalPolynomial<T>(monomials, parsed.Constant);
    }
}

/// <summary>
/// 多项式语法分析器 / The polynomial grammar parser.
/// </summary>
/// <remarks>
/// 从固定基线 `12fdb587` 的（622 行）移植**语法核心**。
/// 一层还负责把它落到 `CanonicalPolynomial`/`LinearInequality` 上，
/// **而 C# 侧的符号是 `ISymbol` 接口族、没有个 `Symbol(id)` 记录**，
/// 所以这里只做**按名字记幂次**的中间结果——**难点（优先级、结合性、一元负号、幂）全在这里，而它不需要符号体系就能测。**
///
/// Ports the **grammar core** of (622 lines). The code layer also lowers onto `CanonicalPolynomial` and the inequality types,
/// **and C# has an `ISymbol` interface family instead of its `Symbol(id)` record**, so stops at symbol-name powers —
/// **the hard part (precedence, associativity, unary minus, exponentiation) is all here, and it is testable without the symbol system.**
///
/// 优先级：`expression`（`+`/`-`，左结合）→ `term`（`*`，左结合）→ `power`（`^`）→ `factor`（一元负号、数字、标识符、括号）。
/// **`^` 不递归**：指数后面只接一个数字，**所以 `2^3^2` 是语法错误而不是右结合。**
///
/// Precedence: `expression` (`+`/`-`, left) → `term` (`*`, left) → `power` (`^`) → `factor` (unary minus, number, identifier, parens).
/// **`^` does not recurse**: the code puts a single number after the exponent, **so `2^3^2` is a syntax error rather than right-associative.**
/// </remarks>
public sealed class PolynomialParser<T>
    where T : struct, IRing<T>
{
    private readonly string _input;
    private readonly List<PolynomialToken> _tokens;
    private readonly INumberParser<T> _numberParser;
    private readonly T _zero;
    private readonly T _one;
    private int _position;

    private PolynomialParser(
        string input,
        List<PolynomialToken> tokens,
        INumberParser<T> numberParser,
        T zero,
        T one)
    {
        _input = input;
        _tokens = tokens;
        _numberParser = numberParser;
        _zero = zero;
        _one = one;
    }

    /// <summary>
    /// 解析一个多项式 / Parse one polynomial.
    /// </summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <param name="numberParser">数值解析器 / The numeric parser.</param>
    /// <param name="zero">零 / Zero.</param>
    /// <param name="one">一 / One.</param>
    /// <returns>多项式，或错误 / The polynomial, or an error.</returns>
    /// <exception cref="ArgumentNullException">输入或解析器为空引用 / The input or parser is a null reference.</exception>
    public static Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> Parse(
        string input,
        INumberParser<T> numberParser,
        T zero,
        T one)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(numberParser);

        Result<List<PolynomialToken>, ErrorCode, Error<ErrorCode>> lexed = PolynomialLexer.Lex(input);
        if (!lexed.IsOk)
        {
            return ParseResult.Failed<ParsedPolynomial<T>>(ErrorCode.IllegalArgument, "词法错误 / lexical error");
        }

        var parser = new PolynomialParser<T>(input, lexed.Value, numberParser, zero, one);
        Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> parsed = parser.ParseExpression();

        if (!parsed.IsOk)
        {
            return parsed;
        }

        // **解析完必须吃到 `End`，否则 `"1 2"` 会被当成 `1`。**
        // **Parsing has to end on `End`, or `"1 2"` would parse as `1`.**
        if (parser.Current().Type != PolynomialTokenType.End)
        {
            return ParseResult.Failed<ParsedPolynomial<T>>(
                ErrorCode.IllegalArgument,
                "多余的输入，位置 " + parser.Current().Position + " / trailing input at " + parser.Current().Position);
        }

        return parsed;
    }

    /// <summary>
    /// 解析一个二次不等式 / Parse a quadratic inequality.
    /// </summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <param name="numberParser">数值解析器 / The numeric parser.</param>
    /// <param name="zero">零 / Zero.</param>
    /// <param name="one">一 / One.</param>
    /// <returns>二次不等式，或错误 / The quadratic inequality, or an error.</returns>
    /// <exception cref="ArgumentNullException">输入或解析器为空引用 / The input or parser is a null reference.</exception>
    /// <remarks>
    /// **与线性版同一条规矩：不移项，比较符原样留着。**
    /// **两边都要过二次这一关**——**只查一边会让 `x &lt;= y^3` 混进来，而它在求解侧会变成三类问题。**
    ///
    /// **Same rule as the linear version: no rearranging, the operator stays as written.**
    /// **Both sides have to pass the quadratic gate** — **checking only one lets `x &lt;= y^3` through, and it becomes a different kind of problem on the solving side.**
    /// </remarks>
    public static Result<QuadraticInequalityOf<T>, ErrorCode, Error<ErrorCode>> ParseQuadraticInequality(
        string input,
        INumberParser<T> numberParser,
        T zero,
        T one)
    {
        Result<ParsedInequality<T>, ErrorCode, Error<ErrorCode>> parsed = ParseInequality(input, numberParser, zero, one);
        if (!parsed.IsOk)
        {
            return ParseResult.Failed<QuadraticInequalityOf<T>>(ErrorCode.IllegalArgument, "输入无法解析 / the input does not parse");
        }

        QuadraticPolynomial<T>? lhs = parsed.Value.Lhs.ToCanonicalPolynomial().ToQuadraticPolynomialOrNull();
        QuadraticPolynomial<T>? rhs = parsed.Value.Rhs.ToCanonicalPolynomial().ToQuadraticPolynomialOrNull();
        if (lhs is null || rhs is null)
        {
            return ParseResult.Failed<QuadraticInequalityOf<T>>(
                ErrorCode.IllegalArgument,
                "有一边超过二次，不是二次不等式 / one side exceeds degree two, so this is not a quadratic inequality");
        }

        return ParseResult.Ok(new QuadraticInequalityOf<T>(lhs, rhs, parsed.Value.Comparison));
    }

    /// <summary>
    /// 解析一个线性不等式 / Parse a linear inequality.
    /// </summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <param name="numberParser">数值解析器 / The numeric parser.</param>
    /// <param name="zero">零 / Zero.</param>
    /// <param name="one">一 / One.</param>
    /// <returns>线性不等式，或错误 / The linear inequality, or an error.</returns>
    /// <exception cref="ArgumentNullException">输入或解析器为空引用 / The input or parser is a null reference.</exception>
    /// <remarks>
    /// **这一层仍然不移项**：两边各自转成线性多项式，**原样留着比较符。**
    /// **`2*x &lt;= 3*y + 1` 降低之后左边是 `2x`、右边是 `3y + 1`，而不是 `2x - 3y &lt;= 1`。**
    /// 移项会把比较符的方向带进来（乘负号要翻转），**那是求解器那一侧的事，这里做就会有两处各自翻转。**
    ///
    /// **This layer still does not rearrange**: each side becomes a linear polynomial, **and the operator stays as written.**
    /// **`2*x &lt;= 3*y + 1` lowers to `2x` on the left and `3y + 1` on the right, not to `2x - 3y &lt;= 1`.**
    /// Rearranging drags the operator direction into it (multiplying by a negative flips it), **which belongs to the solving side; doing it here would flip in two places.**
    /// </remarks>
    public static Result<LinearInequality<T>, ErrorCode, Error<ErrorCode>> ParseLinearInequality(
        string input,
        INumberParser<T> numberParser,
        T zero,
        T one)
    {
        Result<ParsedInequality<T>, ErrorCode, Error<ErrorCode>> parsed = ParseInequality(input, numberParser, zero, one);
        if (!parsed.IsOk)
        {
            return ParseResult.Failed<LinearInequality<T>>(ErrorCode.IllegalArgument, "输入无法解析 / the input does not parse");
        }

        LinearPolynomial<T>? lhs = parsed.Value.Lhs.ToCanonicalPolynomial().ToLinearPolynomialOrNull();
        LinearPolynomial<T>? rhs = parsed.Value.Rhs.ToCanonicalPolynomial().ToLinearPolynomialOrNull();
        if (lhs is null || rhs is null)
        {
            return ParseResult.Failed<LinearInequality<T>>(
                ErrorCode.IllegalArgument,
                "有一边超过一次，不是线性不等式 / one side exceeds degree one, so this is not a linear inequality");
        }

        return ParseResult.Ok(new LinearInequality<T>(lhs, rhs, parsed.Value.Comparison));
    }

    /// <summary>
    /// 解析一个线性多项式 / Parse a linear polynomial.
    /// </summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <param name="numberParser">数值解析器 / The numeric parser.</param>
    /// <param name="zero">零 / Zero.</param>
    /// <param name="one">一 / One.</param>
    /// <returns>线性多项式，或错误 / The linear polynomial, or an error.</returns>
    /// <exception cref="ArgumentNullException">输入或解析器为空引用 / The input or parser is a null reference.</exception>
    /// <remarks>
    /// **度数判断不重新写一遍**：走 `ToLinearPolynomialOrNull`，**那是全仓唯一一处"什么算线性"的定义。**
    /// **在这里另写一个 `^` 计数，两处就会在某次改动后给出不同答案。**
    ///
    /// **The degree test is not rewritten here**: it goes through `ToLinearPolynomialOrNull`, **the one place that defines what counts as linear.**
    /// **A second `^`-counting rule here would, after some later change, disagree with the first.**
    /// </remarks>
    public static Result<LinearPolynomial<T>, ErrorCode, Error<ErrorCode>> ParseLinear(
        string input,
        INumberParser<T> numberParser,
        T zero,
        T one)
    {
        Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> parsed = Parse(input, numberParser, zero, one);
        if (!parsed.IsOk)
        {
            return ParseResult.Failed<LinearPolynomial<T>>(ErrorCode.IllegalArgument, "输入无法解析 / the input does not parse");
        }

        LinearPolynomial<T>? linear = parsed.Value.ToCanonicalPolynomial().ToLinearPolynomialOrNull();
        return linear is { } polynomial
            ? ParseResult.Ok(polynomial)
            : ParseResult.Failed<LinearPolynomial<T>>(
                ErrorCode.IllegalArgument,
                "度数超过一次，不是线性多项式 / the degree exceeds one, so this is not a linear polynomial");
    }

    /// <summary>
    /// 解析一个二次多项式 / Parse a quadratic polynomial.
    /// </summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <param name="numberParser">数值解析器 / The numeric parser.</param>
    /// <param name="zero">零 / Zero.</param>
    /// <param name="one">一 / One.</param>
    /// <returns>二次多项式，或错误 / The quadratic polynomial, or an error.</returns>
    /// <exception cref="ArgumentNullException">输入或解析器为空引用 / The input or parser is a null reference.</exception>
    public static Result<QuadraticPolynomial<T>, ErrorCode, Error<ErrorCode>> ParseQuadratic(
        string input,
        INumberParser<T> numberParser,
        T zero,
        T one)
    {
        Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> parsed = Parse(input, numberParser, zero, one);
        if (!parsed.IsOk)
        {
            return ParseResult.Failed<QuadraticPolynomial<T>>(ErrorCode.IllegalArgument, "输入无法解析 / the input does not parse");
        }

        QuadraticPolynomial<T>? quadratic = parsed.Value.ToCanonicalPolynomial().ToQuadraticPolynomialOrNull();
        return quadratic is { } polynomial
            ? ParseResult.Ok(polynomial)
            : ParseResult.Failed<QuadraticPolynomial<T>>(
                ErrorCode.IllegalArgument,
                "度数超过二次，不是二次多项式 / the degree exceeds two, so this is not a quadratic polynomial");
    }

    /// <summary>
    /// 解析一个不等式 / Parse one inequality.
    /// </summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <param name="numberParser">数值解析器 / The numeric parser.</param>
    /// <param name="zero">零 / Zero.</param>
    /// <param name="one">一 / One.</param>
    /// <returns>不等式，或错误 / The inequality, or an error.</returns>
    /// <exception cref="ArgumentNullException">输入或解析器为空引用 / The input or parser is a null reference.</exception>
    /// <remarks>
    /// **比较符读完之后右边是另一个完整表达式**，所以 `2*x &lt;= 3*y + 1` 的两边各自成式。
    /// **不在这里做移项**：把 `y` 挪到左边是 `LinearInequality` 那一步的事，
    /// **混进来的话，"解析"和"化简"就再也分不开了。**
    ///
    /// **After the operator the right side is another full expression**, so `2*x &lt;= 3*y + 1` parses on both sides.
    /// **No rearranging happens here**: moving `y` leftwards belongs to the `LinearInequality` step,
    /// **and mixing it in would make "parse" and "simplify" inseparable for good.**
    /// </remarks>
    public static Result<ParsedInequality<T>, ErrorCode, Error<ErrorCode>> ParseInequality(
        string input,
        INumberParser<T> numberParser,
        T zero,
        T one)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(numberParser);

        Result<List<PolynomialToken>, ErrorCode, Error<ErrorCode>> lexed = PolynomialLexer.Lex(input);
        if (!lexed.IsOk)
        {
            return ParseResult.Failed<ParsedInequality<T>>(ErrorCode.IllegalArgument, "词法错误 / lexical error");
        }

        var parser = new PolynomialParser<T>(input, lexed.Value, numberParser, zero, one);
        Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> lhs = parser.ParseExpression();
        if (!lhs.IsOk)
        {
            return ParseResult.Failed<ParsedInequality<T>>(ErrorCode.IllegalArgument, "不等式左边无法解析 / the left side does not parse");
        }

        PolynomialToken operatorToken = parser.Current();
        if (!ComparisonOf(operatorToken.Type, out Comparison comparison))
        {
            return ParseResult.Failed<ParsedInequality<T>>(
                ErrorCode.IllegalArgument,
                "这里要一个比较符 / expected a comparison operator at " + operatorToken.Position);
        }

        parser.Advance();
        Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> rhs = parser.ParseExpression();
        if (!rhs.IsOk)
        {
            return ParseResult.Failed<ParsedInequality<T>>(ErrorCode.IllegalArgument, "不等式右边无法解析 / the right side does not parse");
        }

        // **右边之后仍然必须吃到 `End`**，否则 `"a < b c"` 会安静地解析成 `a < b`。
        // **The right side still has to end on `End`**, or `"a < b c"` would quietly parse as `a < b`.
        if (parser.Current().Type != PolynomialTokenType.End)
        {
            return ParseResult.Failed<ParsedInequality<T>>(
                ErrorCode.IllegalArgument,
                "多余的输入，位置 " + parser.Current().Position + " / trailing input at " + parser.Current().Position);
        }

        return ParseResult.Ok(new ParsedInequality<T>(lhs.Value, rhs.Value, comparison));
    }

    /// <summary>词法单元到比较符 / Token type to comparison.</summary>
    private static bool ComparisonOf(PolynomialTokenType type, out Comparison comparison)
    {
        switch (type)
        {
            case PolynomialTokenType.Less:
                comparison = Comparison.LT;
                return true;
            case PolynomialTokenType.LessEqual:
                comparison = Comparison.LE;
                return true;
            case PolynomialTokenType.Equal:
                comparison = Comparison.EQ;
                return true;
            case PolynomialTokenType.NotEqual:
                comparison = Comparison.NE;
                return true;
            case PolynomialTokenType.GreaterEqual:
                comparison = Comparison.GE;
                return true;
            case PolynomialTokenType.Greater:
                comparison = Comparison.GT;
                return true;
            default:
                comparison = default;
                return false;
        }
    }

    private PolynomialToken Current() => _tokens[_position];

    private void Advance()
    {
        if (_position < _tokens.Count - 1)
        {
            _position++;
        }
    }

    private Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> ParseExpression()
    {
        Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> first = ParseTerm();
        if (!first.IsOk)
        {
            return first;
        }

        ParsedPolynomial<T> result = first.Value;
        while (true)
        {
            PolynomialTokenType type = Current().Type;
            if (type is not (PolynomialTokenType.Plus or PolynomialTokenType.Minus))
            {
                return ParseResult.Ok(result);
            }

            Advance();
            Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> right = ParseTerm();
            if (!right.IsOk)
            {
                return right;
            }

            result = type == PolynomialTokenType.Plus
                ? Add(result, right.Value)
                : Subtract(result, right.Value);
        }
    }

    private Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> ParseTerm()
    {
        Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> first = ParsePower();
        if (!first.IsOk)
        {
            return first;
        }

        ParsedPolynomial<T> result = first.Value;
        while (true)
        {
            if (Current().Type != PolynomialTokenType.Star)
            {
                return ParseResult.Ok(result);
            }

            Advance();
            Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> right = ParsePower();
            if (!right.IsOk)
            {
                return right;
            }

            result = Multiply(result, right.Value);
        }
    }

    private Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> ParsePower()
    {
        Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> first = ParseFactor();
        if (!first.IsOk)
        {
            return first;
        }

        ParsedPolynomial<T> result = first.Value;
        if (Current().Type != PolynomialTokenType.Caret)
        {
            return ParseResult.Ok(result);
        }

        Advance();
        PolynomialToken exponent = Current();
        if (exponent.Type != PolynomialTokenType.Number)
        {
            return ParseResult.Failed<ParsedPolynomial<T>>(
                ErrorCode.IllegalArgument,
                "`^` 之后要一个整数指数 / expected an integer exponent after '^' at " + exponent.Position);
        }

        Advance();
        if (!int.TryParse(exponent.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int power))
        {
            return ParseResult.Failed<ParsedPolynomial<T>>(
                ErrorCode.IllegalArgument,
                "指数不是整数 / the exponent is not an integer at " + exponent.Position);
        }

        if (power < 0)
        {
            return ParseResult.Failed<ParsedPolynomial<T>>(
                ErrorCode.IllegalArgument,
                "负指数不支持 / negative exponent is not supported at " + exponent.Position);
        }

        // **`0` 次幂是常数 1，`1` 次幂是它本身。** **`x^0` 若走乘法循环会得到 0 项，那是 0 而不是 1。**
        // **A zero exponent is the constant one, and a single exponent is identity.** **Taking `x^0` through the multiply loop yields no terms, which reads as zero rather than one.**
        ParsedPolynomial<T> powered = new([], _one);
        for (int index = 0; index < power; index++)
        {
            powered = Multiply(powered, result);
        }

        return ParseResult.Ok(powered);
    }

    private Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> ParseFactor()
    {
        PolynomialToken token = Current();
        switch (token.Type)
        {
            case PolynomialTokenType.Minus:
                Advance();
                Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> negated = ParseFactor();
                return negated.IsOk ? ParseResult.Ok(Negate(negated.Value)) : negated;

            case PolynomialTokenType.Number:
            {
                Advance();
                T? value = _numberParser.Parse(token.Text);
                return value is { } number
                    ? ParseResult.Ok(new ParsedPolynomial<T>([], number))
                    : ParseResult.Failed<ParsedPolynomial<T>>(
                        ErrorCode.IllegalArgument,
                        "数值 '" + token.Text + "' 不能被目标类型解析 / '" + token.Text + "' does not parse as the target type");
            }

            case PolynomialTokenType.Identifier:
                Advance();
                return ParseResult.Ok(new ParsedPolynomial<T>(
                    [new ParsedTerm<T>(_one, new Dictionary<string, int> { [token.Text] = 1 })],
                    _zero));

            case PolynomialTokenType.LeftParen:
            {
                Advance();
                Result<ParsedPolynomial<T>, ErrorCode, Error<ErrorCode>> inner = ParseExpression();
                if (!inner.IsOk)
                {
                    return inner;
                }
                if (Current().Type != PolynomialTokenType.RightParen)
                {
                    return ParseResult.Failed<ParsedPolynomial<T>>(
                        ErrorCode.IllegalArgument,
                        "缺少右括号 / missing ')' at " + Current().Position);
                }
                Advance();
                return inner;
            }

            default:
                return ParseResult.Failed<ParsedPolynomial<T>>(
                    ErrorCode.IllegalArgument,
                    "这里不能出现该词法单元 / unexpected token at " + token.Position);
        }
    }

    private ParsedPolynomial<T> Add(ParsedPolynomial<T> left, ParsedPolynomial<T> right) => new(
        [.. left.Terms, .. right.Terms],
        left.Constant.Plus(right.Constant));

    private ParsedPolynomial<T> Subtract(ParsedPolynomial<T> left, ParsedPolynomial<T> right) => new(
        [.. left.Terms, .. right.Terms.Select(term => term with { Coefficient = term.Coefficient.Negate() })],
        left.Constant.Plus(right.Constant.Negate()));

    private ParsedPolynomial<T> Negate(ParsedPolynomial<T> value) => new(
        [.. value.Terms.Select(term => term with { Coefficient = term.Coefficient.Negate() })],
        value.Constant.Negate());

    /// <summary>两个多项式的积 / The product of two polynomials.</summary>
    /// <remarks>
    /// **同符号的幂次相加**，这是 `x * x = x^2` 的全部内容。
    /// **零系数的项直接丢掉**，否则 `0 * x` 会留下一项 `0x`，而它在后续比较里与没有这一项不同。
    ///
    /// **Powers of the same symbol add**, which is all that `x * x = x^2` means.
    /// **Zero-coefficient terms are dropped**, or `0 * x` would leave a `0x` term that compares differently from its absence.
    /// </remarks>
    private ParsedPolynomial<T> Multiply(ParsedPolynomial<T> left, ParsedPolynomial<T> right)
    {
        var terms = new List<ParsedTerm<T>>();
        T constant = left.Constant.Times(right.Constant);

        foreach (ParsedTerm<T> term in left.Terms)
        {
            terms.Add(term with { Coefficient = term.Coefficient.Times(right.Constant) });
        }

        foreach (ParsedTerm<T> term in right.Terms)
        {
            terms.Add(term with { Coefficient = term.Coefficient.Times(left.Constant) });
        }

        foreach (ParsedTerm<T> a in left.Terms)
        {
            foreach (ParsedTerm<T> b in right.Terms)
            {
                var powers = new Dictionary<string, int>(a.Powers);
                foreach (KeyValuePair<string, int> pair in b.Powers)
                {
                    powers[pair.Key] = powers.TryGetValue(pair.Key, out int existing)
                        ? existing + pair.Value
                        : pair.Value;
                }

                terms.Add(new ParsedTerm<T>(a.Coefficient.Times(b.Coefficient), powers));
            }
        }

        return new ParsedPolynomial<T>(
            terms.Where(term => !IsZero(term.Coefficient)).ToArray(),
            constant);
    }

    private bool IsZero(T value) => _zero.Equals(value);
}
