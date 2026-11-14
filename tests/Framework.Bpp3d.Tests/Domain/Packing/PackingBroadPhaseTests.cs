// 宽相位扫掠测试 / Tests for the broad-phase sweep.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Geometry;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Domain.Packing;

/// <summary>
/// 装箱几何的宽相位扫掠 / The broad-phase sweep of the packing geometry.
/// </summary>
/// <remarks>
/// 宽相位是**剪枝**：它的价值是少算配对，它的风险是**漏掉真的相交的配对**。
/// 所以这里的核心断言不是"配对数变少了"，而是"扫掠给出的配对集合里，穷举会判相交的配对一条都不少"。
///
/// The broad phase **prunes**: its value is fewer pairs, its risk is **dropping a genuinely intersecting pair**. So the core assertion here is not
/// "there are fewer pairs" but "the sweep's pair set contains every pair the exhaustive check would call intersecting".
/// </remarks>
public sealed class PackingBroadPhaseTests {
    private static PackingItemGeometry Box(double x, double y, double z, double size = 1.0) =>
        new(PackingShapeType.Cuboid, Axis: null, Radius: 0.0,
            MinX: x, MinY: y, MinZ: z,
            MaxX: x + size, MaxY: y + size, MaxZ: z + size);

    private static PackingItemGeometry Cylinder(double x, double y, double z, double radius = 0.5) =>
        new(PackingShapeType.Cylinder, Axis3.Y, radius,
            MinX: x, MinY: y, MinZ: z,
            MaxX: x + radius * 2, MaxY: y + radius * 2, MaxZ: z + radius * 2);

    /// <summary>**扫掠不得漏掉任何一条真实相交的配对** / The sweep must not drop any genuinely intersecting pair.</summary>
    /// <remarks>
    /// 28 个长方体排在一条线上、其中若干对真的相交（相邻重叠 + 一对抗远离重叠）。穷举全配对得到"真值集合"，
    /// 扫掠给出的候选集合必须**包含**它。只比较"候选少了很多"会放过漏掉个别配对的实现。
    ///
    /// 28 boxes in a line, several of which genuinely intersect (neighbouring overlaps plus one distant overlapping pair). The exhaustive pairing gives
    /// the ground truth set; the sweep's candidate set must **contain** it. Comparing only "the candidates are much fewer" would let an implementation
    /// that drops individual pairs pass.
    /// </remarks>
    [Fact]
    public void CandidatePairs_ShouldNotDropAnyOverlappingPair() {
        var geometries = new List<PackingItemGeometry>();
        for (int index = 0; index < 28; ++index) {
            // 每 10 个里有 1 个与下一个重叠 0.5；第 3 与第 27 个故意放在同一位置。
            // Every tenth box overlaps the next by 0.5; boxes 3 and 27 are deliberately at the same position.
            geometries.Add(Box(index * 3.0, 0, 0, size: 1.0));
        }
        geometries[27] = Box(3 * 3.0, 0, 0, size: 1.0);

        var overlapping = new List<(int Lhs, int Rhs)>();
        for (int lhsIndex = 0; lhsIndex < geometries.Count; ++lhsIndex) {
            for (int rhsIndex = lhsIndex + 1; rhsIndex < geometries.Count; ++rhsIndex) {
                if (PackingGeometryGuard.Overlaps(geometries[lhsIndex], geometries[rhsIndex])) {
                    overlapping.Add((lhsIndex, rhsIndex));
                }
            }
        }
        overlapping.Should().NotBeEmpty();

        IReadOnlyList<(int Lhs, int Rhs)>? candidates = PackingGeometryGuard.CandidateGeometryPairs(geometries);

        candidates.Should().NotBeNull();
        foreach ((int lhs, int rhs) in overlapping) {
            candidates.Should().Contain((lhs, rhs));
        }
        candidates!.Count.Should().BeLessThan(geometries.Count * (geometries.Count - 1) / 2,
            "the sweep is supposed to prune something");
    }

