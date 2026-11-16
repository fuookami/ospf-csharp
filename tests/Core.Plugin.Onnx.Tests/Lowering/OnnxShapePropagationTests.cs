#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Lowering;

/// <summary>
/// **形状沿节点推出来，推不出来就说推不出来** / Shapes are inferred along the nodes, and when there is no ground it says so.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `materializeShapes`：**基线在降阶前先补齐形状，因为"有几个元素"决定了后面每一步能不能按元素走。**
///
/// **这一层的边界很清楚：** **只推仿射链认识的算子**（`Add`／`Sub`／`Mul`／`Sum`／`Neg`／`Identity`／`Cast`），
/// **其余一律"推不出来"——** **而不是拿 1 蒙过去。**
///
/// Syncs `materializeShapes`: **the baseline fills shapes in before lowering, because "how many elements" decides whether later steps can stay per element.**
///
/// **The boundary is explicit: only the operators the affine chain knows are inferred, and everything else is "cannot be inferred" rather than a guessed 1.**
/// </remarks>
public class OnnxShapePropagationTests
{
    /// <summary>**两个同形张量相加，输出元素数是乘积** / Adding two equally shaped tensors gives the product as the element count.</summary>
    [Fact]
    public void AddingTwoShapedTensors_ShouldInferTheProduct() {
        OnnxGraph graph = Graph(
            new[] { Declared("x", 2, 3), Declared("y", 2, 3) },
            new[] { Node("Add", "x", "y", "out") },
            Undeclared("out"));

        OnnxShapePropagation.Result inferred = OnnxShapePropagation.Infer(graph);

        inferred["out"].Should().Equal(new[] { 2, 3 });
        inferred.Elements("out").Should().Be(6);
    }

    /// <summary>**没有声明形状的输出也能推出元素个数** / An output that declares nothing still gets an element count.</summary>
    /// <remarks>
    /// **这一条就是 B1370／B1371 那处上限的关闭证据：** **输出什么都不声明，仿射链此前只能把它当成一个数。**
    ///
    /// **This closes the ceiling recorded in B1370/B1371: the output declares nothing, and the affine chain previously had no choice but to treat it as one number.**
    /// </remarks>
    [Fact]
    public void AnUndeclaredOutput_ShouldStillBeInferred() {
        OnnxGraph graph = Graph(
            new[] { Declared("x", 3), Declared("y", 3) },
            new[] { Node("Add", "x", "y", "out") },
            Undeclared("out"));

        OnnxShapePropagation.Infer(graph).Elements("out").Should().Be(3);
    }

    /// <summary>**常数参与广播时按广播规则走** / A constant participates through the broadcast rule.</summary>
    [Fact]
    public void AConstant_ShouldBroadcast() {
        var initializer = new OnnxTensor("k", 1, new long[] { 1 }, Array.Empty<float>(), Array.Empty<long>(), Array.Empty<double>(), null);
        OnnxGraph graph = Graph(
            new[] { Declared("x", 4) },
            new[] { Node("Add", "x", "k", "out") },
            Undeclared("out"),
            new[] { initializer });

        OnnxShapePropagation.Result inferred = OnnxShapePropagation.Infer(graph);

        inferred["k"].Should().Equal(new[] { 1 }, "a constant's shape is written in the model, not inferred");
        inferred.Elements("out").Should().Be(4, "[4] broadcasts against [1] to [4]");
    }

    /// <summary>**认不出的算子一律"推不出来"** / An unfamiliar operator is "cannot be inferred", full stop.</summary>
    /// <remarks>
    /// **拿 1 蒙过去会让一个未知形状的张量被当成标量，** **而那种错在数值上表现为"结果只有一个数"，没人会去查形状。**
    ///
    /// **Guessing a 1 would treat a tensor of unknown shape as a scalar, and that shows up numerically as "the result is a single number" which nobody investigates.**
    /// </remarks>
    [Fact]
    public void AnUnknownOperator_ShouldNotBeInferred() {
        OnnxGraph graph = Graph(
            new[] { Declared("x", 6) },
            new[] { Node("ReduceSum", "x", "out") },
            Undeclared("out"));

        OnnxShapePropagation.Infer(graph).Elements("out").Should().BeNull();
    }

    /// <summary>**没有声明类型时不当成标量** / A value with no declared type is not treated as a scalar.</summary>
    /// <remarks>
    /// **"没写类型"与"标量"是两件事：** **标量会写 `element_type` 而形状为空，没写类型则什么依据都没有。**
    ///
    /// **"No type written" and "a scalar" are two different things: a scalar writes element_type with an empty shape, while no type writes no basis at all.**
    /// </remarks>
    [Fact]
    public void AValueWithoutADeclaredType_ShouldBeUnknown() {
        OnnxGraph graph = Graph(
            new[] { Undeclared("x") },
            new[] { Node("Neg", "x", "out") },
            Undeclared("out"));

        OnnxShapePropagation.Infer(graph).Elements("out").Should().BeNull();
    }

    /// <summary>**声明为标量的值元素个数是 1** / A value declared as a scalar has one element.</summary>
    [Fact]
    public void ADeclaredScalar_ShouldBeOneElement() {
        var scalar = new OnnxValueInfo("x", 1, Array.Empty<OnnxDimension>());
        OnnxGraph graph = Graph(new[] { scalar }, new[] { Node("Neg", "x", "out") }, Undeclared("out"));

        OnnxShapePropagation.Infer(graph).Elements("out").Should().Be(1);
    }

    private static OnnxValueInfo Declared(string name, params long[] dimensions) {
        var dims = new List<OnnxDimension>(dimensions.Length);
        foreach (long dimension in dimensions) {
            dims.Add(new OnnxDimension(dimension, null));
        }
        return new OnnxValueInfo(name, 1, dims);
    }

    private static OnnxValueInfo Undeclared(string name) => new(name, 0, Array.Empty<OnnxDimension>());

    private static OnnxNode Node(string opType, string left, string right, string output) =>
        new(opType, "n_" + opType, new[] { left, right }, new[] { output }, Array.Empty<OnnxAttribute>());

    private static OnnxNode Node(string opType, string input, string output) =>
        new(opType, "n_" + opType, new[] { input }, new[] { output }, Array.Empty<OnnxAttribute>());

    private static OnnxGraph Graph(
        OnnxValueInfo[] inputs,
        OnnxNode[] nodes,
        OnnxValueInfo output,
        OnnxTensor[]? initializers = null) =>
        new("g", nodes, initializers ?? Array.Empty<OnnxTensor>(), inputs,
            new[] { output }, Array.Empty<OnnxValueInfo>());
}