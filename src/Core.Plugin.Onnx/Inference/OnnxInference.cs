#nullable enable

// IDE0007 与 IDE0008 会在同一个泛型局部变量上互相要求（一个说用 var、一个说用显式类型），
// 而这批代码里两处都触发。按仓库既有做法在文件级关闭这两条，理由记录在此。
//
// IDE0007 and IDE0008 demand opposite things on the same generic local, and both fire in this file. Disabled at file scope per the existing repository practice,
// with the reason recorded here.
#pragma warning disable IDE0007, IDE0008


using System;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Inference;

/// <summary>
/// **ONNX 推理（用官方运行时）** / ONNX inference through the official runtime.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxInference`。**这一层薄是有意的：** **推理本身由 `Microsoft.ML.OnnxRuntime` 承担，**
/// **而插件该负责的是"边界上的那几件事"——** **输入名与形状的核对、错误的转译、生命周期的收束。**
/// **把运行时再包一层抽象只会让"到底谁在读这个张量"变成第三个问题。**
///
/// Syncs `OnnxInference`. **The thinness is deliberate: inference itself belongs to the runtime, and what the plugin owes is the boundary work — checking input names
/// and shapes, translating errors, and closing lifetimes. Wrapping the runtime in another abstraction would only make "who actually reads this tensor" a third
/// question.**
/// </remarks>
public sealed class OnnxInference : IDisposable
{
    private readonly InferenceSession _session;
    private bool _disposed;

    private OnnxInference(InferenceSession session, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs) {
        _session = session;
        InputNames = inputs;
        OutputNames = outputs;
    }

    /// <summary>模型的输入名 / The model's input names.</summary>
    public IReadOnlyList<string> InputNames { get; }

    /// <summary>模型的输出名 / The model's output names.</summary>
    public IReadOnlyList<string> OutputNames { get; }

    /// <summary>
    /// 从模型字节加载 / Load from model bytes.
    /// </summary>
    /// <param name="modelBytes">模型字节 / The model bytes.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>推理会话；失败时为 null / The session, or null on failure.</returns>
    /// <remarks>
    /// **加载失败必须把运行时的原话带出来：** **"模型加载失败"这四个字对着一份损坏的 protobuf 毫无用处——**
    /// **而运行时的消息里通常有偏移量或算子名。**
    ///
    /// **A load failure has to carry the runtime's own words: "failed to load the model" is useless against a corrupted protobuf, while the runtime's message usually
    /// names an offset or an operator.**
    /// </remarks>
    public static OnnxInference? Load(byte[] modelBytes, out string? failure) {
        try {
            var session = new InferenceSession(modelBytes);
            var inputs = new List<string>(session.InputMetadata.Keys);
            var outputs = new List<string>(session.OutputMetadata.Keys);
            failure = null;
            return new OnnxInference(session, inputs, outputs);
        }
        catch (OnnxRuntimeException error) {
            failure = "ONNX 运行时拒绝了这份模型 / the runtime rejected the model: " + error.Message;
            return null;
        }
        catch (ArgumentException error) {
            failure = "模型字节不是合法的 ONNX / the bytes are not a valid ONNX model: " + error.Message;
            return null;
        }
    }

    /// <summary>
    /// 跑一次推理 / Run inference once.
    /// </summary>
    /// <param name="inputs">输入名到值的映射（`float`，按行主序展平）/ The mapping from input names to values, flattened row-major.</param>
    /// <param name="shapes">输入名到形状的映射 / The mapping from input names to shapes.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>输出名到值的映射；失败时为 null / The mapping from output names to values, or null on failure.</returns>
    /// <remarks>
    /// **输入名不在模型里时直接拒绝，不静默忽略：** **忽略一个输入会让模型用它自己的默认（通常没有），**
    /// **于是结果看着正常、其实缺了一项。**
    ///
    /// **An input name absent from the model is refused rather than ignored: ignoring one leaves the model with its own default (usually none), and the result looks
    /// normal while missing a term.**
    /// </remarks>
    public IReadOnlyDictionary<string, float[]>? Run(
        IReadOnlyDictionary<string, float[]> inputs,
        IReadOnlyDictionary<string, int[]> shapes,
        out string? failure) {
        if (_disposed) {
            failure = "推理会话已关闭 / the session is already disposed";
            return null;
        }
        foreach (string name in inputs.Keys) {
            if (!_session.InputMetadata.ContainsKey(name)) {
                failure = "输入 '" + name + "' 不在模型里（模型输入：" + string.Join(", ", InputNames) + "）/ the input is not part of the model";
                return null;
            }
        }
        foreach (string name in _session.InputMetadata.Keys) {
            if (!inputs.ContainsKey(name)) {
                failure = "模型输入 '" + name + "' 没有给出取值 / the model input was not supplied";
                return null;
            }
        }

        var tensors = new List<NamedOnnxValue>(inputs.Count);
        foreach (KeyValuePair<string, float[]> entry in inputs) {
            if (!shapes.TryGetValue(entry.Key, out int[]? shape)) {
                failure = "输入 '" + entry.Key + "' 缺少形状 / the input has no shape";
                return null;
            }
            int expected = 1;
            foreach (int dimension in shape) {
                expected *= dimension;
            }
            if (expected != entry.Value.Length) {
                failure = "输入 '" + entry.Key + "' 的取值有 " + entry.Value.Length + " 个，而形状需要 " + expected + " 个 / the value count disagrees with the shape";
                return null;
            }
            tensors.Add(NamedOnnxValue.CreateFromTensor(entry.Key, new DenseTensor<float>(entry.Value, shape)));
        }

        try {
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _session.Run(tensors);
            var outputs = new Dictionary<string, float[]>(StringComparer.Ordinal);
            foreach (DisposableNamedOnnxValue result in results) {
                DenseTensor<float> tensor = (DenseTensor<float>)result.AsTensor<float>();
                var copy = new float[tensor.Length];
                int index = 0;
                foreach (float value in tensor) {
                    copy[index++] = value;
                }
                outputs[result.Name] = copy;
            }
            failure = null;
            return outputs;
        }
        catch (OnnxRuntimeException error) {
            failure = "推理失败 / inference failed: " + error.Message;
            return null;
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _session.Dispose();
    }
}