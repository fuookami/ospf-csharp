// `Position` 的相等语义 / The equality semantics of `Position`.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// `Position` 必须按 <see cref="Position.Id"/> **值相等**，与基线 `data class` 的可观察行为一致。
/// `Position` must be **value-equal by <see cref="Position.Id"/>**, matching the baseline `data class`'s observable behaviour.
/// </summary>
/// <remarks>
/// B297 之前 `Position` 是 `sealed class` 且**没有 `Equals`/`GetHashCode`**，所以
/// **所有以 `Position` 为键的集合都按引用比较**：两个 `Id` 相同的位置对象会被当成不同的键。
///
/// 这个缺陷最难发现的形态是：**`HashSet` 里"加进去又找不到"**。
/// `Add` 返回 `true`（因为引用不同），随后 `Contains` 也返回 `true`——**看起来全对**，
/// 只是集合里悄悄多了一份。所以这里的判据是**集合的元素个数**，不是 `Contains` 的返回值。
///
/// Before B297 `Position` was a `sealed class` **without `Equals`/`GetHashCode`**, so **every collection keyed by `Position` compared by reference**: two
/// position objects with the same `Id` became distinct keys.
///
/// The hardest form of this defect to notice is **"added to a `HashSet` and then not found"**. `Add` returns `true` (different reference), then `Contains`
/// also returns `true` — **everything looks right**, except the set quietly holds an extra copy. So the criterion here is **the element count**, not
/// `Contains`'s return value.
/// </remarks>
public class Demo2PositionEqualityTests {
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

    /// <summary>**同 `Id` 的两个对象必须相等** / Two objects with the same `Id` must be equal.</summary>
    [Fact]
    public void SameId_ShouldBeEqual() {
        Position left = MakePosition(7UL, "11");
        Position right = MakePosition(7UL, "11");

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    /// <summary>**不同 `Id` 的哈希必须不同** / Different `Id`s must hash differently.</summary>
    /// <remarks>
    /// 只断言"同 `Id` 同哈希"时，`GetHashCode() => 0` **完全通过**——它在功能上正确，只是把字典退化成链表。
    /// 那种实现不会让任何断言失败，只会在数据量上来后变慢，**而"变慢"没有测试会说话**。
    ///
    /// 所以这里要求**不同 `Id` 落到不同的桶**。这条判据不是"性能测试"，是**哈希函数真的用了 `Id`** 的证据。
    ///
    /// Asserting only "same `Id`, same hash" lets `GetHashCode() => 0` **pass completely** — it is functionally correct, merely degrading dictionaries to
    /// linked lists. Nothing fails; it only gets slower as data grows, **and "slower" has no test to speak for it**.
    ///
    /// So this requires **different `Id`s to land in different buckets**. That is not a "performance test" but evidence that **the hash really uses `Id`**.
    /// </remarks>
    [Fact]
    public void DifferentIds_ShouldSpreadAcrossHashes() {
        var hashes = new HashSet<int>();
        for (ulong id = 1UL; id <= 32UL; ++id) {
            hashes.Add(MakePosition(id, "x").GetHashCode());
        }

        hashes.Should().HaveCountGreaterThan(24, "a constant hash would collapse every id into one bucket");
    }

    /// <summary>**不同 `Id` 必须不相等** / Different `Id`s must not be equal.</summary>
    [Fact]
    public void DifferentId_ShouldNotBeEqual() {
        Position left = MakePosition(7UL, "11");
        Position right = MakePosition(8UL, "12");

        left.Should().NotBe(right);
    }

    /// <summary>**`HashSet` 必须去重** / A `HashSet` must de-duplicate.</summary>
    /// <remarks>
    /// 判据落在**元素个数**上。若只看 `Contains`，引用相等的实现同样返回 `true`，缺陷完全隐形。
    ///
    /// The criterion lands on **the element count**. Checking only `Contains` would pass with the reference-equality implementation too, leaving the defect
    /// completely invisible.
    /// </remarks>
    [Fact]
    public void HashSet_ShouldDeDuplicateByValue() {
        var set = new HashSet<Position> {
            MakePosition(7UL, "11"),
            MakePosition(7UL, "11"),
            MakePosition(8UL, "12"),
        };

        set.Should().HaveCount(2);
    }

    /// <summary>**以 `Position` 为键的字典必须能查到** / A dictionary keyed by `Position` must be able to look up.</summary>
    [Fact]
    public void DictionaryKeyedByPosition_ShouldLookUpAcrossInstances() {
        var appointment = new Dictionary<Position, string> {
            [MakePosition(7UL, "11")] = "first",
            [MakePosition(8UL, "12")] = "second",
        };

        appointment.Should().HaveCount(2);
        appointment[MakePosition(7UL, "11")].Should().Be("first");
        appointment[MakePosition(8UL, "12")].Should().Be("second");
    }

    /// <summary>**`Id` 是唯一键：同 `Id` 但其它字段不同仍视为同一位置** / `Id` is the key: same `Id` with other fields differing is still the same position.</summary>
    /// <remarks>
    /// 这是**有意选择**的简化，不是疏漏：基线 `data class` 比较全部字段，但 `Id` 在基线里标注为"唯一标识"，
    /// 同 `Id` 必然同其余字段。若将来允许"同 `Id` 不同字段"的构造，这条断言必须改。
    ///
    /// This is a **deliberate simplification**, not an oversight: the baseline `data class` compares all fields, but `Id` is documented there as the unique
    /// identifier, so the same `Id` implies the same everything else. If constructing "same `Id`, different fields" ever becomes legal, this assertion must
    /// change.
    /// </remarks>
    [Fact]
    public void SameIdWithDifferentSpaceName_ShouldStillBeEqual() {
        Position left = MakePosition(7UL, "11");
        Position right = MakePosition(7UL, "99");

        left.Should().Be(right);
    }
}
