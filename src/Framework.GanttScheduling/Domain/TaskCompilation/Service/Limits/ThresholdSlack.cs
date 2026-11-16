#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 阈值松弛工具 / Threshold slack utility.
/// 提供阈值松弛多项式的创建和访问功能。
/// Provides creation and access for threshold slack polynomials.
/// </summary>
internal static class ThresholdSlack {
    /// <summary>
    /// 创建阈值松弛 / Create a threshold slack
    /// </summary>
    /// <param name="x">输入值 / Input value</param>
    /// <param name="threshold">阈值 / Threshold value</param>
    /// <returns>阈值松弛实例 / Threshold slack instance</returns>
    public static ThresholdSlackResult Create(Flt64 x, Flt64 threshold) {
        Flt64 slack = x - threshold;
        Flt64 positiveSlack = slack > Flt64.Zero ? slack : Flt64.Zero;
        return new ThresholdSlackResult(positiveSlack, x);
    }

    /// <summary>
    /// 建一条阈值松弛并加到模型 / Build a threshold slack and add it to the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="x">被松弛的多项式 / The polynomial being slacked.</param>
    /// <param name="threshold">阈值 / The threshold.</param>
    /// <param name="name">符号名 / The symbol name.</param>
    /// <param name="slack">建出的松弛 / The built slack.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 对应基线 `thresholdSlack(...)`：`withNegative = false`、`withPositive = true`、`threshold = true`、`constraint = true`。
    ///
    /// **`withNegative = false` 是这三个开关里最要紧的**：带下侧会让松弛在**低于**阈值时也变大，
    /// 于是"超出阈值的部分"变成"与阈值的距离"，两个方向的目标都会算错。判据必须能分别钉住这三个开关。
    ///
    /// **必须先加到模型再优化它**：建一个未被约束的松弛去最小化（或最大化），等于又一个恒为零的空操作。
    ///
    /// **返回适配器而不是 `SlackFunction`**：进模型的是适配器，`Threshold` / `WithNegative` / `WithPositive` 三个开关
    /// 要穿过 `adapter.Function` 才读得到；调用方也只拿得到适配器。
    ///
    /// Mirrors the baseline's `thresholdSlack(...)`: `withNegative = false`, `withPositive = true`, `threshold = true`, `constraint = true`. **`withNegative = false` is the most important of the
    /// three**: a lower side would make the slack grow when the value is *below* the threshold, turning "the part above the threshold" into "the distance from it" and making objectives wrong in
    /// both directions, so the criteria must pin all three switches separately. **The slack must be added to the model before it is optimised**: an unconstrained slack is another no-op fixed at
    /// zero. **The adapter is returned rather than the `SlackFunction`**, because the adapter is what enters the model and its three switches are reachable through `adapter.Function`.
    /// </remarks>
    public static Try Register(
        IAbstractLinearMetaModel<Flt64> model,
        LinearPolynomial<Flt64> x,
        Flt64 threshold,
        string name,
        out LinearFunctionSymbolAdapter<Flt64> slack) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(x);

        var function = new SlackFunction<Flt64>(
            x: x,
            y: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), threshold),
            converter: IIntoValue<Flt64>.Identity,
            withNegative: false,
            withPositive: true,
            threshold: true,
            constraint: true,
            name: name);
        slack = new LinearFunctionSymbolAdapter<Flt64>(function);
        return model.Add(slack);
    }

    /// <summary>
    /// 松弛的正部 / The slack's positive part.
    /// </summary>
    /// <param name="slack">阈值松弛 / The threshold slack.</param>
    /// <returns>正部多项式 / The positive-part polynomial.</returns>
    /// <remarks>
    /// `withNegative = false` 让 `ResultPolynomial` 里只剩 `pos` 那一项，所以它本身就是正部。基线叫 `positiveSlackPolynomial`。
    ///
    /// `withNegative = false` leaves only the `pos` term in `ResultPolynomial`, so that polynomial *is* the positive part; the baseline calls it `positiveSlackPolynomial`.
    /// </remarks>
    public static LinearPolynomial<Flt64> PositivePart(LinearFunctionSymbolAdapter<Flt64> slack) {
        ArgumentNullException.ThrowIfNull(slack);
        return slack.Polynomial;
    }

    /// <summary>
    /// 松弛的截断部 / The slack's capped part.
    /// </summary>
    /// <param name="slack">阈值松弛 / The threshold slack.</param>
    /// <param name="x">被松弛的多项式 / The polynomial being slacked.</param>
    /// <returns>截断多项式 / The capped polynomial.</returns>
    /// <remarks>
    /// 基线 `polyX` = `x + neg - pos`；`withNegative = false` 时负部不存在，于是这里是 **`x - pos`**，即"至多到阈值为止"的那个值。
    /// 与正部**不是同一个东西**：正部在阈值以下是 0，截断部在阈值以下是 `x` 本身。奖励类目标要的是截断部。
    ///
    /// The baseline's `polyX` is `x + neg - pos`; with `withNegative = false` there is no negative part, so this is **`x - pos`** — the value capped at the threshold. It is **not** the same as the
    /// positive part: the positive part is zero below the threshold, while the capped part is `x` itself there. Reward objectives want the capped one.
    /// </remarks>
    public static LinearPolynomial<Flt64> CappedPart(
        LinearFunctionSymbolAdapter<Flt64> slack,
        LinearPolynomial<Flt64> x) {
        ArgumentNullException.ThrowIfNull(slack);
        ArgumentNullException.ThrowIfNull(x);

        var monomials = new List<LinearMonomial<Flt64>>(x.Monomials);
        foreach (LinearMonomial<Flt64> monomial in slack.Polynomial.Monomials) {
            // `pos` 带着 +1 系数出现，截断部要把它减回去。
            // `pos` appears with a +1 coefficient, and the capped part subtracts it back out.
            monomials.Add(new LinearMonomial<Flt64>(Flt64.Zero - monomial.Coefficient, monomial.Symbol));
        }
        return new LinearPolynomial<Flt64>(monomials, x.Constant);
    }

    /// <summary>
    /// 按系数缩放一个多项式 / Scale a polynomial by a coefficient.
    /// </summary>
    /// <param name="polynomial">多项式 / The polynomial.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <returns>缩放后的多项式 / The scaled polynomial.</returns>
    public static LinearPolynomial<Flt64> Scale(LinearPolynomial<Flt64> polynomial, Flt64 coefficient) {
        ArgumentNullException.ThrowIfNull(polynomial);
        return new LinearPolynomial<Flt64>(
            polynomial.Monomials
                .Select(monomial => new LinearMonomial<Flt64>(coefficient * monomial.Coefficient, monomial.Symbol))
                .ToArray(),
            coefficient * polynomial.Constant);
    }
}

/// <summary>
/// 阈值松弛结果 / Threshold slack result
/// </summary>
/// <param name="PositiveSlack">正松弛值 / Positive slack value</param>
/// <param name="CappedValue">封顶值 / Capped value</param>
internal sealed record ThresholdSlackResult(Flt64 PositiveSlack, Flt64 CappedValue);
