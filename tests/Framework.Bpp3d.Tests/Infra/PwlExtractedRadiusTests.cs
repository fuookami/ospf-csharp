// PWL 半径提取的判据 / Criteria for extracting a radius from a PWL solve.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Infra;

/// <summary>
/// **近似 r² 与真实 r² 是两个数，体积必须说清用哪一个** / The approximate r² and the true r² are two numbers, and each volume must say which it uses.
/// </summary>
/// <remarks>
/// 弦在 `r²` 上方，所以求解器手里的 `q` **偏大**，体积跟着偏大。把两者混用会让"实际装了多少"与"模型以为装了多少"
/// 悄悄变成同一个数，于是一次过载永远查不出来。
///
/// 误差字段由提取时**现场算出**，不接受调用方传入——那个字段正是用来判断近似能不能信的。
///
/// The chord lies above `r²`, so the solver's `q` is **too large** and so is the volume. Mixing the two silently makes "what was actually packed" and "what the model
/// thought it packed" the same number, and an overload then becomes undetectable. The error fields are computed **on the spot** during extraction and are not accepted
/// from the caller — that field is precisely what is used to judge whether the approximation can be trusted.
/// </remarks>
public class PwlExtractedRadiusTests {
    /// **近似体积与真实体积不同，差值就是 PWL 误差** / The two volumes differ, and the difference is the PWL error.
    [Fact]
    public void Volumes_ShouldDifferByThePwlError() {
        PwlExtractedRadius extracted = Extracted(radius: 1.5);

        double actual = extracted.ActualVolume(Height(), Pi()).ToFlt64().ToDouble();
        double approximate = extracted.PwlVolume(Height(), Pi()).ToFlt64().ToDouble();

        actual.Should().BeApproximately(System.Math.PI * 1.5 * 1.5 * 10.0, 1e-9,
            "the actual volume uses the true r²");
        approximate.Should().BeGreaterThan(actual,
            "the chord lies above r², so the solver's q over-states the footprint");
        (approximate - actual).Should().BeApproximately(
            System.Math.PI * extracted.PwlAbsoluteError.ToFlt64().ToDouble() * 10.0, 1e-9,
            "the gap in volume is exactly pi times the r² error times the height");
    }

    /// **求解器 q 与真实 r² 分开保存** / The solver's q and the true r² are stored separately.
    [Fact]
    public void ExtractedRadius_ShouldKeepBothSquaredValues() {
        // 取 1.75 而不是 2.0：2.0 恰好是断点，弦在那里与 r² 相等，分不出"两个数"。
        // Use 1.75 rather than 2.0: 2.0 is exactly a breakpoint, where the chord equals r² and the two values cannot be told apart.
        PwlExtractedRadius extracted = Extracted(radius: 1.75);

        extracted.ActualRadiusSquared.ToFlt64().ToDouble().Should().BeApproximately(1.75 * 1.75, 1e-12);
        extracted.SolverRadiusSquared.ToFlt64().ToDouble()
            .Should().BeGreaterThan(1.75 * 1.75, "inside a segment the chord over-estimates the convex r²");
        extracted.SolverRadius.ToFlt64().ToDouble().Should().BeApproximately(1.75, 1e-12);
    }

    /// **误差由提取现场算出，相对误差分母用真实 r²** / The errors are computed during extraction, and the relative error divides by the true r².
    [Fact]
    public void ExtractedRadius_ShouldComputeTheErrorsItself() {
        PwlExtractedRadius extracted = Extracted(radius: 1.5);

        double trueSquared = 1.5 * 1.5;
        double expectedRelative = extracted.PwlAbsoluteError.ToFlt64().ToDouble() / trueSquared;

        extracted.PwlRelativeError.ToFlt64().ToDouble().Should().BeApproximately(expectedRelative, 1e-12);
        extracted.PwlAbsoluteError.ToFlt64().ToDouble().Should().BeGreaterThan(0.0);
    }

    /// **落在包络外要标记出来** / A radius outside the envelope is flagged.
    [Fact]
    public void ExtractedRadius_ShouldFlagARadiusOutsideTheEnvelope() {
        Extracted(radius: 1.5).IsWithinEnvelope.Should().BeTrue("1.5 is inside [1.0, 3.0]");
        Extracted(radius: 5.0).IsWithinEnvelope.Should().BeFalse("5.0 is beyond rMax");
    }

    /// **诊断信息同时给出两个 r²** / The diagnostics carry both squared values.
    [Fact]
    public void Info_ShouldExposeBothSquaredValues() {
        IReadOnlyDictionary<string, string> info = Extracted(radius: 1.5).Info();

        info.Should().ContainKey("pwl_radius_r_q");
        info.Should().ContainKey("pwl_radius_r_r_squared");
        info["pwl_radius_r_within_envelope"].Should().Be("True");
        info["pwl_radius_r_r"].Should().Be("1.5", "invariant formatting, not locale-dependent");
    }

    /// <summary>构造一个提取结果 / Build an extracted radius.</summary>
    /// <param name="radius">求解器半径 / The solver radius.</param>
    /// <returns>提取结果 / The extracted radius.</returns>
    private static PwlExtractedRadius Extracted(double radius) {
        var approximation = PwlRadiusSquaredApproximation.FromRadiusInterval(
            rMin: 1.0, rMax: 3.0,
            config: new PwlRadiusApproximationConfig(MaxSegments: 2));
        var envelope = new ConservativeRadiusEnvelope(Metres(1.0), Metres(3.0));
        var solverRadius = new FltX(radius);
        double solverSquared = approximation.Evaluate(radius);
        return PwlExtractedRadius.FromSolver(
            variableName: "r",
            solverRadius: solverRadius,
            solverRadiusSquared: new FltX(solverSquared),
            envelope: envelope,
            approximation: approximation);
    }

    /// <summary>固定高度 / A fixed height.</summary>
    /// <returns>高度 / The height.</returns>
    private static FltX Height() => new(10.0);

    /// <summary>圆周率 / Pi.</summary>
    /// <returns>π / Pi.</returns>
    private static FltX Pi() => new(System.Math.PI);

    /// <summary>构造米量 / Build a length in metres.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<FltX> Metres(double value) => new(new FltX(value), SIBaseUnits.Meter);
}
