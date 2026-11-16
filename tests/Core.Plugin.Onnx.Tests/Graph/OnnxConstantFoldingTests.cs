#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Graph;

/// <summary>
/// **常量折叠：折错一个数，整条链围着错值优化** / Fold a wrong number and the whole chain optimises around it.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `foldConstant` 与 `Shape` 的静态求值。**折叠的价值是把"由图描述的常数"变成"数本身"，风险是折出一个错的常数——**
/// **那种错会让下游每一步都"成功"。**
///
/// Syncs `foldConstant` and the static `Shape` evaluation. **Folding turns "a constant described by a graph" into "a number"; its risk is folding a wrong one,
/// which lets every downstream step report success.**
/// </remarks>
public class OnnxConstantFoldingTests
{
    /// <summary>**`value` 张量被折成同名常量** / A `value` tensor folds into a constant under the output name.</summary>
    /// <remarks>
    /// **输出名必须改写成节点的输出名，而不是沿用它自己的：** **`Constant` 的 `value` 里那个 `name` 通常为空或与输出不同，**
    /// **照抄会让这张常量在张量表里挂着一个没人引用的名字——** **于是"常量存在但输入仍不可得"。**
    ///
    /// **The output name has to be rewritten to the node's output rather than kept: the `name` inside a Constant's `value` is usually empty or different, and
    /// copying it leaves the constant registered under a name nobody references — so "the constant exists and the input is still unavailable".**
    /// </remarks>
    [Fact]
    public void TheValueAttribute_ShouldFoldUnderTheOutputName() {
        OnnxNode node = ConstantNode(
            "const_out",
            new OnnxAttribute("value", null, null, null, null, null, null,
                new OnnxTensor("inner_name", 1, new long[] { 2 }, new[] { 1.5f, -2.5f }, Array.Empty<long>(), Array.Empty<double>(), null),
                4));

        OnnxConstantFolding.Result result = OnnxConstantFolding.FoldConstant(node);

        result.Failure.Should().BeNull();
        result.Value!.Name.Should().Be("const_out", "the folded constant lives under the node's output, not the tensor's own name");
        result.Value.Shape.Should().Equal(new[] { 2 });
        result.Value.DoubleAt(0).Should().Be(1.5);
        result.Value.DoubleAt(1).Should().Be(-2.5);
    }

    /// <summary>**`value_float` 折成 0 阶浮点、`value_int` 折成 0 阶整数** / Scalar attributes fold into rank-0 tensors.</summary>
    /// <remarks>
    /// **两者都是 0 阶（元素数 1），差别只在 dtype：** **把 `value_int` 折成 `Float32` 会让一张索引常量在取整时被截断，**
    /// **而 `Gather` 的索引一旦错了位，取出来的整段都是别的元素。**
    ///
    /// **Both are rank-0 (one element), differing only in dtype: folding `value_int` as `Float32` truncates an index constant during conversion, and once a Gather
    /// index is off the whole slice comes from elsewhere.**
    /// </remarks>
    [Fact]
    public void TheScalarAttributes_ShouldFoldWithTheirOwnDType() {
        OnnxConstantFolding.Result asFloat = OnnxConstantFolding.FoldConstant(
            ConstantNode("f", new OnnxAttribute("value_float", 0.25f, null, null, null, null, null, null, 1)));
        asFloat.Value!.DType.Should().Be(OnnxDataType.Float32);
        asFloat.Value.ElementCount.Should().Be(1, "a scalar is zero-dimensional and holds one element");
        asFloat.Value.DoubleAt(0).Should().Be(0.25);

        OnnxConstantFolding.Result asInt = OnnxConstantFolding.FoldConstant(
            ConstantNode("i", new OnnxAttribute("value_int", null, 7L, null, null, null, null, null, 2)));
        asInt.Value!.DType.Should().Be(OnnxDataType.Int64, "an integer constant stays an integer, or a Gather index loses precision");
        asInt.Value.LongAt(0).Should().Be(7);
    }

    /// <summary>**没有可识别属性的 `Constant` 被拒绝，而不是当成零** / A Constant without a recognised attribute is refused, not read as zero.</summary>
    /// <remarks>
    /// **被当成零的常量会让模型里一整项消失，** **而消失的项永远不会出现在解里——** **所以也没人会去找它。**
    ///
    /// **A constant taken as zero erases a whole term, and a missing term never shows up in the answer, so nobody goes looking for it.**
    /// </remarks>
    [Fact]
    public void AConstantWithoutAKnownAttribute_ShouldBeRefused() {
        OnnxConstantFolding.Result result = OnnxConstantFolding.FoldConstant(
            ConstantNode("c", new OnnxAttribute("value_strings", null, null, null, null, null, new[] { "a" }, null, 8)));

        result.Value.Should().BeNull("a string-list constant is not something this plugin folds, and zero is not its value");
        result.Failure.Should().NotBeNull();

        OnnxConstantFolding.FoldConstant(new OnnxNode("Constant", "n", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<OnnxAttribute>()))
            .Value.Should().BeNull("a Constant with no output cannot be folded at all");
    }

