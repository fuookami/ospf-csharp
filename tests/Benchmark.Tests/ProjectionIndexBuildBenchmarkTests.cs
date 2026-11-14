// 投影索引基准夹具测试 / Tests for the projection-index benchmark fixtures.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// 投影索引基准夹具 / The projection-index benchmark fixtures.
/// </summary>
public sealed class ProjectionIndexBuildBenchmarkTests {
    /// <summary>**源模型的行数是变量数的一半** / The source model has half as many rows as variables.</summary>
    /// <remarks>
    /// 行数与变量数的比例决定了投影索引与行索引的规模比。**行数写成 `Size` 而非 `Size / 2` 时，
    /// "模型建出来了"依然成立，而基准测的已经是另一种规模**。
    ///
    /// The row-to-variable ratio fixes the size ratio between the projection index and the row index. **Writing `Size` rows instead of `Size / 2`
    /// keeps "the model builds" true while the benchmark measures a different scale.**
    /// </remarks>
    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    public void Setup_ShouldBuildHalfAsManyRowsAsVariables(int size) {
        var benchmark = new ProjectionIndexBuildBenchmark { Size = size, Consumer = "snapshot" };

        benchmark.Setup();

        benchmark.Source.RelationConstraints.Should().HaveCount(size / 2);
    }

    /// <summary>**决策按块均分，且每块的投影列只含本块决策** / Decisions split evenly across blocks, and each block's projected column holds only its own decisions.</summary>
    /// <remarks>
    /// 决策若全落进一个块，其余块的投影列就是空的，而**"每条决策都有块归属"依然成立**——校验必须落在"每块拿到的是自己那份"。
    ///
    /// If every decision fell into one block, the other blocks' columns would be empty while **"every decision has a block" stays true** — the check
    /// must be that "each block gets its own share".
    /// </remarks>
    [Fact]
    public void Setup_ShouldSplitDecisionsAcrossBlocks() {
        var benchmark = new ProjectionIndexBuildBenchmark { Size = 16, Consumer = "snapshot" };

        benchmark.Setup();
        ColumnProjection<Flt64> projection = benchmark.Create();

        projection.Decisions.Should().HaveCount(16);
        projection.Decisions.Select(decision => decision.BlockId).Distinct()
            .Should().HaveCount(ProjectionIndexBuildBenchmark.BlockIds.Count);
        foreach (string block in ProjectionIndexBuildBenchmark.BlockIds) {
            projection.Decisions.Count(decision => decision.BlockId == block).Should().Be(4);
        }
    }

    /// <summary>**三个消费面各自产出对应形状** / Each of the three consumers yields its own shape.</summary>
    /// <remarks>
    /// 三个消费面共用一次 `Setup`，所以断言必须分辨"消费面真的切换了"：`master` 给出行数、`pricing` 与 `snapshot` 给出对象。
    /// **只断言"不为 null"时，三个面互相串台也照样通过**。
    ///
    /// The three consumers share one `Setup`, so the assertions must show the consumer really switched: `master` yields a row count while `pricing`
    /// and `snapshot` yield objects. **Asserting only "not null" passes even when the three are wired to the wrong branch.**
    /// </remarks>
    [Fact]
    public void ConsumeAsync_ShouldRouteEachConsumerToItsOwnBranch() {
        var master = new ProjectionIndexBuildBenchmark { Size = 16, Consumer = "master" };
        master.Setup();
        var pricing = new ProjectionIndexBuildBenchmark { Size = 16, Consumer = "pricing" };
        pricing.Setup();
        var snapshot = new ProjectionIndexBuildBenchmark { Size = 16, Consumer = "snapshot" };
        snapshot.Setup();

        master.ColdRevision().Should().BeOfType<int>();
        ((int)master.ColdRevision()).Should().Be(8, "master 的行数等于源模型行数 / master rows equal the source row count");
        pricing.ColdRevision().Should().BeAssignableTo<ColumnPricingRequest<Flt64>>();
        snapshot.ColdRevision().Should().BeOfType<LinearModelSnapshot<Flt64>>();
    }

    /// <summary>**列真的被登记进主问题** / The columns really reach the master registration.</summary>
    /// <remarks>
    /// `Rows` 来自源模型，与登记的列数无关，所以"一列都不登记"时 `master` 分支照样返回 8——
    /// **只断言行数的测试根本没测到列登记**。这里断言列变量数与凸组合数都等于块数。
    /// `Rows` comes from the source model and is independent of the registered column count, so "register no columns" still has the `master` branch
    /// return 8 — **a test asserting only the row count never exercised column registration**. These assertions require both the column-variable count and
    /// the convexity count to equal the block count.
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task RegisterMasterAsync_ShouldRegisterEveryBlockColumn() {
        var benchmark = new ProjectionIndexBuildBenchmark { Size = 16, Consumer = "master" };
        benchmark.Setup();

        ColumnMasterRegistration<Flt64> registration =
            await benchmark.RegisterMasterAsync(benchmark.Create());
        try {
            registration.ColumnVariables.Should().HaveCount(
                ProjectionIndexBuildBenchmark.BlockIds.Count);
            registration.Convexities.Should().HaveCount(
                ProjectionIndexBuildBenchmark.BlockIds.Count);
            registration.Columns.Should().HaveCount(
                ProjectionIndexBuildBenchmark.BlockIds.Count);
        } finally {
            registration.Model.Close();
        }
    }

    /// <summary>**同一个暖投影重复消费与冷消费给出同样的形状** / A warm projection consumed repeatedly yields the same shape as a cold one.</summary>
    [Fact]
    public void WarmRevision_ShouldAgreeWithColdRevision() {
        var benchmark = new ProjectionIndexBuildBenchmark { Size = 32, Consumer = "master" };

        benchmark.Setup();

        benchmark.ColdRevision().Should().Be(benchmark.WarmRevision());
        benchmark.WarmRevision().Should().Be(benchmark.WarmRevision());
    }
}
