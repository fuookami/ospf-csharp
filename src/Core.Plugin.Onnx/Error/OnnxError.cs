#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Error;

/// <summary>
/// **ONNX 插件的结构化错误** / The ONNX plugin's structured errors.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxError.kt`（13 个变体）。**与"随便抛个异常"的差别在于上下文：**
/// **节点名、张量名、形状三者能拼回一条可定位的消息**——**一个只写"推理失败"的错误，在真实模型上等于没有信息。**
///
/// 每个变体携带自己的 `ErrorCode`，让调用方既能按类别分派、也能取回结构化对象。
///
/// Syncs `OnnxError.kt` (13 variants). **The difference from "just throw" is context: node name, tensor name and shape are enough to reconstruct a locatable
/// message** — **an error that only says "inference failed" carries no information on a real model.** Each variant carries its own `ErrorCode` so a caller can
/// dispatch by class and still recover the structured object.
/// </remarks>
public abstract class OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="errorCode">错误码 / The error code.</param>
    /// <param name="message">消息 / The message.</param>
    /// <param name="nodeName">节点名 / The node name.</param>
    /// <param name="tensorName">张量名 / The tensor name.</param>
    /// <param name="shape">形状 / The shape.</param>
    protected OnnxError(
        ErrorCode errorCode,
        string message,
        string? nodeName = null,
        string? tensorName = null,
        IReadOnlyList<long?>? shape = null) {
        ErrorCode = errorCode;
        Message = message;
        NodeName = nodeName;
        TensorName = tensorName;
        Shape = shape;
    }

    /// <summary>错误码 / The error code.</summary>
    public ErrorCode ErrorCode { get; }

    /// <summary>消息 / The message.</summary>
    public string Message { get; }

    /// <summary>节点名 / The node name.</summary>
    public string? NodeName { get; }

    /// <summary>张量名 / The tensor name.</summary>
    public string? TensorName { get; }

    /// <summary>形状（null 表示未知维度）/ The shape, with null for unknown dimensions.</summary>
    public IReadOnlyList<long?>? Shape { get; }

    /// <summary>
    /// 拼接上下文后的完整消息 / The full message with context appended.
    /// </summary>
    /// <remarks>
    /// **上下文只在存在时出现：** **`node=null` 这种占位比不写更糟——**它看起来像一条线索，实际什么也没说。**
    ///
    /// **Context appears only when present: a placeholder like `node=null` is worse than absent** — it reads like a lead while saying nothing.
    /// </remarks>
    /// <returns>完整消息 / The full message.</returns>
    public string FullMessage() {
        var context = new List<string>(3);
        if (NodeName is not null) {
            context.Add("node=" + NodeName);
        }
        if (TensorName is not null) {
            context.Add("tensor=" + TensorName);
        }
        if (Shape is not null) {
            context.Add("shape=[" + string.Join(", ", Shape.Select(dimension => dimension?.ToString() ?? "?")) + "]");
        }
        return context.Count == 0 ? Message : Message + " (" + string.Join(", ", context) + ")";
    }

    /// <inheritdoc/>
    public override string ToString() => FullMessage();
}

/// <summary>模型加载失败（IO、文件不存在、protobuf 解码异常）/ Model loading failed.</summary>
public sealed class OnnxLoadFailed : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="detail">底层原因 / The underlying reason.</param>
    /// <param name="tensorName">张量名 / The tensor name.</param>
    public OnnxLoadFailed(string detail, string? tensorName = null)
        : base(ErrorCode.DeserializationFailed, "ONNX 模型加载失败 / Failed to load ONNX model: " + detail, tensorName: tensorName) {
        Detail = detail;
    }

    /// <summary>底层原因 / The underlying reason.</summary>
    public string Detail { get; }
}

/// <summary>模型结构非法 / The model structure is invalid.</summary>
public sealed class OnnxInvalidModel : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="detail">底层原因 / The underlying reason.</param>
    /// <param name="nodeName">节点名 / The node name.</param>
    public OnnxInvalidModel(string detail, string? nodeName = null)
        : base(ErrorCode.IllegalArgument, "ONNX 模型结构非法 / Invalid ONNX model: " + detail, nodeName: nodeName) {
        Detail = detail;
    }

    /// <summary>底层原因 / The underlying reason.</summary>
    public string Detail { get; }
}

