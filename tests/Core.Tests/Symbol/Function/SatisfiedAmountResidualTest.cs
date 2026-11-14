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
using Fuookami.Ospf.Math.Algebra.ValueRange;
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
using MathUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class SatisfiedAmountResidualTest {
    [Theory]
    [InlineData(0.1, 0.15, 0.25, 1, false, true)]
    [InlineData(0.1, 0.15, 0.25, 1, true, true)]
    [InlineData(-0.1, -0.15, -0.25, 1, false, true)]
    [InlineData(-0.1, -0.15, -0.25, 1, true, true)]
    [InlineData(0.3, 0.2, 0.5, 0, false, true)]
    [InlineData(0.3, 0.2, 0.5, 0, true, true)]
    [InlineData(0.3, -0.04999999999999993, 0.25000000000000006, -1, false, true)]
    [InlineData(-0.3, 0.04999999999999993, -0.25000000000000006, -1, true, true)]
    [InlineData(0.1, 0.15, 0.25, 1, false, false)]
    [InlineData(0.3, 0.2, 0.5, 0, false, false)]
    [InlineData(-0.1, -0.15, -0.25, 1, true, false)]
    [InlineData(0.3, -0.04999999999999993, 0.25000000000000006, -1, true, false)]
    public async Task EqualityShiftedBoundaries_ShouldSurviveExactRowsAndDump(
        double offset,
        double xValue,
        double difference,
        int expected,
        bool nested,
        bool explicitBigM) {
        var x = new RealVar("equality-residual-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        ISymbol input = x;
        Flt64 constant = new(offset);
        if (nested) {
            input = new LinearExpressionSymbol(
                polynomial: new MutableLinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                    constant
                ),
                name: "equality-residual-nested"
            );
            constant = Flt64.Zero;
        }
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(
                input: input,
                sign: Comparison.EQ,
                constant: constant
            ) },
            epsilon: new Flt64(0.25),
            bigM: explicitBigM ? new Flt64(2) : null,
            strictBoundary: new Flt64(0.5)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue($"model construction: {built}");
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Exact(xValue).Add(Exact(offset)).CompareTo(Exact(difference)).Should().Be(0);
        function.Evaluate(new Dictionary<ISymbol, Flt64> {
            [input] = new Flt64(nested ? difference : xValue),
        }).Should().Be(expected < 0 ? null : new Flt64(expected));
        BinVar[] sides = function.HelperVariables.OfType<BinVar>()
            .Where(variable => variable.Key != function.FlagVars[0].Key)
            .ToArray();
        foreach (int flag in new[] { 0, 1 }) {
            bool mechanismFeasible = false;
            bool matrixFeasible = false;
            for (int mask = 0; mask < 1 << sides.Length; ++mask) {
                var values = new Dictionary<IVariableItem, Flt64> {
                    [x] = new Flt64(xValue),
                    [function.FlagVars[0]] = new Flt64(flag),
                };
                for (int side = 0; side < sides.Length; ++side) {
                    values[sides[side]] = new Flt64((mask >> side) & 1);
                }
                if (function.ResidualVars[0] is { } residual) {
                    values[residual] = new Flt64(difference);
                }
                mechanismFeasible |= ExactFeasible(model, values);
                matrixFeasible |= ExactFeasible(matrix, values);
            }
            mechanismFeasible.Should().Be(expected == flag, $"offset={offset}, difference={difference}, flag={flag}");
            matrixFeasible.Should().Be(expected == flag, $"matrix: offset={offset}, difference={difference}, flag={flag}");
        }
    }

    [Theory]
    [InlineData(false, 0.5)]
    [InlineData(true, 0.5)]
    [InlineData(true, 0.25000000000000006)]
    public async Task EqualityDirectInput_ShouldUseExactlyOneBranch(bool explicitBigM, double strict) {
        var x = new RealVar("equality-direct-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(
                input: x,
                sign: Comparison.EQ,
                constant: Flt64.Zero
            ) },
            epsilon: new Flt64(0.25),
            bigM: explicitBigM ? new Flt64(2) : null,
            strictBoundary: new Flt64(strict)
        );
        function.ResidualVars[0].Should().BeNull();
        BinVar positive = function.EqPositiveVars[0] ?? throw new InvalidOperationException("Missing positive flag.");
        BinVar negative = function.EqNegativeVars[0] ?? throw new InvalidOperationException("Missing negative flag.");
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        matrix.Constraints.Size.Should().Be(5);

        foreach (double value in new[] { -2, -strict, -0.375, -0.25, 0, 0.25, 0.375, strict, 2 }) {
            foreach (int flag in new[] { 0, 1 }) {
                foreach (int p in new[] { 0, 1 }) {
                    foreach (int n in new[] { 0, 1 }) {
                        var values = new Dictionary<IVariableItem, Flt64> {
                            [x] = new Flt64(value),
                            [function.FlagVars[0]] = new Flt64(flag),
                            [positive] = new Flt64(p),
                            [negative] = new Flt64(n),
                        };
                        bool expected = flag == 1 && p == 0 && n == 0 && global::System.Math.Abs(value) <= 0.25 ||
                            flag == 0 && p == 1 && n == 0 && value >= strict ||
                            flag == 0 && p == 0 && n == 1 && value <= -strict;
                        ExactFeasible(model, values).Should().Be(expected, $"d={value}, u={flag}, p={p}, n={n}");
                        ExactFeasible(matrix, values).Should().Be(expected, $"matrix: d={value}, u={flag}, p={p}, n={n}");
                    }
                }
            }
        }
    }

    [Fact]
    public async Task EqualityRelaxation_ShouldRoundUpAndRetainBothBounds() {
        var x = new RealVar("equality-round-up-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(
                input: x,
                sign: Comparison.EQ,
                constant: Flt64.Zero
            ) },
            epsilon: new Flt64(double.Epsilon),
            strictBoundary: new Flt64(2 * double.Epsilon)
        );
        BinVar positive = function.EqPositiveVars[0] ?? throw new InvalidOperationException("Missing positive flag.");
        BinVar negative = function.EqNegativeVars[0] ?? throw new InvalidOperationException("Missing negative flag.");
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> rows = ((IMechanismModel<Flt64>)model).Constraints;
        rows[2].Lhs.Cast<ILinearCell<Flt64>>()
            .Single(cell => cell.Token.Variable == function.FlagVars[0]).Coefficient.ToDouble()
            .Should().Be(global::System.Math.BitIncrement(1));

        foreach ((double value, int flag, int p, int n) in new[] {
            (-1.0, 0, 0, 1),
            (-2 * double.Epsilon, 0, 0, 1),
            (-double.Epsilon, 1, 0, 0),
            (double.Epsilon, 1, 0, 0),
            (2 * double.Epsilon, 0, 1, 0),
            (1.0, 0, 1, 0),
        }) {
            var values = new Dictionary<IVariableItem, Flt64> {
                [x] = new Flt64(value),
                [function.FlagVars[0]] = new Flt64(flag),
                [positive] = new Flt64(p),
                [negative] = new Flt64(n),
            };
            ExactFeasible(model, values).Should().BeTrue($"d={value}");
            ExactFeasible(matrix, values).Should().BeTrue($"matrix: d={value}");
        }
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.EQ)]
    public async Task NestedAutomaticBound_ShouldFollowExpandedInputAndCurrentRange(Comparison sign) {
        var x = new RealVar("nested-automatic-bound-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var inner = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) },
                new Flt64(0.25)
            ),
            name: "nested-automatic-bound-inner"
        );
        var outer = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, inner) },
                new Flt64(0.25)
            ),
            name: "nested-automatic-bound-outer"
        );
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(
                input: outer,
                sign: sign,
                constant: Flt64.Zero
            ) },
            epsilon: new Flt64(0.25),
            strictBoundary: new Flt64(0.5)
        );
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-10), new Flt64(10)).Value);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue($"model construction: {built}");
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        IVariableItem boundFlag = sign == Comparison.EQ
            ? function.EqPositiveVars[0] ?? throw new InvalidOperationException("Missing positive flag.")
            : function.FlagVars[0];
        Flt64 coefficient = ((IMechanismModel<Flt64>)model).Constraints[0].Lhs.Cast<ILinearCell<Flt64>>()
            .Single(cell => cell.Token.Variable.Key == boundFlag.Key).Coefficient.Abs();
        coefficient.ToDouble().Should().BeGreaterThanOrEqualTo(20.5);

        foreach (double xValue in new[] { -10.0, 10.0 }) {
            double difference = (2 * xValue) + 0.5;
            bool satisfied = sign == Comparison.LE ? difference < 0 : sign == Comparison.GE && difference > 0;
            var values = new Dictionary<IVariableItem, Flt64> {
                [x] = new Flt64(xValue),
                [residual] = new Flt64(difference),
                [function.FlagVars[0]] = satisfied ? Flt64.One : Flt64.Zero,
            };
            if (function.EqPositiveVars[0] is { } positive && function.EqNegativeVars[0] is { } negative) {
                values[positive] = difference > 0 ? Flt64.One : Flt64.Zero;
                values[negative] = difference < 0 ? Flt64.One : Flt64.Zero;
            }
            ExactFeasible(model, values).Should().BeTrue($"{sign}, x={xValue}");
            ExactFeasible(matrix, values).Should().BeTrue($"matrix: {sign}, x={xValue}");
        }
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.EQ)]
    public async Task RangeChangeAfterModelBuild_ShouldRejectStaleAutomaticBound(Comparison sign) {
        var x = new RealVar("stale-nested-bound-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) },
                new Flt64(0.5)
            ),
            name: "stale-nested-bound"
        );
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(input: nested, sign: sign, constant: Flt64.Zero) },
            epsilon: new Flt64(0.25)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> oldModel = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-10), new Flt64(10)).Value);
        oldModel.ToLinearTriadModel().IsFailed.Should().BeTrue();
        using LinearMechanismModel<Flt64> rebuilt = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel newMatrix = rebuilt.ToLinearTriadModel().Value;
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(10),
            [residual] = new Flt64(20.5),
            [function.FlagVars[0]] = sign == Comparison.GE ? Flt64.One : Flt64.Zero,
        };
        if (function.EqPositiveVars[0] is { } positive && function.EqNegativeVars[0] is { } negative) {
            values[positive] = Flt64.One;
            values[negative] = Flt64.Zero;
        }

        ExactFeasible(oldModel, values).Should().BeFalse();
        ExactFeasible(rebuilt, values).Should().BeTrue();
        ExactFeasible(newMatrix, values).Should().BeTrue();
    }

    [Theory]
    [InlineData(Comparison.LE, false)]
    [InlineData(Comparison.GE, false)]
    [InlineData(Comparison.EQ, false)]
    [InlineData(Comparison.LE, true)]
    [InlineData(Comparison.GE, true)]
    [InlineData(Comparison.EQ, true)]
    public async Task NestedDeclaredRange_ShouldNotReplaceExpandedVariableBounds(Comparison sign, bool unbounded) {
        var x = new RealVar("declared-range-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var y = new RealVar("declared-range-y");
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var nested = new RangedLinearSymbol(x);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                Relation(input: y, sign: Comparison.LE, constant: Flt64.Zero),
                Relation(input: nested, sign: sign, constant: Flt64.Zero),
            },
            epsilon: new Flt64(0.25)
        );
        x.Range.SetRange(unbounded
            ? ValueRange<Flt64>.Full()
            : ValueRange<Flt64>.Of(new Flt64(-10), new Flt64(10)).Value);
        BigMValues.InferredBigM(
            new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, nested) }, Flt64.Zero),
            IIntoValue<Flt64>.Identity
        ).Should().Be(Flt64.One);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(
            relation: Relation(input: y, sign: Comparison.LE, constant: Flt64.Zero),
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();

        Result<Success, ErrorCode, Error<ErrorCode>> result = function.RegisterConstraints(model);
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> rows = ((IMechanismModel<Flt64>)model).Constraints;
        if (unbounded) {
            result.IsFailed.Should().BeTrue();
            rows.Should().ContainSingle();
            return;
        }
        result.IsOk.Should().BeTrue();
        IVariableItem boundFlag = sign == Comparison.EQ
            ? function.EqPositiveVars[1] ?? throw new InvalidOperationException("Missing positive flag.")
            : function.FlagVars[1];
        rows[3].Lhs.Cast<ILinearCell<Flt64>>().Single(cell => cell.Token.Variable.Key == boundFlag.Key)
            .Coefficient.Abs().Should().Be(new Flt64(10));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LaterInvalidNestedInput_ShouldNotWriteEarlierRows(bool cyclic, bool explicitBigM) {
        var x = new RealVar("invalid-nested-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var nested = LinearExpressionSymbol.FromVariable(x, "invalid-nested");
        if (cyclic) {
            nested.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, nested));
        }
        else {
            var absolute = new AbsFunction<Flt64>(
                polynomial: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
                converter: IIntoValue<Flt64>.Identity
            );
            nested.AsMutable().AddMonomial(new LinearMonomial<Flt64>(
                Flt64.One,
                new LinearFunctionSymbolAdapter<Flt64>(absolute)
            ));
        }
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                Relation(input: x, sign: Comparison.LE, constant: Flt64.Zero),
                Relation(input: nested, sign: Comparison.EQ, constant: Flt64.Zero),
            },
            epsilon: new Flt64(0.25),
            bigM: explicitBigM ? new Flt64(10) : null
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(
            relation: Relation(input: x, sign: Comparison.LE, constant: Flt64.Zero),
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Theory]
    [InlineData(Comparison.LE, false, false)]
    [InlineData(Comparison.GE, false, false)]
    [InlineData(Comparison.LE, false, true)]
    [InlineData(Comparison.GE, false, true)]
    [InlineData(Comparison.LE, true, true)]
    [InlineData(Comparison.GE, true, true)]
    [InlineData(Comparison.LE, true, false)]
    [InlineData(Comparison.GE, true, false)]
    public async Task ShiftedBoundaries_ShouldSurviveExactRowsAndDump(Comparison sign, bool nested, bool explicitBigM) {
        var x = new RealVar("residual-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1.5), new Flt64(1.5)).Value);
        ISymbol input = x;
        Flt64 constant = new(0.3);
        if (nested) {
            input = new LinearExpressionSymbol(
                polynomial: new MutableLinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                    constant
                ),
                name: "residual-nested"
            );
            constant = Flt64.Zero;
        }
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(
                input: input,
                sign: sign,
                constant: constant
            ) },
            epsilon: new Flt64(0.125),
            bigM: explicitBigM ? new Flt64(2) : null
        );
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue($"model construction: {built}");
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        matrix.Constraints.Size.Should().Be(3);
        double direction = sign == Comparison.LE ? 1 : -1;
        foreach ((double value, int expected) in new[] { (0.0, 1), (direction * 0.125, 0), (direction * 0.0625, -1) }) {
            double xValue = value - 0.3;
            Exact(xValue).Add(Exact(0.3)).CompareTo(Exact(value)).Should().Be(0);
            function.Evaluate(new Dictionary<ISymbol, Flt64> {
                [input] = new Flt64(nested ? value : xValue),
            }).Should().Be(expected < 0 ? null : new Flt64(expected));
            foreach (int flag in new[] { 0, 1 }) {
                var values = new Dictionary<IVariableItem, Flt64> {
                    [x] = new Flt64(xValue),
                    [residual] = new Flt64(value),
                    [function.FlagVars[0]] = new Flt64(flag),
                };
                bool expectedFeasible = expected == flag;
                ExactFeasible(model, values).Should().Be(expectedFeasible, $"{sign}, residual={value}, flag={flag}");
                ExactFeasible(matrix, values).Should().Be(expectedFeasible, $"matrix: {sign}, residual={value}, flag={flag}");
            }
        }
    }

    [Fact]
    public async Task OriginalQuarterEpsilonEndpoint_ShouldBeExactlyFeasible() {
        var x = new RealVar("original-endpoint-x");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(
                input: x,
                sign: Comparison.LE,
                constant: new Flt64(0.3)
            ) },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(2)
        );
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-0.3),
            [residual] = Flt64.Zero,
            [function.FlagVars[0]] = Flt64.One,
        };

        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-0.3) }).Should().Be(Flt64.One);
        ExactFeasible(model, values).Should().BeTrue();
        ExactFeasible(matrix, values).Should().BeTrue();
    }

    [Theory]
    [InlineData(Comparison.LE, true)]
    [InlineData(Comparison.EQ, true)]
    [InlineData(Comparison.LE, false)]
    [InlineData(Comparison.GE, false)]
    [InlineData(Comparison.EQ, false)]
    public async Task NestedFunction_ShouldKeepItsDefinitionAndResidual(Comparison sign, bool explicitBigM) {
        var x = new RealVar("residual-nested-abs-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        var absolute = new AbsFunction<Flt64>(
            polynomial: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(2),
            name: "residual-absolute"
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(
                input: inner,
                sign: sign,
                constant: new Flt64(-0.3)
            ) },
            epsilon: new Flt64(0.125),
            bigM: explicitBigM ? new Flt64(2) : null
        );
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        parent.Add(inner).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue($"model construction: {built}");
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(0.3),
            [absolute.ResultVar] = new Flt64(0.3),
            [absolute.PositiveVar] = new Flt64(0.3),
            [absolute.NegativeVar] = Flt64.Zero,
            [absolute.SignVar] = Flt64.One,
            [residual] = Flt64.Zero,
            [function.FlagVars[0]] = Flt64.One,
        };
        if (function.EqPositiveVars[0] is { } positive && function.EqNegativeVars[0] is { } negative) {
            values[positive] = Flt64.Zero;
            values[negative] = Flt64.Zero;
        }

        function.Evaluate(new Dictionary<ISymbol, Flt64> { [inner] = new Flt64(0.3) }).Should().Be(Flt64.One);
        ExactFeasible(model, values).Should().BeTrue();
        ExactFeasible(matrix, values).Should().BeTrue();
        values[function.FlagVars[0]] = Flt64.Zero;
        if (function.EqPositiveVars[0] is { } positiveFlag) {
            values[positiveFlag] = Flt64.One;
        }
        ExactFeasible(model, values).Should().BeFalse();
        ExactFeasible(matrix, values).Should().BeFalse();
    }

    [Fact]
    public async Task FloatingEvaluation_CanStillDifferFromExactRowArithmetic() {
        var x = new RealVar("rounding-x");
        var y = new RealVar("rounding-y");
        x.Range.SetRange(ValueRange<Flt64>.Full());
        y.Range.SetRange(ValueRange<Flt64>.Full());
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(
                        new[] {
                            new LinearMonomial<Flt64>(Flt64.One, x),
                            new LinearMonomial<Flt64>(Flt64.One, y),
                        },
                        Flt64.One
                    ),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: Comparison.LE
                ),
            },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(2)
        );
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(1e16),
            [y] = new Flt64(-1e16),
            [residual] = Flt64.One,
            [function.FlagVars[0]] = Flt64.Zero,
        };

        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = values[x], [y] = values[y] }).Should().Be(Flt64.One);
        ExactFeasible(model, values).Should().BeTrue();
        values[function.FlagVars[0]] = Flt64.One;
        ExactFeasible(model, values).Should().BeFalse();
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.EQ)]
    public async Task InexactInputConstantDifference_ShouldFailWithoutChangingParentTokens(Comparison sign) {
        var x = new RealVar("inexact-constant-x");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.One),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(0.3)),
                    Comparison: sign
                ),
            },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(2)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        IVariableItem[] before = parent.Tokens.Tokens.Select(token => token.Variable).ToArray();

        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Tokens.Tokens.Select(token => token.Variable).Should().Equal(before);
    }

    [Theory]
    [InlineData(Comparison.LE, false)]
    [InlineData(Comparison.GE, false)]
    [InlineData(Comparison.LE, true)]
    [InlineData(Comparison.GE, true)]
    [InlineData(Comparison.EQ, false)]
    [InlineData(Comparison.EQ, true)]
    public async Task InexactCoefficientMerge_ShouldLeaveExistingRowsUntouched(Comparison sign, bool shifted) {
        var x = new RealVar("inexact-coefficient-x");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(
                        new[] {
                            new LinearMonomial<Flt64>(Flt64.One, x),
                            new LinearMonomial<Flt64>(new Flt64(global::System.Math.Pow(2, -54)), x),
                        },
                        shifted ? new Flt64(0.3) : Flt64.Zero
                    ),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: sign
                ),
            },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(2)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(
            relation: Relation(
                input: x,
                sign: Comparison.LE,
                constant: Flt64.Zero
            ),
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Theory]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.EQ)]
    public async Task LaterInexactConstant_ShouldNotWriteEarlierIndicatorRows(Comparison sign) {
        var x = new RealVar("later-inexact-x");
        var inexact = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.One),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(0.3)),
            Comparison: sign
        );
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                Relation(
                    input: x,
                    sign: Comparison.LE,
                    constant: Flt64.Zero
                ),
                inexact,
            },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(2)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(
            relation: Relation(
                input: x,
                sign: Comparison.LE,
                constant: Flt64.Zero
            ),
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.EQ)]
    public async Task MutationAfterConstruction_ShouldRejectUnencodableEndpointWithoutResidual(Comparison sign) {
        var x = new RealVar("mutated-residual-x");
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var relation = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            Comparison: sign
        );
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { relation },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(2)
        );
        function.ResidualVars[0].Should().BeNull();
        terms.Add(new LinearMonomial<Flt64>(
            Flt64.One,
            LinearExpressionSymbol.FromConstant(new Flt64(0.3))
        ));
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.EQ)]
    public async Task MissingResidualToken_ShouldNotWriteRows(Comparison sign) {
        var x = new RealVar("missing-residual-x");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(
                input: x,
                sign: sign,
                constant: new Flt64(0.3)
            ) },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(2)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (BinVar helper in function.HelperVariables.OfType<BinVar>()) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Comparison.LE)]
    [InlineData(Comparison.EQ)]
    public async Task InputReferencingResidual_ShouldNotWriteRows(Comparison sign) {
        var x = new RealVar("self-residual-x");
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(terms, new Flt64(0.3)),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison: sign
                ),
            },
            epsilon: new Flt64(0.125),
            bigM: new Flt64(2)
        );
        RealVar residual = function.ResidualVars[0] ?? throw new InvalidOperationException("Missing residual.");
        terms.Add(new LinearMonomial<Flt64>(Flt64.One, residual));
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EqualityMissingSideToken_ShouldLeaveExistingRowsUntouched(bool missingPositive) {
        var x = new RealVar("equality-missing-side-x");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(
                input: x,
                sign: Comparison.EQ,
                constant: Flt64.Zero
            ) },
            epsilon: new Flt64(0.25),
            bigM: new Flt64(2)
        );
        BinVar missing = (missingPositive ? function.EqPositiveVars[0] : function.EqNegativeVars[0])
            ?? throw new InvalidOperationException("Missing side flag.");
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables.Where(helper => helper.Key != missing.Key)) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(
            relation: Relation(
                input: x,
                sign: Comparison.LE,
                constant: Flt64.Zero
            ),
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    [Fact]
    public async Task EqualityNestedInput_WhenExpandedBoundIsUnproven_ShouldNotWriteRows() {
        var x = new RealVar("equality-unbounded-nested-x");
        x.Range.SetRange(ValueRange<Flt64>.Full());
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                new Flt64(0.3)
            ),
            name: "equality-unbounded-nested"
        );
        var function = new SatisfiedAmountFunction(
            inequalities: new[] { Relation(
                input: nested,
                sign: Comparison.EQ,
                constant: Flt64.Zero
            ) },
            epsilon: new Flt64(0.25)
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
    public async Task LaterEqualityUnrepresentableRelaxation_ShouldNotWriteEarlierRows() {
        var x = new RealVar("equality-overflow-x");
        var function = new SatisfiedAmountFunction(
            inequalities: new[] {
                Relation(
                    input: x,
                    sign: Comparison.LE,
                    constant: Flt64.Zero
                ),
                Relation(
                    input: x,
                    sign: Comparison.EQ,
                    constant: Flt64.Zero
                ),
            },
            epsilon: Flt64.One,
            bigM: new Flt64(1e308),
            strictBoundary: new Flt64(1e308)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(
            relation: Relation(
                input: x,
                sign: Comparison.LE,
                constant: Flt64.Zero
            ),
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().ContainSingle();
    }

    private static LinearInequality<Flt64> Relation(
        ISymbol input,
        Comparison sign,
        Flt64 constant) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, constant),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
        Comparison: sign
    );

    internal static bool ExactFeasible(LinearMechanismModel<Flt64> model, IReadOnlyDictionary<IVariableItem, Flt64> values) =>
        ((IMechanismModel<Flt64>)model).Constraints.All(row => {
            ExactBoundValue sum = ExactBoundValue.Zero;
            foreach (ILinearCell<Flt64> cell in row.Lhs.Cast<ILinearCell<Flt64>>()) {
                sum = sum.Add(Exact(cell.Coefficient.ToDouble()).Multiply(Exact(values[cell.Token.Variable].ToDouble())));
            }
            return Satisfies(
                lhs: sum,
                rhs: row.Rhs,
                sign: row.Sign
            );
        });

    internal static bool ExactFeasible(LinearTriadModel model, IReadOnlyDictionary<IVariableItem, Flt64> values) {
        for (int index = 0; index < model.Constraints.Size; ++index) {
            ExactBoundValue sum = ExactBoundValue.Zero;
            foreach (LinearConstraintCell cell in model.Constraints.Lhs[index]) {
                IVariableItem variable = model.Variables[cell.ColIndex].Origin ?? throw new InvalidOperationException("Missing origin.");
                sum = sum.Add(Exact(cell.Coefficient.ToDouble()).Multiply(Exact(values[variable].ToDouble())));
            }
            if (!Satisfies(
                    lhs: sum,
                    rhs: model.Constraints.Rhs[index],
                    sign: model.Constraints.Signs[index]
                )) {
                return false;
            }
        }
        return true;
    }

    private static bool Satisfies(
        ExactBoundValue lhs,
        Flt64 rhs,
        ConstraintRelation sign) {
        int comparison = lhs.CompareTo(Exact(rhs.ToDouble()));
        return sign == ConstraintRelation.LessEqual ? comparison <= 0 :
            sign == ConstraintRelation.GreaterEqual ? comparison >= 0 : comparison == 0;
    }

    private static ExactBoundValue Exact(double value) {
        ExactBoundValue.TryRead(new Flt64(value), out ExactBoundValue exact).Should().BeTrue();
        return exact;
    }

    private sealed class RangedLinearSymbol(IVariableItem variable) : ILinearIntermediateSymbol<Flt64> {
        private readonly LinearExpressionSymbol _inner = LinearExpressionSymbol.FromVariable(variable, "declared-range");
        public string Name { get => _inner.Name; set => _inner.Name = value; }
        public string? DisplayName { get => _inner.DisplayName; set => _inner.DisplayName = value; }
        public MathUInt64 Identifier => _inner.Identifier;
        public int Index => _inner.Index;
        public IIntermediateSymbol? Parent => null;
        public Category Category => LinearCategory.Instance;
        public bool Cached => false;
        public ExpressionRange<Flt64> Range => ExpressionRange<Flt64>.Create(
            ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value
        );
        public IReadOnlySet<IIntermediateSymbol> Dependencies => _inner.Dependencies;
        public LinearPolynomial<Flt64> Polynomial => _inner.Polynomial;
        public MutableLinearPolynomial<Flt64> AsMutable() => _inner.AsMutable();
        public Flt64? Prepare(IReadOnlyDictionary<ISymbol, Flt64>? values, IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter) => _inner.Prepare(
            values: values,
            tokenTable: tokenTable,
            converter: converter
        );
        public void PrepareAndCache(IReadOnlyDictionary<ISymbol, Flt64>? values, IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter) => _inner.PrepareAndCache(
            values: values,
            tokenTable: tokenTable,
            converter: converter
        );
        public Flt64? Evaluate(IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) => _inner.Evaluate(
            tokenTable: tokenTable,
            converter: converter,
            zeroIfNone: zeroIfNone
        );
        public Flt64? Evaluate(IReadOnlyList<Flt64> results, IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) => _inner.Evaluate(
            results: results,
            tokenTable: tokenTable,
            converter: converter,
            zeroIfNone: zeroIfNone
        );
        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values, IAbstractTokenTable<Flt64>? tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) => _inner.Evaluate(
            values: values,
            tokenTable: tokenTable,
            converter: converter,
            zeroIfNone: zeroIfNone
        );
        public void Flush(bool force = false) => _inner.Flush(force);
        public string ToRawString(MathUInt64 unfold) => _inner.ToRawString(unfold);
    }
}
