#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.MySQL;
/// <summary>
/// MySQL EF Core 仓储基类 / MySQL EF Core repository base class.
///
/// 提供基于 EF Core + MySqlConnector (Pomelo) 的通用仓储实现。
/// Provides a generic repository implementation backed by EF Core with MySQL.
/// </summary>
/// <typeparam name="T">实体类型 / Entity type</typeparam>
public abstract class MySqlRepository<T> : IRepository<T> where T : class {
    /// <summary>EF Core 数据库上下文 / EF Core database context.</summary>
    protected DbContext Context { get; }

    /// <summary>实体 DbSet / Entity DbSet.</summary>
    protected DbSet<T> Set { get; }

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="context">EF Core 数据库上下文 / EF Core database context</param>
    protected MySqlRepository(DbContext context) {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Set = context.Set<T>();
    }

    /// <inheritdoc />
    public virtual async Task<Result<T?, ErrorCode, Error<ErrorCode>>> FindByIdAsync(
        object id,
        CancellationToken cancellationToken = default) {
        try {
            T? entity = await Set.FindAsync(new[] { id }, cancellationToken);
            return Results.Ok<T?>(entity);
        }
        catch (Exception ex) {
            return new Failed<T?, ErrorCode, Error<ErrorCode>>(
                ErrorCode.DataNotFound, $"MySQL FindById failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public virtual async Task<Result<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>> FindAllAsync(
        CancellationToken cancellationToken = default) {
        try {
            List<T> results = await Set.ToListAsync(cancellationToken);
            return Results.Ok<IReadOnlyList<T>>(results);
        }
        catch (Exception ex) {
            return new Failed<IReadOnlyList<T>, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationError, $"MySQL FindAll failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public virtual async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InsertAsync(
        T entity,
        CancellationToken cancellationToken = default) {
        try {
            Set.Add(entity);
            await Context.SaveChangesAsync(cancellationToken);
            return Results.OkInstance;
        }
        catch (Exception ex) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, $"MySQL Insert failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public virtual async Task<Result<Success, ErrorCode, Error<ErrorCode>>> InsertManyAsync(
        IEnumerable<T> entities,
        CancellationToken cancellationToken = default) {
        try {
            Set.AddRange(entities);
            await Context.SaveChangesAsync(cancellationToken);
            return Results.OkInstance;
        }
        catch (Exception ex) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, $"MySQL InsertMany failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public virtual async Task<Result<long, ErrorCode, Error<ErrorCode>>> UpdateAsync(
        T entity,
        CancellationToken cancellationToken = default) {
        try {
            Set.Update(entity);
            int affected = await Context.SaveChangesAsync(cancellationToken);
            return Results.Ok((long)affected);
        }
        catch (Exception ex) {
            return new Failed<long, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, $"MySQL Update failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public virtual async Task<Result<Success, ErrorCode, Error<ErrorCode>>> DeleteAsync(
        object id,
        CancellationToken cancellationToken = default) {
        try {
            T? entity = await Set.FindAsync(new[] { id }, cancellationToken);
            if (entity is null) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    ErrorCode.DataNotFound, $"Entity with id {id} not found");
            }
            Set.Remove(entity);
            await Context.SaveChangesAsync(cancellationToken);
            return Results.OkInstance;
        }
        catch (Exception ex) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.ApplicationFailed, $"MySQL Delete failed: {ex.Message}");
        }
    }
}
