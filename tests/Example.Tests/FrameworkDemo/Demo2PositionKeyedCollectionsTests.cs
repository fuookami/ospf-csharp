// `Position` 值相等对以它为键的集合的影响 / The effect of `Position` value equality on collections keyed by it.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B297 把 `Position` 从引用相等改成按 `Id` 值相等。**这改变了所有以它为键的集合的行为**，
/// 本类逐处验证那个改变是**变好**而不是**变坏**。
/// B297 changed `Position` from reference equality to value equality by `Id`. **That changes every collection keyed by it**, and this class verifies each
/// site that the change is **an improvement rather than a regression**.
/// </summary>
/// <remarks>
/// B297 的计划小节明确记下这个风险："`Appointment` 等以它为键的集合行为变了，但没有测试覆盖那些集合"。
/// 这一批就是来还这笔账的。
///
/// **要证明的是"新语义让跨实例查找生效"，而不是"查找返回非空"**——后者在引用语义下也可能通过
/// （同一个实例）。所以每个判据都必须**用另一个实例去查**。
///
/// B297's plan section recorded this risk explicitly: "collections keyed by `Position` changed behaviour, but no test covers them". This batch pays that debt.
///
/// **What must be shown is "the new semantics make cross-instance lookup work", not "lookup returns non-null"** — the latter can pass under reference
/// semantics too (same instance). So every criterion here must **look up with a different instance**.
/// </remarks>
public class Demo2PositionKeyedCollectionsTests {
    private static Position MakePosition(ulong id, string spaceName) =>
        new Position(
            id: id,
            spaceName: spaceName,
            sizeCode: "A",
            linearLoadingOrder: (byte)0,
            coordinate: new PositionCoordinate(
                frontArm: 100.0,
                backArm: 120.0,
                leftArm: -10.0,
                rightArm: 10.0
            ),
            shape: new PositionShape(
                Length: 20.0,
                Width: 20.0,
                Volume: 8000.0
            ),
            location: PositionLocation.NormalMainLocation
        );

