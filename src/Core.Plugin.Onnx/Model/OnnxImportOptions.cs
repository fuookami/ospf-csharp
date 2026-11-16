#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Model;

/// <summary>
/// **导入模式（决定允许的最高多项式阶数）** / The import mode, which fixes the highest polynomial degree allowed.
/// </summary>
public enum OnnxImportMode
{
    /// <summary>线性 / Linear.</summary>
    Linear = 1,
    /// <summary>二次 / Quadratic.</summary>
    Quadratic = 2,
    /// <summary>三次 / Cubic = 3.</summary>
    Cubic = 3,
}

/// <summary>
/// **输入变量的取值区间** / The value range of one input variable.
/// </summary>
/// <param name="Lower">下界 / The lower bound.</param>
/// <param name="Upper">上界 / The upper bound.</param>
public sealed record OnnxBounds(double Lower, double Upper)
{
    /// <summary>
    /// 区间是否可用 / Whether the range is usable.
    /// </summary>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>是否可用 / Whether it is usable.</returns>
    /// <remarks>
    /// **非有限或倒挂的区间必须在导入时拒绝，而不是"留给求解器"：** **求解器拿到 `[5, 1]` 通常直接报"不可行"，**
    /// **而真实原因在选项里，离报错点隔着一整个模型。**
    ///
    /// **A non-finite or inverted range has to be refused at import rather than left to the solver: a solver handed `[5, 1]` usually reports "infeasible", while the
    /// real cause sits in the options, a whole model away from where the error appears.**
    /// </remarks>
    public bool Validate(out string? failure) {
        if (double.IsNaN(Lower) || double.IsNaN(Upper) || double.IsInfinity(Lower) || double.IsInfinity(Upper)) {
            failure = "输入区间不是有限值 / the bounds are not finite";
            return false;
        }
        if (Lower > Upper) {
            failure = "输入区间下界 " + Lower + " 大于上界 " + Upper + " / the lower bound exceeds the upper bound";
            return false;
        }
        failure = null;
        return true;
    }
}

/// <summary>
/// **导入选项** / The import options.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxImportOptions`。**这些选项不是"调参"，而是"这个模型能不能被表达"的边界：**
/// **`mode` 决定多项式阶数上限、`inputBounds` 决定分段近似能不能做、`supportedOpset` 决定算子语义是否与我们实现的一致。**
/// **每一条错了都表现为"模型跑不通"或更糟——"跑通了但是另一个模型"。**
///
/// Syncs `OnnxImportOptions`. **These are not tuning knobs but the boundary of what can be expressed at all: `mode` fixes the degree ceiling, `inputBounds` decides
/// whether piecewise approximation can happen, and `supportedOpset` decides whether operator semantics match what this plugin implements. Getting one wrong shows
/// up as "the model does not run" or worse, "it runs and it is a different model".**
/// </remarks>
public sealed class OnnxImportOptions
{
    /// <summary>默认分段数 / The default segment count.</summary>
    public const int DefaultSegments = 8;

    /// <summary>默认支持的 opset 区间下界 / The default lower bound of the supported opset range.</summary>
    public const int DefaultMinOpset = 13;

    /// <summary>默认支持的 opset 区间上界 / The default upper bound of the supported opset range.</summary>
    public const int DefaultMaxOpset = 24;

    private readonly Dictionary<string, OnnxBounds> _inputBounds;
    private readonly Dictionary<string, IReadOnlyList<long>> _inputShapes;

