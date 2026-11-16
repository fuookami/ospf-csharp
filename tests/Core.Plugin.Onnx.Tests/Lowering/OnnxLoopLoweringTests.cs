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
/// **`Loop` 真的展开：同一批接线多一层种子回写** / Loop unrolled: the same wiring plus one write-back.
/// </summary>
/// <remarks>
/// `lowerLoop` 落地形态。**`If` 只跑一次体，`Loop` 跑 `trip` 次，每一轮的输入是上一轮的输出——** '
/// **这就是它与 `If` 的全部差别。**
///
/// Syncs the concrete `lowerLoop`. **If runs the body once; Loop runs it `trip` times with each round's inputs being the previous round's outputs — that is the whole
/// difference.**
/// </remarks>
public class OnnxLoopLoweringTests
{
    /// <summary>**两轮把常数累加两次** / Two rounds accumulate the constant twice.</summary>
    [Fact]
    public void TwoRounds_ShouldAccumulateTwice() {
        var context = new OnnxLoweringContext();
        OnnxLoweringContext.Binding start = Constant(5.0);

        OnnxLoopLowering.Result result = OnnxLoopLowering.Lower(
            Node(trip: 2, body: Body(new[] { Node("Add", "acc_in", "one", "acc_out") }, "acc_out")),
            context,
            new[] { start },
            new[] { "iter", "acc_in" },
            new[] { "final" },
            new Dictionary<string, double> { ["one"] = 1.0 });

        result.IsOk.Should().BeTrue(result.Failure);
        result.FinalStates![0].Expression.Constant.ToDouble().Should().Be(7.0,
            "5 becomes 6 after the first round and 7 after the second; a single application would give 6");
    }

    /// <summary>**零轮时携带值原样传出** / Zero rounds pass the initial value through.</summary>
    [Fact]
    public void ZeroRounds_ShouldPassTheInitialStateThrough() {
        var context = new OnnxLoweringContext();

        OnnxLoopLowering.Result result = OnnxLoopLowering.Lower(
            Node(trip: 0, body: Body(new[] { Node("Add", "acc_in", "one", "acc_out") }, "acc_out")),
            context,
            new[] { Constant(5.0) },
            new[] { "iter", "acc_in" },
            new[] { "final" },
            new Dictionary<string, double> { ["one"] = 1.0 });

        result.IsOk.Should().BeTrue(result.Failure);
        result.FinalStates![0].Expression.Constant.ToDouble().Should().Be(5.0, "no round means the loop changes nothing");
    }

    /// <summary>**每一轮的作用域是干净的：上一轮的中间结果不会漏到下一轮** / Each round's scope is clean.</summary>
    /// <remarks>
    /// **复用作用域不会报错——** **只是某一项用了上一轮的值，而它看起来像一条正常的递推。** '
    /// **这条判据的做法是：让循环体产出一个中间名，再检查它在循环结束后不在外层。**
    ///
    /// **Reusing a scope throws nothing — some term simply uses the previous round's value while looking like a legitimate recurrence.** The criterion lets the body
    /// produce an intermediate and checks it is absent from the outer scope afterwards.
    /// </remarks>
    [Fact]
    public void ARoundIntermediate_ShouldNotReachTheOuterScope() {
        var context = new OnnxLoweringContext();

        OnnxLoopLowering.Lower(
            Node(trip: 2, body: Body(new[] { Node("Add", "acc_in", "one", "mid"), Node("Identity", "mid", "acc_out") }, "acc_out")),
            context,
            new[] { Constant(0.0) },
            new[] { "iter", "acc_in" },
            new[] { "final" },
            new Dictionary<string, double> { ["one"] = 1.0 }).IsOk.Should().BeTrue();

        context.TryGet("mid", out _).Should().BeFalse("the intermediate belonged to a round and left with it");
        context.TryGet("iter", out _).Should().BeFalse("the iteration variable is per-round as well");
        context.Depth.Should().Be(1, "every round's scope was popped");
        context.TryGet("final", out _).Should().BeTrue("only the declared output belongs to the outer scope");
    }

