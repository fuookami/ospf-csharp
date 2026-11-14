#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>推导边界的保守性 / Conservativeness of inferred bounds.</summary>
public class FiniteBoundsSafetyTest {
    [Fact]
    public async Task DecimalDefaultVariable_ShouldInferBounds() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var x = new RealVar("decimal-default-range");
        var input = new LinearPolynomial<FltX>(
            new[] { new LinearMonomial<FltX>(FltX.One, x) },
            FltX.Zero
        );
        LinearPolynomialBounds<FltX>? bounds = BigMValues.FiniteBounds(input, converter);
        bounds.Should().NotBeNull();
        ExactBoundValue.TryRead(
            value: bounds!.UpperBound,
            converter: converter,
            exact: out ExactBoundValue upper
        ).Should().BeTrue();
        upper.TryRoundSolverUpper(converter, out FltX chosenUpper).Should().BeTrue(because: $"upper={bounds.UpperBound}; rounded={chosenUpper}");
        ExactBoundValue.TryRead(
            value: bounds.LowerBound,
            converter: converter,
            exact: out ExactBoundValue lower
        ).Should().BeTrue();
        lower.Negate().TryRoundSolverUpper(converter, out FltX chosenLower).Should().BeTrue(because: $"lower={bounds.LowerBound}; rounded={chosenLower}");
        var function = new AbsFunction<FltX>(input, converter);
        function.PositiveBigM.Gr(FltX.Zero).Should().BeTrue(because: $"positive M is {function.PositiveBigM}");
        function.NegativeBigM.Gr(FltX.Zero).Should().BeTrue(because: $"negative M is {function.NegativeBigM}");
        using var parent = new LinearMetaModel<FltX>(converter: converter);
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<FltX>.InvokeAsync(parent);
        result.IsOk.Should().BeTrue(because: result.ToString());
        result.Value.Dispose();
    }

    [Fact]
    public void DecimalConstant_ShouldBeEnclosedWithoutLosingPrecision() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var value = new FltX(9007199254740993.125m);
        LinearPolynomialBounds<FltX>? bounds = BigMValues.FiniteBounds(Constant(value), converter);

        bounds.Should().NotBeNull();
        bounds!.LowerBound.Leq(value).Should().BeTrue();
        bounds.UpperBound.Geq(value).Should().BeTrue();
    }

    [Fact]
    public void IntegerConstant_ShouldBeEnclosedAboveDoublePrecision() {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var value = new Integer(9007199254740993L);
        LinearPolynomialBounds<Integer>? bounds = BigMValues.FiniteBounds(Constant(value), converter);

        bounds.Should().NotBeNull();
        bounds!.LowerBound.Leq(value).Should().BeTrue();
        bounds.UpperBound.Geq(value).Should().BeTrue();
    }

    [Fact]
    public void FloatingProduct_ShouldBeRoundedOutward() {
        var x = new RealVar("finite-bounds-float");
        x.Range.Eq(new Flt64(0.2));
        var input = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(0.1), x) },
            Flt64.Zero
        );
        LinearPolynomialBounds<Flt64>? bounds = BigMValues.FiniteBounds(input, IIntoValue<Flt64>.Identity);
        double nearest = 0.1 * 0.2;

        bounds.Should().NotBeNull();
        bounds!.LowerBound.ToDouble().Should().BeLessThan(nearest);
        bounds.UpperBound.ToDouble().Should().BeGreaterThanOrEqualTo(nearest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeIntegerVariableBounds_ShouldEncloseValuesAboveDoublePrecision(bool negative) {
        var x = new IntVar("exact-native-integer");
        var value = new Integer(9007199254740993L);
        x.Range.SetRange(ValueRange<Integer>.Of(value, value).Value);
        var input = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(negative ? -1 : 1), x) },
            Flt64.Zero
        );

        LinearPolynomialBounds<Flt64>? bounds = BigMValues.FiniteBounds(input, IIntoValue<Flt64>.Identity);

        bounds.Should().NotBeNull();
        bounds!.LowerBound.ToDouble().Should().Be(negative ? -9007199254740994d : 9007199254740992d);
        bounds.UpperBound.ToDouble().Should().Be(negative ? -9007199254740992d : 9007199254740994d);
    }

    [Fact]
    public void FloatingUnderflow_ShouldEncloseZeroAndSmallestSubnormal() {
        var x = new RealVar("subnormal-bound");
        x.Range.Eq(new Flt64(0.5));
        var input = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(double.Epsilon), x) },
            Flt64.Zero
        );

        LinearPolynomialBounds<Flt64>? bounds = BigMValues.FiniteBounds(input, IIntoValue<Flt64>.Identity);

        bounds.Should().NotBeNull();
        bounds!.LowerBound.ToDouble().Should().Be(0.0);
        bounds.UpperBound.ToDouble().Should().Be(double.Epsilon);
    }

    [Fact]
    public void MultipleFloatingTerms_ShouldIncludeSequentialRounding() {
        var x = new RealVar("rounding-sum");
        x.Range.Eq(Flt64.One);
        double halfUlp = global::System.Math.ScaleB(1.0, -53);
        var input = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(halfUlp), x),
                new LinearMonomial<Flt64>(new Flt64(halfUlp), x),
            },
            Flt64.One
        );

        LinearPolynomialBounds<Flt64>? bounds = BigMValues.FiniteBounds(input, IIntoValue<Flt64>.Identity);

        bounds.Should().NotBeNull();
        bounds!.LowerBound.ToDouble().Should().BeLessThanOrEqualTo(1.0);
        bounds.UpperBound.ToDouble().Should().BeGreaterThanOrEqualTo(global::System.Math.BitIncrement(1.0));
    }

    [Fact]
    public void DecimalCoefficient_ShouldIncludeItsSolverEncoding() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var x = new RealVar("encoded-decimal");
        x.Range.Eq(Flt64.One);
        var input = new LinearPolynomial<FltX>(
            new[] { new LinearMonomial<FltX>(new FltX(0.1m), x) },
            FltX.Zero
        );

        LinearPolynomialBounds<FltX>? bounds = BigMValues.FiniteBounds(input, converter);

        bounds.Should().NotBeNull();
        bounds!.LowerBound.ToDecimal().Should().BeLessThanOrEqualTo(0.1m);
        bounds.UpperBound.ToDecimal().Should().BeGreaterThanOrEqualTo(0.1000000000000000055511151231m);
    }

    [Fact]
    public void Flt32Product_ShouldEncloseRoundedProduct() {
        var converter = new TestNumberConverter<Flt32>(value => value.ToFlt32());
        var x = new RealVar("float32-bound");
        x.Range.Eq(new Flt64(0.2));
        var input = new LinearPolynomial<Flt32>(
            new[] { new LinearMonomial<Flt32>(new Flt32(0.1f), x) },
            new Flt32(0)
        );

        LinearPolynomialBounds<Flt32>? bounds = BigMValues.FiniteBounds(input, converter);

        bounds.Should().NotBeNull();
        double product = (double)0.1f * 0.2f;
        bounds!.LowerBound.ToFlt64().ToDouble().Should().BeLessThanOrEqualTo(product);
        bounds.UpperBound.ToFlt64().ToDouble().Should().BeGreaterThanOrEqualTo(product);
    }

    [Fact]
    public void RationalConstant_ShouldEncloseNativeAndEncodedValues() {
        var converter = new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1));
        var value = new RtnX(1, 3);

        LinearPolynomialBounds<RtnX>? bounds = BigMValues.FiniteBounds(Constant(value), converter);

        bounds.Should().NotBeNull();
        ExactBoundValue.TryRead(value, out ExactBoundValue exact).Should().BeTrue();
        ExactBoundValue.TryRead(bounds!.LowerBound, out ExactBoundValue lower).Should().BeTrue();
        ExactBoundValue.TryRead(bounds.UpperBound, out ExactBoundValue upper).Should().BeTrue();
        lower.CompareTo(exact).Should().BeLessThanOrEqualTo(0);
        upper.CompareTo(exact).Should().BeGreaterThanOrEqualTo(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactFractions_ShouldRoundFloatingAndIntegerBoundsOutward(bool negative) {
        var tenth = new ExactBoundValue(new BigInteger(negative ? -1 : 1), new BigInteger(10));
        tenth.TryRound(
            converter: IIntoValue<Flt64>.Identity,
            upper: false,
            result: out Flt64 lower
        ).Should().BeTrue();
        tenth.TryRound(
            converter: IIntoValue<Flt64>.Identity,
            upper: true,
            result: out Flt64 upper
        ).Should().BeTrue();
        lower.ToDouble().Should().Be(negative ? -0.1 : global::System.Math.BitDecrement(0.1));
        upper.ToDouble().Should().Be(negative ? global::System.Math.BitIncrement(-0.1) : 0.1);

        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        tenth.TryRound(
            converter: converter,
            upper: false,
            result: out Integer integerLower
        ).Should().BeTrue();
        tenth.TryRound(
            converter: converter,
            upper: true,
            result: out Integer integerUpper
        ).Should().BeTrue();
        integerLower.ToLong().Should().Be(negative ? -1 : 0);
        integerUpper.ToLong().Should().Be(negative ? 0 : 1);
    }

    [Fact]
    public void ExactFraction_ShouldRoundDecimalBoundsOutward() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var third = new ExactBoundValue(BigInteger.One, new BigInteger(3));
        third.TryRound(
            converter: converter,
            upper: false,
            result: out FltX lower
        ).Should().BeTrue();
        third.TryRound(
            converter: converter,
            upper: true,
            result: out FltX upper
        ).Should().BeTrue();
        lower.ToDecimal().Should().Be(0.3333333333333333333333333333m);
        upper.ToDecimal().Should().Be(0.3333333333333333333333333334m);
    }

    [Fact]
    public void OverflowOrNonFaithfulEncoding_ShouldNotInferBounds() {
        var integerConverter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var x = new IntVar("overflow-bounds");
        x.Range.SetRange(ValueRange<Integer>.Of(new Integer(2), new Integer(2)).Value);
        var input = new LinearPolynomial<Integer>(
            new[] { new LinearMonomial<Integer>(new Integer(long.MaxValue), x) },
            Integer.Zero
        );

        BigMValues.FiniteBounds(input, integerConverter).Should().BeNull();
        BigMValues.FiniteBounds(Constant(Flt64.One), new NonFaithfulConverter()).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtremumBigMDistance_ShouldRoundUp(bool minimum) {
        LinearPolynomial<Flt64>[] inputs = minimum
            ? new[] { Constant(new Flt64(-1)), Constant(new Flt64(0.2)) }
            : new[] { Constant(new Flt64(1)), Constant(new Flt64(-0.2)) };

        IReadOnlyList<Flt64>? bigMs = ExtremumValues.BigMs(
            polynomials: inputs,
            explicitBigM: null,
            converter: IIntoValue<Flt64>.Identity,
            minimum: minimum
        );

        bigMs.Should().NotBeNull();
        bigMs![1].ToDouble().Should().BeGreaterThanOrEqualTo(global::System.Math.BitIncrement(1.2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AutomaticBigMWithoutFiniteBounds_ShouldFailBeforeWritingRows(int kind) {
        var x = new RealVar("unbounded-automatic-m");
        x.Range.SetRange(ValueRange<Flt64>.Full());
        var input = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
            Flt64.Zero
        );
        IMathFunctionSymbol<Flt64> function = kind switch {
            0 => new AbsFunction<Flt64>(input, IIntoValue<Flt64>.Identity),
            1 => new MaxFunction<Flt64>(new[] { input }, IIntoValue<Flt64>.Identity),
            _ => new BinaryzationFunction(input),
        };
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = built.Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbsoluteIntegerMinimum_ShouldFailWithoutOverflowOrRows(bool explicitBigM) {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var function = new AbsFunction<Integer>(
            polynomial: Constant(new Integer(long.MinValue)),
            converter: converter,
            bigM: explicitBigM ? new Integer(long.MaxValue) : null
        );
        using var parent = new LinearMetaModel<Integer>(converter: converter);
        Result<LinearMechanismModel<Integer>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Integer>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Integer> model = built.Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Integer>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Extrema_DefaultBigMAndResultDomain_ShouldNotExcludeDecimalConstant(bool minimum) {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var magnitude = new FltX(9007199254740993.125m);
        FltX expected = minimum ? magnitude.Negate() : magnitude;
        ExtremumFunctionBase<FltX> function = minimum
            ? new MinFunction<FltX>(new[] { Constant(expected), Constant(FltX.Zero) }, converter)
            : new MaxFunction<FltX>(new[] { Constant(expected), Constant(FltX.Zero) }, converter);
        double rounded = expected.ToFlt64().ToDouble();
        function.ResultVar.LowerBound.Should().NotBeNull();
        function.ResultVar.UpperBound.Should().NotBeNull();
        function.ResultVar.LowerBound!.Value.ToFlt64().ToDouble()
            .Should().BeLessThanOrEqualTo(minimum ? rounded : global::System.Math.BitDecrement(rounded));
        function.ResultVar.UpperBound!.Value.ToFlt64().ToDouble()
            .Should().BeGreaterThanOrEqualTo(minimum ? global::System.Math.BitIncrement(rounded) : rounded);

        using var parent = new LinearMetaModel<FltX>(converter: converter);
        parent.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<FltX>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<FltX> model = built.Value;
        var values = new Dictionary<IVariableItem, FltX> {
            [function.ResultVar] = expected,
            [function.SelectorVars[0]] = converter.One,
            [function.SelectorVars[1]] = converter.Zero,
        };
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        ((IMechanismModel<FltX>)model).Constraints.Should().OnlyContain(row => row.IsTrue(solution) == true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Absolute_DefaultBigMAndHelperDomains_ShouldNotExcludeDecimalConstant(bool negative) {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var magnitude = new FltX(9007199254740993.125m);
        var function = new AbsFunction<FltX>(
            polynomial: Constant(negative ? magnitude.Negate() : magnitude),
            converter: converter
        );
        FltX chosenM = negative ? function.NegativeBigM : function.PositiveBigM;
        chosenM.Geq(magnitude).Should().BeTrue();
        double rounded = magnitude.ToFlt64().ToDouble();
        function.ResultVar.UpperBound!.Value.ToFlt64().ToDouble().Should().BeGreaterThanOrEqualTo(rounded);
        (negative ? function.NegativeVar : function.PositiveVar).UpperBound!.Value.ToFlt64().ToDouble()
            .Should().BeGreaterThanOrEqualTo(rounded);
        using var parent = new LinearMetaModel<FltX>(converter: converter);
        parent.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<FltX>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<FltX> model = built.Value;
        var values = new Dictionary<IVariableItem, FltX> {
            [function.ResultVar] = magnitude,
            [function.PositiveVar] = negative ? converter.Zero : magnitude,
            [function.NegativeVar] = negative ? magnitude : converter.Zero,
            [function.SignVar] = negative ? converter.Zero : converter.One,
        };
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        ((IMechanismModel<FltX>)model).Constraints.Should().OnlyContain(row => row.IsTrue(solution) == true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WidenedInputDomain_ShouldInvalidateCapturedBounds(bool absolute) {
        var x = new RealVar("widened-bounds-x");
        x.Range.Geq(new Flt64(-1));
        x.Range.Leq(new Flt64(1));
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
            Flt64.Zero
        );
        IMathFunctionSymbol<Flt64> function = absolute
            ? new AbsFunction<Flt64>(polynomial, IIntoValue<Flt64>.Identity)
            : new MaxFunction<Flt64>(new[] { polynomial }, IIntoValue<Flt64>.Identity);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsFailed.Should().BeTrue();
    }

    private static LinearPolynomial<V> Constant<V>(V value)
        where V : struct, Fuookami.Ospf.Math.Algebra.Concept.IRealNumber<V>, Fuookami.Ospf.Math.Algebra.Concept.INumberField<V> =>
        new(Array.Empty<LinearMonomial<V>>(), value);

    private sealed class NonFaithfulConverter : IIntoValue<Flt64> {
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => new(value.ToDouble() * 2);
    }
}
