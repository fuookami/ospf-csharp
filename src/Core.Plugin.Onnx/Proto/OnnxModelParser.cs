#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Error;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Proto;

/// <summary>
/// **ONNX 模型结构的最小解析结果** / A minimal parse of the ONNX model structure.
/// </summary>
/// <remarks>
/// 这些记录只承载本插件真正要用的字段。**每个字段旁标着它在 `onnx.proto` 里的编号**——**这些数字是规范的一部分，
/// 写错了不会有任何运行时症状：** 字段被静默当成 wire type 不匹配而跳过，于是"模型里没有这个字段"，**而模型明明有。**
///
/// These records carry only the fields this plugin actually uses. **Each carries its `onnx.proto` field number: those numbers are part of the specification, and
/// getting one wrong has no runtime symptom at all** — the field is silently skipped as a wire-type mismatch, so it "is not in the model" while the model says
/// otherwise.
/// </remarks>
public sealed record OnnxOperatorSet(string Domain, long Version);

/// <summary>张量形状中的一维 / One dimension of a tensor shape.</summary>
/// <param name="Value">固定维度；符号维度为 null / The fixed dimension, or null when symbolic.</param>
/// <param name="Symbol">符号名（如 `batch`）/ The symbolic name (e.g. `batch`).</param>
public sealed record OnnxDimension(long? Value, string? Symbol);

/// <summary>值信息（图输入、输出或中间值）/ Value info (a graph input, output or intermediate).</summary>
/// <param name="Name">名称 / The name.</param>
/// <param name="ElementType">`TensorProto.DataType` 编号 / The `TensorProto.DataType` id.</param>
/// <param name="Shape">形状 / The shape.</param>
public sealed record OnnxValueInfo(string Name, int ElementType, IReadOnlyList<OnnxDimension> Shape);

/// <summary>属性值 / An attribute value.</summary>
/// <param name="Name">属性名 / The attribute name.</param>
/// <param name="Float">浮点标量 / The float scalar.</param>
/// <param name="Integer">整数标量 / The integer scalar.</param>
/// <param name="Text">字符串标量 / The string scalar.</param>
/// <param name="Floats">浮点列表 / The float list.</param>
/// <param name="Integers">整数列表 / The integer list.</param>
/// <param name="Texts">字符串列表 / The string list.</param>
/// <param name="Tensor">常量张量 / The constant tensor.</param>
/// <param name="Kind">`AttributeProto.AttributeType` 编号 / The `AttributeProto.AttributeType` id.</param>
public sealed record OnnxAttribute(
    string Name,
    float? Float,
    long? Integer,
    string? Text,
    IReadOnlyList<float>? Floats,
    IReadOnlyList<long>? Integers,
    IReadOnlyList<string>? Texts,
    OnnxTensor? Tensor,
    int Kind);

/// <summary>常量张量 / A constant tensor.</summary>
/// <param name="Name">名称 / The name.</param>
/// <param name="DataType">`TensorProto.DataType` 编号 / The `TensorProto.DataType` id.</param>
/// <param name="Dims">维度 / The dimensions.</param>
/// <param name="FloatData">`float_data` / The `float_data` payload.</param>
/// <param name="Int64Data">`int64_data` / The `int64_data` payload.</param>
/// <param name="DoubleData">`double_data` / The `double_data` payload.</param>
/// <param name="RawData">`raw_data` / The `raw_data` payload.</param>
public sealed record OnnxTensor(
    string? Name,
    int DataType,
    IReadOnlyList<long> Dims,
    IReadOnlyList<float> FloatData,
    IReadOnlyList<long> Int64Data,
    IReadOnlyList<double> DoubleData,
    byte[]? RawData);

/// <summary>图中的节点 / A node in the graph.</summary>
/// <param name="OpType">`NodeProto.op_type` / The operator type.</param>
/// <param name="Name">`NodeProto.name`（可空）/ The node name (nullable).</param>
/// <param name="Inputs">`NodeProto.input` / The inputs.</param>
/// <param name="Outputs">`NodeProto.output` / The outputs.</param>
/// <param name="Attributes">`NodeProto.attribute` / The attributes.</param>
public sealed record OnnxNode(
    string OpType,
    string? Name,
    IReadOnlyList<string> Inputs,
    IReadOnlyList<string> Outputs,
    IReadOnlyList<OnnxAttribute> Attributes);

