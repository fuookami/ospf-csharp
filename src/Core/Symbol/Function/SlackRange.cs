#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>到闭区间的距离 / Distance to a closed interval.</summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class SlackRangeFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly MaxFunction<V> _inner;

    /// <summary>输入多项式快照 / Input polynomial snapshot.</summary>
    public LinearPolynomial<V> Input { get; }
    /// <summary>闭区间下界 / Closed-interval lower bound.</summary>
    public V Lower { get; }
    /// <summary>闭区间上界 / Closed-interval upper bound.</summary>
    public V Upper { get; }
    /// <summary>显式 Big-M；null 使用默认策略 / Explicit Big-M; null uses the default policy.</summary>
    public V? BigM { get; }
    /// <summary>距离结果变量 / Distance result variable.</summary>
    public RealVar ResultVar => _inner.ResultVar;
    /// <summary>最大值选择变量 / Maximum-selector variables.</summary>
    public IReadOnlyList<BinVar> SelectorVars => _inner.SelectorVars;
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _inner.HelperVariables;
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Input);
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _inner.ResultPolynomial;
    /// <inheritdoc/>
    public string Name {
        get => _inner.Name;
        set => _inner.Name = value;
    }
    /// <inheritdoc/>
    public string? DisplayName {
        get => _inner.DisplayName;
        set => _inner.DisplayName = value;
    }

    private SlackRangeFunction(
        LinearPolynomial<V> input,
        V lower,
        V upper,
        V? bigM,
        MaxFunction<V> inner) {
        Input = input;
        Lower = lower;
        Upper = upper;
        BigM = bigM;
        _inner = inner;
    }

    /// <summary>校验并创建区间距离函数 / Validate and create a range-distance function.</summary>
    /// <param name="input">输入线性多项式 / Input linear polynomial.</param>
    /// <param name="lower">闭区间下界 / Closed-interval lower bound.</param>
    /// <param name="upper">闭区间上界 / Closed-interval upper bound.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选正 Big-M / Optional positive Big-M.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或校验失败 / Function or validation failure.</returns>
    public static Result<SlackRangeFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? input,
        V lower,
        V upper,
        IIntoValue<V>? converter,
        V? bigM = null,
        string name = "slackRange",
        string? displayName = null) {
        try {
            if (input is null || converter is null || input.Monomials is null ||
                input.Monomials.Any(monomial => monomial is null || monomial.Symbol is null) ||
                !FunctionEvaluation.IsUsable(lower) || !FunctionEvaluation.IsUsable(upper) ||
                !BigMValues.IsUsable(lower, converter) || !BigMValues.IsUsable(upper, converter) ||
                !FunctionEvaluation.Comparable(lower, upper) || lower.Gr(upper) ||
                !FunctionEvaluation.IsUsable(converter.Zero) || !ExtremumValues.CanNegate(converter.One, converter) ||
                !FunctionEvaluation.IsUsable(input.Constant) ||
                !ExtremumValues.IsUsable(input, converter) ||
                input.Monomials.Any(monomial => !FunctionEvaluation.IsUsable(monomial.Coefficient)) ||
                (bigM is { } explicitBigM &&
                    (!FunctionEvaluation.IsUsable(explicitBigM) ||
                    !ExtremumValues.CanNegate(explicitBigM, converter) ||
                    !FunctionEvaluation.Comparable(explicitBigM, converter.Zero) ||
                    !explicitBigM.Gr(converter.Zero)))) {
                return Invalid();
            }

            Result<V?, ErrorCode, Error<ErrorCode>> below = FunctionEvaluation.Difference(lower, input.Constant);
            Result<V?, ErrorCode, Error<ErrorCode>> above = FunctionEvaluation.Difference(input.Constant, upper);
            if (below.IsFailed || above.IsFailed || below.Value is not { } belowConstant ||
                above.Value is not { } aboveConstant ||
                !ExtremumValues.CanNegate(belowConstant, converter) ||
                !ExtremumValues.CanNegate(aboveConstant, converter)) {
                return Invalid();
            }

            var snapshot = new LinearPolynomial<V>(Array.AsReadOnly(input.Monomials.ToArray()), input.Constant);
            LinearPolynomial<V>[] candidates = {
                new LinearPolynomial<V>(
                    snapshot.Monomials.Select(monomial =>
                        new LinearMonomial<V>(monomial.Coefficient.Negate(), monomial.Symbol)).ToArray(),
                    belowConstant
                ),
                new LinearPolynomial<V>(snapshot.Monomials, aboveConstant),
                new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), converter.Zero),
            };
            var inner = new MaxFunction<V>(
                polynomials: candidates,
                converter: converter,
                bigM: bigM,
                name: name,
                displayName: displayName
            );
            // 极值推导经 Flt64 舍入，不能安全收紧泛型结果域 / Flt64-derived extrema cannot safely tighten a generic result domain.
            Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> nonnegative = ValueRange<Flt64>.Geq(Flt64.Zero);
            if (nonnegative.IsFailed) {
                return Invalid();
            }
            inner.ResultVar.Range.SetRange(nonnegative.Value);
            return Results.Ok(new SlackRangeFunction<V>(
                input: snapshot,
                lower: lower,
                upper: upper,
                bigM: bigM,
                inner: inner
            ));
        }
        catch (ArithmeticException) {
            return Invalid();
        }
        catch (InvalidOperationException) {
            return Invalid();
        }
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => _inner.Evaluate(values);

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) =>
        _inner.TryEvaluate(values);

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) =>
        _inner.RegisterAuxiliaryTokens(tokens);

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) =>
        _inner.RegisterConstraints(model);

    private static Result<SlackRangeFunction<V>, ErrorCode, Error<ErrorCode>> Invalid() =>
        Results.Failed<SlackRangeFunction<V>>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "区间距离函数的输入、边界或 Big-M 无效。 / Invalid range-distance input, bounds, or Big-M."
        ));
}
