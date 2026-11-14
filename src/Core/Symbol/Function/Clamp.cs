#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
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
/// **截断函数：把输入夹在 `[lower, upper]` 之间** / Clamp: squeeze the input into `[lower, upper]`.
/// </summary>
/// <remarks>
/// **B1131 按基线 `CompositeFunctions.kt` 的 `ClampFunction` 补。**
///
/// **它不是一个新的数学原语**：**基线自己也是用 `MaxFunction` 与 `MinFunction` 拼出来的**
/// （`max(input, lower)` 再 `min(…, upper)`）——
/// **所以这里照做，不另写一套编码。**
/// **手写一套"夹"的约束会与既有的 max/min 编码产生两套 Big-M，
/// 而两套 Big-M 在同一模型里会互相挤掉对方的可行域。**
///
/// **The `ClampFunction` from the baseline's `CompositeFunctions.kt`, added in B1131.**
///
/// **It is not a new mathematical primitive**: **the baseline itself builds it from `MaxFunction` and `MinFunction`**
/// (`max(input, lower)` then `min(…, upper)`) —
/// **so this follows suit rather than writing another encoding.**
/// **Hand-writing "clamp" constraints would produce a second set of Big-Ms alongside the existing max/min ones,
/// and two sets in one model eat into each other's feasible region.**
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class ClampFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly MaxFunction<V> _lowerBound;
    private readonly MinFunction<V> _upperBound;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    /// <summary>创建截断函数 / Create a clamp function.</summary>
    /// <param name="input">输入多项式 / Input polynomial.</param>
    /// <param name="lower">下界 / Lower bound.</param>
    /// <param name="upper">上界 / Upper bound.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public ClampFunction(
        LinearPolynomial<V> input,
        V lower,
        V upper,
        IIntoValue<V> converter,
        string name = "clamp",
        string? displayName = null) {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Converter = converter ?? throw new ArgumentNullException(nameof(converter));

        // **`lower > upper` 是调用方的错，不是"空区间"**——
        // **而空区间会让整个模型不可行，且报错会指向求解器而不是这里。**
        // **所以在这里就拒绝，让错误落在能看懂的地方。**
        //
        // **`lower > upper` is the caller's mistake, not an "empty interval"** —
        // **an empty interval makes the whole model infeasible and the error points at the solver instead of here.**
        // **So it is rejected here, where the message makes sense.**
        if (lower.CompareTo(upper) > 0) {
            throw new ArgumentException(
                $"clamp lower bound must not exceed upper bound (lower={lower}, upper={upper})",
                nameof(lower));
        }

        Lower = lower;
        Upper = upper;
        Name = name;
        DisplayName = displayName;

        _lowerBound = new MaxFunction<V>(
            polynomials: new[] { input, Constant(lower, converter) },
            converter: converter,
            name: $"{name}_lower",
            displayName: displayName);

        _upperBound = new MinFunction<V>(
            polynomials: new[] { _lowerBound.ResultPolynomial, Constant(upper, converter) },
            converter: converter,
            name: $"{name}_upper",
            displayName: displayName);

        _helperVariables = new List<IVariableItem>(_lowerBound.HelperVariables);
        foreach (IVariableItem helper in _upperBound.HelperVariables) {
            ((List<IVariableItem>)_helperVariables).Add(helper);
        }
    }

    /// <summary>输入多项式 / Input polynomial.</summary>
    public LinearPolynomial<V> Input { get; }

    /// <summary>下界 / Lower bound.</summary>
    public V Lower { get; }

    /// <summary>上界 / Upper bound.</summary>
    public V Upper { get; }

    /// <summary>数值转换器 / Numeric converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Input);

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _upperBound.ResultPolynomial;

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        Result<V?, ErrorCode, Error<ErrorCode>> raw = FunctionEvaluation.Polynomial(Input, values);
        if (raw is Failed<V?, ErrorCode, Error<ErrorCode>> failed) {
            return failed;
        }

        if (raw.Value is not { } value) {
            return new Ok<V?, ErrorCode, Error<ErrorCode>>(null);
        }

        // **夹的顺序不能反**：先下界再上界，与基线一致。
        // **反过来（先上界再下界）在 `lower > upper` 时结果不同**——**虽然构造器已经拒绝那种输入，
        // 但顺序本身是这个函数定义的一部分，不该依赖前置检查才对。**
        //
        // **The order must not be reversed**: lower first, then upper, matching the baseline.
        // **Reversing it gives a different answer when `lower > upper`** — **the constructor rejects that input,
        // but the order is part of the definition and should not rely on the guard.**
        V clamped = value.CompareTo(Lower) < 0
            ? Lower
            : (value.CompareTo(Upper) > 0 ? Upper : value);
        return new Ok<V?, ErrorCode, Error<ErrorCode>>(clamped);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        Try lower = _lowerBound.RegisterAuxiliaryTokens(tokens);
        if (lower is Failed<Success, ErrorCode, Error<ErrorCode>> failedLower) {
            return failedLower;
        }

        return _upperBound.RegisterAuxiliaryTokens(tokens);
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        Try lower = _lowerBound.RegisterConstraints(model);
        if (lower is Failed<Success, ErrorCode, Error<ErrorCode>> failedLower) {
            return failedLower;
        }

        return _upperBound.RegisterConstraints(model);
    }

    /// <summary>把一个标量包成常数多项式 / Wrap a scalar as a constant polynomial.</summary>
    private static LinearPolynomial<V> Constant(V value, IIntoValue<V> converter) =>
        new(Array.Empty<LinearMonomial<V>>(), value);
}
