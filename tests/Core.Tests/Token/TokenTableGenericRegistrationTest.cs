#nullable enable

using System;
using System.Collections.Generic;
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
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Token;

public class TokenTableGenericRegistrationTest {
    [Fact]
    public void DecimalFunctions_ShouldPrepareInDependencyOrderWithoutLosingPrecision() {
        var converter = new NumberConverter<FltX>(value => value.ToFltX());
        using var table = new ConcurrentAutoTokenTable<FltX>(
            category: LinearCategory.Instance,
            symbols: new List<IIntermediateSymbol>(),
            converter: converter
        );
        var x = new RealVar("decimal-registration-x");
        table.Add(x).IsOk.Should().BeTrue();
        table.TokenList.Find(x)!.SetResult(new FltX(-2m));
        LinearFunctionSymbolAdapter<FltX> inner = Abs(
            name: "decimal-inner",
            input: x,
            converter: converter
        );
        LinearFunctionSymbolAdapter<FltX> outer = Abs(
            name: "decimal-outer",
            input: inner,
            converter: converter
        );
        var values = new Dictionary<ISymbol, FltX> { [x] = new FltX(-9007199254740993.125m) };
        converter.Conversions = 0;

        Try result = new IIntermediateSymbol[] { outer, inner }.Register(
            tokenTable: table,
            converter: converter,
            fixedValues: values
        );

        result.IsOk.Should().BeTrue();
        table.CachedValue(inner, (IReadOnlyList<FltX>?)null).Should().Be(new FltX(9007199254740993.125m));
        table.CachedValue(outer, (IReadOnlyList<FltX>?)null).Should().Be(new FltX(9007199254740993.125m));
        converter.Conversions.Should().Be(0);
        table.TokenList.Find(x)!.Result.Should().Be(new FltX(-2m));
        values.Should().ContainSingle();
        inner.HelperVariables.Should().OnlyContain(variable => table.TokenList.Find(variable) != null);
        outer.HelperVariables.Should().OnlyContain(variable => table.TokenList.Find(variable) != null);
    }

