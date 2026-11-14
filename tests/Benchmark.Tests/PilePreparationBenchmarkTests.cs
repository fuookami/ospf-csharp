// 堆叠准备基准夹具测试 / Tests for the pile-preparation benchmark fixtures.

using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Framework.Bpp3d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// 堆叠准备基准夹具 / The pile-preparation benchmark fixtures.
/// </summary>
public sealed class PilePreparationBenchmarkTests {
    /// <summary>**货物数、单位与方向由夹具固定** / Count, units, and orientation are fixed by the fixture.</summary>
    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    public void Setup_ShouldProduceTheRequestedItems(int count) {
        var benchmark = new PilePreparationBenchmark { Count = count, Scenario = "multiplePiles" };

        benchmark.Setup();

        benchmark.Items.Should().HaveCount(count);
        benchmark.Items.Should().OnlyContain(entry => entry.Amount == 1UL);
        benchmark.Items[0].Item.Width.Unit.Should().Be(SIBaseUnits.Meter);
        benchmark.Items[0].Item.Weight.Unit.Should().Be(SIBaseUnits.Kilogram);
    }

    /// <summary>**场景改变的是层数上限，不是重量** / The scenario changes the layer cap, not the weight.</summary>
    /// <remarks>
    /// 重量随 `index % 5` 变化是夹具的一部分，与场景无关。**若场景顺带改了重量，"两个场景的差异"就无法归因到分层**。
    ///
    /// The weight varying with `index % 5` is part of the fixture and independent of the scenario. **If the scenario also changed weights, "the difference
    /// between the two scenarios" could not be attributed to layering.**
    /// </remarks>
    [Fact]
    public void Setup_ShouldVaryOnlyTheLayerCapBetweenScenarios() {
        var unmergeable = new PilePreparationBenchmark { Count = 64, Scenario = "unmergeable" };
        unmergeable.Setup();
        var multiple = new PilePreparationBenchmark { Count = 64, Scenario = "multiplePiles" };
        multiple.Setup();

        unmergeable.Items[0].Item.PackageAttribute.MaxLayer.ToFlt64().ToDouble().Should().Be(1.0);
        multiple.Items[0].Item.PackageAttribute.MaxLayer.ToFlt64().ToDouble().Should().Be(4.0);
        unmergeable.Items.Select(entry => entry.Item.Weight.Value.ToDecimal())
            .Should().Equal(multiple.Items.Select(entry => entry.Item.Weight.Value.ToDecimal()));
        unmergeable.Space.Should().Be(multiple.Space);
    }

    /// <summary>**当前实现的合并结果与层数上限无关** / The current merger's result is independent of the layer cap.</summary>
    /// <remarks>
    /// 这是一条**记录缺口**的断言，不是"设计正确"的断言：仓库内的 `ItemMerger.MergePiles` 只比较底面尺寸与总高，
    /// **不读 `PackageAttribute.MaxLayer`、也不读 `restWeight`**。基线期望"层数上限 1 时不可堆叠"，
    /// 而这里两个场景给出相同的堆叠数。**一旦有人补上分层逻辑，这条断言就会失败——那正是它存在的意义**：
    /// 它把"场景参数目前不影响被测路径"钉在门禁里，避免基准被当成"分层合并的耗时证据"。
    ///
    /// This assertion **records a gap**, it does not assert a correct design: the in-repo `ItemMerger.MergePiles` compares only base dimensions and total
    /// height, **reading neither `PackageAttribute.MaxLayer` nor `restWeight`**. The baseline expects "layer cap 1 means no stacking", whereas here both
    /// scenarios yield the same pile count. **Once layered merging lands this assertion fails — and that is exactly its purpose**: it pins "the scenario
    /// parameter currently does not affect the measured path" into the gate so the benchmark cannot be cited as evidence about layered merging.
    /// </remarks>
    [Fact]
    public void MergePiles_ShouldCurrentlyIgnoreTheLayerCap() {
        var unmergeable = new PilePreparationBenchmark { Count = 64, Scenario = "unmergeable" };
        unmergeable.Setup();
        var multiple = new PilePreparationBenchmark { Count = 64, Scenario = "multiplePiles" };
        multiple.Setup();

        unmergeable.MergePiles().Should().HaveCount(multiple.MergePiles().Count);
        multiple.MergePiles().Should().NotBeEmpty(
            "夹具必须真的产生堆叠，否则这条对比毫无信息 / the fixture must really produce piles, "
            + "otherwise this comparison carries no information");
    }

    /// <summary>**合并不修改夹具** / Merging does not mutate the fixture.</summary>
    [Fact]
    public void MergePiles_ShouldNotMutateTheFixture() {
        var benchmark = new PilePreparationBenchmark { Count = 64, Scenario = "multiplePiles" };
        benchmark.Setup();
        System.Collections.Generic.IReadOnlyList<Pile> before = benchmark.MergePiles();

        System.Collections.Generic.IReadOnlyList<Pile> after = benchmark.MergePiles();

        after.Should().HaveCount(before.Count);
        benchmark.Items.Should().HaveCount(64);
        benchmark.Items.Should().OnlyContain(entry => entry.Amount == 1UL);
    }

    /// <summary>**堆叠的上下两层来自不同货物** / The two stacked layers come from different items.</summary>
    [Fact]
    public void MergePiles_ShouldStackDistinctItems() {
        var benchmark = new PilePreparationBenchmark { Count = 64, Scenario = "multiplePiles" };
        benchmark.Setup();

        System.Collections.Generic.IReadOnlyList<Pile> piles = benchmark.MergePiles();

        piles.Should().OnlyContain(pile => pile.BottomItem.Id != pile.TopItem.Id);
        piles.First().BottomItem.Height.Unit.Should().Be(SIBaseUnits.Meter);
    }
}
