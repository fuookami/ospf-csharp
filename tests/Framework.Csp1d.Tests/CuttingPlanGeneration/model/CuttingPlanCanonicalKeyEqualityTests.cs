// canonical key 的相等性测试 / Equality tests for the canonical key.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.CuttingPlanGeneration.model;

/// <summary>
/// `CuttingPlanCanonicalKey` 必须按**内容**相等，不能按引用。
/// `CuttingPlanCanonicalKey` must be equal by **content**, not by reference.
/// </summary>
/// <remarks>
/// B304 在实现 `NSameGenerator` 后由测试发现：`record` 的默认相等性对 `IReadOnlyList<>` 用**引用比较**，
/// 所以两个内容完全相同的 key 被判为不等——**结构化去重完全失效，而 `DuplicateCandidates` 恒为 0 看起来正常**。
///
/// 本批（B305）为这个修复补上**直接针对 key 本身**的判据，因为 B304 只验证了 `NSame` 一条路径，
/// 而下游还有 `Csp1dColumnGeneration`（两处 `HashSet`）、`Csp1dMilp`（`DistinctByCanonicalKey`）、
/// `Csp1dProduceContext`（`Dictionary`）、`Csp1dRecovery`（去重）——**它们全都依赖这个相等性**。
///
/// B304 found this through a test after implementing `NSameGenerator`: the record's default equality compares lists by **reference**, so two keys with
/// identical content compared unequal — **structural de-duplication did nothing while `DuplicateCandidates` sat at 0 looking normal**.
///
/// This batch (B305) adds criteria **aimed at the key itself**, because B304 verified only one path (`NSame`) while downstream there are
/// `Csp1dColumnGeneration` (two `HashSet`s), `Csp1dMilp` (`DistinctByCanonicalKey`), `Csp1dProduceContext` (a `Dictionary`) and `Csp1dRecovery`
/// (de-duplication) — **all of them rely on this equality**.
/// </remarks>
public class CuttingPlanCanonicalKeyEqualityTests {
    private static CuttingPlanCanonicalKey MakeKey(
        string materialId = "M1",
        string machineId = "MC1",
        string capacity = "10",
        params (string Type, string Id, string Width, ulong Amount)[] slices) {
        var sliceKeys = new List<CuttingPlanSliceCanonicalKey>();
        foreach ((string type, string id, string width, ulong amount) in slices) {
            sliceKeys.Add(new CuttingPlanSliceCanonicalKey(type, id, width, new UInt64(amount)));
        }
        return new CuttingPlanCanonicalKey(
            MaterialId: materialId,
            MachineId: machineId,
            CapacityConsumption: capacity,
            Slices: sliceKeys,
            DemandContributions: new List<CuttingPlanDemandContributionCanonicalKey>()
        );
    }

