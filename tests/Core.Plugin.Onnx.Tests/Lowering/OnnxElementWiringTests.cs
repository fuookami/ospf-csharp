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
/// **带元素列表的绑定让重排与切片真的发生** / A binding with an element list lets the permutation and the slice really happen.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的做法：**基线里每个张量表达式都带 `elements`，所以 `transpose` 与 `sliceLeading` 都有东西可动。**
/// **C# 侧此前只有"一个名字一个标量表达式"，这里给绑定加了<b>可选的**元素列表——**可空是有意的：**
/// **大多数绑定不需要它，于是这一层能逐步接进来，而"有没有元素"也成了一个可判断的事实。**
///
/// Syncs `dee897d2`'s approach: **every tensor expression in the baseline carries elements, so transpose and sliceLeading have something to move.** The C# side had only "one name,
/// one scalar expression"; gives the binding an **optional** element list — **nullability on purpose: most bindings do not need it, so the layer wires in step by step, and
/// "are there elements" becomes a checkable fact.**
/// </remarks>
public class OnnxElementWiringTests
{
    /// <summary>**带元素列表时 `Transpose` 真的重排，即使结果落到按位置读者上** / With elements, Transpose really permutes even into a positional reader.</summary>
    /// <remarks>
    /// **这是第 ⑦ 批那条拒绝的"到期":** **重排无处可写时拒绝，而一旦有地方写，就该真的写。** '
    /// **判据认元素自己的编号：** **形状对了而元素乱序，正是这条要防的。**
    ///
    /// **This is where batch ⑦'s refusal expires: refuse while the permutation has nowhere to go, and really write it once it does.** The criterion identifies elements by their own label:
    /// **a right shape with scrambled elements is exactly what it guards.**
    /// </remarks>
    [Fact]
    public void ATransposeWithElements_ShouldReallyPermute() {
        var context = new OnnxLoweringContext();
        OnnxElementList elements = OnnxElementList.Create(new[] { 2, 3 }, Labels("e0", "e1", "e2", "e3", "e4", "e5")).Value!;
        context.TryBind("x", Labels("e0", "e1", "e2", "e3", "e4", "e5")[0], new[] { 2, 3 }, out _, elements)
            .Should().BeTrue();

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Transpose", new[] { "x" }, "out", PermAttribute(new long[] { 1, 0 })) }, "out"),
            context);

