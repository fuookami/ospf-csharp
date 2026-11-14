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

[Collection("LinearSymbolFlatten")]
public class InequalityNestedInputTest {
    [Theory]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.EQ)]
    [InlineData(Comparison.NE)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.GT)]
    public async Task NestedAbsolute_ShouldMatchComparisonAndMatrix(Comparison sign) {
        var x = new RealVar($"nested-comparison-{sign}-x");
        var absolute = new AbsFunction<Flt64>(
            polynomial: Polynomial(1, (2, x)),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10),
            name: "nested-absolute"
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        LinearExpressionSymbol wrapper = Expression(
            name: "wrapper",
            constant: 3,
            terms: [(2, inner)]
        );
        InequalityFunction<Flt64> function = ComparisonFunction(wrapper, sign);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        parent.Add(inner).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = built.Value;
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> rows = ((IMechanismModel<Flt64>)model).Constraints;
        IConstraint<Flt64, LinearCategory>[] comparisonRows = rows.Where(row => row.Name.StartsWith("nested-comparison")).ToArray();
        comparisonRows.Should().HaveCount(sign is Comparison.EQ or Comparison.NE ? 4 : 2);
        foreach (IConstraint<Flt64, LinearCategory> row in comparisonRows) {
            row.Lhs.Cast<ILinearCell<Flt64>>().Should().ContainSingle(cell => cell.Token.Variable == absolute.ResultVar)
                .Which.Coefficient.Abs().Should().Be(new Flt64(2));
            row.Lhs.Cast<ILinearCell<Flt64>>().Should().NotContain(cell => cell.Token.Variable == x);
        }
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        int resultIndex = model.Tokens.IndexOf(absolute.ResultVar)!.Value;
        for (int row = 0; row < comparisonRows.Length; ++row) {
            matrix.Constraints.Lhs[row].Should().ContainSingle(cell => cell.ColIndex == resultIndex)
                .Which.Coefficient.Abs().Should().Be(new Flt64(2));
        }

        foreach (double input in new[] { -1.5, -1.25, -1.0625, -1.0, -0.96875, -0.9375, -0.875, -0.5, 0.0 }) {
            double raw = 2 * input + 1;
            double abs = global::System.Math.Abs(raw);
            VAssert(function.Evaluate(new Dictionary<ISymbol, Flt64> { [wrapper] = new(2 * abs + 3) }), Expected(sign, 2 * abs - 2));
            foreach (int flag in new[] { 0, 1 }) {
                bool feasible = new[] { 0, 1 }.Any(side => {
                    var values = new Dictionary<IVariableItem, Flt64> {
                        [x] = new(input),
                        [absolute.ResultVar] = new(abs),
                        [absolute.PositiveVar] = new(global::System.Math.Max(raw, 0)),
                        [absolute.NegativeVar] = new(global::System.Math.Max(-raw, 0)),
                        [absolute.SignVar] = new(raw >= 0 ? 1 : 0),
                        [function.FlagVar] = new(flag),
                    };
                    if (function.SideVar is { } sideVar) {
                        values[sideVar] = new Flt64(side);
                    }
                    var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
                    return rows.All(row => row.IsTrue(solution) == true);
                });
                feasible.Should().Be(Expected(sign, 2 * abs - 2) == (flag == 1), $"{sign}, input={input}, flag={flag}");
            }
        }
    }

    [Fact]
    public async Task UnregisteredFunctionOrMissingResultToken_ShouldFailBeforeWritingRows() {
        var x = new RealVar("nested-missing-x");
        var absolute = new AbsFunction<Flt64>(
            polynomial: Polynomial(0, (1, x)),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        InequalityFunction<Flt64> function = ComparisonFunction(inner, Comparison.GT);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(absolute.ResultVar).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);

        using var incomplete = new LinearMetaModel<Flt64>();
        incomplete.Add(x).IsOk.Should().BeTrue();
        incomplete.Add(function.FlagVar).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(incomplete)).Value;
        model.Tokens.Symbols.Add(inner);
        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();

        using var missingInput = new LinearMetaModel<Flt64>();
        missingInput.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        missingInput.Add(inner).IsOk.Should().BeTrue();
        int helperCallbacks = missingInput.Tokens.Find(absolute.ResultVar)!.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(missingInput)).IsFailed.Should().BeTrue();
        missingInput.Tokens.Find(absolute.ResultVar)!.RefreshCallbacks.Should().HaveCount(helperCallbacks);
    }

    [Fact]
    public async Task NestedInput_ShouldRegisterOnQuadraticMechanism() {
        var x = new RealVar("nested-quadratic-x");
        var absolute = new AbsFunction<Flt64>(
            polynomial: Polynomial(1, (2, x)),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        InequalityFunction<Flt64> function = ComparisonFunction(inner, Comparison.GT);
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        parent.Add(inner).IsOk.Should().BeTrue();
        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> model = built.Value;
        IReadOnlyList<IConstraint<Flt64, QuadraticCategory>> rows =
            ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints;
        rows.Should().HaveCount(6);
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-3),
            [absolute.ResultVar] = new Flt64(5),
            [absolute.PositiveVar] = Flt64.Zero,
            [absolute.NegativeVar] = new Flt64(5),
            [absolute.SignVar] = Flt64.Zero,
            [function.FlagVar] = Flt64.Zero,
        };
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        rows.Should().OnlyContain(row => row.IsTrue(solution) == true);
        values[function.FlagVar] = Flt64.One;
        solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        rows.Should().Contain(row => row.IsTrue(solution) == false);
        using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
        matrix.Constraints.Lhs.Should().HaveCount(6);
    }

    [Fact]
    public async Task DefaultBigM_ShouldUseExpandedFiniteVariableBounds() {
        var x = new RealVar("nested-bounded-x");
        x.Range.Geq(new Flt64(-2));
        x.Range.Leq(new Flt64(2));
        LinearExpressionSymbol inner = Expression(
            name: "bounded",
            constant: 3,
            terms: [(2, x)]
        );
        var function = new InequalityFunction<Flt64>(
            lhs: Polynomial(-5, (1, inner)),
            rhs: Flt64.Zero,
            sign: Comparison.GT,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IConstraint<Flt64, LinearCategory>[] rows = ((IMechanismModel<Flt64>)model).Constraints.ToArray();
        rows[0].Lhs.Cast<ILinearCell<Flt64>>().Single(cell => cell.Token.Variable == function.FlagVar)
            .Coefficient.Should().Be(new Flt64(-6));
        rows[1].Lhs.Cast<ILinearCell<Flt64>>().Single(cell => cell.Token.Variable == function.FlagVar)
            .Coefficient.Should().Be(new Flt64(-6.5));
    }

    [Fact]
    public async Task NestedStructuralResults_ShouldRetainRepresentativeNumericTypes() {
        await VerifyGeneric(new TestNumberConverter<Integer>(value => value.ToInt64()));
        await VerifyGeneric(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyGeneric(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await VerifyGeneric(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    [Fact]
    public async Task ChangedNestedExpression_ShouldBeRecheckedWithoutChangingExistingRows() {
        var x = new RealVar("nested-mutable-x");
        LinearExpressionSymbol inner = Expression(
            name: "mutable",
            constant: 0,
            terms: [(1, x)]
        );
        InequalityFunction<Flt64> function = ComparisonFunction(inner, Comparison.GT);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> rows = ((IMechanismModel<Flt64>)model).Constraints;
        Flt64[] originalRhs = rows.Select(row => row.Rhs).ToArray();
        inner.AsMutable().SetConstant(Flt64.One);
        using (LinearMechanismModel<Flt64> updated = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value) {
            ((IMechanismModel<Flt64>)updated).Constraints[0].Rhs.Should().Be(new Flt64(4));
            rows.Select(row => row.Rhs).Should().Equal(originalRhs);
        }
        inner.AsMutable().SetConstant(new Flt64(1e16));
        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        rows.Should().HaveCount(2);
        rows.Select(row => row.Rhs).Should().Equal(originalRhs);
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        inner.AsMutable().SetConstant(Flt64.Zero);
        inner.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, inner));
        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task NestedIntegerOverflow_ShouldFailBeforeAuxiliaryTokensAndRows() {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var x = new RealVar("nested-overflow-x");
        var inner = new LinearFunctionSymbolAdapter<Integer>(new StructuralFunction<Integer>(
            new LinearPolynomial<Integer>(
                new[] { new LinearMonomial<Integer>(new Integer(long.MaxValue), x) }, Integer.Zero)
        ));
        var function = new InequalityFunction<Integer>(
            lhs: new LinearPolynomial<Integer>(
                new[] { new LinearMonomial<Integer>(new Integer(2), inner) }, Integer.Zero),
            rhs: Integer.Zero,
            sign: Comparison.GT,
            converter: converter,
            bigM: new Integer(10),
            strictBoundary: Integer.One
        );
        using var tokens = new ManualTokenList<Integer>();
        function.RegisterAuxiliaryTokens(tokens).IsFailed.Should().BeTrue();
        tokens.Tokens.Should().BeEmpty();
        using var parent = new LinearMetaModel<Integer>(converter: converter);
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(function.FlagVar).IsOk.Should().BeTrue();
        using LinearMechanismModel<Integer> model = (await LinearMechanismModel<Integer>.InvokeAsync(parent)).Value;
        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Integer>)model).Constraints.Should().BeEmpty();

        var repeated = new LinearFunctionSymbolAdapter<Integer>(new StructuralFunction<Integer>(
            new LinearPolynomial<Integer>(
                new[] {
                    new LinearMonomial<Integer>(new Integer(long.MaxValue), x),
                    new LinearMonomial<Integer>(Integer.One, x),
                },
                Integer.Zero)
        ));
        var mergedOverflow = new InequalityFunction<Integer>(
            lhs: new LinearPolynomial<Integer>(new[] { new LinearMonomial<Integer>(Integer.One, repeated) }, Integer.Zero),
            rhs: Integer.Zero,
            sign: Comparison.GT,
            converter: converter,
            bigM: new Integer(10),
            strictBoundary: Integer.One
        );
        mergedOverflow.RegisterAuxiliaryTokens(tokens).IsFailed.Should().BeTrue();
        tokens.Tokens.Should().BeEmpty();
    }

    [Fact]
    public void CycleUnknownFunctionAndUnderflow_ShouldFailBeforeAuxiliaryTokens() {
        LinearExpressionSymbol cycle = Expression("cycle", 0);
        cycle.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, cycle));
        var unknown = new LinearFunctionSymbolAdapter<Flt64>(new UnknownFunctionStub());
        foreach (ISymbol input in new ISymbol[] { cycle, unknown }) {
            InequalityFunction<Flt64> function = ComparisonFunction(input, Comparison.GT);
            using var tokens = new ManualTokenList<Flt64>();
            function.RegisterAuxiliaryTokens(tokens).IsFailed.Should().BeTrue();
            tokens.Tokens.Should().BeEmpty();
            using var parent = new LinearMetaModel<Flt64>();
            parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsFailed.Should().BeTrue();
            parent.Tokens.Symbols.Should().BeEmpty();
            parent.Tokens.Tokens.Should().BeEmpty();
        }
        var x = new RealVar("nested-underflow-x");
        LinearExpressionSymbol tiny = Expression(
            name: "tiny",
            constant: 0,
            terms: [(double.Epsilon, x)]
        );
        var underflow = new InequalityFunction<Flt64>(
            lhs: Polynomial(0, (0.5, tiny)),
            rhs: Flt64.Zero,
            sign: Comparison.GT,
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10),
            strictBoundary: new Flt64(0.5)
        );
        using var underflowTokens = new ManualTokenList<Flt64>();
        underflow.RegisterAuxiliaryTokens(underflowTokens).IsFailed.Should().BeTrue();
        underflowTokens.Tokens.Should().BeEmpty();
    }

    private static async Task VerifyGeneric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V Number(double value) => converter.IntoValue(new Flt64(value));
        var x = new RealVar($"nested-{typeof(V).Name}-x");
        var inner = new LinearFunctionSymbolAdapter<V>(new StructuralFunction<V>(
            new LinearPolynomial<V>(new[] { new LinearMonomial<V>(Number(2), x) }, Number(3))
        ));
        var function = new InequalityFunction<V>(
            lhs: new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, inner) }, Number(-3)),
            rhs: converter.Zero,
            sign: Comparison.GE,
            converter: converter,
            bigM: Number(10),
            tolerance: converter.One
        );
        using var parent = new LinearMetaModel<V>(converter: converter);
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk.Should().BeTrue();
        parent.Add(inner).IsOk.Should().BeTrue();
        using LinearMechanismModel<V> model = (await LinearMechanismModel<V>.InvokeAsync(parent)).Value;
        IReadOnlyList<IConstraint<V, LinearCategory>> rows = ((IMechanismModel<V>)model).Constraints;
        rows.Should().HaveCount(2);
        foreach (IConstraint<V, LinearCategory> row in rows) {
            row.Lhs.Cast<ILinearCell<V>>().Single(cell => cell.Token.Variable == x)
                .Coefficient.Abs().Eq(Number(2)).Should().BeTrue();
        }
        foreach ((double input, bool expected) in new[] { (-1.0, false), (0.0, true), (1.0, true) }) {
            function.Evaluate(new Dictionary<ISymbol, V> { [inner] = Number(2 * input + 3) })!
                .Value.Eq(expected ? converter.One : converter.Zero).Should().BeTrue();
            var values = new Dictionary<IVariableItem, V> {
                [x] = Number(input),
                [function.FlagVar] = expected ? converter.One : converter.Zero,
            };
            var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
            rows.Should().OnlyContain(row => row.IsTrue(solution) == true);
            values[function.FlagVar] = expected ? converter.Zero : converter.One;
            solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
            rows.Should().Contain(row => row.IsTrue(solution) == false);
        }
    }

    private static InequalityFunction<Flt64> ComparisonFunction(ISymbol input, Comparison sign) => new(
        lhs: Polynomial(-5, (1, input)),
        rhs: Flt64.Zero,
        sign: sign,
        converter: IIntoValue<Flt64>.Identity,
        bigM: new Flt64(10),
        tolerance: new Flt64(0.125),
        strictBoundary: new Flt64(0.5),
        name: "nested-comparison"
    );

    private static LinearExpressionSymbol Expression(string name, double constant, params (double Coefficient, ISymbol Symbol)[] terms) =>
        new(
            polynomial: new MutableLinearPolynomial<Flt64>(
                terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)),
                new Flt64(constant)),
            name: name
        );

    private static LinearPolynomial<Flt64> Polynomial(double constant, params (double Coefficient, ISymbol Symbol)[] terms) =>
        new(terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)).ToList(), new Flt64(constant));

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

    private sealed class StructuralFunction<V>(LinearPolynomial<V> polynomial) : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
        where V : struct, IRealNumber<V>, INumberField<V> {
        public string Name { get; set; } = "structural";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public IReadOnlyCollection<ISymbol> InputSymbols => polynomial.Monomials.Select(term => term.Symbol).ToArray();
        public LinearPolynomial<V> ResultPolynomial => polynomial;
        public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) {
            V result = polynomial.Constant;
            foreach (LinearMonomial<V> term in polynomial.Monomials) {
                if (!values.TryGetValue(term.Symbol, out V value)) {
                    return null;
                }
                result = result.Plus(term.Coefficient.Times(value));
            }
            return result;
        }
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => Results.Ok(Results.SuccessInstance);
        public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) => Results.Ok(Results.SuccessInstance);
    }
}
