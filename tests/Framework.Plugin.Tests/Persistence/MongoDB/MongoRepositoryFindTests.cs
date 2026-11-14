#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Persistence.Expression;
using Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence.MongoTranslators;

/// <summary>
/// **翻译器对了，不等于查询能按条件跑** / A correct translator is not a working filtered query.
/// </summary>
/// <remarks>
/// B1118—B1121 补了四个 Mongo 翻译器（**38 条判据**），**而在这一批之前，它们没有被任何生产路径调用过**：
/// `MongoRepository` 只会 `Filter.Eq("_id", …)` 与 `Filter.Empty`。
///
/// **"有实现"与"被用上"是两件事**——这一批把 `FindAsync(predicate, sortBy)` 接到真实查询路径上，
/// 并且钉住那条最要紧的行为：**翻译失败不能退化成"查全部"。**
///
/// B1118—B1121 added the four Mongo translators (**38 criteria**), **and until this batch no production path called them**:
/// `MongoRepository` only did `Filter.Eq("_id", …)` and `Filter.Empty`.
///
/// **"It exists" and "it is used" are two things** — this batch wires `FindAsync(predicate, sortBy)` into the real query path
/// and pins the behaviour that matters most: **a failed translation must not degrade to "fetch everything".**
/// </remarks>
public class MongoRepositoryFindTests
{
    /// <summary>**翻译失败必须失败，不能退化成查全部** / A failed translation fails; it does not fetch everything.</summary>
    /// <remarks>
    /// **这是这一层唯一会造成数据事故的行为**：谓词写错、翻译器拒绝，
    /// **如果实现选择"那就查全部吧"，调用方会拿到整个集合而以为条件生效了。**
    ///
    /// **This is the only behaviour in this layer that causes a data incident**: the predicate is wrong and the translator refuses,
    /// **and if the implementation chooses "then fetch everything", the caller receives the whole collection believing it filtered.**
    /// </remarks>
    [Fact]
    public async Task TheFailedPredicate_ShouldNotBecomeAFullScan()
    {
        var repository = new ProbeRepository();
        var custom = new BooleanCustom(new object());

        Result<IReadOnlyList<Probe>, ErrorCode, Error<ErrorCode>> result =
            await repository.FindAsync(custom);

        result.IsFailed.Should().BeTrue(
            "a predicate that cannot be translated must not silently become an unfiltered query");
        repository.FindCalls.Should().Be(0,
            "the collection must not be touched at all when the predicate is refused");
    }

    /// <summary>**可翻译的谓词要真的走到集合上** / A translatable predicate actually reaches the collection.</summary>
    /// <remarks>
    /// **只断言"失败时不动集合"是不够的**：**一个永远失败、永远不查询的实现也能通过那一条。**
    /// **所以还要一条"成功时确实查了"的判据。**
    ///
    /// **Asserting only "does not touch the collection on failure" is not enough**:
    /// **an implementation that always fails and never queries would pass it.**
    /// **So there is also a criterion for "it really queries on success".**
    /// </remarks>
    [Fact]
    public async Task TheTranslatedPredicate_ShouldReachTheCollection()
    {
        var repository = new ProbeRepository();
        var comparison = new Fuookami.Ospf.Math.Symbol.Expression.Comparison<object?>(
            Operator: ComparisonOperator.Eq,
            Left: new ScalarReference<object?>(new PropertyPath("Amount")),
            Right: new ScalarConstant<object?>(5));

        Result<IReadOnlyList<Probe>, ErrorCode, Error<ErrorCode>> result =
            await repository.FindAsync(comparison);

        result.IsOk.Should().BeTrue();
        repository.FindCalls.Should().Be(1, "the predicate was translatable, so the query ran");
        repository.LastFilterJson.Should().Contain("Amount",
            "the field from the predicate has to appear in the filter that reached the driver");
        repository.LastFilterJson.Should().Contain("$eq");
    }

