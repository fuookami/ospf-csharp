#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain.Packing;

public class PackingTests {
    private static Quantity<FltX> M(double v) => new(new FltX(v), SIBaseUnits.Meter);
    private static Quantity<FltX> Kg(double v) => new(new FltX(v), SIBaseUnits.Kilogram);
    private static double D(Quantity<FltX> q) => q.Value.ToFlt64().ToDouble();

    [Fact]
    public void MaterialAttributeShouldStoreProperties() {
        var attr = new MaterialAttribute<FltX>("M001", Kg(2.5), 100);
        attr.MaterialNo.Should().Be("M001");
        D(attr.Weight).Should().BeApproximately(2.5, 1e-9);
        attr.Amount.Should().Be(100);
    }

    [Fact]
    public void MaterialPackingNumbersShouldStoreProperties() {
        var nums = new MaterialPackingNumbers<FltX>("M001", 50, 100);
        nums.MaterialNo.Should().Be("M001");
        nums.PackedAmount.Should().Be(50);
        nums.TotalAmount.Should().Be(100);
    }

    [Fact]
    public void MaterialPackingPlanGenericShouldStoreProperties() {
        MaterialPackingNumbers<FltX>[] nums = new[] { new MaterialPackingNumbers<FltX>("M001", 10, 20) };
        var plan = new MaterialPackingPlan<FltX>("BIN-001", nums);
        plan.BinTypeCode.Should().Be("BIN-001");
        plan.Numbers.Should().HaveCount(1);
    }

    [Fact]
    public void PackingContextShouldHaveDefaultEmptyProperties() {
        var ctx = new PackingContext();
        ctx.RestItems.Should().BeEmpty();
        ctx.RestMaterials.Should().BeEmpty();
        ctx.Info.Should().BeEmpty();
    }

    [Fact]
    public void PackingContextShouldAllowInitProperties() {
        var ctx = new PackingContext {
            RestItems = new Dictionary<string, ulong> { ["I1"] = 5 },
            RestMaterials = new Dictionary<string, ulong> { ["M1"] = 10 },
            Info = new Dictionary<string, string> { ["key"] = "value" }
        };
        ctx.RestItems.Should().ContainKey("I1");
        ctx.RestMaterials.Should().ContainKey("M1");
        ctx.Info.Should().ContainKey("key");
    }

    [Fact]
    public void PackingStatusShouldHaveExpectedValues() {
        PackingStatus[] values = System.Enum.GetValues<PackingStatus>();
        values.Should().Contain(PackingStatus.Optimal);
        values.Should().Contain(PackingStatus.Infeasible);
        values.Should().Contain(PackingStatus.Unknown);
    }

    [Fact]
    public void PackingSolveInfoShouldStoreStatus() {
        var info = new PackingSolveInfo(PackingStatus.Optimal, "ok");
        info.Status.Should().Be(PackingStatus.Optimal);
        info.RawStatus.Should().Be("ok");
    }

    [Fact]
    public void MaterialPackingDemandShouldHaveDefaults() {
        var demand = new MaterialPackingDemand();
        demand.Material.Should().BeNull();
        demand.Amount.Should().Be(0);
        demand.Weight.Should().BeNull();
    }

    [Fact]
    public void MaterialPackingDemandShouldStoreValues() {
        var mat = new Material<FltX>("M001", MaterialType.RawMaterial, "Steel", Kg(1));
        var demand = new MaterialPackingDemand(mat, 10, Kg(5));
        demand.Material.Should().Be(mat);
        demand.Amount.Should().Be(10);
        D(demand.Weight!).Should().BeApproximately(5.0, 1e-9);
    }

    [Fact]
    public async Task PackerShouldReturnResultWithEmptyBins() {
        var packer = new Packer();
        Result<PackingResult, ErrorCode, Error<ErrorCode>> result = await packer.InvokeAsync(Array.Empty<Bin<BinLayer, FltX>>());
        result.IsFailed.Should().BeFalse();
        // Packer always creates at least one PackingBin (even if empty items)
        result.Value.Aggregation.Bins.Should().HaveCount(1);
        result.Value.Aggregation.Bins[0].Items.Should().BeEmpty();
        result.Value.MaterialSummary.Should().BeEmpty();
    }

