#nullable enable

// IDE0007 与 IDE0008 会在同一个泛型局部上互相要求；按仓库既有做法文件级关闭并写理由。
// IDE0007 and IDE0008 demand opposite things on the same generic local; disabled at file scope per repository practice.
#pragma warning disable IDE0007, IDE0008

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **二次域的元素列表：两侧含变量的矩阵乘** / The second-degree element list: a matrix product with variables on both sides.
/// </summary>
/// <remarks>
/// **次数是"整次导入"的属性，而不是逐节点的判断。** **所以这里是独立的表示，**
/// **只在一处产生——两侧都含变量的矩阵乘。**
///
/// **The degree is a property of the whole import rather than a per-node decision.** Hence a separate representation, **produced in exactly one place: a matrix product
/// with variables on both sides.**
/// </remarks>
public class OnnxQuadraticElementListTests
{
    /// <summary>**两侧含变量时结果是二次项的和** / Variables on both sides give a sum of quadratic terms.</summary>
    /// <remarks>
    /// **手算：** **`[x0, x1] · [[w0], [w1]] = x0·w0 + x1·w1`——** **两个二次单项式，各系数为 1。** '
    /// **若实现把乘积当成"两个常数相乘"，这里会得到一个常数；** **若没有合并同类项，重复项会让模型把同一个乘积算两遍。**
    ///
    /// **Hand-computed: [x0, x1] · [[w0], [w1]] = x0·w0 + x1·w1 — two quadratic monomials with coefficient one each.** Treated as two constants multiplying, this yields a constant; without merging
    /// like terms, a repeated term would make the model count one product twice.
    /// </remarks>
    [Fact]
    public void MatMulOfTwoSymbolicOperands_ShouldSumQuadraticTerms() {
        OnnxElementList left = Linear(new[] { 1, 2 }, "x0", "x1");
        OnnxElementList right = Linear(new[] { 2, 1 }, "w0", "w1");

        OnnxQuadraticElementList product = OnnxQuadraticElementList.MatMul(left, right).Value!;

        product.Shape.Should().Equal(new[] { 1, 1 });
        product.Count.Should().Be(1);
        product.Elements[0].Monomials.Should().HaveCount(2, "x0·w0 and x1·w1 are two distinct products");
        product.Elements[0].Monomials.Select(Label).Should()
            .BeEquivalentTo(new[] { "w0*x0", "w1*x1" }, "the label is written in name order");
    }

    /// <summary>**内维不一致被拒绝，两个数都出现** / A differing inner dimension is refused, with both numbers named.</summary>
    [Fact]
    public void DifferingInnerDimensions_ShouldBeRefused() {
        OnnxElementList left = Linear(new[] { 1, 2 }, "x0", "x1");
        OnnxElementList right = Linear(new[] { 3, 1 }, "w0", "w1", "w2");

        OnnxQuadraticElementList.Result product = OnnxQuadraticElementList.MatMul(left, right);

        product.IsOk.Should().BeFalse("2 does not match 3");
        product.Failure.Should().Contain("2").And.Contain("3");
    }

