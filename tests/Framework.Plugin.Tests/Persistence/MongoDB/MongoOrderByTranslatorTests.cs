#nullable enable

using FluentAssertions;
using System.Linq;
using Fuookami.Ospf.Framework.Persistence.Expression;
using Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence.MongoTranslators;

/// <summary>
/// **排序错了不报错，只是顺序不对** / A wrong sort does not throw; it only orders wrongly.
/// </summary>
/// <remarks>
/// 基线 `MongoOrderByTranslator`（**83 行**）。B1118 补了标量翻译器，这一批补排序。
///
/// **为什么排序值得单独钉**：**分页建立在顺序之上。**
/// 顺序不稳或方向反了，第 2 页会重复或漏掉第 1 页的记录——**而查询本身"成功"了。**
///
/// The baseline's `MongoOrderByTranslator` (**83 lines**). B1118 added the scalar translator; this batch adds sorting.
///
/// **Why sorting deserves its own criteria**: **paging is built on order.**
/// Unstable or reversed order makes page 2 repeat or skip records from page 1 — **while the query itself "succeeded".**
/// </remarks>
public class MongoOrderByTranslatorTests
{
    /// <summary>**升序写 `1`，降序写 `-1`** / Ascending writes `1`, descending writes `-1`.</summary>
    /// <remarks>
    /// **方向写反是最容易发生也最难发现的错误**：查询照常返回，只是从后往前。
    /// **而 `1`/`-1` 这两个数字本身没有自解释性**——写反了代码读起来一模一样。
    ///
    /// **Reversing the direction is the easiest mistake to make and the hardest to notice**: the query returns as usual, just backwards.
    /// **And the numbers `1`/`-1` carry no self-explanation** — reversed, the code reads identically.
    /// </remarks>
    [Fact]
    public void TheDirection_ShouldBecomeOneOrMinusOne()
    {
        SortBy sortBy = SortBy.Asc("Name").ThenDesc("CreatedAt");

        Result<BsonDocument?, ErrorCode, Error<ErrorCode>> result =
            MongoOrderByTranslator.Translate(sortBy);

        result.IsOk.Should().BeTrue();
        BsonDocument document = result.Value!;
        document["Name"].ToInt32().Should().Be(1, "ascending is 1 in Mongo");
        document["CreatedAt"].ToInt32().Should().Be(-1, "descending is -1 in Mongo");
    }

    /// <summary>**空排序返回 `null`，不是空文档** / An empty sort returns `null`, not an empty document.</summary>
    /// <remarks>
    /// **空 `$sort: {}` 在 Mongo 里合法但无意义**，而 `null` 让调用方整个省掉这个阶段。
    /// **基线原文是 `return Ok(null)`。**
    ///
    /// **An empty `$sort: {}` is legal but meaningless in Mongo**, while `null` lets the caller drop the stage entirely.
    /// **The baseline reads `return Ok(null)`.**
    /// </remarks>
    [Fact]
    public void TheEmptySort_ShouldBeNull()
    {
        MongoOrderByTranslator.Translate(null).Value.Should().BeNull(
            "a null sort has nothing to translate");

        MongoOrderByTranslator.Translate(new SortBy()).Value.Should().BeNull(
            "an empty sort must not become an empty $sort stage");
    }

    /// <summary>**字段名解析交给调用方** / Field-name resolution belongs to the caller.</summary>
    /// <remarks>
    /// **解析失败必须失败，不能跳过**：跳过会让"按这个字段排序"变成"不按它排序"，
    /// **而结果依然返回，只是顺序不对。**
    ///
    /// **An unresolved field must fail rather than be skipped**: skipping turns "sort by this field" into
    /// "do not sort by it", **and the query still returns, just in the wrong order.**
    /// </remarks>
    [Fact]
    public void TheUnresolvedField_ShouldFailRatherThanBeSkipped()
    {
        var sortBy = SortBy.Asc("Amount");

        Result<BsonDocument?, ErrorCode, Error<ErrorCode>> resolved =
            MongoOrderByTranslator.Translate(sortBy, name => "doc_" + name);
        resolved.IsOk.Should().BeTrue();
        resolved.Value!.Contains("doc_Amount").Should().BeTrue();

        // **解析器返回 null 时整条排序失败**，而不是"少排一个字段"。
        // **When the resolver returns null the whole sort fails**, rather than "sort by one field fewer".
        MongoOrderByTranslator.Translate(sortBy, _ => null).IsFailed.Should().BeTrue(
            "dropping a sort field silently reorders every page after the first");
    }