    [Fact]
    public async Task PackerShouldCollectItemsFromBins() {
        var item = new Fuookami.Ospf.Framework.Bpp3d.Infra.Item("I1", "Item 1");
        var layer = new BinLayer {
            Shape = new Container3Shape<FltX>(M(3), M(3), M(3)),
            Units = new[] {
                new QuantityPlacement3<Fuookami.Ospf.Framework.Bpp3d.Infra.Item, FltX>(item, new QuantityPoint3<FltX>(M(0), M(0), M(0)))
            }
        };
        var binType = new BinType<FltX>("B1", M(3), M(3), M(3), Kg(100));
        var bin = new Bin<BinLayer, FltX>(binType, new[] {
            new QuantityPlacement3<BinLayer, FltX>(layer, new QuantityPoint3<FltX>(M(0), M(0), M(0)))
        });

        var packer = new Packer();
        Result<PackingResult, ErrorCode, Error<ErrorCode>> result = await packer.InvokeAsync(new[] { bin });
        result.IsFailed.Should().BeFalse();
        result.Value.Aggregation.Bins.Should().HaveCount(1);
        result.Value.Aggregation.Bins[0].Items.Should().HaveCount(1);
    }

    [Fact]
    public void PackingRendererAdapterToSchemaShouldReturnKpi() {
        var adapter = new PackingRendererAdapter();
        var agg = new PackingAggregationNonGeneric(new[] { new PackingBin(Array.Empty<PackingItem>()) });
        var packingResult = new PackingResult(agg, new List<MaterialSummaryEntry>());
        Result<SchemaDTO, ErrorCode, Error<ErrorCode>> result = adapter.ToSchema(packingResult);
        result.IsFailed.Should().BeFalse();
        result.Value.Kpi.Should().ContainKey("bin_count");
        result.Value.Kpi.Should().ContainKey("material_count");
        result.Value.Kpi["bin_count"].Should().Be("1");
    }

    [Fact]
    public void SchemaDTOShouldStoreKpi() {
        var kpi = new Dictionary<string, string> { ["a"] = "1" };
        var dto = new SchemaDTO(kpi);
        dto.Kpi["a"].Should().Be("1");
    }

    [Fact]
    public void MaterialPackingObjectiveConfigShouldBeInstantiable() {
        var config = new MaterialPackingObjectiveConfig();
        config.Should().NotBeNull();
    }

    [Fact]
    public async Task ExhaustiveMaterialPackingSolverExecutorShouldReturnOptimal() {
        var executor = new ExhaustiveMaterialPackingSolverExecutor();
        MaterialPackingDemand[] demands = new[] { new MaterialPackingDemand() };
        MaterialPackingPlan result = await executor.ExecuteAsync(demands, new MaterialPackingObjectiveConfig());
        result.SolveInfo.Status.Should().Be(PackingStatus.Optimal);
        result.RestMaterials.Should().BeEmpty();
        result.PackagedItems.Should().BeEmpty();
    }

    [Fact]
    public async Task MaterialPackerShouldDelegateToExecutor() {
        var executor = new ExhaustiveMaterialPackingSolverExecutor();
        var packer = new MaterialPacker(executor);
        MaterialPackingDemand[] demands = new[] { new MaterialPackingDemand() };
        MaterialPackingPlan result = await packer.PlanAsync(demands, new MaterialPackingObjectiveConfig());
        result.SolveInfo.Status.Should().Be(PackingStatus.Optimal);
    }

    [Fact]
    public async Task MaterialPackingSolverExecutorGenericShouldReturnEmptyList() {
        var executor = new MaterialPackingSolverExecutor<FltX>();
        Result<IReadOnlyList<MaterialPackingPlan<FltX>>, ErrorCode, Error<ErrorCode>> result = await executor.ExecuteAsync();
        result.IsFailed.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ExhaustiveMaterialPackingSolverExecutorGenericShouldReturnEmptyList() {
        var executor = new ExhaustiveMaterialPackingSolverExecutor<FltX>();
        Result<IReadOnlyList<MaterialPackingPlan<FltX>>, ErrorCode, Error<ErrorCode>> result = await executor.ExecuteAsync();
        result.IsFailed.Should().BeFalse();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void PackingAggregationShouldBeInstantiable() {
        var agg = new PackingAggregation<FltX>();
        agg.Should().NotBeNull();
    }
}
