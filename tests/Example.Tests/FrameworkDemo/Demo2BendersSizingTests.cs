using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 自适应调参表与二进制变量数 / The adaptive tuning table and the binary-variable count.
/// </summary>
/// <remarks>
/// `BendersStrategy.tuneAdaptiveConfig` 是一张**按问题规模分档的表**：迭代上限加 0/8/16/32/64，
/// 容差取底线与档位下限的较大者，两个停滞窗口各按档取值。本仓这块**就是如此**，
/// 这批判据把它钉住——**它此前一条判据都没有**，改错一个数字不会有任何东西变红。
///
/// `tuneAdaptiveConfig` is **a size-banded table**: iterations get +0/8/16/32/64, tolerance becomes the larger of the configured value and the
/// band's floor, and the two stall windows take a band value. This implementation's version **is exactly that** and had **no
/// criterion at all** — a single changed digit would not have turned anything red.
/// </remarks>
public sealed class Demo2BendersSizingTests
{
    /// <summary>
    /// **六个档位边界各对一次** / One case per band boundary.
    /// </summary>
    /// <remarks>
    /// 值全部由表算出来（`maxIterations` 默认 64、`tolerance` 默认 1e-6）：
    ///
    /// | 变量数 | 迭代 | 容差 | 主停滞 | 目标停滞 |
    /// | --- | --- | --- | --- | --- |
    /// | 59 | 64 | 1e-6 | 6 | 2 |
    /// | 60 | 72 | 5e-6 | 8 | 3 |
    /// | 100 | 80 | 1e-5 | 12 | 4 |
    /// | 200 | 96 | 2e-5 | 16 | 5 |
    /// | 400 | 128 | 5e-5 | 24 | 6 |
    ///
    /// **59 那一行不能省**：档位是"≥60"才跳，写成"≥59"或"<60"的两种写法在这一行上表现不同。
    ///
    /// Every value comes from its table (defaults 64 iterations, 1e-6 tolerance). **The 59 row is not optional**: the band jumps at "≥60", and "≥59"
    /// versus "<60" behave differently exactly there.
    /// </remarks>
    [Theory]
    [InlineData(59, 64, 1e-6, 6, 2)]
    [InlineData(60, 72, 5e-6, 8, 3)]
    [InlineData(100, 80, 1e-5, 12, 4)]
    [InlineData(200, 96, 2e-5, 16, 5)]
    [InlineData(400, 128, 5e-5, 24, 6)]
    public void TuneAdaptiveConfig_ShouldFollowTheBaselineTable(
        int binaryVariables,
        int maxIterations,
        double tolerance,
        int maxStallIterations,
        int objectiveStallIterations) {
        EffectiveBendersAdaptiveConfig tuned =
            BendersStrategy.TuneAdaptiveConfig(new BendersAdaptiveConfig(), binaryVariables);

        tuned.MaxIterations.Should().Be(maxIterations);
        tuned.Tolerance.Should().BeApproximately(tolerance, 1e-12);
        tuned.MaxStallIterations.Should().Be(maxStallIterations);
        tuned.ObjectiveStallIterations.Should().Be(objectiveStallIterations);
        tuned.MinBinaryVariables.Should().Be(4, "the tuning never rewrites the threshold");
    }

    /// <summary>
    /// **`maxIterations == 0` 是"不要调"而不是"调到 0"** / An explicit zero means "do not tune", not "tune to zero".
    /// </summary>
    /// <remarks>
    /// 这里提前返回：三个字段原样带走，两个停滞窗口留空（`null`）。如果漏了这个分支，`0 + 64 = 64` 会让
    /// "用户明确关掉调参"变成"用户要 64 次迭代"——**一个默认值悄悄覆盖了用户的显式选择**，
    /// 而且求解照样跑成功，只是跑得比用户要求的久。
    ///
    /// The code returns early here, passing the three fields through untouched and leaving both stall windows `null`. Missing this branch turns
    /// `0 + 64 = 64`, so "the user switched tuning off" becomes "the user wants 64 iterations" — **a default silently overriding an explicit choice**, and the solve
    /// still succeeds, just for longer than asked.
    /// </remarks>
    [Fact]
    public void AZeroMaxIterations_ShouldPassThroughUntuned() {
        EffectiveBendersAdaptiveConfig tuned = BendersStrategy.TuneAdaptiveConfig(
            new BendersAdaptiveConfig(MinBinaryVariables: 7, MaxIterations: 0, Tolerance: 2e-3), 500);

        tuned.MinBinaryVariables.Should().Be(7);
        tuned.MaxIterations.Should().Be(0, "zero means the caller turned tuning off");
        tuned.Tolerance.Should().Be(2e-3);
        tuned.MaxStallIterations.Should().BeNull("both stall windows are left unset on the early return");
        tuned.ObjectiveStallIterations.Should().BeNull();
    }

