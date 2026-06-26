#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.EfCore;

public interface IRepository<T> where T : class {
    Task<T?> GetByIdAsync(object id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<T>> FindAsync(Func<T, bool> predicate, CancellationToken ct = default);
    Task<T> AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(T entity, CancellationToken ct = default);
}

public class EfCoreRepository<T> : IRepository<T> where T : class {
    private readonly List<T> _store = new();

    public Task<T?> GetByIdAsync(object id, CancellationToken ct = default)
        => Task.FromResult<T?>(default);

    public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T>>(_store.AsReadOnly());

    public Task<IReadOnlyList<T>> FindAsync(Func<T, bool> predicate, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T>>(_store.Where(predicate).ToList().AsReadOnly());

    public Task<T> AddAsync(T entity, CancellationToken ct = default) {
        _store.Add(entity);
        return Task.FromResult(entity);
    }

    public Task UpdateAsync(T entity, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteAsync(T entity, CancellationToken ct = default) {
        _store.Remove(entity);
        return Task.CompletedTask;
    }
}