    /// <summary>构造选项 / Construct the options.</summary>
    /// <param name="mode">导入模式 / The import mode.</param>
    /// <param name="segments">分段近似的段数 / The segment count.</param>
    /// <param name="inputBounds">输入区间 / The input bounds.</param>
    /// <param name="inputShapes">输入形状 / The input shapes.</param>
    /// <param name="minOpset">支持的 opset 下界 / The supported opset lower bound.</param>
    /// <param name="maxOpset">支持的 opset 上界 / The supported opset upper bound.</param>
    public OnnxImportOptions(
        OnnxImportMode mode = OnnxImportMode.Linear,
        int segments = DefaultSegments,
        IReadOnlyDictionary<string, OnnxBounds>? inputBounds = null,
        IReadOnlyDictionary<string, IReadOnlyList<long>>? inputShapes = null,
        int minOpset = DefaultMinOpset,
        int maxOpset = DefaultMaxOpset) {
        Mode = mode;
        Segments = segments;
        InputBounds = new ReadOnlyDictionary<string, OnnxBounds>(
            inputBounds is null ? new Dictionary<string, OnnxBounds>() : new Dictionary<string, OnnxBounds>(inputBounds));
        InputShapes = new ReadOnlyDictionary<string, IReadOnlyList<long>>(
            inputShapes is null ? new Dictionary<string, IReadOnlyList<long>>() : new Dictionary<string, IReadOnlyList<long>>(inputShapes));
        MinOpset = minOpset;
        MaxOpset = maxOpset;
        _inputBounds = new Dictionary<string, OnnxBounds>(InputBounds);
        _inputShapes = new Dictionary<string, IReadOnlyList<long>>(InputShapes);
    }

    /// <summary>导入模式 / The import mode.</summary>
    public OnnxImportMode Mode { get; }

    /// <summary>分段数 / The segment count.</summary>
    public int Segments { get; }

    /// <summary>输入区间 / The input bounds.</summary>
    public IReadOnlyDictionary<string, OnnxBounds> InputBounds { get; }

    /// <summary>输入形状 / The input shapes.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<long>> InputShapes { get; }

    /// <summary>支持的 opset 下界 / The supported opset lower bound.</summary>
    public int MinOpset { get; }

    /// <summary>支持的 opset 上界 / The supported opset upper bound.</summary>
    public int MaxOpset { get; }

    /// <summary>允许的多项式阶数 / The allowed polynomial degree.</summary>
    public int MaxDegree => (int)Mode;

    /// <summary>
    /// 校验选项自身 / Validate the options themselves.
    /// </summary>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>是否可用 / Whether they are usable.</returns>
    public bool Validate(out string? failure) {
        if (Segments < 2 || Segments > 1024) {
            failure = "分段数 " + Segments + " 超出允许区间 [2, 1024] / the segment count is out of range";
            return false;
        }
        if (MinOpset > MaxOpset) {
            failure = "opset 区间 " + MinOpset + ".." + MaxOpset + " 倒挂 / the opset range is inverted";
            return false;
        }
        foreach (KeyValuePair<string, OnnxBounds> entry in _inputBounds) {
            if (string.IsNullOrEmpty(entry.Key)) {
                failure = "输入区间缺少名称 / a bound entry has no name";
                return false;
            }
            if (!entry.Value.Validate(out string? boundFailure)) {
                failure = "输入 '" + entry.Key + "' 的" + boundFailure;
                return false;
            }
        }
        foreach (KeyValuePair<string, IReadOnlyList<long>> entry in _inputShapes) {
            if (string.IsNullOrEmpty(entry.Key)) {
                failure = "输入形状缺少名称 / a shape entry has no name";
                return false;
            }
            foreach (long dimension in entry.Value) {
                if (dimension <= 0) {
                    failure = "输入 '" + entry.Key + "' 的形状含非正维度 " + dimension + " / the shape contains a non-positive dimension";
                    return false;
                }
            }
        }
        failure = null;
        return true;
    }

    /// <summary>
    /// 校验一张图的 opset 是否在支持区间内 / Check a graph's opset against the supported range.
    /// </summary>
    /// <param name="version">图的 opset / The graph's opset.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>是否支持 / Whether it is supported.</returns>
    public bool SupportsOpset(long version, out string? failure) {
        if (version < MinOpset || version > MaxOpset) {
            failure = "opset " + version + " 不在支持区间 [" + MinOpset + ", " + MaxOpset + "] 内 / the opset is outside the supported range";
            return false;
        }
        failure = null;
        return true;
    }
}