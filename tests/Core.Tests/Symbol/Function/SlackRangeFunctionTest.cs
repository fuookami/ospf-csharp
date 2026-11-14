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
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>区间距离的泛型建模契约 / Generic range-distance modeling contracts.</summary>
public class SlackRangeFunctionTest {
    [Fact]
    public async Task Flt64_ShouldModelAllRegions() =>
        await VerifyRegions(IIntoValue<Flt64>.Identity);

    [Fact]
    public async Task Flt32_ShouldModelAllRegions() =>
        await VerifyRegions(new TestNumberConverter<Flt32>(value => value.ToFlt32()));

    [Fact]
    public async Task Integer_ShouldModelAllRegions() =>
        await VerifyRegions(new TestNumberConverter<Integer>(value => value.ToInt64()));

    [Fact]
    public async Task Decimal_ShouldModelAllRegions() =>
        await VerifyRegions(new TestNumberConverter<FltX>(value => value.ToFltX()));

    [Fact]
    public async Task Rational_ShouldModelAllRegions() =>
        await VerifyRegions(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));

    [Fact]
    public void Evaluation_ShouldPreserveIntegerAndDecimalPrecision() {
        var x = new RealVar("slack-precise");
        var integerConverter = new TestNumberConverter<Integer>(value => value.ToInt64());
        SlackRangeFunction<Integer> integer = Create(
            input: Polynomial(x, integerConverter),
            lower: new Integer(-1),
            upper: new Integer(2),
            converter: integerConverter
        );
        var decimalConverter = new TestNumberConverter<FltX>(value => value.ToFltX());
        SlackRangeFunction<FltX> decimalFunction = Create(
            input: Polynomial(x, decimalConverter),
            lower: new FltX(-1),
            upper: new FltX(2),
            converter: decimalConverter
        );
        int integerCalls = integerConverter.ConversionCalls;
        int decimalCalls = decimalConverter.ConversionCalls;

        integer.TryEvaluate(new Dictionary<ISymbol, Integer> { [x] = new Integer(9007199254740993L) })
            .Value.Should().Be(new Integer(9007199254740991L));
        decimalFunction.TryEvaluate(new Dictionary<ISymbol, FltX> { [x] = new FltX(9007199254740993.125m) })
            .Value.Should().Be(new FltX(9007199254740991.125m));
        integerConverter.ConversionCalls.Should().Be(integerCalls);
        decimalConverter.ConversionCalls.Should().Be(decimalCalls);
    }

    [Fact]
    public async Task DecimalConstant_ShouldNotGainRoundedResultBounds() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var expected = new FltX(9007199254740993.125m);
        SlackRangeFunction<FltX> range = Create(
            input: new LinearPolynomial<FltX>(Array.Empty<LinearMonomial<FltX>>(), expected),
            lower: converter.Zero,
            upper: converter.Zero,
            converter: converter,
            bigM: new FltX(20000000000000000m)
        );
        range.ResultVar.LowerBound!.Value.Unwrap().Should().Be(Flt64.Zero);
        range.ResultVar.Range.UpperBound!.Value.IsInfinityOrNegativeInfinity.Should().BeTrue();
        using var parent = new LinearMetaModel<FltX>(converter: converter);
        parent.Add(new LinearFunctionSymbolAdapter<FltX>(range)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<FltX>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<FltX> model = built.Value;
        var values = new Dictionary<IVariableItem, FltX> {
            [range.ResultVar] = expected,
            [range.SelectorVars[0]] = converter.Zero,
            [range.SelectorVars[1]] = converter.One,
            [range.SelectorVars[2]] = converter.Zero,
        };
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        ((IMechanismModel<FltX>)model).Constraints.Should().OnlyContain(row => row.IsTrue(solution) == true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedFunction_ShouldRegisterAndPrepareInBothFactories(bool quadratic) {
        var x = new RealVar("nested-range-x");
        x.Range.Geq(new Flt64(-4));
        x.Range.Leq(new Flt64(4));
        var absolute = new AbsFunction<Flt64>(
            polynomial: Polynomial(x, IIntoValue<Flt64>.Identity),
            converter: IIntoValue<Flt64>.Identity,
            name: "nested-range-abs"
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        SlackRangeFunction<Flt64> range = Create(
            input: Polynomial(inner, IIntoValue<Flt64>.Identity),
            lower: Flt64.One,
            upper: new Flt64(2),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(20),
            name: "nested-range"
        );
        var outer = new LinearFunctionSymbolAdapter<Flt64>(range);
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>()
            : new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(outer).IsOk.Should().BeTrue();
        parent.Add(inner).IsOk.Should().BeTrue();
        var fixedValues = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(-4) };
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-4),
            [absolute.ResultVar] = new Flt64(4),
            [absolute.PositiveVar] = Flt64.Zero,
            [absolute.NegativeVar] = new Flt64(4),
            [absolute.SignVar] = Flt64.Zero,
            [range.ResultVar] = new Flt64(2),
            [range.SelectorVars[0]] = Flt64.Zero,
            [range.SelectorVars[1]] = Flt64.One,
            [range.SelectorVars[2]] = Flt64.Zero,
        };

        if (quadratic) {
            Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
                await QuadraticMechanismModel<Flt64>.InvokeAsync(
                    metaModel: (QuadraticMetaModel<Flt64>)parent,
                    fixedVariables: fixedValues
                );
            built.IsOk.Should().BeTrue();
            using QuadraticMechanismModel<Flt64> model = built.Value;
            IReadOnlyList<IConstraint<Flt64, QuadraticCategory>> rows =
                ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints;
            rows.Should().HaveCount(11);
            model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(2));
            bool Feasible() => rows.All(row => row.IsTrue(
                model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList()) == true);
            Feasible().Should().BeTrue();
            values[range.ResultVar] = new Flt64(3);
            Feasible().Should().BeFalse();
        }
        else {
            Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
                await LinearMechanismModel<Flt64>.InvokeAsync(
                    metaModel: (LinearMetaModel<Flt64>)parent,
                    fixedVariables: fixedValues
                );
            built.IsOk.Should().BeTrue();
            using LinearMechanismModel<Flt64> model = built.Value;
            IReadOnlyList<IConstraint<Flt64, LinearCategory>> rows = ((IMechanismModel<Flt64>)model).Constraints;
            rows.Should().HaveCount(11);
            model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(2));
            bool Feasible() => rows.All(row => row.IsTrue(
                model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList()) == true);
            Feasible().Should().BeTrue();
            values[range.ResultVar] = new Flt64(3);
            Feasible().Should().BeFalse();
        }
    }

    [Fact]
    public async Task MissingNestedFunction_ShouldFailWithoutLeakingCallbacks() {
        var x = new RealVar("missing-range-x");
        var absolute = new AbsFunction<Flt64>(
            polynomial: Polynomial(x, IIntoValue<Flt64>.Identity),
            converter: IIntoValue<Flt64>.Identity
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        SlackRangeFunction<Flt64> range = Create(
            input: Polynomial(inner, IIntoValue<Flt64>.Identity),
            lower: Flt64.Zero,
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(range)).IsOk.Should().BeTrue();
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
        int tokens = parent.Tokens.Tokens.Count;

        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        parent.Tokens.Tokens.Should().HaveCount(tokens);
        parent.Tokens.Find(absolute.ResultVar).Should().BeNull();
    }

    [Fact]
    public async Task FailedFactory_ShouldNotLeakTokensOrCallbacks() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var x = new RealVar("range-failure-x");
        SlackRangeFunction<FltX> range = Create(
            input: Polynomial(x, converter),
            lower: new FltX(1),
            upper: new FltX(3),
            converter: converter
        );
        using var parent = new LinearMetaModel<FltX>(converter: converter);
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<FltX>(range)).IsOk.Should().BeTrue();
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
        int tokenCount = parent.Tokens.Tokens.Count;
        converter.RejectEncoding = true;

        (await LinearMechanismModel<FltX>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Tokens.Tokens.Should().HaveCount(tokenCount);
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        range.HelperVariables.Should().OnlyContain(variable => parent.Tokens.Find(variable) != null);
    }

    [Fact]
    public async Task OverflowInLaterRow_ShouldFailFactoryWithoutLeakingCallbacks() {
        SlackRangeFunction<Flt64> range = Create(
            input: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(1e308)),
            lower: Flt64.Zero,
            upper: Flt64.Zero,
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(1e308)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(range)).IsOk.Should().BeTrue();
        int[] callbacks = range.HelperVariables
            .Select(variable => parent.Tokens.Find(variable)!.RefreshCallbacks.Count).ToArray();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);

        built.IsFailed.Should().BeTrue();
        parent.Tokens.Tokens.Should().HaveCount(range.HelperVariables.Count);
        range.HelperVariables.Select(variable => parent.Tokens.Find(variable)!.RefreshCallbacks.Count)
            .Should().Equal(callbacks);
    }

    [Fact]
    public void Create_ShouldRejectInvalidBoundsInputAndBigM() {
        var x = new RealVar("range-invalid-x");
        LinearPolynomial<Flt64> valid = Polynomial(x, IIntoValue<Flt64>.Identity);
        LinearPolynomial<Flt64> nanCoefficient = new(
            new[] { new LinearMonomial<Flt64>(new Flt64(double.NaN), x) },
            Flt64.Zero
        );
        LinearPolynomial<Flt64> infinityConstant = new(
            Array.Empty<LinearMonomial<Flt64>>(),
            new Flt64(double.PositiveInfinity)
        );
        foreach ((LinearPolynomial<Flt64> input, Flt64 lower, Flt64 upper, Flt64? bigM) in new[] {
            (valid, new Flt64(2), Flt64.One, (Flt64?)null),
            (valid, new Flt64(double.NaN), Flt64.One, (Flt64?)null),
            (valid, Flt64.Zero, new Flt64(double.PositiveInfinity), (Flt64?)null),
            (valid, Flt64.Zero, Flt64.One, (Flt64?)Flt64.Zero),
            (valid, Flt64.Zero, Flt64.One, (Flt64?)new Flt64(-1)),
            (valid, Flt64.Zero, Flt64.One, (Flt64?)new Flt64(double.PositiveInfinity)),
            (nanCoefficient, Flt64.Zero, Flt64.One, (Flt64?)null),
            (infinityConstant, Flt64.Zero, Flt64.One, (Flt64?)null),
        }) {
            SlackRangeFunction<Flt64>.Create(
                input: input,
                lower: lower,
                upper: upper,
                converter: IIntoValue<Flt64>.Identity,
                bigM: bigM
            ).Should().BeOfType<Failed<SlackRangeFunction<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        }
        SlackRangeFunction<Flt64>.Create(
            input: null,
            lower: Flt64.Zero,
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed.Should().BeTrue();
        SlackRangeFunction<Flt64>.Create(
            input: valid,
            lower: Flt64.Zero,
            upper: Flt64.One,
            converter: null
        ).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Input_ShouldBeSnapshotAndRejectNonFiniteValues() {
        var x = new RealVar("range-snapshot-x");
        var y = new RealVar("range-snapshot-y");
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        SlackRangeFunction<Flt64> range = Create(
            input: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
            lower: Flt64.Zero,
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity
        );
        terms[0] = new LinearMonomial<Flt64>(Flt64.One, y);

        range.InputSymbols.Should().ContainSingle().Which.Should().BeSameAs(x);
        range.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(3) })
            .Value.Should().Be(new Flt64(2));
        range.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.NaN) })
            .IsFailed.Should().BeTrue();
        ((IList<LinearMonomial<Flt64>>)range.Input.Monomials).IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void CreateAndEvaluation_ShouldRejectIntegerOverflow() {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var x = new RealVar("range-overflow-x");
        var minCoefficient = new LinearPolynomial<Integer>(
            new[] { new LinearMonomial<Integer>(new Integer(long.MinValue), x) },
            converter.Zero
        );
        SlackRangeFunction<Integer>.Create(
            input: minCoefficient,
            lower: converter.Zero,
            upper: converter.One,
            converter: converter
        ).IsFailed.Should().BeTrue();
        SlackRangeFunction<Integer>.Create(
            input: new LinearPolynomial<Integer>(Array.Empty<LinearMonomial<Integer>>(), new Integer(-1)),
            lower: new Integer(long.MaxValue),
            upper: new Integer(long.MaxValue),
            converter: converter
        ).IsFailed.Should().BeTrue();

        SlackRangeFunction<Integer> range = Create(
            input: Polynomial(x, converter),
            lower: new Integer(-1),
            upper: converter.One,
            converter: converter
        );
        range.TryEvaluate(new Dictionary<ISymbol, Integer> { [x] = new Integer(long.MinValue) })
            .IsFailed.Should().BeTrue();
        range.TryEvaluate(new Dictionary<ISymbol, Integer>()).IsOk.Should().BeTrue();
        range.Evaluate(new Dictionary<ISymbol, Integer>()).Should().BeNull();
    }

    [Fact]
    public void Create_ShouldRejectRationalIntermediateOverflow() {
        var converter = new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1));
        SlackRangeFunction<RtnX>.Create(
            input: new LinearPolynomial<RtnX>(Array.Empty<LinearMonomial<RtnX>>(), new RtnX(1e200, 1e-100)),
            lower: new RtnX(1, 1e200),
            upper: new RtnX(1, 1e200),
            converter: converter
        ).IsFailed.Should().BeTrue();
    }

    private static async Task VerifyRegions<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V Number(double value) => converter.IntoValue(new Flt64(value));
        var x = new RealVar($"range-{typeof(V).Name}-x");
        x.Range.Geq(new Flt64(-6));
        x.Range.Leq(new Flt64(8));
        SlackRangeFunction<V> function = Create(
            input: Polynomial(x, converter),
            lower: Number(1),
            upper: Number(3),
            converter: converter,
            name: "range"
        );
        function.Name = "renamed-range";
        function.DisplayName = "Range";
        function.InputSymbols.Should().ContainSingle().Which.Should().BeSameAs(x);
        function.HelperVariables.Should().HaveCount(4);
        function.ResultPolynomial.Monomials.Single().Symbol.Should().BeSameAs(function.ResultVar);
        using var parent = new LinearMetaModel<V>(converter: converter);
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<V>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<V> model = built.Value;
        IReadOnlyList<IConstraint<V, LinearCategory>> rows = ((IMechanismModel<V>)model).Constraints;
        rows.Should().HaveCount(7);
        rows.Should().OnlyContain(row => row.Name.StartsWith("renamed-range"));
        foreach (double input in new[] { -6.0, 0, 1, 2, 3, 4, 8 }) {
            double expected = global::System.Math.Max(global::System.Math.Max(1 - input, input - 3), 0);
            function.TryEvaluate(new Dictionary<ISymbol, V> { [x] = Number(input) })
                .Value!.Value.Eq(Number(expected)).Should().BeTrue();
            bool Feasible(double output) => Enumerable.Range(0, 3).Any(selected => {
                var values = new Dictionary<IVariableItem, V> {
                    [x] = Number(input),
                    [function.ResultVar] = Number(output),
                };
                for (int i = 0; i < function.SelectorVars.Count; ++i) {
                    values[function.SelectorVars[i]] = Number(i == selected ? 1 : 0);
                }
                var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
                return rows.All(row => row.IsTrue(solution) == true);
            });
            Feasible(expected).Should().BeTrue($"input {input}");
            Feasible(expected + 1).Should().BeFalse($"input {input} without a minimizing objective");
        }
    }

    private static LinearPolynomial<V> Polynomial<V>(ISymbol symbol, IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        new(new[] { new LinearMonomial<V>(converter.One, symbol) }, converter.Zero);

    private static SlackRangeFunction<V> Create<V>(
        LinearPolynomial<V> input,
        V lower,
        V upper,
        IIntoValue<V> converter,
        V? bigM = null,
        string name = "slackRange")
        where V : struct, IRealNumber<V>, INumberField<V> {
        Result<SlackRangeFunction<V>, ErrorCode, Error<ErrorCode>> created = SlackRangeFunction<V>.Create(
            input: input,
            lower: lower,
            upper: upper,
            converter: converter,
            bigM: bigM,
            name: name
        );
        created.IsOk.Should().BeTrue();
        return created.Value;
    }
}
