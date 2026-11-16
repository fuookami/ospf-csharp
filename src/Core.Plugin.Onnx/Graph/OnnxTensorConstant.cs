#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Graph;

/// <summary>
/// **归一化后的 ONNX 常量张量** / A normalised ONNX constant tensor.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的。**两种规范化取值并存的理由：**
/// ONNX 的张量既可能以 `float_data` 存放、也可能以 `int64_data` 存放，**而 dtype 决定该读哪一个。**
/// **把两者摊平成同一个数组就得在解码时先猜类型**——那是错的：类型是模型自己声明的，不是从内容猜出来的。
///
/// Syncs. **The reason the code keeps two normalised arrays:** an ONNX tensor may store its payload either as
/// `float_data` or as `int64_data`, **and the dtype decides which one to read.** **Flattening both into one array forces the decoder to guess the type,
/// which is wrong: the model declares it rather than it being inferred from the bytes.**
/// </remarks>
public sealed class OnnxTensorConstant
{
    /// <summary>构造常量张量 / Construct a constant tensor.</summary>
    /// <param name="name">张量名 / The tensor name.</param>
    /// <param name="dtype">数据类型 / The data type.</param>
    /// <param name="shape">形状 / The shape.</param>
    /// <param name="doubles">浮点载荷 / The floating payload.</param>
    /// <param name="longs">整数载荷 / The integer payload.</param>
    public OnnxTensorConstant(
        string? name,
        OnnxDataType dtype,
        IReadOnlyList<int> shape,
        IReadOnlyList<double> doubles,
        IReadOnlyList<long> longs) {
        Name = name;
        DType = dtype;
        Shape = shape ?? Array.Empty<int>();
        Doubles = doubles ?? Array.Empty<double>();
        Longs = longs ?? Array.Empty<long>();
    }

    /// <summary>张量名 / The tensor name.</summary>
    public string? Name { get; }

    /// <summary>数据类型 / The data type.</summary>
    public OnnxDataType DType { get; }

    /// <summary>形状 / The shape.</summary>
    public IReadOnlyList<int> Shape { get; }

    /// <summary>浮点载荷 / The floating payload.</summary>
    public IReadOnlyList<double> Doubles { get; }

    /// <summary>整数载荷 / The integer payload.</summary>
    public IReadOnlyList<long> Longs { get; }

    /// <summary>元素个数 / The element count.</summary>
    /// <remarks>
    /// **标量（空 shape）的元素个数是 1，不是 0：** **空 shape 表示"零维"，而零维张量恰好有一个元素。**
    /// 把 `fold` 的初值当作结果会让标量张量看起来是空的。`shape.fold(1) { acc, dim -> acc * dim }` 正是这个意思。
    ///
    /// **A scalar (empty shape) has one element, not zero: an empty shape means zero-dimensional, and a zero-dimensional tensor holds exactly one element.**
    /// Treating the fold's seed as the answer would make scalar tensors look empty. The code's `shape.fold(1) { acc, dim -> acc * dim }` says the same.
    /// </remarks>
    public int ElementCount => Shape.Aggregate(1, (accumulated, dimension) => accumulated * dimension);

    /// <summary>
    /// 读第 `index` 个元素为浮点 / Read element `index` as a floating value.
    /// </summary>
    /// <param name="index">展平索引 / The flattened index.</param>
    /// <returns>浮点值 / The floating value.</returns>
    /// <remarks>
    /// **整数载荷转浮点是显式转换，不是 memcpy：** **`long` 到 `double` 在超过 2^53 后会丢精度**，这是 ONNX 模型里恰好可能出现的整型常量
    /// （例如大索引），**而丢精度后的值仍然"看起来正常"。** 判据 pin 住这个边界。
    ///
    /// **Converting an integer payload to floating point is an explicit conversion, not a memcpy: `long` to `double` loses precision past 2^53**, which is
    /// exactly the kind of integer constant an ONNX model may carry (a large index), **and the lossy value still looks normal.** A criterion pins that edge.
    /// </remarks>
    public double DoubleAt(int index) =>
        OnnxDataTypes.IsFloating(DType) ? Doubles[index] : Longs[index];

    /// <summary>
    /// 读第 `index` 个元素为整数 / Read element `index` as an integer.
    /// </summary>
    /// <param name="index">展平索引 / The flattened index.</param>
    /// <returns>整数值 / The integer value.</returns>
    public long LongAt(int index) =>
        OnnxDataTypes.IsFloating(DType) ? (long)Doubles[index] : Longs[index];

    /// <summary>
    /// 载荷长度是否与形状一致 / Whether the payload length agrees with the shape.
    /// </summary>
    /// <remarks>
    /// **长度与形状不一致必须报出来，不能在读取时才发现：** **短了会在最后一个元素上抛越界，长了会静默忽略尾部——**
    /// **后者尤其危险：多出来的数是模型的一部分，忽略它等于读错模型却不曾说。**
    ///
    /// **A length that disagrees with the shape has to be reported rather than discovered at read time: short throws on the last element, long silently drops the
    /// tail** — **the latter is the dangerous one, since the extra numbers belong to the model and ignoring them means reading it wrong without saying so.**
    /// </remarks>
    /// <param name="detail">不一致时的说明 / The explanation when they disagree.</param>
    /// <returns>是否一致 / Whether they agree.</returns>
    public bool PayloadMatchesShape(out string? detail) {
        int expected = ElementCount;
        int actual = OnnxDataTypes.IsFloating(DType) ? Doubles.Count : Longs.Count;
        if (expected == actual) {
            detail = null;
            return true;
        }
        detail = "载荷长度与形状不一致 / payload length " + actual + " does not match shape " + Render(Shape)
                 + " (" + expected + " elements)";
        return false;
    }

    private static string Render(IReadOnlyList<int> shape) => "[" + string.Join(", ", shape) + "]";
}