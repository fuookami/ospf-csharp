#nullable enable

using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Graph;

/// <summary>
/// **ONNX 数据类型映射** / ONNX data type mapping.
/// </summary>
/// <remarks>
/// 。`protoId` 是 `TensorProto.DataType` 的枚举值，**必须与 ONNX 规范一致**——
/// 它不是我们自己的编号，写错会让读到的模型张量被当成另一种类型，**而那种错误不会抛异常，只会让数值悄悄变形**。
///
/// Synchronises from Kotlin `dee897d2`. `protoId` is the `TensorProto.DataType` enum value and **must match the ONNX specification**: it is
/// not our own numbering, and getting it wrong makes a tensor read as another type without throwing — the numbers just quietly change.
/// </remarks>
public enum OnnxDataType
{
    /// <summary>32 位浮点 / 32-bit float.</summary>
    Float32 = 1,
    /// <summary>8 位无符号整数 / 8-bit unsigned integer.</summary>
    UInt8 = 2,
    /// <summary>8 位有符号整数 / 8-bit signed integer.</summary>
    Int8 = 3,
    /// <summary>16 位无符号整数 / 16-bit unsigned integer.</summary>
    UInt16 = 4,
    /// <summary>16 位有符号整数 / 16-bit signed integer.</summary>
    Int16 = 5,
    /// <summary>32 位有符号整数 / 32-bit signed integer.</summary>
    Int32 = 6,
    /// <summary>64 位有符号整数 / 64-bit signed integer.</summary>
    Int64 = 7,
    /// <summary>布尔 / Boolean.</summary>
    Bool = 9,
    /// <summary>16 位浮点 / 16-bit float.</summary>
    Float16 = 10,
    /// <summary>64 位浮点 / 64-bit float.</summary>
    Double = 11,
    /// <summary>32 位无符号整数 / 32-bit unsigned integer.</summary>
    UInt32 = 12,
    /// <summary>64 位无符号整数 / 64-bit unsigned integer.</summary>
    UInt64 = 13,
    /// <summary>16 位脑浮点 / 16-bit brain float.</summary>
    BFloat16 = 16,
    /// <summary>字符串 / String.</summary>
    String = 8,
}

/// <summary>
/// `OnnxDataType` 的分类与查表 / Classification and lookup for <see cref="OnnxDataType"/>.
/// </summary>
public static class OnnxDataTypes
{
    /// <summary>
    /// 是否为参与算术的浮点类型 / Whether the type takes part in arithmetic as a floating type.
    /// </summary>
    /// <param name="type">数据类型 / The data type.</param>
    /// <returns>是否浮点 / Whether it is floating.</returns>
    public static bool IsFloating(OnnxDataType type) =>
        type is OnnxDataType.Float32 or OnnxDataType.Float16 or OnnxDataType.Double or OnnxDataType.BFloat16;

    /// <summary>
    /// 是否为整数类型 / Whether the type is an integer type.
    /// </summary>
    /// <param name="type">数据类型 / The data type.</param>
    /// <returns>是否整数 / Whether it is an integer.</returns>
    public static bool IsInteger(OnnxDataType type) =>
        type is OnnxDataType.UInt8 or OnnxDataType.Int8 or OnnxDataType.UInt16 or OnnxDataType.Int16
            or OnnxDataType.Int32 or OnnxDataType.Int64 or OnnxDataType.UInt32 or OnnxDataType.UInt64;

    /// <summary>
    /// 是否可以作为控制张量（布尔或整数）/ Whether the type may serve as a control tensor (boolean or integer).
    /// </summary>
    /// <param name="type">数据类型 / The data type.</param>
    /// <returns>是否可作为控制张量 / Whether it may serve as a control tensor.</returns>
    public static bool IsControl(OnnxDataType type) => type == OnnxDataType.Bool || IsInteger(type);

    /// <summary>
    /// 每个元素占用的字节数 / Bytes per element.
    /// </summary>
    /// <param name="type">数据类型 / The data type.</param>
    /// <returns>字节数；字符串返回 0（变长）/ The byte count; zero for variable-length strings.</returns>
    public static int ByteWidth(OnnxDataType type) => type switch {
        OnnxDataType.Float32 or OnnxDataType.Int32 or OnnxDataType.UInt32 => 4,
        OnnxDataType.Int64 or OnnxDataType.UInt64 or OnnxDataType.Double => 8,
        OnnxDataType.Float16 or OnnxDataType.BFloat16 or OnnxDataType.Int16 or OnnxDataType.UInt16 => 2,
        OnnxDataType.Int8 or OnnxDataType.UInt8 or OnnxDataType.Bool => 1,
        _ => 0,
    };

    /// <summary>
    /// 按 `TensorProto.DataType` 的枚举值反查 / Look a type up by its `TensorProto.DataType` value.
    /// </summary>
    /// <param name="protoId">枚举值 / The enum value.</param>
    /// <returns>数据类型；未知时为 null / The type, or null when unknown.</returns>
    /// <remarks>
    /// **返回 null 而不是猜一个默认值：** **一个未知的 `protoId` 意味着读到的是不认识的模型；**猜成 `Float32` 会让整张量被当成浮点读，
    /// 而它可能是 `Int64` 的索引表——**报"不认识"比报一个错的数好。**
    ///
    /// **Returns null instead of guessing: an unknown `protoId` means the model uses a type this code does not know.** **Guessing `Float32` would read the
    /// whole tensor as floating point while it may be an `Int64` index table — reporting "unknown" beats reporting a wrong number.**
    /// </remarks>
    public static OnnxDataType? FromProtoId(int protoId) =>
        ByProtoId.TryGetValue(protoId, out OnnxDataType type) ? type : null;

    /// <summary>可读名称 / The readable name.</summary>
    /// <param name="type">数据类型 / The data type.</param>
    /// <returns>名称 / The name.</returns>
    public static string DisplayName(OnnxDataType type) => type.ToString();

    private static readonly Dictionary<int, OnnxDataType> ByProtoId = new() {
        [1] = OnnxDataType.Float32,
        [8] = OnnxDataType.String,
        [2] = OnnxDataType.UInt8,
        [3] = OnnxDataType.Int8,
        [4] = OnnxDataType.UInt16,
        [5] = OnnxDataType.Int16,
        [6] = OnnxDataType.Int32,
        [7] = OnnxDataType.Int64,
        [9] = OnnxDataType.Bool,
        [10] = OnnxDataType.Float16,
        [11] = OnnxDataType.Double,
        [12] = OnnxDataType.UInt32,
        [13] = OnnxDataType.UInt64,
        [16] = OnnxDataType.BFloat16,
    };
}