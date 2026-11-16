#nullable enable

using BenchmarkDotNet.Attributes;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System.Collections.Generic;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 对内置货物的真实堆叠入口计时，不复制算法 / Time the production pile entry point without replicating its algorithm.
/// </summary>
/// <remarks>
/// 与基线 `PilePreparationBenchmark.kt`（2801 B）对应，但**仓库内的 C# `ItemMerger.MergePiles` 是简化移植**：
/// 它只比较底面尺寸与总高，**不读 `PackageAttribute.MaxLayer`、也不读 `restWeight`**。因此 `Scenario` 参数在 C# 侧
/// 只改变夹具（层数上限 1 或 4），**对被测路径没有影响**——这一点由 `PilePreparationBenchmarkTests` 显式断言，
/// 而不是假装场景区分有效。真实分层合并仍列 P9 缺口。
///
/// Mirrors `PilePreparationBenchmark.kt` (2801 B), but **the in-repo C# `ItemMerger.MergePiles` is a simplified port**: it compares only base
/// dimensions and total height, **reading neither `PackageAttribute.MaxLayer` nor `restWeight`**. So the `Scenario` parameter only changes the fixture
/// in C# (layer cap 1 vs 4) and **has no effect on the measured path** — which `PilePreparationBenchmarkTests` asserts explicitly rather than
/// pretending the scenario distinction is effective. Real layered merging stays a P9 gap.
/// </remarks>
[MemoryDiagnoser]
public class PilePreparationBenchmark {
    /// <summary>货物数 N / Item count N.</summary>
    [Params(64, 128, 256)]
    public int Count { get; set; } = 64;

    /// <summary>无可合并或多堆场景 / Unmergeable or multiple-pile scenario.</summary>
    [Params("unmergeable", "multiplePiles")]
    public string Scenario { get; set; } = "unmergeable";

    private List<(ActualItem Item, ulong Amount)> _items = null!;
    private Container3Shape<FltX> _space = null!;

    /// <summary>层数上限：`unmergeable` 为 1，`multiplePiles` 为 4 / Layer cap: 1 for `unmergeable`, 4 for `multiplePiles`.</summary>
    internal ulong LayerCap => Scenario == "unmergeable" ? 1UL : 4UL;

    /// <summary>只读输入在计时之外构造 / Construct read-only inputs outside timing.</summary>
    [GlobalSetup]
    public void Setup() {
        var attribute = new PackageAttribute(
            PackageType: PackageType.Box,
            PackageMaxLayer: new UInt64(LayerCap),
            MaxHeight: new UInt64(5),
            MinDepth: new UInt64(1),
            MaxDepth: new UInt64(5));
        _items = new List<(ActualItem, ulong)>(Count);
        for (int index = 0; index < Count; ++index) {
            var item = new ActualItem(
                id: $"pile_{index}",
                name: $"pile_{index}",
                width: new Quantity<FltX>(FltX.One, SIBaseUnits.Meter),
                height: new Quantity<FltX>(FltX.One, SIBaseUnits.Meter),
                depth: new Quantity<FltX>(FltX.One, SIBaseUnits.Meter),
                weight: new Quantity<FltX>(
                    new FltX((index % 5) + 1), SIBaseUnits.Kilogram),
                packageAttribute: attribute,
                orientations: new[] { Orientation.Upright });
            _items.Add((item, 1UL));
        }
        _space = new Container3Shape<FltX>(
            Width: new Quantity<FltX>(new FltX(3), SIBaseUnits.Meter),
            Height: new Quantity<FltX>(new FltX(4), SIBaseUnits.Meter),
            Depth: new Quantity<FltX>(new FltX(3), SIBaseUnits.Meter));
    }

    /// <summary>
    /// 合并会返回新列表，不修改夹具 / Merging returns fresh lists without mutating the fixture.
    /// </summary>
    /// <returns>完整生产结果 / The full production result.</returns>
    [Benchmark]
    public IReadOnlyList<Pile> MergePiles() =>
        ItemMerger.MergePiles(
            items: _items,
            space: _space,
            restWeight: new Quantity<FltX>(new FltX(1000), SIBaseUnits.Kilogram));

    internal IReadOnlyList<(ActualItem Item, ulong Amount)> Items => _items;

    internal Container3Shape<FltX> Space => _space;
}
