#nullable enable

using System;
using System.Collections.Generic;
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
/// 死区函数：`max(|x| - delta, 0)` / Dead zone: `max(|x| - delta, 0)`.
/// </summary>
/// <remarks>
/// DeadZoneFunction 的语义与编码。
///
/// 它表示“在 ±delta 之内没有反应，超出之后按超出量线性增长”——
/// 机械间隙、传感器死区、控制器的容差带都是这个形状。
///
/// 实现上不另写编码：这个函数本身就是 AbsFunction 再 MaxFunction 拼出来的，
/// 这里不手写——手写会产生第二套 Big-M，而两套 Big-M 在同一模型里会互相挤掉对方的可行域。
///
/// The dead-zone shape.
///
/// It means “no response within ±delta, linear in the excess beyond it” — mechanical backlash, sensor dead bands
/// and controller tolerance bands all take this shape.
///
/// No separate encoding is written: the function itself composes AbsFunction with MaxFunction, and this follows
/// suit — hand-writing one would produce a second set of Big-Ms, and two sets in one model eat into each other's
/// feasible region.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class DeadZoneFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly AbsFunction<V> _absolute;
    private readonly MaxFunction<V> _positivePart;
    private readonly IReadOnlyList<IVariableItem> _helperVariables;

    /// <summary>创建死区函数 / Create a dead zone function.</summary>
    /// <param name="input">输入多项式 / Input polynomial.</param>
    /// <param name="delta">死区半宽，必须非负 / Dead-zone half-width, must be non-negative.</param>
    /// <param name="converter">数值转换器 / Numeric converter.</param>
    /// <param name="bigM">可选 Big-M / Optional Big-M.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    public DeadZoneFunction(
        LinearPolynomial<V> input,
        V delta,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "dead_zone",
        string? displayName = null) {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Converter = converter ?? throw new ArgumentNullException(nameof(converter));

        // 负的死区半宽不是“反过来的死区”，而是无意义的输入：
        // 它会让 |x| - delta 恒为正，于是死区消失，而调用方以为自己设了容差。
        //
        // A negative half-width is not “a dead zone the other way round” but a meaningless input:
        // it makes |x| - delta always positive, so the dead zone vanishes while the caller believes a tolerance is set.
        if (delta.CompareTo(converter.Zero) < 0) {
            throw new ArgumentException(
                $"dead zone half-width must be non-negative (delta={delta})",
                nameof(delta));
        }

        Delta = delta;
        Name = name;
        DisplayName = displayName;

        _absolute = new AbsFunction<V>(
            polynomial: input,
            converter: converter,
            bigM: bigM,
            name: $"{name}_abs",
            displayName: displayName);

        // 超出量 = |x| - delta，再取它与 0 的较大者。
        //
        // The excess is |x| - delta, then the larger of that and zero.
        LinearPolynomial<V> excess = _absolute.ResultPolynomial - delta;
        _positivePart = new MaxFunction<V>(
            polynomials: new[] { excess, Zero(converter) },
            converter: converter,
            bigM: bigM,
            name: $"{name}_positive_part",
            displayName: displayName);

        var helpers = new List<IVariableItem>(_absolute.HelperVariables);
        foreach (IVariableItem helper in _positivePart.HelperVariables) {
            helpers.Add(helper);
        }

        _helperVariables = helpers.AsReadOnly();
    }

    /// <summary>输入多项式 / Input polynomial.</summary>
    public LinearPolynomial<V> Input { get; }

    /// <summary>死区半宽 / Dead-zone half-width.</summary>
    public V Delta { get; }

    /// <summary>数值转换器 / Numeric converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>内部组合的两个函数 / The two functions this composes.</summary>
    public IReadOnlyList<IMathFunctionSymbol<V>> InnerFunctions =>
        new IMathFunctionSymbol<V>[] { _absolute, _positivePart };

    /// <inheritdoc/>
    public string Name { get; set; }

    /// <inheritdoc/>
    public string? DisplayName { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables => _helperVariables;

    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Input);

    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _positivePart.ResultPolynomial;

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

        // 死区之内返回 0，之外返回超出量。这里直接算，
        // 因为这是 evaluate 的语义定义，而不是约束编码——编码由 inner functions 负责。
        //
        // Zero inside the dead zone, the excess outside. Computed directly because this is the evaluation
        // semantics, not the constraint encoding — the encoding belongs to the inner functions.
        // V 没有定义运算符 -，必须走 Minus——与 AbsFunction 内部的做法一致。
        // V does not define operator -, so Minus is used, matching what AbsFunction does internally.
        V magnitude = value.CompareTo(Converter.Zero) < 0 ? Converter.Zero.Minus(value) : value;
        V excess = magnitude.Minus(Delta);
        V result = excess.CompareTo(Converter.Zero) > 0 ? excess : Converter.Zero;
        return new Ok<V?, ErrorCode, Error<ErrorCode>>(result);
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        Try absolute = _absolute.RegisterAuxiliaryTokens(tokens);
        if (absolute is Failed<Success, ErrorCode, Error<ErrorCode>> failedAbsolute) {
            return failedAbsolute;
        }

        return _positivePart.RegisterAuxiliaryTokens(tokens);
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        Try absolute = _absolute.RegisterConstraints(model);
        if (absolute is Failed<Success, ErrorCode, Error<ErrorCode>> failedAbsolute) {
            return failedAbsolute;
        }

        return _positivePart.RegisterConstraints(model);
    }

    /// <summary>零多项式 / The zero polynomial.</summary>
    private static LinearPolynomial<V> Zero(IIntoValue<V> converter) =>
        new(Array.Empty<LinearMonomial<V>>(), converter.Zero);
}
