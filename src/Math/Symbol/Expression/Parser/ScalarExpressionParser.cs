#nullable enable

using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Parse;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Globalization;
using ScalarRet = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Math.Symbol.Expression.ScalarExpression<object?>,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Math.Symbol.Expression.Parser;

/// <summary>
/// 标量表达式的算术解析层 / The arithmetic layer of the scalar expression parser.
/// </summary>
/// <remarks>
/// 里**算术那一段**在这里。
/// **C# 侧此前只有 `Parser` 内部一个私有的 `ParseScalarValue`**，**只服务比较操作数**（字面量与路径），
/// **没有公开的标量入口，也没有算术。**
///
/// **本层不做三元、`if-then-else`、逻辑与模式匹配**——那些仍在，**而绊线钉着它们不在。**
///
/// The arithmetic part of its lives here.
/// **C# had only a private `ParseScalarValue` inside `Parser`, serving comparison operands (literals and paths)**,
/// **with no public scalar entry and no arithmetic.**
/// **This layer does not do ternaries, `if-then-else`, logic, or pattern matching** — those stay in the code,
/// **and a tripwire pins their absence.**
///
/// 优先级与结合性：
/// `additive`（`+`/`-`，左结合）→ `multiplicative`（`*`/`/`/`%`，左结合）→ `unaryMinus` → `power`（`^`/`**`，**右结合**）→ `unaryPlus` → `primary`。
/// **`-2^2` 读作 `-(2^2)`**：一元负号比幂松。**`2^3^2` 读作 `2^(3^2)`**：幂右结合。
///
/// Precedence and associativity, matching the code: **`-2^2` means `-(2^2)`** and **`2^3^2` means `2^(3^2)`**.
/// </remarks>
public sealed class ScalarExpressionParser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _position;

    private ScalarExpressionParser(IReadOnlyList<Token> tokens) => _tokens = tokens;

    /// <summary>
    /// 解析一个标量表达式 / Parse one scalar expression.
    /// </summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <returns>表达式，或错误 / The expression, or an error.</returns>
    /// <exception cref="ArgumentNullException">输入为空引用 / The input is a null reference.</exception>
    public static ScalarRet Parse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var parser = new ScalarExpressionParser(input.TokenizeAsScalar());
        ScalarRet parsed = parser.ParseTernary();
        if (!parsed.IsOk)
        {
            return parsed;
        }

        // **吃完必须正好停在 `Eof`。** **不查的话 `"1 2"` 会安静地解析成 `1`。**
        // **Parsing has to stop exactly at `Eof`.** **Without the check, `"1 2"` quietly parses as `1`.**
        return parser.Current().Type == TokenType.Eof
            ? parsed
            : Fail("多余的输入，位置 " + parser.Current().Position + " / trailing input at " + parser.Current().Position);
    }

    private Token Current() => _tokens[global::System.Math.Min(_position, _tokens.Count - 1)];

    private void Advance()
    {
        if (_position < _tokens.Count - 1)
        {
            _position++;
        }
    }

    private static ScalarRet Fail(string message) =>
        new Failed<ScalarExpression<object?>, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument,
            message,
            new ParseIssue(ParseIssueType.Syntax, message, string.Empty, 0));

    /// <summary>三元与 `if-then-else` 的入口 / The ternary entry.</summary>
    /// <remarks>
    /// **三元右结合**：`a ? b : c ? d : e` 是 `a ? b : (c ? d : e)`，**因为两支都递归回本层。**
    /// **条件必须是布尔**——**否则 `1 ? 2 : 3` 会变成一个"看起来能跑"的东西。**
    /// </remarks>
    /// <summary>`if ... then ... else ... fi` / The keyword conditional.</summary>
    /// <remarks>
    /// **与三元落到同一个节点 `ScalarConditional`**：**它们是同一个东西的两种写法，
    /// 分成两个节点之后，下游每处判断都得写两遍，而两遍迟早会不一致。**
    ///
    /// **`fi` 不能省**：`if` 是可以嵌套的，**没有结束标记就没有办法知道哪一层在哪结束。**
    ///
    /// **Lowers onto the same `ScalarConditional` node as the ternary**: **they are two spellings of one thing,
    /// and two nodes would mean every downstream test written twice, which drifts.**
    /// **`fi` is not optional**: `if` nests, **and without an end marker there is no way to tell where a level ends.**
    /// </remarks>
    private ScalarRet ParseIfThenElse()
    {
        int ifPosition = Current().Position;
        Advance();

        ScalarRet condition = ParseLogicalOr();
        if (!condition.IsOk)
        {
            return condition;
        }

        if (UnwrapBoolean(condition.Value) is not { } conditionBoolean)
        {
            return Fail("if 条件必须是布尔表达式，位置 " + ifPosition + " / the if condition has to be a boolean expression at " + ifPosition);
        }

        if (Current().Type != TokenType.Then)
        {
            return Fail("if 之后缺少 then，位置 " + Current().Position + " / expected then after the if condition at " + Current().Position);
        }

        Advance();
        ScalarRet thenBranch = ParseTernary();
        if (!thenBranch.IsOk)
        {
            return thenBranch;
        }

        if (Current().Type != TokenType.Else)
        {
            return Fail("if 表达式缺少 else，位置 " + Current().Position + " / expected else in an if expression at " + Current().Position);
        }

        Advance();
        ScalarRet elseBranch = ParseTernary();
        if (!elseBranch.IsOk)
        {
            return elseBranch;
        }

        if (Current().Type != TokenType.Fi)
        {
            return Fail("if 表达式缺少 fi，位置 " + Current().Position + " / expected fi to close the if expression at " + Current().Position);
        }

        Advance();
        return Results.Ok<ScalarExpression<object?>>(
            new ScalarConditional<object?>(conditionBoolean, thenBranch.Value, elseBranch.Value));
    }

    private ScalarRet ParseTernary()
    {
        if (Current().Type == TokenType.If)
        {
            return ParseIfThenElse();
        }

        ScalarRet condition = ParseLogicalOr();
        if (!condition.IsOk)
        {
            return condition;
        }

        if (Current().Type != TokenType.Question)
        {
            return condition;
        }

        int questionPosition = Current().Position;
        Advance();

        ScalarRet thenBranch = ParseTernary();
        if (!thenBranch.IsOk)
        {
            return thenBranch;
        }

        if (Current().Type != TokenType.Colon)
        {
            return Fail("三元表达式缺少冒号，位置 " + Current().Position + " / expected a colon in a ternary at " + Current().Position);
        }

        Advance();
        ScalarRet elseBranch = ParseTernary();
        if (!elseBranch.IsOk)
        {
            return elseBranch;
        }

        return UnwrapBoolean(condition.Value) is { } conditionBoolean
            ? Results.Ok<ScalarExpression<object?>>(
                new ScalarConditional<object?>(conditionBoolean, thenBranch.Value, elseBranch.Value))
            : Fail("三元条件必须是布尔表达式，位置 " + questionPosition + " / the ternary condition has to be a boolean expression at " + questionPosition);
    }

    /// <summary>拆出被包的布尔表达式 / Unwrap a wrapped boolean expression.</summary>
    /// <remarks>**只有 `ScalarBoolean` 能拆；`1 &lt; 2` 能当条件全靠比较那一层把它包成这个。**</remarks>
    private static BooleanExpression? UnwrapBoolean(ScalarExpression<object?> expression) =>
        expression is ScalarBoolean<object?> wrapped ? wrapped.Expr : null;

    private ScalarRet ParseLogicalOr()
    {
        ScalarRet left = ParseLogicalAnd();
        if (!left.IsOk)
        {
            return left;
        }

        ScalarExpression<object?> result = left.Value;
        while (Current().Type is TokenType.Or or TokenType.PipePipe)
        {
            int position = Current().Position;
            Advance();
            ScalarRet right = ParseLogicalAnd();
            if (!right.IsOk)
            {
                return right;
            }

            if (UnwrapBoolean(result) is not { } leftBoolean)
            {
                return Fail("or 左边必须是布尔表达式，位置 " + position + " / expected a boolean before or at " + position);
            }
            if (UnwrapBoolean(right.Value) is not { } rightBoolean)
            {
                return Fail("or 右边必须是布尔表达式，位置 " + position + " / expected a boolean after or at " + position);
            }

            result = new ScalarBoolean<object?>(MergeOr(leftBoolean, rightBoolean));
        }

        return Results.Ok(result);
    }

    private ScalarRet ParseLogicalAnd()
    {
        ScalarRet left = ParseComparison();
        if (!left.IsOk)
        {
            return left;
        }

        ScalarExpression<object?> result = left.Value;
        while (Current().Type is TokenType.And or TokenType.AmpersandAmpersand)
        {
            int position = Current().Position;
            Advance();
            ScalarRet right = ParseComparison();
            if (!right.IsOk)
            {
                return right;
            }

            if (UnwrapBoolean(result) is not { } leftBoolean)
            {
                return Fail("and 左边必须是布尔表达式，位置 " + position + " / expected a boolean before and at " + position);
            }
            if (UnwrapBoolean(right.Value) is not { } rightBoolean)
            {
                return Fail("and 右边必须是布尔表达式，位置 " + position + " / expected a boolean after and at " + position);
            }

            result = new ScalarBoolean<object?>(MergeAnd(leftBoolean, rightBoolean));
        }

        return Results.Ok(result);
    }

    /// <summary>比较，不链式 / Comparison, which does not chain.</summary>
    /// <remarks>
    /// **`a &lt; b &lt; c` 不是链式比较**，所以这里**用 `if` 而不是 `while`**：
    /// **`1 &lt; 2 &lt; 3` 解析出 `(1 &lt; 2)` 之后还剩 `&lt; 3`，被 `Eof` 检查拒掉。**
    /// **改成 `while` 会让它安静地变成嵌套比较，而那是另一门语言的语义。**
    /// </remarks>
    private ScalarRet ParseComparison()
    {
        ScalarRet left = ParseAdditive();
        if (!left.IsOk)
        {
            return left;
        }

        if (Current().Type.ToComparisonOperator() is not { } comparison)
        {
            return left;
        }

        Advance();
        ScalarRet right = ParseAdditive();
        return right.IsOk
            ? Results.Ok<ScalarExpression<object?>>(
                new ScalarBoolean<object?>(new Comparison<object?>(comparison, left.Value, right.Value)))
            : right;
    }

    /// <summary>合并两个布尔为 or / Merge two booleans into an or.</summary>
    /// <remarks>**两边已经是 `OrExpression` 时展开，不再套一层**——**否则 `a or b or c` 会变成右倾嵌套，而扁平的那个才。**</remarks>
    private static OrExpression MergeOr(BooleanExpression left, BooleanExpression right) =>
        new([.. left is OrExpression l ? l.Operands : [left], .. right is OrExpression r ? r.Operands : [right]]);

    /// <summary>合并两个布尔为 and / Merge two booleans into an and.</summary>
    private static AndExpression MergeAnd(BooleanExpression left, BooleanExpression right) =>
        new([.. left is AndExpression l ? l.Operands : [left], .. right is AndExpression r ? r.Operands : [right]]);

    private ScalarRet ParseAdditive()
    {
        ScalarRet first = ParseMultiplicative();
        if (!first.IsOk)
        {
            return first;
        }

        ScalarExpression<object?> result = first.Value;
        while (true)
        {
            BinaryOperator? op = Current().Type switch
            {
                TokenType.Plus => BinaryOperator.Add,
                TokenType.Minus => BinaryOperator.Subtract,
                _ => null,
            };
            if (op is not { } binary)
            {
                return Results.Ok(result);
            }

            Advance();
            ScalarRet right = ParseMultiplicative();
            if (!right.IsOk)
            {
                return right;
            }

            result = new ScalarBinary<object?>(binary, result, right.Value);
        }
    }

    private ScalarRet ParseMultiplicative()
    {
        ScalarRet first = ParseUnaryMinus();
        if (!first.IsOk)
        {
            return first;
        }

        ScalarExpression<object?> result = first.Value;
        while (true)
        {
            BinaryOperator? op = Current().Type switch
            {
                TokenType.Star => BinaryOperator.Multiply,
                TokenType.Slash => BinaryOperator.Divide,
                TokenType.Percent => BinaryOperator.Modulo,
                _ => null,
            };
            if (op is not { } binary)
            {
                return Results.Ok(result);
            }

            Advance();
            ScalarRet right = ParseUnaryMinus();
            if (!right.IsOk)
            {
                return right;
            }

            result = new ScalarBinary<object?>(binary, result, right.Value);
        }
    }

    private ScalarRet ParseUnaryMinus()
    {
        if (Current().Type != TokenType.Minus)
        {
            return ParsePower();
        }

        Advance();
        ScalarRet operand = ParseUnaryMinus();
        return operand.IsOk
            ? Results.Ok<ScalarExpression<object?>>(new ScalarUnary<object?>(UnaryOperator.Negate, operand.Value))
            : operand;
    }

    private ScalarRet ParsePower()
    {
        ScalarRet first = ParseUnaryPlus();
        if (!first.IsOk)
        {
            return first;
        }

        if (Current().Type is not (TokenType.Caret or TokenType.DoubleStar))
        {
            return first;
        }

        Advance();
        // **右结合：递归下降回自己。** **`2^3^2` 是 `2^(3^2)=512`；写成循环会得到 `(2^3)^2=64`，而两者都能编译。**
        // **Right-associative: recurse into itself.** **`2^3^2` is `2^(3^2)=512`; a loop gives `(2^3)^2=64`, and both compile.**
        ScalarRet exponent = ParsePower();
        return exponent.IsOk
            ? Results.Ok<ScalarExpression<object?>>(
                new ScalarBinary<object?>(BinaryOperator.Power, first.Value, exponent.Value))
            : exponent;
    }

    private ScalarRet ParseUnaryPlus()
    {
        if (Current().Type != TokenType.Plus)
        {
            return ParsePrimary();
        }

        Advance();
        ScalarRet operand = ParseUnaryPlus();
        return operand.IsOk
            ? Results.Ok<ScalarExpression<object?>>(new ScalarUnary<object?>(UnaryOperator.Positive, operand.Value))
            : operand;
    }

    /// <summary>标识符：常量、函数调用或引用 / An identifier: a constant, a call, or a reference.</summary>
    /// <remarks>
    /// **`math.PI` 与 `math.E` 在词法上是标识符、在语义上是常量**，所以就地换成数值。
    /// **函数名的 `math.` 前缀会被剥掉**：**带前缀的名字只在这一处归一化，下游只认剥过的那一个。**
    ///
    /// **`math.PI` and `math.E` are identifiers lexically and constants semantically**, so they become numbers here.
    /// **A function name loses its `math.` prefix**: **normalisation happens only here, and downstream sees only the stripped name.**
    /// </remarks>
    private ScalarRet ParseIdentifierOrFunction()
    {
        Token identifier = Current();
        Advance();

        if (identifier.Value == "math.PI")
        {
            return Results.Ok<ScalarExpression<object?>>(new ScalarConstant<object?>(global::System.Math.PI));
        }

        if (identifier.Value == "math.E")
        {
            return Results.Ok<ScalarExpression<object?>>(new ScalarConstant<object?>(global::System.Math.E));
        }

        if (Current().Type != TokenType.LParen)
        {
            return Results.Ok<ScalarExpression<object?>>(
                new ScalarReference<object?>(PropertyPath.Parse(identifier.Value)));
        }

        Advance();
        const string MathPrefix = "math.";
        string functionName = identifier.Value.StartsWith(MathPrefix, StringComparison.Ordinal)
            ? identifier.Value[MathPrefix.Length..]
            : identifier.Value;

        var arguments = new List<ScalarExpression<object?>>();
        if (Current().Type != TokenType.RParen)
        {
            ScalarRet firstArgument = ParseTernary();
            if (!firstArgument.IsOk)
            {
                return firstArgument;
            }

            arguments.Add(firstArgument.Value);
            while (Current().Type == TokenType.Comma)
            {
                Advance();
                ScalarRet argument = ParseTernary();
                if (!argument.IsOk)
                {
                    return argument;
                }

                arguments.Add(argument.Value);
            }
        }

        if (Current().Type != TokenType.RParen)
        {
            return Fail("函数调用缺少右括号，位置 " + Current().Position + " / expected a right parenthesis after the arguments at " + Current().Position);
        }

        Advance();
        return Results.Ok<ScalarExpression<object?>>(new ScalarFunction<object?>(functionName, arguments));
    }

    private ScalarRet ParsePrimary()
    {
        Token token = Current();
        switch (token.Type)
        {
            case TokenType.Number:
                Advance();
                return double.TryParse(token.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    ? Results.Ok<ScalarExpression<object?>>(new ScalarConstant<object?>(value))
                    : Fail("数值 '" + token.Value + "' 无法解析 / '" + token.Value + "' does not parse");

            case TokenType.Identifier:
                return ParseIdentifierOrFunction();

            case TokenType.LParen:
            {
                Advance();
                // **括号里要允许完整的表达式，包括 `if ... fi` 与三元**——**只调到加法层的话，
                // `(if a then b else c fi)` 与 `(a ? b : c)` 都会被拒，而它们本来就该合法。**
                // **The parentheses have to admit a full expression, `if ... fi` and ternaries included** —
                // **stopping at the additive layer refuses `(if a then b else c fi)` and `(a ? b : c)`, both of which are legal.**
                ScalarRet inner = ParseTernary();
                if (!inner.IsOk)
                {
                    return inner;
                }
                if (Current().Type != TokenType.RParen)
                {
                    return Fail("缺少右括号，位置 " + Current().Position + " / missing ')' at " + Current().Position);
                }
                Advance();
                return inner;
            }

            default:
                return Fail("这里不能出现该词法单元，位置 " + token.Position + " / unexpected token at " + token.Position);
        }
    }
}

/// <summary>标量解析的公开入口 / The public scalar parsing entry points.</summary>
public static class ScalarParserResults
{
    /// <summary>解析一个标量表达式 / Parse one scalar expression.</summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <returns>表达式，或错误 / The expression, or an error.</returns>
    public static ScalarRet ParseScalarExpression(string input) => ScalarExpressionParser.Parse(input);

    /// <summary>解析一个标量表达式，失败时给 `null` / Parse one scalar expression, giving `null` on failure.</summary>
    /// <param name="input">输入文本 / The input text.</param>
    /// <returns>表达式，或 `null` / The expression, or `null`.</returns>
    public static ScalarExpression<object?>? ParseScalarExpressionOrNull(string input)
    {
        ScalarRet parsed = ScalarExpressionParser.Parse(input);
        return parsed.IsOk ? parsed.Value : null;
    }
}