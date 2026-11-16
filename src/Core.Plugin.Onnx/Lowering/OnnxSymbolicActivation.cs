#nullable enable

// IDE0008 要求把这里的 `var` 换成显式类型，而那类型跨两个命名空间（`Utils.Functional` 与 `Utils.Error`）；
// 显式写出来会让这一行比周围所有代码都长。按仓库既有做法在文件级关闭，理由记录在此。
//
// IDE0008 wants the `var` here spelled out, and that type spans two namespaces (Utils.Functional and Utils.Error); writing it out makes the line longer than anything
// around it. Disabled at file scope per the existing repository practice, with the reason recorded here.
#pragma warning disable IDE0008

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **把符号输入的激活变成模型层认得的函数符号** / Turn an activation over a symbolic input into a function symbol the model layer understands.
/// </summary>
/// <remarks>
/// 激活降阶在模型层的那一半。**此前 `Relu(x)` 只能拒绝，因为多项式装不下"正部"这件事：**
/// **它是分段线性的，而分段线性不是一次多项式。** **框架给的路是把函数包成系数为一的单项式——** '
/// **`LinearFunctionSymbolAdapter` 实现了 `ILinearIntermediateSymbol`，展平时会把它展开成自己的结果多项式。**
///
/// **那条路有一个硬要求：函数符号必须已注册。** **展平器会检查 `tokens` 里有没有它，没有就报"函数符号未注册"——** '
/// **所以挂接时必须先把它交给模型，顺序不能反。**
///
/// Syncs the model-layer half of the activation lowering. **Until now `Relu(x)` could only be refused because a polynomial cannot hold "positive part": it is piecewise
/// linear and piecewise linear is not a first-degree polynomial.** **The framework's route is to wrap the function as a monomial of coefficient one — the adapter implements
/// `ILinearIntermediateSymbol` and flattening expands it into its own result polynomial.**
///
/// **That route has one hard requirement: the function symbol must be registered.** **The flattener checks the token set and reports "function symbol is not registered"
/// otherwise — so the attachment has to hand it to the model first, and the order cannot be flipped.**
/// </remarks>
public static class OnnxSymbolicActivation
{
    /// <summary>
    /// 构造结果 / The construction result.
    /// </summary>
    /// <param name="Symbol">包好的函数符号（放进多项式用）/ The wrapped function symbol, ready for a polynomial.</param>
    /// <param name="Constant">塌缩成的常数（区间退化为一点时）/ The constant it collapsed into when the interval degenerates.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Result(LinearFunctionSymbolAdapter<Flt64>? Symbol, Flt64? Constant, string? Failure)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;

        /// <summary>构造成功（函数符号）/ A successful construction through a function symbol.</summary>
        public static Result FromSymbol(LinearFunctionSymbolAdapter<Flt64> symbol) => new(symbol, null, null);

