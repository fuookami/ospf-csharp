// 物品域 id 语义类型测试 / Tests for the item-domain id semantic types.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests;

/// <summary>
/// 物品域 id 语义类型 / Item-domain id semantic types.
/// </summary>
public sealed class IdTypesTest {
    /// <summary>工厂构造的物品 id 承载原值 / The item-id factory carries the given value.</summary>
    [Fact]
    public void ItemIdFactory_ShouldWrapValue() =>
        IdFactory.ItemIdOf("a").Value.Should().Be("a");

    /// <summary>工厂构造的箱型 id 承载原值 / The bin-type-id factory carries the given value.</summary>
    [Fact]
    public void BinTypeIdFactory_ShouldWrapValue() =>
        IdFactory.BinTypeIdOf("T1").Value.Should().Be("T1");

    /// <summary>同值的物品 id 相等 / Item ids with equal values are equal.</summary>
    [Fact]
    public void ItemId_ShouldEqualOnValue() =>
        IdFactory.ItemIdOf("a").Equals(IdFactory.ItemIdOf("a")).Should().BeTrue();

    /// <summary>不同值的物品 id 不等 / Item ids with different values are not equal.</summary>
    [Fact]
    public void ItemId_ShouldDifferOnValue() =>
        IdFactory.ItemIdOf("a").Equals(IdFactory.ItemIdOf("b")).Should().BeFalse();

    /// <summary>同值箱型 id 相等 / Bin-type ids with equal values are equal.</summary>
    [Fact]
    public void BinTypeId_ShouldEqualOnValue() =>
        IdFactory.BinTypeIdOf("T1").Equals(IdFactory.BinTypeIdOf("T1")).Should().BeTrue();

    /// <summary>
    /// **同值的物品 id 与箱型 id 不相等**：它们实现不同的接口，这正是把裸 String 换成标记接口的收益——类型上能区分开。
    /// **An item id and a bin-type id with the same value are not equal**: they implement different interfaces, which is
    /// exactly what replacing raw strings with marker interfaces buys — they are distinguishable at the type level.
    /// </summary>
    [Fact]
    public void DifferentIdKinds_ShouldNotBeInterchangeable() {
        IItemId itemId = IdFactory.ItemIdOf("same");
        IBinTypeId binId = IdFactory.BinTypeIdOf("same");

        (itemId as object).Should().NotBe(binId);
    }

    /// <summary>物品 id 按序比较 / Item ids compare ordinally.</summary>
    [Fact]
    public void ItemId_ShouldOrder() {
        var ids = new List<IItemId> { IdFactory.ItemIdOf("c"), IdFactory.ItemIdOf("a"), IdFactory.ItemIdOf("b") };
        ids.Sort();

        ids.Select(id => id.Value).Should().Equal(new[] { "a", "b", "c" });
    }

    /// <summary>箱型 id 按序比较 / Bin-type ids compare ordinally.</summary>
    [Fact]
    public void BinTypeId_ShouldOrder() {
        var ids = new List<IBinTypeId> { IdFactory.BinTypeIdOf("T2"), IdFactory.BinTypeIdOf("T1") };
        ids.Sort();

        ids.Select(id => id.Value).Should().Equal(new[] { "T1", "T2" });
    }

    /// <summary>与 null 比较时非空者更大 / A non-null id compares greater than null.</summary>
    [Fact]
    public void Id_ShouldCompareGreaterThanNull() =>
        IdFactory.ItemIdOf("a").CompareTo(null).Should().Be(1);

    /// <summary>字符串表示为裸值 / The string form is the raw value.</summary>
    [Fact]
    public void Id_ShouldRenderRawValue() =>
        IdFactory.ItemIdOf("a").ToString().Should().Be("a");

    /// <summary>
    /// **空字符串是合法 id**：工厂只拒绝 null。空值代表"未命名"，业务上可能存在，强行拒绝会把可选编号变成必填。
    /// **An empty string is a legal id**: the factory only rejects null. Empty means "unnamed", which may occur in business,
    /// and rejecting it would turn an optional code into a required one.
    /// </summary>
    [Fact]
    public void Factory_ShouldAcceptEmptyString() =>
        IdFactory.ItemIdOf(string.Empty).Value.Should().BeEmpty();

    /// <summary>null 被拒绝 / Null is rejected.</summary>
    [Fact]
    public void Factory_ShouldRejectNull() {
        Action act = () => IdFactory.ItemIdOf(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
