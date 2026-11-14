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
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using NInt16 = Fuookami.Ospf.Math.Algebra.Number.Int16;
using NInt32 = Fuookami.Ospf.Math.Algebra.Number.Int32;
using NUInt16 = Fuookami.Ospf.Math.Algebra.Number.UInt16;
using NUInt32 = Fuookami.Ospf.Math.Algebra.Number.UInt32;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>有界整数乘积契约 / Bounded integer-product contracts.</summary>
public class IntegerProductFunctionTest {
    [Fact]
    public async Task RowsAndMatrix_ShouldEncodeEverySmallIntegerAndRejectUnusedBits() {
        IntVar integer = IntegerVariable(-2, 2);
        RealVar input = RealVariable(-3, 4);
        IntegerProductFunction<Flt64> function = Create(integer, input);
        Assert.Equal(3, function.BitVars.Count);
        Assert.Equal(8, function.HelperVariables.Count);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(integer).IsOk);
        Assert.True(parent.Add(input).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Assert.Equal(18, ((IMechanismModel<Flt64>)model).Constraints.Count);

        foreach (double value in new[] { -3.0, -1.0, 0.0, 2.0, 4.0 }) {
            for (int offset = 0; offset < 8; ++offset) {
                Dictionary<IVariableItem, Flt64> values = Values(
                    function: function,
                    integer: integer,
                    input: input,
                    integerValue: offset - 2,
                    inputValue: value,
                    bits: offset
                );
                bool expected = offset <= 4;
                Assert.Equal(expected, SatisfiedAmountResidualTest.ExactFeasible(model, values));
                Assert.Equal(expected, SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
                if (expected) {
                    Assert.Equal(new Flt64((offset - 2) * value), function.Evaluate(new Dictionary<ISymbol, Flt64> {
                        [integer] = new Flt64(offset - 2),
                        [input] = new Flt64(value),
                    }));
                    values[function.ResultVar] = new Flt64((offset - 2) * value + 1);
                    Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, values));
                    values[function.ResultVar] = new Flt64((offset - 2) * value);
                    values[function.MaskedVars[0]] = new Flt64(((offset & 1) * value) + 1);
                    Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, values));
                }
            }
        }
    }

    [Fact]
    public async Task MaximumWidth_ShouldExplicitlyExcludeLowBitsWhenHighBitIsSet() {
        const long width = 9_007_199_254_740_992L;
        Assert.Equal((double)width, (double)width + 1);
        IntVar integer = IntegerVariable(0, width);
        RealVar input = RealVariable(-1, 1);
        IntegerProductFunction<Flt64> function = Create(integer, input);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(integer).IsOk);
        Assert.True(parent.Add(input).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(54, function.BitVars.Count);
        Assert.Contains(((IMechanismModel<Flt64>)model).Constraints,
            row => row.Name == "integer_product_integer_product_high_bit");
        Dictionary<IVariableItem, Flt64> values = Values(
            function: function,
            integer: integer,
            input: input,
            integerValue: width,
            inputValue: 0,
            bits: 1L << 53
        );
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, values));
        values[function.BitVars[0]] = Flt64.One;
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, values));
    }

    [Fact]
    public async Task DuplicateCoefficientsLostDuringFlattening_ShouldNotWriteRows() {
        IntVar integer = IntegerVariable(0, 1);
        RealVar input = RealVariable(-1, 1);
        var polynomial = new LinearPolynomial<Flt64>(
            new[] {
                new LinearMonomial<Flt64>(new Flt64(1e16), input),
                new LinearMonomial<Flt64>(Flt64.One, input),
            },
            Flt64.Zero
        );
        IntegerProductFunction<Flt64> function = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: polynomial,
            converter: IIntoValue<Flt64>.Identity,
            inputBounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1e16), new Flt64(1e16))
        ).Value;
        Assert.NotNull(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(integer).IsOk);
        Assert.True(parent.Add(input).IsOk);
        foreach (IVariableItem helper in function.HelperVariables) {
            Assert.True(parent.Add(helper).IsOk);
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Empty(((IMechanismModel<Flt64>)model).Constraints);
    }

    [Fact]
    public void RationalCoefficientUnderflowAtSolverBoundary_ShouldFailCreation() {
        IntVar integer = IntegerVariable(0, 1);
        RealVar input = RealVariable(-1, 1);
        var polynomial = new LinearPolynomial<RtnX>(
            new[] { new LinearMonomial<RtnX>(new RtnX(1e-200, 1e200), input) },
            RtnX.Zero
        );
        Assert.True(IntegerProductFunction<RtnX>.Create(
            integer: integer,
            input: polynomial,
            converter: new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)),
            inputBounds: new LinearPolynomialBounds<RtnX>(new RtnX(-1, 1), new RtnX(1, 1))
        ).IsFailed);
    }

    [Fact]
    public void ConverterDroppingNonzeroCoefficient_ShouldFailCreation() {
        IntVar integer = IntegerVariable(0, 1);
        RealVar input = RealVariable(-1, 1);
        var polynomial = new LinearPolynomial<RtnX>(
            new[] { new LinearMonomial<RtnX>(new RtnX(1, 4), input) },
            RtnX.Zero
        );
        Assert.True(IntegerProductFunction<RtnX>.Create(
            integer: integer,
            input: polynomial,
            converter: new DroppingConverter(),
            inputBounds: new LinearPolynomialBounds<RtnX>(new RtnX(-1, 1), new RtnX(1, 1))
        ).IsFailed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConverterChangingCapturedRangesDuringCreation_ShouldFail(bool changeInteger) {
        IntVar integer = IntegerVariable(-2, 2);
        RealVar input = RealVariable(-3, 4);
        var converter = new MutatingConverter(
            trigger: changeInteger ? 0 : -3,
            mutation: () => {
                if (changeInteger) {
                    integer.Range.SetRange(ValueRange<Integer>.Of(new Integer(-3), new Integer(2)).Value);
                }
                else {
                    input.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-10), new Flt64(4)).Value);
                }
            }
        );
        Result<IntegerProductFunction<Flt64>, ErrorCode, Error<ErrorCode>> result = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, Flt64.Zero),
            converter: converter
        );
        Assert.True(converter.Mutated);
        Assert.True(result.IsFailed);
    }

    [Fact]
    public async Task CapturedBounds_ShouldInvalidateBuiltMatrix() {
        IntVar integer = IntegerVariable(-2, 2);
        RealVar input = RealVariable(-3, 4);
        IntegerProductFunction<Flt64> function = Create(integer, input);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(integer).IsOk);
        Assert.True(parent.Add(input).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(model.ToLinearTriadModel().IsOk);
        input.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        Assert.True(model.ToLinearTriadModel().IsFailed);
    }

    [Fact]
    public void TypedEvaluation_ShouldRespectBoundsAndMissingInputs() {
        VerifyTypedEvaluation(IIntoValue<Flt64>.Identity);
        VerifyTypedEvaluation(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        VerifyTypedEvaluation(new TestNumberConverter<Integer>(value => value.ToInt64()));
        VerifyTypedEvaluation(new TestNumberConverter<FltX>(value => value.ToFltX()));
        VerifyTypedEvaluation(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    [Fact]
    public async Task TypedRows_ShouldPreserveProduct() {
        await VerifyTypedRows(IIntoValue<Flt64>.Identity);
        await VerifyTypedRows(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyTypedRows(new TestNumberConverter<Integer>(value => value.ToInt64()));
        await VerifyTypedRows(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await VerifyTypedRows(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    [Fact]
    public void OtherNativeIntegerRanges_ShouldBeReadWithoutDoubleRounding() {
        VerifyIntegerRange(new NInt16(-1), new NInt16(2));
        VerifyIntegerRange(new NInt32(-1), new NInt32(2));
        VerifyIntegerRange(new NUInt16(0), new NUInt16(3));
        VerifyIntegerRange(new NUInt32(0), new NUInt32(3));
        VerifyIntegerRange(new IntX(-1), new IntX(2));
        VerifyIntegerRange(new UIntX(0), new UIntX(3));
    }

    [Fact]
    public void OpenIntegerBounds_ShouldExcludeEndpoints() {
        IntVar integer = IntegerVariable(-2, 2);
        integer.Range.SetRange(ValueRange<Integer>.Of(
            lb: new Integer(-2),
            ub: new Integer(2),
            lbInterval: new Interval.Open(),
            ubInterval: new Interval.Open()
        ).Value);
        RealVar input = RealVariable(-1, 1);
        IntegerProductFunction<Flt64> function = Create(integer, input);
        Assert.Equal(2, function.BitVars.Count);
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64> {
            [integer] = new Flt64(-2), [input] = Flt64.One,
        }).IsFailed);
        Assert.Equal(new Flt64(-1), function.Evaluate(new Dictionary<ISymbol, Flt64> {
            [integer] = new Flt64(-1), [input] = Flt64.One,
        }));
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64> {
            [integer] = new Flt64(2), [input] = Flt64.One,
        }).IsFailed);
    }

    [Fact]
    public void IntegerDomainMustBeRepresentableByTheValueType() {
        var singleConverter = new TestNumberConverter<Flt32>(value => value.ToFlt32());
        var singleInput = new LinearPolynomial<Flt32>(Array.Empty<LinearMonomial<Flt32>>(), singleConverter.Zero);
        Assert.True(IntegerProductFunction<Flt32>.Create(
            integer: IntegerVariable(16_777_216, 16_777_217),
            input: singleInput,
            converter: singleConverter
        ).IsFailed);
        Assert.True(IntegerProductFunction<Flt32>.Create(
            integer: IntegerVariable(16_777_216, 16_777_218),
            input: singleInput,
            converter: singleConverter
        ).IsFailed);
        Assert.True(IntegerProductFunction<Flt32>.Create(
            integer: IntegerVariable(16_777_218, 16_777_218),
            input: singleInput,
            converter: singleConverter
        ).IsOk);
        Assert.True(IntegerProductFunction<NInt16>.Create(
            integer: IntegerVariable(30_000, 40_000),
            input: new LinearPolynomial<NInt16>(Array.Empty<LinearMonomial<NInt16>>(), NInt16.Zero),
            converter: new TestNumberConverter<NInt16>(value => value.ToInt16())
        ).IsFailed);
    }

    [Fact]
    public async Task FixedIntegerAndExplicitBounds_ShouldConstrainInputAndResult() {
        IntVar integer = IntegerVariable(-2, -2);
        RealVar input = RealVariable(-3, 4);
        Result<IntegerProductFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            inputBounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1), new Flt64(2))
        );
        Assert.True(created.IsOk);
        IntegerProductFunction<Flt64> function = created.Value;
        Assert.Empty(function.BitVars);
        Assert.Equal(2, function.HelperVariables.Count);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(integer).IsOk);
        Assert.True(parent.Add(input).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.Equal(6, ((IMechanismModel<Flt64>)model).Constraints.Count);
        Dictionary<IVariableItem, Flt64> values = Values(
            function: function,
            integer: integer,
            input: input,
            integerValue: -2,
            inputValue: 2,
            bits: 0
        );
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, values));
        values[input] = new Flt64(3);
        values[function.InputVar] = new Flt64(3);
        values[function.ResultVar] = new Flt64(-6);
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, values));
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64> {
            [integer] = new Flt64(-2), [input] = new Flt64(3),
        }).IsFailed);
        input.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-10), new Flt64(10)).Value);
        Assert.True(model.ToLinearTriadModel().IsOk);
    }

    [Fact]
    public async Task ChangedNestedExpression_ShouldFailBeforeWritingOrPublishingHelpers() {
        IntVar integer = IntegerVariable(-2, 2);
        RealVar input = RealVariable(-3, 4);
        var nested = LinearExpressionSymbol.FromVariable(input, "integer-product-nested");
        IntegerProductFunction<Flt64> function = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, nested) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(integer).IsOk);
        Assert.True(parent.Add(input).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        int tokenCount = parent.Tokens.Tokens.Count;
        int callbackCount = parent.Tokens.Find(input)!.RefreshCallbacks.Count;
        nested.AsMutable().SetConstant(Flt64.One);
        Assert.True((await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed);
        Assert.Equal(tokenCount, parent.Tokens.Tokens.Count);
        Assert.Equal(callbackCount, parent.Tokens.Find(input)!.RefreshCallbacks.Count);
    }

    [Fact]
    public async Task NestedFunctionAndFixedValues_ShouldUseRegisteredDependency() {
        IntVar integer = IntegerVariable(-2, 2);
        RealVar input = RealVariable(-3, 4);
        var absolute = new AbsFunction<Flt64>(
            polynomial: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity
        );
        var nested = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        Result<IntegerProductFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, nested) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.True(created.IsOk, created.ToString());
        IntegerProductFunction<Flt64> function = created.Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(integer).IsOk);
        Assert.True(parent.Add(input).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Assert.True((await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed);
        Assert.True(parent.Add(nested).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [integer] = new Flt64(-2), [input] = new Flt64(-3) }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Dictionary<IVariableItem, Flt64> values = Values(
            function: function,
            integer: integer,
            input: input,
            integerValue: -2,
            inputValue: 3,
            bits: 0
        );
        values[input] = new Flt64(-3);
        values[absolute.ResultVar] = new Flt64(3);
        values[absolute.PositiveVar] = Flt64.Zero;
        values[absolute.NegativeVar] = new Flt64(3);
        values[absolute.SignVar] = Flt64.Zero;
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, values));
    }

    [Fact]
    public async Task IntegerBoundsChangedAfterCreation_ShouldFailAndInvalidateMatrix() {
        IntVar integer = IntegerVariable(-2, 2);
        RealVar input = RealVariable(-3, 4);
        IntegerProductFunction<Flt64> function = Create(integer, input);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(integer).IsOk);
        Assert.True(parent.Add(input).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        integer.Range.SetRange(ValueRange<Integer>.Of(new Integer(-3), new Integer(2)).Value);
        Assert.True(model.ToLinearTriadModel().IsFailed);
        Assert.True((await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed);
    }

    [Fact]
    public async Task MissingHelperOrFailedConversion_ShouldNotWriteRows() {
        IntVar integer = IntegerVariable(-2, 2);
        RealVar input = RealVariable(-3, 4);
        var converter = new TestNumberConverter<Flt64>(value => value);
        IntegerProductFunction<Flt64> function = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, Flt64.Zero),
            converter: converter
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(integer).IsOk);
        Assert.True(parent.Add(input).IsOk);
        foreach (IVariableItem helper in function.HelperVariables.SkipLast(1)) {
            Assert.True(parent.Add(helper).IsOk);
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Empty(((IMechanismModel<Flt64>)model).Constraints);
        converter.RejectEncoding = true;
        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Empty(((IMechanismModel<Flt64>)model).Constraints);
    }

    [Fact]
    public void InvalidArgumentsAndDomains_ShouldFail() {
        IntVar integer = IntegerVariable(-2, 2);
        RealVar input = RealVariable(-3, 4);
        var polynomial = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, Flt64.Zero);
        Assert.True(IntegerProductFunction<Flt64>.Create(
            integer: null,
            input: polynomial,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(IntegerProductFunction<Flt64>.Create(
            integer: input,
            input: polynomial,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: null,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: polynomial,
            converter: null
        ).IsFailed);
        Assert.True(IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: polynomial,
            converter: IIntoValue<Flt64>.Identity,
            inputBounds: new LinearPolynomialBounds<Flt64>(Flt64.One, Flt64.Zero)
        ).IsFailed);
        Assert.True(IntegerProductFunction<Flt64>.Create(
            integer: IntegerVariable(0, 9_007_199_254_740_993L),
            input: polynomial,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        input.Range.SetRange(ValueRange<Flt64>.Full());
        Assert.True(IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: polynomial,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
    }

    private static void VerifyTypedEvaluation<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        IntVar integer = IntegerVariable(-2, 2);
        RealVar input = RealVariable(-3, 4);
        var polynomial = new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, input) }, converter.Zero);
        Result<IntegerProductFunction<V>, ErrorCode, Error<ErrorCode>> created = IntegerProductFunction<V>.Create(
            integer: integer,
            input: polynomial,
            converter: converter
        );
        Assert.True(created.IsOk, created.ToString());
        IntegerProductFunction<V> function = created.Value;
        V Number(double value) => converter.IntoValue(new Flt64(value));
        var values = new Dictionary<ISymbol, V> { [integer] = Number(-2), [input] = Number(3) };
        Assert.True(function.TryEvaluate(values).Value!.Value.Eq(Number(-6)));
        values.Remove(input);
        Assert.Null(function.TryEvaluate(values).Value);
        values[input] = Number(5);
        Assert.True(function.TryEvaluate(values).IsFailed);
        values[input] = Number(3);
        values[integer] = Number(3);
        Assert.True(function.TryEvaluate(values).IsFailed);
    }

    private static async Task VerifyTypedRows<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        IntVar integer = IntegerVariable(-2, 2);
        RealVar input = RealVariable(-3, 4);
        IntegerProductFunction<V> function = IntegerProductFunction<V>.Create(
            integer: integer,
            input: new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, input) }, converter.Zero),
            converter: converter
        ).Value;
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(integer).IsOk);
        Assert.True(parent.Add(input).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        V Number(double value) => converter.IntoValue(new Flt64(value));
        var values = new Dictionary<IVariableItem, V> {
            [integer] = Number(1), [input] = Number(3), [function.InputVar] = Number(3), [function.ResultVar] = Number(3),
        };
        for (int index = 0; index < function.BitVars.Count; ++index) {
            int bit = (3 >> index) & 1;
            values[function.BitVars[index]] = Number(bit);
            values[function.MaskedVars[index]] = Number(bit * 3);
        }
        V[] solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
        Assert.All(((IMechanismModel<V>)model).Constraints, row => Assert.True(row.IsTrue(solution)));
        values[function.ResultVar] = Number(4);
        solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
        Assert.Contains(((IMechanismModel<V>)model).Constraints, row => row.IsTrue(solution) == false);
    }

    private static void VerifyIntegerRange<T>(T lower, T upper)
        where T : struct, IRealNumber<T>, INumberField<T> {
        var variable = new TestIntegerVariable<T>();
        variable.Range.SetRange(ValueRange<T>.Of(lower, upper).Value);
        IntegerProductFunction<Flt64> function = IntegerProductFunction<Flt64>.Create(
            integer: variable,
            input: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        Assert.NotNull(function);
        Assert.Equal(2, function.BitVars.Count);
    }

    private sealed class TestIntegerType<T> : VariableType<T>, IVariableTypeKind
        where T : struct, IRealNumber<T>, INumberField<T> {
        public TestIntegerType() : base(NumericConstantsRegistry.For<T>()) { }
        public override string Name => "TestInteger";
        public override string ShortName => "testint";
        bool IVariableTypeKind.IsIntegerType => true;
    }

    private sealed class TestIntegerVariable<T> : IndependentVariableItem<T, TestIntegerType<T>>
        where T : struct, IRealNumber<T>, INumberField<T> {
        public TestIntegerVariable() : base(
            type: new TestIntegerType<T>(),
            name: "custom-integer",
            constants: NumericConstantsRegistry.For<T>()
        ) { }
    }

    private sealed class DroppingConverter : IIntoValue<RtnX> {
        public RtnX Zero => RtnX.Zero;
        public RtnX One => RtnX.One;
        public RtnX IntoValue(Flt64 value) => new(value.ToDouble(), 1);
        public Flt64 FromValue(RtnX value) => value.ToFlt64().ToDouble() == 0.25 ? Flt64.Zero : value.ToFlt64();
    }

    private sealed class MutatingConverter(double trigger, Action mutation) : IIntoValue<Flt64> {
        public bool Mutated { get; private set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) {
            if (!Mutated && value.ToDouble() == trigger) {
                Mutated = true;
                mutation();
            }
            return value;
        }
    }

    private static IntVar IntegerVariable(long lower, long upper) {
        var variable = new IntVar("integer-product-integer");
        variable.Range.SetRange(ValueRange<Integer>.Of(new Integer(lower), new Integer(upper)).Value);
        return variable;
    }

    private static RealVar RealVariable(double lower, double upper) {
        var variable = new RealVar("integer-product-input");
        variable.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(lower), new Flt64(upper)).Value);
        return variable;
    }

    private static IntegerProductFunction<Flt64> Create(IntVar integer, RealVar input) {
        Result<IntegerProductFunction<Flt64>, ErrorCode, Error<ErrorCode>> result = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.True(result.IsOk, result.ToString());
        return result.Value;
    }

    private static Dictionary<IVariableItem, Flt64> Values(
        IntegerProductFunction<Flt64> function,
        IntVar integer,
        RealVar input,
        double integerValue,
        double inputValue,
        long bits) {
        var values = new Dictionary<IVariableItem, Flt64> {
            [integer] = new Flt64(integerValue),
            [input] = new Flt64(inputValue),
            [function.InputVar] = new Flt64(inputValue),
            [function.ResultVar] = new Flt64(integerValue * inputValue),
        };
        for (int index = 0; index < function.BitVars.Count; ++index) {
            int bit = (int)((bits >> index) & 1);
            values[function.BitVars[index]] = new Flt64(bit);
            values[function.MaskedVars[index]] = new Flt64(bit * inputValue);
        }
        return values;
    }
}
