#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// **返回最小值所在的下标，而不是最小值本身** / Return the index of the minimum, not the minimum.
/// </summary>
/// <remarks>
/// 对齐基线 OrderStatistic.kt 的 ArgMinFunction。
///
/// **`argmin` 与 `max` 的区别只有一个，而它足够让人写错**：
/// **`max` 给的是值，`argmin` 给的是"哪个"。** 而这里的"哪个"是**用选择器变量的加权和**表达的——
/// **下标 i 的系数就是 i**（0 号选择器不参与，因为它的系数是 0）。
///
/// **所以结果多项式的常数项必须是 0**：**下标 0 对应"全是零"，而它不能靠常数项表达。**
///
/// The ArgMinFunction from its OrderStatistic.kt.
///
/// **`argmin` differs from `max` in exactly one way, and that is enough to get wrong**.
/// **`max` gives the value, `argmin` gives "which one".** And "which one" is expressed **as a weighted sum of the
/// selector variables** — **the coefficient of index i is i** (selector zero does not participate, since its
/// coefficient is zero).
///
/// **So the result polynomial's constant term has to be zero**: **index 0 means "all selectors off", and that cannot be
/// expressed by a constant term.**
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class ArgMinFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly MinFunction<V> _inner;

    /// <summary>创建 argmin 函数 / Create an argmin function.</summary>
    /// <param name="polynomials">候选多项式 / Candidate polynomials.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选 Big-M / Optional Big-M.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public ArgMinFunction(
        IReadOnlyList<LinearPolynomial<V>> polynomials,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "argmin",
        string? displayName = null) {
        if (polynomials is null || polynomials.Count == 0) {
            throw new ArgumentException("argmin needs at least one candidate", nameof(polynomials));
        }

        Polynomials = polynomials.ToArray();
        Converter = converter ?? throw new ArgumentNullException(nameof(converter));

        _inner = new MinFunction<V>(
            polynomials: Polynomials,
            converter: converter,
            bigM: bigM,
            name: name,
            displayName: displayName);

        Name = name;
        DisplayName = displayName;
    }

    /// <summary>候选多项式 / Candidate polynomials.</summary>
    public IReadOnlyList<LinearPolynomial<V>> Polynomials { get; }

    /// <summary>数值转换器 / Numeric converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>选择器变量 / Selector variables.</summary>
    public IReadOnlyList<BinVar> SelectorVars => _inner.SelectorVars;

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _inner.HelperVariables;

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Polynomials);

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => IndexPolynomial(SelectorVars, Converter);

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) {
        int best = -1;
        V? bestValue = null;

        for (int index = 0; index < Polynomials.Count; ++index) {
            Result<V?, ErrorCode, Error<ErrorCode>> raw = FunctionEvaluation.Polynomial(Polynomials[index], values);
            if (raw.Value is not { } value) {
                return null;
            }

            // **严格小于才换**：相等时保留先出现的那个。
            // **这不是"随便选一个"——`argmin` 在并列时必须有确定的答案，否则同一组输入会给出不同下标，
            // 而"结果不稳定"在优化里等于"结果不可复现"。**
            //
            // **Strictly smaller replaces**: ties keep the first occurrence.
            // **This is not arbitrary — `argmin` has to be deterministic on ties, or the same inputs yield different
            // indices, and unstable results mean unreproducible ones in optimisation.**
            if (bestValue is null || value.CompareTo(bestValue.Value) < 0) {
                best = index;
                bestValue = value;
            }
        }

        return best < 0 ? null : Converter.IntoValue(new Flt64(best));
    }

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        new Ok<V?, ErrorCode, Error<ErrorCode>>(Evaluate(values));

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        _inner.RegisterAuxiliaryTokens(tokens);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) =>
        _inner.RegisterConstraints(model);

    /// <summary>**把选择器变量加权成下标** / Weight the selectors into an index.</summary>
    /// <remarks>
    /// **第 i 个选择器的系数是 i，第 0 个不参与**（系数 0，加进去也没意义）。
    /// **常数项必须是 0**——**下标 0 对应"全是零"，不能靠常数项表达。**
    ///
    /// **Selector i carries coefficient i and selector 0 does not participate** (its coefficient is zero anyway).
    /// **The constant term has to be zero** — **index 0 means "all selectors off", which a constant term cannot express.**
    /// </remarks>
    private static LinearPolynomial<V> IndexPolynomial(IReadOnlyList<BinVar> selectors, IIntoValue<V> converter) {
        var monomials = new List<LinearMonomial<V>>();
        for (int index = 1; index < selectors.Count; ++index) {
            V coefficient = converter.Zero;
            for (int step = 0; step < index; ++step) {
                coefficient = coefficient.Plus(converter.One);
            }

            monomials.Add(new LinearMonomial<V>(coefficient, selectors[index]));
        }

        return new LinearPolynomial<V>(monomials.AsReadOnly(), converter.Zero);
    }
}
