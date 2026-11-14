#nullable enable

using System;
using System.Collections.Generic;
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
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class ConditionalEvaluationSafetyTest {
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task IntervalGroupingMismatch_ShouldFailInsteadOfCachingWrongBranch(int input) {
        var x = new RealVar("ifin-safe-grouping");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        IfInFunction<Flt64> function = IfInFunction<Flt64>.Create(
            x: Polynomial(x, 1e16),
            lower: new Flt64(1e16),
            upper: new Flt64(1e16),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(input) };
        AssertIllegal(function.Classify(values));
        AssertIllegal(function.TryEvaluate(values));
        Assert.Null(function.Evaluate(values));
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        parent.Tokens.Cache(
            cacheKey: adapter,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.Zero
        );
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input) }
            );
        Assert.True(built.IsFailed);
        Assert.Equal(Flt64.Zero, parent.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Theory]
    [InlineData(Comparison.GT, 1)]
    [InlineData(Comparison.LE, 1)]
    [InlineData(Comparison.GE, -1)]
    [InlineData(Comparison.LT, -1)]
    public async Task ConditionalGroupingMismatch_ShouldFailInsteadOfCachingWrongBranch(Comparison relation, int input) {
        var x = new RealVar("if-safe-grouping-x");
        var y = new BinVar("if-safe-grouping-y");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var condition = new LinearPolynomial<Flt64>(new[] {
            new LinearMonomial<Flt64>(Flt64.One, x),
            new LinearMonomial<Flt64>(new Flt64(-1e16), y),
        }, new Flt64(1e16));
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        );
        Assert.True(created.IsOk, created.ToString());
        IfFunction<Flt64> function = created.Value;
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(input), [y] = Flt64.One };
        AssertIllegal(function.TryEvaluate(values));
        Assert.Null(function.Evaluate(values));
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input), [y] = Flt64.One }
            );
        Assert.True(built.IsFailed);
    }

    [Theory]
    [InlineData(-1, 0, -1e16)]
    [InlineData(0, 1, 1e16)]
    public void IntervalSubtractionRounding_ShouldNotInventAFalseBranch(double lower, double upper, double input) {
        var x = new RealVar("ifin-safe-subtraction");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1e16), new Flt64(1e16)).Value);
        Result<IfInFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfInFunction<Flt64>.Create(
            x: Polynomial(x),
            lower: new Flt64(lower),
            upper: new Flt64(upper),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(1e16)
        );
        Assert.True(created.IsOk, created.ToString());
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(input) };
        AssertIllegal(created.Value.Classify(values));
        AssertIllegal(created.Value.TryEvaluate(values));
    }

    [Fact]
    public void IntegerSolverConversion_ShouldNotCollapseTheBranch() {
        var x = new IntVar("if-safe-conversion");
        var converter = new IntegerConverter();
        var condition = new LinearPolynomial<Integer>(
            new[] { new LinearMonomial<Integer>(Integer.One, x) }, new Integer(-9007199254740992L)
        );
        Result<IfFunction<Integer>, ErrorCode, Error<ErrorCode>> created = IfFunction<Integer>.Create(
            condition: condition,
            converter: converter,
            strictBoundary: Integer.One,
            relation: Comparison.GT,
            bounds: new LinearPolynomialBounds<Integer>(new Integer(-2), new Integer(2))
        );
        Assert.True(created.IsOk, created.ToString());
        var values = new Dictionary<ISymbol, Integer> { [x] = new Integer(9007199254740993L) };
        AssertIllegal(created.Value.TryEvaluate(values));
    }

    [Fact]
    public void IntegerIntervalConversion_ShouldNotCollapseAnOutsideValueToAnEndpoint() {
        var x = new IntVar("ifin-safe-integer-conversion");
        var converter = new IntegerConverter();
        var boundary = new Integer(9007199254740992L);
        Result<IfInFunction<Integer>, ErrorCode, Error<ErrorCode>> created = IfInFunction<Integer>.Create(
            x: new LinearPolynomial<Integer>(new[] { new LinearMonomial<Integer>(Integer.One, x) }, Integer.Zero),
            lower: boundary,
            upper: boundary,
            converter: converter,
            strictBoundary: Integer.One,
            bounds: new LinearPolynomialBounds<Integer>(
                new Integer(9007199254740990L), new Integer(9007199254740994L)
            )
        );
        Assert.True(created.IsOk, created.ToString());
        var values = new Dictionary<ISymbol, Integer> { [x] = new Integer(9007199254740993L) };
        AssertIllegal(created.Value.Classify(values));
        AssertIllegal(created.Value.TryEvaluate(values));
    }

    [Fact]
    public void HarmlessIntegerConversion_ShouldKeepTheExistingClassification() {
        var x = new IntVar("if-harmless-integer-conversion");
        var converter = new IntegerConverter();
        Result<IfFunction<Integer>, ErrorCode, Error<ErrorCode>> created = IfFunction<Integer>.Create(
            condition: new LinearPolynomial<Integer>(
                new[] { new LinearMonomial<Integer>(Integer.One, x) }, new Integer(-9007199254740980L)
            ),
            converter: converter,
            strictBoundary: Integer.One,
            bounds: new LinearPolynomialBounds<Integer>(Integer.Zero, new Integer(20))
        );
        Assert.True(created.IsOk, created.ToString());
        var values = new Dictionary<ISymbol, Integer> { [x] = new Integer(9007199254740993L) };
        Assert.Equal(Integer.One, created.Value.TryEvaluate(values).Value);
    }

    [Fact]
    public void HarmlessRounding_ShouldKeepTheExistingClassification() {
        var x = new RealVar("ifin-harmless-rounding");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        Result<IfInFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfInFunction<Flt64>.Create(
            x: Polynomial(x, 1e16),
            lower: new Flt64(1e16 - 4),
            upper: new Flt64(1e16 + 4),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        );
        Assert.True(created.IsOk, created.ToString());
        var values = new Dictionary<ISymbol, Flt64> { [x] = Flt64.One };
        Assert.Equal(TruthValue.True, created.Value.Classify(values).Value);
        Assert.Equal(Flt64.One, created.Value.TryEvaluate(values).Value);
    }

    [Fact]
    public void DecimalSolverConversion_ShouldNotCollapseTheBranch() {
        var x = new RealVar("if-safe-decimal-conversion");
        var converter = new DecimalConverter();
        var condition = new LinearPolynomial<FltX>(
            new[] { new LinearMonomial<FltX>(FltX.One, x) }, new FltX(-9007199254740992m)
        );
        Result<IfFunction<FltX>, ErrorCode, Error<ErrorCode>> created = IfFunction<FltX>.Create(
            condition: condition,
            converter: converter,
            strictBoundary: FltX.One,
            relation: Comparison.GT,
            bounds: new LinearPolynomialBounds<FltX>(new FltX(-2), new FltX(2))
        );
        Assert.True(created.IsOk, created.ToString());
        var values = new Dictionary<ISymbol, FltX> { [x] = new FltX(9007199254740993m) };
        AssertIllegal(created.Value.TryEvaluate(values));
    }

    [Fact]
    public void RationalSolverConversion_ShouldNotMoveAnUndefinedValueToTheTrueBoundary() {
        var x = new RealVar("if-safe-rational-conversion");
        var converter = new RationalConverter();
        Result<IfFunction<RtnX>, ErrorCode, Error<ErrorCode>> created = IfFunction<RtnX>.Create(
            condition: new LinearPolynomial<RtnX>(new[] { new LinearMonomial<RtnX>(RtnX.One, x) }, RtnX.Zero),
            converter: converter,
            strictBoundary: new RtnX(0.1, 1),
            bounds: new LinearPolynomialBounds<RtnX>(RtnX.Zero, RtnX.One)
        );
        Assert.True(created.IsOk, created.ToString());
        var values = new Dictionary<ISymbol, RtnX> { [x] = new RtnX(1, 10) };
        AssertIllegal(created.Value.TryEvaluate(values));
    }

    [Fact]
    public void Float32GroupingMismatch_ShouldFail() {
        var x = new RealVar("if-safe-float32-x");
        var y = new BinVar("if-safe-float32-y");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var condition = new LinearPolynomial<Flt32>(new[] {
            new LinearMonomial<Flt32>(new Flt32(1), x),
            new LinearMonomial<Flt32>(new Flt32(-16777216), y),
        }, new Flt32(16777216));
        Result<IfFunction<Flt32>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt32>.Create(
            condition: condition,
            converter: new Float32Converter(),
            strictBoundary: new Flt32(0.5f)
        );
        Assert.True(created.IsOk, created.ToString());
        var values = new Dictionary<ISymbol, Flt32> { [x] = new Flt32(1), [y] = new Flt32(1) };
        AssertIllegal(created.Value.TryEvaluate(values));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(-1)]
    public void ConverterFailureDuringEvaluation_ShouldReturnIllegalArgument(double failingValue) {
        var x = new RealVar("if-safe-converter");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var converter = new SwitchableConverter();
        IfFunction<Flt64> condition = IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: converter,
            strictBoundary: new Flt64(0.5)
        ).Value;
        IfInFunction<Flt64> interval = IfInFunction<Flt64>.Create(
            x: Polynomial(x),
            lower: new Flt64(-1),
            upper: Flt64.One,
            converter: converter,
            strictBoundary: new Flt64(0.5)
        ).Value;
        converter.FailingValue = failingValue;
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.25) };
        if (failingValue != -1) {
            AssertIllegal(condition.TryEvaluate(values));
        }
        else {
            Assert.True(condition.TryEvaluate(values).IsOk);
        }
        AssertIllegal(interval.Classify(values));
        AssertIllegal(interval.TryEvaluate(values));
    }

    [Fact]
    public async Task RoundedNestedInput_ShouldFailAndRestoreTheExistingCache() {
        var x = new RealVar("if-nested-rounding-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var inner = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, new Flt64(1e16)
            ),
            name: "if-nested-rounding-inner"
        );
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, inner) }, new Flt64(-1e16)
            ),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: Comparison.GT
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = Flt64.One,
            [function.ConditionVar] = Flt64.One,
            [function.ResultVar] = Flt64.One,
            [function.FalseVar] = Flt64.Zero,
        };
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        assignment[function.ResultVar] = Flt64.Zero;
        assignment[function.FalseVar] = Flt64.One;
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        parent.Tokens.Cache(
            cacheKey: adapter,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
            );
        AssertIllegal(built);
        Assert.Equal(Flt64.One, parent.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    private static void AssertIllegal<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<T, ErrorCode, Error<ErrorCode>>>(result).Code);

    private static LinearPolynomial<Flt64> Polynomial(ISymbol x, double constant = 0) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, new Flt64(constant)
    );

    private sealed class IntegerConverter : IIntoValue<Integer> {
        public Integer Zero => Integer.Zero;
        public Integer One => Integer.One;
        public Integer IntoValue(Flt64 value) => value.ToInt64();
        public Flt64 FromValue(Integer value) => value.ToFlt64();
    }

    private sealed class DecimalConverter : IIntoValue<FltX> {
        public FltX Zero => FltX.Zero;
        public FltX One => FltX.One;
        public FltX IntoValue(Flt64 value) => value.ToFltX();
        public Flt64 FromValue(FltX value) => value.ToFlt64();
    }

    private sealed class RationalConverter : IIntoValue<RtnX> {
        public RtnX Zero => RtnX.Zero;
        public RtnX One => RtnX.One;
        public RtnX IntoValue(Flt64 value) => new(value.ToDouble(), 1);
        public Flt64 FromValue(RtnX value) => value.ToFlt64();
    }

    private sealed class Float32Converter : IIntoValue<Flt32> {
        public Flt32 Zero => new(0);
        public Flt32 One => new(1);
        public Flt32 IntoValue(Flt64 value) => value.ToFlt32();
        public Flt64 FromValue(Flt32 value) => value.ToFlt64();
    }

    private sealed class SwitchableConverter : IIntoValue<Flt64> {
        public double? FailingValue { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => value.ToDouble() == FailingValue
            ? throw new ArgumentException("conversion failed")
            : value;
    }
}
