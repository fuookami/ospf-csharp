// 物料模型与物料汇总测试 / Tests for the material model and the material summary.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests;

/// <summary>
/// 物料模型与物料汇总 / Material model and material summary.
/// </summary>
public sealed class MaterialModelTest {
    /// <summary>同编号同类型同来源的物料键相等 / Keys with equal number, type and source are equal.</summary>
    [Fact]
    public void MaterialKey_ShouldEqualOnSameFields() =>
        Key("M-1").Equals(Key("M-1")).Should().BeTrue();

    /// <summary>
    /// **供应商参与物料键相等性**：同编号不同供应商成本与交期不同，只按编号合并会让采购拿到抹平来源的汇总。
    /// **The supplier participates in key equality**: the same number from different suppliers differs in cost and lead
    /// time, and merging by number alone would flatten the source out of procurement's summary.
    /// </summary>
    [Fact]
    public void MaterialKey_ShouldDistinguishSupplier() =>
        Key("M-1", supplier: "A").Equals(Key("M-1", supplier: "B")).Should().BeFalse();

    /// <summary>物料类型参与物料键相等性 / The material type participates in key equality.</summary>
    [Fact]
    public void MaterialKey_ShouldDistinguishType() =>
        Key("M-1").Equals(Key("M-1", type: MaterialType.FinishedProduct)).Should().BeFalse();

    /// <summary>
    /// **仓库不参与物料相等性**：换个仓库不是新物料，算进去会让 BOM 在库位调整后出现重复行。
    /// **The warehouse does not participate in material equality**: moving a material does not make it new, and
    /// including it would duplicate BOM rows after any location change.
    /// </summary>
    [Fact]
    public void Material_ShouldIgnoreWarehouseInEquality() =>
        MaterialOf("M-1", warehouse: "W1").Equals(MaterialOf("M-1", warehouse: "W2")).Should().BeTrue();

    /// <summary>名称参与物料相等性 / The name participates in material equality.</summary>
    [Fact]
    public void Material_ShouldDistinguishName() =>
        MaterialOf("M-1", name: "钢").Equals(MaterialOf("M-1", name: "铝")).Should().BeFalse();

    /// <summary>物料键由物料自身字段生成 / The key is derived from the material's own fields.</summary>
    [Fact]
    public void Material_ShouldExposeKey() {
        MaterialKey key = MaterialOf("M-1", supplier: "S").Key;

        key.No.Value.Should().Be("M-1");
        key.Type.Should().Be(MaterialType.RawMaterial);
        key.Supplier.Should().Be("S");
    }

    /// <summary>空集合累加得空汇总 / Accumulating nothing yields nothing.</summary>
    [Fact]
    public void Accumulate_ShouldHandleEmpty() =>
        MaterialSummaryOps.Accumulate(Array.Empty<IReadOnlyDictionary<MaterialKey, ulong>>()).Should().BeEmpty();

    /// <summary>同一物料的用量在多份映射间累加 / Amounts of one material accumulate across maps.</summary>
    [Fact]
    public void Accumulate_ShouldSumSameMaterial() {
        IReadOnlyList<MaterialSummary> summary = MaterialSummaryOps.Accumulate(new[] {
            Map((Key("M-1"), 2UL)),
            Map((Key("M-1"), 3UL)),
        });

        summary.Should().ContainSingle();
        summary[0].Amount.Should().Be(5UL);
    }

    /// <summary>不同物料各自保留 / Distinct materials are kept apart.</summary>
    [Fact]
    public void Accumulate_ShouldKeepDistinctMaterials() {
        IReadOnlyList<MaterialSummary> summary = MaterialSummaryOps.Accumulate(new[] {
            Map((Key("M-1"), 2UL), (Key("M-2"), 1UL)),
        });

        summary.Should().HaveCount(2);
        summary.Sum(entry => (long)entry.Amount).Should().Be(3L);
    }

    /// <summary>累加保持首次出现顺序，便于结果稳定可复现 / Accumulation keeps first-seen order for stable output.</summary>
    [Fact]
    public void Accumulate_ShouldKeepFirstSeenOrder() {
        IReadOnlyList<MaterialSummary> summary = MaterialSummaryOps.Accumulate(new[] {
            Map((Key("M-2"), 1UL), (Key("M-1"), 1UL)),
        });

        summary.Select(entry => entry.Material.No.Value).Should().Equal("M-2", "M-1");
    }

