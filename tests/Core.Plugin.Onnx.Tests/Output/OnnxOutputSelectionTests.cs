#nullable enable

using System;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Onnx.Output;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Tests.Output;

/// <summary>
/// **输出聚合是模型语义，不是后处理** / Aggregation is semantics, not post-processing.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxAggregation` 与 `OnnxOutputSelection`。**优化模型只有一个目标值而 ONNX 输出是张量，**
/// **所以"怎么折"决定了优化在优化什么：** **`Sum` 与 `Mean` 会让同一个网络优化出不同的解。**
///
/// Syncs `OnnxAggregation` and `OnnxOutputSelection`. **An optimisation model has one objective while an ONNX output is a tensor, so how it folds decides what is
/// being optimised: Sum and Mean drive the same network to different solutions.**
/// </remarks>
public class OnnxOutputSelectionTests
{
    /// <summary>**越界索引返回 null，不夹紧** / An out-of-range index returns null rather than clamping.</summary>
    /// <remarks>
    /// **夹紧会让"我要第 100 个元素"在一张 10 元素的表上安静地变成第 9 个：** **优化照常完成，** **而解对应的是一个没人要求过的位置。**
    ///
    /// **Clamping turns "I want element 100" into element 9 on a ten-element table: the optimisation completes and the answer belongs to a position nobody asked
    /// for.**
    /// </remarks>
    [Fact]
    public void AnOutOfRangeIndex_ShouldBeNull() {
        var selection = new OnnxOutputSelection("y", OnnxAggregation.Element, ElementIndex: 100);
        selection.Fold(new[] { 0.0, 1.0, 2.0 }).Should().BeNull("there is no hundredth element to return");
        new OnnxOutputSelection("y", OnnxAggregation.Element, ElementIndex: -1).Validate(out string? negative).Should().BeFalse();
        negative.Should().NotBeNull();
    }

    /// <summary>**六种折叠各自的取值** / Each of the six folds yields its own value.</summary>
    [Fact]
    public void EachAggregation_ShouldFoldDifferently() {
        double[] values = { 3.0, 1.0, 4.0, 1.0, 5.0 };

        new OnnxOutputSelection("y", OnnxAggregation.Element, 2).Fold(values).Should().Be(4.0);
        new OnnxOutputSelection("y", OnnxAggregation.First).Fold(values).Should().Be(3.0);
        new OnnxOutputSelection("y", OnnxAggregation.Last).Fold(values).Should().Be(5.0);
        new OnnxOutputSelection("y", OnnxAggregation.Sum).Fold(values).Should().Be(14.0);
        new OnnxOutputSelection("y", OnnxAggregation.Mean).Fold(values).Should().Be(2.8);
        new OnnxOutputSelection("y", OnnxAggregation.Max).Fold(values).Should().Be(5.0);
        new OnnxOutputSelection("y", OnnxAggregation.Min).Fold(values).Should().Be(1.0);
    }

    /// <summary>**`Sum` 与 `Mean` 只在元素数不为 1 时才有别** / Sum and Mean differ only when there is more than one element.</summary>
    /// <remarks>
    /// **这正是它危险的形状：** **一个恰好只有单元素的输出上，选错聚合方式完全看不出来——** **而模型里张量形状会随输入变化，**
    /// **于是同一个选错在某次运行里正确、在另一次里错。**
    ///
    /// **That is exactly what makes it dangerous: on an output that happens to hold a single element the wrong choice is invisible — and tensor shapes vary with
    /// the input, so the same wrong choice is right in one run and wrong in the next.**
    /// </remarks>
    [Fact]
    public void SumAndMean_ShouldCoincideOnlyForASingleElement() {
        new OnnxOutputSelection("y", OnnxAggregation.Sum).Fold(new[] { 7.0 })
            .Should().Be(new OnnxOutputSelection("y", OnnxAggregation.Mean).Fold(new[] { 7.0 })!.Value);

        new OnnxOutputSelection("y", OnnxAggregation.Sum).Fold(new[] { 7.0, 7.0 }).Should().Be(14.0);
        new OnnxOutputSelection("y", OnnxAggregation.Mean).Fold(new[] { 7.0, 7.0 }).Should().Be(7.0);
    }

    /// <summary>**空表返回 null，任何聚合都一样** / An empty table yields null for every aggregation.</summary>
    /// <remarks>
    /// **空表上的 `Max` 若返回 0 或 `-∞`，那个数会成为一个"合法的目标值"，** **而模型其实没有任何输出——** **优化会安静地优化一个不存在的目标。**
    ///
    /// **If Max on an empty table returned 0 or -infinity, that number would become a legitimate objective while the model has no output at all — the optimisation
    /// would quietly optimise something that does not exist.**
    /// </remarks>
    [Fact]
    public void AnEmptyTable_ShouldFoldToNull() {
        foreach (OnnxAggregation aggregation in Enum.GetValues<OnnxAggregation>()) {
            new OnnxOutputSelection("y", aggregation).Fold(Array.Empty<double>())
                .Should().BeNull(aggregation + " has no value over an empty table");
        }
    }

    /// <summary>**只有 `Element` 不需要整张张量** / Only Element avoids needing the whole tensor.</summary>
    /// <remarks>
    /// **这条区分决定了降阶规模：** **一张 1000×1000 的输出若只要一个元素却逐格建表达式，等于把模型放大一百万倍。**
    ///
    /// **This distinction decides the size of the lowering: building expressions cell by cell for a 1000×1000 output when only one cell is wanted inflates the model
    /// a million-fold.**
    /// </remarks>
    [Fact]
    public void OnlyElement_ShouldAvoidTheWholeTensor() {
        new OnnxOutputSelection("y", OnnxAggregation.Element).NeedsWholeTensor.Should().BeFalse();

        foreach (OnnxAggregation aggregation in Enum.GetValues<OnnxAggregation>()) {
            if (aggregation == OnnxAggregation.Element) {
                continue;
            }
            new OnnxOutputSelection("y", aggregation).NeedsWholeTensor.Should().BeTrue(
                aggregation + " needs every element to produce its value");
        }
    }

    /// <summary>**输出名不能为空** / The output name cannot be empty.</summary>
    [Fact]
    public void AnEmptyOutputName_ShouldBeRefused() {
        new OnnxOutputSelection("").Validate(out string? failure).Should().BeFalse(
            "a selection with no name cannot be matched against any graph output");
        failure.Should().NotBeNull();

        new OnnxOutputSelection("y").Validate(out _).Should().BeTrue();
    }
}