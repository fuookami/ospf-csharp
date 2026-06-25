#nullable enable

using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InfraItem = Fuookami.Ospf.Framework.Bpp3d.Infra.Item;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model {
    /// <summary>装箱状态 / Packing status.</summary>
    public enum PackingStatus { Optimal, Infeasible, Unknown }

    /// <summary>装箱求解信息 / Packing solve info.</summary>
    public sealed record PackingSolveInfo(PackingStatus Status, string? RawStatus = null);

    /// <summary>装箱需求 / Packing demand.</summary>
    public sealed record MaterialPackingDemand(
        Material<FltX>? Material = null,
        ulong Amount = 0,
        Quantity<FltX>? Weight = null);

    /// <summary>装箱结果 / Packing result.</summary>
    public sealed record PackingResult(
        PackingAggregationNonGeneric Aggregation,
        IReadOnlyList<MaterialSummaryEntry> MaterialSummary);

    /// <summary>装箱聚合（非泛型）/ Packing aggregation (non-generic).</summary>
    public sealed record PackingAggregationNonGeneric(IReadOnlyList<PackingBin> Bins);

    /// <summary>装箱中的箱子 / Packing bin.</summary>
    public sealed record PackingBin(IReadOnlyList<PackingItem> Items);

    /// <summary>装箱中的货物 / Packing item.</summary>
    public sealed record PackingItem(InfraItem Item, ulong Amount);

    /// <summary>材质汇总条目 / Material summary entry.</summary>
    public sealed record MaterialSummaryEntry(MaterialKey Material, ulong Amount);

    /// <summary>装箱中的已包装货物 / Packaged item.</summary>
    public sealed record PackagedItem(InfraItem Item, ulong Amount);

    /// <summary>材质装箱计划（非泛型）/ Material packing plan (non-generic).</summary>
    public sealed record MaterialPackingPlan(
        PackingSolveInfo SolveInfo,
        IReadOnlyDictionary<MaterialKey, ulong> RestMaterials,
        IReadOnlyList<PackagedItem> PackagedItems);

    /// <summary>渲染方案 DTO / Schema DTO.</summary>
    public sealed record SchemaDTO(IReadOnlyDictionary<string, string> Kpi);
}

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service {
    /// <summary>装箱器 / Packer.</summary>
    public sealed class Packer {
        public Task<Result<PackingResult, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            IReadOnlyList<Bin<BinLayer, FltX>> bins,
            PackingContext? context = null) {
            var items = new List<PackingItem>();
            foreach (Bin<BinLayer, FltX> bin in bins) {
                foreach (QuantityPlacement3<BinLayer, FltX> unit in bin.Units) {
                    foreach (QuantityPlacement3<InfraItem, FltX> u in unit.Unit.Units) {
                        items.Add(new PackingItem(u.Unit, 1));
                    }
                }
            }
            var agg = new PackingAggregationNonGeneric(new[] { new PackingBin(items) });
            var result = new PackingResult(agg, new List<MaterialSummaryEntry>());
            return Task.FromResult<Result<PackingResult, ErrorCode, Error<ErrorCode>>>(new Ok<PackingResult, ErrorCode, Error<ErrorCode>>(result));
        }
    }

    /// <summary>渲染适配器 / Renderer adapter.</summary>
    public sealed class PackingRendererAdapter {
        public Result<SchemaDTO, ErrorCode, Error<ErrorCode>> ToSchema(PackingResult result) {
            var kpi = new Dictionary<string, string> {
                ["bin_count"] = result.Aggregation.Bins.Count.ToString(),
                ["material_count"] = result.MaterialSummary.Count.ToString()
            };
            return new Ok<SchemaDTO, ErrorCode, Error<ErrorCode>>(new SchemaDTO(kpi));
        }
    }

    /// <summary>物料装箱求解器执行器基类（非泛型）/ Material packing solver executor base (non-generic).</summary>
    public abstract class MaterialPackingSolverExecutor {
        public abstract Task<MaterialPackingPlan> ExecuteAsync(
            IReadOnlyList<MaterialPackingDemand> demands,
            MaterialPackingObjectiveConfig objective);
    }

    /// <summary>穷举物料装箱求解器执行器（非泛型）/ Exhaustive material packing solver executor (non-generic).</summary>
    public sealed class ExhaustiveMaterialPackingSolverExecutor : MaterialPackingSolverExecutor {
        public override Task<MaterialPackingPlan> ExecuteAsync(
            IReadOnlyList<MaterialPackingDemand> demands,
            MaterialPackingObjectiveConfig objective) {
            return Task.FromResult(new MaterialPackingPlan(
                new PackingSolveInfo(PackingStatus.Optimal),
                new Dictionary<MaterialKey, ulong>(),
                new List<PackagedItem>()));
        }
    }

    /// <summary>物料装箱器 / Material packer.</summary>
    public sealed class MaterialPacker {
        private readonly MaterialPackingSolverExecutor _executor;
        public MaterialPacker(MaterialPackingSolverExecutor executor) { _executor = executor; }

        public async Task<MaterialPackingPlan> PlanAsync(
            IReadOnlyList<MaterialPackingDemand> demands,
            MaterialPackingObjectiveConfig objective) => await _executor.ExecuteAsync(demands, objective);
    }
}

// Stub - to be implemented
public record MaterialPackingObjectiveConfig;