/// <summary>计算图 / A computation graph.</summary>
/// <param name="Name">图名 / The graph name.</param>
/// <param name="Nodes">节点 / The nodes.</param>
/// <param name="Initializers">初始化张量 / The initialisers.</param>
/// <param name="Inputs">输入 / The inputs.</param>
/// <param name="Outputs">输出 / The outputs.</param>
/// <param name="ValueInfos">中间值信息 / The intermediate value infos.</param>
public sealed record OnnxGraph(
    string? Name,
    IReadOnlyList<OnnxNode> Nodes,
    IReadOnlyList<OnnxTensor> Initializers,
    IReadOnlyList<OnnxValueInfo> Inputs,
    IReadOnlyList<OnnxValueInfo> Outputs,
    IReadOnlyList<OnnxValueInfo> ValueInfos);

/// <summary>模型 / The model.</summary>
/// <param name="IrVersion">`ModelProto.ir_version` / The IR version.</param>
/// <param name="ProducerName">`ModelProto.producer_name` / The producer name.</param>
/// <param name="Opsets">`ModelProto.opset_import` / The imported operator sets.</param>
/// <param name="Graph">`ModelProto.graph` / The graph.</param>
public sealed record OnnxModel(
    long IrVersion,
    string? ProducerName,
    IReadOnlyList<OnnxOperatorSet> Opsets,
    OnnxGraph? Graph);

