#nullable enable

using System;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// 分段线性函数的 Big-M 解析 / Resolve a piecewise function's Big-M.
/// </summary>
/// <remarks>
/// B1192 按基线 `DeferredFunctionFallback.kt` 的 `resolvePwlBigM` 补。
///
/// **同一个非正候选值，在两条路径上得到相反的结果：**
///
/// | 路径 | 候选 ≤ 0 时 |
/// | --- | --- |
/// | **自动**（`explicit = false`） | **被规范化成正的，通过** |
/// | **显式**（`explicit = true`） | **直接拒绝** |
///
/// **理由不是随意：**
///
/// **自动 M 是一个<b>推导出来的下界估计</b>——它可以合理地是 0 或负数
/// （比如"没有数据"时），而把它规范化成一个安全的正值不会改变任何语义。**
///
/// **显式 M 是<b>调用方的声明</b>。声明一个非正的 Big-M 是矛盾：
/// "所有违反情形的偏差都 ≤ 0"意味着那些约束根本没有放松余量。
/// 这时规范化会<b>悄悄改掉调用方的声明</b>，而规范化之后的模型可能可行、
/// 求解成功——只是它与调用方要求的不一样。**
///
/// **所以：可修复的就修复，不可修复的就拒绝。**
///
/// **两次校验（规范化前后各一次）也不是重复**：**规范化本身可能产出不可用的值，
/// 而那时错误消息不同——"候选不可用"与"规范化后不可用"是两件事。**
///
/// B1192, following the baseline's `resolvePwlBigM`.
///
/// **The same non-positive candidate yields opposite outcomes on the two paths:**
///
/// | Path | Candidate ≤ 0 |
/// | --- | --- |
/// | **automatic** (`explicit = false`) | **normalized positive, accepted** |
/// | **explicit** (`explicit = true`) | **rejected outright** |
///
/// **And not arbitrarily:**
///
/// **An automatic M is a <b>derived lower estimate</b> — it may legitimately be zero or negative (say, with no data),
/// and normalizing it to a safe positive value changes no semantics.**
///
/// **An explicit M is <b>the caller's declaration</b>, and declaring a non-positive Big-M is a contradiction: "every
/// violating deviation is ≤ 0" means those constraints have no relaxation slack at all.** **Normalizing would
/// <b>quietly rewrite the caller's declaration</b>, and the normalized model may be feasible and solve fine — it just
/// is not what was asked for.**
///
/// **So: repair what can be repaired, reject what cannot.**
///
/// **The two checks (before and after normalization) are not duplication**: **normalization itself may produce an
/// unusable value, and the message differs — "the candidate is unusable" and "the normalized value is unusable" are two
/// different facts.**
/// </remarks>
public static class PiecewiseBigM
{
    /// <summary>
    /// 解析 Big-M / Resolve the Big-M.
    /// </summary>
    /// <param name="candidate">候选值 / The candidate value.</param>
    /// <param name="explicit">是否由调用方显式给出 / Whether the caller supplied it.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> Resolve(
        Flt64 candidate,
        bool @explicit,
        IIntoValue<Flt64> converter) {
        ArgumentNullException.ThrowIfNull(converter);

        try {
            if (!BigMValues.IsUsable(candidate) ||
                (@explicit && candidate.CompareTo(Flt64.Zero) <= 0)) {
                return Fail(
                    "the piecewise Big-M must be finite, representable and positive");
            }

            Flt64 normalized = BigMValues.EnsurePositiveBigM(candidate, converter);

            if (!BigMValues.IsUsable(normalized) || normalized.CompareTo(Flt64.Zero) <= 0) {
                return Fail("the normalized piecewise Big-M is not usable");
            }

            return new Ok<Flt64, ErrorCode, Error<ErrorCode>>(normalized);
        }
        catch (Exception error) {
            return Fail($"failed to validate the piecewise Big-M: {error.Message}");
        }
    }

    private static Result<Flt64, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        new Failed<Flt64, ErrorCode, Error<ErrorCode>>(ErrorCode.IllegalArgument, message);
}
