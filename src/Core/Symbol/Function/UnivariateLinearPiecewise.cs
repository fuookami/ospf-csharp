#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>单变量分段线性函数 / Univariate piecewise-linear function.</summary>
/// <remarks>
/// 每段为闭区间；不连续断点处模型可选择任一相邻段，直接求值选择前一段。
/// Segments are closed; at a discontinuous breakpoint the model may choose either segment, while direct evaluation chooses the first.
/// 显式 Big-M 的充分性由调用方负责。 / The caller is responsible for the sufficiency of an explicit Big-M.
/// FltX 原生值及 Flt64 编码分别校验；其他类型须精确编码。 / Native and Flt64-encoded FltX values are checked separately; other types require exact encoding.
/// </remarks>
/// <typeparam name="V">输入和系数数值类型 / Input and coefficient numeric type.</typeparam>
public sealed class UnivariateLinearPiecewiseFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, IModelAwareFunctionPreparation<V>, IUnivariatePiecewiseFunction<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    UnivariateLinearPiecewiseFunction<V> IUnivariatePiecewiseFunction<V>.Piecewise => this;
    private readonly IIntoValue<V> _converter;
    private readonly LinearPolynomial<V> _expanded;
    private readonly BoundDependencySnapshot<V>? _inputDependencies;
    private readonly BoundDependencySnapshot<V> _helperDependencies;
    private readonly IReadOnlyList<V> _bigM;
    private readonly V _zero;
    private readonly V _one;

    /// <summary>输入多项式的浅快照 / Shallow snapshot of the input polynomial.</summary>
    public LinearPolynomial<V> Input { get; }
    /// <summary>严格递增的断点快照 / Snapshot of strictly increasing breakpoints.</summary>
    public IReadOnlyList<V> Breakpoints { get; }
    /// <summary>各段斜率快照 / Snapshot of segment slopes.</summary>
    public IReadOnlyList<V> Slopes { get; }
    /// <summary>各段截距快照 / Snapshot of segment intercepts.</summary>
    public IReadOnlyList<V> Intercepts { get; }
    /// <summary>原生、编码及舍入后端点的外包络 / Outward envelope of native, encoded and rounded endpoints.</summary>
    public LinearPolynomialBounds<V> OutputBounds { get; }
    /// <summary>输入辅助变量 / Auxiliary input variable.</summary>
    public RealVar InputVar { get; }
    /// <summary>结果变量 / Result variable.</summary>
    public RealVar ResultVar { get; }
    /// <summary>各段的激活变量 / Segment activation variables.</summary>
    public IReadOnlyList<BinVar> SelectorVars { get; }
    /// <summary>各段的未激活变量 / Segment deactivation variables.</summary>
    public IReadOnlyList<BinVar> InactiveVars { get; }
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables { get; }
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(Input);
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => new(
        Array.AsReadOnly(new[] { new LinearMonomial<V>(_one, ResultVar) }), _zero
    );

    private UnivariateLinearPiecewiseFunction(
        LinearPolynomial<V> input,
        LinearPolynomial<V> expanded,
        IReadOnlyList<V> breakpoints,
        IReadOnlyList<V> slopes,
        IReadOnlyList<V> intercepts,
        IReadOnlyList<V> bigM,
        LinearPolynomialBounds<V> outputBounds,
        ValueRange<Flt64> inputRange,
        ValueRange<Flt64> outputRange,
        BoundDependencySnapshot<V>? inputDependencies,
        IIntoValue<V> converter,
        V zero,
        V one,
        string name,
        string? displayName) {
        Input = input;
        _expanded = expanded;
        Breakpoints = breakpoints;
        Slopes = slopes;
        Intercepts = intercepts;
        _bigM = bigM;
        OutputBounds = outputBounds;
        _inputDependencies = inputDependencies;
        _converter = converter;
        _zero = zero;
        _one = one;
        Name = name;
        DisplayName = displayName;
        InputVar = new RealVar($"{name}_input");
        InputVar.Range.SetRange(inputRange);
        ResultVar = new RealVar($"{name}_y");
        ResultVar.Range.SetRange(outputRange);
        SelectorVars = Array.AsReadOnly(Enumerable.Range(0, slopes.Count)
            .Select(index => new BinVar($"{name}_s{index}")).ToArray());
        InactiveVars = Array.AsReadOnly(Enumerable.Range(0, slopes.Count)
            .Select(index => new BinVar($"{name}_inactive{index}")).ToArray());
        HelperVariables = Array.AsReadOnly<IVariableItem>(new IVariableItem[] { ResultVar, InputVar }
            .Concat(SelectorVars).Concat(InactiveVars).ToArray());
        _helperDependencies = new BoundDependencySnapshot<V>(new[] {
            new LinearPolynomial<V>(HelperVariables.Select(item => new LinearMonomial<V>(one, item)).ToArray(), zero),
        });
    }

    /// <summary>校验并创建分段函数 / Validate and create a piecewise function.</summary>
    /// <param name="input">输入线性多项式 / Input linear polynomial.</param>
    /// <param name="breakpoints">严格递增的断点 / Strictly increasing breakpoints.</param>
    /// <param name="slopes">各段斜率 / Segment slopes.</param>
    /// <param name="intercepts">各段截距 / Segment intercepts.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="bigM">可选正 Big-M，调用方须保证充分 / Optional positive Big-M, whose sufficiency is the caller's responsibility.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或结构化失败 / Function or structured failure.</returns>
    public static Result<UnivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? input,
        IReadOnlyList<V>? breakpoints,
        IReadOnlyList<V>? slopes,
        IReadOnlyList<V>? intercepts,
        IIntoValue<V>? converter,
        V? bigM = null,
        string name = "piecewise",
        string? displayName = null) {
        try {
            if (input?.Monomials is null || converter is null || breakpoints is null || slopes is null || intercepts is null ||
                input.Monomials.Any(term => term is null || term.Symbol is null)) {
                return Invalid();
            }
            V[] points = breakpoints.ToArray();
            V[] gradients = slopes.ToArray();
            V[] offsets = intercepts.ToArray();
            if (points.Length < 2 || gradients.Length != points.Length - 1 || offsets.Length != gradients.Length ||
                !Exact(
                    value: converter.Zero,
                    converter: converter,
                    exact: out ExactBoundValue zero
                ) || zero.Sign != 0 ||
                !Exact(
                    value: converter.One,
                    converter: converter,
                    exact: out ExactBoundValue one
                ) ||
                one.CompareTo(new ExactBoundValue(BigInteger.One, BigInteger.One)) != 0 ||
                !TryNegate(
                    value: converter.One,
                    converter: converter,
                    result: out _
                )) {
                return Invalid();
            }
            var pointValues = new ExactBoundValue[points.Length];
            var encodedPoints = new ExactBoundValue[points.Length];
            var slopeValues = new ExactBoundValue[gradients.Length];
            var encodedSlopes = new ExactBoundValue[gradients.Length];
            var offsetValues = new ExactBoundValue[offsets.Length];
            var encodedOffsets = new ExactBoundValue[offsets.Length];
            for (int i = 0; i < points.Length; ++i) {
                if (!ReadValues(
                        value: points[i],
                        converter: converter,
                        natural: out pointValues[i],
                        encoded: out encodedPoints[i]
                    ) || !TryNegate(
                        value: points[i],
                        converter: converter,
                        result: out _
                    ) || i > 0 && (pointValues[i - 1].CompareTo(pointValues[i]) >= 0 ||
                        encodedPoints[i - 1].CompareTo(encodedPoints[i]) >= 0)) {
                    return Invalid();
                }
            }
            ExactBoundValue? outputLower = null;
            ExactBoundValue? outputUpper = null;
            for (int i = 0; i < gradients.Length; ++i) {
                if (!ReadValues(
                        value: gradients[i],
                        converter: converter,
                        natural: out slopeValues[i],
                        encoded: out encodedSlopes[i]
                    ) || !TryNegate(
                        value: gradients[i],
                        converter: converter,
                        result: out _
                    ) || !ReadValues(
                        value: offsets[i],
                        converter: converter,
                        natural: out offsetValues[i],
                        encoded: out encodedOffsets[i]
                    ) || !TryNegate(
                        value: offsets[i],
                        converter: converter,
                        result: out V negativeOffset
                    )) {
                    return Invalid();
                }
                for (int endpoint = i; endpoint <= i + 1; ++endpoint) {
                    Include(slopeValues[i].Multiply(pointValues[endpoint]).Add(offsetValues[i]));
                    Include(encodedSlopes[i].Multiply(encodedPoints[endpoint]).Add(encodedOffsets[i]));
                    Result<V?, ErrorCode, Error<ErrorCode>> product = FunctionEvaluation.Product(gradients[i], points[endpoint]);
                    if (!product.IsOk || product.Value is not { } scaled) {
                        return Invalid();
                    }
                    Result<V?, ErrorCode, Error<ErrorCode>> evaluated = FunctionEvaluation.Difference(scaled, negativeOffset);
                    if (!evaluated.IsOk || evaluated.Value is not { } endpointValue || !ReadValues(
                            value: endpointValue,
                            converter: converter,
                            natural: out ExactBoundValue rounded,
                            encoded: out ExactBoundValue encodedRounded
                        )) {
                        return Invalid();
                    }
                    Include(rounded);
                    Include(encodedRounded);
                }
            }
            void Include(ExactBoundValue value) {
                outputLower = outputLower is null || value.CompareTo(outputLower.Value) < 0 ? value : outputLower;
                outputUpper = outputUpper is null || value.CompareTo(outputUpper.Value) > 0 ? value : outputUpper;
            }
            ExactBoundValue inputLower = pointValues[0].CompareTo(encodedPoints[0]) <= 0 ? pointValues[0] : encodedPoints[0];
            ExactBoundValue inputUpper = pointValues[^1].CompareTo(encodedPoints[^1]) >= 0 ? pointValues[^1] : encodedPoints[^1];
            if (outputLower is null || outputUpper is null ||
                !outputLower.Value.TryRound(
                    converter: converter,
                    upper: false,
                    result: out V lower
                ) || !outputUpper.Value.TryRound(
                    converter: converter,
                    upper: true,
                    result: out V upper
                ) || !outputLower.Value.TryRound(
                    converter: IIntoValue<Flt64>.Identity,
                    upper: false,
                    result: out Flt64 solverLower
                ) || !outputUpper.Value.TryRound(
                    converter: IIntoValue<Flt64>.Identity,
                    upper: true,
                    result: out Flt64 solverUpper
                ) ||
                !inputLower.TryRound(
                    converter: IIntoValue<Flt64>.Identity,
                    upper: false,
                    result: out Flt64 solverInputLower
                ) || !inputUpper.TryRound(
                    converter: IIntoValue<Flt64>.Identity,
                    upper: true,
                    result: out Flt64 solverInputUpper
                ) || !BigMValues.IsUsable(solverLower) || !BigMValues.IsUsable(solverUpper) ||
                !BigMValues.IsUsable(solverInputLower) || !BigMValues.IsUsable(solverInputUpper)) {
                return Invalid();
            }
            Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> outputRange = ValueRange<Flt64>.Of(solverLower, solverUpper);
            Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> inputRange = ValueRange<Flt64>.Of(solverInputLower, solverInputUpper);
            if (!inputRange.IsOk || !outputRange.IsOk || !ExactBoundValue.TryRead(solverLower, out ExactBoundValue solverOutputLower) ||
                !ExactBoundValue.TryRead(solverUpper, out ExactBoundValue solverOutputUpper) ||
                !ExactBoundValue.TryRead(solverInputLower, out ExactBoundValue actualInputLower) ||
                !ExactBoundValue.TryRead(solverInputUpper, out ExactBoundValue actualInputUpper)) {
                return Invalid();
            }
            var snapshot = new LinearPolynomial<V>(Array.AsReadOnly(input.Monomials.ToArray()), input.Constant);
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> expansion =
                new SymbolFlatten.LinearFlattenData<V>(snapshot.Monomials, snapshot.Constant).ExpandIntermediateSymbols();
            if (!expansion.IsOk) {
                return Invalid();
            }
            var expanded = new LinearPolynomial<V>(
                Array.AsReadOnly(expansion.Value.Monomials.ToArray()), expansion.Value.Constant);
            if (!ExactLinearExpansion.Matches(
                    input: snapshot,
                    expanded: expanded,
                    converter: converter
                )) {
                return Invalid();
            }
            BoundDependencySnapshot<V>? dependencies = bigM is null ? new BoundDependencySnapshot<V>(new[] { expanded }) : null;
            if (dependencies is not null) {
                LinearPolynomialBounds<V>? bounds = BigMValues.FiniteBounds(expanded, converter);
                double freeBound = Continuous.Instance.Maximum.ToDouble();
                if (!dependencies.IsCurrent() || bounds is null ||
                    converter.FromValue(bounds.LowerBound).ToDouble() <= -freeBound ||
                    converter.FromValue(bounds.UpperBound).ToDouble() >= freeBound) {
                    return Invalid();
                }
            }
            if (bigM is { } explicitM && (!ReadValues(
                    value: explicitM,
                    converter: converter,
                    natural: out ExactBoundValue checkedM,
                    encoded: out ExactBoundValue encodedM
                ) || checkedM.Sign <= 0 || encodedM.Sign <= 0 || !TryNegate(
                    value: explicitM,
                    converter: converter,
                    result: out _
                ))) {
                return Invalid();
            }
            var bigMs = new V[gradients.Length];
            for (int i = 0; i < gradients.Length; ++i) {
                ExactBoundValue required = one;
                foreach ((ExactBoundValue[] pointsForGraph, ExactBoundValue[] slopesForGraph, ExactBoundValue[] offsetsForGraph) in new[] {
                    (pointValues, slopeValues, offsetValues),
                    (encodedPoints, encodedSlopes, encodedOffsets),
                }) {
                    ExactBoundValue firstLine = slopesForGraph[i].Multiply(actualInputLower).Add(offsetsForGraph[i]);
                    ExactBoundValue lastLine = slopesForGraph[i].Multiply(actualInputUpper).Add(offsetsForGraph[i]);
                    ExactBoundValue lineLower = firstLine.CompareTo(lastLine) <= 0 ? firstLine : lastLine;
                    ExactBoundValue lineUpper = firstLine.CompareTo(lastLine) >= 0 ? firstLine : lastLine;
                    foreach (ExactBoundValue candidate in new[] {
                        pointsForGraph[i].Add(actualInputLower.Negate()),
                        actualInputUpper.Add(pointsForGraph[i + 1].Negate()),
                        solverOutputUpper.Add(lineLower.Negate()),
                        lineUpper.Add(solverOutputLower.Negate()),
                    }) {
                        required = required.CompareTo(candidate) >= 0 ? required : candidate;
                    }
                }
                if (bigM is { } value) {
                    bigMs[i] = value;
                }
                else if (!required.TryRoundSolverUpper(converter, out bigMs[i]) ||
                    !TryNegate(
                        value: bigMs[i],
                        converter: converter,
                        result: out _
                    )) {
                    return Invalid();
                }
            }
            if (dependencies is not null && !dependencies.IsCurrent()) {
                return Invalid();
            }
            return Results.Ok(new UnivariateLinearPiecewiseFunction<V>(
                input: snapshot,
                expanded: expanded,
                breakpoints: Array.AsReadOnly(points),
                slopes: Array.AsReadOnly(gradients),
                intercepts: Array.AsReadOnly(offsets),
                bigM: Array.AsReadOnly(bigMs),
                outputBounds: new LinearPolynomialBounds<V>(lower, upper),
                inputRange: inputRange.Value,
                outputRange: outputRange.Value,
                inputDependencies: dependencies,
                converter: converter,
                zero: converter.Zero,
                one: converter.One,
                name: name,
                displayName: displayName
            ));
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid();
        }
    }

    /// <inheritdoc/>
    public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => TryEvaluate(values).Value;

    /// <inheritdoc/>
    public Result<V?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, V> values) {
        try {
            Result<V?, ErrorCode, Error<ErrorCode>> input = FunctionEvaluation.Polynomial(Input, values);
            if (!input.IsOk || input.Value is not { } value) {
                return input;
            }
            if (!ExactBoundValue.TryRead(value, out ExactBoundValue numeric)) {
                return FunctionEvaluation.Invalid<V>();
            }
            for (int i = 0; i < Slopes.Count; ++i) {
                if (ExactBoundValue.TryRead(Breakpoints[i], out ExactBoundValue lower) &&
                    ExactBoundValue.TryRead(Breakpoints[i + 1], out ExactBoundValue upper) &&
                    numeric.CompareTo(lower) >= 0 && numeric.CompareTo(upper) <= 0) {
                    Result<V?, ErrorCode, Error<ErrorCode>> product = FunctionEvaluation.Product(Slopes[i], value);
                    if (!product.IsOk || product.Value is not { } scaled ||
                        !TryNegate(
                            value: Intercepts[i],
                            converter: _converter,
                            result: out V negativeIntercept
                        )) {
                        return FunctionEvaluation.Invalid<V>();
                    }
                    return FunctionEvaluation.Difference(scaled, negativeIntercept);
                }
            }
            return Results.Ok<V?>(null);
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return FunctionEvaluation.Invalid<V>();
        }
    }

    Result<V?, ErrorCode, Error<ErrorCode>> IModelAwareFunctionPreparation<V>.TryPrepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) => Prepare(inputs, modelValues);

    private Result<V?, ErrorCode, Error<ErrorCode>> Prepare(
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues) {
        if (!Current(null)) {
            return FunctionEvaluation.Invalid<V>();
        }
        Result<V?, ErrorCode, Error<ErrorCode>> evaluated = TryEvaluate(inputs);
        if (!evaluated.IsOk || evaluated.Value is not { } result) {
            return evaluated;
        }
        try {
            Result<V?, ErrorCode, Error<ErrorCode>> direct = FunctionEvaluation.ConditionalPolynomial(
                polynomial: Input,
                values: inputs,
                converter: _converter,
                exact: out ExactBoundValue directValue,
                encoded: out ExactBoundValue encodedDirect
            );
            Result<bool, ErrorCode, Error<ErrorCode>> expanded = FunctionEvaluation.ExactPolynomial(
                polynomial: _expanded,
                values: modelValues,
                converter: _converter,
                exact: out ExactBoundValue expandedValue,
                encoded: out ExactBoundValue encodedExpanded
            );
            if (!direct.IsOk || direct.Value is not { } calculatedInput || !expanded.IsOk || !expanded.Value ||
                directValue.CompareTo(expandedValue) != 0 || encodedDirect.CompareTo(encodedExpanded) != 0 ||
                !FunctionEvaluation.TryReadConditionValue(
                    value: calculatedInput,
                    converter: _converter,
                    exact: out ExactBoundValue actualInput,
                    encoded: out ExactBoundValue encodedInput
                ) || actualInput.CompareTo(directValue) != 0 || encodedInput.CompareTo(encodedDirect) != 0 ||
                !FunctionEvaluation.TryReadConditionValue(
                    value: result,
                    converter: _converter,
                    exact: out ExactBoundValue actualResult,
                    encoded: out ExactBoundValue encodedResult
                )) {
                return Results.Ok<V?>(null);
            }
            int segment = UniqueSegment(directValue, encoded: false);
            if (segment < 0 || UniqueSegment(encodedDirect, encoded: true) != segment ||
                UniqueSegment(expandedValue, encoded: false) != segment ||
                UniqueSegment(encodedExpanded, encoded: true) != segment ||
                !SafeAssignment(
                    input: expandedValue,
                    result: actualResult,
                    selected: segment,
                    encoded: false
                ) || !SafeAssignment(
                    input: encodedExpanded,
                    result: encodedResult,
                    selected: segment,
                    encoded: true
                ) || !MatchesKnown(
                    variable: InputVar,
                    natural: expandedValue,
                    encoded: encodedExpanded
                ) || !MatchesKnown(
                    variable: ResultVar,
                    natural: actualResult,
                    encoded: encodedResult
                )) {
                return Results.Ok<V?>(null);
            }
            for (int i = 0; i < SelectorVars.Count; ++i) {
                ExactBoundValue selected = i == segment
                    ? new ExactBoundValue(BigInteger.One, BigInteger.One) : ExactBoundValue.Zero;
                ExactBoundValue inactive = i == segment
                    ? ExactBoundValue.Zero : new ExactBoundValue(BigInteger.One, BigInteger.One);
                if (!MatchesKnown(
                        variable: SelectorVars[i],
                        natural: selected,
                        encoded: selected
                    ) || !MatchesKnown(
                        variable: InactiveVars[i],
                        natural: inactive,
                        encoded: inactive
                    )) {
                    return Results.Ok<V?>(null);
                }
            }
            return Current(null) ? Results.Ok<V?>(result) : Results.Ok<V?>(null);

            bool MatchesKnown(IVariableItem variable, ExactBoundValue natural, ExactBoundValue encoded) =>
                !modelValues.TryGetValue(variable, out V known) ||
                FunctionEvaluation.TryReadConditionValue(
                    value: known,
                    converter: _converter,
                    exact: out ExactBoundValue actual,
                    encoded: out ExactBoundValue encodedActual
                ) && actual.CompareTo(natural) == 0 && encodedActual.CompareTo(encoded) == 0;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Results.Ok<V?>(null);
        }
    }

    internal bool ProvesResult(
        V result,
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues,
        IFlt64ValueConverter<V> converter) {
        if (!IsCompatibleConverter(converter)) {
            return false;
        }
        Result<V?, ErrorCode, Error<ErrorCode>> prepared = Prepare(inputs, modelValues);
        return prepared.IsOk && prepared.Value is { } value && StructuralValueEquality<V>.Same(value, result) &&
            IsCompatibleConverter(converter);
    }

    internal bool IsCompatibleConverter(IFlt64ValueConverter<V> converter) => Current(tokens: null, modelConverter: converter);

    // shortcut: 相邻闭段边界暂不发布证据；可证明两段一致后放宽 / Do not publish evidence at shared closed boundaries until both segments can be proven equivalent.
    private int UniqueSegment(ExactBoundValue value, bool encoded) {
        for (int i = 0; i < Slopes.Count; ++i) {
            if (!ReadGraph(
                    value: Breakpoints[i],
                    encoded: encoded,
                    result: out ExactBoundValue lower
                ) || !ReadGraph(
                    value: Breakpoints[i + 1],
                    encoded: encoded,
                    result: out ExactBoundValue upper
                )) {
                return -1;
            }
            if (value.CompareTo(lower) >= 0 && value.CompareTo(upper) <= 0) {
                return i > 0 && value.CompareTo(lower) == 0 ||
                    i + 1 < Slopes.Count && value.CompareTo(upper) == 0 ? -1 : i;
            }
        }
        return -1;
    }

    private bool SafeAssignment(ExactBoundValue input, ExactBoundValue result, int selected, bool encoded) {
        if (!Exact(
                value: _zero,
                converter: _converter,
                exact: out ExactBoundValue zero
            ) || zero.Sign != 0 || !TryNegate(
                value: _one,
                converter: _converter,
                result: out _
            ) || InputVar.Range.LowerBound?.Value.UnwrapOrNull() is not { } inputLower ||
            InputVar.Range.UpperBound?.Value.UnwrapOrNull() is not { } inputUpper ||
            ResultVar.Range.LowerBound?.Value.UnwrapOrNull() is not { } resultLower ||
            ResultVar.Range.UpperBound?.Value.UnwrapOrNull() is not { } resultUpper ||
            !ExactBoundValue.TryRead(inputLower, out ExactBoundValue lowerX) ||
            !ExactBoundValue.TryRead(inputUpper, out ExactBoundValue upperX) ||
            !ExactBoundValue.TryRead(resultLower, out ExactBoundValue lowerY) ||
            !ExactBoundValue.TryRead(resultUpper, out ExactBoundValue upperY) ||
            input.CompareTo(lowerX) < 0 || input.CompareTo(upperX) > 0 ||
            result.CompareTo(lowerY) < 0 || result.CompareTo(upperY) > 0) {
            return false;
        }
        for (int i = 0; i < Slopes.Count; ++i) {
            if (!ReadGraph(
                    value: Breakpoints[i],
                    encoded: encoded,
                    result: out ExactBoundValue lower
                ) || !TryNegate(
                    value: Breakpoints[i],
                    converter: _converter,
                    result: out _
                ) || !ReadGraph(
                    value: Breakpoints[i + 1],
                    encoded: encoded,
                    result: out ExactBoundValue upper
                ) || !TryNegate(
                    value: Breakpoints[i + 1],
                    converter: _converter,
                    result: out _
                ) || !ReadGraph(
                    value: Slopes[i],
                    encoded: encoded,
                    result: out ExactBoundValue slope
                ) || !TryNegate(
                    value: Slopes[i],
                    converter: _converter,
                    result: out _
                ) || !ReadGraph(
                    value: Intercepts[i],
                    encoded: encoded,
                    result: out ExactBoundValue intercept
                ) || !TryNegate(
                    value: Intercepts[i],
                    converter: _converter,
                    result: out _
                ) || !ReadGraph(
                    value: _bigM[i],
                    encoded: encoded,
                    result: out ExactBoundValue m
                ) || m.Sign <= 0 || !TryNegate(
                    value: _bigM[i],
                    converter: _converter,
                    result: out _
                )) {
                return false;
            }
            ExactBoundValue line = slope.Multiply(input).Add(intercept);
            if (i == selected) {
                if (input.CompareTo(lower) < 0 || input.CompareTo(upper) > 0 || result.CompareTo(line) != 0) {
                    return false;
                }
            }
            else if (input.Add(m).CompareTo(lower) < 0 || input.Add(m.Negate()).CompareTo(upper) > 0 ||
                result.Add(m.Negate()).CompareTo(line) > 0 || result.Add(m).CompareTo(line) < 0) {
                return false;
            }
        }
        return true;
    }

    /// <inheritdoc/>
    public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) {
        try {
            return tokens is not null && Current(null)
                ? tokens.Add(HelperVariables) ?? InvalidRegistration()
                : InvalidRegistration();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return InvalidRegistration();
        }
    }

    /// <inheritdoc/>
    public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) {
        try {
            if (model is null || !CurrentForModel(model)) {
                return InvalidRegistration();
            }
            IReadOnlyList<LinearInequality<V>> rows = Constraints();
            var registered = model.Tokens.Tokens.Select(token => token.Variable.Key).ToHashSet();
            foreach (LinearInequality<V> row in rows) {
                Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened = row.ToLinearFlattenData(model.Tokens);
                if (!flattened.IsOk || !ExactLinearExpansion.MatchesDifference(row, flattened.Value) ||
                    !MatchesEncodedRow(row, flattened.Value) ||
                    flattened.Value.Monomials.Any(term => term.Symbol is not IVariableItem variable ||
                        !registered.Contains(variable.Key) || !ReadValues(
                            value: term.Coefficient,
                            converter: _converter,
                            natural: out _,
                            encoded: out _
                        )) || !ReadValues(
                        value: flattened.Value.Constant,
                        converter: _converter,
                        natural: out _,
                        encoded: out _
                    )) {
                    return InvalidRegistration();
                }
            }
            if (!CurrentForModel(model)) {
                return InvalidRegistration();
            }
            return IndicatorSupport.AddConstraints(
                model: model,
                constraints: rows,
                afterWrite: () => {
                    if (!CurrentForModel(model)) {
                        return InvalidRegistration();
                    }
                    if (model is BasicMechanismModel<V> mechanism) {
                        mechanism.TrackModelDependency(() => CurrentForModel(model));
                    }
                    return Results.OkInstance;
                }
            );
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return InvalidRegistration();
        }
    }

    private bool CurrentForModel(IAbstractLinearMechanismModel<V> model) => Current(
        tokens: model.Tokens,
        modelConverter: model switch {
            LinearMechanismModel<V> linear => linear.Parent.Converter,
            QuadraticMechanismModel<V> quadratic => quadratic.Parent.Converter,
            // shortcut: 自定义模型未暴露转换器；提供统一转换器契约后校验 / Custom models do not expose converters; validate once a common contract exists.
            _ => null,
        }
    );

    private bool Current(IAbstractTokenTable<V>? tokens, IFlt64ValueConverter<V>? modelConverter = null) {
        try {
            if (!_helperDependencies.IsCurrent() || _inputDependencies is not null && !_inputDependencies.IsCurrent() ||
                !StructuralValueEquality<V>.Same(_converter.Zero, _zero) ||
                !StructuralValueEquality<V>.Same(_converter.One, _one) ||
                modelConverter is not null && (!StructuralValueEquality<V>.Same(modelConverter.Zero, _zero) ||
                    !StructuralValueEquality<V>.Same(modelConverter.One, _one)) || !Exact(
                    value: _zero,
                    converter: _converter,
                    exact: out _
                ) || !CompatibleConversion(_zero) || !CompatibleConversion(_one)) {
                return false;
            }
            foreach (V value in Breakpoints.Concat(Slopes).Concat(Intercepts).Concat(_bigM)) {
                if (!CompatibleConversion(value)) {
                    return false;
                }
            }
            Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> current =
                new SymbolFlatten.LinearFlattenData<V>(Input.Monomials, Input.Constant).ExpandIntermediateSymbols(tokens);
            if (!current.IsOk || current.Value.Monomials.Count != _expanded.Monomials.Count ||
                !StructuralValueEquality<V>.Same(current.Value.Constant, _expanded.Constant) ||
                !ExactLinearExpansion.Matches(
                    input: Input,
                    expanded: new LinearPolynomial<V>(current.Value.Monomials, current.Value.Constant),
                    converter: _converter
                )) {
                return false;
            }
            for (int i = 0; i < _expanded.Monomials.Count; ++i) {
                LinearMonomial<V> original = _expanded.Monomials[i];
                LinearMonomial<V> actual = current.Value.Monomials[i];
                if (original.Symbol is not IVariableItem first || actual.Symbol is not IVariableItem second ||
                    first.Key != second.Key || !StructuralValueEquality<V>.Same(original.Coefficient, actual.Coefficient)) {
                    return false;
                }
            }
            foreach (LinearInequality<V> row in Constraints()) {
                Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened = row.ToLinearFlattenData(tokens);
                if (!flattened.IsOk || !ExactLinearExpansion.MatchesDifference(row, flattened.Value) ||
                    !MatchesEncodedRow(row, flattened.Value) ||
                    flattened.Value.Monomials.Any(term => !CompatibleConversion(term.Coefficient)) ||
                    !CompatibleConversion(flattened.Value.Constant)) {
                    return false;
                }
            }
            return _helperDependencies.IsCurrent() && (_inputDependencies is null || _inputDependencies.IsCurrent());

            bool CompatibleConversion(V value) => TryNegate(
                value: value,
                converter: _converter,
                result: out V negative
            ) && (modelConverter is null ||
                modelConverter.FromValue(value).ToDouble() == _converter.FromValue(value).ToDouble() &&
                modelConverter.FromValue(negative).ToDouble() == _converter.FromValue(negative).ToDouble());
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    private IReadOnlyList<LinearInequality<V>> Constraints() {
        var rows = new List<LinearInequality<V>>();
        var definition = new List<LinearMonomial<V>> { new(_one, InputVar) };
        definition.AddRange(_expanded.Monomials.Select(term => new LinearMonomial<V>(term.Coefficient.Negate(), term.Symbol)));
        rows.Add(IndicatorSupport.Row(
            monomials: definition,
            constant: _expanded.Constant.Negate(),
            comparison: Comparison.EQ,
            rhs: _zero,
            name: $"{Name}_input"
        ));
        rows.Add(IndicatorSupport.Row(
            monomials: SelectorVars.Select(selector => new LinearMonomial<V>(_one, selector)).ToArray(),
            constant: _zero,
            comparison: Comparison.EQ,
            rhs: _one,
            name: $"{Name}_select_one"
        ));
        for (int i = 0; i < Slopes.Count; ++i) {
            BinVar inactive = InactiveVars[i];
            V m = _bigM[i];
            rows.Add(IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<V>(_one, SelectorVars[i]), new LinearMonomial<V>(_one, inactive) },
                constant: _zero,
                comparison: Comparison.EQ,
                rhs: _one,
                name: $"{Name}_seg_{i}_complement"
            ));
            rows.Add(IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<V>(_one, InputVar), new LinearMonomial<V>(m, inactive) },
                constant: _zero,
                comparison: Comparison.GE,
                rhs: Breakpoints[i],
                name: $"{Name}_seg_{i}_lb"
            ));
            rows.Add(IndicatorSupport.Row(
                monomials: new[] { new LinearMonomial<V>(_one, InputVar), new LinearMonomial<V>(m.Negate(), inactive) },
                constant: _zero,
                comparison: Comparison.LE,
                rhs: Breakpoints[i + 1],
                name: $"{Name}_seg_{i}_ub"
            ));
            rows.Add(IndicatorSupport.Row(
                monomials: new[] {
                    new LinearMonomial<V>(_one, ResultVar), new LinearMonomial<V>(Slopes[i].Negate(), InputVar),
                    new LinearMonomial<V>(m.Negate(), inactive),
                },
                constant: _zero,
                comparison: Comparison.LE,
                rhs: Intercepts[i],
                name: $"{Name}_seg_{i}_eq_ub"
            ));
            rows.Add(IndicatorSupport.Row(
                monomials: new[] {
                    new LinearMonomial<V>(_one, ResultVar), new LinearMonomial<V>(Slopes[i].Negate(), InputVar),
                    new LinearMonomial<V>(m, inactive),
                },
                constant: _zero,
                comparison: Comparison.GE,
                rhs: Intercepts[i],
                name: $"{Name}_seg_{i}_eq_lb"
            ));
        }
        return rows;
    }

    private static bool Exact(V value, IIntoValue<V> converter, out ExactBoundValue exact) =>
        ExactBoundValue.TryRead(
            value: value,
            converter: converter,
            exact: out exact
        ) &&
        ExactBoundValue.TryRead(converter.FromValue(value), out ExactBoundValue encoded) && exact.CompareTo(encoded) == 0;

    // shortcut: 仅为 decimal 证明非精确编码；其他类型有完整双图证明后再放宽 / Allow inexact decimal encoding only until other types have a full dual-graph proof.
    private static bool ReadValues(V value, IIntoValue<V> converter, out ExactBoundValue natural, out ExactBoundValue encoded) =>
        FunctionEvaluation.TryReadConditionValue(
            value: value,
            converter: converter,
            exact: out natural,
            encoded: out encoded
        ) && (typeof(V) == typeof(FltX) || natural.CompareTo(encoded) == 0);

    private bool ReadGraph(V value, bool encoded, out ExactBoundValue result) {
        bool valid = ReadValues(
            value: value,
            converter: _converter,
            natural: out ExactBoundValue natural,
            encoded: out ExactBoundValue solver
        );
        result = encoded ? solver : natural;
        return valid;
    }

    private static bool TryNegate(V value, IIntoValue<V> converter, out V result) {
        result = default;
        return ReadValues(
            value: value,
            converter: converter,
            natural: out ExactBoundValue original,
            encoded: out ExactBoundValue encodedOriginal
        ) &&
            LinearFlattenExtensions.Arithmetic<V>.TryNegate(value, out result) &&
            ReadValues(
                value: result,
                converter: converter,
                natural: out ExactBoundValue actual,
                encoded: out ExactBoundValue encodedActual
            ) && original.Negate().CompareTo(actual) == 0 &&
            encodedOriginal.Negate().CompareTo(encodedActual) == 0;
    }

    private bool MatchesEncodedRow(LinearInequality<V> row, SymbolFlatten.LinearFlattenData<V> flattened) {
        var coefficients = new Dictionary<VariableItemKey, ExactBoundValue>();
        ExactBoundValue constant = ExactBoundValue.Zero;
        return Add(
                terms: row.Lhs.Monomials,
                value: row.Lhs.Constant,
                subtract: false
            ) && Add(
                terms: row.Rhs.Monomials,
                value: row.Rhs.Constant,
                subtract: true
            ) && Add(
                terms: flattened.Monomials,
                value: flattened.Constant,
                subtract: true
            ) &&
            constant.Sign == 0 && coefficients.Values.All(value => value.Sign == 0);

        bool Add(IReadOnlyList<LinearMonomial<V>> terms, V value, bool subtract) {
            if (!ReadGraph(
                    value: value,
                    encoded: true,
                    result: out ExactBoundValue encodedConstant
                )) {
                return false;
            }
            constant = constant.Add(subtract ? encodedConstant.Negate() : encodedConstant);
            foreach (LinearMonomial<V> term in terms) {
                if (term.Symbol is not IVariableItem variable || !ReadGraph(
                        value: term.Coefficient,
                        encoded: true,
                        result: out ExactBoundValue coefficient
                    )) {
                    return false;
                }
                ExactBoundValue previous = coefficients.TryGetValue(variable.Key, out ExactBoundValue existing)
                    ? existing : ExactBoundValue.Zero;
                coefficients[variable.Key] = previous.Add(subtract ? coefficient.Negate() : coefficient);
            }
            return true;
        }
    }

    private static Result<UnivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>> Invalid() =>
        Results.Failed<UnivariateLinearPiecewiseFunction<V>>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "分段函数输入、范围或数值无效。 / Invalid piecewise input, bounds or numeric value."
        ));

    private static Try InvalidRegistration() => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "分段函数输入、令牌或范围已失效。 / Piecewise input, tokens or bounds are invalid or stale."
    ));
}

