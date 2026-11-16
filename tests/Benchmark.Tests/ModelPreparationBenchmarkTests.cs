// 模型准备基准夹具测试 / Tests for the model-preparation benchmark fixtures.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// 模型准备基准夹具 / The model-preparation benchmark fixtures.
/// </summary>
public sealed class ModelPreparationBenchmarkTests {
    /// <summary>**规模因子 1 / 2 / 4 各放大变量与行** / Scale factors 1 / 2 / 4 multiply variables and rows.</summary>
    [Theory]
    [InlineData("small", 1)]
    [InlineData("medium", 2)]
    [InlineData("large", 4)]
    public void Setup_ShouldScaleVariablesAndRows(string dataset, int factor) {
        var benchmark = new ModelPreparationBenchmark { Dataset = dataset };

        benchmark.Setup();

        benchmark.Factor.Should().Be(factor);
        benchmark.Projection.Decisions.Should().HaveCount(128 * factor);
        benchmark.Source.RelationConstraints.Should().HaveCount(32);
    }

    /// <summary>**决策均分到 8 个块** / Decisions split evenly across the eight blocks.</summary>
    /// <remarks>
    /// 决策若全落一个块，`prepareScalarPricingModels` 的循环就只在第一个块上有效工作，
    /// 而**"8 个块都跑了"依然成立**——循环次数与真实工作量不是一回事。
    ///
    /// If every decision landed in one block, the `prepareScalarPricingModels` loop would do real work only on the first block while **"all eight
    /// blocks ran" stays true** — loop count and real work are not the same thing.
    /// </remarks>
    [Fact]
    public void Setup_ShouldSplitDecisionsAcrossEveryBlock() {
        var benchmark = new ModelPreparationBenchmark { Dataset = "small" };

        benchmark.Setup();

        benchmark.Projection.Decisions.Select(decision => decision.BlockId).Distinct()
            .Should().HaveCount(ModelPreparationBenchmark.BlockIds.Count);
        foreach (string block in ModelPreparationBenchmark.BlockIds) {
            benchmark.Projection.Decisions.Count(decision => decision.BlockId == block)
                .Should().BeGreaterThan(0);
        }
        benchmark.Columns.Should().HaveCount(ModelPreparationBenchmark.BlockIds.Count * 8);
    }

    /// <summary>**每个块的投影列只含本块的决策** / Each block's projected column holds only its own decisions.</summary>
    /// <remarks>
    /// 列里塞进别的块的决策时，主问题装配依然成功，而**凸组合约束的系数已经多出了本不属于它的变量**。
    ///
    /// Stuffing another block's decisions into a column still lets the master assemble, while **the convexity row now carries variables that do not
    /// belong to it**.
    /// </remarks>
    [Fact]
    public void Setup_ShouldKeepEachColumnInsideItsOwnBlock() {
        var benchmark = new ModelPreparationBenchmark { Dataset = "small" };

        benchmark.Setup();

        var decisionBlocks = benchmark.Projection.Decisions
            .ToDictionary(decision => decision.Id, decision => decision.BlockId);
        foreach (ProjectedColumn<Flt64> column in benchmark.Columns) {
            column.Values.Keys.Should().OnlyContain(
                id => decisionBlocks[id] == column.BlockId);
            column.BlockId.Should().Be(column.Id.Split('_')[0]);
        }
    }

    /// <summary>**稀疏主问题能装配出行，且行数随列数增长** / The sparse master assembles rows, and the row count grows with the columns.</summary>
    /// <remarks>
    /// **必须调用 `[Benchmark]` 方法本身**，不能只调内部的 `RegisterMaster`：两者是**两份独立的调用点**，
    /// 变异只改其中一份时，只测另一份的断言会静默通过（这里实测过一次）。
    ///
    /// **The `[Benchmark]` method itself must be called**, not just the internal `RegisterMaster`: they are **two independent call sites**, and a mutation
    /// that changes only one of them slips past an assertion that exercises only the other (observed once in).
    /// </remarks>
    [Fact]
    public void AssembleSparseMaster_ShouldProduceRows() {
        var benchmark = new ModelPreparationBenchmark { Dataset = "small" };
        benchmark.Setup();

        int assembled = benchmark.AssembleSparseMaster();
        assembled.Should().Be(
            benchmark.Source.RelationConstraints.Count
            + (ModelPreparationBenchmark.BlockIds.Count * 8),
            "行数与列变量数都必须计入装配量 / both rows and column variables must count towards the assembly measure");

        ColumnMasterRegistration<Flt64> registration = benchmark.RegisterMaster();
        try {
            registration.Rows.Should().NotBeEmpty();
            registration.ColumnVariables.Should()
                .HaveCount(ModelPreparationBenchmark.BlockIds.Count * 8);
        } finally {
            registration.Model.Close();
        }
    }

    /// <summary>**局部定价模型逐块产出** / The local pricing models are produced block by block.</summary>
    [Fact]
    public void PrepareScalarPricingModels_ShouldProducePerBlockModels() {
        var benchmark = new ModelPreparationBenchmark { Dataset = "small" };

        benchmark.Setup();

        benchmark.PrepareScalarPricingModels().Should().BeGreaterThan(0);
    }
}