    /// <summary>**二次域的批量维按 numpy 广播逐批相乘，不相容时拒绝** / Batch dimensions broadcast and multiply per batch; incompatible ones are refused.</summary>
    /// <remarks>
    /// ** **原来这一条断言"二次域只做二维"，** **而批量维接上之后它会让一个能算的情形看起来仍然不行。** '
    /// **它同时钉两件事：** **`[2,1,2]·[2,2,1]` 广播成 `[2,2,1,1]`（四个元素），** **而 `[3,…]` 的批量维拒绝。**
    ///
    /// **the criterion used to assert "the second-degree domain is 2-D only", which after batch support would keep a working case looking unsupported.** It now pins two
    /// things: **[2,1,2]·[2,2,1] broadcasts to [2,2,1,1] with four elements, and a batch of 3 against 2 is refused.**
    /// </remarks>
    [Fact]
    public void BatchDimensions_ShouldBroadcastAndMultiplyPerBatch() {
        OnnxElementList left = Linear(new[] { 2, 1, 2 }, "x0", "x1", "x2", "x3");
        OnnxElementList right = Linear(new[] { 2, 2, 1 }, "w0", "w1", "w2", "w3");

        OnnxQuadraticElementList product = OnnxQuadraticElementList.MatMul(left, right).Value!;

        // `[2,1,2]` 的批量维是 `[2]`（末两维是矩阵），不是 `[2,1]`——
        // In [2,1,2] the batch is [2] with the last two dimensions forming the matrix, not [2,1] — 
        product.Shape.Should().Equal(new[] { 2, 1, 1 }, "both sides carry a batch of 2, and the matrix corners are 1×1");
        product.Count.Should().Be(2);
        foreach (QuadraticPolynomial<Flt64> element in product.Elements) {
            element.Monomials.Should().HaveCount(2, "each batch sums two products over the inner dimension");
        }

        // 内维先要能对上，才能测到批量维：`[2,2,2]` 的内维是 2，与左侧一致，而批量是 2 对 3。
        // The inner dimensions have to agree first for the batch mismatch to be reached: [2,2,2] has inner 2 like the left side, while the batch is 2 against 3.
        OnnxElementList incompatible = Linear(
            new[] { 2, 2, 2 },
            "v0", "v1", "v2", "v3", "v4", "v5", "v6", "v7");
        OnnxElementList batchOfThree = Linear(new[] { 3, 1, 2 }, "u0", "u1", "u2", "u3", "u4", "u5");
        OnnxQuadraticElementList.Result refused = OnnxQuadraticElementList.MatMul(batchOfThree, incompatible);
        refused.IsOk.Should().BeFalse("a batch of 2 against a batch of 3 is not broadcastable");
        refused.Failure.Should().Contain("批量维");
    }

    /// <summary>**元素个数必须等于形状的乘积** / The element count has to equal the product of the shape.</summary>
    [Fact]
    public void AMismatchedCount_ShouldFailToBuild() {
        OnnxQuadraticElementList.Result built = OnnxQuadraticElementList.Create(
            new[] { 2, 2 },
            new[] { Quadratic("x0"), Quadratic("x1") });

        built.IsOk.Should().BeFalse("four positions cannot hold two forms");
        built.Failure.Should().Contain("4").And.Contain("2");
    }

