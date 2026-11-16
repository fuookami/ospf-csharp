#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **精确激活算子的表达式降阶** / Expression lowering for the exact activation operators.
/// </summary>
/// <remarks>
/// `OnnxActivation` 里"精确"那一半（`Relu`／`Clip`／`LeakyRelu`／`HardSigmoid`）。
/// **它们在 LP 里都不是"一个函数调用"，而是一组约束加一个结果变量：** **`Relu(x)` 在本插件里是一条正部函数的曲线，`
/// **而把这些写成"直接代公式"会得到一条非线性的目标——** **求解器要么拒绝，要么在不该松弛的地方松弛。**
///
/// **这一层只做**代数化简**那一步（哪些情形可以化简成纯仿射、哪些必须交给函数符号），不做约束生成：**
/// **化简判错会让模型少加约束（结果本该被钉住却自由），而少加的约束永远不会报错。**
///
/// Syncs the "exact" half of `OnnxActivation`. **In an LP these are not function calls but a set of constraints plus a result variable: Relu is a positive-part curve,
/// and writing the formula inline yields a non-linear objective — the solver either refuses it or relaxes where it must not.**
///
/// ** does only the algebraic simplification (which cases collapse to a pure affine form and which must go to a function symbol), not constraint
/// generation: a wrong simplification drops constraints (a result that should be pinned stays free), and a dropped constraint never reports anything.**
/// </remarks>
public static class OnnxActivationLowering
{
    /// <summary>
    /// 化简结果 / The simplification result.
    /// </summary>
    /// <param name="Polynomial">纯仿射结果；无法化简时为 null / The affine result, null when it cannot be simplified.</param>
    /// <param name="NeedsFunctionSymbol">是否必须交给函数符号 / Whether a function symbol is required.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Result(LinearPolynomial<Flt64>? Polynomial, bool NeedsFunctionSymbol, string? Failure);

    /// <summary>
    /// 化简 `LeakyRelu` / Simplify `LeakyRelu`.
    /// </summary>
    /// <param name="input">输入表达式 / The input expression.</param>
    /// <param name="alpha">负半轴斜率 / The negative-side slope.</param>
    /// <returns>化简结果 / The result.</returns>
    /// <remarks>
    /// **`alpha = 1` 时它恒等于输入，这一条必须真的化简成输入本身而不是"加一个恒等函数符号"：** **恒等符号在模型里多一个变量、多一条等式，**
    /// **而对求解毫无贡献——** **几百个这样的节点会让模型凭空大一倍。**
    ///
    /// **At `alpha = 1` it is the identity, and that has to collapse to the input itself rather than "an identity function symbol": an identity symbol costs a
    /// variable and an equality per node while contributing nothing, and a few hundred such nodes double the model for free.**
    /// </remarks>
    public static Result SimplifyLeakyRelu(LinearPolynomial<Flt64> input, double alpha) {
        if (alpha < 0.0) {
            return new Result(null, false, "LeakyRelu 的 alpha 不得为负 / LeakyRelu alpha must not be negative");
        }
        if (alpha == 1.0) {
            return new Result(input, false, null);
        }
        if (alpha == 0.0) {
            // alpha = 0 就是 Relu 本身。 / alpha = 0 is Relu itself.
            return new Result(null, true, null);
        }
        return new Result(null, true, null);
    }

    /// <summary>
    /// 化简 `Clip` / Simplify `Clip`.
    /// </summary>
    /// <param name="input">输入表达式 / The input expression.</param>
    /// <param name="lower">下界 / The lower bound.</param>
    /// <param name="upper">上界 / The upper bound.</param>
    /// <returns>化简结果 / The result.</returns>
    /// <remarks>
    /// **两侧都缺省时 `Clip` 是恒等映射**，**必须化简成输入本身而不是"钳到一个形式无界的区间"：** **后者会在模型里引入两条永远不紧的约束，**
    /// **而它们的存在让"这个模型有多少条约束"这个诊断数字失去意义。**
    ///
    /// **With neither bound Clip is the identity, and it has to collapse to the input rather than "clamping to a formally unbounded interval": the latter adds two
    /// constraints that never bind, and their presence makes "how many constraints does this model have" meaningless as a diagnostic.**
    /// </remarks>
    public static Result SimplifyClip(LinearPolynomial<Flt64> input, double? lower, double? upper) {
        if (lower is null && upper is null) {
            return new Result(input, false, null);
        }
        if (lower is double low && upper is double high && low > high) {
            return new Result(null, false, "Clip 下界 " + low + " 大于上界 " + high + " / the lower bound exceeds the upper bound");
        }
        return new Result(null, true, null);
    }

