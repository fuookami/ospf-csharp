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
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class IndicatorConstraintSafetyTest {
    [Theory]
    [InlineData(1e16, 0.125, 0)]
    [InlineData(10, 0.125, 1e16)]
    [InlineData(1e308, 1e308, 0)]
    public async Task Binaryzation_CollapsedOrOverflowingRows_ShouldFailBeforeWriting(
        double bigM,
        double tolerance,
        double constant) {
        var x = new RealVar("binary-unsafe");
        var function = new BinaryzationFunction(
            polynomial: Polynomial(x, constant),
            bigM: new Flt64(bigM),
            tolerance: new Flt64(tolerance)
        );
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        AddSentinel(model);

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Fact]
    public async Task Binaryzation_NestedConstant_ShouldBeCheckedAfterExpansion() {
        var x = new RealVar("binary-nested-unsafe");
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                new Flt64(1e16)
            ),
            name: "binary-nested-expression"
        );
        var function = new BinaryzationFunction(
            polynomial: Polynomial(nested),
            bigM: new Flt64(10),
            tolerance: new Flt64(0.125)
        );
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        AddSentinel(model);

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Binaryzation_MissingInputOrFlag_ShouldFailBeforeWriting(bool missingFlag) {
        var x = new RealVar("binary-missing");
        var function = new BinaryzationFunction(
            polynomial: Polynomial(x),
            bigM: new Flt64(2),
            tolerance: new Flt64(0.125)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(missingFlag ? x : function.ResultVar).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        AddSentinel(model);

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Fact]
    public async Task Binaryzation_NestedResultReference_ShouldFailEvenIfCoefficientRoundsAway() {
        var x = new RealVar("binary-self-x");
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero
            ),
            name: "binary-self-expression"
        );
        var function = new BinaryzationFunction(
            polynomial: Polynomial(nested),
            bigM: new Flt64(1e16),
            tolerance: new Flt64(4)
        );
        nested.AsMutable().AddMonomial(new LinearMonomial<Flt64>(new Flt64(0.125), function.ResultVar));
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        AddSentinel(model);

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Fact]
    public async Task Binaryzation_CyclicInput_ShouldFailBeforeWriting() {
        var x = new RealVar("binary-cycle-x");
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero
            ),
            name: "binary-cycle-expression"
        );
        var function = new BinaryzationFunction(
            polynomial: Polynomial(nested),
            bigM: new Flt64(2),
            tolerance: new Flt64(0.125)
        );
        nested.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, nested));
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        AddSentinel(model);

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Theory]
    [InlineData(-2, 0, true)]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(0.0625, 0, false)]
    [InlineData(0.0625, 1, false)]
    [InlineData(0.125, 0, false)]
    [InlineData(0.125, 1, true)]
    [InlineData(2, 1, true)]
    public async Task Binaryzation_RepresentableGap_ShouldRetainBothBranches(double input, double flag, bool expected) {
        var x = new RealVar("binary-valid-gap");
        var function = new BinaryzationFunction(
            polynomial: Polynomial(x),
            bigM: new Flt64(2),
            tolerance: new Flt64(0.125)
        );
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        function.RegisterConstraints(model).IsOk.Should().BeTrue();

        Feasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(input),
            [function.ResultVar] = new Flt64(flag),
        }).Should().Be(expected);
    }

    [Theory]
    [InlineData(Comparison.LE, false)]
    [InlineData(Comparison.LE, true)]
    [InlineData(Comparison.GE, false)]
    [InlineData(Comparison.GE, true)]
    public async Task SatisfiedAmount_ShouldRetainBoundEndpoints(Comparison sign, bool explicitBigM) {
        var x = new RealVar("satisfied-endpoint");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(Polynomial(x), sign) },
            epsilon: new Flt64(0.25),
            bigM: explicitBigM ? new Flt64(2) : null
        );
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        function.RegisterConstraints(model).IsOk.Should().BeTrue();
        double satisfied = sign == Comparison.LE ? -2 : 2;
        double violated = -satisfied;

        Feasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(satisfied),
            [function.FlagVars[0]] = Flt64.One,
        }).Should().BeTrue();
        Feasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(violated),
            [function.FlagVars[0]] = Flt64.Zero,
        }).Should().BeTrue();
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.GE)]
    public async Task SatisfiedAmount_NonzeroConstant_ShouldRetainBoundEndpoints(Comparison sign) {
        var x = new RealVar("satisfied-shifted-endpoint");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), Flt64.One).Value);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(Polynomial(x, 1), sign) },
            epsilon: new Flt64(0.25)
        );
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        function.RegisterConstraints(model).IsOk.Should().BeTrue();
        double satisfied = sign == Comparison.LE ? -3 : 1;
        double violated = sign == Comparison.LE ? 1 : -3;
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");

        Feasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(satisfied),
            [function.FlagVars[0]] = Flt64.One,
            [residual] = new Flt64(satisfied + 1),
        }).Should().BeTrue();
        Feasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(violated),
            [function.FlagVars[0]] = Flt64.Zero,
            [residual] = new Flt64(violated + 1),
        }).Should().BeTrue();
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.GE)]
    public async Task SatisfiedAmount_LargeConstant_ShouldPreserveGapWithResidual(Comparison sign) {
        var x = new RealVar("satisfied-collapsed-constant");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1e16 - 2), new Flt64(-1e16 + 2)).Value);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(Polynomial(x, 1e16), sign) },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(2)
        );
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        AddSentinel(model);

        function.RegisterConstraints(model).IsOk.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().HaveCount(4);
        double violated = sign == Comparison.LE ? 2 : -2;
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");
        Feasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1e16),
            [function.FlagVars[0]] = Flt64.One,
            [residual] = Flt64.Zero,
        }).Should().BeTrue();
        Feasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1e16 + violated),
            [function.FlagVars[0]] = Flt64.Zero,
            [residual] = new Flt64(violated),
        }).Should().BeTrue();
    }

    [Fact]
    public async Task SatisfiedAmount_LaterMissingToken_ShouldFailBeforeWritingEarlierRows() {
        var present = new RealVar("satisfied-present");
        var missing = new RealVar("satisfied-missing");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                Relation(Polynomial(present), Comparison.LE),
                Relation(Polynomial(missing), Comparison.LE),
            },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(2)
        );
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, present);
        AddSentinel(model);

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.EQ)]
    public async Task SatisfiedAmount_MissingHelper_ShouldFailBeforeWriting(Comparison sign) {
        var x = new RealVar("satisfied-missing-helper");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(Polynomial(x), sign) },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(2)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        if (sign == Comparison.EQ) {
            parent.Add(function.FlagVars[0]).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        AddSentinel(model);

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Fact]
    public async Task SatisfiedAmount_NestedHelperReference_ShouldFailBeforeWriting() {
        var x = new RealVar("satisfied-self-x");
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero
            ),
            name: "satisfied-self-expression"
        );
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(Polynomial(nested), Comparison.LE) },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(1e16)
        );
        nested.AsMutable().AddMonomial(new LinearMonomial<Flt64>(new Flt64(0.125), function.FlagVars[0]));
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        AddSentinel(model);

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Fact]
    public async Task SatisfiedAmount_Relaxation_ShouldRoundUpRatherThanLoseEpsilon() {
        var x = new RealVar("satisfied-round-up");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(Polynomial(x), Comparison.LE) },
            epsilon: new Flt64(double.Epsilon)
        );
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        function.RegisterConstraints(model).IsOk.Should().BeTrue();
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> rows = ((IMechanismModel<Flt64>)model).Constraints;
        rows[1].Lhs.Cast<ILinearCell<Flt64>>()
            .Single(cell => cell.Token.Variable == function.FlagVars[0]).Coefficient.ToDouble()
            .Should().Be(-global::System.Math.BitIncrement(1));
        Feasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1),
            [function.FlagVars[0]] = Flt64.One,
        }).Should().BeTrue();
    }

    [Fact]
    public async Task SatisfiedAmount_UnrepresentableRelaxation_ShouldFailBeforeWritingEarlierRows() {
        var x = new RealVar("satisfied-overflow");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1e308), new Flt64(1e308)).Value);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(Constant(Flt64.Zero), Comparison.LE), Relation(Polynomial(x), Comparison.LE) },
            epsilon: new Flt64(1e308)
        );
        using LinearMechanismModel<Flt64> model = await EmptyModel(function, x);
        AddSentinel(model);

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    private static async Task<LinearMechanismModel<Flt64>> EmptyModel(
        IMathFunctionSymbol<Flt64> function,
        IVariableItem input) {
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(input).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        return (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
    }

    private static void AddSentinel(LinearMechanismModel<Flt64> model) {
        model.AddConstraint(
            relation: Relation(Constant(Flt64.Zero), Comparison.LE),
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();
    }

    private static bool Feasible(LinearMechanismModel<Flt64> model, IReadOnlyDictionary<IVariableItem, Flt64> values) {
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        return ((IMechanismModel<Flt64>)model).Constraints.All(row => row.IsTrue(solution) == true);
    }

    private static LinearInequality<Flt64> Relation(LinearPolynomial<Flt64> input, Comparison sign) => new(
        Lhs: input,
        Rhs: Constant(Flt64.Zero),
        Comparison: sign
    );

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol, double constant = 0) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) },
        new Flt64(constant)
    );

    private static LinearPolynomial<Flt64> Constant(Flt64 value) => new(
        Array.Empty<LinearMonomial<Flt64>>(),
        value
    );
}