/// <summary>分段函数采样点工厂 / Piecewise function factory from sampling points.</summary>
public static class UnivariateLinearPiecewiseFunction {
    /// <summary>从严格递增的采样点创建分段函数 / Create a piecewise function from strictly increasing sampling points.</summary>
    /// <param name="input">输入线性多项式 / Input linear polynomial.</param>
    /// <param name="points">二维采样点 / Two-dimensional sampling points.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="bigM">可选正 Big-M，调用方须保证充分 / Optional positive Big-M, whose sufficiency is the caller's responsibility.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <typeparam name="V">浮点坐标类型 / Floating-point coordinate type.</typeparam>
    /// <returns>函数或结构化失败 / Function or structured failure.</returns>
    public static Result<UnivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>> FromPoints<V>(
        LinearPolynomial<V>? input,
        IReadOnlyList<Point<Dim2, V>>? points,
        IIntoValue<V>? converter,
        V? bigM = null,
        string name = "piecewise",
        string? displayName = null)
        where V : struct, IFloatingNumber<V> {
        try {
            if (points is null || converter is null || points.Count < 2) {
                return Invalid<V>();
            }
            Point<Dim2, V>[] snapshot = points.ToArray();
            var x = new V[snapshot.Length];
            var y = new V[snapshot.Length];
            for (int i = 0; i < snapshot.Length; ++i) {
                if (snapshot[i] is null || snapshot[i].Count != 2) {
                    return Invalid<V>();
                }
                x[i] = snapshot[i][0];
                y[i] = snapshot[i][1];
                if (!FunctionEvaluation.IsUsable(x[i]) || !FunctionEvaluation.IsUsable(y[i]) ||
                    !ExactBoundValue.TryRead(x[i], out ExactBoundValue current) ||
                    i > 0 && (!ExactBoundValue.TryRead(x[i - 1], out ExactBoundValue previous) ||
                        previous.CompareTo(current) >= 0)) {
                    return Invalid<V>();
                }
            }
            var slopes = new V[x.Length - 1];
            var intercepts = new V[slopes.Length];
            for (int i = 0; i < slopes.Length; ++i) {
                Result<V?, ErrorCode, Error<ErrorCode>> dx = FunctionEvaluation.Difference(x[i + 1], x[i]);
                Result<V?, ErrorCode, Error<ErrorCode>> dy = FunctionEvaluation.Difference(y[i + 1], y[i]);
                if (!dx.IsOk || !dy.IsOk || dx.Value is not { } width || dy.Value is not { } height ||
                    width.Eq(converter.Zero)) {
                    return Invalid<V>();
                }
                slopes[i] = height.Div(width);
                if (!FunctionEvaluation.IsUsable(slopes[i]) || !height.Eq(converter.Zero) && slopes[i].Eq(converter.Zero)) {
                    return Invalid<V>();
                }
                Result<V?, ErrorCode, Error<ErrorCode>> product = FunctionEvaluation.Product(slopes[i], x[i]);
                if (!product.IsOk || product.Value is not { } scaled) {
                    return Invalid<V>();
                }
                Result<V?, ErrorCode, Error<ErrorCode>> intercept = FunctionEvaluation.Difference(y[i], scaled);
                if (!intercept.IsOk || intercept.Value is not { } offset) {
                    return Invalid<V>();
                }
                intercepts[i] = offset;
            }
            return UnivariateLinearPiecewiseFunction<V>.Create(
                input: input,
                breakpoints: x,
                slopes: slopes,
                intercepts: intercepts,
                converter: converter,
                bigM: bigM,
                name: name,
                displayName: displayName
            );
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return Invalid<V>();
        }
    }

    private static Result<UnivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>> Invalid<V>()
        where V : struct, IFloatingNumber<V> => Results.Failed<UnivariateLinearPiecewiseFunction<V>>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "分段函数采样点无效。 / Invalid piecewise sampling points."
        ));
}
