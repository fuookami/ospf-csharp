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
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>比较指示函数契约 / Comparison-indicator contracts.</summary>
public class InequalityFunctionTest {
    [Theory]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.EQ)]
    [InlineData(Comparison.NE)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.GT)]
    public async Task Relations_ShouldMatchTruthTableAndConstraintRows(Comparison sign) {
        var x = new RealVar($"comparison-{sign}-x");
        var function = new InequalityFunction<Flt64>(
            lhs: Polynomial(
                variable: x,
                coefficient: Flt64.One,
                constant: new Flt64(3)
            ),
            rhs: new Flt64(3),
            sign: sign,
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10),
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(0.5),
            name: $"comparison-{sign}"
        );
        using var metaModel = new LinearMetaModel<Flt64>();
        metaModel.Add(x).IsOk.Should().BeTrue();
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)built).Value;
        ((IMechanismModel<Flt64>)model).Constraints.Should().HaveCount(sign is Comparison.EQ or Comparison.NE ? 4 : 2);
        function.HelperVariables.Should().HaveCount(sign is Comparison.EQ or Comparison.NE ? 2 : 1);
        function.ResultPolynomial.Monomials.Single().Symbol.Should().BeSameAs(function.FlagVar);

        foreach (double input in new[] { -10, -1, -0.5, -0.25, -0.125, -0.0625, 0, 0.0625, 0.125, 0.25, 0.5, 1, 10 }) {
            bool? expected = Expected(sign, input);
            VAssert(function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new(input) }), expected);
            foreach (int flag in new[] { 0, 1 }) {
                bool feasible = new[] { 0, 1 }.Any(side => IsFeasible(
                    model: model,
                    function: function,
                    variable: x,
                    input: new Flt64(input),
                    flag: new Flt64(flag),
                    side: new Flt64(side)
                ));
                feasible.Should().Be(expected == (flag == 1), $"{sign} input={input}, flag={flag}");
            }
        }
        function.Evaluate(new Dictionary<ISymbol, Flt64>()).Should().BeNull();
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new(double.NaN) }).Should().BeNull();
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new(double.PositiveInfinity) }).Should().BeNull();
    }

    [Fact]
    public async Task Flt32_ShouldRegisterAllRelationsWithRepresentableGaps() =>
        await VerifyNumericTypes(new TestNumberConverter<Flt32>(value => value.ToFlt32()));

    [Fact]
    public async Task Integer_ShouldRegisterAllRelationsWithExplicitIntegralGaps() =>
        await VerifyNumericTypes(new TestNumberConverter<Integer>(value => value.ToInt64()));

    [Fact]
    public async Task Decimal_ShouldRegisterAllRelations() =>
        await VerifyNumericTypes(new TestNumberConverter<FltX>(value => value.ToFltX()));

    [Fact]
    public async Task Rational_ShouldRegisterAllRelations() =>
        await VerifyNumericTypes(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1.0)));

    [Fact]
    public void IntegerAndDecimalEvaluation_ShouldRetainValuesAboveDoublePrecision() {
        var integerConverter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var integerX = new RealVar("comparison-large-integer");
        var integer = new InequalityFunction<Integer>(
            lhs: Polynomial(
                variable: integerX,
                coefficient: Integer.One,
                constant: Integer.Zero
            ),
            rhs: new Integer(9007199254740992L),
            sign: Comparison.GT,
            converter: integerConverter,
            strictBoundary: Integer.One
        );
        integer.Evaluate(new Dictionary<ISymbol, Integer> {
            [integerX] = new Integer(9007199254740993L),
        }).Should().Be(Integer.One);

        var decimalConverter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var decimalX = new RealVar("comparison-large-decimal");
        var decimalFunction = new InequalityFunction<FltX>(
            lhs: Polynomial(
                variable: decimalX,
                coefficient: FltX.One,
                constant: FltX.Zero
            ),
            rhs: new FltX(9007199254740993.1m),
            sign: Comparison.GT,
            converter: decimalConverter,
            strictBoundary: new FltX(0.02m)
        );
        decimalFunction.Evaluate(new Dictionary<ISymbol, FltX> {
            [decimalX] = new FltX(9007199254740993.125m),
        }).Should().Be(FltX.One);
    }

    [Fact]
    public void DefaultBoundariesAndInputSnapshot_ShouldMatchBaseline() {
        var x = new RealVar("comparison-defaults");
        var monomials = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var function = new InequalityFunction<Flt64>(
            lhs: new LinearPolynomial<Flt64>(monomials, Flt64.Zero),
            rhs: Flt64.Zero,
            sign: Comparison.LE,
            converter: IIntoValue<Flt64>.Identity
        );
        monomials.Clear();
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }).Should().Be(Flt64.One);
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.5e-10) }).Should().BeNull();
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(1e-10) }).Should().Be(Flt64.Zero);
    }

    [Theory]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.EQ)]
    [InlineData(Comparison.NE)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.GT)]
    public async Task InvalidValues_ShouldFailBeforeTokensAndRows(Comparison sign) {
        var x = new RealVar("comparison-invalid-x");
        foreach (int invalid in Enumerable.Range(0, 7)) {
            Flt64 coefficient = invalid == 2 ? new Flt64(double.NaN) : new Flt64(invalid == 3 ? 1e308 : 1);
            var lhs = new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(coefficient, x), new LinearMonomial<Flt64>(coefficient, x) },
                Flt64.Zero
            );
            var function = new InequalityFunction<Flt64>(
                lhs: lhs,
                rhs: invalid == 1 ? new Flt64(double.NaN) : Flt64.Zero,
                sign: sign,
                converter: IIntoValue<Flt64>.Identity,
                bigM: invalid == 0 ? Flt64.Zero : invalid == 4 ? new Flt64(double.NaN) : new Flt64(10),
                tolerance: invalid == 5 ? new Flt64(double.NaN) : new Flt64(0.125),
                strictBoundary: invalid == 6 ? Flt64.Zero : new Flt64(0.5)
            );
            bool irrelevantBoundary = (invalid == 5 && sign is Comparison.LT or Comparison.GT) ||
                (invalid == 6 && sign is Comparison.LE or Comparison.GE);
            if (irrelevantBoundary) {
                continue;
            }
            await AssertRejected(
                function: function,
                variable: x,
                converter: IIntoValue<Flt64>.Identity
            );
        }
    }

    [Fact]
    public async Task InvalidEqualityBandsAndCollapsedGaps_ShouldFailBeforeWriting() {
        var x = new RealVar("comparison-gap-x");
        foreach ((double tolerance, double strict) in new[] { (0.5, 0.5), (1.0, 0.5), (-0.125, 0.5), (double.NaN, 0.5) }) {
            foreach (Comparison sign in new[] { Comparison.EQ, Comparison.NE }) {
                var function = new InequalityFunction<Flt64>(
                    lhs: Polynomial(
                        variable: x,
                        coefficient: Flt64.One,
                        constant: Flt64.Zero
                    ),
                    rhs: Flt64.Zero,
                    sign: sign,
                    converter: IIntoValue<Flt64>.Identity,
                    bigM: new Flt64(10),
                    tolerance: new Flt64(tolerance),
                    strictBoundary: new Flt64(strict)
                );
                function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }).Should().BeNull();
                await AssertRejected(
                    function: function,
                    variable: x,
                    converter: IIntoValue<Flt64>.Identity
                );
            }
        }
        var floatConverter = new TestNumberConverter<Flt32>(value => value.ToFlt32());
        var collapsed = new InequalityFunction<Flt32>(
            lhs: Polynomial(
                variable: x,
                coefficient: floatConverter.One,
                constant: floatConverter.Zero
            ),
            rhs: floatConverter.Zero,
            sign: Comparison.LE,
            converter: floatConverter,
            bigM: floatConverter.One
        );
        await AssertRejected(
            function: collapsed,
            variable: x,
            converter: floatConverter
        );
    }

    [Theory]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.EQ)]
    [InlineData(Comparison.NE)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.GT)]
    public async Task FlattenedConstant_ShouldNotEraseComparisonGap(Comparison sign) {
        var x = new RealVar("comparison-large-constant");
        var function = new InequalityFunction<Flt64>(
            lhs: Polynomial(
                variable: x,
                coefficient: Flt64.One,
                constant: new Flt64(1e16)
            ),
            rhs: Flt64.Zero,
            sign: sign,
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10),
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(0.5)
        );
        await AssertRejected(
            function: function,
            variable: x,
            converter: IIntoValue<Flt64>.Identity
        );
    }

    [Theory]
    [InlineData(Comparison.EQ)]
    [InlineData(Comparison.NE)]
    public async Task BigM_ShouldNotEraseEqualityBandGap(Comparison sign) {
        var x = new RealVar("comparison-rounded-band");
        var function = new InequalityFunction<Flt64>(
            lhs: Polynomial(
                variable: x,
                coefficient: Flt64.One,
                constant: Flt64.Zero
            ),
            rhs: Flt64.Zero,
            sign: sign,
            converter: IIntoValue<Flt64>.Identity,
            bigM: Flt64.One,
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(global::System.Math.BitIncrement(0.125))
        );
        await AssertRejected(
            function: function,
            variable: x,
            converter: IIntoValue<Flt64>.Identity
        );
    }

    [Fact]
    public async Task MissingInputOrHelperToken_ShouldFailBeforeWritingRows() {
        var x = new RealVar("comparison-missing-token");
        var function = new InequalityFunction<Flt64>(
            lhs: Polynomial(
                variable: x,
                coefficient: Flt64.One,
                constant: Flt64.Zero
            ),
            rhs: Flt64.Zero,
            sign: Comparison.EQ,
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10),
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(0.5)
        );
        using var missingInput = new LinearMetaModel<Flt64>();
        missingInput.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> missingInputResult =
            await LinearMechanismModel<Flt64>.InvokeAsync(missingInput);
        missingInputResult.Should().BeOfType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();

        using var missingSide = new LinearMetaModel<Flt64>();
        missingSide.Add(x).IsOk.Should().BeTrue();
        missingSide.Add(function.FlagVar).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(missingSide);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)built).Value;
        function.RegisterConstraints(model)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownComparison_ShouldFailBeforeWriting() {
        var x = new RealVar("comparison-unknown");
        var function = new InequalityFunction<Flt64>(
            lhs: Polynomial(
                variable: x,
                coefficient: Flt64.One,
                constant: Flt64.Zero
            ),
            rhs: Flt64.Zero,
            sign: (Comparison)int.MaxValue,
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        );
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }).Should().BeNull();
        await AssertRejected(
            function: function,
            variable: x,
            converter: IIntoValue<Flt64>.Identity
        );
    }

    [Fact]
    public void IntegerEvaluation_ShouldRejectMultiplicationAndAdditionOverflow() {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var x = new RealVar("comparison-evaluation-overflow");
        var product = new InequalityFunction<Integer>(
            lhs: Polynomial(
                variable: x,
                coefficient: new Integer(3),
                constant: Integer.Zero
            ),
            rhs: Integer.Zero,
            sign: Comparison.GT,
            converter: converter,
            strictBoundary: Integer.One
        );
        var sum = new InequalityFunction<Integer>(
            lhs: Polynomial(
                variable: x,
                coefficient: Integer.One,
                constant: new Integer(long.MaxValue)
            ),
            rhs: Integer.Zero,
            sign: Comparison.GT,
            converter: converter,
            strictBoundary: Integer.One
        );
        var zeroProduct = new InequalityFunction<Integer>(
            lhs: Polynomial(
                variable: x,
                coefficient: new Integer(4294967296L),
                constant: Integer.Zero
            ),
            rhs: Integer.Zero,
            sign: Comparison.GT,
            converter: converter,
            strictBoundary: Integer.One
        );
        product.Evaluate(new Dictionary<ISymbol, Integer> {
            [x] = new Integer(long.MaxValue),
        }).Should().BeNull();
        zeroProduct.Evaluate(new Dictionary<ISymbol, Integer> {
            [x] = new Integer(4294967296L),
        }).Should().BeNull();
        sum.Evaluate(new Dictionary<ISymbol, Integer> {
            [x] = new Integer(2),
        }).Should().BeNull();
    }

    [Fact]
    public async Task IntegerOverflow_ShouldFailBeforeWriting() {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var x = new RealVar("comparison-overflow-x");
        var repeated = new LinearPolynomial<Integer>(
            new[] {
                new LinearMonomial<Integer>(new Integer(long.MaxValue), x),
                new LinearMonomial<Integer>(Integer.One, x),
            },
            Integer.Zero
        );
        var coefficientOverflow = new InequalityFunction<Integer>(
            lhs: repeated,
            rhs: Integer.Zero,
            sign: Comparison.LE,
            converter: converter,
            bigM: new Integer(10),
            tolerance: Integer.One
        );
        var boundOverflow = new InequalityFunction<Integer>(
            lhs: Polynomial(
                variable: x,
                coefficient: Integer.One,
                constant: new Integer(long.MaxValue)
            ),
            rhs: new Integer(-1),
            sign: Comparison.GT,
            converter: converter,
            bigM: new Integer(10),
            strictBoundary: Integer.One
        );
        var mOverflow = new InequalityFunction<Integer>(
            lhs: Polynomial(
                variable: x,
                coefficient: Integer.One,
                constant: Integer.Zero
            ),
            rhs: Integer.Zero,
            sign: Comparison.NE,
            converter: converter,
            bigM: new Integer(long.MaxValue - 1),
            tolerance: Integer.Zero,
            strictBoundary: Integer.One
        );
        await AssertRejected(
            function: coefficientOverflow,
            variable: x,
            converter: converter
        );
        await AssertRejected(
            function: boundOverflow,
            variable: x,
            converter: converter
        );
        await AssertRejected(
            function: mOverflow,
            variable: x,
            converter: converter
        );
    }

    [Fact]
    public async Task InvalidRegistration_ShouldPreserveExistingTokensAndSymbolState() {
        var x = new RealVar("comparison-rollback-x");
        var function = new InequalityFunction<Flt64>(
            lhs: Polynomial(
                variable: x,
                coefficient: Flt64.One,
                constant: Flt64.Zero
            ),
            rhs: Flt64.Zero,
            sign: Comparison.EQ,
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10),
            tolerance: new Flt64(1),
            strictBoundary: new Flt64(1)
        );
        using var metaModel = new LinearMetaModel<Flt64>();
        metaModel.Add(x).IsOk.Should().BeTrue();
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsFailed.Should().BeTrue();
        metaModel.Tokens.Symbols.Should().BeEmpty();
        metaModel.Tokens.TokenList.Find(x).Should().NotBeNull();
        metaModel.Tokens.TokenList.Find(function.FlagVar).Should().BeNull();
        metaModel.Tokens.TokenList.Find(function.SideVar!).Should().BeNull();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)built).Value;
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    private static async Task VerifyNumericTypes<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V Number(double value) => converter.IntoValue(new Flt64(value));
        foreach (Comparison sign in Enum.GetValues<Comparison>()) {
            var x = new RealVar($"comparison-{typeof(V).Name}-{sign}");
            var function = new InequalityFunction<V>(
                lhs: Polynomial(
                    variable: x,
                    coefficient: converter.One,
                    constant: converter.Zero
                ),
                rhs: converter.Zero,
                sign: sign,
                converter: converter,
                bigM: Number(10),
                tolerance: Number(1),
                strictBoundary: Number(2)
            );
            using var metaModel = new LinearMetaModel<V>(converter: converter);
            metaModel.Add(x).IsOk.Should().BeTrue();
            metaModel.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk.Should().BeTrue();
            Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built =
                await LinearMechanismModel<V>.InvokeAsync(metaModel);
            built.IsOk.Should().BeTrue();
            using LinearMechanismModel<V> model = ((Ok<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>)built).Value;
            foreach (double input in new[] { -2.0, 0.0, 2.0 }) {
                bool expected = sign switch {
                    Comparison.LT => input < 0,
                    Comparison.LE => input <= 0,
                    Comparison.EQ => input == 0,
                    Comparison.NE => input != 0,
                    Comparison.GE => input >= 0,
                    Comparison.GT => input > 0,
                    _ => false,
                };
                function.Evaluate(new Dictionary<ISymbol, V> { [x] = Number(input) })!.Value
                    .Eq(expected ? converter.One : converter.Zero).Should().BeTrue();
                bool feasible = new[] { 0, 1 }.Any(side => IsFeasible(
                    model: model,
                    function: function,
                    variable: x,
                    input: Number(input),
                    flag: expected ? converter.One : converter.Zero,
                    side: Number(side)
                ));
                feasible.Should().BeTrue($"{typeof(V).Name} {sign}, input={input}");
            }
        }
    }

    private static async Task AssertRejected<V>(
        InequalityFunction<V> function,
        RealVar variable,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        using var tokens = new ManualTokenList<V>();
        function.RegisterAuxiliaryTokens(tokens)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        tokens.Tokens.Should().BeEmpty();
        using var typedMeta = new LinearMetaModel<V>(converter: converter);
        typedMeta.Add(variable).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(typedMeta);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<V> model = ((Ok<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>)built).Value;
        function.RegisterConstraints(model)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        ((IMechanismModel<V>)model).Constraints.Should().BeEmpty();
    }

    private static bool IsFeasible<V>(
        LinearMechanismModel<V> model,
        InequalityFunction<V> function,
        RealVar variable,
        V input,
        V flag,
        V side)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var values = new Dictionary<IVariableItem, V> { [variable] = input, [function.FlagVar] = flag };
        if (function.SideVar is { } sideVar) {
            values[sideVar] = side;
        }
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        return ((IMechanismModel<V>)model).Constraints.All(row => row.IsTrue(solution) == true);
    }

    private static LinearPolynomial<V> Polynomial<V>(RealVar variable, V coefficient, V constant)
        where V : struct, IRealNumber<V>, INumberField<V> => new(
            new[] { new LinearMonomial<V>(coefficient, variable) },
            constant
        );

    private static bool? Expected(Comparison sign, double value) => sign switch {
        Comparison.GT => value >= 0.5 ? true : value <= 0 ? false : null,
        Comparison.GE => value >= 0 ? true : value <= -0.125 ? false : null,
        Comparison.LT => value <= -0.5 ? true : value >= 0 ? false : null,
        Comparison.LE => value <= 0 ? true : value >= 0.125 ? false : null,
        Comparison.EQ => global::System.Math.Abs(value) <= 0.125 ? true : global::System.Math.Abs(value) >= 0.5 ? false : null,
        Comparison.NE => global::System.Math.Abs(value) <= 0.125 ? false : global::System.Math.Abs(value) >= 0.5 ? true : null,
        _ => null,
    };

    private static void VAssert(Flt64? actual, bool? expected) {
        if (expected is null) {
            actual.Should().BeNull();
        }
        else {
            actual.Should().Be(expected.Value ? Flt64.One : Flt64.Zero);
        }
    }
}