/// <summary>不支持的 opset 版本 / Unsupported opset version.</summary>
public sealed class OnnxUnsupportedOpset : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="version">实际版本 / The actual version.</param>
    /// <param name="supports">支持的版本 / The supported versions.</param>
    public OnnxUnsupportedOpset(long version, IReadOnlyList<long> supports)
        : base(
            ErrorCode.IllegalArgument,
            "不支持的 ONNX opset " + version + " / Unsupported ONNX opset " + version + "; supports: " + string.Join(", ", supports)) {
        Version = version;
        Supports = supports;
    }

    /// <summary>实际版本 / The actual version.</summary>
    public long Version { get; }

    /// <summary>支持的版本 / The supported versions.</summary>
    public IReadOnlyList<long> Supports { get; }
}

/// <summary>不支持的算子 / Unsupported operator.</summary>
public sealed class OnnxUnsupportedOperator : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="opType">算子类型 / The operator type.</param>
    /// <param name="nodeName">节点名 / The node name.</param>
    public OnnxUnsupportedOperator(string opType, string? nodeName = null)
        : base(ErrorCode.IllegalArgument, "不支持的 ONNX 算子 / Unsupported ONNX operator: " + opType, nodeName: nodeName) {
        OpType = opType;
    }

    /// <summary>算子类型 / The operator type.</summary>
    public string OpType { get; }
}

/// <summary>不支持的数据类型 / Unsupported data type.</summary>
public sealed class OnnxUnsupportedDType : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="protoId">类型编号 / The type id.</param>
    /// <param name="tensorName">张量名 / The tensor name.</param>
    public OnnxUnsupportedDType(int protoId, string? tensorName = null)
        : base(ErrorCode.IllegalArgument, "不支持的 ONNX 数据类型 / Unsupported ONNX data type id: " + protoId, tensorName: tensorName) {
        ProtoId = protoId;
    }

    /// <summary>类型编号 / The type id.</summary>
    public int ProtoId { get; }
}

/// <summary>动态形状不可静态降阶 / A dynamic shape cannot be lowered statically.</summary>
public sealed class OnnxDynamicShape : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="detail">底层原因 / The underlying reason.</param>
    /// <param name="tensorName">张量名 / The tensor name.</param>
    /// <param name="shape">形状 / The shape.</param>
    public OnnxDynamicShape(string detail, string? tensorName = null, IReadOnlyList<long?>? shape = null)
        : base(ErrorCode.IllegalArgument, "动态形状无法静态降阶 / Dynamic shape cannot be lowered: " + detail, tensorName: tensorName, shape: shape) {
        Detail = detail;
    }

    /// <summary>底层原因 / The underlying reason.</summary>
    public string Detail { get; }
}

/// <summary>形状不匹配 / Shape mismatch.</summary>
public sealed class OnnxShapeMismatch : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="expected">期望形状 / The expected shape.</param>
    /// <param name="actual">实际形状 / The actual shape.</param>
    /// <param name="nodeName">节点名 / The node name.</param>
    /// <param name="tensorName">张量名 / The tensor name.</param>
    public OnnxShapeMismatch(
        IReadOnlyList<long?> expected,
        IReadOnlyList<long?> actual,
        string? nodeName = null,
        string? tensorName = null)
        : base(
            ErrorCode.IllegalArgument,
            "形状不匹配 / Shape mismatch: expected " + Render(expected) + ", actual " + Render(actual),
            nodeName: nodeName,
            tensorName: tensorName,
            shape: actual) {
        Expected = expected;
        Actual = actual;
    }

    /// <summary>期望形状 / The expected shape.</summary>
    public IReadOnlyList<long?> Expected { get; }

    /// <summary>实际形状 / The actual shape.</summary>
    public IReadOnlyList<long?> Actual { get; }

    private static string Render(IReadOnlyList<long?> shape) =>
        "[" + string.Join(", ", shape.Select(dimension => dimension?.ToString() ?? "?")) + "]";
}