    /// <summary>**内容相同必须相等** / Identical content must be equal.</summary>
    [Fact]
    public void IdenticalContent_ShouldBeEqual() {
        CuttingPlanCanonicalKey left = MakeKey(slices: ("Product", "A", "20", 1));
        CuttingPlanCanonicalKey right = MakeKey(slices: ("Product", "A", "20", 1));

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    /// <summary>**内容相同必须能在一个 `HashSet` 里去重** / Identical content must de-duplicate in a `HashSet`.</summary>
    /// <remarks>
    /// 判据落在**元素个数**上。`Add` 返回 `true` 在引用相等下同样成立，**只看返回值抓不到缺陷**。
    ///
    /// The criterion lands on **the element count**. `Add` returning `true` holds under reference equality too, **so checking the return value alone misses
    /// the defect**.
    /// </remarks>
    [Fact]
    public void IdenticalContent_ShouldDeDuplicateInAHashSet() {
        var set = new HashSet<CuttingPlanCanonicalKey> {
            MakeKey(slices: ("Product", "A", "20", 1)),
            MakeKey(slices: ("Product", "A", "20", 1)),
        };

        set.Should().HaveCount(1);
    }

    /// <summary>**内容相同必须能作为字典键互相查找** / Identical content must look up across instances in a dictionary.</summary>
    [Fact]
    public void IdenticalContent_ShouldLookUpInADictionary() {
        var map = new Dictionary<CuttingPlanCanonicalKey, string> {
            [MakeKey(slices: ("Product", "A", "20", 1))] = "first",
        };

        map[MakeKey(slices: ("Product", "A", "20", 1))].Should().Be("first");
    }

    /// <summary>**物料不同必须不等** / Different material ids must differ.</summary>
    [Fact]
    public void DifferentMaterial_ShouldNotBeEqual() {
        MakeKey(materialId: "M1", slices: ("Product", "A", "20", 1))
            .Should().NotBe(MakeKey(materialId: "M2", slices: ("Product", "A", "20", 1)));
    }

    /// <summary>**切片宽度不同必须不等** / Different slice widths must differ.</summary>
    [Fact]
    public void DifferentSliceWidth_ShouldNotBeEqual() {
        MakeKey(slices: ("Product", "A", "20", 1))
            .Should().NotBe(MakeKey(slices: ("Product", "A", "30", 1)));
    }

    /// <summary>**切片数量不同必须不等** / Different slice amounts must differ.</summary>
    [Fact]
    public void DifferentSliceAmount_ShouldNotBeEqual() {
        MakeKey(slices: ("Product", "A", "20", 1))
            .Should().NotBe(MakeKey(slices: ("Product", "A", "20", 2)));
    }

    /// <summary>**切片数不同必须不等** / A different number of slices must differ.</summary>
    /// <remarks>
    /// 序列比较必须**先比长度**。若只逐项比到较短的那条，`[A]` 与 `[A, B]` 会被判为相等——
    /// **那是"前缀相等"而不是"序列相等"**。
    ///
    /// The sequence comparison must **compare lengths first**. Comparing only up to the shorter one makes `[A]` equal to `[A, B]` — that is **prefix
    /// equality, not sequence equality**.
    /// </remarks>
    [Fact]
    public void DifferentSliceCount_ShouldNotBeEqual() {
        MakeKey(slices: ("Product", "A", "20", 1))
            .Should().NotBe(MakeKey(slices: [("Product", "A", "20", 1), ("Product", "B", "10", 1)]));
    }

    /// <summary>**切片顺序不同必须不等** / A different slice order must differ.</summary>
    /// <remarks>
    /// 序列是有序的。若实现改成"集合相等"，`[A, B]` 与 `[B, A]` 会被判等——**而切割方案的切片顺序是有语义的**。
    ///
    /// The sequence is ordered. If the implementation became set equality, `[A, B]` would equal `[B, A]` — **and slice order is semantically meaningful in a
    /// cutting plan**.
    /// </remarks>
    [Fact]
    public void DifferentSliceOrder_ShouldNotBeEqual() {
        MakeKey(slices: [("Product", "A", "20", 1), ("Product", "B", "10", 1)])
            .Should().NotBe(MakeKey(slices: [("Product", "B", "10", 1), ("Product", "A", "20", 1)]));
    }

    /// <summary>**设备与产能不同必须不等** / Different machine or capacity must differ.</summary>
    [Fact]
    public void DifferentMachineOrCapacity_ShouldNotBeEqual() {
        MakeKey(machineId: "MC1", slices: ("Product", "A", "20", 1))
            .Should().NotBe(MakeKey(machineId: "MC2", slices: ("Product", "A", "20", 1)));
        MakeKey(capacity: "10", slices: ("Product", "A", "20", 1))
            .Should().NotBe(MakeKey(capacity: "11", slices: ("Product", "A", "20", 1)));
    }

    /// <summary>**不同的切片必须落到不同的哈希** / Different slices must hash differently.</summary>
    /// <remarks>
    /// 只断言"内容相同 → 哈希相同"时，**忽略切片的哈希实现完全通过**——它在功能上正确，
    /// 只是把字典退化成链表。那种实现不会让任何断言失败，只会在数据量上来后变慢，**而"变慢"没有测试会说话**。
    ///
    /// 所以这里要求**不同内容落到不同桶**。这不是性能测试，是**哈希真的用了切片**的证据。
    ///
    /// Asserting only "same content → same hash" lets a hash that **ignores slices** pass completely — it is functionally correct, merely degrading
    /// dictionaries to linked lists. Nothing fails; it only gets slower as data grows, **and "slower" has no test to speak for it**.
    ///
    /// So this requires **different content to land in different buckets**. That is not a performance test but evidence that **the hash really uses the
    /// slices**.
    /// </remarks>
    [Fact]
    public void DifferentSlices_ShouldSpreadAcrossHashes() {
        var hashes = new HashSet<int>();
        for (ulong amount = 1; amount <= 32; ++amount) {
            hashes.Add(MakeKey(slices: ("Product", "A", "20", amount)).GetHashCode());
        }

        hashes.Should().HaveCountGreaterThan(24, "a hash ignoring the slices would collapse every key into one bucket");
    }

    /// <summary>**自定义字符串键仍然自相等** / The custom-string constructor still yields a self-equal key.</summary>
    [Fact]
    public void CustomStringKey_ShouldBeEqualAcrossInstances() {
        new CuttingPlanCanonicalKey("custom")
            .Should().Be(new CuttingPlanCanonicalKey("custom"));
    }
}
