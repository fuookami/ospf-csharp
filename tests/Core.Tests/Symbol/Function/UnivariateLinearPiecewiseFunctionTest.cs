#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class UnivariateLinearPiecewiseFunctionTest {
    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, 0.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(1, 2.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(2, 0.0)]
    [InlineData(3, null)]
    public void Evaluate_ShouldFollowTheFirstClosedSegment(double input, double? expected) {
        RealVar x = Bounded("pwl-evaluate");
        UnivariateLinearPiecewiseFunction<Flt64> function = Function(x);
        Result<Flt64?, ErrorCode, Error<ErrorCode>> value = function.TryEvaluate(
            new Dictionary<ISymbol, Flt64> { [x] = new Flt64(input) });
        Assert.True(value.IsOk, value.ToString());
        Assert.Equal(expected is { } number ? new Flt64(number) : null, value.Value);
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64>()));
        Assert.True(function.TryEvaluate(null!).IsFailed);
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.NaN) }).IsFailed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rows_ShouldEncodeBothSegmentsAndDiscontinuousBoundary(bool discontinuous) {
        RealVar x = Bounded("pwl-rows");
        UnivariateLinearPiecewiseFunction<Flt64> function = Function(x, discontinuous);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Assert.Equal(12, ((IMechanismModel<Flt64>)model).Constraints.Count);
        Assert.Same(function.ResultVar, adapter.Polynomial.Monomials.Single().Symbol);
        Assert.Equal(6, function.HelperVariables.Count);
        foreach (double input in new[] { -0.5, 0.0, 0.5, 1.0, 1.5, 2.0, 2.5 }) {
            foreach (double result in new[] { -1.0, 0.0, 1.0, 2.0, 3.0, 4.0 }) {
                for (int selector = 0; selector < 2; ++selector) {
                    var assignment = new Dictionary<IVariableItem, Flt64> {
                        [x] = new Flt64(input),
                        [function.InputVar] = new Flt64(input),
                        [function.ResultVar] = new Flt64(result),
                        [function.SelectorVars[0]] = new Flt64(selector == 0 ? 1 : 0),
                        [function.SelectorVars[1]] = new Flt64(selector == 1 ? 1 : 0),
                        [function.InactiveVars[0]] = new Flt64(selector == 0 ? 0 : 1),
                        [function.InactiveVars[1]] = new Flt64(selector == 1 ? 0 : 1),
                    };
                    double expected = selector == 0 ? 2 * input : -2 * input + (discontinuous ? 5 : 4);
                    bool feasible = input >= selector && input <= selector + 1 && result == expected;
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(matrix, assignment));
                }
            }
        }
        if (discontinuous) {
            Assert.Equal(new Flt64(2), function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }));
            Assert.Equal(new Flt64(3), function.OutputBounds.UpperBound);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModelPreparation_ShouldNotCacheUnprovenResults(bool quadratic) {
        RealVar x = Bounded("pwl-cache");
        UnivariateLinearPiecewiseFunction<Flt64> function = Function(x, discontinuous: true);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var linear = new LinearMetaModel<Flt64>();
        using var quadraticModel = new QuadraticMetaModel<Flt64>();
        if (quadratic) {
            Assert.True(quadraticModel.Add(x).IsOk);
            Assert.True(quadraticModel.Add(adapter).IsOk);
            Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await QuadraticMechanismModel<Flt64>.InvokeAsync(
                metaModel: quadraticModel,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
            );
            Assert.True(result.IsOk, result.ToString());
            using QuadraticMechanismModel<Flt64> model = result.Value;
            Assert.Null(model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
            Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> matrix = model.ToQuadraticTetradModel();
            Assert.True(matrix.IsOk, matrix.ToString());
            matrix.Value.Dispose();
        }
        else {
            Assert.True(linear.Add(x).IsOk);
            Assert.True(linear.Add(adapter).IsOk);
            Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: linear,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
            );
            Assert.True(result.IsOk, result.ToString());
            using LinearMechanismModel<Flt64> model = result.Value;
            Assert.Null(model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
            Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> matrix = model.ToLinearTriadModel();
            Assert.True(matrix.IsOk, matrix.ToString());
            matrix.Value.Dispose();
        }
    }

    [Theory]
    [InlineData(0.0, 0.0, false)]
    [InlineData(0.5, 1.0, false)]
    [InlineData(1.5, 2.0, false)]
    [InlineData(2.0, 1.0, false)]
    [InlineData(0.0, 0.0, true)]
    [InlineData(0.5, 1.0, true)]
    [InlineData(1.5, 2.0, true)]
    [InlineData(2.0, 1.0, true)]
    public async Task ModelPreparation_ShouldCacheProvenUniqueSegment(double input, double output, bool quadratic) {
        RealVar x = Bounded("pwl-cache-proven");
        UnivariateLinearPiecewiseFunction<Flt64> function = Function(x, discontinuous: true);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var linear = new LinearMetaModel<Flt64>();
        using var quadraticModel = new QuadraticMetaModel<Flt64>();
        var fixedValues = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input) };
        if (quadratic) {
            Assert.True(quadraticModel.Add(x).IsOk);
            Assert.True(quadraticModel.Add(adapter).IsOk);
            Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await QuadraticMechanismModel<Flt64>.InvokeAsync(
                metaModel: quadraticModel,
                fixedVariables: fixedValues
            );
            Assert.True(built.IsOk, built.ToString());
            using QuadraticMechanismModel<Flt64> model = built.Value;
            Assert.Equal(new Flt64(output), model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        }
        else {
            Assert.True(linear.Add(x).IsOk);
            Assert.True(linear.Add(adapter).IsOk);
            Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: linear,
                fixedVariables: fixedValues
            );
            Assert.True(built.IsOk, built.ToString());
            using LinearMechanismModel<Flt64> model = built.Value;
            Assert.Equal(new Flt64(output), model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ModelPreparation_ShouldSuppressConflictingHelpersAndInsufficientBigM(int conflict) {
        RealVar x = Bounded("pwl-cache-conflict");
        UnivariateLinearPiecewiseFunction<Flt64> function = UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: Polynomial(x),
            breakpoints: new[] { Flt64.Zero, Flt64.One, new Flt64(2) },
            slopes: new[] { new Flt64(2), new Flt64(-2) },
            intercepts: new[] { Flt64.Zero, new Flt64(5) },
            converter: IIntoValue<Flt64>.Identity,
            bigM: conflict == 3 ? new Flt64(0.1) : null
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        var fixedValues = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(0.5) };
        if (conflict == 0) {
            fixedValues[function.InputVar] = Flt64.Zero;
        }
        else if (conflict == 1) {
            fixedValues[function.ResultVar] = Flt64.Zero;
        }
        else if (conflict == 2) {
            fixedValues[function.SelectorVars[1]] = Flt64.One;
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: fixedValues
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Null(model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public void ChangedNegatedSlopeConversion_ShouldSuppressDownstreamEvidence() {
        RealVar x = Bounded("pwl-evidence-converter");
        var converter = new SwitchingConverter();
        UnivariateLinearPiecewiseFunction<Flt64> function = UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: Polynomial(x),
            breakpoints: new[] { Flt64.Zero, Flt64.One, new Flt64(2) },
            slopes: new[] { new Flt64(2), Flt64.One },
            intercepts: new[] { Flt64.Zero, new Flt64(2) },
            converter: converter
        ).Value;
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        var consumer = new LinearFunctionSymbolAdapter<Flt64>(IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), producer) }, new Flt64(-1)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try registered = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.5) },
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 1) {
                    converter.CorruptNegativeTwo = true;
                }
                return Results.OkInstance;
            }
        );
        Assert.True(registered.IsOk, registered.ToString());
        Assert.Equal(Flt64.One, parent.Tokens.CachedValue(producer, (IReadOnlyList<Flt64>?)null));
        Assert.Null(parent.Tokens.CachedValue(consumer, (IReadOnlyList<Flt64>?)null));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EarlierEvidence_ShouldBeRevalidatedBeforeTheConsumer(int change) {
        RealVar x = Bounded("pwl-evidence-revalidation");
        UnivariateLinearPiecewiseFunction<Flt64> function = Function(x, discontinuous: true);
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        var consumer = new LinearFunctionSymbolAdapter<Flt64>(IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), producer) }, new Flt64(-1)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value);
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.5) };
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try registered = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: values,
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 1) {
                    if (change == 0) {
                        values[x] = new Flt64(1.5);
                    }
                    else if (change == 1) {
                        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
                    }
                    else if (change == 2) {
                        values[function.InactiveVars[0]] = Flt64.One;
                    }
                }
                return Results.OkInstance;
            }
        );
        Assert.True(registered.IsOk, registered.ToString());
        Assert.Equal(Flt64.One, parent.Tokens.CachedValue(producer, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(change == -1 ? Flt64.One : null, parent.Tokens.CachedValue(consumer, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public void Factory_ShouldRejectInvalidDimensionsNumbersBoundsAndConverter() {
        RealVar x = Bounded("pwl-invalid");
        LinearPolynomial<Flt64> input = Polynomial(x);
        foreach (Flt64[] points in new[] {
            Array.Empty<Flt64>(), new[] { Flt64.Zero }, new[] { Flt64.Zero, Flt64.Zero },
            new[] { Flt64.One, Flt64.Zero }, new[] { Flt64.Zero, new Flt64(double.NaN) },
            new[] { Flt64.Zero, new Flt64(double.PositiveInfinity) }, new[] { Flt64.Zero, new Flt64(double.MaxValue) },
        }) {
            Assert.True(UnivariateLinearPiecewiseFunction<Flt64>.Create(
                input: input,
                breakpoints: points,
                slopes: Enumerable.Repeat(Flt64.One, global::System.Math.Max(0, points.Length - 1)).ToArray(),
                intercepts: Enumerable.Repeat(Flt64.Zero, global::System.Math.Max(0, points.Length - 1)).ToArray(),
                converter: IIntoValue<Flt64>.Identity
            ).IsFailed);
        }
        Assert.True(Create(
            input: input,
            points: new[] { Flt64.Zero, Flt64.One },
            slopes: Array.Empty<Flt64>(),
            intercepts: new[] { Flt64.Zero }
        ).IsFailed);
        Assert.True(Create(
            input: input,
            points: new[] { Flt64.Zero, Flt64.One },
            slopes: new[] { Flt64.One },
            intercepts: Array.Empty<Flt64>()
        ).IsFailed);
        Assert.True(Create(
            input: input,
            points: new[] { Flt64.Zero, Flt64.One },
            slopes: new[] { new Flt64(double.NaN) },
            intercepts: new[] { Flt64.Zero }
        ).IsFailed);
        Assert.True(Create(
            input: input,
            points: new[] { Flt64.Zero, new Flt64(1e200) },
            slopes: new[] { new Flt64(1e200) },
            intercepts: new[] { Flt64.Zero }
        ).IsFailed);
        foreach (double bigM in new[] { 0, -1, double.NaN, double.PositiveInfinity }) {
            Assert.True(UnivariateLinearPiecewiseFunction<Flt64>.Create(
                input: input,
                breakpoints: new[] { Flt64.Zero, Flt64.One },
                slopes: new[] { Flt64.One },
                intercepts: new[] { Flt64.Zero },
                converter: IIntoValue<Flt64>.Identity,
                bigM: new Flt64(bigM)
            ).IsFailed);
        }
        Assert.True(UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: null,
            breakpoints: new[] { Flt64.Zero, Flt64.One },
            slopes: new[] { Flt64.One },
            intercepts: new[] { Flt64.Zero },
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: input,
            breakpoints: new[] { Flt64.Zero, Flt64.One },
            slopes: new[] { Flt64.One },
            intercepts: new[] { Flt64.Zero },
            converter: null
        ).IsFailed);
        x.Range.SetRange(ValueRange<Flt64>.Full());
        Assert.True(Create(
            input: input,
            points: new[] { Flt64.Zero, Flt64.One },
            slopes: new[] { Flt64.One },
            intercepts: new[] { Flt64.Zero }
        ).IsFailed);
        Assert.True(UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: input,
            breakpoints: new[] { Flt64.Zero, Flt64.One },
            slopes: new[] { Flt64.One },
            intercepts: new[] { Flt64.Zero },
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        ).IsOk);
        var defaultRange = new RealVar("pwl-default-range");
        Assert.True(Create(
            input: Polynomial(defaultRange),
            points: new[] { Flt64.Zero, Flt64.One },
            slopes: new[] { Flt64.One },
            intercepts: new[] { Flt64.Zero }
        ).IsFailed);
    }

    [Fact]
    public void Factory_ShouldSnapshotCollectionsAndDeriveOutwardOutputRange() {
        RealVar x = Bounded("pwl-snapshot");
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var points = new List<Flt64> { Flt64.Zero, Flt64.One, new Flt64(2) };
        var slopes = new List<Flt64> { new Flt64(2), new Flt64(-2) };
        var intercepts = new List<Flt64> { Flt64.Zero, new Flt64(5) };
        Result<UnivariateLinearPiecewiseFunction<Flt64>, ErrorCode, Error<ErrorCode>> result = Create(
            input: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
            points: points,
            slopes: slopes,
            intercepts: intercepts
        );
        Assert.True(result.IsOk, result.ToString());
        UnivariateLinearPiecewiseFunction<Flt64> function = result.Value;
        points[1] = new Flt64(100);
        slopes[0] = Flt64.Zero;
        intercepts[1] = Flt64.Zero;
        terms[0] = new LinearMonomial<Flt64>(new Flt64(-1), x);
        Assert.Equal(new Flt64(2), function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }));
        Assert.Equal(Flt64.Zero, function.OutputBounds.LowerBound);
        Assert.Equal(new Flt64(3), function.OutputBounds.UpperBound);
        Assert.Throws<NotSupportedException>(() => ((IList<Flt64>)function.Breakpoints)[0] = Flt64.One);
        Assert.Equal(new Flt64(3), function.ResultVar.Range.UpperBound!.Value.Unwrap());
    }

    [Fact]
    public void OutputEnvelope_ShouldIncludeNativeEndpointRoundingAndExactSolverValue() {
        RealVar x = Bounded("pwl-rounding");
        double first = global::System.Math.BitIncrement(1.0);
        double second = global::System.Math.BitIncrement(first);
        UnivariateLinearPiecewiseFunction<Flt64> function = Create(
            input: Polynomial(x),
            points: new[] { new Flt64(first), new Flt64(second) },
            slopes: new[] { new Flt64(1e16) },
            intercepts: new[] { new Flt64(-1e16) }
        ).Value;
        Flt64 evaluated = function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(first) })!.Value;
        Assert.Equal(new Flt64(2), evaluated);
        Assert.True(function.OutputBounds.LowerBound.Leq(evaluated));
        Assert.True(function.ResultVar.Range.LowerBound!.Value.Unwrap().Leq(evaluated));
        Assert.True(ExactBoundValue.TryRead(new Flt64(first), out ExactBoundValue point));
        Assert.True(ExactBoundValue.TryRead(new Flt64(1e16), out ExactBoundValue slope));
        Assert.True(ExactBoundValue.TryRead(new Flt64(-1e16), out ExactBoundValue intercept));
        Assert.True(ExactBoundValue.TryRead(function.ResultVar.Range.UpperBound!.Value.Unwrap(), out ExactBoundValue upper));
        Assert.True(slope.Multiply(point).Add(intercept).CompareTo(upper) <= 0);
    }

    [Fact]
    public async Task AutomaticBigM_ShouldRoundOutwardForEveryInactiveSegment() {
        RealVar x = Bounded("pwl-m-rounding");
        UnivariateLinearPiecewiseFunction<Flt64> function = Create(
            input: Polynomial(x),
            points: new[] { Flt64.Zero, new Flt64(0.1), new Flt64(0.3) },
            slopes: new[] { new Flt64(0.2), new Flt64(-0.1) },
            intercepts: new[] { Flt64.Zero, new Flt64(0.4) }
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.True(ExactBoundValue.TryRead(function.ResultVar.Range.LowerBound!.Value.Unwrap(), out ExactBoundValue outputLower));
        Assert.True(ExactBoundValue.TryRead(function.ResultVar.Range.UpperBound!.Value.Unwrap(), out ExactBoundValue outputUpper));
        Assert.True(ExactBoundValue.TryRead(function.Breakpoints[0], out ExactBoundValue inputLower));
        Assert.True(ExactBoundValue.TryRead(function.Breakpoints[^1], out ExactBoundValue inputUpper));
        for (int i = 0; i < function.Slopes.Count; ++i) {
            IConstraint<Flt64, LinearCategory> row = ((IMechanismModel<Flt64>)model).Constraints
                .Single(constraint => constraint.Name == $"{function.Name}_seg_{i}_eq_ub");
            Flt64 coefficient = row.Lhs.Cast<ILinearCell<Flt64>>()
                .Single(cell => cell.Token.Variable.Key == function.InactiveVars[i].Key).Coefficient.Abs();
            Assert.True(ExactBoundValue.TryRead(coefficient, out ExactBoundValue m));
            Assert.True(ExactBoundValue.TryRead(function.Breakpoints[i], out ExactBoundValue segmentLower));
            Assert.True(ExactBoundValue.TryRead(function.Breakpoints[i + 1], out ExactBoundValue segmentUpper));
            Assert.True(ExactBoundValue.TryRead(function.Slopes[i], out ExactBoundValue slope));
            Assert.True(ExactBoundValue.TryRead(function.Intercepts[i], out ExactBoundValue intercept));
            ExactBoundValue first = slope.Multiply(inputLower).Add(intercept);
            ExactBoundValue last = slope.Multiply(inputUpper).Add(intercept);
            ExactBoundValue lineLower = first.CompareTo(last) < 0 ? first : last;
            ExactBoundValue lineUpper = first.CompareTo(last) > 0 ? first : last;
            Assert.True(m.CompareTo(segmentLower.Add(inputLower.Negate())) >= 0);
            Assert.True(m.CompareTo(inputUpper.Add(segmentUpper.Negate())) >= 0);
            Assert.True(m.CompareTo(outputUpper.Add(lineLower.Negate())) >= 0);
            Assert.True(m.CompareTo(lineUpper.Add(outputLower.Negate())) >= 0);
        }
    }

    [Fact]
    public void ChangedNestedExpression_ShouldRejectRegistration() {
        RealVar x = Bounded("pwl-nested-x");
        var nested = new LinearExpressionSymbol(
            name: "pwl-nested",
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero)
        );
        UnivariateLinearPiecewiseFunction<Flt64> function = Create(
            input: Polynomial(nested),
            points: new[] { Flt64.Zero, Flt64.One },
            slopes: new[] { Flt64.One },
            intercepts: new[] { Flt64.Zero }
        ).Value;
        nested.AsMutable().SetConstant(Flt64.One);
        using var tokens = new ManualTokenList<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
        Assert.Empty(tokens.Tokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedCapturedBounds_ShouldInvalidateRegistrationAndDump(bool helper) {
        RealVar x = Bounded("pwl-stale");
        UnivariateLinearPiecewiseFunction<Flt64> function = Function(x);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        if (helper) {
            function.ResultVar.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), new Flt64(4)).Value);
        }
        else {
            x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
        }
        Assert.True(model.ToLinearTriadModel().IsFailed);
        using var tokens = new ManualTokenList<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
        Assert.Empty(tokens.Tokens);
    }

    [Fact]
    public void DuplicateHelper_ShouldNotPartiallyRegister() {
        RealVar x = Bounded("pwl-duplicate");
        UnivariateLinearPiecewiseFunction<Flt64> function = Function(x);
        using var parent = new LinearMetaModel<Flt64>(configuration: new MetaModelConfiguration(CheckTokenExists: true));
        Assert.True(parent.Add(function.SelectorVars[0]).IsOk);
        Token<Flt64> original = parent.Tokens.Find(function.SelectorVars[0])!;
        Assert.True(function.RegisterAuxiliaryTokens(parent.Tokens).IsFailed);
        Assert.Same(original, parent.Tokens.Find(function.SelectorVars[0]));
        Assert.Null(parent.Tokens.Find(function.ResultVar));
        Assert.Null(parent.Tokens.Find(function.InputVar));
        Assert.Null(parent.Tokens.Find(function.SelectorVars[1]));
    }

    [Fact]
    public void FromPoints_ShouldValidateAndBuildInterpolation() {
        RealVar x = Bounded("pwl-points");
        Point<Dim2, Flt64>[] points = new[] {
            Point<Dim2, Flt64>.Create(Flt64.Zero, Flt64.Zero),
            Point<Dim2, Flt64>.Create(Flt64.One, new Flt64(2)),
            Point<Dim2, Flt64>.Create(new Flt64(2), Flt64.Zero),
        };
        Result<UnivariateLinearPiecewiseFunction<Flt64>, ErrorCode, Error<ErrorCode>> result = UnivariateLinearPiecewiseFunction.FromPoints(
            input: Polynomial(x),
            points: points,
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(new Flt64(1), result.Value.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(1.5) }));
        Assert.True(UnivariateLinearPiecewiseFunction.FromPoints(
            input: Polynomial(x),
            points: new[] { points[0], points[0] },
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(UnivariateLinearPiecewiseFunction.FromPoints(
            input: Polynomial(x),
            points: new[] { points[0] },
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(UnivariateLinearPiecewiseFunction.FromPoints(
            input: Polynomial(x),
            points: new[] { points[0], Point<Dim2, Flt64>.Create(Flt64.One, new Flt64(double.NaN)) },
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
    }

    [Fact]
    public async Task RepresentativeNumericTypes_ShouldEvaluateAndBuild() {
        await VerifyNumeric(IIntoValue<Flt64>.Identity);
        await VerifyNumeric(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyNumeric(new TestNumberConverter<Integer>(value => value.ToInt64()));
        await VerifyNumeric(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await VerifyNumeric(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    private static async Task VerifyNumeric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        RealVar x = Bounded($"pwl-{typeof(V).Name}");
        V two = converter.IntoValue(new Flt64(2));
        Result<UnivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>> created = UnivariateLinearPiecewiseFunction<V>.Create(
            input: new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, x) }, converter.Zero),
            breakpoints: new[] { converter.Zero, converter.One, two },
            slopes: new[] { two, converter.One.Negate() },
            intercepts: new[] { converter.Zero, converter.IntoValue(new Flt64(3)) },
            converter: converter
        );
        Assert.True(created.IsOk, created.ToString());
        UnivariateLinearPiecewiseFunction<V> function = created.Value;
        Assert.True(function.Evaluate(new Dictionary<ISymbol, V> { [x] = converter.One })!.Value.Eq(two));
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        Assert.Equal(12, ((IMechanismModel<V>)model).Constraints.Count);
    }

    private static RealVar Bounded(string name) {
        var x = new RealVar(name);
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(3)).Value);
        return x;
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol x) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero
    );

    private static UnivariateLinearPiecewiseFunction<Flt64> Function(RealVar x, bool discontinuous = false) => Create(
        input: Polynomial(x),
        points: new[] { Flt64.Zero, Flt64.One, new Flt64(2) },
        slopes: new[] { new Flt64(2), new Flt64(-2) },
        intercepts: new[] { Flt64.Zero, new Flt64(discontinuous ? 5 : 4) }
    ).Value;

    private static Result<UnivariateLinearPiecewiseFunction<Flt64>, ErrorCode, Error<ErrorCode>> Create(
        LinearPolynomial<Flt64> input,
        IReadOnlyList<Flt64> points,
        IReadOnlyList<Flt64> slopes,
        IReadOnlyList<Flt64> intercepts) => UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: input,
            breakpoints: points,
            slopes: slopes,
            intercepts: intercepts,
            converter: IIntoValue<Flt64>.Identity
        );

    private sealed class SwitchingConverter : IIntoValue<Flt64> {
        public bool CorruptNegativeTwo { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => CorruptNegativeTwo && value.ToDouble() == -2
            ? Flt64.Zero : value;
    }
}
