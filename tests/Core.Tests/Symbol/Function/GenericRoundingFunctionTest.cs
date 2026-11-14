#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>泛型取整函数契约 / Generic rounding-function contracts.</summary>
public class GenericRoundingFunctionTest {
    [Fact]
    public void Flt32Evaluation_ShouldUseNativeArithmetic() =>
        VerifyEvaluation(new TestNumberConverter<Flt32>(value => value.ToFlt32()));

    [Fact]
    public void Flt64Evaluation_ShouldUseNativeArithmetic() =>
        VerifyEvaluation(IIntoValue<Flt64>.Identity);

    [Fact]
    public void DecimalEvaluation_ShouldUseNativeArithmetic() =>
        VerifyEvaluation(new TestNumberConverter<FltX>(value => value.ToFltX()));

    [Fact]
    public void RationalEvaluation_ShouldUseNativeArithmetic() =>
        VerifyEvaluation(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1.0)));

    [Fact]
    public void IntegerEvaluation_ShouldPreserveValuesAboveDoublePrecision() {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var x = new RealVar("rounding-exact-integer-x");
        IMathFunctionSymbol<Integer>[] functions = Functions(Polynomial(x, converter), converter);
        int calls = converter.ConversionCalls;
        var input = new Integer(9007199254740993L);

        foreach (IMathFunctionSymbol<Integer> function in functions) {
            function.Evaluate(new Dictionary<ISymbol, Integer> { [x] = input }).Should().Be(input);
            function.Evaluate(new Dictionary<ISymbol, Integer>()).Should().BeNull();
        }
        converter.ConversionCalls.Should().Be(calls);
    }

    [Fact]
    public void DecimalEvaluation_ShouldPreserveLargeFractionalValues() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var x = new RealVar("rounding-exact-decimal-x");
        IMathFunctionSymbol<FltX>[] functions = Functions(Polynomial(x, converter), converter);
        int calls = converter.ConversionCalls;
        var values = new Dictionary<ISymbol, FltX> { [x] = new(9007199254740993.625m) };

        functions[0].Evaluate(values).Should().Be(new FltX(9007199254740993m));
        functions[1].Evaluate(values).Should().Be(new FltX(9007199254740994m));
        functions[2].Evaluate(values).Should().Be(new FltX(9007199254740994m));
        converter.ConversionCalls.Should().Be(calls);
    }

    [Fact]
    public void Flt64Evaluation_ShouldMatchMidpointsAndAdjacentValues() {
        var x = new RealVar("rounding-neighbors-x");
        IMathFunctionSymbol<Flt64>[] functions = Functions(Polynomial(x, IIntoValue<Flt64>.Identity), IIntoValue<Flt64>.Identity);
        var values = new Dictionary<ISymbol, Flt64>();
        for (int integer = -10; integer <= 10; ++integer) {
            double midpoint = integer + 0.5;
            foreach (double value in new[] { double.BitDecrement(midpoint), midpoint, double.BitIncrement(midpoint) }) {
                values[x] = new Flt64(value);
                functions[0].Evaluate(values)!.Value.ToDouble().Should().Be(global::System.Math.Floor(value));
                functions[1].Evaluate(values)!.Value.ToDouble().Should().Be(global::System.Math.Ceiling(value));
                functions[2].Evaluate(values)!.Value.ToDouble().Should().Be(global::System.Math.Round(value));
            }
        }

        values[x] = new Flt64(-0.1);
        BitConverter.DoubleToInt64Bits(functions[1].Evaluate(values)!.Value.ToDouble()).Should().Be(long.MinValue);
        BitConverter.DoubleToInt64Bits(functions[2].Evaluate(values)!.Value.ToDouble()).Should().Be(long.MinValue);
    }

    [Fact]
    public async Task Decimal_ShouldRegisterAndEvaluateTypedRows() =>
        await VerifyRows(new TestNumberConverter<FltX>(value => value.ToFltX()));

    [Fact]
    public async Task Rational_ShouldRegisterAndEvaluateTypedRows() =>
        await VerifyRows(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1.0)));

    [Fact]
    public async Task Flt32_ShouldRejectCollapsedStrictGapBeforeWritingRows() =>
        await VerifyUnsupportedGap(new TestNumberConverter<Flt32>(value => value.ToFlt32()));

    [Fact]
    public async Task Integer_ShouldRejectTruncatedStrictGapBeforeWritingRows() =>
        await VerifyUnsupportedGap(new TestNumberConverter<Integer>(value => value.ToInt64()));

    [Fact]
    public async Task LargeConstants_ShouldRejectGapLostDuringFlattening() {
        await VerifyUnsupportedGap(IIntoValue<Flt64>.Identity, new Flt64(1e12));
        await VerifyUnsupportedGap(
            new TestNumberConverter<FltX>(value => value.ToFltX()),
            new FltX(1000000000000000000000000m)
        );
    }

    [Fact]
    public async Task ConstantConversionFailure_ShouldNotWriteRows() {
        var converter = new TestNumberConverter<FltX>(value =>
            value.ToDouble() == 1e-10
                ? throw new OverflowException("测试常量转换失败 / Test constant conversion failure.")
                : value.ToFltX());
        await VerifyUnsupportedGap(converter);
    }

    [Fact]
    public async Task ConversionFailure_ShouldNotWriteRows() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var x = new RealVar("rounding-conversion-x");
        IMathFunctionSymbol<FltX>[] functions = Functions(Polynomial(x, converter), converter);
        using var metaModel = new LinearMetaModel<FltX>(converter: converter);
        metaModel.Add(x).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<FltX>.InvokeAsync(metaModel);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<FltX> model = ((Ok<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>>)built).Value;
        converter.RejectEncoding = true;

        foreach (IMathFunctionSymbol<FltX> function in functions) {
            function.RegisterConstraints(model)
                .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        }
        ((IMechanismModel<FltX>)model).Constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task DecimalRows_ShouldRetainLargeFractionalCoefficients() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var x = new RealVar("rounding-coefficient-x");
        var coefficient = new FltX(9007199254740993.125m);
        var polynomial = new LinearPolynomial<FltX>(
            new[] { new LinearMonomial<FltX>(coefficient, x) },
            converter.Zero
        );
        IMathFunctionSymbol<FltX>[] functions = Functions(polynomial, converter);
        foreach (IMathFunctionSymbol<FltX> function in functions) {
            using var metaModel = new LinearMetaModel<FltX>(converter: converter);
            metaModel.Add(x).IsOk.Should().BeTrue();
            metaModel.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk.Should().BeTrue();
            Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built =
                await LinearMechanismModel<FltX>.InvokeAsync(metaModel);
            built.IsOk.Should().BeTrue();
            using LinearMechanismModel<FltX> model = ((Ok<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>>)built).Value;
            ((IMechanismModel<FltX>)model).Constraints
                .OfType<LinearConstraintImpl<FltX>>()
                .SelectMany(row => row.LinearCells)
                .Any(cell => cell.Coefficient.Eq(coefficient) || cell.Coefficient.Eq(coefficient.Negate()))
                .Should().BeTrue();
        }
    }

    private static void VerifyEvaluation<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V Number(double value) => converter.IntoValue(new Flt64(value));
        var x = new RealVar("rounding-typed-evaluate-x");
        IMathFunctionSymbol<V>[] functions = Functions(Polynomial(x, converter), converter);
        var values = new Dictionary<ISymbol, V>();
        foreach ((double input, double floor, double ceiling, double round) in new[] {
            (1.2, 1.0, 2.0, 1.0),
            (-1.2, -2.0, -1.0, -1.0),
            (2.5, 2.0, 3.0, 2.0),
            (-2.5, -3.0, -2.0, -2.0),
            (3.5, 3.0, 4.0, 4.0),
            (-3.5, -4.0, -3.0, -4.0),
        }) {
            values[x] = Number(input);
            functions[0].Evaluate(values)!.Value.Eq(Number(floor)).Should().BeTrue();
            functions[1].Evaluate(values)!.Value.Eq(Number(ceiling)).Should().BeTrue();
            functions[2].Evaluate(values)!.Value.Eq(Number(round)).Should().BeTrue();
        }
    }

    private static async Task VerifyRows<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V Number(double value) => converter.IntoValue(new Flt64(value));
        var x = new RealVar("rounding-typed-rows-x");
        LinearPolynomial<V> polynomial = Polynomial(x, converter);
        var floor = new FloorFunction<V>(polynomial, converter);
        var ceiling = new CeilingFunction<V>(polynomial, converter);
        var round = new RoundingFunction<V>(polynomial, converter);
        IMathFunctionSymbol<V>[] functions = { floor, ceiling, round };
        foreach (IMathFunctionSymbol<V> function in functions) {
            using var metaModel = new LinearMetaModel<V>(converter: converter);
            metaModel.Add(x).IsOk.Should().BeTrue();
            metaModel.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk.Should().BeTrue();
            Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built =
                await LinearMechanismModel<V>.InvokeAsync(metaModel);
            built.IsOk.Should().BeTrue();
            using LinearMechanismModel<V> model = ((Ok<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>)built).Value;
            var values = new Dictionary<IVariableItem, V> { [x] = Number(1.5) };
            switch (function) {
                case FloorFunction<V> f:
                    values[f.KVar] = Number(1);
                    values[f.ResultVar] = Number(1);
                    break;
                case CeilingFunction<V> c:
                    values[c.KVar] = Number(2);
                    values[c.ResultVar] = Number(2);
                    break;
                case RoundingFunction<V> r:
                    values[r.KVar] = Number(1);
                    values[r.FractionVar] = Number(0.5);
                    values[r.RoundUpVar] = Number(1);
                    values[r.ResultVar] = Number(2);
                    break;
            }
            var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
            ((IMechanismModel<V>)model).Constraints.Count.Should().Be(function == round ? 7 : 3);
            ((IMechanismModel<V>)model).Constraints.All(row => row.IsTrue(solution) == true).Should().BeTrue();
            if (function is RoundingFunction<V> rounding) {
                values[rounding.RoundUpVar] = Number(0);
                values[rounding.ResultVar] = Number(1);
                solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
                ((IMechanismModel<V>)model).Constraints.All(row => row.IsTrue(solution) == true).Should().BeFalse();
                values[rounding.RoundUpVar] = Number(1);
            }
            values[function switch {
                FloorFunction<V> f => f.ResultVar,
                CeilingFunction<V> c => c.ResultVar,
                RoundingFunction<V> r => r.ResultVar,
                _ => throw new InvalidOperationException(),
            }] = Number(0);
            solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
            ((IMechanismModel<V>)model).Constraints.All(row => row.IsTrue(solution) == true).Should().BeFalse();

            values[x] = Number(-1.5);
            switch (function) {
                case FloorFunction<V> f:
                    values[f.KVar] = Number(-2);
                    values[f.ResultVar] = Number(-2);
                    break;
                case CeilingFunction<V> c:
                    values[c.KVar] = Number(-1);
                    values[c.ResultVar] = Number(-1);
                    break;
                case RoundingFunction<V> r:
                    values[r.KVar] = Number(-2);
                    values[r.ResultVar] = Number(-1);
                    break;
            }
            solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
            ((IMechanismModel<V>)model).Constraints.All(row => row.IsTrue(solution) == true).Should().BeTrue();
        }
    }

    private static async Task VerifyUnsupportedGap<V>(IIntoValue<V> converter, V? constant = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var x = new RealVar("rounding-gap-x");
        var polynomial = new LinearPolynomial<V>(
            new[] { new LinearMonomial<V>(converter.One, x) },
            constant ?? converter.Zero
        );
        IMathFunctionSymbol<V>[] functions = Functions(polynomial, converter);
        using var metaModel = new LinearMetaModel<V>(converter: converter);
        metaModel.Add(x).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<V>.InvokeAsync(metaModel);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<V> model = ((Ok<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>)built).Value;
        foreach (IMathFunctionSymbol<V> function in functions) {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed = function.RegisterConstraints(model)
                .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>().Which;
            failed.Code.Should().Be(ErrorCode.IllegalArgument);
            failed.Message.Should().Contain("cannot represent");
        }
        ((IMechanismModel<V>)model).Constraints.Should().BeEmpty();
    }

    private static IMathFunctionSymbol<V>[] Functions<V>(LinearPolynomial<V> polynomial, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> => new IMathFunctionSymbol<V>[] {
            new FloorFunction<V>(polynomial, converter),
            new CeilingFunction<V>(polynomial, converter),
            new RoundingFunction<V>(polynomial, converter),
        };

    private static LinearPolynomial<V> Polynomial<V>(RealVar x, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> => new(
            new[] { new LinearMonomial<V>(converter.One, x) },
            converter.Zero
        );
}
