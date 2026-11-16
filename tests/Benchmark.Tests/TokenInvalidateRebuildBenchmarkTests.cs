#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Xunit;

namespace Fuookami.Ospf.Test.Benchmark;

/// <summary>
/// **`TokenInvalidateRebuildBenchmark` 的前提与对照必须真的成立** /
/// The premise and the controls of `TokenInvalidateRebuildBenchmark` must actually hold.
/// </summary>
/// <remarks>
/// **这个基准测的是"失效后的重建成本"，而这件事有三条隐含前提，测错了不会报错、只会安静地量错东西：**
///
/// **① 探针真的能注册**（否则"增删"里只有删，索引根本没失效）；**② 三个方法在同一份结构上跑**
/// （否则对照组不可比）；**③ 返回值有实际含义**（`sum` 要等于各稳定 token 的索引和，
/// 全返回 `-1` 也"不报错"，但那说明每次读都失败）。
///
/// **它的三条前提都是行为，不是时间——所以能在这里钉住。**
///
/// **The benchmark measures "rebuild cost after invalidation", and that carries three unstated premises; getting one wrong produces no error,
/// only a silently wrong measurement.** **① The probe really registers** (otherwise "add and remove" is just removal and the index never
/// invalidates); **② all three methods run on the same structure** (otherwise the controls are not comparable); **③ the return value means
/// something** (`sum` must equal the sum of the stable tokens' indices; returning `-1` everywhere also "does not throw", but it means every
/// read failed).** **All three premises are behaviour, not timing, so they can be pinned here.**
/// </remarks>
public sealed class TokenInvalidateRebuildBenchmarkTests
{
    /// <summary>**三个方法给出的结果必须互相能对上** / The three methods must agree with each other.</summary>
    [Fact]
    public void TheThreeMethods_ShouldAgreeOnTheSameStructure() {
        var benchmark = new TokenInvalidateRebuildBenchmark { Dataset = "small", Cycles = 16 };
        benchmark.Setup();

        try {
            int warm = benchmark.WarmReadsOnly();
            int rebuilt = benchmark.InvalidateThenRebuild();
            int mutated = benchmark.MutationOnly();

            // **两个读方法覆盖同一批索引**：`WarmReadsOnly` 与 `InvalidateThenRebuild` 读的是同一串稳定 token，
            // **所以它们的和必须相等——这会同时证明"探针增删没有破坏已有位置"和"重建结果与稳态读一致"。**
            //
            // **Both read methods cover the same indices: they read the same stable tokens, so their sums must match, which proves both that
            // the probe's add/remove did not disturb existing positions and that the rebuilt answer agrees with the steady-state one.**
            rebuilt.Should().Be(
                warm,
                "invalidation must not change the answer; if it does, the benchmark is timing a wrong result");
            mutated.Should().Be(16, "the mutation control returns its cycle count, so a mismatch means it did not run them all");
            warm.Should().BeGreaterThan(0, "every read resolved a real index; a sum of zero means every lookup returned -1");
        }
        finally {
            benchmark.Cleanup();
        }
    }

    /// <summary>**三档规模的稳定 token 数量是 1024/2048/4096** / The three scales hold 1024/2048/4096 stable tokens.</summary>
    /// <remarks>
    /// **规模档位写错时，"每操作重建次数固定"这句话就不成立**，而报告里的数字仍然会出来——
    /// **判据把这三个数钉住，顺带证明 `Setup` 真的按档位建了结构。**
    ///
    /// **A wrong scale breaks the "fixed number of rebuilds per operation" claim while the report still prints numbers.** **This pins the
    /// three sizes and thereby proves `Setup` built the structure the dataset asks for.**
    /// </remarks>
    [Fact]
    public void TheThreeScales_ShouldHoldTheStatedTokenCounts() {
        foreach ((string dataset, int expected) in new[] { ("small", 1_024), ("medium", 2_048), ("large", 4_096) }) {
            var benchmark = new TokenInvalidateRebuildBenchmark { Dataset = dataset, Cycles = 16 };
            benchmark.Setup();
            try {
                benchmark.StableTokenCount.Should().Be(
                    expected,
                    dataset + " must hold exactly " + expected + " stable tokens");
            }
            finally {
                benchmark.Cleanup();
            }
        }
    }
}