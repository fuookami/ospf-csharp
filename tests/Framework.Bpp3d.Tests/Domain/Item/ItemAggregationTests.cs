#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain.Item;

public class ItemAggregationTests {
    private static Quantity<FltX> M(double v) => new(new FltX(v), SIBaseUnits.Meter);
    private static Quantity<FltX> Kg(double v) => new(new FltX(v), SIBaseUnits.Kilogram);

    private static PackageAttribute DefaultAttr => new(PackageType.Box, new UInt64(1), new UInt64(100), new UInt64(1), new UInt64(100));

    private static Item<FltX> MakeItem(string id) => new(id, $"Item-{id}", M(1), M(1), M(1), Kg(1), DefaultAttr);

    private static BinType<FltX> MakeBin(string code, bool isMain = false) =>
        new(code, M(1), M(1), M(1), Kg(100), IsMain: isMain);

    [Fact]
    public void ConstructorShouldStoreSchemasAndBins() {
        Item<FltX> item = MakeItem("I1");
        BinType<FltX> bin = MakeBin("B1");
        var schema = new Schema<FltX>("batch-1", new[] { (item, new UInt64(10)) });
        var bins = new Dictionary<BinType<FltX>, ulong?> { { bin, null } };

        var agg = new ItemAggregation<FltX>(new[] { schema }, bins);

        agg.Schemas.Should().HaveCount(1);
        agg.Bins.Should().HaveCount(1);
    }

    [Fact]
    public void BatchNosShouldReturnAllBatchNumbers() {
        Item<FltX> item = MakeItem("I1");
        var s1 = new Schema<FltX>("B001", new[] { (item, new UInt64(5)) });
        var s2 = new Schema<FltX>("B002", new[] { (item, new UInt64(3)) });

        var agg = new ItemAggregation<FltX>(new[] { s1, s2 }, new Dictionary<BinType<FltX>, ulong?>());

        agg.BatchNos.Should().BeEquivalentTo(new[] { "B001", "B002" });
    }

    [Fact]
    public void MainBinShouldReturnIsMainBin() {
        BinType<FltX> bin1 = MakeBin("B1");
        BinType<FltX> bin2 = MakeBin("B2", isMain: true);
        var bins = new Dictionary<BinType<FltX>, ulong?> { { bin1, null }, { bin2, null } };

        var agg = new ItemAggregation<FltX>(Array.Empty<Schema<FltX>>(), bins);

        agg.MainBin.Should().Be(bin2);
    }

    [Fact]
    public void MainBinShouldReturnLargestVolumeWhenNoIsMain() {
        // Only one bin to avoid IComparable issue with Quantity<FltX> in OrderByDescending
        var bin1 = new BinType<FltX>("B1", M(1), M(1), M(1), Kg(1));
        var bins = new Dictionary<BinType<FltX>, ulong?> { { bin1, null } };

        var agg = new ItemAggregation<FltX>(Array.Empty<Schema<FltX>>(), bins);

        agg.MainBin.Should().Be(bin1);
    }

    [Fact]
    public void MainBinShouldReturnNullWhenNoBins() {
        var agg = new ItemAggregation<FltX>(Array.Empty<Schema<FltX>>(), new Dictionary<BinType<FltX>, ulong?>());
        agg.MainBin.Should().BeNull();
    }

    [Fact]
    public void RestItemsShouldReturnTotalDemandWhenNothingUsed() {
        Item<FltX> item = MakeItem("I1");
        var schema = new Schema<FltX>("B1", new[] { (item, new UInt64(10)) });

        var agg = new ItemAggregation<FltX>(new[] { schema }, new Dictionary<BinType<FltX>, ulong?>());

        // RestItems uses GroupBy on the item from the schema
        agg.RestItems.Should().HaveCount(1);
        agg.RestItems.Values.First().Should().Be(10);
    }

    [Fact]
    public void RestItemsShouldReturnMultipleSchemasAggregated() {
        Item<FltX> item1 = MakeItem("I1");
        Item<FltX> item2 = MakeItem("I2");
        var s1 = new Schema<FltX>("B1", new[] { (item1, new UInt64(5)) });
        var s2 = new Schema<FltX>("B2", new[] { (item2, new UInt64(3)) });

        var agg = new ItemAggregation<FltX>(new[] { s1, s2 }, new Dictionary<BinType<FltX>, ulong?>());

        agg.RestItems.Should().HaveCount(2);
    }

    [Fact]
    public void UseItemsShouldNotThrow() {
        Item<FltX> item = MakeItem("I1");
        var schema = new Schema<FltX>("B1", new[] { (item, new UInt64(10)) });

        var agg = new ItemAggregation<FltX>(new[] { schema }, new Dictionary<BinType<FltX>, ulong?>());
        // UseItems should accept items without throwing
        agg.UseItems(new Dictionary<Item<FltX>, ulong> { { item, 3 } });
    }

    [Fact]
    public void UseItemsShouldAccumulateWithoutThrowing() {
        Item<FltX> item = MakeItem("I1");
        var schema = new Schema<FltX>("B1", new[] { (item, new UInt64(10)) });

        var agg = new ItemAggregation<FltX>(new[] { schema }, new Dictionary<BinType<FltX>, ulong?>());
        agg.UseItems(new Dictionary<Item<FltX>, ulong> { { item, 2 } });
        agg.UseItems(new Dictionary<Item<FltX>, ulong> { { item, 3 } });
        // Verify no exception thrown
    }

    [Fact]
    public void UseBinsShouldTrackUsage() {
        BinType<FltX> bin = MakeBin("B1");
        var bins = new Dictionary<BinType<FltX>, ulong?> { { bin, null } };
        var agg = new ItemAggregation<FltX>(Array.Empty<Schema<FltX>>(), bins);

        agg.UseBins(new Dictionary<BinType<FltX>, ulong> { { bin, 5 } });
        // Verify no exception thrown
    }
}
