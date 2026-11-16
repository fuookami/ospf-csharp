#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 通过生产渲染入口测量几何守卫与 DTO 准备，不运行求解器。
/// Measure geometry validation and DTO preparation through the production renderer entry point, without running a solver.
/// </summary>
/// <remarks>
/// 移植。
///
/// **它测的是"守卫 + DTO 构造"这条路径**，不是装箱算法：`sparse` 场景里所有货物互不重叠，守卫走完全量检查；
/// `denseFirstConflict` 场景里前两件货物放在原点，守卫在**第一个冲突对**就短路返回。
/// 两个场景的耗时差异因此是**真实的路径差异**，不是夹具差异。
///
/// Ports (121 lines).
///
/// **It measures the "guard + DTO construction" path**, not the packing algorithm: in `sparse` no items overlap so the guard checks everything, while in
/// `denseFirstConflict` the first two items share the origin and the guard short-circuits at **the first conflicting pair**. The timing difference between
/// the two scenarios is therefore a **real path difference**, not a fixture difference.
/// </remarks>
[MemoryDiagnoser]
public class PackingGeometryPreparationBenchmark {
    /// <summary>单箱货物数 / Per-bin item count.</summary>
    [Params(64, 256, 1024)]
    public int Count { get; set; } = 64;

    /// <summary>稀疏成功路径或首 pair 冲突失败路径 / Sparse success path or first-pair conflict failure path.</summary>
    [Params("sparse", "denseFirstConflict")]
    public string Scenario { get; set; } = "sparse";

    private readonly PackingRendererAdapter _adapter = new();
    private PackingResult _result = null!;

    /// <summary>
    /// 在准备阶段构造真实不可变输入，避免把夹具构造计入被测时间。
    /// Build the real immutable input at setup so fixture construction stays outside the timed operation.
    /// </summary>
    [GlobalSetup]
    public void Setup() =>
        _result = BuildResult(denseFirstConflict: Scenario == "denseFirstConflict", count: Count);

    /// <summary>
    /// 构造基准夹具；`Setup` 与测试都调用它，**保证测试测的就是基准实际用的那份输入**。
    /// Build the benchmark fixture; both `Setup` and the tests call it, **so the tests exercise the very input the benchmark uses**.
    /// </summary>
    /// <param name="denseFirstConflict">是否让前两件货物冲突 / Whether the first two items conflict.</param>
    /// <param name="count">货物数 / Item count.</param>
    /// <returns>装箱结果 / The packing result.</returns>
    internal static PackingResult BuildResult(bool denseFirstConflict, int count) {
        var binType = new BinType<FltX>(
            TypeCode: "PACKING-GEOMETRY-BENCH",
            Width: new Quantity<FltX>(new FltX(100.0), SIBaseUnits.Meter),
            Height: new Quantity<FltX>(new FltX(100.0), SIBaseUnits.Meter),
            Depth: new Quantity<FltX>(new FltX(100.0), SIBaseUnits.Meter),
            Capacity: new Quantity<FltX>(new FltX(100000.0), SIBaseUnits.Kilogram)
        );

        int itemSide = (int)global::System.Math.Ceiling(global::System.Math.Sqrt(count));
        var items = new List<PackingItem>(count);
        for (int index = 0; index < count; ++index) {
            (double x, double y) = PlacementCoordinates(
                index: index,
                itemSide: itemSide,
                denseFirstConflict: denseFirstConflict
            );
            var geometry = new PackingItemGeometry(
                ShapeType: PackingShapeType.Cuboid,
                Axis: null,
                Radius: 0.0,
                MinX: x,
                MinY: y,
                MinZ: 0.0,
                MaxX: x + 0.5,
                MaxY: y + 0.5,
                MaxZ: 0.5
            );
            items.Add(new PackingItem(
                Item: new Item(
                    Id: $"packing-geometry-{(denseFirstConflict ? "denseFirstConflict" : "sparse")}-{count}-{index}",
                    Name: $"packing-geometry-{index}"
                ),
                Amount: 1UL,
                Geometry: geometry,
                LoadingOrder: (ulong)index
            ));
        }

        var bin = new PackingBin(Items: items) {
            Name = $"packing-geometry-{(denseFirstConflict ? "denseFirstConflict" : "sparse")}-{count}",
            Type = binType,
        };
        return new PackingResult(
            Aggregation: new PackingAggregationNonGeneric(Bins: new List<PackingBin> { bin }),
            MaterialSummary: Array.Empty<MaterialSummaryEntry>()
        );
    }

    /// <summary>
    /// 调用真实公开渲染 API；成功路径含几何守卫与完整 DTO 构造，冲突路径在守卫处短路。
    /// Call the real public renderer API; the success path includes geometry validation and full DTO construction, while the conflict path short-circuits
    /// in validation.
    /// </summary>
    /// <returns>生产 renderer 的结果对象 / The production renderer result object.</returns>
    [Benchmark]
    public object ToSchema() => _adapter.ToSchema(_result);

    /// <summary>
    /// 网格坐标；`denseFirstConflict` 时前两件都在原点，其余整体偏移 2 格避开冲突。
    /// Grid coordinates; under `denseFirstConflict` the first two items sit at the origin and the rest shift by two cells to avoid it.
    /// </summary>
    internal static (double X, double Y) PlacementCoordinates(
        int index,
        int itemSide,
        bool denseFirstConflict) {
        if (denseFirstConflict && index < 2) {
            return (0.0, 0.0);
        }
        int gridIndex = denseFirstConflict ? index - 2 : index;
        double offset = denseFirstConflict ? 2.0 : 0.0;
        double x = offset + (gridIndex % itemSide) * 1.5;
        double y = offset + (gridIndex / itemSide) * 1.5;
        return (x, y);
    }
}
