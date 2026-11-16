#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Plugin.Persistence.InMemory;


/// <remarks>
/// **这个类是内存实现，不是 EF Core。** 它持有一个 `List&lt;T&gt;`，不接触 `DbContext`；名字沿用
/// `KtormRepository` / `JdbcClientRepository` 那一格，但行为必须诚实地反映"内存"。
///
/// 第一版有两个静默空洞：
///
/// * `GetByIdAsync` 对**任何** id 都返回 `default`——调用方拿到的是"没找到"，而实际上它从来就没找过；
/// * `UpdateAsync` 是 `Task.CompletedTask`——写入被吞掉，没有任何信号。
///
/// 两个都改成有信号：给了键匹配器就真的按 id 找，没给就 `NotSupportedException` 并说明要什么；
/// 更新找不到实体也直接抛，而不是假装成功。
///
/// **This class is an in-memory implementation, not EF Core.** It holds a `List&lt;T&gt;` and never touches a `DbContext`; the name follows its
/// `KtormRepository` / `JdbcClientRepository` slot, but the behaviour must honestly say "in memory". The first version had two silent holes: `GetByIdAsync` returned `default`
/// for **any** id — the caller reads "not found" while nothing was ever looked up — and `UpdateAsync` was `Task.CompletedTask`, swallowing the write with no signal. Both now
/// signal: with a key matcher it really looks the id up, without one it throws `NotSupportedException` naming what is needed, and an update that finds no entity throws instead of
/// pretending to succeed.
/// </remarks>
public sealed class InMemoryRepository<T> where T : class {
    private readonly List<T> _store = new();

    private readonly Func<T, object, bool>? _keyMatcher;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="keyMatcher">键匹配器：给出实体与 id 是否对应 / Key matcher: whether an entity matches an id.</param>
    public InMemoryRepository(Func<T, object, bool>? keyMatcher = null) {
        _keyMatcher = keyMatcher;
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">没有键匹配器时无法按 id 查找 / Without a key matcher there is no way to look an id up.</exception>
    public Task<T?> GetByIdAsync(object id, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(id);
        if (_keyMatcher is null) {
            throw new NotSupportedException(
                "InMemoryRepository 没有键匹配器就无法按 id 查找；请在构造时传入 keyMatcher / "
                + "InMemoryRepository has no key matcher, so it cannot look an id up — pass one to the constructor");
        }
        return Task.FromResult(_store.FirstOrDefault(entity => _keyMatcher(entity, id)));
    }

    public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T>>(_store.AsReadOnly());

    public Task<IReadOnlyList<T>> FindAsync(Func<T, bool> predicate, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<T>>(_store.Where(predicate).ToList().AsReadOnly());

    public Task<T> AddAsync(T entity, CancellationToken ct = default) {
        _store.Add(entity);
        return Task.FromResult(entity);
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">实体不在库里 / The entity is not in the store.</exception>
    public Task UpdateAsync(T entity, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(entity);
        int index = _store.FindIndex(stored => ReferenceEquals(stored, entity) || Equals(stored, entity));
        if (index < 0) {
            throw new InvalidOperationException(
                "更新失败：实体不在库里 / update failed: the entity is not in the store");
        }
        _store[index] = entity;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity, CancellationToken ct = default) {
        _store.Remove(entity);
        return Task.CompletedTask;
    }
}