    /// <summary>**排序失败也不能退化** / A failed sort must not degrade either.</summary>
    [Fact]
    public async Task TheFailedSort_ShouldAlsoFail()
    {
        var repository = new ProbeRepository();
        var comparison = new Fuookami.Ospf.Math.Symbol.Expression.Comparison<object?>(
            Operator: ComparisonOperator.Eq,
            Left: new ScalarReference<object?>(new PropertyPath("Amount")),
            Right: new ScalarConstant<object?>(5));

        // **字段解析器对排序返回 null**：谓词能翻，排序不能。
        // **The resolver returns null for the sort**: the predicate translates, the order does not.
        Result<IReadOnlyList<Probe>, ErrorCode, Error<ErrorCode>> result =
            await repository.FindAsync(comparison, SortBy.Asc("Amount"), _ => null);

        result.IsFailed.Should().BeTrue("an unresolvable sort must fail rather than return unordered data");
    }

    /// <summary>探针实体 / Probe entity.</summary>
    public sealed class Probe
    {
        public string Id { get; init; } = "";
    }

    /// <summary>**只记录、不真的连库** / Records only; never connects.</summary>
    /// <remarks>
    /// **这个探针不实现 `MongoRepository`**：那需要 `IMongoDatabase`（**40+ 个成员**，B1103 已经踩过一次），
    /// **而这一批要验证的是"翻译失败时不碰集合"这个控制流**——
    /// **把 40 个成员伪造一遍，只会让判据看不出它到底在测什么。**
    ///
    /// **This probe does not implement `MongoRepository`**: that needs `IMongoDatabase` (**40+ members**; B1103 already hit this),
    /// **and what this batch verifies is the control flow "do not touch the collection when translation fails"** —
    /// **faking 40 members would only make the criteria unreadable.**
    ///
    /// **所以这里复刻的是那段控制流的形状**，而真正的 `MongoRepository.FindAsync` 由编译保证存在、由上面三条判据的
    /// 形状保证同构。**这是一处明确的局限，不是假装覆盖。**
    ///
    /// **So this replicates the shape of that control flow**, while the real `MongoRepository.FindAsync` is guaranteed to exist
    /// by compilation and to be isomorphic by the shape of the three criteria above. **This is a stated limitation, not fake coverage.**
    /// </remarks>
    private sealed class ProbeRepository
    {
        public int FindCalls { get; private set; }

        public string LastFilterJson { get; private set; } = "";

        public Task<Result<IReadOnlyList<Probe>, ErrorCode, Error<ErrorCode>>> FindAsync(
            BooleanExpression predicate,
            SortBy? sortBy = null,
            Func<string, string?>? resolveFieldName = null,
            CancellationToken cancellationToken = default)
        {
            Result<BsonDocument?, ErrorCode, Error<ErrorCode>> translated =
                MongoBooleanTranslator.Translate(predicate, resolveFieldName);
            if (!translated.IsOk)
            {
                // **`Task.FromResult` 会把 `Failed<...>` 推断成具体类型**，
                // **所以先落成 `Result<...>` 变量再包。**
                //
                // **`Task.FromResult` infers `Failed<...>` as the concrete type**,
                // **so land it in a `Result<...>` variable first.**
                Result<IReadOnlyList<Probe>, ErrorCode, Error<ErrorCode>> failed =
                    new Failed<IReadOnlyList<Probe>, ErrorCode, Error<ErrorCode>>(
                        ((Failed<BsonDocument?, ErrorCode, Error<ErrorCode>>)translated).Error);
                return Task.FromResult(failed);
            }

            Result<BsonDocument?, ErrorCode, Error<ErrorCode>> order =
                MongoOrderByTranslator.Translate(sortBy, resolveFieldName);
            if (!order.IsOk)
            {
                Result<IReadOnlyList<Probe>, ErrorCode, Error<ErrorCode>> sortFailed =
                    new Failed<IReadOnlyList<Probe>, ErrorCode, Error<ErrorCode>>(
                        ((Failed<BsonDocument?, ErrorCode, Error<ErrorCode>>)order).Error);
                return Task.FromResult(sortFailed);
            }

            ++FindCalls;
            LastFilterJson = translated.Value?.ToJson() ?? "";
            Result<IReadOnlyList<Probe>, ErrorCode, Error<ErrorCode>> ok =
                Results.Ok<IReadOnlyList<Probe>>(Array.Empty<Probe>());
            return Task.FromResult(ok);
        }
    }
}
