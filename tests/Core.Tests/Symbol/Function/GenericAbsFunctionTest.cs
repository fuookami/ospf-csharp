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
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>泛型 ABS 的数值与注册契约 / Numeric and registration contracts of generic ABS.</summary>
public class GenericAbsFunctionTest {
    [Fact]
    public async Task Flt32_ShouldRegisterTypedRows() =>
        await VerifyRegistration(new TestNumberConverter<Flt32>(value => value.ToFlt32()));

    [Fact]
    public async Task Integer_ShouldRegisterTypedRows() =>
        await VerifyRegistration(new TestNumberConverter<Integer>(value => value.ToInt64()));

    [Fact]
    public async Task Decimal_ShouldRegisterTypedRows() =>
        await VerifyRegistration(new TestNumberConverter<FltX>(value => value.ToFltX()));

    [Fact]
    public async Task Rational_ShouldRegisterTypedRows() =>
        await VerifyRegistration(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1.0)));

    [Fact]
    public void IntegerEvaluation_ShouldPreserveValuesAboveDoublePrecisionWithoutConversion() {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var x = new RealVar("exact-integer-x");
        var function = new AbsFunction<Integer>(
            polynomial: new LinearPolynomial<Integer>(
                new[] { new LinearMonomial<Integer>(new Integer(3), x) },
                new Integer(2)
            ),
            converter: converter
        );
        int calls = converter.ConversionCalls;

        function.Evaluate(new Dictionary<ISymbol, Integer> {
            [x] = new Integer(-9007199254740993L),
        }).Should().Be(new Integer(27021597764222977L));
        function.Evaluate(new Dictionary<ISymbol, Integer>()).Should().BeNull();
        converter.ConversionCalls.Should().Be(calls);
    }

    [Fact]
    public void DecimalEvaluation_ShouldPreserveFractionWithoutConversion() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var x = new RealVar("exact-decimal-x");
        var function = new AbsFunction<FltX>(
            polynomial: new LinearPolynomial<FltX>(
                new[] { new LinearMonomial<FltX>(new FltX(3m), x) },
                new FltX(2m)
            ),
            converter: converter
        );
        int calls = converter.ConversionCalls;

        function.Evaluate(new Dictionary<ISymbol, FltX> {
            [x] = new FltX(-9007199254740993.125m),
        }).Should().Be(new FltX(27021597764222977.375m));
        converter.ConversionCalls.Should().Be(calls);
    }

    [Fact]
    public void RationalEvaluation_ShouldUseRationalArithmetic() {
        var converter = new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1.0));
        var x = new RealVar("rational-evaluate-x");
        var function = new AbsFunction<RtnX>(
            polynomial: new LinearPolynomial<RtnX>(
                new[] { new LinearMonomial<RtnX>(new RtnX(1, 3), x) },
                new RtnX(1, 2)
            ),
            converter: converter
        );
        int calls = converter.ConversionCalls;

        function.Evaluate(new Dictionary<ISymbol, RtnX> {
            [x] = new RtnX(-3, 1),
        })!.Value.Eq(new RtnX(1, 2)).Should().BeTrue();
        converter.ConversionCalls.Should().Be(calls);
    }

    [Fact]
    public async Task Flt32NonFiniteCoefficient_ShouldReturnFailure() {
        var converter = new TestNumberConverter<Flt32>(value => value.ToFlt32());
        var x = new RealVar("nonfinite-float-x");
        var function = new AbsFunction<Flt32>(
            polynomial: new LinearPolynomial<Flt32>(
                new[] { new LinearMonomial<Flt32>(new Flt32(float.NaN), x) },
                converter.Zero
            ),
            converter: converter
        );
        using var model = new LinearMetaModel<Flt32>(converter: converter);
        model.Add(x).IsOk.Should().BeTrue();
        model.Add(new LinearFunctionSymbolAdapter<Flt32>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt32>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt32>.InvokeAsync(model);
        result.Should().BeOfType<Failed<LinearMechanismModel<Flt32>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    [Fact]
    public async Task ConversionOverflow_ShouldReturnFailureWithoutWritingRows() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var function = new AbsFunction<FltX>(
            polynomial: new LinearPolynomial<FltX>(Array.Empty<LinearMonomial<FltX>>(), FltX.One),
            converter: converter
        );
        using var metaModel = new LinearMetaModel<FltX>(converter: converter);
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<FltX>.InvokeAsync(metaModel);
        result.IsOk.Should().BeTrue();
        using LinearMechanismModel<FltX> model = ((Ok<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>>)result).Value;
        converter.RejectEncoding = true;

        function.RegisterConstraints(model)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        ((IMechanismModel<FltX>)model).Constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task IntegerCells_ShouldPreserveLargeCoefficients() =>
        await VerifyNativeCells(
            new TestNumberConverter<Integer>(value => value.ToInt64()),
            new Integer(9007199254740993L)
        );

    [Fact]
    public async Task DecimalCells_ShouldPreserveFractionalCoefficients() =>
        await VerifyNativeCells(
            new TestNumberConverter<FltX>(value => value.ToFltX()),
            new FltX(9007199254740993.125m)
        );

    [Fact]
    public async Task RationalCells_ShouldPreserveFractionsAndValidZeros() =>
        await VerifyNativeCells(
            new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1.0)),
            new RtnX(1, 3)
        );

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TokenTables_ShouldUseModelConverterForCachedResults(bool manual, bool concurrent) {
        var converter = new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1.0));
        using var model = new LinearMetaModel<RtnX>(
            configuration: new MetaModelConfiguration(
                ManualTokenAddition: manual,
                Concurrent: concurrent
            ),
            converter: converter
        );
        var x = new RealVar("typed-token-x");
        model.Add(x).IsOk.Should().BeTrue();
        model.Tokens.Find(x)!.SetResult(new RtnX(1, 2));
        model.Tokens.Find(x)!.Result!.Value.Eq(new RtnX(1, 2)).Should().BeTrue();
    }

    private static async Task VerifyNativeCells<V>(TestNumberConverter<V> converter, V coefficient)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var x = new RealVar("native-coefficient-x");
        using var metaModel = new LinearMetaModel<V>(converter: converter);
        metaModel.Add(x).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<V>.InvokeAsync(metaModel);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<V> model = ((Ok<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>)built).Value;
        int calls = converter.ConversionCalls;

        var linear = new LinearRelationImpl<V>(
            FlattenData: new LinearFlattenData<V>(
                new[] { new LinearMonomial<V>(coefficient, x) },
                coefficient.Negate()
            ),
            Sign: Comparison.GE
        );
        var quadratic = new QuadraticRelationImpl<V>(
            FlattenData: new QuadraticFlattenData<V>(
                new[] {
                    new QuadraticMonomial<V>(
                        Coefficient: coefficient,
                        Symbol1: x,
                        Symbol2: x
                    ),
                },
                coefficient.Negate()
            ),
            Sign: Comparison.GE
        );
        ILinearRelation<V> normalizedLinear = linear.Normalize();
        IQuadraticRelation<V> normalizedQuadratic = quadratic.Normalize();
        normalizedLinear.Sign.Should().Be(Comparison.LE);
        normalizedQuadratic.Sign.Should().Be(Comparison.LE);
        normalizedLinear.FlattenData.Constant.Eq(coefficient).Should().BeTrue();
        normalizedQuadratic.FlattenData.Constant.Eq(coefficient).Should().BeTrue();
        normalizedLinear.FlattenData.Monomials.Single().Coefficient.Eq(coefficient.Negate()).Should().BeTrue();
        normalizedQuadratic.FlattenData.Monomials.Single().Coefficient.Eq(coefficient.Negate()).Should().BeTrue();

        Result<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> linearResult = LinearConstraintImpl<V>.Create(
            relation: linear,
            tokens: model.Tokens,
            converter: converter
        );
        Result<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> quadraticResult = QuadraticConstraintImpl<V>.Create(
            relation: quadratic,
            tokens: model.Tokens,
            converter: converter
        );
        linearResult.IsOk.Should().BeTrue();
        quadraticResult.IsOk.Should().BeTrue();
        LinearConstraintImpl<V> linearRow = ((Ok<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)linearResult).Value;
        QuadraticConstraintImpl<V> quadraticRow = ((Ok<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)quadraticResult).Value;
        linearRow.LinearCells.Single().Coefficient.Eq(coefficient).Should().BeTrue();
        quadraticRow.QuadraticCells.Single().Coefficient.Eq(coefficient).Should().BeTrue();
        linearRow.Rhs.Eq(coefficient).Should().BeTrue();
        quadraticRow.Rhs.Eq(coefficient).Should().BeTrue();
        linearRow.IsTrue(new[] { converter.One }).Should().BeTrue();
        quadraticRow.IsTrue(new[] { converter.One }).Should().BeTrue();
        linearRow.IsTrue(new[] { converter.Zero }).Should().BeFalse();
        quadraticRow.IsTrue(new[] { converter.Zero }).Should().BeFalse();
        linearRow.LinearCells.Single().Evaluate(new Dictionary<VariableItemKey, V> {
            [x.Key] = converter.One,
        })!.Value.Eq(coefficient).Should().BeTrue();
        quadraticRow.QuadraticCells.Single().Evaluate(new Dictionary<VariableItemKey, V> {
            [x.Key] = converter.One,
        })!.Value.Eq(coefficient).Should().BeTrue();
        converter.ConversionCalls.Should().Be(calls);
        linearRow.LinearCells.Single().Token.SetResult(converter.One);
        linearRow.IsTrue().Should().BeTrue();
        quadraticRow.IsTrue().Should().BeTrue();
        linearRow.LinearCells.Single().Token.SetResult(converter.Zero);
        linearRow.IsTrue().Should().BeFalse();
        quadraticRow.IsTrue().Should().BeFalse();
    }

    private static async Task VerifyRegistration<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V Number(double value) => converter.IntoValue(new Flt64(value));
        var x = new RealVar($"typed-{typeof(V).Name}-x");
        x.Range.Geq(new Flt64(-3.0));
        x.Range.Leq(new Flt64(7.0));
        var function = new AbsFunction<V>(
            polynomial: new LinearPolynomial<V>(
                new[] { new LinearMonomial<V>(Number(2.0), x) },
                converter.One
            ),
            converter: converter,
            name: "typed"
        );
        function.PositiveBigM.Eq(Number(15.0)).Should().BeTrue();
        function.NegativeBigM.Eq(Number(5.0)).Should().BeTrue();
        function.ResultVar.UpperBound!.Value.Unwrap().Should().Be(new Flt64(15.0));
        function.PositiveVar.UpperBound!.Value.Unwrap().Should().Be(new Flt64(15.0));
        function.NegativeVar.UpperBound!.Value.Unwrap().Should().Be(new Flt64(5.0));
        function.ResultPolynomial.Monomials.Single().Coefficient.Eq(converter.One).Should().BeTrue();

        using var metaModel = new LinearMetaModel<V>(converter: converter);
        metaModel.Add(x).IsOk.Should().BeTrue();
        metaModel.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<V>.InvokeAsync(metaModel);
        result.IsOk.Should().BeTrue();
        using LinearMechanismModel<V> model = ((Ok<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>)result).Value;
        IMechanismModel<V> mechanism = model;
        mechanism.Constraints.Select(row => row.Name).Should().Equal(new[] {
            "typed_abs_result", "typed_abs_decompose", "typed_abs_pos_ub", "typed_abs_neg_ub",
        });

        var values = new Dictionary<IVariableItem, V> {
            [x] = Number(-3.0),
            [function.ResultVar] = Number(5.0),
            [function.PositiveVar] = converter.Zero,
            [function.NegativeVar] = Number(5.0),
            [function.SignVar] = converter.Zero,
        };
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        mechanism.Constraints.All(row => row.IsTrue(solution) == true).Should().BeTrue();
        values[function.ResultVar] = Number(6.0);
        solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        mechanism.Constraints.All(row => row.IsTrue(solution) == true).Should().BeFalse();
    }
}

internal sealed class TestNumberConverter<V>(Func<Flt64, V> intoValue) : IIntoValue<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    public int ConversionCalls { get; private set; }
    public bool RejectEncoding { get; set; }
    public V Zero => NumericConstantsRegistry.For<V>().Zero;
    public V One => NumericConstantsRegistry.For<V>().One;

    public V IntoValue(Flt64 value) {
        ++ConversionCalls;
        return intoValue(value);
    }

    public Flt64 FromValue(V value) {
        ++ConversionCalls;
        if (RejectEncoding) {
            throw new OverflowException("测试转换溢出 / Test conversion overflow.");
        }
        return value.ToFlt64();
    }
}