    /// <summary>**未知数据类型编号不被猜成 `Float32`** / An unknown dtype id is not guessed.</summary>
    /// <remarks>
    /// **这正是本插件在数据类型那一批里钉死的规则**，**折叠这里必须遵守同一套：** **猜成 `Float32` 会把一张 `Int64` 索引表当浮点读，**
    /// **而那种错要到数值层面才看得见。**
    ///
    /// **This is the rule pinned in the data-type batch, and folding has to obey the same one: guessing `Float32` reads an `Int64` index table as floating point,
    /// an error visible only at the numeric level.**
    /// </remarks>
    [Fact]
    public void AnUnknownDType_ShouldBeRefused() {
        OnnxConstantFolding.Result result = OnnxConstantFolding.FoldConstant(
            ConstantNode("c", new OnnxAttribute("value", null, null, null, null, null, null,
                new OnnxTensor("t", 14, new long[] { 1 }, new[] { 1.0f }, Array.Empty<long>(), Array.Empty<double>(), null), 4)));

        result.Value.Should().BeNull("14 is not a type this plugin knows, and guessing one is worse than refusing");
        result.Failure.Should().Contain("14");
    }

    /// <summary>**`Shape` 折叠出各维长度** / `Shape` folds to the dimension lengths.</summary>
    [Fact]
    public void Shape_ShouldFoldToTheDimensionLengths() {
        OnnxConstantFolding.Result result = OnnxConstantFolding.FoldShape(
            new[] { new OnnxDimension(2, null), new OnnxDimension(3, null), new OnnxDimension(4, null) },
            "shape_out");

        result.Failure.Should().BeNull();
        result.Value!.DType.Should().Be(OnnxDataType.Int64, "Shape outputs int64, and a Reshape downstream reads them as integers");
        result.Value.Shape.Should().Equal(new[] { 3 }, "the result is one value per dimension");
        result.Value.Longs.Should().Equal(new long[] { 2, 3, 4 });
    }

    /// <summary>**符号维度让 `Shape` 折叠失败，而不是折成零** / A symbolic dimension makes Shape fail rather than fold to zero.</summary>
    /// <remarks>
    /// **把符号维度当成 0 会折出一排零，下游 `Reshape` 拿这排零算目标形状，得到"每维都是 0"的合法形状——**
    /// **形状推演从此完全走错，且每一步都没有报错。**
    ///
    /// **Reading a symbolic dimension as 0 folds a row of zeros, the downstream Reshape uses it to compute a target shape, and gets a legal shape whose every dimension
    /// is zero — shape inference is then completely wrong and no step reports anything.**
    /// </remarks>
    [Fact]
    public void Shape_ShouldRefuseASymbolicDimension() {
        OnnxConstantFolding.Result result = OnnxConstantFolding.FoldShape(
            new[] { new OnnxDimension(2, null), new OnnxDimension(null, "batch") },
            "shape_out");

        result.Value.Should().BeNull("a symbolic dimension has no length to fold");
        result.Failure.Should().Contain("batch", "and the reason names the symbolic dimension so the caller knows which axis is unknown");
    }

    /// <summary>**折出来的张量载荷与形状一致（幂等的前提）** / A folded tensor's payload agrees with its shape.</summary>
    /// <remarks>
    /// **折叠会在每轮遍历里被调用：** **折出来的张量若载荷与形状不一致，下一轮读到的是错位的数据，** **而错位本身没有任何报错。**
    ///
    /// **Folding runs once per traversal: a folded tensor whose payload disagrees with its shape feeds shifted data into the next pass, and the shift itself reports
    /// nothing.**
    /// </remarks>
    [Fact]
    public void AFoldedTensor_ShouldBeConsistentWithItsShape() {
        OnnxConstantFolding.Result result = OnnxConstantFolding.FoldConstant(
            ConstantNode("c", new OnnxAttribute("value", null, null, null, null, null, null,
                new OnnxTensor("t", 1, new long[] { 2, 2 }, new[] { 1f, 2f, 3f, 4f }, Array.Empty<long>(), Array.Empty<double>(), null), 4)));

        result.Value!.PayloadMatchesShape(out string? detail).Should().BeTrue(detail);
        OnnxConstantFolding.IsIdempotent(result.Value).Should().BeTrue("folding a well-formed constant again changes nothing");
    }

    private static OnnxNode ConstantNode(string output, params OnnxAttribute[] attributes) =>
        new("Constant", "n_Constant", Array.Empty<string>(), new[] { output }, attributes);
}