    /// <summary>
    /// **物料汇总量真来自物品目录**：这是先前刻意留空的地方，现在必须给出真实数字而不是空列表。
    /// **The material summary really computes from the catalogue**: this was deliberately empty before and must now
    /// produce real amounts rather than an empty list.
    /// </summary>
    [Fact]
    public void Packer_ShouldSummarizeMaterials() {
        var catalog = new Dictionary<string, IAbstractItem<FltX>> {
            ["a"] = Item("a", ("M-1", 2UL), ("M-2", 1UL)),
        };

        PackingResult result = new Packer(catalog).Invoke(new[] {
            new PackedSheetInput(Sheet(10, 10), new[] { Placed(0, 0, 2, 2, "a"), Placed(3, 0, 2, 2, "a") }),
        }).Value;

        result.MaterialSummaries.Should().HaveCount(2);
        result.MaterialSummaries.Single(entry => entry.Material.No.Value == "M-1").Amount.Should().Be(4UL);
        result.MaterialSummaries.Single(entry => entry.Material.No.Value == "M-2").Amount.Should().Be(2UL);
    }

    /// <summary>
    /// **目录中缺失的物品不计入物料汇总**：宁可少报，也不凭放置上的编号猜一个用量。
    /// **Items missing from the catalogue are not counted**: under-reporting beats guessing an amount from the id.
    /// </summary>
    [Fact]
    public void Packer_ShouldSkipUnknownItems() {
        PackingResult result = new Packer().Invoke(new[] {
            new PackedSheetInput(Sheet(10, 10), new[] { Placed(0, 0, 2, 2, "unknown") }),
        }).Value;

        result.MaterialSummaries.Should().BeEmpty();
    }

    /// <summary>多块板材的物料用量合并 / Material usage merges across sheets.</summary>
    [Fact]
    public void Packer_ShouldMergeAcrossSheets() {
        var catalog = new Dictionary<string, IAbstractItem<FltX>> {
            ["a"] = Item("a", ("M-1", 1UL)),
        };

        PackingResult result = new Packer(catalog).Invoke(new[] {
            new PackedSheetInput(Sheet(10, 10), new[] { Placed(0, 0, 2, 2, "a") }),
            new PackedSheetInput(Sheet(10, 10), new[] { Placed(0, 0, 2, 2, "a") }),
        }).Value;

        result.MaterialSummaries.Should().ContainSingle();
        result.MaterialSummaries[0].Amount.Should().Be(2UL);
    }

    /// <summary>构造物料键 / Build a material key.</summary>
    private static MaterialKey Key(
        string no,
        MaterialType type = MaterialType.RawMaterial,
        string? supplier = null) =>
        new(new MaterialNo(no), type, null, supplier);

    /// <summary>构造物料 / Build a material.</summary>
    private static Material<FltX> MaterialOf(
        string no, string name = "material", string? supplier = null, string? warehouse = null) =>
        new(new MaterialNo(no), MaterialType.RawMaterial, name, Meters(1), null, supplier, warehouse);

    /// <summary>构造一份物料用量映射 / Build a material-amount map.</summary>
    private static IReadOnlyDictionary<MaterialKey, ulong> Map(
        params (MaterialKey Key, ulong Amount)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Amount);

    /// <summary>构造带物料用量的物品 / Build an item carrying material amounts.</summary>
    private static IAbstractItem<FltX> Item(
        string id, params (string No, ulong Amount)[] materials) =>
        new ActualItem<FltX>(
            new ItemId(id), id,
            new RectanglePackingShape2<FltX>(Meters(2), Meters(2)),
            true, Meters(0),
            materials.ToDictionary(
                entry => Key(entry.No),
                entry => entry.Amount));

    /// <summary>构造板材 / Build a sheet.</summary>
    private static Sheet2Shape<FltX> Sheet(double w, double h) => new(Meters(w), Meters(h));

    /// <summary>构造放置 / Build a placement.</summary>
    private static Placement2<FltX> Placed(
        double x, double y, double w, double h, string? itemId) =>
        new(
            new RectanglePackingShape2<FltX>(Meters(w), Meters(h)),
            new QuantityPoint2<FltX>(Meters(x), Meters(y)),
            false,
            itemId);

    /// <summary>构造以米为单位的量 / Build a quantity in metres.</summary>
    private static Quantity<FltX> Meters(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