    /// <summary>
    /// 化简 `Relu` / Simplify `Relu`.
    /// </summary>
    /// <param name="input">输入表达式 / The input expression.</param>
    /// <param name="lower">输入下界 / The input's lower bound.</param>
    /// <param name="upper">输入上界 / The input's upper bound.</param>
    /// <returns>化简结果 / The result.</returns>
    /// <remarks>
    /// **当输入区间整段落在正半轴时 `Relu` 恒等于输入；整段落在负半轴时恒等于零。** **这两条不是优化，而是正确性：**
    /// **不化简而硬建一条正部函数，在"输入已经非负"的模型里会引入一个多出来的变量与两条约束——** **它们不改变解，但会让对偶值不可解释。**
    ///
    /// **When the input range lies entirely on the positive side Relu is the input; entirely on the negative side it is zero. These are not optimisations but
    /// correctness: building a positive-part function for an already non-negative input adds a variable and two constraints that do not change the answer while
    /// making the duals uninterpretable.**
    /// </remarks>
    public static Result SimplifyRelu(LinearPolynomial<Flt64> input, double lower, double upper) {
        if (double.IsNaN(lower) || double.IsNaN(upper) || double.IsInfinity(lower) || double.IsInfinity(upper)) {
            return new Result(null, false, "Relu 需要有限输入区间 / Relu needs finite input bounds");
        }
        if (lower > upper) {
            return new Result(null, false, "Relu 的输入区间倒挂 / the input range is inverted");
        }
        if (lower >= 0.0) {
            return new Result(input, false, null);
        }
        if (upper <= 0.0) {
            var zero = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
            return new Result(zero, false, null);
        }
        return new Result(null, true, null);
    }

    /// <summary>
    /// 化简 `HardSigmoid` / Simplify `HardSigmoid`.
    /// </summary>
    /// <param name="input">输入表达式 / The input expression.</param>
    /// <param name="alpha">斜率 / The slope.</param>
    /// <param name="beta">偏移 / The offset.</param>
    /// <param name="lower">输入下界 / The input's lower bound.</param>
    /// <param name="upper">输入上界 / The input's upper bound.</param>
    /// <returns>化简结果 / The result.</returns>
    /// <remarks>
    /// **`alpha·x + beta` 若整段落在 `[0, 1]` 内，钳位不发生，结果就是纯仿射。** **这不是小优化：** **它是把"带钳位的节点"变成"一条线性表达式"的唯一机会，**
    /// **而一旦漏掉，模型里就多出一整个分段结构，只为表示一个从不触发的钳位。**
    ///
    /// **If `alpha·x + beta` lies entirely inside `[0, 1]` the clamp never fires and the result is purely affine. This is not a small optimisation: it is the only
    /// chance to turn a clamping node into one linear expression, and missing it adds a whole piecewise structure to represent a clamp that never triggers.**
    /// </remarks>
    public static Result SimplifyHardSigmoid(LinearPolynomial<Flt64> input, double alpha, double beta, double lower, double upper) {
        if (double.IsNaN(lower) || double.IsNaN(upper) || double.IsInfinity(lower) || double.IsInfinity(upper)) {
            return new Result(null, false, "HardSigmoid 需要有限输入区间 / HardSigmoid needs finite input bounds");
        }
        if (lower > upper) {
            return new Result(null, false, "HardSigmoid 的输入区间倒挂 / the input range is inverted");
        }

        // 端点的取值已经决定了整段是否落在 [0, 1] 内：仿射函数在区间上是单调的。
        // The endpoints already decide whether the whole segment stays inside [0, 1]: an affine function is monotone over an interval.
        double atLower = alpha * lower + beta;
        double atUpper = alpha * upper + beta;
        double minimum = System.Math.Min(atLower, atUpper);
        double maximum = System.Math.Max(atLower, atUpper);

        if (minimum >= 0.0 && maximum <= 1.0) {
            var scaled = new List<LinearMonomial<Flt64>>(input.Monomials.Count);
            foreach (LinearMonomial<Flt64> monomial in input.Monomials) {
                scaled.Add(new LinearMonomial<Flt64>(monomial.Coefficient * new Flt64(alpha), monomial.Symbol));
            }
            return new Result(
                new LinearPolynomial<Flt64>(scaled, input.Constant * new Flt64(alpha) + new Flt64(beta)),
                false,
                null);
        }
        return new Result(null, true, null);
    }
}