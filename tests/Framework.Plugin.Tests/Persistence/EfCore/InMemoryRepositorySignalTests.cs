// 内存仓储不再静默（B721）/ The in-memory repository stops being silent (B721).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.InMemory;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence.EfCore;

/// <summary>
/// **内存仓储不再静默（B721）** / The in-memory repository stops being silent (B721).
/// </summary>
/// <remarks>
/// `InMemoryRepository` 是第一版叫 `EfCoreRepository` 的那个类：**名字里有 EF Core，行为上是一个 `List&lt;T&gt;`**。
/// 名字撒谎比没有名字更糟，所以先改名（B723），再处理两个**静默空洞**：
///
/// * `GetByIdAsync` 对任何 id 都返回 `default`——调用方读成"没找到"，而它从来就没找过；
/// * `UpdateAsync` 是 `Task.CompletedTask`——写入被吞掉，没有任何信号。
///
/// 这一批判据钉的是"有信号"：给键匹配器就真按 id 找，不给就抛并说明缺什么；更新找不到实体也抛。
///
/// `InMemoryRepository` is the class first named `EfCoreRepository`: **EF Core in the name, a `List&lt;T&gt;` in the body.** A lying name is worse than none, so B723 renames it first, then handles two
/// **silent holes**: `GetByIdAsync` returned `default` for any id, which the caller reads as "not found" while nothing was ever looked up, and `UpdateAsync` was
/// `Task.CompletedTask`, swallowing the write with no signal. These criteria pin "it signals": with a key matcher it really looks the id up, without one it throws naming what is
/// missing, and an update that finds no entity throws too.
/// </remarks>
public class InMemoryRepositorySignalTests {
    /// <remarks>
    /// **按 `Id` 判等**，不按引用：更新判据必须用**另一个实例**去覆盖，否则"改了原对象"和"真的写进库"看起来一样——
    /// 第一版就是这样，把 `_store[index] = entity` 删掉判据照样通过。
    ///
    /// **Equality by `Id`, not by reference**: the update criterion must overwrite with **a different instance**, otherwise "the original object was mutated" and "the write really landed"
    /// look identical — which is exactly how the first version passed with `_store[index] = entity` deleted.
    /// </remarks>
    private sealed class Row : IEquatable<Row> {
        public Row(int id, string name) {
            Id = id;
            Name = name;
        }

        public int Id { get; }

        public string Name { get; }

        public bool Equals(Row? other) => other is not null && other.Id == Id;

        public override bool Equals(object? obj) => Equals(obj as Row);

        public override int GetHashCode() => Id;
    }

    private static InMemoryRepository<Row> Repository() =>
        new((row, id) => row.Id == (int)id);

    /// <summary>
    /// **没有键匹配器时按 id 查找直接抛，而不是返回 `default`** / Without a key matcher, looking an id up throws rather than returning `default`.
    /// </summary>
    /// <remarks>
    /// 三点成对：无匹配器 → `NotSupportedException`（不是 `null`）/ 消息里点名 `keyMatcher`，让人知道该补什么 /
    /// 给了匹配器之后**同一个调用**就真的找得到——所以"抛"是因为缺配置，不是因为方法不工作。
    ///
    /// Three paired points: without a matcher it throws `NotSupportedException` rather than returning `null`; the message names `keyMatcher`, so the caller knows what to supply; and with a
    /// matcher the **same call** does find the row, so the throw is about missing configuration rather than a broken method.
    /// </remarks>
    [Fact]
    public async Task GetByIdAsync_ShouldThrowWithoutAKeyMatcher() {
        var blind = new InMemoryRepository<Row>();
        await blind.AddAsync(new Row(1, "a"));

        Func<Task> looking = () => blind.GetByIdAsync(1);
        await looking.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*keyMatcher*", "the message must say what is missing");

        InMemoryRepository<Row> keyed = Repository();
        var wanted = new Row(2, "b");
        await keyed.AddAsync(new Row(1, "a"));
        await keyed.AddAsync(wanted);

        Row? found = await keyed.GetByIdAsync(2);
        found.Should().BeSameAs(wanted, "with a matcher the lookup really happens");
        (await keyed.GetByIdAsync(99)).Should().BeNull("an id that is genuinely absent yields null");
    }

    /// <summary>
    /// **更新真的写进去；找不到实体就抛** / An update really writes; a missing entity throws.
    /// </summary>
    /// <remarks>
    /// 三点成对：更新一个库里的实体后，`GetAllAsync` 读到的就是新值（第一版这里是 `Task.CompletedTask`，写入被吞掉）/
    /// 更新一个库里没有的实体 → `InvalidOperationException`（而不是静默成功）/ 删除仍然按引用生效。
    ///
    /// Three paired points: after updating a stored entity, `GetAllAsync` returns the new value (the first version was `Task.CompletedTask`, swallowing the write); updating an entity that
    /// is not stored throws `InvalidOperationException` rather than silently succeeding; and deletion still works by reference.
    /// </remarks>
    [Fact]
    public async Task UpdateAsync_ShouldWriteAndShouldRejectAnUnknownEntity() {
        InMemoryRepository<Row> repository = Repository();
        var row = new Row(1, "before");
        await repository.AddAsync(row);

        // **用另一个实例覆盖**：同一实例原地改字段，"写入被吞掉"与"真的写进去"读出来一样。
        // **Overwrite with a different instance**: mutating the same object in place makes "the write was swallowed" and "the write landed" read the same.
        await repository.UpdateAsync(new Row(1, "after"));

        IReadOnlyList<Row> all = await repository.GetAllAsync();
        all.Should().ContainSingle();
        all[0].Name.Should().Be("after", "an update must actually write, not just return");
        all[0].Should().NotBeSameAs(row, "the stored instance must be replaced, not mutated in place");

        Func<Task> updating = () => repository.UpdateAsync(new Row(2, "ghost"));
        await updating.Should().ThrowAsync<InvalidOperationException>(
            "a silent no-op update is worse than a loud failure");

        await repository.DeleteAsync(row);
        (await repository.GetAllAsync()).Should().BeEmpty();
    }
}
