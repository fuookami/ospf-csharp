// CP 基准夹具的冒烟测试 / Smoke tests for the CP benchmark fixture.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// CP 基准夹具 / The CP benchmark fixture.
/// </summary>
/// <remarks>
/// **这些断言只证明夹具自己是对的，不产出性能结论**。基准类里任何一个夹具建错（域越界、约束没注册、
/// 降阶失败被吞成 0）都会让测量数字"很好看"，而**基准跑起来不报任何错**。
///
/// **These assertions only prove the fixture itself is correct; they produce no performance conclusion.** Any fixture mistake inside the benchmark class
/// (an out-of-range domain, an unregistered constraint, a lowering failure swallowed into 0) makes the measured numbers "look great" while
/// **the benchmark run reports nothing wrong**.
/// </remarks>
public sealed class ConstraintProgrammingBenchmarkTests {
    /// <summary>**夹具每一组都能构建出非空结果** / Every fixture group builds a non-empty result.</summary>
    /// <param name="dataset">夹具规模 / The fixture size.</param>
    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    public void Setup_ShouldProduceNonEmptyFixtures(string dataset) {
        var benchmark = new ConstraintProgrammingBenchmark { Dataset = dataset };
        benchmark.Setup();
        try {
            benchmark.SnapshotEncode().Should().BePositive();
            benchmark.FixedDurationMipLowering().Should()
                .BePositive("排程模型必须真的降阶出约束 / the scheduling model must lower into constraints");
            benchmark.OptionalDurationMipLowering().Should().BePositive();
            // **可变时长模型降阶必须真的产出约束**：域全部合法但不含 NoOverlap 时这里会是 0，
            // 而"0 条约束"和"降阶失败被吞成 0"在基准里看不出区别。
            // **The variable-duration model must really lower into constraints**: with legal domains but no NoOverlap this is 0, and "0 constraints"
            // is indistinguishable from "lowering failed and was swallowed into 0" inside a benchmark.
            benchmark.VariableDurationMipLowering().Should().BePositive();
            benchmark.CheckpointCapture().Should().NotBeNullOrEmpty();
            benchmark.CheckpointRestore().Should()
                .Be("benchmark-checkpoint", "恢复必须回到同一个 checkpoint 标识 / restore must return the same checkpoint identifier");
            benchmark.DirectSolve().Should().NotBeNullOrEmpty();
            benchmark.SchedulingSolve().Should().NotBeNullOrEmpty();
            benchmark.OptionalSchedulingSolve().Should().NotBeNullOrEmpty();
            benchmark.VariableDurationSchedulingSolve().Should().NotBeNullOrEmpty();
            benchmark.BendersSolve().Should().NotBeNullOrEmpty();
        } finally {
            benchmark.Cleanup();
        }
    }

    /// <summary>**规模变化真的改变夹具大小** / Changing the dataset really changes the fixture size.</summary>
    /// <remarks>
    /// `[Params]` 写错（三档落到同一个分支）会让三档耗时看起来"差不多"，而**基准只会把它当成三份独立数据**。
    ///
    /// A wrong `[Params]` (all three sizes falling into the same branch) makes the three timings look "about the same", and **the benchmark just treats
    /// them as three independent datasets**.
    /// </remarks>
    [Fact]
    public void Dataset_ShouldScaleTheSchedulingFixture() {
        var lowered = new List<int>(3);
        foreach (string dataset in new[] { "small", "medium", "large" }) {
            var benchmark = new ConstraintProgrammingBenchmark { Dataset = dataset };
            benchmark.Setup();
            try {
                lowered.Add(benchmark.FixedDurationMipLowering());
            } finally {
                benchmark.Cleanup();
            }
        }

        lowered.Should().BeInAscendingOrder();
        lowered.Distinct().Should().HaveCount(3,
            "三档规模必须产生不同的约束数 / the three sizes must produce different constraint counts");
    }

    /// <summary>**约束真的注册进了模型** / The constraints really reach the model.</summary>
    /// <remarks>
    /// **降阶出的行数不能证明约束进了模型**：interval 的 link 行本身就随作业数增长，把 `NoOverlap` 整个删掉，
    /// "行数递增"仍然成立——这里变异实测如此。必须直接对**模型**断言，不是对降阶产物。
    ///
    /// **The lowered row count cannot prove the constraints reached the model**: the interval link rows alone grow with the job count, so deleting the whole
    /// `NoOverlap` still keeps "the row count increases" true —'s mutation run showed exactly that. Assert on the **model**, not on the lowering output.
    /// </remarks>
    /// <param name="dataset">夹具规模 / The fixture size.</param>
    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    [InlineData("large")]
    public void Fixtures_ShouldKeepEveryRegisteredConstraint(string dataset) {
        var benchmark = new ConstraintProgrammingBenchmark { Dataset = dataset };
        benchmark.Setup();
        try {
            int jobs = dataset switch {
                "small" => 4,
                "medium" => 6,
                _ => 8,
            };
            // 直接夹具：`jobs` 条变量上界，约束数必须正好是 `jobs`。
            benchmark.DirectModel.ConstraintCount.Should().Be(jobs);
            // 排程夹具：唯一那条约束必须是 NoOverlap。
            ConstraintProgrammingModel scheduling = benchmark.SchedulingModel;
            scheduling.ConstraintCount.Should().Be(1);
            scheduling.Snapshot().Value.Constraints[0].Constraint.Should()
                .BeOfType<NoOverlap>(
                    "NoOverlap 必须真的注册进模型 / NoOverlap must really be registered");
        } finally {
            benchmark.Cleanup();
        }
    }
}
