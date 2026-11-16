#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 三个二次"松弛/判定"类共用的减法与合并 / The subtraction and combination shared by the three classes.
/// </summary>
/// <remarks>
/// B1164 按基线 `QuadraticInequality.kt`、`QuadraticSlack.kt`、`QuadraticSlackRange.kt` 补三个类。
///
/// **这三个类讲的是同一件事：把"差了多少"变成非负量。**
///
/// - **`QuadraticInequalityFunction`**：**桥接后交给 `InequalityFunction`，返回 0/1 真值。**
/// - **`QuadraticSlackFunction`**：**`max(l − r, 0) + max(r − l, 0)`，也就是 `|l − r|`。**
/// - **`QuadraticSlackRangeFunction`**：**`max(lower − x, 0) + max(x − upper, 0)`，区间总违背量。**
///
/// **后两者都由两个 `QuadraticPositivePartFunction`拼成，没有自己的极值编码。**
///
/// **最容易写错的是"只取一侧"**：**`|l − r|` 只留 `max(l − r, 0)` 会在 `l &lt; r` 时给 0；
/// 区间违背量只留一侧会在另一半给 0。** **两者都不报错，只是在半个定义域上悄悄变成 0。**
///
/// adds
/// **The last two are assembled from two `QuadraticPositivePartFunction`s and hold no extremum encoding.**
///
/// **The easy mistake is keeping only one side**: **using `max(l − r, 0)` alone gives zero when `l &lt; r`, and a
/// one-sided range violation gives zero on the other half.** **Neither throws — each silently becomes zero over half
/// the domain.**
/// </remarks>
internal static class QuadraticSlackSupport
{
    /// <summary>逐项相减 <c>left − right</c> / Subtract term by term.</summary>
    public static QuadraticPolynomial<Flt64> Subtract(
        QuadraticPolynomial<Flt64> left,
        QuadraticPolynomial<Flt64> right,
        IIntoValue<Flt64> converter) {
        var monomials = new List<QuadraticMonomial<Flt64>>(left.Monomials.Count + right.Monomials.Count);

        foreach (QuadraticMonomial<Flt64> monomial in left.Monomials) {
            monomials.Add(monomial);
        }

        foreach (QuadraticMonomial<Flt64> monomial in right.Monomials) {
            monomials.Add(new QuadraticMonomial<Flt64>(
                monomial.Coefficient.Negate(), monomial.Symbol1, monomial.Symbol2));
        }

        return new QuadraticPolynomial<Flt64>(monomials, left.Constant.Minus(right.Constant));
    }

    /// <summary>把两个正部的结果相加 / Add the results of two positive parts.</summary>
    public static LinearPolynomial<Flt64> Sum(
        QuadraticPositivePartFunction left,
        QuadraticPositivePartFunction right) {
        var monomials = new List<LinearMonomial<Flt64>>(
            left.ResultPolynomial.Monomials.Count + right.ResultPolynomial.Monomials.Count);

        foreach (LinearMonomial<Flt64> monomial in left.ResultPolynomial.Monomials) {
            monomials.Add(monomial);
        }

        foreach (LinearMonomial<Flt64> monomial in right.ResultPolynomial.Monomials) {
            monomials.Add(monomial);
        }

        return new LinearPolynomial<Flt64>(
            monomials, left.ResultPolynomial.Constant.Plus(right.ResultPolynomial.Constant));
    }

    /// <summary>两个正部都求值再相加 / Evaluate both positive parts and add.</summary>
    /// <remarks>
    /// **任一侧为 `null` 就让整体为 `null`**——**这是"不知道"与"知道是 0"的区别，
    /// 填 0 会让缺失的输入看起来像"没有违背"。**
    ///
    /// **Either side being `null` makes the whole `null`** — **that is the difference between "unknown" and "known to
    /// be zero"; filling in zero would make a missing input look like "no violation".**
    /// </remarks>
    public static Flt64? EvaluateBoth(
        QuadraticPositivePartFunction left,
        QuadraticPositivePartFunction right,
        IReadOnlyDictionary<ISymbol, Flt64> values) {
        if (left.Evaluate(values) is not { } leftValue || right.Evaluate(values) is not { } rightValue) {
            return null;
        }

        return leftValue.Plus(rightValue);
    }

