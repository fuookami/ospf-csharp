#nullable enable

// IDE0007 与 IDE0008 会在同一个泛型局部上互相要求，而本文件两处都触发；按仓库既有做法文件级关闭并写理由。
// IDE0007 and IDE0008 demand opposite things on the same generic local and both fire here; disabled at file scope per repository practice.
#pragma warning disable IDE0007, IDE0008

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

    /// <summary>**多状态各自累加，互不串线** / Multiple states accumulate separately.</summary>
    /// <remarks>
    /// **两个状态用同一条切片、但走不同的运算：** **若实现只保留"一个状态"的形状，第二个状态会被第一个覆盖，'
    /// **而模型照常展开、照常求解——只是第二个状态的值其实是第一个。**
    ///
    /// **Two states share one slice but take different operations: an implementation that only really keeps one state lets the second overwrite the first, and the model still
    /// unrolls and still solves — the second state's value is simply the first's.**
    /// </remarks>
    [Fact]
    public void MultipleStates_ShouldAccumulateSeparately() {
        var context = new OnnxLoweringContext();

        // state_a += slice；state_b -= slice。三个切片 1、2、3。
        var body = new OnnxGraph(
            "body",
            new[] {
                Node("Add", "a_in", "slice_in", "a_out"),
                Node("Sub", "b_in", "slice_in", "b_out"),
            },
            Array.Empty<OnnxTensor>(),
            new[] { new OnnxValueInfo("a_in", 1, Array.Empty<OnnxDimension>()),
                    new OnnxValueInfo("b_in", 1, Array.Empty<OnnxDimension>()),
                    new OnnxValueInfo("slice_in", 1, Array.Empty<OnnxDimension>()) },
            new[] { new OnnxValueInfo("a_out", 1, Array.Empty<OnnxDimension>()),
                    new OnnxValueInfo("b_out", 1, Array.Empty<OnnxDimension>()) },
            Array.Empty<OnnxValueInfo>());

        OnnxScanLowering.Result result = OnnxScanLowering.Lower(
            new OnnxScanExpansion.ScanNode(body, 3, 3, 2, 2, 2, 2),
            context,
            new[] { Constant(0.0), Constant(10.0) },
            new[] { Constant(1.0), Constant(2.0), Constant(3.0) },
            new[] { "a_in", "b_in", "slice_in" },
            new[] { "final_a", "final_b" });

        result.IsOk.Should().BeTrue(result.Failure);
        result.FinalStates![0].Expression.Constant.ToDouble().Should().Be(6.0, "0 + 1 + 2 + 3");
        result.FinalStates![1].Expression.Constant.ToDouble().Should().Be(4.0,
            "10 - 1 - 2 - 3; the first state's 6 appearing here would mean the states were not kept apart");
    }

    /// <summary>**多状态的就地更新体也能展开** / A multi-state in-place body unrolls as well.</summary>
    [Fact]
    public void MultipleInPlaceStates_ShouldUnroll() {
        var context = new OnnxLoweringContext();

        var body = new OnnxGraph(
            "body",
            new[] {
                Node("Add", "a_in", "slice_in", "a_in"),
                Node("Sub", "b_in", "slice_in", "b_in"),
            },
            Array.Empty<OnnxTensor>(),
            new[] { new OnnxValueInfo("a_in", 1, Array.Empty<OnnxDimension>()),
                    new OnnxValueInfo("b_in", 1, Array.Empty<OnnxDimension>()),
                    new OnnxValueInfo("slice_in", 1, Array.Empty<OnnxDimension>()) },
            new[] { new OnnxValueInfo("a_in", 1, Array.Empty<OnnxDimension>()),
                    new OnnxValueInfo("b_in", 1, Array.Empty<OnnxDimension>()) },
            Array.Empty<OnnxValueInfo>());

        OnnxScanLowering.Result result = OnnxScanLowering.Lower(
            new OnnxScanExpansion.ScanNode(body, 2, 2, 2, 2, 2, 2),
            context,
            new[] { Constant(0.0), Constant(10.0) },
            new[] { Constant(1.0), Constant(2.0) },
            new[] { "a_in", "b_in", "slice_in" },
            new[] { "out_a", "out_b" });

        result.IsOk.Should().BeTrue(result.Failure);
        result.FinalStates![0].Expression.Constant.ToDouble().Should().Be(3.0, "0 + 1 + 2");
        result.FinalStates![1].Expression.Constant.ToDouble().Should().Be(7.0, "10 - 1 - 2");
    }

    /// <summary>**切片可以是符号，不是只有常数** / Slices may be symbolic, not only constants.</summary>
    /// <remarks>
    /// **这一条证明的是契约本身已经支持"运行期值"：** **每一轮的切片只是一个绑定，而绑定里装的可以是含变量的表达式。** '
    /// **所以 `Scan` 缺的不是"能不能带变量"，而是"导入期怎么把一个多元素张量切成每轮一片"——** '
    /// **那需要逐元素表示（基线的 `sliceLeading`），是清单里同一项。**
    ///
    /// **This proves the contract already accepts runtime values: a round's slice is just a binding, and a binding may carry an expression with variables.** **So what Scan lacks is
    /// not "can it hold a variable" but "how does the import time cut a multi-element tensor into one slice per round" — that needs the per-element representation (the
    /// baseline's sliceLeading), which is the same checklist item.**
    /// </remarks>
    [Fact]
    public void SymbolicSlices_ShouldCarryTheirCoefficients() {
        var context = new OnnxLoweringContext();

        // 三片分别是 x、2x、3x：两轮之后状态 = 初值 + x + 2x = x + 3x 的一半……严格算：
        // 第一轮 state = 0 + 1x = x；第二轮 state = x + 2x = 3x。
        // The three slices are x, 2x and 3x: after two rounds the state is x, then 3x.
        OnnxScanLowering.Result result = OnnxScanLowering.Lower(
            Node(length: 2, body: Body(new[] { Node("Add", "state_in", "slice_in", "state_out") }, "state_out")),
            context,
            new[] { Constant(0.0) },
            new[] { Symbolic(1.0), Symbolic(2.0) },
            new[] { "state_in", "slice_in" },
            new[] { "final" });

        result.IsOk.Should().BeTrue(result.Failure);
        result.FinalStates![0].Expression.Monomials.Should().HaveCount(1, "the result is a multiple of x, not a constant");
        result.FinalStates[0].Expression.Monomials[0].Coefficient.ToDouble().Should().Be(3.0,
            "0 + x is x, then x + 2x is 3x; a contract that only accepted constants could not produce this");
    }

    private static OnnxLoweringContext.Binding Symbolic(double coefficient) {
        var x = new RealVar("x");
        return new OnnxLoweringContext.Binding(
            new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), x) }, Flt64.Zero),
            Array.Empty<int>());
    }

    /// <summary>**非常量扫描输入真的逐轮切片了** / A non-constant scan input really is sliced round by round.</summary>
    /// <remarks>
    /// **这一条是"运行期切片"与"常量数组切片"的分水岭：** **扫描输入是三片 `x0`、`x1`、`x2`（<b>表达式**，不是数），**
    /// **长度取它的首维，第 `i` 片喂给第 `i` 轮。** **结果 `x0 + x1 + x2` 只能由"切的是表达式"得来——** '
    /// **若还按常量数组处理，这里连输入都没有。**
    ///
    /// **This is the dividing line between runtime slicing and slicing a constant array: the scan input is three slices x0, x1 and x2 — expressions, not numbers — the length is its
    /// leading dimension and slice i feeds round i.** **The result x0 + x1 + x2 can only come from cutting expressions; a constant-array path would not even have an input here.**
    /// </remarks>
    [Fact]
    public void ARuntimeScanInput_ShouldBeSlicedPerRound() {
        var context = new OnnxLoweringContext();
        var scanInput = OnnxElementList.Create(new[] { 3 }, Labels("x0", "x1", "x2")).Value!;

        OnnxScanLowering.Result result = OnnxScanLowering.LowerWithElements(
            Node(length: 3, body: Body(new[] { Node("Add", "state_in", "slice_in", "state_out") }, "state_out")),
            context,
            new[] { Constant(0.0) },
            scanInput,
            new[] { "state_in", "slice_in" },
            new[] { "final" });

        result.IsOk.Should().BeTrue(result.Failure);
        result.FinalStates![0].Expression.Monomials.Should().HaveCount(3, "x0, x1 and x2 all survive with coefficient one");
        foreach (string name in new[] { "x0", "x1", "x2" }) {
            result.FinalStates[0].Expression.Monomials.Should().Contain(
                monomial => monomial.Symbol.Name == name && monomial.Coefficient.ToDouble() == 1.0,
                name + " has to arrive exactly once");
        }
    }

    /// <summary>**首维与声明的扫描长度不符时被拒绝** / A leading dimension that disagrees with the declared length is refused.</summary>
    /// <remarks>
    /// **少切一轮会漏掉一片、多切一轮会少一片，两者都不报错——** **只是循环跑的次数与模型声明的不同。**
    ///
    /// **One round short drops a slice and one round long reads past the end, and neither raises — the loop simply runs a different number of times than the model declares.**
    /// </remarks>
    [Fact]
    public void ALengthMismatch_ShouldBeRefused() {
        var scanInput = OnnxElementList.Create(new[] { 2 }, Labels("x0", "x1")).Value!;

        OnnxScanLowering.Result result = OnnxScanLowering.LowerWithElements(
            Node(length: 3, body: Body(new[] { Node("Identity", "slice_in", "state_out") }, "state_out")),
            new OnnxLoweringContext(),
            new[] { Constant(0.0) },
            scanInput,
            new[] { "state_in", "slice_in" },
            new[] { "final" });

        result.IsOk.Should().BeFalse("two slices cannot cover three declared rounds");
        result.Failure.Should().Contain("3").And.Contain("2", "both numbers have to appear");
    }

    /// <summary>**多元素切片被明确拒绝，而不是塞给循环体** / A multi-element slice is refused rather than handed to the body.</summary>
    [Fact]
    public void AMultiElementSlice_ShouldBeRefused() {
        var scanInput = OnnxElementList.Create(new[] { 2, 3 }, Labels("a", "b", "c", "d", "e", "f")).Value!;

        OnnxScanLowering.Result result = OnnxScanLowering.LowerWithElements(
            Node(length: 2, body: Body(new[] { Node("Identity", "slice_in", "state_out") }, "state_out")),
            new OnnxLoweringContext(),
            new[] { Constant(0.0) },
            scanInput,
            new[] { "state_in", "slice_in" },
            new[] { "final" });

        result.IsOk.Should().BeFalse("each slice holds three elements while the body takes one input");
        result.Failure.Should().Contain("元素");
    }

    private static IReadOnlyList<LinearPolynomial<Flt64>> Labels(params string[] names) {
        var elements = new List<LinearPolynomial<Flt64>>(names.Length);
        foreach (string name in names) {
            elements.Add(new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, new RealVar(name)) },
                Flt64.Zero));
        }
        return elements;
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