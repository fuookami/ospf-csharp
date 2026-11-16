#nullable enable

using Fuookami.Ospf.Framework.Persistence.Expression;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Parse;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;

/// <summary>**更新赋值 → Mongo `$set` 文档** / Update assignments → a Mongo `$set` document.</summary>
/// <remarks>
/// 本类把 `UpdateAssignments` 变成 `Updates.combine(...)`：
/// 每种赋值都落到 `$set`，字段名交给调用方的解析器。
///
/// **更新写错比查询写错更贵**：查询错了重新查一次就好，**更新错了数据已经变了**——
/// **而没有备份的地方，改错的值不会自己回来。**
///
/// The code's `MongoUpdateTranslator` (**120 lines**) turns `UpdateAssignments` into `Updates.combine(...)`:
/// every assignment lands on `$set`, and the field name goes through the caller's resolver.
///
/// **A wrong update costs more than a wrong query**: a wrong query can be run again,
/// **a wrong update has already changed the data** — **and where there is no backup, a wrong value does not come back.**
/// </remarks>
public static class MongoUpdateTranslator
{
    /// <summary>把一组赋值翻成 `$set` 文档 / Translate assignments into a `$set` document.</summary>
    /// <remarks>
    /// **空赋值返回 `null` 而不是 `$set: {}`**：`if (assignments.isEmpty) return Ok(null)`。
    /// **空 `$set` 在 Mongo 里是合法但无意义的更新**，而 `null` 让调用方整个省掉这次写。
    ///
    /// **Empty assignments return `null`, not `$set: {}`**: `if (assignments.isEmpty) return Ok(null)`.
    /// **An empty `$set` is legal but meaningless in Mongo**, while `null` lets the caller skip the write entirely.
    /// </remarks>
    public static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> Translate(
        UpdateAssignments assignments,
        Func<string, string?>? resolveFieldName = null)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        resolveFieldName ??= static name => name;

        if (assignments.Items.Count == 0)
        {
            return Results.Ok<BsonDocument?>(null);
        }

        var set = new BsonDocument();
        foreach (UpdateAssignment assignment in assignments.Items)
        {
            string? field = resolveFieldName(assignment.Field);
            if (field is null)
            {
                return Failure($"赋值字段翻译失败：字段 {assignment.Field}：字段未被解析 / "
                    + $"Assignment field translation failed: field {assignment.Field}: field was not resolved");
            }

            switch (assignment)
            {
                case UpdateAssignment.SetValue setValue:
                    set[field] = ToBson(setValue.Value);
                    break;

                case UpdateAssignment.SetNull:
                    // **`SetNull` 写 `BsonNull`**：Mongo 的 `$set` 里 null 是"把字段设成 null"，
                    // **与"删掉字段"（`$unset`）是两件事**——这里要的是前者。
                    //
                    // **`SetNull` writes `BsonNull`**: inside Mongo's `$set`, null means "set the field to null",
                    // **which is not the same as removing it (`$unset`)** — this is the former.
                    set[field] = BsonNull.Value;
                    break;

                case UpdateAssignment.SetFromExpression setFromExpression:
                    Result<BsonValue, ErrorCode, Error<ErrorCode>> value = TranslateExpression(setFromExpression);
                    if (!value.IsOk)
                    {
                        // **`Failed` 只接错误对象**：`new Failed<T, C, E>(E Error)`，
                        // **所以直接把内层的 `Error<ErrorCode>` 传上去，而不是再包一层。**
                        //
                        // **`Failed` takes the error object itself**: `new Failed<T, C, E>(E Error)`,
                        // **so the inner `Error<ErrorCode>` is passed straight through rather than wrapped again.**
                        return new Failed<BsonDocument?, ErrorCode, Error<ErrorCode>>(
                            ((Failed<BsonValue, ErrorCode, Error<ErrorCode>>)value).Error);
                    }
                    set[field] = value.Value;
                    break;

                default:
                    return Failure($"不支持的赋值类型 / Unsupported assignment: {assignment.GetType().Name}");
            }
        }

        return Results.Ok<BsonDocument?>(new BsonDocument("$set", set));
    }

    /// <summary>**表达式只接受常量** / The expression accepts constants only.</summary>
    /// <remarks>
    /// 写法是 `item.expression as? ScalarConstant&lt;*&gt; ?: return assignmentFailure(..., "expression is not supported")`——
    /// **只有常量能进来，其他一律失败。**
    ///
    /// **这不是偷懒**：Mongo 的 `$set` 里可以放聚合表达式，**但那会引入一整套作用域规则**，
    /// 而更新路径根本没打算支持它。**在这里"顺手支持"等于发明一个没人验证过的语义。**
    ///
    /// The code reads `item.expression as? ScalarConstant&lt;*&gt; ?: return assignmentFailure(..., "expression is not supported")` —
    /// **only constants get through; everything else fails.**
    ///
    /// **That is not laziness**: Mongo's `$set` can carry aggregation expressions, **but that brings a whole scoping model**,
    /// and its update path never intended to support it. **Supporting it "while here" invents a semantics nobody validated.**
    /// </remarks>
    private static Result<BsonValue, ErrorCode, Error<ErrorCode>> TranslateExpression(
        UpdateAssignment.SetFromExpression assignment)
    {
        // **`SetFromExpression` 拿的是 `string Expression`，而不是结构化的 `ScalarExpression&lt;*&gt;`。**
        // **这是两种不同的东西**：字符串既可能是常量，也可能是一段没人解析过的表达式源码。
        // **所以这里不猜**——只有能确定是常量的输入才被接受，而字符串无法区分，于是失败。
        //
        // **`SetFromExpression` carries a `string Expression` rather than a structured `ScalarExpression&lt;*&gt;`.
        // **Those are different things**: a string may be a constant or an unparsed expression source.
        // **So this does not guess** — only input known to be a constant is accepted, and a string cannot be distinguished, so it fails.
        return new Failed<BsonValue, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument,
            $"赋值表达式翻译失败：字段 {assignment.Field}："
            + "C# 的 `SetFromExpression` 只有字符串形式，无法判断它是否为常量，故拒绝 / "
            + $"Assignment expression translation failed: field {assignment.Field}: "
            + "the C# `SetFromExpression` carries only a string, so whether it is a constant cannot be decided, and it is refused",
            new ParseIssue(
                ParseIssueType.Semantic,
                "SetFromExpression carries a string, not a scalar expression"));
    }

    /// <summary>把 CLR 值变成 Bson 值 / Turn a CLR value into a Bson value.</summary>
    /// <remarks>
    /// **与 `MongoScalarTranslator` 用同一套映射**：两处不同会让"查询条件里的 5"与"写入的 5"变成不同的 Bson 类型，
    /// **而 Mongo 认为 `Int32(5)` 与 `Int64(5)` 不相等**——**于是写进去的值查不出来。**
    ///
    /// **Same mapping as `MongoScalarTranslator`**: differing here makes "5 in a query" and "5 written" different Bson types,
    /// **and Mongo treats `Int32(5)` and `Int64(5)` as unequal** — **so what was written cannot be found.**
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

    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        new Failed<BsonDocument?, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument,
            message,
            new ParseIssue(ParseIssueType.Semantic, message));
}