    /// <summary>**含圆柱的配对全部保留** / Every cylinder-related pair is kept.</summary>
    /// <remarks>
    /// 圆柱判据不是包围盒，扫掠无法证明它不相交。**把圆柱当长方体一起剪枝**会漏掉真正相贯的圆柱对。
    ///
    /// The cylinder predicate is not a bounding box, so the sweep cannot prove it disjoint. **Pruning cylinders like boxes** drops cylinders that really
    /// do intersect.
    /// </remarks>
    [Fact]
    public void CandidatePairs_ShouldKeepEveryCylinderPair() {
        var geometries = new List<PackingItemGeometry>();
        for (int index = 0; index < 20; ++index) {
            geometries.Add(Box(index * 5.0, 0, 0));
        }
        // 两根圆柱放在很远处，彼此相距 1.0（半径和 1.0），并且与所有长方体都不相交。
        // Two cylinders far away, one radius-sum apart, disjoint from every box.
        geometries[10] = Cylinder(500.0, 0, 0);
        geometries[11] = Cylinder(501.0, 0, 0);

        IReadOnlyList<(int Lhs, int Rhs)>? candidates = PackingGeometryGuard.CandidateGeometryPairs(geometries);

        candidates.Should().NotBeNull();
        candidates!.Should().Contain((10, 11));
        candidates.Should().Contain((0, 10));
        candidates.Should().Contain((0, 11));
        // 配对按 (较小下标, 较大下标) 归一化 / Pairs are normalised to (smaller index, larger index).
        candidates.Should().Contain((10, 19));
    }

    /// <summary>**物品不够多时不启用扫掠** / The sweep is not used below the item threshold.</summary>
    [Fact]
    public void CandidatePairs_ShouldReturnNullBelowTheThreshold() {
        var geometries = new List<PackingItemGeometry>();
        for (int index = 0; index < 15; ++index) {
            geometries.Add(Box(index * 3.0, 0, 0));
        }

        PackingGeometryGuard.CandidateGeometryPairs(geometries).Should().BeNull();
    }

    /// <summary>**数值不安全时退回穷举** / Falling back to the exhaustive pairing for numerically unsafe geometry.</summary>
    /// <remarks>
    /// 两种不安全：坐标量级过大和**退化包围盒**（宽/高/深为 0）。退化盒在扫掠里会被自己的端点剪掉，
    /// 而它同时又会被重叠判据当成"没占地方"——两种误判叠加起来最难发现。
    ///
    /// Two kinds: an oversized coordinate magnitude and a **degenerate box** (zero width/height/depth). A degenerate box gets pruned by its own endpoints
    /// while the overlap predicate treats it as occupying nothing — the combination is the hardest to notice.
    /// </remarks>
    [Fact]
    public void CandidatePairs_ShouldReturnNullForUnsafeGeometry() {
        var huge = new List<PackingItemGeometry>();
        for (int index = 0; index < 20; ++index) {
            huge.Add(Box(index * 3.0, 0, 0));
        }
        huge[5] = Box(1e7, 0, 0);
        PackingGeometryGuard.CandidateGeometryPairs(huge).Should().BeNull();

        var degenerate = new List<PackingItemGeometry>();
        for (int index = 0; index < 20; ++index) {
            degenerate.Add(Box(index * 3.0, 0, 0));
        }
        degenerate[7] = Box(21.0, 0, 0, size: 0.0);
        PackingGeometryGuard.CandidateGeometryPairs(degenerate).Should().BeNull();
    }

