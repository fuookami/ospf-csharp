#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// 延迟函数 fallback 的物化器 / A deferred-function fallback materializer.
/// </summary>
/// <remarks>
/// 对齐的 `DeferredFunctionFallbackMaterializer`。
///
/// **Following its `DeferredFunctionFallbackMaterializer`.**
/// </remarks>
public interface IDeferredFunctionFallbackMaterializer
{
    /// <summary>物化一个结构 / Materialize one structure.</summary>
    Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> Materialize(
        DeferredFunctionStructure structure);
}

/// <summary>
/// 延迟函数 fallback 的物化器集合 / The deferred-function fallback materializers.
/// </summary>
/// <remarks>
/// 对齐的六个物化器。
///
/// **失败有三种，而它们必须被分开：**
///
/// | 情况 | 消息 |
/// | --- | --- |
/// | **结构类型不在本物化器的范围内** | **`unsupported ... structure: X`** |
/// | **结构类型对，但行生成还没实现** | **`... row generation is not implemented yet: X`** |
/// | **结构类型对、行生成在，但那一层自己失败了** | **把内层的错误原样传出去** |
///
/// **把前两种合成一条消息，调用方就分不出"我用错了物化器"与"这个能力还没有"。
/// 前者是调用方的错，后者是本仓库的缺口——修法完全不同。**
///
/// **第三种不能折成任何一种：** **内层的失败带着具体原因（例如二值逻辑的输入非法），
/// 折成"不支持"会让那个原因消失。**
///
/// **此后七个结构都能真正生成行。**
///
/// | 结构 | 能物化吗 |
/// | --- | --- |
/// | `SemiStructure` | **能** |
/// | `BinaryLogicStructure` | **能** |
/// | `IndicatorStructure` | **能** |
/// | `MaskingStructure` | **能** |
/// | `UnivariateLinearPiecewiseStructure` | **能** |
/// | `AbsStructure` | **能** |
/// | `MaxStructure` | **能** |
///
/// **物化层的完整性<b>由行生成决定，而不是由分派表决定</b>。
/// 这句话值得写下来：分派表全绿而三个结构物化不了，是一个看起来完整、实际空着三格的层。**
///
/// **There are three failures, and they must stay apart:**
///
/// | Case | Message |
/// | --- | --- |
/// | **the structure type is outside this materializer's range** | **`unsupported ... structure: X`** |
/// | **the type is right but row generation is not implemented yet** | **`... row generation is not implemented yet: X`** |
/// | **the type is right and row generation exists but that layer failed** | **forward the inner error unchanged** |
///
/// **Collapsing the first two leaves the caller unable to tell "I used the wrong materializer" from "this capability does
/// not exist here".** **The former is the caller's mistake, the latter is this repository's gap — the fixes differ
/// completely.**
///
/// **The third must not fold into either**: **the inner failure carries the concrete cause (say, invalid binary-logic
/// inputs), and folding it into "unsupported" loses that cause.**
///
/// **All seven structures can generate rows.**
///
/// **The materializer layer's completeness is decided by <b>row generation</b>, not by the dispatch table.
/// Worth writing down: a fully green dispatch table with three unmaterializable structures is a layer that looks
/// complete and is blank in three cells.**
/// </remarks>
public static class DeferredFunctionFallbackMaterializers
{
    /// <summary>分段线性物化器 / The piecewise materializer.</summary>
    public static IDeferredFunctionFallbackMaterializer Pwl { get; } = new PwlMaterializer();

    /// <summary>ABS 物化器 / The ABS materializer.</summary>
    public static IDeferredFunctionFallbackMaterializer Abs { get; } = new AbsMaterializer();

    /// <summary>MAX 物化器 / The MAX materializer.</summary>
    public static IDeferredFunctionFallbackMaterializer Max { get; } = new MaxMaterializer();

    /// <summary>半连续物化器 / The semi-continuous materializer.</summary>
    public static IDeferredFunctionFallbackMaterializer Semi { get; } = new SemiMaterializer();

    /// <summary>二值逻辑物化器 / The binary logic materializer.</summary>
    public static IDeferredFunctionFallbackMaterializer BinaryLogic { get; } =
        new BinaryLogicMaterializer();

    /// <summary>core 延迟函数集合的物化器 / The core deferred-function materializer.</summary>
    public static IDeferredFunctionFallbackMaterializer Core { get; } = new CoreMaterializer();

    private static Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
        Unsupported(string kind, DeferredFunctionStructure structure) =>
            new Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"unsupported {kind} fallback structure: {structure.GetType().Name}");

    /// <summary>
    /// **这个结构属于本物化器、但行生成还没实现** / Row generation for this structure is not implemented yet.
    /// </summary>
    private static Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
        NotPorted(string kind, DeferredFunctionStructure structure) =>
            new Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                $"{kind} row generation is not ported yet: {structure.GetType().Name}");

    private sealed class PwlMaterializer : IDeferredFunctionFallbackMaterializer
    {
        public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
            Materialize(DeferredFunctionStructure structure) =>
            structure switch {
                UnivariateLinearPiecewiseStructure piecewise => piecewise.MaterializeFallbackConstraints(),
                _ => Unsupported("piecewise", structure),
            };
    }

    private sealed class AbsMaterializer : IDeferredFunctionFallbackMaterializer
    {
        public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
            Materialize(DeferredFunctionStructure structure) =>
            structure switch {
                AbsStructure abs => abs.MaterializeFallbackConstraints(),
                _ => Unsupported("ABS", structure),
            };
    }

    private sealed class MaxMaterializer : IDeferredFunctionFallbackMaterializer
    {
        public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
            Materialize(DeferredFunctionStructure structure) =>
            structure switch {
                MaxStructure max => max.MaterializeFallbackConstraints(),
                _ => Unsupported("MAX", structure),
            };
    }

    private sealed class SemiMaterializer : IDeferredFunctionFallbackMaterializer
    {
        public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
            Materialize(DeferredFunctionStructure structure) =>
            structure switch {
                SemiStructure semi => semi.MaterializeFallbackConstraints(),
                _ => Unsupported("semi-continuous", structure),
            };
    }

    private sealed class BinaryLogicMaterializer : IDeferredFunctionFallbackMaterializer
    {
        public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
            Materialize(DeferredFunctionStructure structure) =>
            structure switch {
                BinaryLogicStructure logic => logic.MaterializeFallbackConstraints(),
                _ => Unsupported("binary logic", structure),
            };
    }

    private sealed class CoreMaterializer : IDeferredFunctionFallbackMaterializer
    {
        public Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>
            Materialize(DeferredFunctionStructure structure) =>
            structure switch {
                UnivariateLinearPiecewiseStructure piecewise => Pwl.Materialize(piecewise),
                AbsStructure abs => Abs.Materialize(abs),
                MaxStructure max => Max.Materialize(max),
                SemiStructure semi => Semi.Materialize(semi),
                BinaryLogicStructure logic => BinaryLogic.Materialize(logic),
                IndicatorStructure indicator => indicator.MaterializeFallbackConstraints(),
                MaskingStructure masking => masking.MaterializeFallbackConstraints(),
                _ => Unsupported("deferred function", structure),
            };
    }
}
