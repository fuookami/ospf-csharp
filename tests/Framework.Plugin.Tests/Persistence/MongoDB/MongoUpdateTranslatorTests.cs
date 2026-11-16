#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Expression;
using Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence.MongoTranslators;

/// <summary>
/// **更新写错比查询写错更贵** / A wrong update costs more than a wrong query.
/// </summary>
/// <remarks>
/// 基线 `MongoUpdateTranslator`（**120 行**）。补齐标量、排序，这里补更新。
///
/// **查询错了重新查一次就好，更新错了数据已经变了**——**而没有备份的地方，改错的值不会自己回来。**
///
/// The baseline's `MongoUpdateTranslator` (**120 lines**). Added the scalar translator, sorting and updates.
///
/// **A wrong query can be run again; a wrong update has already changed the data** — **and where there is no backup,
/// a wrong value does not come back.**
/// </remarks>
public class MongoUpdateTranslatorTests
{
    /// <summary>**`SetValue` 变成 `$set`** / `SetValue` becomes `$set`.</summary>
    [Fact]
    public void TheSetValue_ShouldBecomeASetDocument()
    {
        UpdateAssignments assignments = new UpdateAssignments().ThenSet("Name", "widget");

        Result<BsonDocument?, ErrorCode, Error<ErrorCode>> result =
            MongoUpdateTranslator.Translate(assignments);

        result.IsOk.Should().BeTrue();
        BsonDocument document = result.Value!;
        document.Contains("$set").Should().BeTrue("updates go under $set");
        document["$set"].AsBsonDocument["Name"].AsString.Should().Be("widget");
    }

    /// <summary>**`SetNull` 写 `BsonNull`，不是删字段** / `SetNull` writes `BsonNull`, it does not remove the field.</summary>
    /// <remarks>
    /// Mongo 里"把字段设成 null"（`$set: {f: null}`）与"删掉字段"（`$unset: {f: ""}`）**是两件事**。
    /// **写成 `$unset` 会让"这个字段是空"变成"这个字段不存在"**——
    /// **而下游查询"字段等于 null"就再也查不到它。**
    ///
    /// In Mongo "set the field to null" (`$set: {f: null}`) and "remove the field" (`$unset: {f: ""}`) **are two things**.
    /// **Writing `$unset` turns "this field is empty" into "this field does not exist"** —
    /// **and a downstream query for "field equals null" never finds it again.**
    /// </remarks>
    [Fact]
    public void TheSetNull_ShouldWriteBsonNullNotUnset()
    {
        UpdateAssignments assignments = new UpdateAssignments().ThenSetNull("Note");

        BsonDocument document = MongoUpdateTranslator.Translate(assignments).Value!;

        document.Contains("$unset").Should().BeFalse(
            "removing the field is not the same as setting it to null");
        document["$set"].AsBsonDocument["Note"].Should().Be(BsonNull.Value);
    }

    /// <summary>**空赋值返回 `null`，不是空 `$set`** / Empty assignments return `null`, not an empty `$set`.</summary>
    /// <remarks>
    /// **空 `$set: {}` 在 Mongo 里是合法但无意义的写**，而 `null` 让调用方整个省掉这次写。
    /// **基线原文是 `return Ok(null)`。**
    ///
    /// **An empty `$set: {}` is a legal but meaningless write in Mongo**, while `null` lets the caller skip it entirely.
    /// **The baseline reads `return Ok(null)`.**
    /// </remarks>
    [Fact]
    public void TheEmptyAssignments_ShouldBeNull()
    {
        MongoUpdateTranslator.Translate(new UpdateAssignments()).Value.Should().BeNull(
            "an empty update must not become an empty $set");
    }

    /// <summary>**字段解析失败即失败** / An unresolved field fails.</summary>
    [Fact]
    public void TheUnresolvedField_ShouldFail()
    {
        UpdateAssignments assignments = new UpdateAssignments().ThenSet("Amount", 5);

        MongoUpdateTranslator.Translate(assignments, name => "doc_" + name)
            .Value!["$set"].AsBsonDocument.Contains("doc_Amount").Should().BeTrue();

        MongoUpdateTranslator.Translate(assignments, _ => null).IsFailed.Should().BeTrue(
            "writing to a field that could not be resolved would update the wrong place");
    }

    /// <summary>**整数值必须是 `Int32`，否则写进去查不出来** / An integer must be `Int32`, or what was written cannot be found.</summary>
    /// <remarks>
    /// **Mongo 认为 `Int32(5)` 与 `Int64(5)` 不相等。**
    /// **如果写入用 `Int64` 而查询用 `Int32`，"刚写进去的 5"一条也查不到**——
    /// **而两边都"成功"了。**
    ///
    /// **Mongo treats `Int32(5)` and `Int64(5)` as unequal.**
    /// **If the write uses `Int64` while the query uses `Int32`, the 5 that was just written cannot be found** —
    /// **while both sides "succeeded".**
    /// </remarks>
    [Fact]
    public void TheInteger_ShouldKeepItsBsonType()
    {
        UpdateAssignments assignments = new UpdateAssignments()
            .ThenSet("Count", 5)
            .ThenSet("Total", 5L);

        BsonDocument set = MongoUpdateTranslator.Translate(assignments).Value!["$set"].AsBsonDocument;

        set["Count"].BsonType.Should().Be(BsonType.Int32,
            "an int stays Int32, matching what a query would build");
        set["Total"].BsonType.Should().Be(BsonType.Int64,
            "a long stays Int64, so its own queries still match");
    }

    /// <summary>**`SetFromExpression` 拒绝而不是猜** / `SetFromExpression` refuses rather than guesses.</summary>
    /// <remarks>
    /// **C# 的 `SetFromExpression` 拿的是 `string Expression`，基线拿的是 `ScalarExpression&lt;*&gt;`。**
    /// **字符串既可能是常量，也可能是一段没人解析过的表达式源码**——
    /// **猜它是常量，就是把一段未验证的文本当成数据写进库里。**
    ///
    /// **基线只接受 `ScalarConstant`**，其余 `assignmentFailure(..., "expression is not supported")`。
    ///
    /// **C# `SetFromExpression` carries a `string Expression` where the baseline carries a `ScalarExpression&lt;*&gt;`.**
    /// **A string may be a constant or unparsed expression source** —
    /// **guessing it is a constant writes unvalidated text into the database as data.**
    ///
    /// **The baseline accepts `ScalarConstant` only** and fails the rest with `"expression is not supported"`.
    /// </remarks>
    [Fact]
    public void TheExpressionAssignment_ShouldRefuseRatherThanGuess()
    {
        UpdateAssignments assignments = new UpdateAssignments().ThenSetExpr("Amount", "Price * 2");

        Result<BsonDocument?, ErrorCode, Error<ErrorCode>> result =
            MongoUpdateTranslator.Translate(assignments);

        result.IsFailed.Should().BeTrue(
            "a string cannot be shown to be a constant, and writing 'Price * 2' as a literal value would be wrong");
    }
}