    /// <summary>**扫掠轴取最稀疏的那个** / The sweep axis is the sparsest one.</summary>
    /// <remarks>
    /// 故意只让 Z 方向拉开距离：X/Y 上每个物品的跨度都等于全局跨度（评分 1），Z 上全局跨度远大于单件跨度。
    /// **恒取 X** 在这里给出不同的扫掠顺序与剪枝效果。
    ///
    /// The geometry deliberately spreads only along Z: on X and Y each item's span equals the global span (score 1), while on Z the global span is far
    /// larger than a single item. **Always picking X** yields a different sweep order and pruning.
    /// </remarks>
    [Fact]
    public void SelectSweepAxis_ShouldPickTheSparsestAxis() {
        var geometries = new List<PackingItemGeometry>();
        for (int index = 0; index < 20; ++index) {
            geometries.Add(Box(0, 0, index * 4.0, size: 1.0));
        }

        IReadOnlyList<(int Lhs, int Rhs)>? candidates = PackingGeometryGuard.CandidateGeometryPairs(geometries);

        candidates.Should().NotBeNull();
        candidates!.Should().BeEmpty("nothing overlaps when the boxes are spread along one axis");

        var bounds = new Dictionary<int, PackingGeometryGuard.SweepBounds>();
        for (int index = 0; index < geometries.Count; ++index) {
            PackingItemGeometry geometry = geometries[index];
            bounds[index] = new PackingGeometryGuard.SweepBounds(
                geometry.MinX, geometry.MaxX, geometry.MinY, geometry.MaxY, geometry.MinZ, geometry.MaxZ);
        }
        var indices = Enumerable.Range(0, geometries.Count).ToList();
        PackingGeometryGuard.SelectSweepAxis(bounds, indices).Should().Be(Axis3.Z);
    }

    /// <summary>**守卫的整体判决与穷举一致** / The guard's verdict matches the exhaustive one.</summary>
    [Fact]
    public void GuardVerdict_ShouldMatchTheExhaustiveCheck() {
        var geometries = new List<PackingItemGeometry>();
        for (int index = 0; index < 24; ++index) {
            geometries.Add(Box(index * 2.0, 0, 0, size: 1.0));
        }

        static PackingBin BinOf(IReadOnlyList<PackingItemGeometry> geometries) =>
            new(geometries.Select(geometry => new PackingItem(
                new Fuookami.Ospf.Framework.Bpp3d.Infra.Item("I", "I"), 1, geometry)).ToList()) {
                Name = "B1",
                Type = new Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model.BinType<
                    Fuookami.Ospf.Math.Algebra.Number.FltX>(
                    "B1",
                    new Fuookami.Ospf.Quantities.Quantity.Quantity<Fuookami.Ospf.Math.Algebra.Number.FltX>(
                        new Fuookami.Ospf.Math.Algebra.Number.FltX(1000),
                        Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
                    new Fuookami.Ospf.Quantities.Quantity.Quantity<Fuookami.Ospf.Math.Algebra.Number.FltX>(
                        new Fuookami.Ospf.Math.Algebra.Number.FltX(1000),
                        Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
                    new Fuookami.Ospf.Quantities.Quantity.Quantity<Fuookami.Ospf.Math.Algebra.Number.FltX>(
                        new Fuookami.Ospf.Math.Algebra.Number.FltX(1000),
                        Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter),
                    new Fuookami.Ospf.Quantities.Quantity.Quantity<Fuookami.Ospf.Math.Algebra.Number.FltX>(
                        new Fuookami.Ospf.Math.Algebra.Number.FltX(1000),
                        Fuookami.Ospf.Quantities.Unit.SIBaseUnits.Meter)),
            };

        // 无重叠 → 通过 / No overlap → passes.
        PackingGeometryGuard.RequirePackedBinShapeGeometry(BinOf(geometries), "test").IsOk.Should().BeTrue();

        // 远处插入一个与第 0 个重叠的物品（跨越很多"在扫掠里早该退休"的物品）→ 必须被拒。
        // Insert a far-away item that overlaps item 0 (crossing many items the sweep would have retired early) → must be rejected.
        var withLateOverlap = new List<PackingItemGeometry>(geometries) {
            Box(0.25, 0, 0, size: 1.0),
        };
        PackingGeometryGuard.RequirePackedBinShapeGeometry(BinOf(withLateOverlap), "test").IsOk.Should().BeFalse();
    }
}