    /// <summary>**迭代变量在每一轮里可见且是当轮的值** / The iteration variable is visible per round and carries the round's index.</summary>
    /// <remarks>
    /// **`iter` 必须是当轮的常数：** **若它一直是 0，循环体会把每一轮算成第一轮，** **而模型照常展开、照常求解——**
    /// **只是所有轮次贡献的是同一个值。**
    ///
    /// **`iter` has to be the round's own constant: were it always 0 the body would compute the first round every time, and the model still unrolls and still solves — every
    /// round just contributes the same value.**
    /// </remarks>
    [Fact]
    public void TheIterationVariable_ShouldCarryTheRoundIndex() {
        var context = new OnnxLoweringContext();

        // acc 每轮加上 iter：0 + 1 + 2 = 3，若 iter 恒为 0 则结果是 0。
        OnnxLoopLowering.Result result = OnnxLoopLowering.Lower(
            Node(trip: 3, body: Body(new[] { Node("Add", "acc_in", "iter", "acc_out") }, "acc_out")),
            context,
            new[] { Constant(0.0) },
            new[] { "iter", "acc_in" },
            new[] { "final" });

        result.IsOk.Should().BeTrue(result.Failure);
        result.FinalStates![0].Expression.Constant.ToDouble().Should().Be(3.0,
            "0 + 1 + 2; if iter never advanced the sum would stay 0");
    }

    /// <summary>**循环体失败时作用域仍被弹掉，且错误带上轮次** / A failing body still pops its scope, and the error names the round.</summary>
    [Fact]
    public void AFailingBody_ShouldPopAndNameTheRound() {
        var context = new OnnxLoweringContext();

        OnnxLoopLowering.Result result = OnnxLoopLowering.Lower(
            Node(trip: 3, body: Body(new[] { Node("Conv", "acc_in", "acc_out") }, "acc_out")),
            context,
            new[] { Constant(0.0) },
            new[] { "iter", "acc_in" },
            new[] { "final" });

        result.IsOk.Should().BeFalse("Conv is not part of node-by-node evaluation");
        result.Failure.Should().Contain("0", "the round index is what tells the reader how far it got");
        context.Depth.Should().Be(1);
    }

    /// <summary>**初值与声明的携带值个数不符被拒绝** / An initial count that disagrees with the declaration is refused.</summary>
    [Fact]
    public void AWrongInitialCount_ShouldBeRefused() {
        OnnxLoopLowering.Lower(
            Node(trip: 2, body: Body(new[] { Node("Identity", "acc_in", "acc_out") }, "acc_out")),
            new OnnxLoweringContext(),
            Array.Empty<OnnxLoweringContext.Binding>(),
            new[] { "iter", "acc_in" },
            new[] { "final" }).IsOk.Should().BeFalse("the loop declares one carried value and receives none");
    }

    /// <summary>**体输入名个数不符被拒绝** / Disagreeing body-input names are refused.</summary>
    [Fact]
    public void WrongBodyInputNames_ShouldBeRefused() {
        OnnxLoopLowering.Lower(
            Node(trip: 2, body: Body(new[] { Node("Identity", "acc_in", "acc_out") }, "acc_out")),
            new OnnxLoweringContext(),
            new[] { Constant(0.0) },
            new[] { "acc" },
            new[] { "final" }).IsOk.Should().BeFalse("the body needs the iteration variable as its first input");
    }

    /// <summary>**就地更新的循环体也能展开** / An in-place body unrolls too.</summary>
    /// <remarks>
    /// **这是 `TryRebind` 存在的理由：** **ONNX 允许循环体把结果写回输入名，** **而早先的实现会以"同层重名"拒绝它——'
    /// **拒绝本身不算错，错在把一个合法模型判成非法。** 判据用两轮累加钉住结果，确保"能展开"与"算对了"是同一件事。
    ///
    /// **This is why TryRebind exists: ONNX lets a body write its result back onto the input name, and the earlier implementation refused it as a same-scope duplicate —
    /// refusing is not a bug by itself; calling a legal model illegal is.** Two rounds pin the value, so "it unrolls" and "it computes right" stay the same claim.
    /// </remarks>
    [Fact]
    public void AnInPlaceBody_ShouldUnroll() {
        var context = new OnnxLoweringContext();

        // 体：acc_in = acc_in + one（输出名与输入名相同）。
        OnnxLoopLowering.Result result = OnnxLoopLowering.Lower(
            Node(trip: 2, body: Body(new[] { Node("Add", "acc_in", "one", "acc_in") }, "acc_in")),
            context,
            new[] { Constant(1.0) },
            new[] { "iter", "acc_in" },
            new[] { "final" },
            new Dictionary<string, double> { ["one"] = 1.0 });

        result.IsOk.Should().BeTrue(result.Failure);
        result.FinalStates![0].Expression.Constant.ToDouble().Should().Be(3.0,
            "1 becomes 2 then 3; an implementation that refused the in-place write would have failed outright");
    }

    private static OnnxLoopExpansion.LoopNode Node(long trip, OnnxGraph body) =>
        new(trip, true, body, 1, 1, 1, 1);

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