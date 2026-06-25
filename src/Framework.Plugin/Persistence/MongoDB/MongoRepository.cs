#nullable enable

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
