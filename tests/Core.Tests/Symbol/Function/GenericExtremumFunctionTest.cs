#nullable enable

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

/// <summary>泛型极值函数契约 / Generic extremum-function contracts.</summary>
public class GenericExtremumFunctionTest {
    [Fact]
    public async Task Flt32_ShouldRegisterAllExtremumVariants() =>
        await VerifyVariants(new TestNumberConverter<Flt32>(value => value.ToFlt32()));

    [Fact]
    public async Task Integer_ShouldRegisterAllExtremumVariants() =>
        await VerifyVariants(new TestNumberConverter<Integer>(value => value.ToInt64()));

    [Fact]
    public async Task Decimal_ShouldRegisterAllExtremumVariants() =>
        await VerifyVariants(new TestNumberConverter<FltX>(value => value.ToFltX()));

    [Fact]
    public async Task Rational_ShouldRegisterAllExtremumVariants() =>
        await VerifyVariants(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1.0)));

    [Fact]
    public void IntegerEvaluation_ShouldNotRoundLargeValuesThroughFlt64() => VerifyExactEvaluation(
        converter: new TestNumberConverter<Integer>(value => value.ToInt64()),
        larger: new Integer(9007199254740993L),
        smaller: new Integer(9007199254740992L)
    );

    [Fact]
    public void DecimalEvaluation_ShouldNotRoundFractionsThroughFlt64() => VerifyExactEvaluation(
        converter: new TestNumberConverter<FltX>(value => value.ToFltX()),
        larger: new FltX(9007199254740993.125m),
        smaller: new FltX(9007199254740993.124m)
    );

    [Fact]
    public async Task IntegerRows_ShouldRetainLargeCoefficients() => await VerifyRowCoefficient(
        converter: new TestNumberConverter<Integer>(value => value.ToInt64()),
        coefficient: new Integer(9007199254740993L)
    );

    [Fact]
    public async Task DecimalRows_ShouldRetainFractionalCoefficients() => await VerifyRowCoefficient(
        converter: new TestNumberConverter<FltX>(value => value.ToFltX()),
        coefficient: new FltX(9007199254740993.125m)
    );

    [Fact]
    public async Task ConversionOverflow_ShouldFailWithoutWritingRows() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var function = new MaxFunction<FltX>(
            polynomials: new[] { Constant(FltX.One) },
            converter: converter
        );
        using var metaModel = new LinearMetaModel<FltX>(converter: converter);
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<FltX>.InvokeAsync(metaModel);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<FltX> model = ((Ok<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>>)built).Value;
        converter.RejectEncoding = true;

        function.RegisterConstraints(model)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        ((IMechanismModel<FltX>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task NonFiniteInputOrBigM_ShouldFailWithoutWritingRows(int invalidPart) {
        var converter = new TestNumberConverter<Flt32>(value => value.ToFlt32());
        var x = new RealVar("nonfinite-extremum-x");
        var polynomial = new LinearPolynomial<Flt32>(
            new[] {
                new LinearMonomial<Flt32>(
                    invalidPart == 0 ? new Flt32(float.NaN) : converter.One,
                    x
                ),
            },
            invalidPart == 1 ? new Flt32(float.PositiveInfinity) : converter.Zero
        );
        Flt32? bigM = invalidPart == 2 ? new Flt32(float.PositiveInfinity) : null;
        IMathFunctionSymbol<Flt32>[] functions = {
            new MaxFunction<Flt32>(
                polynomials: new[] { polynomial },
                converter: converter,
                bigM: bigM
            ),
            new MinFunction<Flt32>(
                polynomials: new[] { polynomial },
                converter: converter,
                bigM: bigM
            ),
        };
        using var metaModel = new LinearMetaModel<Flt32>(converter: converter);
        metaModel.Add(x).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt32>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt32>.InvokeAsync(metaModel);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt32> model = ((Ok<LinearMechanismModel<Flt32>, ErrorCode, Error<ErrorCode>>)built).Value;
        foreach (IMathFunctionSymbol<Flt32> function in functions) {
            function.RegisterConstraints(model)
                .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        }
        ((IMechanismModel<Flt32>)model).Constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegerNegationOverflow_ShouldFailWithoutWritingRows() {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var x = new RealVar("negative-overflow-x");
        var function = new MinFunction<Integer>(
            polynomials: new[] {
                new LinearPolynomial<Integer>(
                    new[] { new LinearMonomial<Integer>(new Integer(long.MinValue), x) },
                    converter.Zero
                ),
            },
            converter: converter
        );
        using var metaModel = new LinearMetaModel<Integer>(converter: converter);
        metaModel.Add(x).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Integer>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Integer>.InvokeAsync(metaModel);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Integer> model = ((Ok<LinearMechanismModel<Integer>, ErrorCode, Error<ErrorCode>>)built).Value;

        Failed<Success, ErrorCode, Error<ErrorCode>> failure = function.RegisterConstraints(model)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>().Which;
        failure.Code.Should().Be(ErrorCode.IllegalArgument);
        failure.Message.Should().Contain("input");
        ((IMechanismModel<Integer>)model).Constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegerBigMDistanceOverflow_ShouldFailWithoutWritingRows() {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var lower = new RealVar("bigm-overflow-lower");
        lower.Range.Geq(new Flt64(-2));
        lower.Range.Leq(new Flt64(-2));
        var upper = new RealVar("bigm-overflow-upper");
        upper.Range.Geq(new Flt64(2));
        upper.Range.Leq(new Flt64(2));
        var coefficient = new Integer(3000000000000000000L);
        var function = new MaxFunction<Integer>(
            polynomials: new[] {
                new LinearPolynomial<Integer>(
                    new[] { new LinearMonomial<Integer>(coefficient, lower) },
                    converter.Zero
                ),
                new LinearPolynomial<Integer>(
                    new[] { new LinearMonomial<Integer>(coefficient, upper) },
                    converter.Zero
                ),
            },
            converter: converter
        );
        BigMValues.FiniteBounds(function.Polynomials[0], converter).Should().NotBeNull();
        BigMValues.FiniteBounds(function.Polynomials[1], converter).Should().NotBeNull();
        using var metaModel = new LinearMetaModel<Integer>(converter: converter);
        metaModel.Add(lower).IsOk.Should().BeTrue();
        metaModel.Add(upper).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Integer>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Integer>.InvokeAsync(metaModel);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Integer> model = ((Ok<LinearMechanismModel<Integer>, ErrorCode, Error<ErrorCode>>)built).Value;

        Failed<Success, ErrorCode, Error<ErrorCode>> failure = function.RegisterConstraints(model)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>().Which;
        failure.Code.Should().Be(ErrorCode.IllegalArgument);
        failure.Message.Should().Contain("Big-M");
        ((IMechanismModel<Integer>)model).Constraints.Should().BeEmpty();
    }

    private static void VerifyExactEvaluation<V>(TestNumberConverter<V> converter, V larger, V smaller)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var x = new RealVar("exact-extremum-x");
        var y = new RealVar("exact-extremum-y");
        LinearPolynomial<V>[] polynomials = { Polynomial(x, converter), Polynomial(y, converter) };
        IMathFunctionSymbol<V>[] functions = {
            new MaxFunction<V>(polynomials, converter),
            new MinFunction<V>(polynomials, converter),
            new MinMaxFunction<V>(polynomials, converter),
            new MaxMinFunction<V>(polynomials, converter),
        };
        int calls = converter.ConversionCalls;
        var values = new Dictionary<ISymbol, V> { [x] = larger, [y] = smaller };
        functions[0].Evaluate(values)!.Value.Eq(larger).Should().BeTrue();
        functions[1].Evaluate(values)!.Value.Eq(smaller).Should().BeTrue();
        functions[2].Evaluate(values)!.Value.Eq(larger).Should().BeTrue();
        functions[3].Evaluate(values)!.Value.Eq(smaller).Should().BeTrue();
        converter.ConversionCalls.Should().Be(calls);
    }

    private static async Task VerifyRowCoefficient<V>(IIntoValue<V> converter, V coefficient)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var x = new RealVar("precise-extremum-x");
        x.Range.Geq(Flt64.Zero);
        x.Range.Leq(Flt64.Zero);
        var function = new MaxFunction<V>(
            polynomials: new[] {
                new LinearPolynomial<V>(new[] { new LinearMonomial<V>(coefficient, x) }, converter.Zero),
            },
            converter: converter,
            bigM: converter.One
        );
        using var metaModel = new LinearMetaModel<V>(converter: converter);
        metaModel.Add(x).IsOk.Should().BeTrue();
        metaModel.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<V>.InvokeAsync(metaModel);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<V> model = ((Ok<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>)built).Value;
        var row = (LinearConstraintImpl<V>)((IMechanismModel<V>)model).Constraints[0];
        row.LinearCells.Single(cell => ReferenceEquals(cell.Token.Variable, x)).Coefficient
            .Eq(coefficient.Negate()).Should().BeTrue();
    }

    private static async Task VerifyVariants<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V Number(double value) => converter.IntoValue(new Flt64(value));
        var x = new RealVar("extremum-x");
        x.Range.Geq(new Flt64(-2));
        x.Range.Leq(new Flt64(5));
        var y = new RealVar("extremum-y");
        y.Range.Geq(Flt64.Zero);
        y.Range.Leq(new Flt64(4));
        LinearPolynomial<V>[] polynomials = { Polynomial(x, converter), Polynomial(y, converter) };
        (IMathFunctionSymbol<V> Function, bool Minimum)[] variants = {
            (new MaxFunction<V>(polynomials, converter), false),
            (new MinFunction<V>(polynomials, converter), true),
            (new MinMaxFunction<V>(polynomials, converter), false),
            (new MaxMinFunction<V>(polynomials, converter), true),
        };
        foreach ((IMathFunctionSymbol<V> function, bool minimum) in variants) {
            var resultVariable = (RealVar)function.HelperVariables[0];
            resultVariable.LowerBound!.Value.Unwrap().Should().Be(new Flt64(minimum ? -2 : 0));
            resultVariable.UpperBound!.Value.Unwrap().Should().Be(new Flt64(minimum ? 4 : 5));
            var values = new Dictionary<ISymbol, V> { [x] = Number(2), [y] = Number(4) };
            function.Evaluate(values)!.Value.Eq(Number(minimum ? 2 : 4)).Should().BeTrue();
            function.Evaluate(new Dictionary<ISymbol, V> { [x] = Number(2) }).Should().BeNull();
            ((IHasResultPolynomial<V>)function).ResultPolynomial.Monomials.Single().Coefficient
                .Eq(converter.One).Should().BeTrue();

            using var metaModel = new LinearMetaModel<V>(converter: converter);
            metaModel.Add(x).IsOk.Should().BeTrue();
            metaModel.Add(y).IsOk.Should().BeTrue();
            metaModel.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk.Should().BeTrue();
            Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built =
                await LinearMechanismModel<V>.InvokeAsync(metaModel);
            built.IsOk.Should().BeTrue();
            using LinearMechanismModel<V> model = ((Ok<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>)built).Value;
            IMechanismModel<V> mechanism = model;
            mechanism.Constraints.Should().HaveCount(5);
            var solution = new Dictionary<IVariableItem, V> {
                [x] = Number(2),
                [y] = Number(4),
                [function.HelperVariables[0]] = Number(minimum ? 2 : 4),
                [function.HelperVariables[1]] = Number(minimum ? 1 : 0),
                [function.HelperVariables[2]] = Number(minimum ? 0 : 1),
            };
            bool Feasible() => mechanism.Constraints.All(row => row.IsTrue(
                model.Tokens.TokensInSolver.Select(token => solution[token.Variable]).ToList()) == true);
            Feasible().Should().BeTrue();
            solution[function.HelperVariables[1]] = converter.One;
            solution[function.HelperVariables[2]] = converter.One;
            Feasible().Should().BeFalse();
            solution[y] = Number(2);
            solution[function.HelperVariables[0]] = Number(2);
            solution[function.HelperVariables[1]] = converter.One;
            solution[function.HelperVariables[2]] = converter.Zero;
            Feasible().Should().BeTrue();
            solution[function.HelperVariables[1]] = converter.Zero;
            solution[function.HelperVariables[2]] = converter.One;
            Feasible().Should().BeTrue();
        }
    }

    private static LinearPolynomial<V> Polynomial<V>(RealVar variable, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new(new[] { new LinearMonomial<V>(converter.One, variable) }, converter.Zero);

    private static LinearPolynomial<V> Constant<V>(V value)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new(System.Array.Empty<LinearMonomial<V>>(), value);
}
