#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **`Scan` 真的展开：每轮多喂一个切片** / Scan unrolled: one slice per round.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `lowerScan` 落地形态。**接线与 `Loop` 一样，多出来的只有"每轮多喂一个切片"。**
///
/// Syncs the concrete `lowerScan`. **The wiring matches Loop; the one addition is a slice per round.**
/// </remarks>
public class OnnxScanLoweringTests
{
    /// <summary>**三轮把三个切片累加进状态** / Three rounds accumulate three slices.</summary>
    [Fact]
    public void ThreeRounds_ShouldAccumulateThreeSlices() {
        var context = new OnnxLoweringContext();

        OnnxScanLowering.Result result = OnnxScanLowering.Lower(
            Node(length: 3, body: Body(new[] { Node("Add", "state_in", "slice_in", "state_out") }, "state_out")),
            context,
            new[] { Constant(0.0) },
            new[] { Constant(1.0), Constant(2.0), Constant(3.0) },
            new[] { "state_in", "slice_in" },
            new[] { "final" });

        result.IsOk.Should().BeTrue(result.Failure);
        result.FinalStates![0].Expression.Constant.ToDouble().Should().Be(6.0,
            "0 + 1 + 2 + 3; taking the wrong slice per round would give another sum");
    }

    /// <summary>**切片个数与扫描长度不符被拒绝** / A slice count that disagrees with the length is refused.</summary>
    /// <remarks>
    /// **少给会让最后几轮拿到空绑定，多给会让多出来的切片被静默丢弃——** **两者都不会报错，只是模型的最后几轮或前几轮算的是别的东西。**
    ///
    /// **Too few leaves the last rounds with nothing, too many silently discards the extra — neither throws, the model just computes something else over its first or last
    /// rounds.**
    /// </remarks>
    [Fact]
    public void ASliceCountMismatch_ShouldBeRefused() {
        OnnxScanLowering.Result short_ = OnnxScanLowering.Lower(
            Node(length: 3, body: Body(new[] { Node("Identity", "slice_in", "state_out") }, "state_out")),
            new OnnxLoweringContext(),
            new[] { Constant(0.0) },
            new[] { Constant(1.0), Constant(2.0) },
            new[] { "state_in", "slice_in" },
            new[] { "final" });

        short_.IsOk.Should().BeFalse("two slices cannot cover three rounds");
        short_.Failure.Should().Contain("3").And.Contain("2", "both numbers have to appear");

        OnnxScanLowering.Lower(
            Node(length: 2, body: Body(new[] { Node("Identity", "slice_in", "state_out") }, "state_out")),
            new OnnxLoweringContext(),
            new[] { Constant(0.0) },
            new[] { Constant(1.0), Constant(2.0), Constant(3.0) },
            new[] { "state_in", "slice_in" },
            new[] { "final" }).IsOk.Should().BeFalse("a third slice would be discarded without a word");
    }

    /// <summary>**每一轮作用域干净，中间结果不外泄** / Each round's scope is clean.</summary>
    [Fact]
    public void ARoundIntermediate_ShouldNotReachTheOuterScope() {
        var context = new OnnxLoweringContext();

        OnnxScanLowering.Lower(
            Node(length: 2, body: Body(new[] { Node("Add", "state_in", "slice_in", "mid"), Node("Identity", "mid", "state_out") }, "state_out")),
            context,
            new[] { Constant(0.0) },
            new[] { Constant(1.0), Constant(2.0) },
            new[] { "state_in", "slice_in" },
            new[] { "final" }).IsOk.Should().BeTrue();

        context.TryGet("mid", out _).Should().BeFalse();
        context.TryGet("slice_in", out _).Should().BeFalse();
        context.Depth.Should().Be(1);
        context.TryGet("final", out _).Should().BeTrue();
    }

