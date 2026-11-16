#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Graph;

/// <summary>
/// **半精度：坑全在次正规数与无穷/NaN 这两条路径上** / Half precision: the traps live on the subnormal and infinity/NaN paths.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxHalfPrecision`。**FP16 常量几乎总是权重与初始值，读错一个不会报错——** '
/// **只是模型能跑但偏差一点点，而那是很难归因的一类错。**
///
/// Syncs `OnnxHalfPrecision`. **FP16 constants are almost always weights and initialisers, and getting one wrong does not raise — the model runs slightly off, which is hard
/// to attribute.**
/// </remarks>
public class OnnxHalfPrecisionTests
{
    /// <summary>**常见值逐一对得上** / The ordinary values line up.</summary>
    [Theory]
    [InlineData(0x0000, 0.0)]
    [InlineData(0x3C00, 1.0)]
    [InlineData(0x4000, 2.0)]
    [InlineData(0x3800, 0.5)]
    [InlineData(0xC000, -2.0)]
    [InlineData(0x4900, 10.0)]
    public void TheOrdinaryValues_ShouldMatch(ushort bits, double expected) =>
        OnnxHalfPrecision.ToDouble(bits).Should().Be(expected);

    /// <summary>**次正规数不是零** / Subnormals are not zero.</summary>
    /// <remarks>
    /// **这是最容易写错的一条：** **把指数为 0 一律当零，会让最小的那些权重全部变成 0，** **而模型照常求解——**
    /// **只是那些参数被抹平了，解与原本的网络无关。**
    ///
    /// **This is the easiest thing to get wrong: treating a zero exponent as zero wipes out the smallest weights, and the model still solves — those parameters are simply
    /// flattened and the answer has nothing to do with the intended network.**
    /// </remarks>
    [Fact]
    public void TheSubnormals_ShouldNotBeZero() {
        OnnxHalfPrecision.ToDouble((ushort)0x0001).Should().BeApproximately(System.Math.Pow(2.0, -24), 1e-30);
        OnnxHalfPrecision.ToDouble((ushort)0x03FF).Should().BeGreaterThan(0.0, "the largest subnormal is still positive");
        OnnxHalfPrecision.ToDouble((ushort)0x03FF).Should().BeLessThan(System.Math.Pow(2.0, -14),
            "and it is still below the smallest normal");
        OnnxHalfPrecision.ToDouble((ushort)0x8001).Should().BeApproximately(-System.Math.Pow(2.0, -24), 1e-30);
    }

    /// <summary>**无穷与 NaN 分开处理** / Infinity and NaN are distinguished.</summary>
    /// <remarks>
    /// **把 NaN 读成无穷会让后续比较"看起来正常"（`x != NaN` 与 `x != ∞` 都成立），** **而把无穷读成 NaN 会让一个本可求解的界变成不可比较。**
    ///
    /// **Reading NaN as infinity makes later comparisons look fine (both `x != NaN` and `x != ∞` hold), while reading infinity as NaN turns a usable bound into something
    /// incomparable.**
    /// </remarks>
    [Fact]
    public void InfinityAndNaN_ShouldBeDistinguished() {
        double.IsPositiveInfinity(OnnxHalfPrecision.ToDouble((ushort)0x7C00)).Should().BeTrue();
        double.IsNegativeInfinity(OnnxHalfPrecision.ToDouble((ushort)0xFC00)).Should().BeTrue();
        double.IsNaN(OnnxHalfPrecision.ToDouble((ushort)0x7C01)).Should().BeTrue("a non-zero mantissa with a full exponent is NaN, not infinity");
        double.IsNaN(OnnxHalfPrecision.ToDouble((ushort)0x7FFF)).Should().BeTrue();
    }

    /// <summary>**负数与零的符号位生效** / The sign bit works for negative zero and negatives.</summary>
    [Fact]
    public void TheSignBit_ShouldApply() {
        OnnxHalfPrecision.ToDouble((ushort)0x8000).Should().Be(0.0);
        double.IsNegative(OnnxHalfPrecision.ToDouble((ushort)0x8000)).Should().BeTrue("negative zero is still negative");
        OnnxHalfPrecision.ToDouble((ushort)0x3C00).Should().Be(1.0);
        OnnxHalfPrecision.ToDouble((ushort)0xBC00).Should().Be(-1.0);
    }

    /// <summary>**有符号读法与无符号读法结果一致** / The signed and unsigned readings agree.</summary>
    [Fact]
    public void TheSignedOverload_ShouldAgree() {
        OnnxHalfPrecision.ToDouble((short)unchecked((short)0xBC00)).Should().Be(OnnxHalfPrecision.ToDouble((ushort)0xBC00));
        OnnxHalfPrecision.ToDouble((short)0x3C00).Should().Be(1.0);
    }

    /// <summary>**最大正规值就是 65504** / The largest normal is exactly 65504.</summary>
    [Fact]
    public void TheLargestNormal_ShouldBe65504() =>
        OnnxHalfPrecision.ToDouble((ushort)0x7BFF).Should().Be(65504.0);
}