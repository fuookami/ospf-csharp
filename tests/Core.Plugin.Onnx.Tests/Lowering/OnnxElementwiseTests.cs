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
/// **逐元素算子按元素配对，而不是把整个张量当成一个数** / Elementwise operators pair by element instead of treating a tensor as one number.
/// </summary>
/// <remarks>
/// 对齐基线 `lowerElementwiseBinary`：**每个张量是 `OnnxTensorExpr(shape, elements)`，所以"加"天然就是逐位置加。**
/// **C# 侧此前把多元素张量压成一个标量表达式——** **那在数学上是一个错误但自洽的表示，一旦两个不同形状的多元素张量相遇就会把两个不同的东西相加。**
///
/// **判据读的是中间绑定：** **图输出多元素时会明确拒绝（按元素取输出还没接），而中间结果仍留在上下文里，**
/// **所以"配对对不对"可以直接看，而不是只能断言"失败了"。**
///
/// Syncs `lowerElementwiseBinary`: **in the code every tensor is OnnxTensorExpr(shape, elements), so "add" is per position by construction.** The C# side used to collapse a
/// multi-element tensor into one scalar expression — **a self-consistent but mathematically wrong representation that adds two different things the moment two differently shaped
/// multi-element tensors meet.**
///
/// **The criteria read the intermediate binding: a multi-element graph output is refused (element-indexed selection is not wired yet) while the intermediate result stays in the
/// context, so "did the pairing work" is observable instead of only "did it fail".**
/// </remarks>
public class OnnxElementwiseTests
{
    /// <summary>**两个同形张量逐元素相加，位置一一对应** / Two equally shaped tensors add per element, position by position.</summary>
    [Fact]
    public void AddingTwoTensors_ShouldPairPerElement() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 3 }, "a0", "a1", "a2");
        BindTensor(context, "b", new[] { 3 }, "b0", "b1", "b2");

        OnnxLoweringContext.Binding summed = EvaluateThenRead(context, new[] { Node("Add", new[] { "a", "b" }, "s") }, "s");

        summed.Elements.Should().NotBeNull("an elementwise result carries its elements");
        summed.Elements!.Count.Should().Be(3);
        Labels(summed.Elements).Should().Equal(
            new[] { "a0+b0", "a1+b1", "a2+b2" },
            "position i takes the i-th element of each side, not the whole tensor");
    }

    /// <summary>**标量加张量时，标量对齐到每个位置** / A scalar adds to every position of a tensor.</summary>
    /// <remarks>
    /// **ONNX 定义"标量 + 张量"为逐元素加。** **更一般的广播（`[3]` 与 `[2,3]`）没有在这一层实现，遇到长度不同的两个列表会拒绝。**
    ///
    /// **ONNX defines scalar-plus-tensor as elementwise.** The general broadcast ([3] against [2,3]) is not implemented here; two lists of different lengths are refused.
    /// </remarks>
    [Fact]
    public void AScalarAddingATensor_ShouldReachEveryElement() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 3 }, "a0", "a1", "a2");
        context.TryBind("k", Constant(2.0), Array.Empty<int>(), out _).Should().BeTrue();

        OnnxLoweringContext.Binding summed = EvaluateThenRead(context, new[] { Node("Add", new[] { "a", "k" }, "s") }, "s");

        summed.Elements!.Count.Should().Be(3);
        Labels(summed.Elements).Should().Equal(new[] { "a0+2", "a1+2", "a2+2" });
    }

    /// <summary>**取反逐元素进行** / Negation applies per element.</summary>
    [Fact]
    public void Negation_ShouldApplyPerElement() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 2 }, "a0", "a1");

        OnnxLoweringContext.Binding negated = EvaluateThenRead(context, new[] { Node("Neg", new[] { "a" }, "s") }, "s");

        negated.Elements!.Count.Should().Be(2);
        Labels(negated.Elements).Should().Equal(new[] { "-a0", "-a1" });
    }

    /// <summary>**元素个数不同时被拒绝** / Different element counts are refused.</summary>
    /// <remarks>
    /// **放过去会让某几个位置静默取错数：** **按下标配对时，长的一方的尾部要么被忽略、要么越界。**
    ///
    /// **Letting it through would silently take the wrong number at some positions: pairing by index either drops the longer side's tail or runs past its end.**
    /// </remarks>
    [Fact]
    public void DifferentElementCounts_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 3 }, "a0", "a1", "a2");
        BindTensor(context, "b", new[] { 2 }, "b0", "b1");

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Add", new[] { "a", "b" }, "out") }, "out"),
            context);

        result.IsOk.Should().BeFalse();
        result.Failure.Should().Contain("3").And.Contain("2", "both counts have to appear in the message");
    }

    /// <summary>**多元素图输出被明确拒绝，而不是交出第一个位置** / A multi-element graph output is refused rather than handing back the first position.</summary>
    /// <remarks>
    /// **这是"宁可不做也不静默算错"的那条线：** **多元素绑定没有唯一的标量表达式，** **而按元素取输出（`elementIndex`）还没接。**
    ///
    /// **This is the line between "not done" and "silently wrong": a multi-element binding has no single scalar expression, and element-indexed output selection (its
    /// elementIndex) is not wired yet.**
    /// </remarks>
    [Fact]
    public void AMultiElementGraphOutput_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 3 }, "a0", "a1", "a2");

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Identity", new[] { "a" }, "out") }, "out"),
            context);

        result.IsOk.Should().BeFalse("a three-element output needs element-indexed selection");
        result.Failure.Should().Contain("out").And.Contain("元素");
    }

    /// <summary>**标量路径不受影响** / The scalar path is untouched.</summary>
    [Fact]
    public void TheScalarPath_ShouldStillWork() {
        var context = new OnnxLoweringContext();
        context.TryBind("x", Symbol("x"), Array.Empty<int>(), out _).Should().BeTrue();
        context.TryBind("y", Symbol("y"), Array.Empty<int>(), out _).Should().BeTrue();

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Add", new[] { "x", "y" }, "out") }, "out"),
            context);

        result.IsOk.Should().BeTrue(result.Failure);
        result.Outputs!["out"].Expression.Monomials.Should().HaveCount(2);
        result.Outputs["out"].Elements.Should().BeNull("nothing here needed an element list");
    }

    /// <summary>
    /// 跑图并读中间绑定 / Run the graph and read the intermediate binding.
    /// </summary>
    /// <remarks>
    /// **图输出多元素会被拒绝，而中间绑定仍留在上下文里——** **这样"配对对不对"能直接看，** **而不是只能断言"整张图失败了"。**
    ///
    /// **A multi-element graph output is refused while the intermediate binding stays in the context — so the pairing is directly observable instead of only "the graph failed".**
    /// </remarks>
    /// <summary>**`Concat` 有元素列表时按轴拼接** / Concat concatenates along the axis when the element lists are there.</summary>
    /// <remarks>
    /// **轴 1 上拼接必须按行交错：** **整块接在末尾会让形状与元素个数都正确，而每一行的内容全部错位。**
    ///
    /// **Concatenating on axis 1 has to interleave by row: appending wholesale matches the shape and the element count while every row's content shifts.**
    /// </remarks>
    [Fact]
    public void ConcatWithElements_ShouldJoinAlongTheAxis() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 2, 2 }, "a0", "a1", "a2", "a3");
        BindTensor(context, "b", new[] { 2, 1 }, "b0", "b1");

        OnnxLoweringContext.Binding joined = EvaluateThenRead(
            context,
            new[] { NodeWithAxis("Concat", new[] { "a", "b" }, "s", 1) },
            "s");

        joined.Shape.Should().Equal(new[] { 2, 3 });
        joined.Elements.Should().NotBeNull();
        Labels(joined.Elements!).Should().Equal(new[] { "a0", "a1", "b0", "a2", "a3", "b1" });
    }

    /// <summary>**`Concat` 没有元素列表时被拒绝，并点名 inputBindings** / Concat without element lists is refused, naming inputBindings.</summary>
    /// <remarks>
    /// **没有元素列表时，"第几个元素接到哪里"没有任何依据；** **按"一个数"处理会把多段张量当成一个加数，而数值上看不出异常。**
    ///
    /// **Without element lists there is no ground for "which element goes where"; treating it as one number turns several tensors into a single summand, invisible numerically.**
    /// </remarks>
    [Fact]
    public void ConcatWithoutElements_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        context.TryBind("a", Symbol("a"), new[] { 2, 2 }, out _).Should().BeTrue();
        context.TryBind("b", Symbol("b"), new[] { 2, 1 }, out _).Should().BeTrue();

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { NodeWithAxis("Concat", new[] { "a", "b" }, "out", 1) }, "out"),
            context);

        result.IsOk.Should().BeFalse();
        result.Failure.Should().Contain("inputBindings");
    }

    private static OnnxNode NodeWithAxis(string opType, string[] inputs, string output, long axis) =>
        new(opType, "n_" + opType + "_" + output, inputs, new[] { output },
            new[] { new OnnxAttribute("axis", null, axis, null, null, null, null, null, 3) });

    /// <summary>**`Split` 的每一段都绑到各自声明的名字上** / Every piece of a Split is bound to its own declared name.</summary>
    /// <remarks>
    /// **这一条就是多输出绑定的目的：** **若只绑第一段，第二段在上下文里根本不存在，** '
    /// **而"形状对、元素对、后面读不到"这种半成品最容易在事后被当成成功。**
    ///
    /// **This is what multi-output binding is for: with only the first piece bound, the second does not exist in the context, and a half-done job — right shape, right elements,
    /// unreadable afterwards — is the easiest thing to mistake for success later.**
    /// </remarks>
    [Fact]
    public void SplitWithElements_ShouldBindEveryPiece() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 4 }, "a0", "a1", "a2", "a3");

        // 两个输出各有两个元素，所以要显式允许逐元素输出——**这正是"调用方按元素取"的形态。**
        // Both outputs hold two elements, so the per-element output is allowed explicitly — **which is what "the caller reads by element" looks like.**
        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            GraphWithTwoOutputs(new[] { Node("Split", new[] { "a" }, "left", "right") }, "left", "right"),
            context,
            allowMultiElementOutput: true);

        result.IsOk.Should().BeTrue(result.Failure);
        context.TryGet("left", out OnnxLoweringContext.Binding left).Should().BeTrue();
        context.TryGet("right", out OnnxLoweringContext.Binding right).Should().BeTrue("the second piece has to be bound as well");
        left.Elements.Should().NotBeNull();
        right.Elements.Should().NotBeNull();
        Labels(left.Elements!).Should().Equal(new[] { "a0", "a1" });
        Labels(right.Elements!).Should().Equal(new[] { "a2", "a3" });
    }

    /// <summary>**`Split` 没有元素列表时被拒绝，并点名 inputBindings** / Split without element lists is refused, naming inputBindings.</summary>
    [Fact]
    public void SplitWithoutElements_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        context.TryBind("a", Symbol("a"), new[] { 4 }, out _).Should().BeTrue();

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            GraphWithTwoOutputs(new[] { Node("Split", new[] { "a" }, "left", "right") }, "left", "right"),
            context);

        result.IsOk.Should().BeFalse();
        result.Failure.Should().Contain("inputBindings");
    }

    /// <summary>**`Gather` 用常量索引采集，结果按索引顺序** / Gather with constant indices follows the index order.</summary>
    [Fact]
    public void GatherWithConstantIndices_ShouldFollowTheIndexOrder() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 4 }, "a0", "a1", "a2", "a3");
        context.TryBind("idx", Constant(0.0), new[] { 2 }, out _).Should().BeTrue();

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Gather", new[] { "a", "idx" }, "out") }, "out"),
            context,
            constantIntArrays: new Dictionary<string, IReadOnlyList<long>> { ["idx"] = new long[] { 3, 1 } });

        result.IsOk.Should().BeFalse("the output holds two elements");
        context.TryGet("out", out OnnxLoweringContext.Binding gathered).Should().BeTrue("the node ran");
        Labels(gathered.Elements!).Should().Equal(new[] { "a3", "a1" });
    }

    /// <summary>**索引不是常量时被拒绝** / A non-constant index is refused.</summary>
    /// <remarks>
    /// **索引是变量意味着每次取的位置不同，** **那就不是一层能展开的东西；** **按"取第一个"处理会让模型照常求解、结果不同。**
    ///
    /// **A variable index means a different position each time, which this layer cannot unroll; treating it as "take the first" would let the model solve with a different answer.**
    /// </remarks>
    [Fact]
    public void GatherWithAVariableIndex_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 4 }, "a0", "a1", "a2", "a3");
        context.TryBind("idx", Symbol("idx"), new[] { 2 }, out _).Should().BeTrue();

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Gather", new[] { "a", "idx" }, "out") }, "out"),
            context);

        result.IsOk.Should().BeFalse();
        result.Failure.Should().Contain("静态常量");
    }

    private static OnnxGraph GraphWithTwoOutputs(OnnxNode[] nodes, string first, string second) =>
        new("g", nodes, Array.Empty<OnnxTensor>(), Array.Empty<OnnxValueInfo>(),
            new[] {
                new OnnxValueInfo(first, 1, Array.Empty<OnnxDimension>()),
                new OnnxValueInfo(second, 1, Array.Empty<OnnxDimension>()),
            },
            Array.Empty<OnnxValueInfo>());

    private static OnnxNode Node(string opType, string[] inputs, string firstOutput, string secondOutput) =>
        new(opType, "n_" + opType, inputs, new[] { firstOutput, secondOutput }, Array.Empty<OnnxAttribute>());

    /// <summary>**两侧含变量的 `Mul` 是逐元素相乘，不是矩阵乘** / Mul with variables on both sides multiplies element by element, not as a matrix product.</summary>
    /// <remarks>
    /// **这条判据防的是"把逐元素写成了矩阵乘"：** **`[x0,x1]·[w0,w1]` 逐元素得 `[x0·w0, x1·w1]`（两项），** '
    /// **而矩阵乘会得到一个求和（一项）。** **两者形状可以一样，只在元素个数与内容上不同。**
    ///
    /// **This criterion guards against writing the elementwise product as a matrix product: [x0,x1]·[w0,w1] elementwise gives [x0·w0, x1·w1] (two terms),** while a matrix product gives one
    /// summed term. **The shapes can agree, and only the element count and content differ.**
    /// </remarks>
    [Fact]
    public void MultiplyingTwoTensors_ShouldBeElementwise() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 2 }, "a0", "a1");
        BindTensor(context, "b", new[] { 2 }, "b0", "b1");

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Mul", new[] { "a", "b" }, "out") }, "out"),
            context,
            quadraticMode: true);

        result.IsOk.Should().BeFalse("the output is multi-element");
        context.TryGet("out", out OnnxLoweringContext.Binding product).Should().BeTrue("the node ran");
        product.QuadraticElements.Should().NotBeNull();
        product.QuadraticElements!.Count.Should().Be(2, "elementwise means one product per position, not one sum");
        product.QuadraticElements.Elements[0].Monomials.Should().HaveCount(1);
        product.QuadraticElements.Elements[1].Monomials.Should().HaveCount(1);
    }

    /// <summary>**一次模式下两侧含变量的 `Mul` 被拒绝，并点名 Quadratic** / In linear mode a two-variable Mul is refused, naming Quadratic.</summary>
    [Fact]
    public void MultiplyingTwoTensorsInLinearMode_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 2 }, "a0", "a1");
        BindTensor(context, "b", new[] { 2 }, "b0", "b1");

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Mul", new[] { "a", "b" }, "out") }, "out"),
            context);

        result.IsOk.Should().BeFalse();
        result.Failure.Should().Contain("二次").And.Contain("Quadratic");
    }

    /// <summary>**元素个数不同时拒绝** / Different element counts are refused.</summary>
    [Fact]
    public void MultiplyingDifferentCounts_ShouldBeRefused() {
        var context = new OnnxLoweringContext();
        BindTensor(context, "a", new[] { 2 }, "a0", "a1");
        BindTensor(context, "b", new[] { 3 }, "b0", "b1", "b2");

        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(
            Graph(new[] { Node("Mul", new[] { "a", "b" }, "out") }, "out"),
            context,
            quadraticMode: true);

        result.IsOk.Should().BeFalse("two cannot pair with three");
        result.Failure.Should().Contain("2").And.Contain("3");
    }

    private static OnnxLoweringContext.Binding EvaluateThenRead(
        OnnxLoweringContext context,
        OnnxNode[] nodes,
        string intermediateName) {
        OnnxGraphEvaluator.Result result = OnnxGraphEvaluator.Evaluate(Graph(nodes, intermediateName), context);

        result.IsOk.Should().BeFalse("the output is multi-element and element-indexed selection is not wired yet");
        context.TryGet(intermediateName, out OnnxLoweringContext.Binding binding).Should().BeTrue(
            "the node ran even though the graph output was refused");
        return binding;
    }

    /// <summary>把每个位置的表达式读成可比较的字符串 / Read each position's expression as a comparable label.</summary>
    private static IEnumerable<string> Labels(OnnxElementList list) {
        foreach (LinearPolynomial<Flt64> element in list.Elements) {
            string label = string.Empty;
            foreach (LinearMonomial<Flt64> monomial in element.Monomials) {
                if (label.Length > 0) {
                    label += "+";
                }
                label += (monomial.Coefficient.ToDouble() < 0 ? "-" : string.Empty) + monomial.Symbol.Name;
            }
            double constant = element.Constant.ToDouble();
            if (constant != 0.0) {
                label += "+" + constant.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            yield return label;
        }
    }

    private static void BindTensor(OnnxLoweringContext context, string name, int[] shape, params string[] elements) {
        IReadOnlyList<LinearPolynomial<Flt64>> list = Symbols(elements);
        OnnxElementList built = OnnxElementList.Create(shape, list).Value!;
        context.TryBind(name, list[0], shape, out _, built).Should().BeTrue();
    }

    private static IReadOnlyList<LinearPolynomial<Flt64>> Symbols(params string[] names) {
        var elements = new List<LinearPolynomial<Flt64>>(names.Length);
        foreach (string name in names) {
            elements.Add(Symbol(name));
        }
        return elements;
    }

    private static LinearPolynomial<Flt64> Symbol(string name) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, new RealVar(name)) }, Flt64.Zero);

    private static LinearPolynomial<Flt64> Constant(double value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value));

    private static OnnxNode Node(string opType, string[] inputs, string output) =>
        new(opType, "n_" + opType + "_" + output, inputs, new[] { output }, Array.Empty<OnnxAttribute>());

    private static OnnxGraph Graph(OnnxNode[] nodes, string output) =>
        new("g", nodes, Array.Empty<OnnxTensor>(), Array.Empty<OnnxValueInfo>(),
            new[] { new OnnxValueInfo(output, 1, Array.Empty<OnnxDimension>()) }, Array.Empty<OnnxValueInfo>());
}