    /// <summary>两个正部的辅助变量 / The helpers of both positive parts.</summary>
    public static IReadOnlyList<IVariableItem> Helpers(
        QuadraticPositivePartFunction left,
        QuadraticPositivePartFunction right) {
        var helpers = new List<IVariableItem>();
        helpers.AddRange(left.HelperVariables);
        helpers.AddRange(right.HelperVariables);
        return helpers.AsReadOnly();
    }

    /// <summary>两个正部都注册约束 / Register both positive parts' constraints.</summary>
    public static Try RegisterBoth(
        QuadraticPositivePartFunction left,
        QuadraticPositivePartFunction right,
        IAbstractLinearMechanismModel<Flt64> model) {
        Try first = left.RegisterConstraints(model);
        if (first is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return right.RegisterConstraints(model);
    }

    /// <summary>两个正部都注册辅助 token / Register both positive parts' auxiliary tokens.</summary>
    public static Try RegisterTokens(
        QuadraticPositivePartFunction left,
        QuadraticPositivePartFunction right,
        IAddableTokenCollection<Flt64> tokens) {
        Try first = left.RegisterAuxiliaryTokens(tokens);
        if (first is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return right.RegisterAuxiliaryTokens(tokens);
    }
}

/// <summary>
/// 二次不等式判定：桥接后交给 `InequalityFunction`，返回 0/1 / A quadratic inequality, bridged, yielding 0 or 1.
/// </summary>
/// <remarks>
/// 对齐基线 `QuadraticInequality.kt`。
///
/// **第八个"桥接后委托"。** **与前面七个不同的是：委托目标返回的是<b>真值</b>（0 或 1），不是一个多项式值。**
///
/// **所以它的 `ResultPolynomial` 是 0/1 指示式**，**而 `Evaluate` 直接读真值分类**。
///
/// B1164, following the baseline's `QuadraticInequality.kt`.
///
/// **The eighth "bridge then delegate".** **Unlike the previous seven, the delegate yields a <b>truth value</b>
/// (0 or 1) rather than a polynomial value.**
///
/// **So its `ResultPolynomial` is a 0/1 indicator**, **and `Evaluate` reads the truth classification directly.**
/// </remarks>
public sealed class QuadraticInequalityFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _lhs;

    /// <summary>创建二次不等式判定 / Create one.</summary>
    public QuadraticInequalityFunction(
        QuadraticPolynomial<Flt64> lhs,
        Flt64 rhs,
        Comparison sign,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        Flt64? tolerance = null,
        Flt64? strictBoundary = null,
        string name = "quadratic_inequality",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(lhs);
        ArgumentNullException.ThrowIfNull(converter);

        _lhs = lhs;

        (LinearPolynomial<Flt64>[] inputs, RealVar[] bridges, QuadraticInequalityOf<Flt64>[] rows) =
            QuadraticExtremumBridging.Bridge(new[] { lhs }, converter, name);
        Bridges = bridges;
        BridgeRows = rows;

        Inner = new InequalityFunction<Flt64>(
            lhs: inputs[0],
            rhs: rhs,
            sign: sign,
            converter: converter,
            bigM: bigM,
            tolerance: tolerance,
            strictBoundary: strictBoundary,
            name: name,
            displayName: displayName);
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>左端多项式 / The left-hand polynomial.</summary>
    public QuadraticPolynomial<Flt64> Lhs => _lhs;

    /// <summary>桥接变量 / The bridge.</summary>
    public IReadOnlyList<RealVar> Bridges { get; }

    /// <summary>桥接等式 / The bridge rows.</summary>
    public IReadOnlyList<QuadraticInequalityOf<Flt64>> BridgeRows { get; }

    /// <summary>被委托的判定函数 / The delegated judgement.</summary>
    public InequalityFunction<Flt64> Inner { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables {
        get {
            var helpers = new List<IVariableItem>();
            helpers.AddRange(Bridges);
            helpers.AddRange(Inner.HelperVariables);
            return helpers.AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols =>
        QuadraticExtremumBridging.SymbolsOf(new[] { _lhs });

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial => Inner.ResultPolynomial;

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) =>
        QuadraticExtremumBridging.EvaluateDelegated(Inner, new[] { _lhs }, Bridges, values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        tokens.Add(HelperVariables);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
        Try bridged = QuadraticExtremumBridging.RegisterBridgeRows(model, BridgeRows);
        if (bridged is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return failure;
        }

        return Inner.RegisterConstraints(model);
    }
}

/// <summary>
/// 两个多项式之间差了多少：<c>|l − r|</c> / How far apart two polynomials are.
/// </summary>
/// <remarks>
/// 对齐基线 `QuadraticSlack.kt`。
///
/// **`max(l − r, 0) + max(r − l, 0)`，也就是 `|l − r|`。**
/// **两个正部分别由 `QuadraticPositivePartFunction` 承担，本类只是把它们加起来。**
///
/// **"松弛"这个名字容易读成"有符号的差"，而它是<b>无符号</b>的。**
/// **只留 `max(l − r, 0)` 会在 `l &lt; r` 时给 0，而正确值是 `r − l`——
/// 在半个定义域上恒为 0 且不报错。**
///
/// B1164, following the baseline's `QuadraticSlack.kt`.
///
/// **`max(l − r, 0) + max(r − l, 0)`, that is `|l − r|`.**
/// **Each positive part comes from `QuadraticPositivePartFunction` ; this class only adds them.**
///
/// **The name "slack" reads like a signed difference, but it is <b>unsigned</b>.**
/// **Keeping only `max(l − r, 0)` gives zero when `l &lt; r` while the correct value is `r − l` — identically zero over
/// half the domain, silently.**
/// </remarks>
public sealed class QuadraticSlackFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _left;
    private readonly QuadraticPolynomial<Flt64> _right;

    /// <summary>创建两者之间的差 / Create one.</summary>
    public QuadraticSlackFunction(
        QuadraticPolynomial<Flt64> left,
        QuadraticPolynomial<Flt64> right,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        string name = "quadratic_slack",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(converter);

        _left = left;
        _right = right;

        PositivePart = new QuadraticPositivePartFunction(
            input: QuadraticSlackSupport.Subtract(left, right, converter),
            converter: converter,
            bigM: bigM,
            name: $"{name}_positive",
            displayName: displayName);

        NegativePart = new QuadraticPositivePartFunction(
            input: QuadraticSlackSupport.Subtract(right, left, converter),
            converter: converter,
            bigM: bigM,
            name: $"{name}_negative",
            displayName: displayName);

        Name = name;
        DisplayName = displayName;
    }

    /// <summary>左端 / The left-hand side.</summary>
    public QuadraticPolynomial<Flt64> Left => _left;

    /// <summary>右端 / The right-hand side.</summary>
    public QuadraticPolynomial<Flt64> Right => _right;

    /// <summary>`max(l − r, 0)` / The positive half.</summary>
    public QuadraticPositivePartFunction PositivePart { get; }

    /// <summary>`max(r − l, 0)` / The negative half.</summary>
    public QuadraticPositivePartFunction NegativePart { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables =>
        QuadraticSlackSupport.Helpers(PositivePart, NegativePart);

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols =>
        QuadraticExtremumBridging.SymbolsOf(new[] { _left, _right });

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial =>
        QuadraticSlackSupport.Sum(PositivePart, NegativePart);

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) =>
        QuadraticSlackSupport.EvaluateBoth(PositivePart, NegativePart, values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        QuadraticSlackSupport.RegisterTokens(PositivePart, NegativePart, tokens);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) =>
        QuadraticSlackSupport.RegisterBoth(PositivePart, NegativePart, model);
}

/// <summary>
/// 超出区间的总量：<c>max(lower − x, 0) + max(x − upper, 0)</c> / The total distance outside an interval.
/// </summary>
/// <remarks>
/// 对齐基线 `QuadraticSlackRange.kt`。
///
/// **区间内为 0，低于下界为 `lower − x`，高于上界为 `x − upper`。**
/// **两侧都要算：只留一侧会在另一半恒为 0。**
///
/// **`lower > upper` 必须拒绝**：**否则"区间"是空的，两侧违背量同时为正，
/// 于是函数在任何点都非零——而模型仍然可行，只是目标值莫名其妙地大。**
///
/// B1164, following the baseline's `QuadraticSlackRange.kt`.
///
/// **Zero inside, `lower − x` below, `x − upper` above.**
/// **Both sides must be computed: keeping one makes the other half identically zero.**
///
/// **`lower > upper` must be rejected**: **otherwise the "interval" is empty, both violations are positive at once and
/// the function is non-zero everywhere — while the model stays feasible, only the objective is inexplicably large.**
/// </remarks>
public sealed class QuadraticSlackRangeFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
    private readonly QuadraticPolynomial<Flt64> _input;

    /// <summary>创建区间违背量 / Create one.</summary>
    public QuadraticSlackRangeFunction(
        QuadraticPolynomial<Flt64> input,
        Flt64 lower,
        Flt64 upper,
        IIntoValue<Flt64> converter,
        Flt64? bigM = null,
        string name = "quadratic_slack_range",
        string? displayName = null) {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(converter);

        // **倒序区间必须拒绝**，理由见类注释。
        // Reversed bounds must be rejected; see the class remarks.
        if (lower.CompareTo(upper) > 0) {
            throw new ArgumentException(
                $"the lower bound {lower} exceeds the upper bound {upper}", nameof(lower));
        }

        _input = input;
        Lower = lower;
        Upper = upper;

        // `lower − x`
        LowerViolation = new QuadraticPositivePartFunction(
            input: QuadraticSlackSupport.Subtract(
                new QuadraticPolynomial<Flt64>(
                    Array.Empty<QuadraticMonomial<Flt64>>(), lower),
                input,
                converter),
            converter: converter,
            bigM: bigM,
            name: $"{name}_lower",
            displayName: displayName);

        // `x − upper`
        UpperViolation = new QuadraticPositivePartFunction(
            input: QuadraticSlackSupport.Subtract(
                input,
                new QuadraticPolynomial<Flt64>(
                    Array.Empty<QuadraticMonomial<Flt64>>(), upper),
                converter),
            converter: converter,
            bigM: bigM,
            name: $"{name}_upper",
            displayName: displayName);

        Name = name;
        DisplayName = displayName;
    }

    /// <summary>输入多项式 / The input.</summary>
    public QuadraticPolynomial<Flt64> Input => _input;

    /// <summary>下界 / The lower bound.</summary>
    public Flt64 Lower { get; }

    /// <summary>上界 / The upper bound.</summary>
    public Flt64 Upper { get; }

    /// <summary>`max(lower − x, 0)` / The lower violation.</summary>
    public QuadraticPositivePartFunction LowerViolation { get; }

    /// <summary>`max(x − upper, 0)` / The upper violation.</summary>
    public QuadraticPositivePartFunction UpperViolation { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables =>
        QuadraticSlackSupport.Helpers(LowerViolation, UpperViolation);

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols =>
        QuadraticExtremumBridging.SymbolsOf(new[] { _input });

    /// <inheritdoc/>
    public LinearPolynomial<Flt64> ResultPolynomial =>
        QuadraticSlackSupport.Sum(LowerViolation, UpperViolation);

    /// <inheritdoc/>
    public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) =>
        QuadraticSlackSupport.EvaluateBoth(LowerViolation, UpperViolation, values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
        QuadraticSlackSupport.RegisterTokens(LowerViolation, UpperViolation, tokens);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) =>
        QuadraticSlackSupport.RegisterBoth(LowerViolation, UpperViolation, model);
}
