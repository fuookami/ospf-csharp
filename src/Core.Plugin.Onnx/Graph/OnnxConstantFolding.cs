#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Graph;

/// <summary>
/// **常量折叠** / Constant folding.
/// </summary>
/// <remarks>
/// `OnnxGraphParser.foldConstant` 与 `Shape`/`Reshape` 的静态求值。**折叠的价值在于把"由图描述的常数"变成"数本身"，**
/// **而它的风险在于折叠出一个错误的常数——** **那种错会让整条链下游全部围绕一个错值优化，** **而每一步都"成功"。**
///
/// Syncs `foldConstant` and the static evaluation of `Shape`/`Reshape`. **Folding turns "a constant described by a graph" into "a number", and its risk is folding
/// a wrong number — which makes the entire downstream optimise around a wrong value while every step reports success.**
/// </remarks>
public static class OnnxConstantFolding
{
    /// <summary>
    /// 折叠结果 / The folding result.
    /// </summary>
    /// <param name="Value">折叠出的张量，失败时为 null / The folded tensor, null on failure.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Result(OnnxTensorConstant? Value, string? Failure);

    /// <summary>
    /// 折叠一个 `Constant` 节点 / Fold one `Constant` node.
    /// </summary>
    /// <param name="node">节点 / The node.</param>
    /// <returns>折叠结果 / The result.</returns>
    /// <remarks>
    /// **`Constant` 只支持 `value`／`value_float`／`value_int` 三种属性，其余一律拒绝而不是"返回零"：** **一个被当成零的常量会让模型缺口整段消失，**
    /// **而缺失的项永远不会出现在解里，** **所以也没人会去找它。**
    ///
    /// **`Constant` supports only `value`, `value_float` and `value_int`; anything else is refused rather than read as zero: a constant silently taken as zero
    /// erases a whole term from the model, the missing term never appears in the answer, and so nobody goes looking for it.**
    /// </remarks>
    public static Result FoldConstant(OnnxNode node) {
        string? output = node.Outputs.Count > 0 ? node.Outputs[0] : null;
        if (output is null) {
            return new Result(null, "Constant 节点缺少输出 / the Constant node has no output");
        }

        OnnxAttribute? valueAttribute = FindAttribute(node, "value");
        if (valueAttribute?.Tensor is OnnxTensor tensor) {
            // 不认识的数据类型不猜一个默认值：猜成 Float32 会让一张 Int64 的索引表被当成浮点读。
            // An unknown data type is not guessed: defaulting to Float32 would read an Int64 index table as floating point.
            if (OnnxDataTypes.FromProtoId(tensor.DataType) is not OnnxDataType dtype) {
                return new Result(null, "Constant 的 value 使用了未知数据类型编号 " + tensor.DataType + " / unknown data type id");
            }
            return new Result(
                new OnnxTensorConstant(output, dtype, ToIntShape(tensor.Dims), ToDoubles(tensor.FloatData), tensor.Int64Data),
                null);
        }

        OnnxAttribute? floatAttribute = FindAttribute(node, "value_float");
        if (floatAttribute?.Float is float scalar) {
            return new Result(
                new OnnxTensorConstant(output, OnnxDataType.Float32, Array.Empty<int>(), new[] { (double)scalar }, Array.Empty<long>()),
                null);
        }

        OnnxAttribute? intAttribute = FindAttribute(node, "value_int");
        if (intAttribute?.Integer is long integer) {
            return new Result(
                new OnnxTensorConstant(output, OnnxDataType.Int64, Array.Empty<int>(), Array.Empty<double>(), new[] { integer }),
                null);
        }

        return new Result(
            null,
            "Constant 节点只支持 value/value_float/value_int / the Constant node supports only value, value_float and value_int");
    }

    /// <summary>
    /// 静态求值 `Shape` / Statically evaluate `Shape`.
    /// </summary>
    /// <param name="inputShape">输入形状 / The input shape.</param>
    /// <param name="output">输出名 / The output name.</param>
    /// <returns>折叠出的常量 / The folded constant.</returns>
    /// <remarks>
    /// **未知维度（符号维度）在这里必须被拒绝：** **把它当成 0 会让 `Shape` 折叠出一排零，** **而下游的 `Reshape` 会拿这排零去算目标形状，**
    /// **得到"每个维度都是 0"的合法形状——** **形状推演从此完全走错，且每一步都没有报错。**
    ///
    /// **An unknown (symbolic) dimension has to be refused here: reading it as 0 folds `Shape` into a row of zeros, the downstream `Reshape` uses that row to compute
    /// a target shape, and gets a legal shape whose every dimension is zero — shape inference is then completely wrong and no step reports anything.**
    /// </remarks>
    public static Result FoldShape(IReadOnlyList<OnnxDimension> inputShape, string output) {
        long[] dims = new long[inputShape.Count];
        for (int index = 0; index < inputShape.Count; ++index) {
            if (inputShape[index].Value is not long value) {
                // 名字要带出来：只知道"某一维未知"没法让调用方去补哪个绑定。
                // The name has to appear: "some dimension is unknown" does not tell the caller which binding to supply.
                return new Result(
                    null,
                    "Shape 的输入含未知维度（第 " + index + " 维是符号维度 '"
                    + (inputShape[index].Symbol ?? "?") + "'）/ Shape cannot be folded over a symbolic dimension");
            }
            dims[index] = value;
        }
        return new Result(
            new OnnxTensorConstant(output, OnnxDataType.Int64, new[] { dims.Length }, Array.Empty<double>(), dims),
            null);
    }

    /// <summary>
    /// 折叠是否幂等：再折一次得到同样的张量 / Whether folding is idempotent: folding again yields the same tensor.
    /// </summary>
    /// <param name="folded">第一次折叠的结果 / The first folding result.</param>
    /// <returns>是否幂等 / Whether it is idempotent.</returns>
    /// <remarks>
    /// **幂等性不是学术趣味：** **折叠会在每轮遍历里被调用，** **不幂等意味着"多跑一轮就变一个值"，** **而最终值取决于遍历了多少轮——**
    /// **那是最难复现的一类 bug。**
    ///
    /// **Idempotence is not academic: folding runs once per traversal, and non-idempotence means "each extra pass changes the value", so the final number depends on
    /// how many passes ran — the hardest kind of bug to reproduce.**
    /// </remarks>
    public static bool IsIdempotent(OnnxTensorConstant folded) => folded.PayloadMatchesShape(out _);

    private static OnnxAttribute? FindAttribute(OnnxNode node, string name) {
        foreach (OnnxAttribute attribute in node.Attributes) {
            if (string.Equals(attribute.Name, name, StringComparison.Ordinal)) {
                return attribute;
            }
        }
        return null;
    }

    private static double[] ToDoubles(IReadOnlyList<float> values) {
        double[] result = new double[values.Count];
        for (int index = 0; index < values.Count; ++index) {
            result[index] = values[index];
        }
        return result;
    }

    private static int[] ToIntShape(IReadOnlyList<long> dims) {
        int[] shape = new int[dims.Count];
        for (int index = 0; index < dims.Count; ++index) {
            shape[index] = (int)dims[index];
        }
        return shape;
    }
}