    /// <summary>
    /// 把两个符号按名排序后拼起来 / Join the two symbols after sorting them by name.
    /// </summary>
    /// <remarks>
    /// **`x0·w0` 与 `w0·x0` 是同一个乘积：** **底层按名字序排过，所以判据要比"是不是同一个乘积"，** '
    /// **而不是比"写在前面的那个名字"——** **后者会让一条正确的实现因为符号顺序而红。**
    ///
    /// **x0·w0 and w0·x0 are the same product: the underlying form sorts by name, so the criterion compares whether it is the same product, not which name was written first — the latter would
    /// fail a correct implementation over symbol order.**
    /// </remarks>
    /// <summary>**二次域的 `Gemm`：`alpha` 缩放乘积、`beta` 缩放偏置再相加** / Quadratic Gemm: alpha scales the product, beta the bias, then they are added.</summary>
    /// <remarks>
    /// **手算：** **`[x0, x1] · [[w0],[w1]] = w0·x0 + w1·x1`；** **`alpha = 2` 后系数为 2；** '
    /// **`C = [[c0]]`、`beta = 3` 再加 `3·c0`。** **所以三项系数分别是 2、2、3。** '
    /// **若 `beta` 忘了乘、或偏置被当成乘积的一部分，系数会不对而形状完全一样。**
    ///
    /// **Hand-computed: [x0, x1] · [[w0],[w1]] = w0·x0 + w1·x1; with alpha = 2 the coefficients are 2; C = [[c0]] with beta = 3 adds 3·c0.** **So the three coefficients are 2, 2 and 3.** Losing
    /// beta or folding the bias into the product changes the coefficients while the shape stays identical.
    /// </remarks>
    [Fact]
    public void GemmWithAlphaAndBeta_ShouldScaleAndAdd() {
        OnnxElementList left = Linear(new[] { 1, 2 }, "x0", "x1");
        OnnxElementList right = Linear(new[] { 2, 1 }, "w0", "w1");
        OnnxQuadraticElementList bias = OnnxQuadraticElementList.FromLinear(Linear(new[] { 1, 1 }, "c0"));

        OnnxQuadraticElementList product = OnnxQuadraticElementList.Gemm(
            left, right, bias, transA: false, transB: false, alpha: 2.0, beta: 3.0).Value!;

        product.Shape.Should().Equal(new[] { 1, 1 });
        var labelled = product.Elements[0].Monomials
            .Select(monomial => Label(monomial) + ":" + monomial.Coefficient.ToDouble().ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        labelled.Should().BeEquivalentTo(new[] { "w0*x0:2", "w1*x1:2", "c0:3" });
    }

    /// <summary>**`transA`／`transB` 在二次域里同样只改索引** / transA and transB only change indexing in the second-degree domain as well.</summary>
    [Fact]
    public void TransposedQuadraticGemm_ShouldOnlyChangeIndexing() {
        OnnxElementList leftStoredTransposed = Linear(new[] { 2, 1 }, "x0", "x1");
        OnnxElementList right = Linear(new[] { 2, 1 }, "w0", "w1");

        OnnxQuadraticElementList product = OnnxQuadraticElementList.Gemm(
            leftStoredTransposed, right, null, transA: true, transB: false, alpha: 1.0, beta: 1.0).Value!;

        product.Shape.Should().Equal(new[] { 1, 1 }, "transposing [2,1] gives [1,2], and [1,2]·[2,1] is [1,1]");
        product.Elements[0].Monomials.Should().HaveCount(2, "x0·w0 and x1·w1");
    }

    /// <summary>**乘积与偏置都含二次项时拒绝，并点名三次** / A quadratic product plus a quadratic bias is refused, named as degree three.</summary>
    /// <remarks>
    /// **"二次 + 二次"会到三次，而这一层只装二次；** **悄悄截断会留下一个看起来正常的二次模型，** **而目标里少了三次项。**
    ///
    /// **"Quadratic plus quadratic" reaches degree three while this layer holds degree two; truncating quietly would leave a plausible-looking quadratic model missing its cubic terms.**
    /// </remarks>
    [Fact]
    public void AQuadraticBiasOnAQuadraticProduct_ShouldBeRefused() {
        OnnxElementList left = Linear(new[] { 1, 2 }, "x0", "x1");
        OnnxElementList right = Linear(new[] { 2, 1 }, "w0", "w1");
        OnnxQuadraticElementList quadraticBias = OnnxQuadraticElementList.MatMul(
            Linear(new[] { 1, 2 }, "a0", "a1"),
            Linear(new[] { 2, 1 }, "b0", "b1")).Value!;

        OnnxQuadraticElementList.Result product = OnnxQuadraticElementList.Gemm(
            left, right, quadraticBias, false, false, 1.0, 1.0);

        product.IsOk.Should().BeFalse("the sum would reach degree three");
        product.Failure.Should().Contain("三次");
    }

    private static string Label(QuadraticMonomial<Flt64> monomial) {
        if (monomial.Symbol2 is null) {
            return monomial.Symbol1.Name;
        }
        string first = monomial.Symbol1.Name;
        string second = monomial.Symbol2.Name;
        return string.CompareOrdinal(first, second) <= 0 ? first + "*" + second : second + "*" + first;
    }

    private static QuadraticPolynomial<Flt64> Quadratic(string name) =>
        new(new[] { new QuadraticMonomial<Flt64>(Flt64.One, new RealVar(name)) }, Flt64.Zero);

    private static OnnxElementList Linear(IReadOnlyList<int> shape, params string[] names) {
        var elements = new List<LinearPolynomial<Flt64>>(names.Length);
        foreach (string name in names) {
            elements.Add(new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, new RealVar(name)) },
                Flt64.Zero));
        }
        return OnnxElementList.Create(shape, elements).Value!;
    }
}