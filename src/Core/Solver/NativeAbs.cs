#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver;

/// <summary>
/// 原生绝对值关系的数据 / Data for a native absolute-value relation.
/// </summary>
/// <remarks>
/// B1201 按基线 `NativeAbs.kt` 的 `NativeAbsData` 补。
/// **B1201, following the baseline's `NativeAbsData`.**
/// </remarks>
/// <param name="InputKey">输入变量键 / Input variable key.</param>
/// <param name="ResultKey">结果变量键 / Result variable key.</param>
/// <param name="PositiveBigM">fallback 正部上界 / Fallback positive-part bound.</param>
/// <param name="NegativeBigM">fallback 负部上界 / Fallback negative-part bound.</param>
/// <param name="Name">函数名称 / Function name.</param>
public sealed record NativeAbsData(
    VariableItemKey InputKey,
    VariableItemKey ResultKey,
    double PositiveBigM,
    double NegativeBigM,
    string Name);

/// <summary>
/// 校验并转换绝对值 primitive / Validate and convert an absolute-value primitive.
/// </summary>
/// <remarks>
/// B1201 按基线 `NativeAbs.kt` 的 `prepareNativeAbs` 补。
///
/// **"`1·x + 0`" 被检查<b>两次</b>，而这<b>不是</b>重复：**
///
/// | 层次 | 检查什么 |
/// | --- | --- |
/// | **数值层**（`InputVariableKeyOrNull`） | **系数等于 `one`、常数等于 `zero`** |
/// | **求解器层**（转成 `double` 之后） | **那个 `double` 恰为 `1.0` 与 `0.0`** |
///
/// **两者在"转换器把 `one` 映射成 1.0"时给出同一个答案，所以看起来是重复的。**
/// **但转换器是一个<b>可注入的值映射</b>——一个带缩放的转换器完全可以让
/// `coefficient == one` 成立而 `ToSolverDouble(coefficient) == 1000.0`。**
/// **原生 primitive 走的是求解器的那一侧，所以必须按求解器的数值再验一次。**
///
/// **`maximumMagnitude` 是<b>严格</b>上界：`|M| >= maximumMagnitude` 被拒，
/// 而不是 `> maximumMagnitude`。** **刚好等于 SDK 上限的值不可用，
/// 因为它没有留出任何余量——求解器在加上别的项之后就会溢出。**
///
/// **Five keys must be distinct, and the input variable is one of them**:
/// **`abs(x)` 的结果或辅助列若与输入是同一列，方程仍然有解，只是那个解不是 `abs`。**
///
/// **"`1·x + 0`" is checked <b>twice</b>, and that is <b>not</b> duplication:**
///
/// | Layer | What it checks |
/// | --- | --- |
/// | **numeric** (`InputVariableKeyOrNull`) | **the coefficient equals `one` and the constant equals `zero`** |
/// | **solver** (after `ToSolverDouble`) | **that double is exactly `1.0` and `0.0`** |
///
/// **They agree when the converter maps `one` to 1.0, which is why it looks duplicated.** **But a converter is an
/// <b>injectable value mapping</b> — a scaling converter can satisfy `coefficient == one` while
/// `ToSolverDouble(coefficient) == 1000.0`.** **The native primitive runs on the solver side, so the solver-side numbers
/// must be re-checked.**
///
/// **`maximumMagnitude` is a <b>strict</b> bound: `|M| >= maximumMagnitude` is rejected, not
/// `> maximumMagnitude`.** **A value exactly at the SDK limit is unusable because it leaves no headroom — the solver
/// overflows as soon as another term is added.**
///
/// **Five keys must be distinct and the input variable is one of them**: **if `abs(x)`'s result or helper shares a column
/// with the input, the equations remain solvable, just not as `abs`.**
/// </remarks>
public static class NativeAbs
{
    /// <summary>校验并转换 / Validate and convert.</summary>
    public static Result<NativeAbsData, ErrorCode, Error<ErrorCode>> Prepare(
        AbsStructure structure,
        double maximumMagnitude = double.PositiveInfinity) {
        ArgumentNullException.ThrowIfNull(structure);

        if (double.IsNaN(maximumMagnitude) || maximumMagnitude <= 0.0) {
            return Invalid("maximum magnitude must be positive");
        }

        try {
            // **数值层：复用 `AbsStructure` 已有的裸变量判定，而不是再写一遍。**
            // **Numeric layer: reuse `AbsStructure`'s existing bare-variable check instead of rewriting it.**
            VariableItemKey? maybeKey = structure.InputVariableKeyOrNull();
            if (maybeKey is null) {
                return Invalid("input must be 1 * variable + 0");
            }

            // **`VariableItemKey` 是记录（引用类型），所以判空之后要取一个新的非空变量。**
            // **`VariableItemKey` is a record (a reference type), so take a fresh non-null variable after the check.**
            VariableItemKey inputKey = maybeKey;

            IIntoValue<Flt64> converter = structure.Converter;

            // **求解器层：读<b>实际的</b>系数与常数，而不是 `one` 的往返。**
            // **`one` 的往返在任何转换器下都等于 `1.0`，所以那样写是一个恒等式，
            // 检查不到任何东西。要检查的是"这个多项式的系数与常数到了求解器那一侧还是 1 与 0"。**
            //
            // **Solver layer: read the <b>actual</b> coefficient and constant, not a round-trip of `one`.
            // **A round-trip of `one` equals `1.0` under any converter, making that check a tautology that verifies nothing.
            // What matters is whether this polynomial's coefficient and constant are still one and zero on the solver
            // side.**
            Flt64 coefficient = structure.Input.Monomials[0].Coefficient;
            if (converter.FromValue(coefficient).ToSolverDouble() != 1.0 ||
                converter.FromValue(structure.Input.Constant).ToSolverDouble() != 0.0) {
                return Invalid("converted input must be 1 * variable + 0");
            }

            // **键的唯一性包含输入列。**
            // **Key uniqueness includes the input column.**
            var keys = new VariableItemKey[] {
                inputKey, structure.ResultVariable.Key,
                structure.PositiveVariable.Key, structure.NegativeVariable.Key,
                structure.SignVariable.Key,
            };

            if (new HashSet<VariableItemKey>(keys).Count != keys.Length) {
                return Invalid("input, result, and helpers must have distinct keys");
            }

            double positiveBigM = converter.FromValue(structure.PositiveBigM).ToSolverDouble();
            double negativeBigM = converter.FromValue(structure.NegativeBigM).ToSolverDouble();

            foreach (double bigM in new[] { positiveBigM, negativeBigM }) {
                if (!double.IsFinite(bigM) || bigM < 0.0 ||
                    global::System.Math.Abs(bigM) >= maximumMagnitude) {
                    return Invalid(
                        "fallback bounds must be finite, nonnegative, and representable");
                }
            }

            return new Ok<NativeAbsData, ErrorCode, Error<ErrorCode>>(
                new NativeAbsData(
                    InputKey: inputKey,
                    ResultKey: structure.ResultVariable.Key,
                    PositiveBigM: positiveBigM,
                    NegativeBigM: negativeBigM,
                    Name: structure.Name));
        }
        catch (Exception error) {
            return Invalid($"numeric conversion failed: {error.Message}");
        }
    }

    private static Result<NativeAbsData, ErrorCode, Error<ErrorCode>> Invalid(string reason) =>
        new Failed<NativeAbsData, ErrorCode, Error<ErrorCode>>(
            ErrorCode.IllegalArgument, $"invalid native ABS data: {reason}");
}
