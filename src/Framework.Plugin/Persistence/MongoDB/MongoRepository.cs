#nullable enable

using Fuookami.Ospf.Framework.Persistence.Expression;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;
/// <summary>
/// MongoDB 仓储基类 / MongoDB repository base class.
///
/// 提供基于 MongoDB.Driver 的通用仓储实现。
/// Provides a generic repository implementation backed by MongoDB.Driver.
/// </summary>
/// <typeparam name="T">实体类型 / Entity type</typeparam>
public abstract class MongoRepository<T> : IRepository<T> where T : class {
    /// <summary>Mongo 数据库实例 / MongoDB database instance.</summary>
    protected IMongoDatabase Database { get; }

    /// <summary>集合名称 / Collection name.</summary>
    protected string CollectionName { get; }

    /// <summary>Mongo 集合 / Mongo collection.</summary>
    protected IMongoCollection<T> Collection =>
        Database.GetCollection<T>(CollectionName);

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="database">Mongo 数据库 / MongoDB database</param>
    /// <param name="collectionName">集合名称 / Collection name</param>
    protected MongoRepository(IMongoDatabase database, string collectionName) {
        Database = database ?? throw new ArgumentNullException(nameof(database));
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
    }

    /// <inheritdoc />
    public virtual async Task<Result<T?, ErrorCode, Error<ErrorCode>>> FindByIdAsync(
        object id,
        CancellationToken cancellationToken = default) {
        try {
            FilterDefinition<T> filter = Builders<T>.Filter.Eq("_id", BsonValue.Create(id));
            T result = await Collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
            return Results.Ok<T?>(result);
        }
        catch (Exception ex) {
            return new Failed<T?, ErrorCode, Error<ErrorCode>>(
                ErrorCode.DataNotFound, $"MongoDB FindById failed: {ex.Message}");
        }
    }

    /// <summary>**按布尔表达式查询** / Query by a boolean expression.</summary>
    /// <remarks>
    /// **在此之前，这个仓库只会按 `_id` 查**：`FindByIdAsync` 用 `Filter.Eq("_id", …)`，
    /// `FindAllAsync` 用 `Filter.Empty`。**四个 Mongo 翻译器（共 38 条判据）没有被任何生产路径调用过**——
    /// **而"翻译器对了"与"查询能按条件跑"是两件事。**
    ///
    /// **Before this, the repository only queried by `_id`**: `FindByIdAsync` uses `Filter.Eq("_id", …)` and
    /// `FindAllAsync` uses `Filter.Empty`. **The four Mongo translators (38 criteria) were called by no
    /// production path** — **and "the translator is right" is not the same as "a filtered query can run".**
    /// </remarks>
    /// <param name="predicate">布尔表达式 / Boolean expression.</param>
    /// <param name="sortBy">排序，可选 / Sort order, optional.</param>
    /// <param name="resolveFieldName">字段名解析器 / Field-name resolver.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    public virtual async Task<Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>> FindAsync(
        BooleanExpression predicate,
        SortBy? sortBy = null,
        Func<string, string?>? resolveFieldName = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(predicate);

        // **翻译失败要原样返回失败，不能退化成"查全部"**：
        // **退化成查全部会让一个写错的谓词返回整个集合，而调用方以为条件生效了。**
        //
        // **A failed translation is returned as a failure, never degraded to "fetch everything"**:
        // **degrading returns the whole collection for a mistyped predicate while the caller believes it filtered.**
        Result<BsonDocument?, ErrorCode, Error<ErrorCode>> translated =
            MongoBooleanTranslator.Translate(predicate, resolveFieldName);
        if (!translated.IsOk) {
            return new Failed<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>(
                ((Failed<BsonDocument?, ErrorCode, Error<ErrorCode>>)translated).Error);
        }

        Result<BsonDocument?, ErrorCode, Error<ErrorCode>> order =
            MongoOrderByTranslator.Translate(sortBy, resolveFieldName);
        if (!order.IsOk) {
            return new Failed<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>(
                ((Failed<BsonDocument?, ErrorCode, Error<ErrorCode>>)order).Error);
        }

        try {
            FilterDefinition<T> filter = translated.Value is { ElementCount: > 0 }
                ? new BsonDocumentFilterDefinition<T>(translated.Value)
                : Builders<T>.Filter.Empty;
            // **没有排序时不调 `Sort`**：`Builders<T>.Sort` 没有 `Empty`，
            // **而传一个空 `BsonDocument` 会让 Mongo 收到 `$sort: {}`**——那是合法但无意义的阶段。
            //
            // **Skip `Sort` when there is no order**: `Builders<T>.Sort` has no `Empty`,
            // **and passing an empty `BsonDocument` sends Mongo a `$sort: {}`** — legal but meaningless.
            IFindFluent<T, T> query = Collection.Find(filter);
            if (order.Value is not null) {
                query = query.Sort(new BsonDocumentSortDefinition<T>(order.Value));
            }

            List<T> results = await query.ToListAsync(cancellationToken);
            return Results.Ok<IReadOnlyList<T>>(results);
        }
        catch (Exception ex) {
            return new Failed<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, $"MongoDB Find failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public virtual async Task<Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>> FindAllAsync(
        CancellationToken cancellationToken = default) {
        try {
            List<T> results = await Collection.Find(Builders<T>.Filter.Empty).ToListAsync(cancellationToken);
            return Results.Ok<IReadOnlyList<T>>(results);
        }
        catch (Exception ex) {
            return new Failed<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationError, $"MongoDB FindAll failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public virtual async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InsertAsync(
        T entity,
        CancellationToken cancellationToken = default) {
        try {
            await Collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
            return Results.OkInstance;
        }
        catch (Exception ex) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, $"MongoDB Insert failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public virtual async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InsertManyAsync(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default) {
        try {
            await Collection.InsertManyAsync(entities, cancellationToken: cancellationToken);
            return Results.OkInstance;
        }
        catch (Exception ex) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, $"MongoDB InsertMany failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public virtual async Task<Result<long, ErrorCode, Error<ErrorCode>>> UpdateAsync(
        T entity,
        CancellationToken cancellationToken = default) {
        try {
            // Subclasses should override for proper update semantics.
            // Default: replace-one by id extracted from entity.
            BsonValue id = GetEntityId(entity);
            FilterDefinition<T> filter = Builders<T>.Filter.Eq("_id", id);
            ReplaceOneResult result = await Collection.ReplaceOneAsync(filter, entity, cancellationToken: cancellationToken);
            return Results.Ok((long)result.ModifiedCount);
        }
        catch (Exception ex) {
            return new Failed<long, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, $"MongoDB Update failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public virtual async Task<Result<Success, ErrorCode, Error<ErrorCode>>> DeleteAsync(
        object id,
        CancellationToken cancellationToken = default) {
        try {
            FilterDefinition<T> filter = Builders<T>.Filter.Eq("_id", BsonValue.Create(id));
            await Collection.DeleteOneAsync(filter, cancellationToken);
            return Results.OkInstance;
        }
        catch (Exception ex) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, $"MongoDB Delete failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取实体 ID（子类可重写）/ Get entity ID (subclass may override).
    /// </summary>
    protected virtual BsonValue GetEntityId(T entity) {
        // Default implementation attempts to reflect on an "Id" property.
        PropertyInfo? prop = typeof(T).GetProperty("Id") ?? typeof(T).GetProperty("id");
        if (prop is null) {
            throw new InvalidOperationException(
                $"Entity type {typeof(T).Name} does not have an Id property. Override GetEntityId.");
        }
        return BsonValue.Create(prop.GetValue(entity));
    }
}