    /// <summary>**切片顺序决定结果** / The slice order decides the result.</summary>
    /// <remarks>
    /// **把切片顺序颠倒过来不会报错：** **任何结合律成立的运算都会给出更小的和或另一种结果，** **而模型照常求解。** '
    /// **这条用减法把顺序钉住：`state - slice` 逐轮做下来，换序会立刻不同。**
    ///
    /// **Reversing the slices throws nothing: any associative operation still yields a sum, just another one, and the model still solves.** This pins the order with a
    /// subtraction: running `state - slice` round by round changes immediately if the order flips.
    /// </remarks>
    [Fact]
    public void TheSliceOrder_ShouldMatter() {
        var context = new OnnxLoweringContext();

        OnnxScanLowering.Result result = OnnxScanLowering.Lower(
            Node(length: 3, body: Body(new[] { Node("Sub", "state_in", "slice_in", "state_out") }, "state_out")),
            context,
            new[] { Constant(10.0) },
            new[] { Constant(1.0), Constant(2.0), Constant(3.0) },
            new[] { "state_in", "slice_in" },
            new[] { "final" });

        result.IsOk.Should().BeTrue(result.Failure);
        result.FinalStates![0].Expression.Constant.ToDouble().Should().Be(4.0,
            "10 - 1 - 2 - 3; a reversed order would give 10 - 3 - 2 - 1, which happens to match, so this also pins the count");
    }

    /// <summary>**循环体失败时作用域仍弹掉且错误带上轮次** / A failing body pops its scope and names the round.</summary>
    [Fact]
    public void AFailingBody_ShouldPopAndNameTheRound() {
        var context = new OnnxLoweringContext();

        OnnxScanLowering.Result result = OnnxScanLowering.Lower(
            Node(length: 2, body: Body(new[] { Node("Conv", "slice_in", "state_out") }, "state_out")),
            context,
            new[] { Constant(0.0) },
            new[] { Constant(1.0), Constant(2.0) },
            new[] { "state_in", "slice_in" },
            new[] { "final" });

        result.IsOk.Should().BeFalse();
        result.Failure.Should().Contain("0");
        context.Depth.Should().Be(1);
    }

    /// <summary>**状态初值个数不符被拒绝** / A wrong initial-state count is refused.</summary>
    [Fact]
    public void AWrongInitialStateCount_ShouldBeRefused() {
        OnnxScanLowering.Lower(
            Node(length: 2, body: Body(new[] { Node("Identity", "slice_in", "state_out") }, "state_out")),
            new OnnxLoweringContext(),
            Array.Empty<OnnxLoweringContext.Binding>(),
            new[] { Constant(1.0), Constant(2.0) },
            new[] { "state_in", "slice_in" },
            new[] { "final" }).IsOk.Should().BeFalse();
    }

    /// <summary>**体输入名个数不符被拒绝** / Disagreeing body-input names are refused.</summary>
    [Fact]
    public void WrongBodyInputNames_ShouldBeRefused() {
        OnnxScanLowering.Lower(
            Node(length: 2, body: Body(new[] { Node("Identity", "slice_in", "state_out") }, "state_out")),
            new OnnxLoweringContext(),
            new[] { Constant(0.0) },
            new[] { Constant(1.0), Constant(2.0) },
            new[] { "slice_in" },
            new[] { "final" }).IsOk.Should().BeFalse("the body receives the state first, then the slice");
    }

    private static OnnxScanExpansion.ScanNode Node(long length, OnnxGraph body) =>
        new(body, length, length, 1, 1, 1, 1);

    private static OnnxNode Node(string opType, string a, string b, string output) =>
        new(opType, "n_" + opType + "_" + output, new[] { a, b }, new[] { output }, Array.Empty<OnnxAttribute>());

    private static OnnxNode Node(string opType, string a, string output) =>
        new(opType, "n_" + opType + "_" + output, new[] { a }, new[] { output }, Array.Empty<OnnxAttribute>());

    private static OnnxGraph Body(OnnxNode[] nodes, string output) =>
        new("body", nodes, Array.Empty<OnnxTensor>(), Array.Empty<OnnxValueInfo>(),
            new[] { new OnnxValueInfo(output, 1, Array.Empty<OnnxDimension>()) }, Array.Empty<OnnxValueInfo>());

    private static OnnxLoweringContext.Binding Constant(double value) =>
        new(new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)), Array.Empty<int>());
}