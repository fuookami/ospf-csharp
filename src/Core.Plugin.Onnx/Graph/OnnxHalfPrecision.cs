#nullable enable

namespace Fuookami.Ospf.Core.Plugin.Onnx.Graph;

/// <summary>
/// **半精度（binary16）位模式转 `double`** / IEEE 754 binary16 bit patterns to `double`.
/// </summary>
/// <remarks>
/// `OnnxHalfPrecision`。**为什么值得单独写一层：** **`TensorProto` 里 FP16 的常量是 2 字节位模式，'
/// **而它们几乎总是权重与初始值——** **读错一个的后果不是"报错"，是"模型能跑但偏差一点点"，**
/// **而那正是最难归因的一类错误。**
///
/// 实现按 IEEE 754 逐字段展开（符号 1 / 指数 5 / 尾数 10），**不借助 `BitConverter` 的 `Half`：** '
/// **`System.Half` 在 .NET 里可用，但显式展开让"次正规数"与"无穷/NaN"这两条路径都写在明面上，**
/// **而半精度的坑恰好全在这两条路径上。**
///
/// Syncs `OnnxHalfPrecision`. **Why a separate layer: an FP16 constant in a `TensorProto` is a two-byte bit pattern, and those are almost always weights and initialisers —
/// getting one wrong does not raise, it gives a model that runs and is slightly off, which is the hardest kind of error to attribute.**
///
/// The implementation expands the IEEE fields by hand (sign 1 / exponent 5 / mantissa 10) rather than leaning on `BitConverter`'s `Half`: **`System.Half` exists in .NET, but
/// expanding it explicitly puts subnormals and infinity/NaN on the face of the implementation, and half precision's traps live entirely on those two paths.**
/// </remarks>
public static class OnnxHalfPrecision
{
    /// <summary>
    /// 把一个 16 位位模式转成 `double` / Convert one 16-bit pattern to a double.
    /// </summary>
    /// <param name="bits">位模式 / The bit pattern.</param>
    /// <returns>对应的值（可能是无穷或 NaN）/ The value, possibly an infinity or NaN.</returns>
    public static double ToDouble(ushort bits) {
        int sign = (bits >> 15) & 0x1;
        int exponent = (bits >> 10) & 0x1F;
        int mantissa = bits & 0x3FF;

        double magnitude;
        if (exponent == 0) {
            // 次正规数：没有隐含的前导 1，值 = 尾数 × 2^-24。零也走这条路（尾数为 0）。
            // Subnormal: no implicit leading 1, value = mantissa × 2^-24. Zero takes this path too (mantissa 0).
            magnitude = mantissa * System.Math.Pow(2.0, -24);
        }
        else if (exponent == 0x1F) {
            // 无穷与 NaN：尾数为 0 是无穷，否则是 NaN。
            // Infinity and NaN: a zero mantissa is infinity, anything else is NaN.
            return mantissa == 0
                ? (sign == 0 ? double.PositiveInfinity : double.NegativeInfinity)
                : double.NaN;
        }
        else {
            magnitude = (1.0 + mantissa / 1024.0) * System.Math.Pow(2.0, exponent - 15);
        }
        return sign == 0 ? magnitude : -magnitude;
    }

    /// <summary>
    /// 把一个 16 位位模式读成整数或浮点 / Read a 16-bit pattern as an integer or a float.
    /// </summary>
    /// <param name="bits">位模式 / The bit pattern.</param>
    /// <returns>对应的值 / The value.</returns>
    public static double ToDouble(short bits) => ToDouble(unchecked((ushort)bits));
}