    [Fact]
    public void IntegerAndRationalFunctions_ShouldPrepareInTheirNativeTypes() {
        var x = new RealVar("native-registration-x");
        var integerConverter = new NumberConverter<Integer>(value => value.ToInt64());
        using var integer = new ConcurrentAutoTokenTable<Integer>(
            category: LinearCategory.Instance,
            symbols: new List<IIntermediateSymbol>(),
            converter: integerConverter
        );
        integer.Add(x).IsOk.Should().BeTrue();
        LinearFunctionSymbolAdapter<Integer> integerFunction = Abs(
            name: "integer-absolute",
            input: x,
            converter: integerConverter
        );
        new IIntermediateSymbol[] { integerFunction }.Register(
            tokenTable: integer,
            converter: integerConverter,
            fixedValues: new Dictionary<ISymbol, Integer> { [x] = new Integer(-9007199254740993L) }
        ).IsOk.Should().BeTrue();
        integer.CachedValue(integerFunction, (IReadOnlyList<Integer>?)null)
            .Should().Be(new Integer(9007199254740993L));

        var rationalConverter = new NumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1));
        using var rational = new ConcurrentAutoTokenTable<RtnX>(
            category: LinearCategory.Instance,
            symbols: new List<IIntermediateSymbol>(),
            converter: rationalConverter
        );
        rational.Add(x).IsOk.Should().BeTrue();
        LinearFunctionSymbolAdapter<RtnX> rationalFunction = Abs(
            name: "rational-absolute",
            input: x,
            converter: rationalConverter
        );
        new IIntermediateSymbol[] { rationalFunction }.Register(
            tokenTable: rational,
            converter: rationalConverter,
            fixedValues: new Dictionary<ISymbol, RtnX> { [x] = new RtnX(-2, 3) }
        ).IsOk.Should().BeTrue();
        rational.CachedValue(rationalFunction, (IReadOnlyList<RtnX>?)null)!.Value.Eq(new RtnX(2, 3)).Should().BeTrue();
    }

    [Fact]
    public void GenericRegistrationFailure_ShouldRestoreCachesAndRemoveAddedHelpers() {
        var converter = new NumberConverter<FltX>(value => value.ToFltX());
        using var table = new ConcurrentAutoTokenTable<FltX>(
            category: LinearCategory.Instance,
            symbols: new List<IIntermediateSymbol>(),
            converter: converter
        );
        var x = new RealVar("decimal-rollback-x");
        var removed = new RealVar("decimal-rollback-removed");
        table.Add(x).IsOk.Should().BeTrue();
        table.Add(removed).IsOk.Should().BeTrue();
        table.Remove(removed);
        LinearFunctionSymbolAdapter<FltX> function = Abs(
            name: "decimal-rollback",
            input: x,
            converter: converter
        );
        table.Cache(
            cacheKey: function,
            solution: (IReadOnlyList<FltX>?)null,
            value: new FltX(42m)
        );

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            converter: converter,
            fixedValues: new Dictionary<ISymbol, FltX> { [x] = new FltX(-3m) },
            callBack: status => status.ReadySymbolAmount.ToULong() == 0
                ? Results.OkInstance
                : new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationStopped, "stopped"),
                    new Err<ErrorCode>(ErrorCode.ApplicationFailed, "failed")
                )
        );

        result.Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Errors.Should().HaveCount(2);
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().ContainSingle().Which.Variable.Should().BeSameAs(x);
        table.CachedValue(function, (IReadOnlyList<FltX>?)null).Should().Be(new FltX(42m));
        var next = new RealVar("decimal-rollback-next");
        table.Add(next).IsOk.Should().BeTrue();
        table.TokenList.Find(next)!.SolverIndex.Should().Be(2);
    }

    [Fact]
    public void RationalFixedFunctionConflict_ShouldNotBeHiddenByCrossProductOverflow() {
        var converter = new NumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1));
        using var table = new ConcurrentAutoTokenTable<RtnX>(
            category: LinearCategory.Instance,
            symbols: new List<IIntermediateSymbol>(),
            converter: converter
        );
        var x = new RealVar("rational-conflict-x");
        table.Add(x).IsOk.Should().BeTrue();
        LinearFunctionSymbolAdapter<RtnX> function = Abs(
            name: "rational-conflict",
            input: x,
            converter: converter
        );
        var values = new Dictionary<ISymbol, RtnX> {
            [x] = new RtnX(double.MaxValue, double.MaxValue),
            [function] = new RtnX(double.MaxValue / 2, double.MaxValue),
        };

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            converter: converter,
            fixedValues: values
        );

        result.IsFailed.Should().BeTrue();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().ContainSingle().Which.Variable.Should().BeSameAs(x);
    }

    [Fact]
    public void ArithmeticOverflowDuringPreparation_ShouldReturnFailureAndRestoreTheTable() {
        var converter = new NumberConverter<FltX>(value => value.ToFltX());
        using var table = new ConcurrentAutoTokenTable<FltX>(
            category: LinearCategory.Instance,
            symbols: new List<IIntermediateSymbol>(),
            converter: converter
        );
        var x = new RealVar("decimal-overflow-x");
        table.Add(x).IsOk.Should().BeTrue();
        var function = new LinearFunctionSymbolAdapter<FltX>(new AbsFunction<FltX>(
            polynomial: new LinearPolynomial<FltX>([new LinearMonomial<FltX>(new FltX(2m), x)], FltX.Zero),
            converter: converter,
            name: "decimal-overflow"
        ));
        table.Cache(
            cacheKey: function,
            solution: (IReadOnlyList<FltX>?)null,
            value: new FltX(42m)
        );

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            converter: converter,
            fixedValues: new Dictionary<ISymbol, FltX> { [x] = new FltX(decimal.MaxValue) }
        );

        result.IsFailed.Should().BeTrue();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().ContainSingle().Which.Variable.Should().BeSameAs(x);
        table.CachedValue(function, (IReadOnlyList<FltX>?)null).Should().Be(new FltX(42m));
    }

    [Fact]
    public void NonfinitePreparedValues_ShouldFailWithoutPublishingCaches() {
        var x = new RealVar("floating-overflow-x");
        using var table = new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        table.Add(x).IsOk.Should().BeTrue();
        var function = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(
            polynomial: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(new Flt64(2), x)], Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            name: "floating-overflow"
        ));

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.MaxValue) }
        );

        result.IsFailed.Should().BeTrue();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().ContainSingle().Which.Variable.Should().BeSameAs(x);
        table.Cached(function, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    [Theory]
    [InlineData("abs", true)]
    [InlineData("abs", false)]
    [InlineData("max", true)]
    [InlineData("max", false)]
    [InlineData("minmax", true)]
    [InlineData("floor", true)]
    [InlineData("ceiling", false)]
    [InlineData("round", true)]
    public void IntegerOverflow_ShouldFailPreparationAndRestoreTheTable(string kind, bool multiplication) {
        var converter = new NumberConverter<Integer>(value => value.ToInt64());
        using var table = new ConcurrentAutoTokenTable<Integer>(
            category: LinearCategory.Instance,
            symbols: new List<IIntermediateSymbol>(),
            converter: converter
        );
        var x = new RealVar($"integer-overflow-{kind}");
        table.Add(x).IsOk.Should().BeTrue();
        var polynomial = new LinearPolynomial<Integer>(
            [new LinearMonomial<Integer>(new Integer(multiplication ? 2 : 1), x)],
            new Integer(multiplication ? 0 : long.MaxValue)
        );
        IMathFunctionSymbol<Integer> source = kind switch {
            "abs" => new AbsFunction<Integer>(polynomial, converter),
            "max" => new MaxFunction<Integer>([polynomial], converter),
            "minmax" => new MinMaxFunction<Integer>([polynomial], converter),
            "floor" => new FloorFunction<Integer>(polynomial, converter),
            "ceiling" => new CeilingFunction<Integer>(polynomial, converter),
            _ => new RoundingFunction<Integer>(polynomial, converter),
        };
        var function = new LinearFunctionSymbolAdapter<Integer>(source);
        var values = new Dictionary<ISymbol, Integer> {
            [x] = new Integer(multiplication ? long.MaxValue : 1),
        };
        table.Cache(
            cacheKey: function,
            solution: (IReadOnlyList<Integer>?)null,
            value: new Integer(42)
        );

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            converter: converter,
            fixedValues: values
        );

        result.IsFailed.Should().BeTrue();
        source.Evaluate(values).Should().BeNull();
        source.TryEvaluate(values).IsFailed.Should().BeTrue();
        function.TryEvaluate(values).IsFailed.Should().BeTrue();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().ContainSingle().Which.Variable.Should().BeSameAs(x);
        table.CachedValue(function, (IReadOnlyList<Integer>?)null).Should().Be(new Integer(42));
    }

    [Fact]
    public void StructuredEvaluationFailure_ShouldPreserveFatalErrorsAndRollback() {
        var converter = new NumberConverter<Integer>(value => value.ToInt64());
        using var table = new ConcurrentAutoTokenTable<Integer>(
            category: LinearCategory.Instance,
            symbols: new List<IIntermediateSymbol>(),
            converter: converter
        );
        var source = new FatalEvaluationFunction();
        var function = new LinearFunctionSymbolAdapter<Integer>(source);

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            converter: converter
        );

        result.Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Errors.Should().HaveCount(2);
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().BeEmpty();
        table.Cached(function, (IReadOnlyList<Integer>?)null).Should().BeFalse();
    }

    [Fact]
    public void CheckedEvaluation_ShouldDistinguishUnknownValuesFromOverflow() {
        var converter = new NumberConverter<Integer>(value => value.ToInt64());
        var x = new RealVar("checked-input");
        LinearFunctionSymbolAdapter<Integer> function = Abs(
            name: "checked-absolute",
            input: x,
            converter: converter
        );

        Result<Integer?, ErrorCode, Error<ErrorCode>> missing = function.TryEvaluate(new Dictionary<ISymbol, Integer>());
        Result<Integer?, ErrorCode, Error<ErrorCode>> boundary = function.TryEvaluate(
            new Dictionary<ISymbol, Integer> { [x] = new Integer(-long.MaxValue) }
        );
        Result<Integer?, ErrorCode, Error<ErrorCode>> overflow = function.TryEvaluate(
            new Dictionary<ISymbol, Integer> { [x] = new Integer(long.MinValue) }
        );

        missing.IsOk.Should().BeTrue();
        missing.Value.Should().BeNull();
        boundary.IsOk.Should().BeTrue();
        boundary.Value.Should().Be(new Integer(long.MaxValue));
        overflow.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void DefaultRationalValue_ShouldFailWithoutMutatingTheTable() {
        var converter = new NumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1));
        using var table = new ConcurrentAutoTokenTable<RtnX>(
            category: LinearCategory.Instance,
            symbols: new List<IIntermediateSymbol>(),
            converter: converter
        );
        var x = new RealVar("invalid-rational");
        table.Add(x).IsOk.Should().BeTrue();
        LinearFunctionSymbolAdapter<RtnX> function = Abs(
            name: "invalid-rational-function",
            input: x,
            converter: converter
        );

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            converter: converter,
            fixedValues: new Dictionary<ISymbol, RtnX> { [x] = default }
        );

        result.IsFailed.Should().BeTrue();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().ContainSingle().Which.Variable.Should().BeSameAs(x);
    }

    [Fact]
    public void RationalIntermediateUnderflowOrNegativeDenominator_ShouldNotReturnWrongValues() {
        var converter = new NumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1));
        var x = new RealVar("rational-arithmetic");
        var underflow = new FloorFunction<RtnX>(
            new LinearPolynomial<RtnX>(
                [new LinearMonomial<RtnX>(new RtnX(double.Epsilon, 1e-200), x)],
                RtnX.Zero
            ),
            converter
        );
        var negativeDenominator = new FloorFunction<RtnX>(
            new LinearPolynomial<RtnX>([new LinearMonomial<RtnX>(RtnX.One, x)], RtnX.Zero),
            converter
        );

        underflow.TryEvaluate(new Dictionary<ISymbol, RtnX> {
            [x] = new RtnX(double.Epsilon, 1e-200),
        }).IsFailed.Should().BeTrue();
        negativeDenominator.TryEvaluate(new Dictionary<ISymbol, RtnX> {
            [x] = new RtnX(1, -2),
        }).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void RationalSumRoundedToZero_ShouldFailInsteadOfCachingZero() {
        var converter = new NumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1));
        var x = new RealVar("rational-cancellation");
        double next = double.BitIncrement(1);
        double epsilon = next - 1;
        var function = new FloorFunction<RtnX>(
            new LinearPolynomial<RtnX>(
                [new LinearMonomial<RtnX>(RtnX.One, x)],
                new RtnX(-next, 1)
            ),
            converter
        );

        Result<RtnX?, ErrorCode, Error<ErrorCode>> result = function.TryEvaluate(new Dictionary<ISymbol, RtnX> {
            [x] = new RtnX(1 + (2 * epsilon), next),
        });

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void RationalExtremumCrossProductOverflow_ShouldFailAndRollback() {
        var converter = new NumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1));
        using var table = new ConcurrentAutoTokenTable<RtnX>(
            category: LinearCategory.Instance,
            symbols: new List<IIntermediateSymbol>(),
            converter: converter
        );
        var x = new RealVar("rational-maximum-x");
        var y = new RealVar("rational-maximum-y");
        table.Add(x).IsOk.Should().BeTrue();
        table.Add(y).IsOk.Should().BeTrue();
        var source = new MaxFunction<RtnX>(
            [
                new LinearPolynomial<RtnX>([new LinearMonomial<RtnX>(RtnX.One, x)], RtnX.Zero),
                new LinearPolynomial<RtnX>([new LinearMonomial<RtnX>(RtnX.One, y)], RtnX.Zero),
            ],
            converter
        );
        var function = new LinearFunctionSymbolAdapter<RtnX>(source);
        var values = new Dictionary<ISymbol, RtnX> {
            [x] = new RtnX(double.MaxValue / 2, double.MaxValue),
            [y] = new RtnX(double.MaxValue, double.MaxValue),
        };

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            converter: converter,
            fixedValues: values
        );

        result.IsFailed.Should().BeTrue();
        source.TryEvaluate(values).IsFailed.Should().BeTrue();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().HaveCount(2);
    }

    [Fact]
    public void RationalExtremum_ShouldAllowEquivalentFractionsButRejectRoundedCrossProductTies() {
        var converter = new NumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1));
        var x = new RealVar("rational-tie-x");
        var y = new RealVar("rational-tie-y");
        var source = new MaxFunction<RtnX>(
            [
                new LinearPolynomial<RtnX>([new LinearMonomial<RtnX>(RtnX.One, x)], RtnX.Zero),
                new LinearPolynomial<RtnX>([new LinearMonomial<RtnX>(RtnX.One, y)], RtnX.Zero),
            ],
            converter
        );
        double next = double.BitIncrement(1);
        double epsilon = double.BitIncrement(1) - 1;

        Result<RtnX?, ErrorCode, Error<ErrorCode>> equal = source.TryEvaluate(new Dictionary<ISymbol, RtnX> {
            [x] = new RtnX(1, 2),
            [y] = new RtnX(2, 4),
        });
        Result<RtnX?, ErrorCode, Error<ErrorCode>> rounded = source.TryEvaluate(new Dictionary<ISymbol, RtnX> {
            [x] = new RtnX(1 + (2 * epsilon), next),
            [y] = new RtnX(next, 1),
        });

        equal.IsOk.Should().BeTrue();
        rounded.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void RationalRoundingIntermediateOverflow_ShouldNotReturnAnIncorrectInteger() {
        var converter = new NumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1));
        var x = new RealVar("rational-rounding-overflow");
        var function = new RoundingFunction<RtnX>(
            new LinearPolynomial<RtnX>([new LinearMonomial<RtnX>(RtnX.One, x)], RtnX.Zero),
            converter
        );

        Result<RtnX?, ErrorCode, Error<ErrorCode>> result = function.TryEvaluate(new Dictionary<ISymbol, RtnX> {
            [x] = new RtnX(1e308, 9e307),
        });

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void FloatingUnderflow_ShouldKeepExistingArithmeticSemantics() {
        using var table = new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var x = new RealVar("floating-underflow");
        table.Add(x).IsOk.Should().BeTrue();
        var function = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(
            polynomial: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(new Flt64(0.5), x)], Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            name: "floating-underflow"
        ));

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.Epsilon) }
        );

        result.IsOk.Should().BeTrue();
        table.CachedValue(function, (IReadOnlyList<Flt64>?)null).Should().Be(Flt64.Zero);
    }

    private static LinearFunctionSymbolAdapter<V> Abs<V>(string name, ISymbol input, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new(new AbsFunction<V>(
            polynomial: new LinearPolynomial<V>([new LinearMonomial<V>(converter.One, input)], converter.Zero),
            converter: converter,
            name: name
        ));

    private sealed class NumberConverter<V>(Func<Flt64, V> intoValue) : IIntoValue<V>
        where V : struct, IRealNumber<V> {
        public int Conversions { get; set; }
        public V Zero => NumericConstantsRegistry.For<V>().Zero;
        public V One => NumericConstantsRegistry.For<V>().One;
        public V IntoValue(Flt64 value) {
            Conversions++;
            return intoValue(value);
        }
        public Flt64 FromValue(V value) {
            Conversions++;
            return value.ToFlt64();
        }
    }

    private sealed class FatalEvaluationFunction : IMathFunctionSymbol<Integer> {
        public string Name { get; set; } = "fatal-evaluation";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables { get; } = [new RealVar("fatal-helper")];
        public IReadOnlyCollection<ISymbol> InputSymbols => Array.Empty<ISymbol>();
        public Integer? Evaluate(IReadOnlyDictionary<ISymbol, Integer> values) => null;
        public Result<Integer?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, Integer> values) =>
            new Fatal<Integer?, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.ApplicationStopped, "stopped"),
                new Err<ErrorCode>(ErrorCode.ApplicationFailed, "failed")
            );
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Integer> tokens) => tokens.Add(HelperVariables);
        public Try RegisterConstraints(IAbstractLinearMechanismModel<Integer> model) => Results.OkInstance;
    }
}
