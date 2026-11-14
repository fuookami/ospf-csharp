#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
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

/// <summary>三角形插值的双变量分段线性函数 / Bivariate piecewise-linear interpolation over triangles.</summary>
/// <remarks>
/// 三角形相交时模型允许任一片面，直接求值使用第一个匹配片面。
/// The model permits any overlapping surface; direct evaluation uses the first matching triangle.
/// 直接求值沿用 Kotlin 的几何容差，可能接受模型边界外的点。
/// Direct evaluation uses Kotlin's geometric tolerance and may accept points outside the model boundary.
/// </remarks>
/// <typeparam name="V">输入和系数数值类型 / Input and coefficient numeric type.</typeparam>
public sealed class BivariateLinearPiecewiseFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>, IModelAwareFunctionPreparation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private const double GeometryTolerance = 1e-12;
    private readonly IIntoValue<V> _converter;
    private readonly LinearPolynomial<V> _expandedX;
    private readonly LinearPolynomial<V> _expandedY;
    private readonly IReadOnlyList<V[]> _coordinates;
    private readonly IReadOnlyList<ExactBoundValue[]> _exactCoordinates;
    private readonly IReadOnlyList<ExactBoundValue> _determinants;
    private readonly BoundDependencySnapshot<V> _helperDependencies;
    private readonly LinearPolynomial<V> _result;
    private readonly V _zero;
    private readonly V _one;
    private readonly V _three;

    /// <summary>第一个输入多项式的浅快照 / Shallow snapshot of the first input polynomial.</summary>
    public LinearPolynomial<V> X { get; }
    /// <summary>第二个输入多项式的浅快照 / Shallow snapshot of the second input polynomial.</summary>
    public LinearPolynomial<V> Y { get; }
    /// <summary>三角形和顶点坐标快照 / Snapshot of triangles and vertex coordinates.</summary>
    public IReadOnlyList<Triangle<Dim3, Flt64>> Triangles { get; }
    /// <summary>每个三角形的三个重心变量 / Three barycentric variables per triangle.</summary>
    public IReadOnlyList<IReadOnlyList<PctVar>> LambdaVars { get; }
    /// <summary>每个三角形的选择变量 / Selection variables for the triangles.</summary>
    public IReadOnlyList<BinVar> SelectorVars { get; }
    /// <inheritdoc/>
    public string Name { get; set; }
    /// <inheritdoc/>
    public string? DisplayName { get; set; }
    /// <inheritdoc/>
    public IReadOnlyList<IVariableItem> HelperVariables { get; }
    /// <inheritdoc/>
    public IReadOnlyCollection<ISymbol> InputSymbols => FunctionInputs.Of(new[] { X, Y });
    /// <inheritdoc/>
    public LinearPolynomial<V> ResultPolynomial => _result;

    private BivariateLinearPiecewiseFunction(
        LinearPolynomial<V> x,
        LinearPolynomial<V> y,
        LinearPolynomial<V> expandedX,
        LinearPolynomial<V> expandedY,
        IReadOnlyList<Triangle<Dim3, Flt64>> triangles,
        IReadOnlyList<V[]> coordinates,
        IReadOnlyList<ExactBoundValue[]> exactCoordinates,
        IReadOnlyList<ExactBoundValue> determinants,
        IIntoValue<V> converter,
        V zero,
        V one,
        V three,
        string name,
        string? displayName) {
        X = x;
        Y = y;
        _expandedX = expandedX;
        _expandedY = expandedY;
        Triangles = triangles;
        _coordinates = coordinates;
        _exactCoordinates = exactCoordinates;
        _determinants = determinants;
        _converter = converter;
        _zero = zero;
        _one = one;
        _three = three;
        Name = name;
        DisplayName = displayName;
        LambdaVars = Array.AsReadOnly(Enumerable.Range(0, triangles.Count)
            .Select(i => (IReadOnlyList<PctVar>)Array.AsReadOnly(Enumerable.Range(0, 3)
                .Select(j => new PctVar($"{name}_lambda_{i}_{j}")).ToArray())).ToArray());
        SelectorVars = Array.AsReadOnly(Enumerable.Range(0, triangles.Count)
            .Select(i => new BinVar($"{name}_tri_{i}")).ToArray());
        HelperVariables = Array.AsReadOnly<IVariableItem>(LambdaVars.SelectMany(group => group)
            .Cast<IVariableItem>().Concat(SelectorVars).ToArray());
        _result = new LinearPolynomial<V>(
            Array.AsReadOnly(LambdaVars.SelectMany((group, i) => group.Select((variable, j) =>
                new LinearMonomial<V>(coordinates[i][(j * 3) + 2], variable))).ToArray()), zero);
        _helperDependencies = new BoundDependencySnapshot<V>(new[] {
            new LinearPolynomial<V>(HelperVariables.Select(variable => new LinearMonomial<V>(one, variable)).ToArray(), zero),
        });
    }

    /// <summary>校验并创建双变量分段函数 / Validate and create a bivariate piecewise function.</summary>
    /// <param name="x">第一个输入多项式 / First input polynomial.</param>
    /// <param name="y">第二个输入多项式 / Second input polynomial.</param>
    /// <param name="triangles">带高度的三角形 / Triangles with vertex heights.</param>
    /// <param name="converter">求解器数值转换器 / Solver-value converter.</param>
    /// <param name="name">函数名称 / Function name.</param>
    /// <param name="displayName">可选显示名称 / Optional display name.</param>
    /// <returns>函数或结构化失败 / Function or structured failure.</returns>
    public static Result<BivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<V>? x,
        LinearPolynomial<V>? y,
        IReadOnlyList<Triangle<Dim3, Flt64>>? triangles,
        IIntoValue<V>? converter,
        string name = "bivariateLinearPiecewise",
        string? displayName = null) {
        try {
            if (x?.Monomials is null || y?.Monomials is null || triangles is null || triangles.Count == 0 ||
                converter is null || name is null || x.Monomials.Concat(y.Monomials).Any(term => term is null || term.Symbol is null) ||
                !TryConvert(
                    value: Flt64.Zero,
                    converter: converter,
                    result: out V zero
                ) || !StructuralValueEquality<V>.Same(zero, converter.Zero) || !TryConvert(
                    value: Flt64.One,
                    converter: converter,
                    result: out V one
                ) || !StructuralValueEquality<V>.Same(one, converter.One) || !TryConvert(
                    value: new Flt64(3),
                    converter: converter,
                    result: out V three
                )) {
                return Invalid();
            }
            var snapshots = new List<Triangle<Dim3, Flt64>>(triangles.Count);
            var coordinates = new List<V[]>(triangles.Count);
            var exactCoordinates = new List<ExactBoundValue[]>(triangles.Count);
            var determinants = new List<ExactBoundValue>(triangles.Count);
            ExactBoundValue.TryRead(new Flt64(GeometryTolerance), out ExactBoundValue tolerance);
            foreach (Triangle<Dim3, Flt64> triangle in triangles) {
                if (triangle is null || triangle.P1 is null || triangle.P2 is null || triangle.P3 is null ||
                    triangle.P1.Count != 3 || triangle.P2.Count != 3 || triangle.P3.Count != 3) {
                    return Invalid();
                }
                Flt64[] values = triangle.Vertices.SelectMany(point => Enumerable.Range(0, 3).Select(i => point[i])).ToArray();
                var converted = new V[9];
                var exact = new ExactBoundValue[9];
                for (int i = 0; i < values.Length; ++i) {
                    if (!TryConvert(
                            value: values[i],
                            converter: converter,
                            result: out converted[i]
                        ) || !ExactBoundValue.TryRead(values[i], out exact[i])) {
                        return Invalid();
                    }
                }
                ExactBoundValue determinant = Determinant(exact);
                if (determinant.Abs().CompareTo(tolerance) <= 0) {
                    return Invalid();
                }
                snapshots.Add(new Triangle<Dim3, Flt64>(
                    P1: Point<Dim3, Flt64>.Point3(
                        x: values[0],
                        y: values[1],
                        z: values[2]
                    ),
                    P2: Point<Dim3, Flt64>.Point3(
                        x: values[3],
                        y: values[4],
                        z: values[5]
                    ),
                    P3: Point<Dim3, Flt64>.Point3(
                        x: values[6],
                        y: values[7],
                        z: values[8]
                    )
                ));
                coordinates.Add(converted);
                exactCoordinates.Add(exact);
                determinants.Add(determinant);
            }
            var xSnapshot = new LinearPolynomial<V>(Array.AsReadOnly(x.Monomials.ToArray()), x.Constant);
            var ySnapshot = new LinearPolynomial<V>(Array.AsReadOnly(y.Monomials.ToArray()), y.Constant);
            if (!Expand(
                    input: xSnapshot,
                    converter: converter,
                    expanded: out LinearPolynomial<V> expandedX
                ) || !Expand(
                    input: ySnapshot,
                    converter: converter,
                    expanded: out LinearPolynomial<V> expandedY
                )) {
                return Invalid();
            }
            return Results.Ok(new BivariateLinearPiecewiseFunction<V>(
                x: xSnapshot,
                y: ySnapshot,
                expandedX: expandedX,
                expandedY: expandedY,
                triangles: Array.AsReadOnly(snapshots.ToArray()),
                coordinates: Array.AsReadOnly(coordinates.ToArray()),
                exactCoordinates: Array.AsReadOnly(exactCoordinates.ToArray()),
                determinants: Array.AsReadOnly(determinants.ToArray()),
                converter: converter,
                zero: zero,
                one: one,
                three: three,
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
            Result<V?, ErrorCode, Error<ErrorCode>> x = FunctionEvaluation.Polynomial(X, values);
            if (!x.IsOk || x.Value is not { } xValue) {
                return x;
            }
            Result<V?, ErrorCode, Error<ErrorCode>> y = FunctionEvaluation.Polynomial(Y, values);
            if (!y.IsOk || y.Value is not { } yValue) {
                return y;
            }
            if (!Exact(
                    value: xValue,
                    converter: _converter,
                    exact: out ExactBoundValue px
                ) || !Exact(
                    value: yValue,
                    converter: _converter,
                    exact: out ExactBoundValue py
                )) {
                return FunctionEvaluation.Invalid<V>();
            }
            ExactBoundValue.TryRead(new Flt64(GeometryTolerance), out ExactBoundValue tolerance);
            ExactBoundValue.TryRead(new Flt64(1 + GeometryTolerance), out ExactBoundValue upper);
            for (int i = 0; i < Triangles.Count; ++i) {
                ExactBoundValue[] weights = WeightNumerators(
                    values: _exactCoordinates[i],
                    x: px,
                    y: py
                );
                ExactBoundValue determinant = _determinants[i];
                if (determinant.Sign < 0) {
                    determinant = determinant.Negate();
                    weights = weights.Select(weight => weight.Negate()).ToArray();
                }
                ExactBoundValue margin = tolerance.Multiply(determinant);
                if (weights[0].CompareTo(margin.Negate()) >= 0 && weights[1].CompareTo(margin.Negate()) >= 0 &&
                    weights[0].Add(weights[1]).CompareTo(upper.Multiply(determinant)) <= 0) {
                    ExactBoundValue numerator = ExactBoundValue.Zero;
                    for (int j = 0; j < 3; ++j) {
                        numerator = numerator.Add(weights[j].Multiply(_exactCoordinates[i][(j * 3) + 2]));
                    }
                    if (!RoundNearest(Divide(numerator, determinant), out Flt64 output)) {
                        return FunctionEvaluation.Invalid<V>();
                    }
                    return TryConvert(
                        value: output,
                        converter: _converter,
                        result: out V result
                    )
                        ? Results.Ok<V?>(result) : FunctionEvaluation.Invalid<V>();
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
        IReadOnlyDictionary<ISymbol, V> modelValues) {
        if (!Current(null)) {
            return FunctionEvaluation.Invalid<V>();
        }
        Result<V?, ErrorCode, Error<ErrorCode>> result = TryEvaluate(inputs);
        if (!result.IsOk || result.Value is not { } value) {
            return result;
        }
        return ProvesResult(
            result: value,
            inputs: inputs,
            modelValues: modelValues
        ) ? result : Results.Ok<V?>(null);
    }

    private bool ProvesResult(V result, IReadOnlyDictionary<ISymbol, V> inputs, IReadOnlyDictionary<ISymbol, V> modelValues) =>
        TryProve(
            result: result,
            inputs: inputs,
            modelValues: modelValues,
            witness: out _
        );

    internal bool TryGetEvidence(
        V result,
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues,
        out IReadOnlyDictionary<ISymbol, V> evidence) {
        evidence = new Dictionary<ISymbol, V>();
        try {
            if (!TryProve(
                    result: result,
                    inputs: inputs,
                    modelValues: modelValues,
                    witness: out Witness? witness
                ) || witness is null) {
                return false;
            }
            var derived = new Dictionary<ISymbol, V>();
            for (int i = 0; i < Triangles.Count; ++i) {
                derived.Add(SelectorVars[i], i == witness.Selected ? _one : _zero);
                for (int j = 0; j < 3; ++j) {
                    if (i != witness.Selected) {
                        derived.Add(LambdaVars[i][j], _zero);
                        continue;
                    }
                    ExactBoundValue ratio = Divide(witness.Weights[j], witness.Denominator);
                    // shortcut: 仅发布精确可表示的重心值；支持更广泛数值证据后放宽 / Publish only exact barycentric values; relax with wider numeric evidence.
                    if (!ratio.TryRound(
                            converter: _converter,
                            upper: false,
                            result: out V value
                        ) || !Exact(
                            value: value,
                            converter: _converter,
                            exact: out ExactBoundValue represented
                        ) || represented.CompareTo(ratio) != 0) {
                        return false;
                    }
                    derived.Add(LambdaVars[i][j], value);
                }
            }
            if (!ProvesResult(
                    result: result,
                    inputs: inputs,
                    modelValues: modelValues
                )) {
                return false;
            }
            evidence = derived;
            return true;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    private bool TryProve(
        V result,
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues,
        out Witness? witness) {
        witness = null;
        try {
            if (!Current(null) || !InputValue(
                    input: X,
                    expanded: _expandedX,
                    inputs: inputs,
                    modelValues: modelValues,
                    value: out ExactBoundValue x
                ) || !InputValue(
                    input: Y,
                    expanded: _expandedY,
                    inputs: inputs,
                    modelValues: modelValues,
                    value: out ExactBoundValue y
                ) || !Exact(
                    value: result,
                    converter: _converter,
                    exact: out ExactBoundValue output
                )) {
                return false;
            }
            int selected = -1;
            ExactBoundValue[]? weights = null;
            ExactBoundValue denominator = default;
            for (int i = 0; i < Triangles.Count; ++i) {
                ExactBoundValue[] candidate = WeightNumerators(
                    values: _exactCoordinates[i],
                    x: x,
                    y: y
                );
                ExactBoundValue det = _determinants[i];
                if (det.Sign < 0) {
                    det = det.Negate();
                    candidate = candidate.Select(weight => weight.Negate()).ToArray();
                }
                if (candidate.All(weight => weight.Sign >= 0)) {
                    if (selected >= 0) {
                        // shortcut: 重叠片面暂不发布证据；能证明片面一致后放宽 / Suppress overlapping surfaces until equivalence can be proven.
                        return false;
                    }
                    selected = i;
                    weights = candidate;
                    denominator = det;
                }
            }
            if (selected < 0 || weights is null) {
                return false;
            }
            ExactBoundValue expected = ExactBoundValue.Zero;
            for (int j = 0; j < 3; ++j) {
                expected = expected.Add(weights[j].Multiply(_exactCoordinates[selected][(j * 3) + 2]));
            }
            if (output.Multiply(denominator).CompareTo(expected) != 0) {
                return false;
            }
            for (int i = 0; i < Triangles.Count; ++i) {
                if (!MatchesKnown(SelectorVars[i], i == selected ? denominator : ExactBoundValue.Zero)) {
                    return false;
                }
                for (int j = 0; j < 3; ++j) {
                    if (!MatchesKnown(LambdaVars[i][j], i == selected ? weights[j] : ExactBoundValue.Zero)) {
                        return false;
                    }
                }
            }
            if (!Current(null)) {
                return false;
            }
            witness = new Witness(
                Selected: selected,
                Weights: weights,
                Denominator: denominator
            );
            return true;

            bool MatchesKnown(IVariableItem variable, ExactBoundValue numerator) =>
                !modelValues.TryGetValue(variable, out V known) ||
                Exact(
                    value: known,
                    converter: _converter,
                    exact: out ExactBoundValue actual
                ) && actual.Multiply(denominator).CompareTo(numerator) == 0;
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    private sealed record Witness(int Selected, ExactBoundValue[] Weights, ExactBoundValue Denominator);

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
            if (model is null || !Current(model.Tokens)) {
                return InvalidRegistration();
            }
            IReadOnlyList<LinearInequality<V>> rows = Constraints();
            var registered = model.Tokens.Tokens.Select(token => token.Variable.Key).ToHashSet();
            foreach (LinearInequality<V> row in rows) {
                Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattened = row.ToLinearFlattenData(model.Tokens);
                if (!flattened.IsOk || !ExactLinearExpansion.MatchesDifference(row, flattened.Value) ||
                    flattened.Value.Monomials.Any(term => term.Symbol is not IVariableItem variable ||
                        !registered.Contains(variable.Key) || !Exact(
                            value: term.Coefficient,
                            converter: _converter,
                            exact: out _
                        )) || !Exact(
                        value: flattened.Value.Constant,
                        converter: _converter,
                        exact: out _
                    )) {
                    return InvalidRegistration();
                }
            }
            if (!Current(model.Tokens)) {
                return InvalidRegistration();
            }
            return IndicatorSupport.AddConstraints(
                model: model,
                constraints: rows,
                afterWrite: () => {
                    if (!Current(model.Tokens)) {
                        return InvalidRegistration();
                    }
                    if (model is BasicMechanismModel<V> mechanism) {
                        mechanism.TrackModelDependency(() => Current(model.Tokens));
                    }
                    return Results.OkInstance;
                }
            );
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return InvalidRegistration();
        }
    }

    private bool Current(IAbstractTokenTable<V>? tokens) {
        try {
            if (!_helperDependencies.IsCurrent() || !TryConvert(
                    value: Flt64.Zero,
                    converter: _converter,
                    result: out V zero
                ) || !TryConvert(
                    value: Flt64.One,
                    converter: _converter,
                    result: out V one
                ) || !TryConvert(
                    value: new Flt64(3),
                    converter: _converter,
                    result: out V three
                ) ||
                !StructuralValueEquality<V>.Same(zero, _zero) || !StructuralValueEquality<V>.Same(one, _one) ||
                !StructuralValueEquality<V>.Same(three, _three) || !StructuralValueEquality<V>.Same(_converter.Zero, _zero) ||
                !StructuralValueEquality<V>.Same(_converter.One, _one) ||
                !SameExpansion(
                    input: X,
                    original: _expandedX,
                    tokens: tokens
                ) || !SameExpansion(
                    input: Y,
                    original: _expandedY,
                    tokens: tokens
                )) {
                return false;
            }
            for (int i = 0; i < Triangles.Count; ++i) {
                Flt64[] source = Triangles[i].Vertices.SelectMany(point => Enumerable.Range(0, 3).Select(j => point[j])).ToArray();
                for (int j = 0; j < source.Length; ++j) {
                    if (!TryConvert(
                            value: source[j],
                            converter: _converter,
                            result: out V current
                        ) || !StructuralValueEquality<V>.Same(current, _coordinates[i][j])) {
                        return false;
                    }
                }
            }
            return _helperDependencies.IsCurrent();
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) {
            return false;
        }
    }

    private bool SameExpansion(LinearPolynomial<V> input, LinearPolynomial<V> original, IAbstractTokenTable<V>? tokens) {
        Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> current =
            new SymbolFlatten.LinearFlattenData<V>(input.Monomials, input.Constant).ExpandIntermediateSymbols(tokens);
        if (!current.IsOk || current.Value.Monomials.Count != original.Monomials.Count ||
            !StructuralValueEquality<V>.Same(current.Value.Constant, original.Constant) || !ExactLinearExpansion.Matches(
                input: input,
                expanded: new LinearPolynomial<V>(current.Value.Monomials, current.Value.Constant),
                converter: _converter
            )) {
            return false;
        }
        for (int i = 0; i < original.Monomials.Count; ++i) {
            LinearMonomial<V> first = original.Monomials[i];
            LinearMonomial<V> second = current.Value.Monomials[i];
            if (first.Symbol is not IVariableItem firstVariable || second.Symbol is not IVariableItem secondVariable ||
                firstVariable.Key != secondVariable.Key || !StructuralValueEquality<V>.Same(first.Coefficient, second.Coefficient)) {
                return false;
            }
        }
        return true;
    }

    private IReadOnlyList<LinearInequality<V>> Constraints() {
        var rows = new List<LinearInequality<V>>();
        var xTerms = new List<LinearMonomial<V>>(_expandedX.Monomials);
        var yTerms = new List<LinearMonomial<V>>(_expandedY.Monomials);
        for (int i = 0; i < Triangles.Count; ++i) {
            for (int j = 0; j < 3; ++j) {
                xTerms.Add(new LinearMonomial<V>(_coordinates[i][j * 3].Negate(), LambdaVars[i][j]));
                yTerms.Add(new LinearMonomial<V>(_coordinates[i][(j * 3) + 1].Negate(), LambdaVars[i][j]));
            }
        }
        rows.Add(IndicatorSupport.Row(
            monomials: xTerms,
            constant: _expandedX.Constant,
            comparison: Comparison.EQ,
            rhs: _zero,
            name: $"{Name}_x_eq"
        ));
        rows.Add(IndicatorSupport.Row(
            monomials: yTerms,
            constant: _expandedY.Constant,
            comparison: Comparison.EQ,
            rhs: _zero,
            name: $"{Name}_y_eq"
        ));
        rows.Add(IndicatorSupport.Row(
            monomials: LambdaVars.SelectMany(group => group).Select(variable => new LinearMonomial<V>(_one, variable)).ToArray(),
            constant: _zero,
            comparison: Comparison.EQ,
            rhs: _one,
            name: $"{Name}_sum_lambda"
        ));
        for (int i = 0; i < Triangles.Count; ++i) {
            rows.Add(IndicatorSupport.Row(
                monomials: LambdaVars[i].Select(variable => new LinearMonomial<V>(_one, variable))
                    .Append(new LinearMonomial<V>(_three.Negate(), SelectorVars[i])).ToArray(),
                constant: _zero,
                comparison: Comparison.LE,
                rhs: _zero,
                name: $"{Name}_tri_lambda_{i}"
            ));
        }
        rows.Add(IndicatorSupport.Row(
            monomials: SelectorVars.Select(variable => new LinearMonomial<V>(_one, variable)).ToArray(),
            constant: _zero,
            comparison: Comparison.EQ,
            rhs: _one,
            name: $"{Name}_sum_z"
        ));
        return rows;
    }

    private bool InputValue(
        LinearPolynomial<V> input,
        LinearPolynomial<V> expanded,
        IReadOnlyDictionary<ISymbol, V> inputs,
        IReadOnlyDictionary<ISymbol, V> modelValues,
        out ExactBoundValue value) {
        value = default;
        Result<V?, ErrorCode, Error<ErrorCode>> direct = FunctionEvaluation.ConditionalPolynomial(
            polynomial: input,
            values: inputs,
            converter: _converter,
            exact: out ExactBoundValue actual,
            encoded: out ExactBoundValue encoded
        );
        Result<bool, ErrorCode, Error<ErrorCode>> flattened = FunctionEvaluation.ExactPolynomial(
            polynomial: expanded,
            values: modelValues,
            converter: _converter,
            exact: out ExactBoundValue expandedValue,
            encoded: out ExactBoundValue expandedEncoded
        );
        if (!direct.IsOk || direct.Value is not { } calculated || !flattened.IsOk || !flattened.Value ||
            !Exact(
                value: calculated,
                converter: _converter,
                exact: out ExactBoundValue calculatedValue
            ) || actual.CompareTo(calculatedValue) != 0 ||
            actual.CompareTo(encoded) != 0 || actual.CompareTo(expandedValue) != 0 || actual.CompareTo(expandedEncoded) != 0) {
            return false;
        }
        value = actual;
        return true;
    }

    private static bool Expand(LinearPolynomial<V> input, IIntoValue<V> converter, out LinearPolynomial<V> expanded) {
        Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> result =
            new SymbolFlatten.LinearFlattenData<V>(input.Monomials, input.Constant).ExpandIntermediateSymbols();
        expanded = result.IsOk
            ? new LinearPolynomial<V>(Array.AsReadOnly(result.Value.Monomials.ToArray()), result.Value.Constant)
            : new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), converter.Zero);
        return result.IsOk && ExactLinearExpansion.Matches(
            input: input,
            expanded: expanded,
            converter: converter
        );
    }

    private static ExactBoundValue Determinant(ExactBoundValue[] values) =>
        Difference(values[4], values[7]).Multiply(Difference(values[0], values[6]))
            .Add(Difference(values[6], values[3]).Multiply(Difference(values[1], values[7])));

    private static ExactBoundValue[] WeightNumerators(ExactBoundValue[] values, ExactBoundValue x, ExactBoundValue y) {
        ExactBoundValue first = Difference(values[4], values[7]).Multiply(Difference(x, values[6]))
            .Add(Difference(values[6], values[3]).Multiply(Difference(y, values[7])));
        ExactBoundValue second = Difference(values[7], values[1]).Multiply(Difference(x, values[6]))
            .Add(Difference(values[0], values[6]).Multiply(Difference(y, values[7])));
        return new[] { first, second, Difference(Difference(Determinant(values), first), second) };
    }

    private static ExactBoundValue Difference(ExactBoundValue left, ExactBoundValue right) => left.Add(right.Negate());

    private static ExactBoundValue Divide(ExactBoundValue numerator, ExactBoundValue denominator) => new(
        numerator.Numerator * denominator.Denominator,
        numerator.Denominator * denominator.Numerator
    );

    private static bool RoundNearest(ExactBoundValue value, out Flt64 result) {
        result = default;
        if (!value.TryRound(
                converter: IIntoValue<Flt64>.Identity,
                upper: false,
                result: out Flt64 lower
            ) || !value.TryRound(
                converter: IIntoValue<Flt64>.Identity,
                upper: true,
                result: out Flt64 upper
            ) || !ExactBoundValue.TryRead(lower, out ExactBoundValue lowerValue) ||
            !ExactBoundValue.TryRead(upper, out ExactBoundValue upperValue)) {
            return false;
        }
        int comparison = Difference(value, lowerValue).CompareTo(Difference(upperValue, value));
        result = comparison < 0 || comparison == 0 && (BitConverter.DoubleToInt64Bits(lower.ToDouble()) & 1) == 0
            ? lower : upper;
        return true;
    }

    private static bool Exact(V value, IIntoValue<V> converter, out ExactBoundValue exact) =>
        ExactBoundValue.TryRead(
            value: value,
            converter: converter,
            exact: out exact
        ) &&
        ExactBoundValue.TryRead(converter.FromValue(value), out ExactBoundValue encoded) && exact.CompareTo(encoded) == 0;

    private static bool TryConvert(Flt64 value, IIntoValue<V> converter, out V result) {
        result = default;
        if (!BigMValues.IsUsable(value) || !ExactBoundValue.TryRead(value, out ExactBoundValue original)) {
            return false;
        }
        result = converter.IntoValue(value);
        return Exact(
                value: result,
                converter: converter,
                exact: out ExactBoundValue converted
            ) && converted.CompareTo(original) == 0 &&
            LinearFlattenExtensions.Arithmetic<V>.TryNegate(result, out V negative) &&
            Exact(
                value: negative,
                converter: converter,
                exact: out ExactBoundValue negated
            ) && original.Negate().CompareTo(negated) == 0;
    }

    private static Result<BivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>> Invalid() =>
        Results.Failed<BivariateLinearPiecewiseFunction<V>>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "双变量分段函数输入、三角形或数值无效。 / Invalid bivariate piecewise inputs, triangles or numeric values."
        ));

    private static Try InvalidRegistration() => Results.Failed<Success>(new Err<ErrorCode>(
        ErrorCode.IllegalArgument,
        "双变量分段函数输入、令牌或范围已失效。 / Bivariate piecewise inputs, tokens or bounds are invalid or stale."
    ));
}
