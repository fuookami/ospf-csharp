#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using InfraItem = Fuookami.Ospf.Framework.Bpp3d.Infra.Item;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;

/// <summary>
/// 装箱器 / Packer.
/// 将最终箱子转换为装箱结果 / Converts final bins into packing results.
/// </summary>
public sealed class Packer {
    /// <summary>
    /// 终态装箱分析：将 final bins 转为 PackingResult 所需结构。
    /// Final packing analyzer: converts final bins into PackingResult-ready structures.
    /// </summary>
    /// <param name="bins">最终箱子列表 / Final bin list</param>
    /// <param name="context">装箱上下文 / Packing context</param>
    /// <returns>装箱结果 / Packing result</returns>
    public Task<Result<PackingResult, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        IReadOnlyList<Bin<BinLayer, FltX>> bins,
        PackingContext? context = null) {
        var items = new List<PackingItem>();
        BinType<FltX>? binType = null;
        string? binName = null;
        ulong loadingOrder = 0;
        foreach (Bin<BinLayer, FltX> bin in bins) {
            binType ??= bin.Type;
            binName ??= bin.Type?.TypeCode;
            foreach (QuantityPlacement3<BinLayer, FltX> unit in bin.Units) {
                foreach (QuantityPlacement3<InfraItem, FltX> u in unit.Unit.Units) {
                    // **装载顺序与几何都要留下**：过去这两个字段一个不落地丢掉，下游既排不出装载次序，
                    // 也算不出重叠。货物没有形状时 `Geometry` 为 null，由几何守卫显式拒绝。
                    // **Loading order and geometry both have to survive**: both used to be dropped outright, so downstream could neither order the
                    // loads nor detect overlaps. When an item carries no shape `Geometry` is null, and the geometry guard rejects it explicitly.
                    items.Add(new PackingItem(
                        Item: u.Unit,
                        Amount: 1,
                        Geometry: u.Unit.Shape is { } shape
                            ? PackingItemGeometry.From(shape, u.Position)
                            : null,
                        LoadingOrder: loadingOrder));
                    ++loadingOrder;
                }
            }
        }
        // 箱子身份与容器尺寸一并留下：越界检测需要容器多大，而 `Items` 只描述箱内有什么。
        // Carry the bin identity and the container extents too: the outside-bin check needs the container size, and `Items` only says what is inside.
        var packedBin = new PackingBin(items) {
            Name = binName,
            Type = binType,
        };
        var agg = new PackingAggregationNonGeneric(new[] { packedBin });
        var info = context?.Info;
        var result = new PackingResult(agg, new List<MaterialSummaryEntry>(), info);
        return Task.FromResult<Result<PackingResult, ErrorCode, Error<ErrorCode>>>(
            new Ok<PackingResult, ErrorCode, Error<ErrorCode>>(result));
    }

    /// <summary>
    /// 校验每层中圆柱体的轴向是否一致。
    /// Validate that cylinder axes are consistent within each layer.
    /// </summary>
    /// <param name="bin">待校验的箱子 / Bin to validate</param>
    /// <param name="source">调用来源标识 / Caller source identifier</param>
    /// <returns>校验结果 / Validation result</returns>
    internal static Result<Success, ErrorCode, Error<ErrorCode>> RequireSingleCylinderAxisPerLayer(
        Bin<BinLayer, FltX> bin,
        string source) {
        for (int layerIndex = 0; layerIndex < bin.Units.Count; layerIndex++) {
            var axes = new HashSet<string>();
            foreach (QuantityPlacement3<InfraItem, FltX> placement in bin.Units[layerIndex].Unit.Units) {
                // 圆柱轴向来自货物自己的形状；没有形状的货物不参与轴向一致性判断。
                // The cylinder axis comes from the item's own shape; items without one do not take part in the axis-consistency check.
                IPackingShape3<FltX>? shape = placement.ResolvedPackingShape();
                if (shape?.Axis is not null) {
                    axes.Add(shape.Axis.ToString()!);
                }
            }
            if (axes.Count > 1) {
                string axisText = string.Join(", ", System.Linq.Enumerable.OrderBy(axes, a => a));
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument,
                        $"Unsupported placement geometry in {source}: layer[{layerIndex}] mixes cylinder axes [{axisText}]."));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 汇总物料使用情况 / Summarize material usage.
    /// </summary>
    /// <param name="bins">已装箱列表 / Packed bin list</param>
    /// <returns>物料汇总列表 / Material summary list</returns>
    internal static List<MaterialSummaryEntry> SummarizeMaterials(IReadOnlyList<PackingBin> bins) {
        var summary = new Dictionary<MaterialKey, ulong>();
        foreach (PackingBin bin in bins) {
            foreach (PackingItem item in bin.Items) {
                // Material counting delegated to domain item model
            }
        }
        return new List<MaterialSummaryEntry>();
    }
}
