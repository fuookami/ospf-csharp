#nullable enable

using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Parse;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;

/// <summary>**标量表达式 → Mongo 聚合值** / Scalar expression → Mongo aggregation value.</summary>
/// <remarks>
/// 本类把 `ScalarExpression` 变成聚合管道里能用的 `BsonValue`：
/// 引用变 `$field`，常量原样，一元/二元运算变 `$multiply`/`$add`/`$divide` 之类的文档。
///
/// **不支持的运算必须失败，不能返回 null 或原样**——`$power` 就是一个例子：
/// **把不支持的运算悄悄当成"没这回事"，查询会返回一个错误的结果集，而调用方以为查对了。**
///
/// Its `MongoScalarTranslator` (**209 lines**) turns a `ScalarExpression` into a `BsonValue` usable inside
/// an aggregation pipeline: references become `$field`, constants pass through, unary/binary operators become
/// `$multiply`/`$add`/`$divide` documents.
///
/// **An unsupported operator has to fail rather than return null or the input** — `$power` is one example:
/// **quietly treating an unsupported operator as absent returns a wrong result set while the caller believes it succeeded.**
/// </remarks>
public static class MongoScalarTranslator
{
    /// <summary>把一个标量表达式翻成 Mongo 值 / Translate one scalar expression into a Mongo value.</summary>
    /// <remarks>
    /// **`resolveFieldName` 由调用方给**：它做成构造参数，因为文档字段名与模型属性名常常不同
    /// （前者带前缀、或做过下划线转换）。**在这里写死属性名，等于假设两者永远相同。**
    ///
 /// **`resolveFieldName` comes from the caller**: the implementation takes it as a constructor parameter because
    /// document field names and model property names often differ (prefixing, snake_case conversion).
    /// **Hard-coding the property name here would assume the two are always identical.**
    /// </remarks>
    public static Result<BsonValue, ErrorCode, Error<ErrorCode>> Translate(
        ScalarExpression<object?> expression,
        Func<string, string>? resolveFieldName = null)
    {
        ArgumentNullException.ThrowIfNull(expression);
        resolveFieldName ??= static name => name;
        return Translate(expression, resolveFieldName, depth: 0);
    }

    private static Result<BsonValue, ErrorCode, Error<ErrorCode>> Translate(
        ScalarExpression<object?> expression,
        Func<string, string> resolveFieldName,
        int depth)
    {
        // **深度护栏**：表达式可能被构造成很深的树，而这里用递归。
        // **超过上限就失败，不是返回原值**——返回原值会让一个没翻译的子树混进管道。
        //
        // **Depth guard**: the expression may be a deep tree and this recurses.
        // **Past the limit it fails rather than returning the input** — returning it would slip an untranslated subtree into the pipeline.
        if (depth > MaxDepth)
        {
            return Failure($"标量表达式嵌套超过 {MaxDepth} 层 / Scalar expression nested deeper than {MaxDepth}");
        }

        switch (expression)
        {
            case ScalarConstant<object?> constant:
                return Ok(ToBson(constant.Value));

            case ScalarReference<object?> reference:
                // **引用变成 `$字段`**：这是聚合管道的写法，与查询过滤器的裸字段名不同。
                // **A reference becomes `$field`**: that is the aggregation spelling, unlike the bare name used in filters.
                return Ok(new BsonString("$" + resolveFieldName(reference.Path.Value)));

            case ScalarUnary<object?> unary:
                return TranslateUnary(unary, resolveFieldName, depth);

            case ScalarBinary<object?> binary:
                return TranslateBinary(binary, resolveFieldName, depth);

            case ScalarFunction<object?> function:
                return TranslateFunction(function, resolveFieldName, depth);

            default:
                return Failure($"不支持的标量表达式 / Unsupported scalar expression: {expression.TypeName}");
        }
    }

    private static Result<BsonValue, ErrorCode, Error<ErrorCode>> TranslateUnary(
        ScalarUnary<object?> unary,
        Func<string, string> resolveFieldName,
        int depth)
    {
        Result<BsonValue, ErrorCode, Error<ErrorCode>> operand = Translate(unary.Operand, resolveFieldName, depth + 1);
        if (!operand.IsOk)
        {
            return operand;
        }

        switch (unary.Operator)
        {
            // **`-x` 写成 `$multiply: [-1, x]`**，因为聚合管道没有一元减号。
            // **`-x` becomes `$multiply: [-1, x]`** because the aggregation pipeline has no unary minus.
            case UnaryOperator.Negate:
                return Ok(new BsonDocument("$multiply", new BsonArray { -1, operand.Value }));

            // **`+x` 就是 `x`**：正号是恒等，包一层只会让管道多一步。
            // **`+x` is just `x`**: the plus sign is identity, and wrapping it only adds a pipeline stage.
            case UnaryOperator.Positive:
                return operand;

            // **`ABS` 不支持**——Mongo 有 `$abs`，但这里没接，**于是直接失败而不是自作主张**。
            // **`ABS` is not supported** — Mongo has `$abs` but it is not wired here,
            // **so the translation fails rather than inventing support.**
            case UnaryOperator.Abs:
                return Failure("ABS 一元标量表达式不支持 / ABS unary scalar expression is not supported");

            default:
                return Failure($"不支持的一元运算 / Unsupported unary operator: {unary.Operator}");
        }
    }

