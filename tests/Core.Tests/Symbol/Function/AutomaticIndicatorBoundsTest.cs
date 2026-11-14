#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
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
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class AutomaticIndicatorBoundsTest {
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    public async Task UnboundedOrOverflowingLaterInput_ShouldNotWriteAnyRows(int kind, bool overflow) {
        var x = new RealVar($"unbounded-indicator-{kind}");
        x.Range.SetRange(overflow
            ? ValueRange<Flt64>.Of(new Flt64(1e308), new Flt64(1e308)).Value
            : ValueRange<Flt64>.Full());
        LinearPolynomial<Flt64> zero = Constant(Flt64.Zero);
        LinearPolynomial<Flt64> input = new(
            new[] { new LinearMonomial<Flt64>(new Flt64(overflow ? 2 : 1), x) },
            Flt64.Zero
        );
        var finite = new LinearInequality<Flt64>(
            Lhs: zero,
            Rhs: zero,
            Comparison: Comparison.LE
        );
        var unbounded = new LinearInequality<Flt64>(
            Lhs: input,
            Rhs: zero,
            Comparison: Comparison.LE
        );
        IMathFunctionSymbol<Flt64> function = kind switch {
            0 => new AndFunction(new[] { zero, input }),
            1 => new OrFunction(new[] { zero, input }),
            2 => new NotFunction(input),
            3 => new XorFunction(new[] { zero, input }),
            4 => new OneOfFunction(new[] { zero, input }),
            5 => new SameAsFunction(new[] { finite, unbounded }, new Flt64(0.25)),
            _ => new SatisfiedAmountFunction(new[] { finite, unbounded }, new Flt64(0.25)),
        };
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(
            relation: finite,
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> rows = ((IMechanismModel<Flt64>)model).Constraints;
        rows.Should().ContainSingle();

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Comparison_UnboundedInput_ShouldFailBeforeTokensAndRows() {
        var x = new RealVar("comparison-unbounded");
        x.Range.SetRange(ValueRange<Flt64>.Full());
        var function = new InequalityFunction<Flt64>(
            lhs: Polynomial(x),
            rhs: Flt64.Zero,
            sign: Comparison.GT,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        );
        using var tokens = new ManualTokenList<Flt64>();
        function.RegisterAuxiliaryTokens(tokens).IsFailed.Should().BeTrue();
        tokens.Tokens.Should().BeEmpty();
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(function.FlagVar).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task Comparison_FiniteMagnitudeNotRepresentable_ShouldFailBeforeTokensAndRows() {
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var x = new IntVar("comparison-integer-minimum");
        x.Range.Eq(new Integer(long.MinValue));
        var input = new LinearPolynomial<Integer>(
            new[] { new LinearMonomial<Integer>(Integer.One, x) },
            Integer.Zero
        );
        BigMValues.FiniteBounds(input, converter).Should().NotBeNull();
        BigMValues.InferredBigM(input, converter).Should().BeNull();
        var function = new InequalityFunction<Integer>(
            lhs: input,
            rhs: Integer.Zero,
            sign: Comparison.GT,
            converter: converter,
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
    }

    [Fact]
    public async Task Comparison_ShouldRecheckBoundsAfterAuxiliaryRegistration() {
        var x = new RealVar("comparison-changed-range");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var function = new InequalityFunction<Flt64>(
            lhs: Polynomial(x),
            rhs: Flt64.Zero,
            sign: Comparison.GT,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        );
        using var tokens = new ManualTokenList<Flt64>();
        function.RegisterAuxiliaryTokens(tokens).IsOk.Should().BeTrue();
        x.Range.SetRange(ValueRange<Flt64>.Full());
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(function.FlagVar).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task ExplicitBigM_RemainsAvailableForUnboundedInputs() {
        var x = new RealVar("explicit-indicator-bound");
        x.Range.SetRange(ValueRange<Flt64>.Full());
        LinearPolynomial<Flt64> zero = Constant(Flt64.Zero);
        LinearPolynomial<Flt64> input = Polynomial(x);
        var inequality = new LinearInequality<Flt64>(
            Lhs: input,
            Rhs: zero,
            Comparison: Comparison.LE
        );
        var m = new Flt64(10);
        IMathFunctionSymbol<Flt64>[] functions = {
            new BinaryzationFunction(input, m),
            new AndFunction(new[] { zero, input }, m),
            new OrFunction(new[] { zero, input }, m),
            new NotFunction(input, m),
            new XorFunction(new[] { zero, input }, m),
            new OneOfFunction(new[] { zero, input }, m),
            new SameAsFunction(
                inequalities: new[] { inequality },
                epsilon: new Flt64(0.25),
                bigM: m
            ),
            new SatisfiedAmountFunction(
                inequalities: new[] { inequality },
                epsilon: new Flt64(0.25),
                bigM: m
            ),
            new InequalityFunction<Flt64>(
                lhs: input,
                rhs: Flt64.Zero,
                sign: Comparison.GT,
                converter: IIntoValue<Flt64>.Identity,
                bigM: m,
                strictBoundary: new Flt64(0.5)
            ),
        };
        foreach (IMathFunctionSymbol<Flt64> function in functions) {
            using var parent = new LinearMetaModel<Flt64>();
            parent.Add(x).IsOk.Should().BeTrue();
            foreach (IVariableItem helper in function.HelperVariables) {
                parent.Add(helper).IsOk.Should().BeTrue();
            }
            using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            function.RegisterConstraints(model).IsOk.Should().BeTrue(because: function.GetType().Name);
            ((IMechanismModel<Flt64>)model).Constraints.Should().NotBeEmpty();
        }
    }

    [Fact]
    public async Task Not_AutomaticBigM_ShouldUseCurrentRangeAtRegistration() {
        var x = new RealVar("not-changing-range");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var function = new NotFunction(
            polynomial: Polynomial(x),
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(0.5)
        );
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-100), new Flt64(100)).Value);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(100),
            [function.IndicatorVar] = Flt64.One,
            [function.SideVar] = Flt64.One,
            [function.ResultVar] = Flt64.Zero,
        };
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        ((IMechanismModel<Flt64>)model).Constraints.Should().OnlyContain(row => row.IsTrue(solution) == true);
    }

    [Fact]
    public async Task Not_BinaryFastPath_ShouldKeepItsInputSnapshot() {
        var first = new BinVar("not-original");
        var second = new BinVar("not-replacement");
        var monomials = new List<LinearMonomial<Flt64>> { new(Flt64.One, first) };
        var function = new NotFunction(new LinearPolynomial<Flt64>(monomials, Flt64.Zero));
        monomials[0] = new LinearMonomial<Flt64>(Flt64.One, second);

        function.InputSymbols.Should().ContainSingle().Which.Should().BeSameAs(first);
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [first] = Flt64.One }).Should().Be(Flt64.Zero);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(first).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var values = new Dictionary<IVariableItem, Flt64> {
            [first] = Flt64.One,
            [function.ResultVar] = Flt64.Zero,
        };
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        ((IMechanismModel<Flt64>)model).Constraints.Should().OnlyContain(row => row.IsTrue(solution) == true);
    }

    private static LinearPolynomial<Flt64> Polynomial(IVariableItem variable) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
        Flt64.Zero
    );

    private static LinearPolynomial<Flt64> Constant(Flt64 value) => new(
        Array.Empty<LinearMonomial<Flt64>>(),
        value
    );
}
