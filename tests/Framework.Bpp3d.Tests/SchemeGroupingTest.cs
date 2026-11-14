// 方案分组测试 / Tests for scheme grouping.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests;

/// <summary>
/// 方案分组 / Scheme grouping.
/// </summary>
public sealed class SchemeGroupingTest {
    /// <summary>同图样物品归入同一组 / Items sharing a pattern join one group.</summary>
    [Fact]
    public void GroupByPattern_ShouldMergeSamePattern() {
        IReadOnlyList<IReadOnlyList<(Item<FltX> Item, UInt64 Amount)>> groups =
            SchemeGrouping.GroupByPattern(new[] { (ItemOf("a", 1.0), UInt64(1)), (ItemOf("b", 2.0), UInt64(1)) });

        groups.Should().ContainSingle();
        groups[0].Should().HaveCount(2);
    }

    /// <summary>批次不同即不同图样 / A different batch number means a different pattern.</summary>
    [Fact]
    public void GroupByPattern_ShouldSplitOnBatchNo() {
        IReadOnlyList<IReadOnlyList<(Item<FltX> Item, UInt64 Amount)>> groups = SchemeGrouping.GroupByPattern(new[] {
            (ItemOf("a", 1.0, batchNo: "B1"), UInt64(1)),
            (ItemOf("b", 2.0, batchNo: "B2"), UInt64(1)),
        });

        groups.Should().HaveCount(2);
    }

    /// <summary>仓库不同即不同图样 / A different warehouse means a different pattern.</summary>
    [Fact]
    public void GroupByPattern_ShouldSplitOnWarehouse() {
        IReadOnlyList<IReadOnlyList<(Item<FltX> Item, UInt64 Amount)>> groups = SchemeGrouping.GroupByPattern(new[] {
            (ItemOf("a", 1.0, warehouse: "W1"), UInt64(1)),
            (ItemOf("b", 2.0, warehouse: "W2"), UInt64(1)),
        });

        groups.Should().HaveCount(2);
    }

    /// <summary>包装类型不同即不同图样 / A different package type means a different pattern.</summary>
    [Fact]
    public void GroupByPattern_ShouldSplitOnPackageType() {
        IReadOnlyList<IReadOnlyList<(Item<FltX> Item, UInt64 Amount)>> groups = SchemeGrouping.GroupByPattern(new[] {
            (ItemOf("a", 1.0, packageType: PackageType.Box), UInt64(1)),
            (ItemOf("b", 2.0, packageType: PackageType.Bag), UInt64(1)),
        });

        groups.Should().HaveCount(2);
    }

    /// <summary>
    /// **组内按重量升序**：轻件在前、重件在后。层生成按顺序取件时轻件先上、重件压底。
    /// **Groups are ordered by ascending weight**: light first, heavy last. Layer generation taking items in order puts
    /// light items on top and heavy ones at the bottom.
    /// </summary>
    [Fact]
    public void GroupByPattern_ShouldOrderByAscendingWeight() {
        IReadOnlyList<IReadOnlyList<(Item<FltX> Item, UInt64 Amount)>> groups = SchemeGrouping.GroupByPattern(new[] {
            (ItemOf("heavy", 5.0), UInt64(1)),
            (ItemOf("light", 1.0), UInt64(1)),
            (ItemOf("mid", 3.0), UInt64(1)),
        });

        groups[0].Select(entry => entry.Item.Id).Should().Equal(new[] { "light", "mid", "heavy" });
    }

    /// <summary>
    /// **组按首次出现顺序排列**：分组本身不做二次排序，因此"哪个图样先出现"由重量排序后的第一个成员决定。
    /// **Groups follow first-appearance order**: grouping does not re-sort groups, so which pattern comes first is decided by
    /// the first member after weight sorting.
    /// </summary>
    [Fact]
    public void GroupByPattern_ShouldOrderGroupsByFirstAppearance() {
        IReadOnlyList<IReadOnlyList<(Item<FltX> Item, UInt64 Amount)>> groups = SchemeGrouping.GroupByPattern(new[] {
            (ItemOf("b-heavy", 4.0, batchNo: "B2"), UInt64(1)),
            (ItemOf("a-light", 1.0, batchNo: "B1"), UInt64(1)),
            (ItemOf("b-light", 2.0, batchNo: "B2"), UInt64(1)),
        });

        // 重量升序后为 a-light(1)、b-light(2)、b-heavy(4)，因此 B1 组先出现。
        // After ascending weight the order is a-light(1), b-light(2), b-heavy(4), so group B1 appears first.
        groups.Should().HaveCount(2);
        groups[0].Should().ContainSingle().Which.Item.Id.Should().Be("a-light");
        groups[1].Select(entry => entry.Item.Id).Should().Equal(new[] { "b-light", "b-heavy" });
    }

    /// <summary>空输入得到空分组 / An empty input yields no group.</summary>
    [Fact]
    public void GroupByPattern_ShouldHandleEmpty() =>
        SchemeGrouping.GroupByPattern(Array.Empty<(Item<FltX>, UInt64)>()).Should().BeEmpty();

    /// <summary>单件输入得到单组 / A single item yields one group.</summary>
    [Fact]
    public void GroupByPattern_ShouldHandleSingle() =>
        SchemeGrouping.GroupByPattern(new[] { (ItemOf("a", 1.0), UInt64(1)) }).Should().ContainSingle();

    /// <summary>件数原样保留 / Amounts are carried through unchanged.</summary>
    [Fact]
    public void GroupByPattern_ShouldPreserveAmounts() {
        IReadOnlyList<IReadOnlyList<(Item<FltX> Item, UInt64 Amount)>> groups =
            SchemeGrouping.GroupByPattern(new[] { (ItemOf("a", 1.0), UInt64(7)) });

        groups[0][0].Amount.Should().Be(UInt64(7));
    }

    /// <summary>构造物品 / Build an item.</summary>
    private static Item<FltX> ItemOf(
        string id,
        double weight,
        string? batchNo = null,
        string? warehouse = null,
        PackageType packageType = PackageType.Box) =>
        new(
            id, id,
            Meters(1), Meters(1), Meters(1),
            Meters(weight),
            new PackageAttribute(packageType, UInt64(1), UInt64(1), UInt64(1), UInt64(1)),
            batchNo,
            warehouse);

    /// <summary>构造无符号整数 / Build an unsigned integer.</summary>
    private static UInt64 UInt64(ulong value) => new(value);

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