    private static Result<BsonValue, ErrorCode, Error<ErrorCode>> TranslateBinary(
        ScalarBinary<object?> binary,
        Func<string, string> resolveFieldName,
        int depth)
    {
        string? mongoOperator = binary.Operator switch
        {
            BinaryOperator.Add => "$add",
            BinaryOperator.Subtract => "$subtract",
            BinaryOperator.Multiply => "$multiply",
            BinaryOperator.Divide => "$divide",
            BinaryOperator.Modulo => "$mod",
            // **`POWER` 不支持**：`$pow` 存在，但二元运算符映射里没有它。
            // **`POWER` is unsupported**: `$pow` exists, but the binary map omits it.
            BinaryOperator.Power => null,
            _ => null,
        };
        if (mongoOperator is null)
        {
            return Failure($"不支持的二元运算 / Unsupported binary operator: {binary.Operator}");
        }

        Result<BsonValue, ErrorCode, Error<ErrorCode>> left = Translate(binary.Left, resolveFieldName, depth + 1);
        if (!left.IsOk)
        {
            return left;
        }
        Result<BsonValue, ErrorCode, Error<ErrorCode>> right = Translate(binary.Right, resolveFieldName, depth + 1);
        if (!right.IsOk)
        {
            return right;
        }

        return Ok(new BsonDocument(mongoOperator, new BsonArray { left.Value, right.Value }));
    }

    private static Result<BsonValue, ErrorCode, Error<ErrorCode>> TranslateFunction(
        ScalarFunction<object?> function,
        Func<string, string> resolveFieldName,
        int depth)
    {
        // **函数名映射按清单来**：接了少数几个，其余一律失败。
        // **多接一个就多一处超出既定清单的行为。**
        //
        // **Function names follow a fixed list**: only a few are wired and the rest fail.
        // **Wiring an extra one would go beyond that list.**
        string? mongoOperator = function.Name.ToUpperInvariant() switch
        {
            "ABS" => "$abs",
            "CEIL" or "CEILING" => "$ceil",
            "FLOOR" => "$floor",
            "ROUND" => "$round",
            "SQRT" => "$sqrt",
            "EXP" => "$exp",
            "LN" => "$ln",
            "LOG10" => "$log10",
            _ => null,
        };
        if (mongoOperator is null)
        {
            return Failure($"不支持的标量函数 / Unsupported scalar function: {function.Name}");
        }

        var arguments = new BsonArray();
        foreach (ScalarExpression<object?> argument in function.Arguments)
        {
            Result<BsonValue, ErrorCode, Error<ErrorCode>> translated = Translate(argument, resolveFieldName, depth + 1);
            if (!translated.IsOk)
            {
                return translated;
            }
            arguments.Add(translated.Value);
        }

        // **单参数函数写成标量而不是数组**：`$abs: "$x"` 与 `$abs: ["$x"]` 在 Mongo 里都合法，
        // **但这里对单参数写标量**，两边形状不同会让管道比较失效。
        //
        // **A single-argument function is written as a scalar, not an array**: Mongo accepts both `$abs: "$x"` and
        // `$abs: ["$x"]`, **but a scalar is written here**, and differing shapes break pipeline comparison.
        return Ok(arguments.Count == 1
            ? new BsonDocument(mongoOperator, arguments[0])
            : new BsonDocument(mongoOperator, arguments));
    }

    /// <summary>把 CLR 值变成 Bson 值 / Turn a CLR value into a Bson value.</summary>
    /// <remarks>
    /// **`null` 必须变成 `BsonNull`**：Mongo 里"字段不存在"与"字段是 null"是两件事，
    /// **而 CLR 的 `null` 只表达后者**——用 `BsonNull` 而不是省掉，才能让后者被查出来。
    ///
    /// **`null` has to become `BsonNull`**: in Mongo "the field is absent" and "the field is null" are two things,
    /// **and a CLR `null` only expresses the latter** — using `BsonNull` rather than omitting it is what makes the latter findable.
    /// </remarks>
    private static BsonValue ToBson(object? value) => value switch
    {
        null => BsonNull.Value,
        BsonValue bson => bson,
        string text => new BsonString(text),
        bool flag => BsonBoolean.Create(flag),
        int number => new BsonInt32(number),
        long number => new BsonInt64(number),
        double number => new BsonDouble(number),
        decimal number => new BsonDecimal128(number),
        DateTime moment => new BsonDateTime(moment.ToUniversalTime()),
        _ => BsonValue.Create(value),
    };

    /// <summary>**`Result` 对 `BsonValue` 不变型**，所以每个分支都显式装箱 / `Result` is invariant over `BsonValue`, so each branch boxes explicitly.</summary>
    private static Result<BsonValue, ErrorCode, Error<ErrorCode>> Ok(BsonValue value) =>
        Results.Ok(value);

    private static Result<BsonValue, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        new Failed<BsonValue, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument,
            message,
            new ParseIssue(ParseIssueType.Semantic, message));

    /// <summary>嵌套深度上限 / Maximum nesting depth.</summary>
    /// <remarks>
    /// **与 `RelationalQueryExpressionFreezer` 的 64 层一致**——两处护栏不同会让"能冻结的表达式翻译不了"。
    /// **Matches the 64 levels in `RelationalQueryExpressionFreezer`** — differing guards would let an expression freeze but not translate.
    /// </remarks>
    public const int MaxDepth = 64;
}
