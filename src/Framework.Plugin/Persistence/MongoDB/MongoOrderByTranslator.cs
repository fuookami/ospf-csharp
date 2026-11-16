#nullable enable

using Fuookami.Ospf.Framework.Persistence.Expression;
using Fuookami.Ospf.Math.Symbol.Parse;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;

/// <summary>**排序 → Mongo `$sort` 文档** / Sort → a Mongo `$sort` document.</summary>
/// <remarks>
/// 基线 `MongoOrderByTranslator`（**83 行**）把 `SortBy` 变成 `Sorts.orderBy(...)`：
/// 升序写 `1`、降序写 `-1`，字段名交给调用方的解析器。
///
/// **排序错了不会报错，只会让结果顺序不对**——**而分页正建立在顺序之上**：
/// 顺序不稳，第 2 页会重复或漏掉第 1 页的记录。**这是最容易"看起来正常"的一类错误。**
///
/// The code's `MongoOrderByTranslator` (**83 lines**) turns a `SortBy` into `Sorts.orderBy(...)`:
/// ascending writes `1`, descending writes `-1`, and the field name goes through the caller's resolver.
///
/// **A wrong sort does not throw; it only puts results in the wrong order** — **and paging is built on order**:
/// unstable order makes page 2 repeat or skip records from page 1. **This is the kind of error that most looks fine.**
/// </remarks>
public static class MongoOrderByTranslator
{
    /// <summary>把一个 `SortBy` 翻成 `$sort` 文档 / Translate a `SortBy` into a `$sort` document.</summary>
    /// <remarks>
    /// **空排序返回 `null` 而不是空文档**：基线原文是 `if (sortBy == null || sortBy.isEmpty) return Ok(null)`。
    /// **空 `$sort: {}` 在 Mongo 里是合法但无意义的阶段**，而返回 `null` 让调用方整个省掉这个阶段。
    ///
    /// **An empty sort returns `null`, not an empty document**: the code reads
    /// `if (sortBy == null || sortBy.isEmpty) return Ok(null)`.
    /// **An empty `$sort: {}` is legal but meaningless in Mongo**, while `null` lets the caller drop the stage entirely.
    /// </remarks>
    public static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> Translate(
        SortBy? sortBy,
        Func<string, string?>? resolveFieldName = null)
    {
        resolveFieldName ??= static name => name;

        if (sortBy is null || sortBy.Items.Count == 0)
        {
            return Results.Ok<BsonDocument?>(null);
        }

        var document = new BsonDocument();
        foreach (SortItem item in sortBy.Items)
        {
            string? field = resolveFieldName(item.Field);
            if (field is null)
            {
                // **字段解析失败要失败，不能跳过**：跳过会让"按这个字段排序"变成"不按它排序"，
                // **而结果依然返回，只是顺序不对**——**分页于是从第一页就开始错。**
                //
                // **An unresolved field must fail rather than be skipped**: skipping turns "sort by this field" into
                // "do not sort by it", **and the query still returns, just in the wrong order** — **paging then goes wrong from page one.**
                return Failure($"排序字段翻译失败：字段 {item.Field}：字段未被解析 / "
                    + $"Sort field translation failed: field {item.Field}: field was not resolved");
            }

            // **`Nulls` 在没有**：C# 的 `SortItem` 多一个 `NullsOrder? Nulls`，
            // **而`MongoOrderByTranslator` 不看它**。
            // **这里也不看，但要说出来**——静默忽略一个调用方明确设置的选项，
            // **比不支持更糟：调用方以为它生效了。**
            //
            // **`Nulls` does not exist in the code**: the C# `SortItem` carries an extra `NullsOrder? Nulls`,
            // **and its `MongoOrderByTranslator` never looks at it.**
            // **Neither does this, but it says so** — silently ignoring an option the caller explicitly set
            // **is worse than not supporting it: the caller believes it took effect.**
            if (item.Nulls is not null)
            {
                return Failure($"排序字段 {item.Field} 指定了 nulls 顺序，"
                    + "而 Mongo 的 `$sort` 不支持按字段指定 / "
                    + $"field {item.Field} asks for a nulls order, which Mongo's `$sort` does not take per field");
            }

            document[field] = item.Direction == SortDirection.Desc ? -1 : 1;
        }

        return Results.Ok<BsonDocument?>(document);
    }

    private static Result<BsonDocument?, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        new Failed<BsonDocument?, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument,
            message,
            new ParseIssue(ParseIssueType.Semantic, message));
}
