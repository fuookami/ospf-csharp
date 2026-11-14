#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class DecimalPiecewiseFunctionTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DefaultTrigonometricGraph_ShouldEvaluateAndBuild(bool cosine, bool quadratic) {
        RealVar x = Variable();
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        IMathFunctionSymbol<FltX> function;
        if (cosine) {
            Result<CosFunction<FltX>, ErrorCode, Error<ErrorCode>> created = CosFunction<FltX>.Create(Input(x), converter);
            Assert.True(created.IsOk, created.ToString());
            function = created.Value;
        }
        else {
            Result<SinFunction<FltX>, ErrorCode, Error<ErrorCode>> created = SinFunction<FltX>.Create(Input(x), converter);
            Assert.True(created.IsOk, created.ToString());
            function = created.Value;
        }
        UnivariateLinearPiecewiseFunction<FltX> piecewise = ((IUnivariatePiecewiseFunction<FltX>)function).Piecewise;
        Assert.Equal(cosine ? FltX.One : FltX.Zero, function.Evaluate(new Dictionary<ISymbol, FltX> { [x] = FltX.Zero }));
        for (int i = 0; i < piecewise.Slopes.Count; ++i) {
            FltX midpoint = piecewise.Breakpoints[i].Plus(piecewise.Breakpoints[i + 1]).Div(new FltX(2m));
            foreach (FltX input in new[] { midpoint, piecewise.Breakpoints[i + 1] }) {
                FltX expected = piecewise.Slopes[i].Times(input).Plus(piecewise.Intercepts[i]);
                Assert.Equal(expected, function.Evaluate(new Dictionary<ISymbol, FltX> { [x] = input }));
            }
        }
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, FltX> { [x] = new FltX(4m) }));
        if (quadratic) {
            using var parent = new QuadraticMetaModel<FltX>(converter: converter);
            Assert.True(parent.Add(x).IsOk);
            Assert.True(parent.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk);
            Result<QuadraticMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built = await QuadraticMechanismModel<FltX>.InvokeAsync(parent);
            Assert.True(built.IsOk, built.ToString());
            using QuadraticMechanismModel<FltX> model = built.Value;
            Assert.Equal(22, ((IAbstractQuadraticMechanismModel<FltX>)model).Constraints.Count);
        }
        else {
            using var parent = new LinearMetaModel<FltX>(converter: converter);
            Assert.True(parent.Add(x).IsOk);
            Assert.True(parent.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk);
            Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<FltX>.InvokeAsync(parent);
            Assert.True(built.IsOk, built.ToString());
            using LinearMechanismModel<FltX> model = built.Value;
            Assert.Equal(22, ((IMechanismModel<FltX>)model).Constraints.Count);
        }
    }

    [Fact]
    public async Task DecimalGraphs_ShouldCoverBothEncodingsAndEveryInactiveSegment() {
        RealVar x = Variable();
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        Result<UnivariateLinearPiecewiseFunction<FltX>, ErrorCode, Error<ErrorCode>> created = UnivariateLinearPiecewiseFunction<FltX>.Create(
            input: Input(x),
            breakpoints: new[] { new FltX(0.1m), new FltX(0.3m), new FltX(1.1m) },
            slopes: new[] { new FltX(0.2m), new FltX(-0.4m) },
            intercepts: new[] { new FltX(0.3m), new FltX(0.5m) },
            converter: converter
        );
        Assert.True(created.IsOk, created.ToString());
        UnivariateLinearPiecewiseFunction<FltX> function = created.Value;
        using var parent = new LinearMetaModel<FltX>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk);
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<FltX>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<FltX> model = built.Value;
        ExactBoundValue inputLower = Read(function.InputVar.Range.LowerBound!.Value.Unwrap());
        ExactBoundValue inputUpper = Read(function.InputVar.Range.UpperBound!.Value.Unwrap());
        ExactBoundValue outputLower = Read(function.ResultVar.Range.LowerBound!.Value.Unwrap());
        ExactBoundValue outputUpper = Read(function.ResultVar.Range.UpperBound!.Value.Unwrap());
        ExactBoundValue nativeLower = Read(function.OutputBounds.LowerBound);
        ExactBoundValue nativeUpper = Read(function.OutputBounds.UpperBound);
        for (int i = 0; i < function.Slopes.Count; ++i) {
            IConstraint<FltX, LinearCategory> row = ((IMechanismModel<FltX>)model).Constraints
                .Single(constraint => constraint.Name == $"{function.Name}_seg_{i}_eq_ub");
            FltX magnitude = row.Lhs.Cast<ILinearCell<FltX>>()
                .Single(cell => cell.Token.Variable.Key == function.InactiveVars[i].Key).Coefficient.Abs();
            foreach (bool encoded in new[] { false, true }) {
                ExactBoundValue pointLower = Read(function.Breakpoints[i], encoded);
                ExactBoundValue pointUpper = Read(function.Breakpoints[i + 1], encoded);
                ExactBoundValue slope = Read(function.Slopes[i], encoded);
                ExactBoundValue intercept = Read(function.Intercepts[i], encoded);
                ExactBoundValue m = Read(magnitude, encoded);
                Assert.True(inputLower.CompareTo(pointLower) <= 0);
                Assert.True(inputUpper.CompareTo(pointUpper) >= 0);
                foreach (ExactBoundValue point in new[] { pointLower, pointUpper }) {
                    ExactBoundValue endpoint = slope.Multiply(point).Add(intercept);
                    Assert.True(outputLower.CompareTo(endpoint) <= 0);
                    Assert.True(outputUpper.CompareTo(endpoint) >= 0);
                    Assert.True(nativeLower.CompareTo(endpoint) <= 0);
                    Assert.True(nativeUpper.CompareTo(endpoint) >= 0);
                }
                foreach (int endpoint in new[] { i, i + 1 }) {
                    FltX evaluated = function.Slopes[i].Times(function.Breakpoints[endpoint]).Plus(function.Intercepts[i]);
                    ExactBoundValue rounded = Read(evaluated, encoded);
                    Assert.True(outputLower.CompareTo(rounded) <= 0);
                    Assert.True(outputUpper.CompareTo(rounded) >= 0);
                    Assert.True(nativeLower.CompareTo(rounded) <= 0);
                    Assert.True(nativeUpper.CompareTo(rounded) >= 0);
                }
                ExactBoundValue first = slope.Multiply(inputLower).Add(intercept);
                ExactBoundValue last = slope.Multiply(inputUpper).Add(intercept);
                ExactBoundValue lineLower = first.CompareTo(last) <= 0 ? first : last;
                ExactBoundValue lineUpper = first.CompareTo(last) >= 0 ? first : last;
                Assert.True(m.CompareTo(pointLower.Add(inputLower.Negate())) >= 0);
                Assert.True(m.CompareTo(inputUpper.Add(pointUpper.Negate())) >= 0);
                Assert.True(m.CompareTo(outputUpper.Add(lineLower.Negate())) >= 0);
                Assert.True(m.CompareTo(lineUpper.Add(outputLower.Negate())) >= 0);
            }
        }
    }

    [Fact]
    public void Factory_ShouldRejectEncodedBreakpointCollisionAndWrongNegation() {
        RealVar x = Variable();
        var converter = new SwitchingDecimalConverter();
        Assert.True(UnivariateLinearPiecewiseFunction<FltX>.Create(
            input: Input(x),
            breakpoints: new[] { FltX.One, new FltX(1.0000000000000000000000000001m) },
            slopes: new[] { FltX.One },
            intercepts: new[] { FltX.Zero },
            converter: converter
        ).IsFailed);
        converter.Corrupt = -0.1m;
        Assert.True(UnivariateLinearPiecewiseFunction<FltX>.Create(
            input: Input(x),
            breakpoints: new[] { new FltX(0.1m), FltX.One },
            slopes: new[] { FltX.One },
            intercepts: new[] { FltX.Zero },
            converter: converter
        ).IsFailed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Preparation_ShouldPublishOnlyProvenValues(int scenario) {
        RealVar x = Variable();
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        UnivariateLinearPiecewiseFunction<FltX> function = SimpleFunction(x, converter);
        var adapter = new LinearFunctionSymbolAdapter<FltX>(function);
        using var parent = new LinearMetaModel<FltX>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        var fixedValues = new Dictionary<IVariableItem, FltX> { [x] = scenario == 1 ? FltX.One : FltX.Zero };
        if (scenario == 2) {
            fixedValues[function.ResultVar] = FltX.Zero;
        }
        else if (scenario == 3) {
            fixedValues[function.InactiveVars[0]] = FltX.One;
        }
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<FltX>.InvokeAsync(
            metaModel: parent,
            fixedVariables: fixedValues
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<FltX> model = built.Value;
        Assert.Equal(scenario == 0 ? FltX.One : null, model.Tokens.CachedValue(adapter, (IReadOnlyList<FltX>?)null));
    }

    [Fact]
    public async Task EncodedSharedBreakpoint_ShouldSuppressEvidenceEvenWhenNativeInputIsInsideOneSegment() {
        RealVar x = Variable();
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        Result<UnivariateLinearPiecewiseFunction<FltX>, ErrorCode, Error<ErrorCode>> created = UnivariateLinearPiecewiseFunction<FltX>.Create(
            input: Input(x),
            breakpoints: new[] { FltX.Zero, new FltX(0.1m), FltX.One },
            slopes: new[] { FltX.One, FltX.One },
            intercepts: new[] { FltX.Zero, FltX.Zero },
            converter: converter
        );
        Assert.True(created.IsOk, created.ToString());
        var adapter = new LinearFunctionSymbolAdapter<FltX>(created.Value);
        var input = new FltX(0.1000000000000000000000000001m);
        Assert.True(input.ToFlt64().Eq(new FltX(0.1m).ToFlt64()));
        Assert.Equal(input, created.Value.Evaluate(new Dictionary<ISymbol, FltX> { [x] = input }));
        using var parent = new LinearMetaModel<FltX>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<FltX>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, FltX> { [x] = input }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<FltX> model = built.Value;
        Assert.Null(model.Tokens.CachedValue(adapter, (IReadOnlyList<FltX>?)null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DownstreamEvidence_ShouldRevalidateDecimalConversion(bool corrupt) {
        RealVar x = Variable();
        var converter = new SwitchingDecimalConverter();
        UnivariateLinearPiecewiseFunction<FltX> function = SimpleFunction(x, converter);
        var producer = new LinearFunctionSymbolAdapter<FltX>(function);
        Result<IfFunction<FltX>, ErrorCode, Error<ErrorCode>> condition = IfFunction<FltX>.Create(
            condition: new LinearPolynomial<FltX>(
                new[] { new LinearMonomial<FltX>(new FltX(2m), producer) }, new FltX(-1m)),
            converter: converter,
            strictBoundary: FltX.One,
            bounds: new LinearPolynomialBounds<FltX>(FltX.One, new FltX(3m))
        );
        Assert.True(condition.IsOk, condition.ToString());
        var consumer = new LinearFunctionSymbolAdapter<FltX>(condition.Value);
        using var parent = new LinearMetaModel<FltX>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Try registered = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            converter: converter,
            fixedValues: new Dictionary<ISymbol, FltX> { [x] = FltX.Zero },
            callBack: status => {
                if (corrupt && status.ReadySymbolAmount.ToULong() == 1) {
                    converter.Corrupt = -0.1m;
                }
                return Results.OkInstance;
            }
        );
        Assert.True(registered.IsOk, registered.ToString());
        Assert.Equal(FltX.One, parent.Tokens.CachedValue(producer, (IReadOnlyList<FltX>?)null));
        Assert.Equal(corrupt ? null : FltX.One, parent.Tokens.CachedValue(consumer, (IReadOnlyList<FltX>?)null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Preparation_ShouldUseAndRevalidateTheTableConverter(int scenario) {
        RealVar x = Variable();
        var functionConverter = new SwitchingDecimalConverter();
        var tableConverter = new SwitchingDecimalConverter();
        UnivariateLinearPiecewiseFunction<FltX> function = SimpleFunction(x, functionConverter);
        var producer = new LinearFunctionSymbolAdapter<FltX>(function);
        Result<IfFunction<FltX>, ErrorCode, Error<ErrorCode>> condition = IfFunction<FltX>.Create(
            condition: new LinearPolynomial<FltX>(
                new[] { new LinearMonomial<FltX>(new FltX(2m), producer) }, new FltX(-1m)),
            converter: functionConverter,
            strictBoundary: FltX.One,
            bounds: new LinearPolynomialBounds<FltX>(FltX.One, new FltX(3m))
        );
        Assert.True(condition.IsOk, condition.ToString());
        var consumer = new LinearFunctionSymbolAdapter<FltX>(condition.Value);
        using var parent = new LinearMetaModel<FltX>(converter: tableConverter);
        Assert.True(parent.Add(x).IsOk);
        var values = new Dictionary<ISymbol, FltX> { [x] = FltX.Zero };
        if (scenario == 1) {
            tableConverter.Corrupt = -0.1m;
        }
        Try registered = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            converter: tableConverter,
            fixedValues: values,
            callBack: status => {
                if (scenario == 2 && status.ReadySymbolAmount.ToULong() == 1) {
                    tableConverter.Corrupt = -0.1m;
                }
                return Results.OkInstance;
            }
        );
        Assert.True(registered.IsOk, registered.ToString());
        Assert.Equal(scenario == 1 ? null : FltX.One, parent.Tokens.CachedValue(producer, (IReadOnlyList<FltX>?)null));
        Assert.Equal(scenario == 0 ? FltX.One : null, parent.Tokens.CachedValue(consumer, (IReadOnlyList<FltX>?)null));
        Assert.Equal(scenario == 0 ? FltX.One : null, producer.Prepare(
            values: values,
            tokenTable: parent.Tokens,
            converter: tableConverter
        ));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BeforeCommitConversionChange_ShouldRollBackHelpers(bool quadratic, bool changeModelConverter) {
        RealVar x = Variable();
        var functionConverter = new SwitchingDecimalConverter();
        var modelConverter = new SwitchingDecimalConverter();
        UnivariateLinearPiecewiseFunction<FltX> function = SimpleFunction(x, functionConverter);
        using AbstractMetaModel<FltX> parent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: modelConverter)
            : new LinearMetaModel<FltX>(converter: modelConverter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Tokens.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk);
        Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
        Token<FltX> original = parent.Tokens.Find(x)!;
        bool callbackRan = false;
        Try Change(BasicMechanismModel<FltX> model) {
            Assert.True(model.ValidateBoundDependencies().IsOk);
            callbackRan = true;
            (changeModelConverter ? modelConverter : functionConverter).Corrupt = -0.1m;
            return Results.OkInstance;
        }
        Result<IMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built = quadratic
            ? (await QuadraticMechanismModel<FltX>.InvokeBeforeCommitAsync(
                metaModel: (QuadraticMetaModel<FltX>)parent,
                fixedVariables: null,
                registrationStatusCallBack: null,
                beforeCommit: model => Change(model)
            )).Map<IMechanismModel<FltX>>(model => model)
            : (await LinearMechanismModel<FltX>.InvokeBeforeCommitAsync(
                metaModel: (LinearMetaModel<FltX>)parent,
                fixedVariables: null,
                registrationStatusCallBack: null,
                beforeCommit: model => Change(model)
            )).Map<IMechanismModel<FltX>>(model => model);
        if (built.IsOk) {
            built.Value.Dispose();
        }
        Assert.True(callbackRan);
        Assert.True(built.IsFailed);
        Assert.Same(original, parent.Tokens.Find(x));
        Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversionChangeDuringWrite_ShouldRollBackRows(bool quadratic) {
        RealVar x = Variable();
        var converter = new SwitchingDecimalConverter();
        UnivariateLinearPiecewiseFunction<FltX> function = SimpleFunction(x, converter);
        using AbstractMetaModel<FltX> parent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: converter)
            : new LinearMetaModel<FltX>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(function.HelperVariables).IsOk);
        using IAbstractLinearMechanismModel<FltX> model = quadratic
            ? (await QuadraticMechanismModel<FltX>.InvokeAsync((QuadraticMetaModel<FltX>)parent)).Value
            : (await LinearMechanismModel<FltX>.InvokeAsync((LinearMetaModel<FltX>)parent)).Value;
        Assert.True(model.AddConstraint(
            relation: IndicatorSupport.Row(
                monomials: Array.Empty<LinearMonomial<FltX>>(),
                constant: FltX.Zero,
                comparison: Comparison.LE,
                rhs: FltX.One,
                name: "original"
            ),
            from: (IIntermediateSymbol?)null
        ).IsOk);
        object original = quadratic
            ? ((IAbstractQuadraticMechanismModel<FltX>)model).Constraints[0]
            : model.Constraints[0];
        using var writer = new ChangingWriter(model, () => converter.Corrupt = -0.1m);
        Try result = function.RegisterConstraints(writer);
        Assert.True(result.IsFailed);
        Assert.True(writer.Writes > 0);
        if (quadratic) {
            Assert.Single(((IAbstractQuadraticMechanismModel<FltX>)model).Constraints);
            Assert.Same(original, ((IAbstractQuadraticMechanismModel<FltX>)model).Constraints[0]);
        }
        else {
            Assert.Single(model.Constraints);
            Assert.Same(original, model.Constraints[0]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedConversion_ShouldInvalidateBuiltMechanism(bool quadratic) {
        RealVar x = Variable();
        var converter = new SwitchingDecimalConverter();
        UnivariateLinearPiecewiseFunction<FltX> function = SimpleFunction(x, converter);
        using AbstractMetaModel<FltX> parent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: converter)
            : new LinearMetaModel<FltX>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk);
        using IMechanismModel<FltX> model = quadratic
            ? (await QuadraticMechanismModel<FltX>.InvokeAsync((QuadraticMetaModel<FltX>)parent)).Value
            : (await LinearMechanismModel<FltX>.InvokeAsync((LinearMetaModel<FltX>)parent)).Value;
        var mechanism = (BasicMechanismModel<FltX>)model;
        Assert.True(mechanism.ValidateBoundDependencies().IsOk);
        converter.Corrupt = -0.1m;
        Assert.True(mechanism.ValidateBoundDependencies().IsFailed);
        converter.Corrupt = null;
        Assert.True(mechanism.ValidateBoundDependencies().IsOk);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DistinctModelConverter_ShouldBeCheckedDuringAndAfterBuild(bool quadratic, bool corruptBeforeBuild) {
        RealVar x = Variable();
        var functionConverter = new SwitchingDecimalConverter();
        var modelConverter = new SwitchingDecimalConverter();
        UnivariateLinearPiecewiseFunction<FltX> function = SimpleFunction(x, functionConverter);
        using AbstractMetaModel<FltX> parent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: modelConverter)
            : new LinearMetaModel<FltX>(converter: modelConverter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Tokens.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk);
        if (corruptBeforeBuild) {
            modelConverter.Corrupt = -0.1m;
        }
        Result<IMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built = quadratic
            ? (await QuadraticMechanismModel<FltX>.InvokeAsync((QuadraticMetaModel<FltX>)parent)).Map<IMechanismModel<FltX>>(value => value)
            : (await LinearMechanismModel<FltX>.InvokeAsync((LinearMetaModel<FltX>)parent)).Map<IMechanismModel<FltX>>(value => value);
        if (corruptBeforeBuild) {
            if (built.IsOk) {
                built.Value.Dispose();
            }
            Assert.True(built.IsFailed);
            Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
            return;
        }
        Assert.True(built.IsOk, built.ToString());
        using IMechanismModel<FltX> model = built.Value;
        var mechanism = (BasicMechanismModel<FltX>)model;
        Assert.True(mechanism.ValidateBoundDependencies().IsOk);
        modelConverter.Corrupt = -0.1m;
        Assert.True(mechanism.ValidateBoundDependencies().IsFailed);
    }

    private static UnivariateLinearPiecewiseFunction<FltX> SimpleFunction(RealVar x, IIntoValue<FltX> converter) {
        Result<UnivariateLinearPiecewiseFunction<FltX>, ErrorCode, Error<ErrorCode>> created = UnivariateLinearPiecewiseFunction<FltX>.Create(
            input: Input(x),
            breakpoints: new[] { new FltX(-1m), FltX.One },
            slopes: new[] { new FltX(0.1m) },
            intercepts: new[] { FltX.One },
            converter: converter
        );
        Assert.True(created.IsOk, created.ToString());
        return created.Value;
    }

    private static ExactBoundValue Read(FltX value, bool encoded = false) =>
        encoded ? Read(value.ToFlt64()) : ExactBoundValue.TryRead(value, out ExactBoundValue result) ? result : throw new InvalidOperationException();

    private static ExactBoundValue Read(Flt64 value) =>
        ExactBoundValue.TryRead(value, out ExactBoundValue result) ? result : throw new InvalidOperationException();

    private static LinearPolynomial<FltX> Input(ISymbol x) => new(
        new[] { new LinearMonomial<FltX>(FltX.One, x) }, FltX.Zero);

    private static RealVar Variable() {
        var x = new RealVar($"decimal-pwl-{Guid.NewGuid()}");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        return x;
    }

    private sealed class SwitchingDecimalConverter : IIntoValue<FltX> {
        public decimal? Corrupt { get; set; }
        public FltX Zero => FltX.Zero;
        public FltX One => FltX.One;
        public FltX IntoValue(Flt64 value) => value.ToFltX();
        public Flt64 FromValue(FltX value) => Corrupt == value.ToDecimal()
            ? new Flt64(global::System.Math.BitIncrement(value.ToFlt64().ToDouble()))
            : value.ToFlt64();
    }

    private sealed class ChangingWriter(IAbstractLinearMechanismModel<FltX> model, Action change) : IAbstractLinearMechanismModel<FltX> {
        public int Writes { get; private set; }
        public string Name => model.Name;
        public IReadOnlyList<IConstraint<FltX, LinearCategory>> Constraints => model.Constraints;
        public IObject ObjectFunction => model.ObjectFunction;
        public IAbstractTokenTable<FltX> Tokens => model.Tokens;
        public void Dispose() { }

        public Try AddConstraint(
            LinearInequality<FltX> relation,
            string? name = null,
            (IIntermediateSymbol Symbol, bool Direction)? from = null) {
            Try result = model.AddConstraint(
                relation: relation,
                name: name,
                from: from
            );
            if (++Writes == 1) {
                change();
            }
            return result;
        }

        public Try AddConstraint(
            LinearInequality<FltX> relation,
            string? name = null,
            IIntermediateSymbol? from = null) => AddConstraint(
                relation: relation,
                name: name,
                from: from is null ? null : (from, false)
            );

        public Result<IConstraintWriteTransaction, ErrorCode, Error<ErrorCode>> BeginConstraintWrite() => model.BeginConstraintWrite();
    }
}