        // 多元素图输出按列表语义要拒绝——**"整张图交出一个多项式"对六元素输出本来就不成立。**
        // **中间绑定仍留在上下文里，所以重排是不是真发生了可以直接读，不必依赖图输出。**
        //
        // a multi-element graph output is refused under list semantics — "the graph yields one polynomial" never held for six elements. The intermediate binding stays
        // in the context, so the permutation is read directly instead of through the output.
        result.IsOk.Should().BeFalse("the output holds six elements and element-indexed selection is not wired yet");
        context.TryGet("out", out OnnxLoweringContext.Binding produced).Should().BeTrue(
            "the node ran even though the graph output was refused");
        produced.Shape.Should().Equal(new[] { 3, 2 });
        produced.Elements.Should().NotBeNull("the permutation has somewhere to go now");
        produced.Elements!.Elements[0].Monomials[0].Symbol.Name.Should().Be("e0");
        produced.Elements.Elements[1].Monomials[0].Symbol.Name.Should().Be("e3");
        produced.Elements.Elements[2].Monomials[0].Symbol.Name.Should().Be("e1");
        produced.Elements.Elements[3].Monomials[0].Symbol.Name.Should().Be("e4");
    }

    /// <summary>**没有元素列表时仍然按"可观察性"决定拒绝与否** / Without elements the observability rule still decides.</summary>
    [Fact]
    public void ATransposeWithoutElements_ShouldStillFollowObservability() {
        var context = new OnnxLoweringContext();
        context.TryBind("x", Labels("e0")[0], new[] { 2, 3 }, out _).Should().BeTrue();

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] {
                Node("Transpose", new[] { "x" }, "t", PermAttribute(new long[] { 1, 0 })),
                Node("MatMul", new[] { "t", "x" }, "out"),
            }, "out"),
            context);

        result.IsOk.Should().BeFalse("without elements the permutation cannot be written, and MatMul reads positionally");
        result.Failure.Should().Contain("Transpose");
    }

    /// <summary>**扫描输入带元素列表时，`Scan` 逐轮切片** / With elements on the scan input, Scan slices round by round.</summary>
    /// <remarks>
    /// **这一条把 ⑧ 从"能力已存在"推到"求值器真的用上了它"：** **三片 `x0`、`x1`、`x2` 走完循环体，得到 `x0 + x1 + x2`。**
    ///
    /// **This moves ⑧ from "the capability exists" to "the evaluator really uses it": three slices x0, x1 and x2 go through the body and give x0 + x1 + x2.**
    /// </remarks>
    [Fact]
    public void AScanWithElements_ShouldSlicePerRound() {
        var context = new OnnxLoweringContext();
        OnnxElementList slices = OnnxElementList.Create(new[] { 3 }, Labels("x0", "x1", "x2")).Value!;
        context.TryBind("slices", Labels("x0")[0], new[] { 3 }, out _, slices).Should().BeTrue();
        context.TryBind("init", Constant().Expression, Array.Empty<int>(), out _).Should().BeTrue();

        OnnxGraph body = Body(new[] { Node("Add", new[] { "state_in", "slice_in" }, "state_out") }, "state_out");
        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Scan", new[] { "init", "slices" }, "out", BodyAttribute(body)) }, "out"),
            context);

        result.IsOk.Should().BeTrue(result.Failure);
        LinearPolynomial<Flt64> final = result.Outputs!["out"].Expression;
        final.Monomials.Should().HaveCount(3, "x0, x1 and x2 each arrive once");
        foreach (string name in new[] { "x0", "x1", "x2" }) {
            final.Monomials.Should().Contain(
                monomial => monomial.Symbol.Name == name && monomial.Coefficient.ToDouble() == 1.0,
                name + " has to arrive exactly once");
        }
    }

    private static OnnxNode Node(string opType, string[] inputs, string output, params OnnxAttribute[] attributes) =>
        new(opType, "n_" + opType + "_" + output, inputs, new[] { output }, attributes);

    private static OnnxAttribute PermAttribute(long[] permutation) =>
        new("perm", null, null, null, null, permutation, null, null, 7);

    private static OnnxAttribute BodyAttribute(OnnxGraph body) =>
        new("body", null, null, null, null, null, null, null, 5, body);

    private static OnnxGraph Graph(OnnxNode[] nodes, string output) =>
        new("g", nodes, Array.Empty<OnnxTensor>(), Array.Empty<OnnxValueInfo>(),
            new[] { new OnnxValueInfo(output, 1, Array.Empty<OnnxDimension>()) }, Array.Empty<OnnxValueInfo>());

    private static OnnxGraph Body(OnnxNode[] nodes, string output) =>
        new("body", nodes, Array.Empty<OnnxTensor>(),
            new[] {
                new OnnxValueInfo("state_in", 1, Array.Empty<OnnxDimension>()),
                new OnnxValueInfo("slice_in", 1, Array.Empty<OnnxDimension>()),
            },
            new[] { new OnnxValueInfo(output, 1, Array.Empty<OnnxDimension>()) },
            Array.Empty<OnnxValueInfo>());

    private static OnnxLoweringContext.Binding Constant() =>
        new(new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero), Array.Empty<int>());

    private static IReadOnlyList<LinearPolynomial<Flt64>> Labels(params string[] names) {
        var elements = new List<LinearPolynomial<Flt64>>(names.Length);
        foreach (string name in names) {
            elements.Add(new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, new RealVar(name)) },
                Flt64.Zero));
        }
        return elements;
    }
}