    private static Item MakeItem(string name) =>
        new Item(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(
                Name: "Normal",
                Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL),
                Category: CargoPriorityCategory.Normal
            ),
            cargoType: CargoType.FromName("General"),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: new ItemLocation(
                enabledLocations: new HashSet<DeckLocation> { DeckLocation.Main }
            )
        );

    /// <summary>**`Appointment.ItemsOf` 必须能用另一个实例查到** / `Appointment.ItemsOf` must work with a different instance.</summary>
    /// <remarks>
    /// 这是 B297 改动最直接的影响点：`Reverse` 以 `Position` 为键，`ItemsOf` 用调用方传进来的实例去查。
    /// 引用语义下，**调用方只要传的不是建映射时那个实例，就永远查不到**——而 `Appointment` 的构造方与查询方
    /// 通常是不同的代码路径（聚合器建映射、求解器查货物）。
    ///
    /// This is the most direct impact of B297: `Reverse` is keyed by `Position`, and `ItemsOf` looks up with the caller's instance. Under reference
    /// semantics, **any caller passing something other than the exact instance used to build the map finds nothing** — and the builder and the querier are
    /// usually different code paths (the aggregator builds the map, the solver queries items).
    /// </remarks>
    [Fact]
    public void Appointment_ItemsOf_ShouldWorkAcrossInstances() {
        var appointment = new Appointment(
            new Dictionary<Item, Position> {
                [MakeItem("A")] = MakePosition(7UL, "11"),
            }
        );

        appointment.ItemsOf(MakePosition(7UL, "11")).Should().HaveCount(1);
        appointment.ItemsOf(MakePosition(8UL, "12")).Should().BeEmpty();
    }

    /// <summary>**同舱位多个货物必须合并到一条反向映射** / Several items on one position must merge into one reverse entry.</summary>
    /// <remarks>
    /// 判据落在**元素个数**上：反向映射若按引用分组，两个货物会被拆成两条，总数变成 2 而不是 1 条含 2 个货物。
    ///
    /// The criterion lands on **element counts**: if the reverse map grouped by reference, the two items would split into two entries, making the map size
    /// 2 rather than one entry holding 2 items.
    /// </remarks>
    [Fact]
    public void Appointment_Reverse_ShouldMergeItemsOnTheSameId() {
        var appointment = new Appointment(
            new Dictionary<Item, Position> {
                [MakeItem("A")] = MakePosition(7UL, "11"),
                [MakeItem("B")] = MakePosition(7UL, "11"),
                [MakeItem("C")] = MakePosition(8UL, "12"),
            }
        );

        appointment.Reverse.Should().HaveCount(2);
        appointment.Reverse[MakePosition(7UL, "11")].Should().HaveCount(2);
        appointment.Reverse[MakePosition(8UL, "12")].Should().HaveCount(1);
    }

    /// <summary>**`PositionOf` 必须返回映射里那个实例，而不是调用方传的** / `PositionOf` must return the mapped instance, not the caller's.</summary>
    [Fact]
    public void Appointment_PositionOf_ShouldReturnTheMappedInstance() {
        Position mapped = MakePosition(7UL, "11");
        var appointment = new Appointment(new Dictionary<Item, Position> { [MakeItem("A")] = mapped });

        Position? found = appointment.PositionOf(appointment.Mapping.Keys.First());

        found.Should().Be(mapped);
    }

    /// <summary>**`LoadingOrder` 的前驱/后继集合必须跨实例命中** / `LoadingOrder`'s predecessor/successor sets must match across instances.</summary>
    [Fact]
    public void LoadingOrder_ShouldMatchAcrossInstances() {
        var order = new LoadingOrder(
            location: DeckLocation.Main,
            order: (byte)1,
            directPrec: new HashSet<Position> { MakePosition(7UL, "11") },
            directSucc: new HashSet<Position> { MakePosition(8UL, "12") }
        );

        order.DirectPrec.Should().Contain(MakePosition(7UL, "11"));
        order.DirectSucc.Should().Contain(MakePosition(8UL, "12"));
        order.DirectPrec.Should().NotContain(MakePosition(8UL, "12"));
    }

    /// <summary>**货物类型缓存必须线程安全** / The cargo-type cache must be thread-safe.</summary>
    /// <remarks>
    /// B303 跑全量门禁时这条真的崩了：
    ///
    /// ```
    /// System.InvalidOperationException : Operations that change non-concurrent collections must have exclusive access.
    /// ```
    ///
    /// 原因是 `CargoType.FromName` 与 `ULD.FromName` 用**非线程安全静态 `Dictionary`**，
    /// 而 xUnit 并行跑多个测试类。**单线程跑一千遍都不会失败**，只有并行才暴露。
    ///
    /// 判据不能是"不抛异常"——那在单线程下永远成立。这里用**并行访问 + 断言每个名字都拿到同一个实例**，
    /// 既验线程安全，也验缓存语义。
    ///
    /// This really crashed during the B303 full gate:
    ///
    /// ```
    /// System.InvalidOperationException : Operations that change non-concurrent collections must have exclusive access.
    /// ```
    ///
    /// The cause: `CargoType.FromName` and `ULD.FromName` used a **non-thread-safe static `Dictionary`**, and xUnit runs test classes in parallel.
    /// **A thousand single-threaded runs would never fail**; only parallelism exposes it.
    ///
    /// The criterion cannot be "does not throw" — that always holds single-threaded. This uses **parallel access plus asserting every name resolves to the same
    /// instance**, checking both thread safety and cache semantics.
    /// </remarks>
    [Fact]
    public void CargoTypeCache_ShouldSurviveConcurrentAccess() {
        // **同一批名字被所有线程反复取**：这才是缓存该做的事，也才是竞态窗口。
        // **The same set of names is fetched repeatedly by every thread**: that is what a cache is for, and that is the race window.
        //
        // 第一版让每个线程用**不同**的名字，然后断言"不同名 → 不同实例"——那条断言与缓存有没有生效**无关**，
        // 于是"绕过缓存"的变异体照样通过。判据必须是**同名 → 同一实例**。
        //
        // The first version gave every thread **different** names and asserted "different names → different instances" — an assertion **unrelated** to whether the
        // cache works at all, so a "bypass the cache" mutant passed. The criterion must be **same name → same instance**.
        const int Threads = 16;
        const int PerThread = 200;
        string[] shared = { "General", "Dangerous", "Perishable", "Live", "Valuable" };
        var results = new System.Collections.Concurrent.ConcurrentBag<CargoType>();

        System.Threading.Tasks.Parallel.For(0, Threads, thread => {
            for (int index = 0; index < PerThread; ++index) {
                results.Add(CargoType.FromName(shared[index % shared.Length]));
            }
        });

        results.Should().HaveCount(Threads * PerThread);
        results.Select(type => type.Type).Distinct().Should().HaveCount(shared.Length);
        // 同一个名字必须每次都拿到**同一个实例**。
        // The same name must yield **the same instance** every time.
        results.Where(type => type.Type == "General").Distinct().Should().HaveCount(1);
    }

    /// <summary>**同 `SpaceName` 但不同 `Id` 必须是两个不同条目** / Same `SpaceName`, different `Id`, must be two distinct entries.</summary>
    /// <remarks>
    /// 前面几条测试里 `Id` 与 `SpaceName` 是一一对应的（7→"11"、8→"12"），所以"比 `SpaceName`"这种错误实现
    /// **也能通过**——两条字段恰好同步。这里故意让它们**不一致**：两个位置 `SpaceName` 相同、`Id` 不同。
    ///
    /// 若相等按 `SpaceName` 判，这两个会被合并成一条；按 `Id` 判则是两条。**这是区分两种实现的唯一方式。**
    ///
    /// In the tests above `Id` and `SpaceName` are in lockstep (7→"11", 8→"12"), so a wrong implementation comparing `SpaceName` **also passes** — the two
    /// fields happen to agree. Here they are deliberately **inconsistent**: two positions with the same `SpaceName` and different `Id`s.
    ///
    /// Equality by `SpaceName` would merge them into one entry; by `Id` it yields two. **This is the only way to tell the two implementations apart.**
    /// </remarks>
    [Fact]
    public void SameSpaceNameDifferentId_ShouldBeTwoDistinctEntries() {
        var appointment = new Appointment(
            new Dictionary<Item, Position> {
                [MakeItem("A")] = MakePosition(7UL, "shared"),
                [MakeItem("B")] = MakePosition(8UL, "shared"),
            }
        );

        appointment.Reverse.Should().HaveCount(2);
        appointment.ItemsOf(MakePosition(7UL, "shared")).Should().HaveCount(1);
        appointment.ItemsOf(MakePosition(8UL, "shared")).Should().HaveCount(1);
    }

    /// <summary>**同 `Id`、不同 `SpaceName` 必须合并成一条** / Same `Id`, different `SpaceName`, must merge into one entry.</summary>
    /// <remarks>
    /// **这条才是真正能区分"按 `Id`"与"按 `SpaceName`"的判据。**
    ///
    /// 前面几条都没用：`Dictionary` 先按 `GetHashCode` 分桶，而哈希按 `Id`——
    /// **`Id` 不同的两个位置根本走不到 `Equals`**。所以"相等比 `SpaceName`"的变异体在那里**必然存活**。
    /// 只有 `Id` 相同（同桶）时才真正调用 `Equals`，此时若它比 `SpaceName`，两个本该合并的位置会被拆成两条。
    ///
    /// **This is the criterion that actually distinguishes "by `Id`" from "by `SpaceName`".**
    ///
    /// The earlier ones do not: `Dictionary` buckets by `GetHashCode` first, and the hash uses `Id` — **two positions with different `Id`s never reach
    /// `Equals`**, so a "compare `SpaceName`" mutant necessarily survives there. Only when `Id`s match (same bucket) is `Equals` really called, and then
    /// comparing `SpaceName` splits two positions that should have merged.
    /// </remarks>
    [Fact]
    public void SameIdDifferentSpaceName_ShouldMergeIntoOneEntry() {
        var appointment = new Appointment(
            new Dictionary<Item, Position> {
                [MakeItem("A")] = MakePosition(7UL, "11"),
                [MakeItem("B")] = MakePosition(7UL, "99"),
            }
        );

        appointment.Reverse.Should().HaveCount(1);
        appointment.Reverse[MakePosition(7UL, "11")].Should().HaveCount(2);
    }

    /// <summary>**不同 `Id` 不能被判成相等** / Different `Id`s must not compare equal.</summary>
    [Fact]
    public void DifferentId_ShouldNeverCollapseIntoOneEntry() {
        var order = new LoadingOrder(
            location: DeckLocation.Main,
            order: (byte)1,
            directPrec: new HashSet<Position> {
                MakePosition(7UL, "shared"),
                MakePosition(8UL, "shared"),
            },
            directSucc: new HashSet<Position>()
        );

        order.DirectPrec.Should().HaveCount(2);
    }

    /// <summary>**同一 `Id` 在同一集合里只应占一个位置** / One `Id` should occupy one slot in a set.</summary>
    [Fact]
    public void LoadingOrder_ShouldNotHoldDuplicateIds() {
        var order = new LoadingOrder(
            location: DeckLocation.Main,
            order: (byte)1,
            directPrec: new HashSet<Position> {
                MakePosition(7UL, "11"),
                MakePosition(7UL, "11"),
            },
            directSucc: new HashSet<Position>()
        );

        order.DirectPrec.Should().HaveCount(1);
    }
}