/// <summary>绑定不匹配 / Binding mismatch.</summary>
public sealed class OnnxBindingMismatch : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="detail">底层原因 / The underlying reason.</param>
    /// <param name="tensorName">张量名 / The tensor name.</param>
    public OnnxBindingMismatch(string detail, string? tensorName = null)
        : base(ErrorCode.IllegalArgument, "张量绑定不匹配 / Tensor binding mismatch: " + detail, tensorName: tensorName) {
        Detail = detail;
    }

    /// <summary>底层原因 / The underlying reason.</summary>
    public string Detail { get; }
}

/// <summary>输出选择非法 / Invalid output selection.</summary>
public sealed class OnnxOutputSelection : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="detail">底层原因 / The underlying reason.</param>
    /// <param name="tensorName">张量名 / The tensor name.</param>
    public OnnxOutputSelection(string detail, string? tensorName = null)
        : base(ErrorCode.IllegalArgument, "输出选择非法 / Invalid output selection: " + detail, tensorName: tensorName) {
        Detail = detail;
    }

    /// <summary>底层原因 / The underlying reason.</summary>
    public string Detail { get; }
}

/// <summary>不支持的属性 / Unsupported attribute.</summary>
public sealed class OnnxUnsupportedAttribute : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="attributeName">属性名 / The attribute name.</param>
    /// <param name="nodeName">节点名 / The node name.</param>
    public OnnxUnsupportedAttribute(string attributeName, string? nodeName = null)
        : base(ErrorCode.IllegalArgument, "不支持的 ONNX 属性 / Unsupported ONNX attribute: " + attributeName, nodeName: nodeName) {
        AttributeName = attributeName;
    }

    /// <summary>属性名 / The attribute name.</summary>
    public string AttributeName { get; }
}

/// <summary>降阶阶数溢出 / The lowered degree overflows.</summary>
public sealed class OnnxDegreeOverflow : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="detail">底层原因 / The underlying reason.</param>
    /// <param name="nodeName">节点名 / The node name.</param>
    public OnnxDegreeOverflow(string detail, string? nodeName = null)
        : base(ErrorCode.IllegalArgument, "降阶阶数溢出 / Degree overflow: " + detail, nodeName: nodeName) {
        Detail = detail;
    }

    /// <summary>底层原因 / The underlying reason.</summary>
    public string Detail { get; }
}

/// <summary>缺少边界 / A bound is missing.</summary>
public sealed class OnnxMissingBound : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="detail">底层原因 / The underlying reason.</param>
    /// <param name="tensorName">张量名 / The tensor name.</param>
    public OnnxMissingBound(string detail, string? tensorName = null)
        : base(ErrorCode.IllegalArgument, "缺少边界 / Missing bound: " + detail, tensorName: tensorName) {
        Detail = detail;
    }

    /// <summary>底层原因 / The underlying reason.</summary>
    public string Detail { get; }
}

/// <summary>推理失败 / Inference failed.</summary>
public sealed class OnnxInferenceFailed : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="detail">底层原因 / The underlying reason.</param>
    /// <param name="nodeName">节点名 / The node name.</param>
    /// <param name="tensorName">张量名 / The tensor name.</param>
    public OnnxInferenceFailed(string detail, string? nodeName = null, string? tensorName = null)
        : base(ErrorCode.ApplicationFailed, "ONNX 推理失败 / ONNX inference failed: " + detail, nodeName: nodeName, tensorName: tensorName) {
        Detail = detail;
    }

    /// <summary>底层原因 / The underlying reason.</summary>
    public string Detail { get; }
}

/// <summary>资源已关闭 / The resource is already closed.</summary>
public sealed class OnnxResourceClosed : OnnxError
{
    /// <summary>构造错误 / Construct the error.</summary>
    /// <param name="detail">底层原因 / The underlying reason.</param>
    public OnnxResourceClosed(string detail)
        : base(ErrorCode.ApplicationFailed, "ONNX 资源已关闭 / ONNX resource is closed: " + detail) {
        Detail = detail;
    }

    /// <summary>底层原因 / The underlying reason.</summary>
    public string Detail { get; }
}