        /// <summary>构造成功（常量塌缩）/ A successful construction collapsed into a constant.</summary>
        public static Result FromConstant(Flt64 value) => new(null, value, null);
    }

    /// <summary>
    /// 用正部函数表达 `Relu` / Express Relu through the positive part.
    /// </summary>
    /// <param name="input">有界的线性输入 / The bounded linear input.</param>
    /// <param name="name">函数名 / The function name.</param>
    /// <returns>构造结果 / The result.</returns>
    /// <remarks>
    /// **输入必须已有有限界：** **正部函数要靠界算 big-M，界是 ±∞ 时它拒绝构造而不是猜一个——** '
    /// **而猜出来的大 M 会让模型在一个"大到不可能取到"的区域里给出最优解，看起来完全正常。**
    ///
    /// **The input has to carry finite bounds: the positive part needs them for its big-M, and with ±∞ it refuses to build rather than guessing one — a guessed big-M lets
    /// the model report an optimum in a region that can never be reached, and that looks entirely normal.**
    /// </remarks>
    public static Result PositivePart(LinearPolynomial<Flt64> input, string name = "relu") {
        var created = PositivePartFunction<Flt64>.Create(input, IIntoValue<Flt64>.Identity, name);
        if (created.IsFailed) {
            return new Result(null, null, "正部函数构造失败（多半是输入没有有限界）/ the positive part could not be built, usually because the input has no finite bounds");
        }
        return Result.FromSymbol(new LinearFunctionSymbolAdapter<Flt64>(created.Value));
    }

    /// <summary>
    /// 把函数符号化成系数为一的单项式 / Turn the symbol into a monomial of coefficient one.
    /// </summary>
    /// <param name="symbol">函数符号 / The function symbol.</param>
    /// <returns>单项式 / The monomial.</returns>
    public static Math.Symbol.Monomial.LinearMonomial<Flt64> Monomial(LinearFunctionSymbolAdapter<Flt64> symbol) =>
        new(Flt64.One, symbol);

    /// <summary>
    /// 用分段线性近似表达一个非线性激活 / Express a non-linear activation through a piecewise-linear approximation.
    /// </summary>
    /// <param name="opType">算子类型 / The operator type.</param>
    /// <param name="input">线性输入 / The linear input.</param>
    /// <param name="bounds">输入的区间（模型约束由它生成）/ The input's range, from which the model constraints are generated.</param>
    /// <param name="segments">分段数 / The segment count.</param>
    /// <returns>构造结果 / The result.</returns>
    /// <remarks>
    /// **的四处：** **①界从输入自己**取（`BigMValues.FiniteBounds`），不是调用方给；'
    /// **②`|界| > 1e6` 按"缺少有效范围"处理；** **③`lower == upper` 直接塌缩成一个常数，不建函数；'
    /// **④断点均匀、在断点上精确插值**（不是拟合）。**
    ///
    /// **为什么"界从输入自己取"是要紧的：** **调用方给的界与模型里的约束是两份东西，** **两边一不一致模型都能求解——**
    /// **只是分段近似的区间与实际可行域错开了，近似的误差落在没人看的地方。**
    ///
    /// **Four things follow the code: a bound beyond 1e6 is treated as missing; lower == upper collapses to a constant instead of building a function; the breakpoints are
    /// uniform with exact interpolation at them (not a fit); and the interval is the one the model itself is constrained by.**
    ///
    /// **取界的来源**：`BigMValues` 在 C# 侧是 `internal`，插件调不到；
    /// **而本插件里界只有唯一来源 `OnnxBounds`——模型约束正是由它生成，** **所以传进来的区间与模型约束用的是同一份数，不是第二份。**
    ///
    /// **The bounds come from `OnnxBounds`: BigMValues is internal on the C# side so the plugin
    /// cannot call it, but OnnxBounds is the plugin's only source of bounds and the model's constraints are generated from it, so the interval passed in is the same number
    /// rather than a second copy.**
    ///
    /// **Why taking the bounds from the input matters: a caller-supplied range and the model's constraints are two different things, and the model solves either way — the
    /// approximation's interval just no longer lines up with the feasible region, and the error lands where nobody looks.**
    /// </remarks>
    public static Result Piecewise(
        string opType,
        LinearPolynomial<Flt64> input,
        OnnxBounds bounds,
        int segments = OnnxPiecewiseApproximation.DefaultSegments) {
        if (!CanApproximate(opType)) {
            return new Result(null, null, "算子 '" + opType + "' 不在可分段近似的名单里 / the operator is not on the piecewise list");
        }
        if (segments < OnnxPiecewiseApproximation.MinSegments || segments > OnnxPiecewiseApproximation.MaxSegments) {
            return new Result(
                null,
                null,
                "分段数 " + segments + " 超出允许区间 ["
                + OnnxPiecewiseApproximation.MinSegments + ", " + OnnxPiecewiseApproximation.MaxSegments + "] / the segment count is outside the allowed range");
        }

        if (bounds is null || !bounds.Validate(out string? boundFailure)) {
            return new Result(null, null, "输入没有有限界，无法做分段近似 / the input carries no finite bounds, so it cannot be approximated");
        }
        // 界优先取输入变量**自己注册的范围**；
        // 变量没有有限范围时才用传入的 `OnnxBounds`，并把它注册上去——否则分段函数构造时看不到界。
        //
        // Bounds come from the range the input variables carry themselves; the supplied OnnxBounds is used and registered
        // only when a variable has no finite range, because the piecewise constructor cannot see a bound that was never registered.
        (double registeredLower, double registeredUpper, bool fromVariables) = RegisteredBounds(input);
        if (fromVariables) {
            if (System.Math.Abs(registeredLower - bounds.Lower) > 1e-12 || System.Math.Abs(registeredUpper - bounds.Upper) > 1e-12) {
                return new Result(
                    null, null,
                    "变量自己注册的界 [" + registeredLower + ", " + registeredUpper + "] 与传入的 [" + bounds.Lower + ", " + bounds.Upper
                    + "] 不一致 / the variable's own range disagrees with the bound passed in");
            }
        }
        else {
            RegisterBounds(input, bounds);
        }
        double lower = fromVariables ? registeredLower : bounds.Lower;
        double upper = fromVariables ? registeredUpper : bounds.Upper;
        if (!double.IsFinite(lower) || !double.IsFinite(upper)) {
            return new Result(null, null, "输入的界不是有限值 / the input's bounds are not finite");
        }
        if (System.Math.Abs(lower) > OnnxPiecewiseApproximation.MaxPiecewiseBound
            || System.Math.Abs(upper) > OnnxPiecewiseApproximation.MaxPiecewiseBound) {
            return new Result(
                null,
                null,
                "界超过分段近似的可识别上限 " + OnnxPiecewiseApproximation.MaxPiecewiseBound
                + "，按缺少有效范围处理 / the bound exceeds the piecewise limit and is treated as missing");
        }

        Func<double, double> function = Function(opType);
        if (lower == upper) {
            // 常量输入直接塌缩：建一个零斜率的函数只是多一层没有意义的结构。
            // A constant input collapses directly: building a zero-slope function only adds a pointless layer.
            return Result.FromConstant(new Flt64(function(lower)));
        }

        OnnxPiecewiseApproximation.Result approximate =
            OnnxPiecewiseApproximation.Approximate(function, lower, upper, segments);
        if (approximate.Value is null) {
            return new Result(null, null, approximate.Failure);
        }

        var breakpoints = new List<Flt64>(approximate.Value.Breakpoints.Count);
        foreach (double value in approximate.Value.Breakpoints) {
            breakpoints.Add(new Flt64(value));
        }
        var slopes = new List<Flt64>(approximate.Value.Slopes.Count);
        foreach (double value in approximate.Value.Slopes) {
            slopes.Add(new Flt64(value));
        }
        var intercepts = new List<Flt64>(approximate.Value.Intercepts.Count);
        foreach (double value in approximate.Value.Intercepts) {
            intercepts.Add(new Flt64(value));
        }

        var created = UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input,
            breakpoints,
            slopes,
            intercepts,
            IIntoValue<Flt64>.Identity,
            name: opType.ToLowerInvariant());
        if (created.IsFailed) {
            return new Result(null, null, "分段函数构造失败 / the piecewise function could not be built");
        }
        return Result.FromSymbol(new LinearFunctionSymbolAdapter<Flt64>(created.Value));
    }

    /// <summary>按区间算术求多项式变量的注册范围 / The registered range of a polynomial's variables by interval arithmetic.</summary>
    private static (double Lower, double Upper, bool Present) RegisteredBounds(LinearPolynomial<Flt64> input) {
        double lower = input.Constant.ToDouble();
        double upper = input.Constant.ToDouble();
        foreach (LinearMonomial<Flt64> monomial in input.Monomials) {
            if (monomial.Symbol is not IVariableItem variable) {
                return (0.0, 0.0, false);
            }
            double? symbolLower = variable.LowerBound?.Value.ToFlt64().ToDouble();
            double? symbolUpper = variable.UpperBound?.Value.ToFlt64().ToDouble();
            if (symbolLower is not double lo || symbolUpper is not double hi) {
                return (0.0, 0.0, false);
            }
            // 默认的 ±MaxValue 视为"没有有限范围"：形式有限、实际无界。
            // The default ±MaxValue counts as "no finite range": formally finite, practically unbounded.
            if (!double.IsFinite(lo) || !double.IsFinite(hi)
                || System.Math.Abs(lo) > OnnxPiecewiseApproximation.MaxPiecewiseBound
                || System.Math.Abs(hi) > OnnxPiecewiseApproximation.MaxPiecewiseBound) {
                return (0.0, 0.0, false);
            }
            double coefficient = monomial.Coefficient.ToDouble();
            lower += coefficient > 0 ? coefficient * lo : coefficient * hi;
            upper += coefficient > 0 ? coefficient * hi : coefficient * lo;
        }
        return (lower, upper, true);
    }

    /// <summary>把界注册到多项式的变量上 / Register the bounds onto the polynomial's variables.</summary>
    private static void RegisterBounds(LinearPolynomial<Flt64> input, OnnxBounds bounds) {
        var range = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Of(
            new Flt64(bounds.Lower),
            new Flt64(bounds.Upper),
            new Fuookami.Ospf.Math.Algebra.ValueRange.Interval.Closed(),
            new Fuookami.Ospf.Math.Algebra.ValueRange.Interval.Closed());
        if (range.IsFailed) {
            return;
        }
        foreach (LinearMonomial<Flt64> monomial in input.Monomials) {
            // `Range` 只在具体变量类型上：`IVariableItem` 没有它，所以要落到 RealVar。
            // Range lives on the concrete variable type: IVariableItem does not have it, so this lands on RealVar.
            if (monomial.Symbol is RealVar real) {
                real.Range.SetRange(range.Value);
            }
        }
    }

    private static Func<double, double> Function(string opType) => opType switch {
        "Sigmoid" => OnnxActivationSemantics.Sigmoid,
        "Tanh" => System.Math.Tanh,
        "Softplus" => OnnxActivationSemantics.Softplus,
        "Elu" => value => OnnxActivationSemantics.Elu(value, 1.0),
        _ => throw new InvalidOperationException("unreachable", null),
    };

    /// <summary>
    /// 判断某个激活能否用分段线性近似 / Whether an activation can be approximated piecewise.
    /// </summary>
    /// <param name="opType">算子类型 / The operator type.</param>
    /// <returns>是否可用 / Whether it is usable.</returns>
    public static bool CanApproximate(string opType) => OnnxPiecewiseApproximation.Approximated.Contains(opType);

    /// <summary>
    /// 判断某个激活能否用正部表达 / Whether an activation can be expressed through the positive part.
    /// </summary>
    /// <param name="opType">算子类型 / The operator type.</param>
    /// <returns>是否可用 / Whether it is usable.</returns>
    /// <remarks>
    /// **只有 `Relu` 走这条：** **`Sigmoid`／`Tanh` 不是分段线性，`Relu` 是。** **把非线性但非分段的函数塞进正部，
    /// 会得到一个"能求解但算的是别的函数"的模型——** **而它比拒绝更难发现。**
    ///
    /// **Only Relu takes this route: Sigmoid and Tanh are not piecewise linear, Relu is.** **Feeding a non-piecewise function into the positive part yields a model that
    /// solves while evaluating a different function — harder to notice than a refusal.**
    /// </remarks>
    public static bool CanExpress(string opType) => opType == "Relu";
}