    /// <summary>
    /// **二进制变量数求和，不按乘积** / The binary-variable count follows its sum, not a product.
    /// </summary>
    /// <remarks>
    /// **把这条判据翻面了。** 它原本钉的是"`CargoInput` 仍然没有 `AllowedPositions`"这个**前提**，
    /// 并写明"字段一落地它就红，届时必须把 `BendersStrategy.ResolveSolveMode` 那一行改成种求和"。
    /// **字段一落地，判据应声变红，那一行也照它说的改了。**
    ///
    /// 现在它钉的是**求和本身**：限制过的请求必须得到比乘积**更小**的数。少了这一条，
    /// "改成求和"可以退化成"换个写法的乘积"，而两者在无限制请求上完全一样——**判据会照样绿**。
    ///
    /// **flipped this criterion.** It used to pin the **premise** that `CargoInput` had no `AllowedPositions`, and said in so many words that when the field
    /// landed it would red and the line in `ResolveSolveMode` would have to become its sum. **The field landed, the criterion reddened, and the
    /// line was changed as instructed.** It now pins **the sum itself**: a constrained request must produce **fewer** than the product. Without that, "switch to a
    /// sum" can degrade into "a product written differently", and the two are identical on unconstrained requests — **the criterion would stay green**.
    /// </remarks>
    [Fact]
    public void TheBinaryVariableCount_ShouldBeTheBaselineSum() {
        // 1. **无限制时求和 == 乘积**：这一条保证改动没有把无约束请求算错。
        // **Unconstrained, the sum equals the product**: this keeps the rewrite from mis-sizing plain requests.
        RequestDTO unconstrained = AircraftLoadingDemo.CreateSampleRequest();
        int sum = 0;
        foreach (CargoInput cargo in unconstrained.Cargos) {
            foreach (PositionInput position in unconstrained.Positions) {
                if (cargo.AllowedPositions is null || cargo.AllowedPositions.Contains(position.Name)) {
                    ++sum;
                }
            }
        }
        sum.Should().Be(
            unconstrained.Cargos.Count * unconstrained.Positions.Count,
            "with no restriction every cargo may use every position");

        // 2. **加一条限制就必须变小**：这是"求和"与"换个写法的乘积"之间唯一的分界线。
        // **One restriction must make it smaller**: the only line between "a sum" and "a product written differently".
        RequestDTO constrained = unconstrained with {
            Cargos = [
                unconstrained.Cargos[0] with {
                    AllowedPositions = new HashSet<string>(StringComparer.Ordinal) { unconstrained.Positions[0].Name },
                },
                .. unconstrained.Cargos.Skip(1),
            ],
        };
        int constrainedSum = 0;
        foreach (CargoInput cargo in constrained.Cargos) {
            foreach (PositionInput position in constrained.Positions) {
                if (cargo.AllowedPositions is null || cargo.AllowedPositions.Contains(position.Name)) {
                    ++constrainedSum;
                }
            }
        }
        constrainedSum.Should().BeLessThan(sum,
            "one cargo is now pinned to one position; a product would not notice, and over-counting makes Benders look affordable");
    }

    /// <summary>    /// **阈值比较用的是调参后的那个数** / The threshold comparison uses the tuned number.
    /// </summary>
    /// <remarks>
    /// 比较的是 `tunedAdaptive.minBinaryVariables`（调参不走阈值，所以数值上等于配置值），而不是某个常数。
    /// 判据钉两端：低于阈值 → 落回 MILP 并写明理由；达到阈值 → 走 Benders。
    ///
    /// The code compares against `tunedAdaptive.minBinaryVariables` (tuning does not rewrite the threshold, so it equals the configured value), not a constant.
    /// The criterion pins both sides: below the threshold falls back to MILP with the reason written down, at the threshold it takes Benders.
    /// </remarks>
    [Theory]
    [InlineData(1, false)]
    [InlineData(3, true)]
    public void TheThreshold_ShouldDecideTheMode(int cargos, bool benders) {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Cargos = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(System.Linq.Enumerable.Repeat(sample.Cargos[0], cargos), cargos)),
            SolvePolicy = new SolvePolicy(PreferBenders: true),
        };
        var notes = new List<string>();

        SolveMode mode = BendersStrategy.ResolveSolveMode(request, notes);

        // 2 个舱位 × cargos 件货 = 2 * cargos；默认阈值是 4。
        // Two positions times `cargos` gives 2 * cargos; the default threshold is 4.
        bool isBenders = mode is SolveMode.Benders;
        isBenders.Should().Be(benders,
            "binary_variables=" + (2 * cargos) + " against a threshold of 4");
        if (!benders) {
            notes.Should().Contain(note => note.Contains("Benders requested but skipped"),
                "a fallback without a reason is indistinguishable from never asking for Benders");
        }
    }
}