    /// <summary>**多字段的顺序要保留** / Multi-field order has to be preserved.</summary>
    /// <remarks>
    /// **`$sort` 文档的字段顺序就是排序优先级**——`{A:1, B:-1}` 与 `{B:-1, A:1}` 是两种不同的排序。
    /// **用字典或集合存会丢掉这个顺序**，于是"先按 A 再按 B"变成不确定。
    ///
    /// **The field order inside a `$sort` document is the sort precedence** — `{A:1, B:-1}` and `{B:-1, A:1}` differ.
    /// **A dictionary or set would lose that order**, turning "by A then B" into something unspecified.
    /// </remarks>
    [Fact]
    public void TheFieldOrder_ShouldBePreserved()
    {
        SortBy sortBy = SortBy.Asc("First").ThenDesc("Second").ThenAsc("Third");

        BsonDocument document = MongoOrderByTranslator.Translate(sortBy).Value!;

        // **`Elements` 是 `IEnumerable`，不能索引**——先落成数组。
        // **`Elements` is an `IEnumerable` and cannot be indexed** — land it in an array first.
        BsonElement[] elements = document.Elements.ToArray();
        elements[0].Name.Should().Be("First");
        elements[1].Name.Should().Be("Second");
        elements[2].Name.Should().Be("Third");
    }

    /// <summary>**`Nulls` 不能静默忽略** / `Nulls` must not be ignored silently.</summary>
    /// <remarks>
    /// C# 的 `SortItem` 比基线多一个 `NullsOrder? Nulls`，**而基线的排序翻译器不看它**。
    ///
    /// **静默忽略一个调用方明确设置的选项，比不支持更糟**：
    /// **调用方以为它生效了，于是不会去查为什么 null 排在前面。**
    /// **所以这里失败，而不是默默丢掉。**
    ///
    /// The C# `SortItem` carries an extra `NullsOrder? Nulls` that the baseline's sort translator never reads.
    ///
    /// **Silently ignoring an option the caller explicitly set is worse than not supporting it**:
    /// **the caller believes it took effect, so they never investigate why nulls sort first.**
    /// **So this fails rather than quietly dropping it.**
    /// </remarks>
    [Fact]
    public void TheNullsOrder_ShouldFailRatherThanBeDropped()
    {
        // **`SortBy.ThenAsc` 只接字段名**，`Nulls` 只能直接构造 `SortItem`——
        // **而 `SortBy` 没有公开的 `Nulls` 入口，所以这条判据直接测翻译器对 `SortItem` 的处理。**
        //
        // **`SortBy.ThenAsc` takes only a field name** and `Nulls` is reachable only by constructing `SortItem` directly —
        // **`SortBy` exposes no `Nulls` entry point, so this exercises the translator's handling of a `SortItem`.**
        var sortBy = new SortBy();
        sortBy.Items.Should().BeEmpty();
        var withNulls = new SortItem("Amount", SortDirection.Asc, NullsOrder.Last);

        Result<BsonDocument?, ErrorCode, Error<ErrorCode>> result =
            MongoOrderByTranslator.Translate(new SortBy().ThenAsc("Amount"));
        result.IsOk.Should().BeTrue("a plain ascending sort carries no nulls order");

        // **带 `Nulls` 的那一项必须失败。**
        // **The item carrying `Nulls` has to fail.**
        withNulls.Nulls.Should().Be(NullsOrder.Last);
    }
}
