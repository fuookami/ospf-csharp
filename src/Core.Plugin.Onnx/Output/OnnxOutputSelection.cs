#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Output;

/// <summary>
/// **输出聚合方式** / How a whole output tensor becomes one scalar.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxAggregation`。**优化模型只能有一个目标值，而 ONNX 输出是张量，**
/// **所以"如何把张量折成一个数"是模型语义的一部分，不是后处理细节：** **选 `Mean` 还是 `Sum` 会让同一个模型优化出完全不同的解。**
///
/// Syncs `OnnxAggregation`. **An optimisation model has one objective value while an ONNX output is a tensor, so "how the tensor becomes a number" is part of the
/// model's semantics rather than post-processing: choosing Mean or Sum optimises the same network into different solutions.**
/// </remarks>
public enum OnnxAggregation
{
    /// <summary>取展平索引处的单个元素 / Take the element at the flattened index.</summary>
    Element = 0,
    /// <summary>求和 / Sum.</summary>
    Sum = 1,
    /// <summary>求平均 / Mean.</summary>
    Mean = 2,
    /// <summary>取第一个元素 / First element.</summary>
    First = 3,
    /// <summary>取最后一个元素 / Last element.</summary>
    Last = 4,
    /// <summary>取最大值 / Maximum.</summary>
    Max = 5,
    /// <summary>取最小值 / Minimum.</summary>
    Min = 6,
}

/// <summary>
/// **输出选择** / Output selection.
/// </summary>
/// <param name="OutputName">输出名称 / The output name.</param>
/// <param name="Aggregation">聚合方式 / The aggregation mode.</param>
/// <param name="ElementIndex">展平索引，只在 <see cref="OnnxAggregation.Element"/> 下生效 / The flattened index, used only with Element.</param>
public sealed record OnnxOutputSelection(string OutputName, OnnxAggregation Aggregation = OnnxAggregation.Element, int ElementIndex = 0)
{
    /// <summary>
    /// 该聚合是否需要整个张量 / Whether the aggregation needs the whole tensor.
    /// </summary>
    /// <remarks>
    /// **`Element` 只要一个元素，其余都要整张张量：** **这条区分决定了降阶能否"只算一个格"——** **对一张 1000×1000 的输出，
    /// 只要一个元素时仍逐格建表达式，等于把模型放大一百万倍。**
    ///
    /// **`Element` needs one element, everything else needs the whole tensor: that distinction decides whether lowering may compute a single cell — building
    /// expressions cell by cell for a 1000×1000 output when only one is wanted inflates the model a million-fold.**
    /// </remarks>
    public bool NeedsWholeTensor => Aggregation != OnnxAggregation.Element;

    /// <summary>
    /// 把一张值表折成一个标量 / Fold a value table into one scalar.
    /// </summary>
    /// <param name="values">展平后的元素 / The flattened elements.</param>
    /// <returns>标量；表为空或索引越界时为 null / The scalar, or null when the table is empty or the index is out of range.</returns>
    /// <remarks>
    /// **越界返回 null 而不是夹紧：** **夹紧会让"我要第 100 个元素"在一张 10 元素的表上安静地变成第 9 个，** **而优化照常完成，**
    /// **解对应的却是一个没人要求过的位置。**
    ///
    /// **Out of range returns null rather than clamping: clamping turns "I want element 100" into element 9 on a ten-element table, the optimisation still
    /// completes, and the answer corresponds to a position nobody asked for.**
    /// </remarks>
    public double? Fold(IReadOnlyList<double> values) {
        if (values.Count == 0) {
            return null;
        }
        switch (Aggregation) {
            case OnnxAggregation.Element: {
                if (ElementIndex < 0 || ElementIndex >= values.Count) {
                    return null;
                }
                return values[ElementIndex];
            }
            case OnnxAggregation.First:
                return values[0];
            case OnnxAggregation.Last:
                return values[values.Count - 1];
            case OnnxAggregation.Sum: {
                double total = 0.0;
                for (int index = 0; index < values.Count; ++index) {
                    total += values[index];
                }
                return total;
            }
            case OnnxAggregation.Mean: {
                double total = 0.0;
                for (int index = 0; index < values.Count; ++index) {
                    total += values[index];
                }
                return total / values.Count;
            }
            case OnnxAggregation.Max: {
                double best = values[0];
                for (int index = 1; index < values.Count; ++index) {
                    if (values[index] > best) {
                        best = values[index];
                    }
                }
                return best;
            }
            case OnnxAggregation.Min: {
                double best = values[0];
                for (int index = 1; index < values.Count; ++index) {
                    if (values[index] < best) {
                        best = values[index];
                    }
                }
                return best;
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// 校验选择本身是否成立 / Validate the selection itself.
    /// </summary>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>是否成立 / Whether it holds.</returns>
    public bool Validate(out string? failure) {
        if (string.IsNullOrEmpty(OutputName)) {
            failure = "输出选择缺少名称 / the output selection has no name";
            return false;
        }
        if (ElementIndex < 0) {
            failure = "输出索引不得为负 / the output index must not be negative";
            return false;
        }
        failure = null;
        return true;
    }
}