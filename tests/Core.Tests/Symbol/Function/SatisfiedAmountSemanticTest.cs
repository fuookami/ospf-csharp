#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class SatisfiedAmountSemanticTest {
    [Theory]
    [InlineData(Comparison.LE, -0.5, 1)]
    [InlineData(Comparison.LE, 0, 1)]
    [InlineData(Comparison.LE, 0.125, -1)]
    [InlineData(Comparison.LE, 0.25, 0)]
    [InlineData(Comparison.GE, 0.5, 1)]
    [InlineData(Comparison.GE, 0, 1)]
    [InlineData(Comparison.GE, -0.125, -1)]
    [InlineData(Comparison.GE, -0.25, 0)]
    [InlineData(Comparison.EQ, 0, 1)]
    [InlineData(Comparison.EQ, 0.25, 1)]
    [InlineData(Comparison.EQ, -0.25, 1)]
    [InlineData(Comparison.EQ, 0.375, -1)]
    [InlineData(Comparison.EQ, -0.375, -1)]
    [InlineData(Comparison.EQ, 0.5, 0)]
    [InlineData(Comparison.EQ, -0.5, 0)]
    public async Task Evaluation_ShouldMatchRepresentableFlagBranches(Comparison sign, double input, int expected) {
        var x = new RealVar("satisfied-semantic-x");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, sign) },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(2),
            strictBoundary: new Flt64(0.5)
        );
        using LinearMechanismModel<Flt64> model = await Build(function, x);

        Flt64? value = function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(input) });
        if (expected < 0) {
            value.Should().BeNull();
        }
        else {
            value.Should().Be(new Flt64(expected));
        }
        foreach (int flag in new[] { 0, 1 }) {
            bool feasible = new[] { 0, 1 }.Any(side => Feasible(
                model: model,
                function: function,
                variable: x,
                input: input,
                flag: flag,
                side: side
            ));
            feasible.Should().Be(expected == flag, $"{sign}, input={input}, flag={flag}");
        }
    }

    [Fact]
    public async Task Amount_ShouldBeBooleanOnFeasibleModelsAndEnforceItsThreshold() {
        var x = new RealVar("satisfied-amount-result");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, Comparison.LE), Relation(x, Comparison.LE) },
            epsilon: new Flt64(0.25),
            amount: new UInt64(1),
            bigM: new Flt64(2)
        );
        using LinearMechanismModel<Flt64> model = await Build(function, x);

        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-1) }).Should().Be(Flt64.One);
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }).Should().Be(Flt64.Zero);
        function.ResultPolynomial.Constant.Should().Be(Flt64.One);
        function.ResultPolynomial.Monomials.Should().BeEmpty();
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1),
            [function.FlagVars[0]] = Flt64.One,
            [function.FlagVars[1]] = Flt64.One,
        };
        ((IMechanismModel<Flt64>)model).Constraints.Should().OnlyContain(row => row.IsTrue(
            model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList()) == true);
        values[x] = Flt64.One;
        values[function.FlagVars[0]] = Flt64.Zero;
        values[function.FlagVars[1]] = Flt64.Zero;
        ((IMechanismModel<Flt64>)model).Constraints.Should().Contain(row => row.IsTrue(
            model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList()) == false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Result_ShouldFlowThroughPreparedAdapterAndObjective(bool threshold) {
        var x = new RealVar("satisfied-nested-result");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, Comparison.LE), Relation(x, Comparison.LE) },
            epsilon: new Flt64(0.25),
            amount: threshold ? new UInt64(1) : null,
            bigM: new Flt64(2)
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) },
                Flt64.Zero
            ),
            name: "satisfied-objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await LinearMechanismModel<Flt64>.InvokeAsync(
            parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(-1) }
        );
        result.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = result.Value;
        Flt64 expected = new(threshold ? 1 : 2);
        model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null).Should().Be(expected);
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1),
            [function.FlagVars[0]] = Flt64.One,
            [function.FlagVars[1]] = Flt64.One,
        };
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        LinearSubObject<Flt64> objective = model.ObjectFunction.GetSubObjects<Flt64>()
            .OfType<LinearSubObject<Flt64>>().Single();
        objective.Evaluate(solution).Should().Be(expected);
        ((IMechanismModel<Flt64>)model).Constraints.Should().OnlyContain(row => row.IsTrue(solution) == true);
    }

    [Fact]
    public void Equality_DefaultAndExplicitBoundaries_ShouldReportTheGap() {
        var x = new RealVar("satisfied-eq-boundaries");
        var standard = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, Comparison.EQ) },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(2)
        );
        var wider = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, Comparison.EQ) },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(2),
            strictBoundary: new Flt64(0.75)
        );
        standard.StrictBoundary.Should().Be(new Flt64(0.5));
        standard.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.5) }).Should().Be(Flt64.Zero);
        wider.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.5) }).Should().BeNull();
        wider.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.75) }).Should().Be(Flt64.Zero);
        wider.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.NaN) }).Should().BeNull();
        wider.Evaluate(new Dictionary<ISymbol, Flt64>()).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AGapInAnyInput_ShouldMakeTheCountUnknown(bool threshold) {
        var x = new RealVar("satisfied-mixed-gap");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, Comparison.GE), Relation(x, Comparison.LE) },
            epsilon: new Flt64(0.25),
            amount: threshold ? new UInt64(1) : null,
            bigM: new Flt64(2)
        );
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.125) }).Should().BeNull();
    }

    [Fact]
    public async Task ZeroThreshold_ShouldStillEnforceEachInputIndicator() {
        var x = new RealVar("satisfied-zero-threshold");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, Comparison.LE) },
            epsilon: new Flt64(0.25),
            amount: UInt64.Zero,
            bigM: new Flt64(2)
        );
        using LinearMechanismModel<Flt64> model = await Build(function, x);
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }).Should().Be(Flt64.One);
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.125) }).Should().BeNull();
        function.ResultPolynomial.Constant.Should().Be(Flt64.One);
        Feasible(
            model: model,
            function: function,
            variable: x,
            input: 1,
            flag: 0,
            side: 0
        ).Should().BeTrue();
        Feasible(
            model: model,
            function: function,
            variable: x,
            input: 0.125,
            flag: 0,
            side: 0
        ).Should().BeFalse();
        Feasible(
            model: model,
            function: function,
            variable: x,
            input: 0.125,
            flag: 1,
            side: 0
        ).Should().BeFalse();
    }

    [Fact]
    public async Task UnreachableThreshold_ShouldLeaveTheModelInfeasible() {
        var x = new RealVar("satisfied-unreachable-threshold");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, Comparison.LE) },
            epsilon: new Flt64(0.25),
            amount: new UInt64(ulong.MaxValue),
            bigM: new Flt64(2)
        );
        using LinearMechanismModel<Flt64> model = await Build(function, x);
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-1) }).Should().Be(Flt64.Zero);
        function.ResultPolynomial.Constant.Should().Be(Flt64.One);
        Feasible(
            model: model,
            function: function,
            variable: x,
            input: -1,
            flag: 1,
            side: 0
        ).Should().BeFalse();
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.GE)]
    public async Task ZeroEpsilon_ShouldFailWithoutWritingAnOverlappingIndicator(Comparison sign) {
        var x = new RealVar("satisfied-zero-gap");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, sign) },
            epsilon: Flt64.Zero,
            bigM: new Flt64(2)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(function.FlagVars[0]).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }).Should().BeNull();
        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.125)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public async Task Equality_InvalidBoundary_ShouldFailBeforeWriting(double strict) {
        var x = new RealVar("satisfied-eq-gap");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, Comparison.EQ) },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(10),
            strictBoundary: new Flt64(strict)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task LaterEqualityWithInvalidGap_ShouldNotWriteEarlierRows() {
        var x = new RealVar("satisfied-later-equality");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(x, Comparison.LE), Relation(x, Comparison.EQ) },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(10),
            strictBoundary: new Flt64(0.25)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(
            relation: Relation(x, Comparison.LE),
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Fact]
    public async Task Amount_NestedSelfReference_ShouldFailEvenWithConstantResult() {
        var x = new RealVar("satisfied-constant-self-x");
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero
            ),
            name: "satisfied-constant-self-expression"
        );
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, nested) }, Flt64.Zero),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: Comparison.LE
                ),
            },
            epsilon: new Flt64(0.25),
            amount: new UInt64(1),
            bigM: new Flt64(4)
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.Tokens.Symbols.Add(adapter);
        nested.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, adapter));

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    private static async Task<LinearMechanismModel<Flt64>> Build(SatisfiedAmountFunction function, IVariableItem variable) {
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(variable).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        result.IsOk.Should().BeTrue();
        return result.Value;
    }

    private static bool Feasible(
        LinearMechanismModel<Flt64> model,
        SatisfiedAmountFunction function,
        IVariableItem variable,
        double input,
        int flag,
        int side) {
        var values = new Dictionary<IVariableItem, Flt64> {
            [variable] = new Flt64(input),
            [function.FlagVars[0]] = new Flt64(flag),
        };
        if (function.EqPositiveVars[0] is { } positive && function.EqNegativeVars[0] is { } negative) {
            values[positive] = new Flt64(flag == 0 ? side : 0);
            values[negative] = new Flt64(flag == 0 ? 1 - side : 0);
        }
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        return ((IMechanismModel<Flt64>)model).Constraints.All(row => row.IsTrue(solution) == true);
    }

    private static LinearInequality<Flt64> Relation(IVariableItem variable, Comparison sign) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
        Comparison: sign
    );
}