/// <summary>
/// **按 `onnx.proto` 的字段号解析模型** / Parse a model by the field numbers in `onnx.proto`.
/// </summary>
public static class OnnxModelParser
{
    /// <summary>
    /// 解析整个模型 / Parse a whole model.
    /// </summary>
    /// <param name="bytes">模型字节 / The model bytes.</param>
    /// <returns>模型 / The model.</returns>
    public static OnnxModel Parse(byte[] bytes) {
        var reader = new ProtoReader(bytes);
        long irVersion = 0;
        string? producerName = null;
        var opsets = new List<OnnxOperatorSet>();
        OnnxGraph? graph = null;

        while (reader.TryReadTag(out int field, out int wire)) {
            switch (field) {
                case 1: // ModelProto.ir_version
                    irVersion = (long)reader.ReadVarint();
                    break;
                case 2: // ModelProto.producer_name
                    producerName = reader.ReadString();
                    break;
                case 7: // ModelProto.graph
                    graph = ParseGraph(reader.ReadBytes());
                    break;
                case 8: // ModelProto.opset_import
                    opsets.Add(ParseOperatorSet(reader.ReadBytes()));
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }
        return new OnnxModel(irVersion, producerName, opsets, graph);
    }

    /// <summary>解析 `OperatorSetIdProto` / Parse an `OperatorSetIdProto`.</summary>
    /// <param name="bytes">字节 / The bytes.</param>
    /// <returns>算子集 / The operator set.</returns>
    public static OnnxOperatorSet ParseOperatorSet(byte[] bytes) {
        var reader = new ProtoReader(bytes);
        string domain = string.Empty;
        long version = 0;
        while (reader.TryReadTag(out int field, out int wire)) {
            switch (field) {
                case 1: // OperatorSetIdProto.domain
                    domain = reader.ReadString();
                    break;
                case 2: // OperatorSetIdProto.version
                    version = (long)reader.ReadVarint();
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }
        return new OnnxOperatorSet(domain, version);
    }

    /// <summary>解析 `GraphProto` / Parse a `GraphProto`.</summary>
    /// <param name="bytes">字节 / The bytes.</param>
    /// <returns>图 / The graph.</returns>
    public static OnnxGraph ParseGraph(byte[] bytes) {
        var reader = new ProtoReader(bytes);
        string? name = null;
        var nodes = new List<OnnxNode>();
        var initializers = new List<OnnxTensor>();
        var inputs = new List<OnnxValueInfo>();
        var outputs = new List<OnnxValueInfo>();
        var valueInfos = new List<OnnxValueInfo>();

        while (reader.TryReadTag(out int field, out int wire)) {
            switch (field) {
                case 1: // GraphProto.node
                    nodes.Add(ParseNode(reader.ReadBytes()));
                    break;
                case 2: // GraphProto.name
                    name = reader.ReadString();
                    break;
                case 5: // GraphProto.initializer
                    initializers.Add(ParseTensor(reader.ReadBytes()));
                    break;
                case 11: // GraphProto.input
                    inputs.Add(ParseValueInfo(reader.ReadBytes()));
                    break;
                case 12: // GraphProto.output
                    outputs.Add(ParseValueInfo(reader.ReadBytes()));
                    break;
                case 13: // GraphProto.value_info
                    valueInfos.Add(ParseValueInfo(reader.ReadBytes()));
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }
        return new OnnxGraph(name, nodes, initializers, inputs, outputs, valueInfos);
    }

    /// <summary>解析 `NodeProto` / Parse a `NodeProto`.</summary>
    /// <param name="bytes">字节 / The bytes.</param>
    /// <returns>节点 / The node.</returns>
    public static OnnxNode ParseNode(byte[] bytes) {
        var reader = new ProtoReader(bytes);
        string opType = string.Empty;
        string? name = null;
        var inputs = new List<string>();
        var outputs = new List<string>();
        var attributes = new List<OnnxAttribute>();

        while (reader.TryReadTag(out int field, out int wire)) {
            switch (field) {
                case 1: // NodeProto.input
                    inputs.Add(reader.ReadString());
                    break;
                case 2: // NodeProto.output
                    outputs.Add(reader.ReadString());
                    break;
                case 3: // NodeProto.name
                    name = reader.ReadString();
                    break;
                case 4: // NodeProto.op_type
                    opType = reader.ReadString();
                    break;
                case 5: // NodeProto.attribute
                    attributes.Add(ParseAttribute(reader.ReadBytes()));
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }
        return new OnnxNode(opType, name, inputs, outputs, attributes);
    }

    /// <summary>解析 `AttributeProto` / Parse an `AttributeProto`.</summary>
    /// <param name="bytes">字节 / The bytes.</param>
    /// <returns>属性 / The attribute.</returns>
    public static OnnxAttribute ParseAttribute(byte[] bytes) {
        var reader = new ProtoReader(bytes);
        string name = string.Empty;
        float? floatValue = null;
        long? integerValue = null;
        string? textValue = null;
        List<float>? floats = null;
        List<long>? integers = null;
        List<string>? texts = null;
        OnnxTensor? tensor = null;
        int kind = 0;

        while (reader.TryReadTag(out int field, out int wire)) {
            switch (field) {
                case 1: // AttributeProto.name
                    name = reader.ReadString();
                    break;
                case 2: // AttributeProto.f —— wire type 5（定长 32 位浮点）
                    floatValue = BitConverter.Int32BitsToSingle((int)reader.ReadFixed32());
                    break;
                case 3: // AttributeProto.i
                    integerValue = (long)reader.ReadVarint();
                    break;
                case 4: // AttributeProto.s
                    textValue = reader.ReadString();
                    break;
                case 5: // AttributeProto.t
                    tensor = ParseTensor(reader.ReadBytes());
                    break;
                case 7: // AttributeProto.floats（packed）
                    floats = wire == 2 ? reader.ReadPackedFloat() : new List<float> { BitConverter.Int32BitsToSingle((int)reader.ReadFixed32()) };
                    break;
                case 8: // AttributeProto.ints（packed）
                    integers = wire == 2 ? reader.ReadPackedVarint() : new List<long> { (long)reader.ReadVarint() };
                    break;
                case 9: // AttributeProto.strings
                    texts ??= new List<string>();
                    texts.Add(reader.ReadString());
                    break;
                case 20: // AttributeProto.type
                    kind = (int)reader.ReadVarint();
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }
        return new OnnxAttribute(name, floatValue, integerValue, textValue, floats, integers, texts, tensor, kind);
    }

    /// <summary>解析 `TensorProto` / Parse a `TensorProto`.</summary>
    /// <param name="bytes">字节 / The bytes.</param>
    /// <returns>张量 / The tensor.</returns>
    public static OnnxTensor ParseTensor(byte[] bytes) {
        var reader = new ProtoReader(bytes);
        string? name = null;
        int dataType = 0;
        var dims = new List<long>();
        List<float> floatData = new();
        List<long> int64Data = new();
        List<double> doubleData = new();
        byte[]? rawData = null;

        while (reader.TryReadTag(out int field, out int wire)) {
            switch (field) {
                case 1: // TensorProto.dims（packed 或逐个）
                    if (wire == 2) {
                        dims.AddRange(reader.ReadPackedVarint());
                    }
                    else {
                        dims.Add((long)reader.ReadVarint());
                    }
                    break;
                case 2: // TensorProto.data_type
                    dataType = (int)reader.ReadVarint();
                    break;
                case 4: // TensorProto.float_data（packed fixed32）
                    floatData.AddRange(reader.ReadPackedFloat());
                    break;
                case 7: // TensorProto.int64_data（packed varint）
                    int64Data.AddRange(reader.ReadPackedVarint());
                    break;
                case 8: // TensorProto.name
                    name = reader.ReadString();
                    break;
                case 9: // TensorProto.raw_data
                    rawData = reader.ReadBytes();
                    break;
                case 10: // TensorProto.double_data（packed fixed64）
                    if (wire == 2) {
                        byte[] payload = reader.ReadBytes();
                        for (int offset = 0; offset + 8 <= payload.Length; offset += 8) {
                            doubleData.Add(BitConverter.Int64BitsToDouble(BitConverter.ToInt64(payload, offset)));
                        }
                    }
                    else {
                        doubleData.Add(BitConverter.Int64BitsToDouble((long)reader.ReadFixed64()));
                    }
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }
        return new OnnxTensor(name, dataType, dims, floatData, int64Data, doubleData, rawData);
    }

    /// <summary>解析 `ValueInfoProto` / Parse a `ValueInfoProto`.</summary>
    /// <param name="bytes">字节 / The bytes.</param>
    /// <returns>值信息 / The value info.</returns>
    public static OnnxValueInfo ParseValueInfo(byte[] bytes) {
        var reader = new ProtoReader(bytes);
        string name = string.Empty;
        int elementType = 0;
        var shape = new List<OnnxDimension>();

        while (reader.TryReadTag(out int field, out int wire)) {
            switch (field) {
                case 1: // ValueInfoProto.name
                    name = reader.ReadString();
                    break;
                case 2: // ValueInfoProto.type
                    ParseType(reader.ReadBytes(), out elementType, shape);
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }
        return new OnnxValueInfo(name, elementType, shape);
    }

    private static void ParseType(byte[] bytes, out int elementType, List<OnnxDimension> shape) {
        var reader = new ProtoReader(bytes);
        elementType = 0;
        while (reader.TryReadTag(out int field, out int wire)) {
            switch (field) {
                case 1: // TypeProto.tensor_type
                    ParseTensorType(reader.ReadBytes(), out elementType, shape);
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }
    }

    private static void ParseTensorType(byte[] bytes, out int elementType, List<OnnxDimension> shape) {
        var reader = new ProtoReader(bytes);
        elementType = 0;
        while (reader.TryReadTag(out int field, out int wire)) {
            switch (field) {
                case 1: // TypeProto.Tensor.elem_type
                    elementType = (int)reader.ReadVarint();
                    break;
                case 2: // TypeProto.Tensor.shape
                    ParseShape(reader.ReadBytes(), shape);
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }
    }

    private static void ParseShape(byte[] bytes, List<OnnxDimension> shape) {
        var reader = new ProtoReader(bytes);
        while (reader.TryReadTag(out int field, out int wire)) {
            if (field == 1) { // TensorShapeProto.dim
                shape.Add(ParseDimension(reader.ReadBytes()));
            }
            else {
                reader.SkipField(wire);
            }
        }
    }

    private static OnnxDimension ParseDimension(byte[] bytes) {
        var reader = new ProtoReader(bytes);
        long? value = null;
        string? symbol = null;
        while (reader.TryReadTag(out int field, out int wire)) {
            switch (field) {
                case 1: // TensorShapeProto.Dimension.dim_value
                    value = (long)reader.ReadVarint();
                    break;
                case 2: // TensorShapeProto.Dimension.dim_param
                    symbol = reader.ReadString();
                    break;
                default:
                    reader.SkipField(wire);
                    break;
            }
        }
        return new OnnxDimension(value, symbol);
    }
}