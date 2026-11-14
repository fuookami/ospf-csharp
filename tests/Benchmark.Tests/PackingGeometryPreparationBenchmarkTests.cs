// 几何准备基准的场景有效性测试 / Scenario-validity tests for the packing-geometry preparation benchmark.

using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Model;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Packing.Service;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// 基准的两个场景必须走**不同的路径**，否则"场景"只是换个名字的同一份夹具。
/// The benchmark's two scenarios must take **different paths**, otherwise "scenario" is one fixture under two names.
/// </summary>
/// <remarks>
/// **这里刻意调用 `PackingGeometryPreparationBenchmark.BuildResult`，而不是在测试里复制一份夹具。**
/// 第一版就是复制的，结果三个变异体全部存活：我把基准的坐标逻辑改坏，测试却用自己那份副本继续通过。
/// 复制的夹具只能证明"我以为基准在做什么"，不能证明"基准实际在做什么"。
///
/// **These deliberately call `PackingGeometryPreparationBenchmark.BuildResult` instead of copying the fixture.** The first version copied it, and all three
/// mutants survived: I broke the benchmark's coordinate logic while the test kept passing on its own copy. A copied fixture proves only "what I think the
/// benchmark does", never "what it actually does".
/// </remarks>
public class PackingGeometryPreparationBenchmarkTests {
    private static PackingResult Build(bool denseFirstConflict, int count = 9) =>
        PackingGeometryPreparationBenchmark.BuildResult(denseFirstConflict: denseFirstConflict, count: count);

    /// <summary>**稀疏场景必须成功** / The sparse scenario must succeed.</summary>
    [Fact]
    public void SparseScenario_ShouldSucceedThroughTheProductionRenderer() {
        var adapter = new PackingRendererAdapter();
        Result<SchemaDTO, ErrorCode, Error<ErrorCode>> result =
            adapter.ToSchema(Build(denseFirstConflict: false));

        result.Should().BeOfType<Ok<SchemaDTO, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**冲突场景必须在守卫处失败** / The conflict scenario must fail in the guard.</summary>
    [Fact]
    public void DenseFirstConflictScenario_ShouldFailThroughTheProductionRenderer() {
        var adapter = new PackingRendererAdapter();
        Result<SchemaDTO, ErrorCode, Error<ErrorCode>> result =
            adapter.ToSchema(Build(denseFirstConflict: true));

        result.Should().BeOfType<Failed<SchemaDTO, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**两个场景必须产生不同的结果** / The two scenarios must produce different outcomes.</summary>
    /// <remarks>
    /// 这条是"场景有效"的最小判据：如果两个场景都成功或都失败，基准测出的差异就与场景无关。
    ///
    /// This is the minimal criterion for "the scenario is meaningful": if both succeed or both fail, the measured difference has nothing to do with the
    /// scenario.
    /// </remarks>
    [Fact]
    public void TheTwoScenarios_ShouldDiverge() {
        var adapter = new PackingRendererAdapter();
        object sparse = adapter.ToSchema(Build(denseFirstConflict: false));
        object dense = adapter.ToSchema(Build(denseFirstConflict: true));

        sparse.GetType().Name.Should().StartWith("Ok");
        dense.GetType().Name.Should().StartWith("Failed");
    }

    /// <summary>**稀疏坐标必须真的落在网格上** / The sparse coordinates must really land on the grid.</summary>
    [Fact]
    public void SparseCoordinates_ShouldLandOnTheGrid() {
        for (int index = 0; index < 9; ++index) {
            (double x, double y) = PackingGeometryPreparationBenchmark.PlacementCoordinates(
                index: index,
                itemSide: 3,
                denseFirstConflict: false
            );
            x.Should().BeApproximately((index % 3) * 1.5, 1e-9);
            y.Should().BeApproximately((index / 3) * 1.5, 1e-9);
        }
    }

    /// <summary>**冲突场景的前两件必须都在原点** / The conflict scenario's first two items must both sit at the origin.</summary>
    [Fact]
    public void DenseFirstConflictCoordinates_ShouldPutBothFirstItemsAtTheOrigin() {
        (double x0, double y0) = PackingGeometryPreparationBenchmark.PlacementCoordinates(
            index: 0, itemSide: 3, denseFirstConflict: true);
        (double x1, double y1) = PackingGeometryPreparationBenchmark.PlacementCoordinates(
            index: 1, itemSide: 3, denseFirstConflict: true);

        x0.Should().Be(0.0);
        y0.Should().Be(0.0);
        x1.Should().Be(0.0);
        y1.Should().Be(0.0);
    }

    /// <summary>**冲突场景的第三件必须已经偏移，不能继续重叠** / The third item must already be offset rather than still overlapping.</summary>
    [Fact]
    public void DenseFirstConflictCoordinates_ShouldOffsetFromTheThirdItemOn() {
        (double x, double y) = PackingGeometryPreparationBenchmark.PlacementCoordinates(
            index: 2, itemSide: 3, denseFirstConflict: true);

        x.Should().BeApproximately(2.0, 1e-9);
        y.Should().BeApproximately(2.0, 1e-9);
    }
}
