#nullable enable

using System;
using System.Collections.Generic;
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
using Fuookami.Ospf.Core.Model.Basic;
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

public class TrigonometricFunctionTest {
    [Theory]
    [InlineData(false, -3.141592653589793, 0)]
    [InlineData(false, -1.5707963267948966, -1)]
    [InlineData(false, 0, 0)]
    [InlineData(false, 1.5707963267948966, 1)]
    [InlineData(false, 3.141592653589793, 0)]
    [InlineData(true, -3.141592653589793, -1)]
    [InlineData(true, -1.5707963267948966, 0)]
    [InlineData(true, 0, 1)]
    [InlineData(true, 1.5707963267948966, 0)]
    [InlineData(true, 3.141592653589793, -1)]
    public void DefaultPoints_ShouldEvaluateThePiecewiseGraph(bool cosine, double input, double expected) {
        RealVar x = Variable();
        IMathFunctionSymbol<Flt64> function = Create(x, cosine);

        Result<Flt64?, ErrorCode, Error<ErrorCode>> value = function.TryEvaluate(
            new Dictionary<ISymbol, Flt64> { [x] = new Flt64(input) });

        Assert.True(value.IsOk, value.ToString());
        Assert.InRange(
            actual: value.Value!.Value.ToDouble(),
            low: expected - 1e-14,
            high: expected + 1e-14
        );
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64>()));
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(4) }));
    }

    [Fact]
    public async Task DefaultPoints_ShouldSupportRepresentableGenericNumbers() {
        await VerifyDefault(IIntoValue<Flt64>.Identity);
        await VerifyDefault(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyDefault(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    [Fact]
    public async Task ExactCustomPoints_ShouldSupportDecimalAndIntegerNumbers() {
        await VerifyCustom(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await VerifyCustom(new TestNumberConverter<Integer>(value => value.ToInt64()));
    }

    [Fact]
    public void DefaultIntegerPoints_ShouldRejectUnsafeEncoding() {
        RealVar x = Variable();
        var integerConverter = new TestNumberConverter<Integer>(value => value.ToInt64());
        Assert.True(SinFunction<Integer>.Create(Input(x, integerConverter), integerConverter).IsFailed);
        Assert.True(CosFunction<Integer>.Create(Input(x, integerConverter), integerConverter).IsFailed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSamplesAndBounds_ShouldReturnFailure(bool cosine) {
        RealVar x = Variable();
        Point<Dim2, Flt64>[] valid = { Point(-1, 0), Point(1, 2) };
        foreach (Point<Dim2, Flt64>[] points in new[] {
            Array.Empty<Point<Dim2, Flt64>>(),
            new[] { valid[0] },
            new[] { valid[0], valid[0] },
            new[] { valid[1], valid[0] },
            new[] { valid[0], Point(1, double.NaN) },
            new[] { Point(-double.MaxValue, 0), Point(double.MaxValue, 1) },
        }) {
            Assert.True(CreateResult(
                x: x,
                cosine: cosine,
                points: points
            ).IsFailed);
        }
        var unbounded = new RealVar("trig-unbounded");
        Assert.True(CreateResult(
            x: unbounded,
            cosine: cosine,
            points: valid
        ).IsFailed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Samples_ShouldBeSnapshottedAndConvertedAfterFlt64Arithmetic(bool cosine) {
        RealVar x = Variable();
        Flt64[] coordinates = { Flt64.Zero, Flt64.Zero };
        Point<Dim2, Flt64>[] points = { new(coordinates, Dim2.Instance), Point(0.004, 1) };
        var converter = new TestNumberConverter<Flt32>(value => value.ToFlt32());
        Result<IUnivariatePiecewiseFunction<Flt32>, ErrorCode, Error<ErrorCode>> created = cosine
            ? CosFunction<Flt32>.Create(
                input: Input(x, converter),
                converter: converter,
                samplingPoints: points
            ).Map<IUnivariatePiecewiseFunction<Flt32>>(value => value)
            : SinFunction<Flt32>.Create(
                input: Input(x, converter),
                converter: converter,
                samplingPoints: points
            ).Map<IUnivariatePiecewiseFunction<Flt32>>(value => value);
        Assert.True(created.IsOk, created.ToString());
        UnivariateLinearPiecewiseFunction<Flt32> piecewise = created.Value.Piecewise;
        IReadOnlyList<Point<Dim2, Flt64>> snapshot = created.Value switch {
            SinFunction<Flt32> sine => sine.SamplingPoints,
            CosFunction<Flt32> cosineFunction => cosineFunction.SamplingPoints,
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(new Flt64(1.0 / 0.004).ToFlt32(), piecewise.Slopes[0]);
        Assert.NotEqual(new Flt64(1.0 / (double)(float)0.004).ToFlt32(), piecewise.Slopes[0]);
        coordinates[0] = new Flt64(-100);
        points[1] = Point(3, 5);
        Assert.Equal(converter.Zero, piecewise.Breakpoints[0]);
        Assert.Equal(new Flt64(0.004).ToFlt32(), piecewise.Breakpoints[1]);
        Assert.Equal(Flt64.Zero, snapshot[0][0]);
        Assert.Equal(new Flt64(0.004), snapshot[1][0]);
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, -1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void DownstreamEvidence_ShouldBeRevalidated(bool cosine, int change) {
        RealVar x = Variable();
        Point<Dim2, Flt64>[] points = { Point(-1, 0), Point(1, 2) };
        IMathFunctionSymbol<Flt64> function = CreateResult(
            x: x,
            cosine: cosine,
            points: points
        ).Value;
        UnivariateLinearPiecewiseFunction<Flt64> piecewise = ((IUnivariatePiecewiseFunction<Flt64>)function).Piecewise;
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        var consumer = new LinearFunctionSymbolAdapter<Flt64>(IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), producer) }, new Flt64(-1)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value);
        var values = new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero };
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try registered = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: values,
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 1) {
                    if (change == 0) {
                        values[x] = new Flt64(0.5);
                    }
                    else if (change == 1) {
                        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-5), new Flt64(5)).Value);
                    }
                    else if (change == 2) {
                        values[piecewise.InactiveVars[0]] = Flt64.One;
                    }
                }
                return Results.OkInstance;
            }
        );
        Assert.True(registered.IsOk, registered.ToString());
        Assert.Equal(Flt64.One, parent.Tokens.CachedValue(producer, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(change == -1 ? Flt64.One : null, parent.Tokens.CachedValue(consumer, (IReadOnlyList<Flt64>?)null));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task ChangedDependencies_ShouldRejectOldMatrix(bool cosine, bool quadratic, bool changeConverter) {
        RealVar x = Variable();
        var nested = new LinearExpressionSymbol(
            name: "trig-lifecycle-input",
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero)
        );
        var converter = new SwitchingConverter();
        Point<Dim2, Flt64>[] points = { Point(-1, 0), Point(1, 2) };
        IMathFunctionSymbol<Flt64> function = cosine
            ? CosFunction<Flt64>.Create(
                input: Input(nested, converter),
                converter: converter,
                samplingPoints: points
            ).Value
            : SinFunction<Flt64>.Create(
                input: Input(nested, converter),
                converter: converter,
                samplingPoints: points
            ).Value;
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(converter: converter)
            : new LinearMetaModel<Flt64>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(nested).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        if (quadratic) {
            using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
            Change();
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsFailed);
            Assert.True(model.ToQuadraticTetradModel().IsFailed);
        }
        else {
            using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent)).Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
            Change();
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsFailed);
            Assert.True(model.ToLinearTriadModel().IsFailed);
        }

        void Change() {
            if (changeConverter) {
                converter.Corrupt = true;
            }
            else {
                nested.AsMutable().SetConstant(Flt64.One);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedBreakpoint_ShouldNotPublishAnUnprovenCache(bool cosine) {
        RealVar x = Variable();
        IMathFunctionSymbol<Flt64> function = Create(x, cosine);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(cosine ? Flt64.One : Flt64.Zero, function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }));
        Assert.Null(model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateHelper_ShouldNotPartiallyRegister(bool cosine) {
        RealVar x = Variable();
        IMathFunctionSymbol<Flt64> function = Create(x, cosine);
        UnivariateLinearPiecewiseFunction<Flt64> piecewise = ((IUnivariatePiecewiseFunction<Flt64>)function).Piecewise;
        using var parent = new LinearMetaModel<Flt64>(configuration: new MetaModelConfiguration(CheckTokenExists: true));
        Assert.True(parent.Add(piecewise.SelectorVars[0]).IsOk);
        Token<Flt64> original = parent.Tokens.Find(piecewise.SelectorVars[0])!;
        Assert.True(function.RegisterAuxiliaryTokens(parent.Tokens).IsFailed);
        Assert.Same(original, parent.Tokens.Find(piecewise.SelectorVars[0]));
        Assert.Null(parent.Tokens.Find(piecewise.ResultVar));
        Assert.Null(parent.Tokens.Find(piecewise.InputVar));
        Assert.Null(parent.Tokens.Find(piecewise.SelectorVars[1]));
    }

    private static async Task VerifyDefault<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        RealVar x = Variable();
        Result<SinFunction<V>, ErrorCode, Error<ErrorCode>> sin = SinFunction<V>.Create(Input(x, converter), converter);
        Result<CosFunction<V>, ErrorCode, Error<ErrorCode>> cos = CosFunction<V>.Create(Input(x, converter), converter);
        Assert.True(sin.IsOk, sin.ToString());
        Assert.True(cos.IsOk, cos.ToString());
        var values = new Dictionary<ISymbol, V> { [x] = converter.Zero };
        Assert.True(sin.Value.Evaluate(values)!.Value.Eq(converter.Zero));
        Assert.True(cos.Value.Evaluate(values)!.Value.Eq(converter.One));
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(sin.Value)).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(cos.Value)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        Assert.Equal(44, ((IMechanismModel<V>)model).Constraints.Count);
    }

    private static async Task VerifyCustom<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        RealVar x = Variable();
        Point<Dim2, Flt64>[] points = { Point(-1, 0), Point(1, 2) };
        Result<SinFunction<V>, ErrorCode, Error<ErrorCode>> sin = SinFunction<V>.Create(
            input: Input(x, converter),
            converter: converter,
            samplingPoints: points
        );
        Result<CosFunction<V>, ErrorCode, Error<ErrorCode>> cos = CosFunction<V>.Create(
            input: Input(x, converter),
            converter: converter,
            samplingPoints: points
        );
        Assert.True(sin.IsOk, sin.ToString());
        Assert.True(cos.IsOk, cos.ToString());
        var values = new Dictionary<ISymbol, V> { [x] = converter.Zero };
        Assert.True(sin.Value.Evaluate(values)!.Value.Eq(converter.One));
        Assert.True(cos.Value.Evaluate(values)!.Value.Eq(converter.One));
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(sin.Value)).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(cos.Value)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        Assert.Equal(14, ((IMechanismModel<V>)model).Constraints.Count);
    }

    private static IMathFunctionSymbol<Flt64> Create(RealVar x, bool cosine) =>
        CreateResult(
            x: x,
            cosine: cosine,
            points: null
        ).Value;

    private static Result<IMathFunctionSymbol<Flt64>, ErrorCode, Error<ErrorCode>> CreateResult(
        RealVar x, bool cosine, IReadOnlyList<Point<Dim2, Flt64>>? points) => cosine
        ? CosFunction<Flt64>.Create(
            input: Input(x, IIntoValue<Flt64>.Identity),
            converter: IIntoValue<Flt64>.Identity,
            samplingPoints: points
        )
            .Map<IMathFunctionSymbol<Flt64>>(function => function)
        : SinFunction<Flt64>.Create(
            input: Input(x, IIntoValue<Flt64>.Identity),
            converter: IIntoValue<Flt64>.Identity,
            samplingPoints: points
        )
            .Map<IMathFunctionSymbol<Flt64>>(function => function);

    private static LinearPolynomial<V> Input<V>(ISymbol x, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> => new(
            new[] { new LinearMonomial<V>(converter.One, x) }, converter.Zero);

    private static Point<Dim2, Flt64> Point(double x, double y) =>
        Point<Dim2, Flt64>.Create(new Flt64(x), new Flt64(y));

    private static RealVar Variable() {
        var variable = new RealVar($"trig-{Guid.NewGuid()}");
        variable.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        return variable;
    }

    private sealed class SwitchingConverter : IIntoValue<Flt64> {
        public bool Corrupt { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => Corrupt && value == new Flt64(-1) ? Flt64.Zero : value;
    }
}
