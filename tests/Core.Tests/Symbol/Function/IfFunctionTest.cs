#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class IfFunctionTest {
    [Theory]
    [InlineData(Comparison.GT)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.LE)]
    public async Task BoundedCondition_ShouldMatchClassificationAndExactRows(Comparison relation) {
        var x = new RealVar("if-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        foreach (double value in new[] { -2.0, -0.5, -0.25, 0, 0.25, 0.5, 2.0 }) {
            TruthValue expected = ConditionClassification.Classify(
                difference: new Flt64(value),
                relation: relation,
                strictBoundary: new Flt64(0.5)
            ).Value;
            Flt64? result = function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new(value) });
            Assert.Equal(expected == TruthValue.Undefined ? null : expected == TruthValue.True ? Flt64.One : Flt64.Zero, result);
            for (int yes = 0; yes <= 1; ++yes) {
                for (int no = 0; no <= 1; ++no) {
                    var assignment = new Dictionary<IVariableItem, Flt64> {
                        [x] = new(value),
                        [function.ResultVar] = new(yes),
                        [function.FalseVar] = new(no),
                        [function.ConditionVar] = new(value),
                    };
                    bool feasible = expected != TruthValue.Undefined && yes + no == 1 &&
                        yes == (expected == TruthValue.True ? 1 : 0);
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(matrix, assignment));
                }
            }
        }
    }

    [Theory]
    [InlineData(Comparison.GT)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.LE)]
    public async Task LargeBoundsAndOffset_ShouldKeepActiveThresholdsExact(Comparison relation) {
        var x = new RealVar("if-large-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1e16), new Flt64(1e16)).Value);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(x, 0.5),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            relation: relation
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        foreach (double difference in new[] { -1.0, -0.5, 0.0, 0.5, 1.0 }) {
            TruthValue expected = ConditionClassification.Classify(
                difference: new Flt64(difference),
                relation: relation,
                strictBoundary: Flt64.One
            ).Value;
            for (int result = 0; result <= 1; ++result) {
                var values = new Dictionary<IVariableItem, Flt64> {
                    [x] = new Flt64(difference - 0.5),
                    [function.ConditionVar] = new Flt64(difference),
                    [function.ResultVar] = new Flt64(result),
                    [function.FalseVar] = new Flt64(1 - result),
                };
                bool feasible = expected != TruthValue.Undefined && result == (expected == TruthValue.True ? 1 : 0);
                Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, values));
                Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
            }
        }
    }

    [Fact]
    public void DiscreteGap_ShouldRequireLatticeProof() {
        var x = new IntVar("if-integer");
        x.Range.SetRange(ValueRange<Integer>.Of(new Integer(-2), new Integer(2)).Value);
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: Comparison.GE,
            delta: Flt64.One
        ).IsOk);
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(x, 0.25),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: Comparison.GT
        ).IsFailed);
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(x, -0.25),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: Comparison.GE,
            delta: Flt64.One
        ).IsFailed);
    }

    [Theory]
    [InlineData(Comparison.GT, 0.25, false)]
    [InlineData(Comparison.GT, 0.5, true)]
    [InlineData(Comparison.LT, -0.25, false)]
    [InlineData(Comparison.LT, -0.5, true)]
    [InlineData(Comparison.GE, -0.25, false)]
    [InlineData(Comparison.GE, -1.0, true)]
    [InlineData(Comparison.LE, 0.25, false)]
    [InlineData(Comparison.LE, 1.0, true)]
    public void DiscreteProof_ShouldRespectOffsetAndRelation(Comparison relation, double constant, bool accepted) {
        var x = new IntVar("if-lattice");
        x.Range.SetRange(ValueRange<Integer>.Of(new Integer(-3), new Integer(3)).Value);
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> result = IfFunction<Flt64>.Create(
            condition: Polynomial(x, constant),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation,
            delta: relation is Comparison.GE or Comparison.LE ? Flt64.One : new Flt64(0.5)
        );
        Assert.Equal(accepted, result.IsOk);
    }

    [Fact]
    public void DiscreteProof_ShouldUseCombinedCoefficientGcdAndHonorSingleBranchBounds() {
        var x = new IntVar("if-lattice-x");
        var y = new IntVar("if-lattice-y");
        x.Range.SetRange(ValueRange<Integer>.Of(new Integer(-3), new Integer(3)).Value);
        y.Range.SetRange(ValueRange<Integer>.Of(new Integer(-3), new Integer(3)).Value);
        LinearPolynomial<Flt64> condition = new(new[] {
            new LinearMonomial<Flt64>(new Flt64(3), x),
            new LinearMonomial<Flt64>(Flt64.One, x),
            new LinearMonomial<Flt64>(new Flt64(6), y),
        }, new Flt64(0.5));
        Assert.True(IfFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: Comparison.GE,
            delta: new Flt64(1.5)
        ).IsOk);
        Assert.True(IfFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: Comparison.GE,
            delta: new Flt64(1.75)
        ).IsFailed);
        Assert.True(IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(new Flt64(0.5), x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).IsFailed);
        Assert.True(IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(new Flt64(0.5), x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(2), new Flt64(3))
        ).IsOk);
    }

    [Fact]
    public async Task AcceptedIntegerLattices_ShouldAvoidGapsAndMatchRows() {
        int accepted = 0;
        foreach (Comparison relation in new[] { Comparison.GT, Comparison.GE, Comparison.LT, Comparison.LE }) {
            foreach (double coefficient in new[] { -2.0, 1.0, 2.0 }) {
                foreach (double offset in new[] { -0.5, 0.0, 0.5 }) {
                    foreach (double delta in new[] { 0.5, 1.0 }) {
                        var x = new IntVar("if-lattice-exhaustive");
                        x.Range.SetRange(ValueRange<Integer>.Of(new Integer(-2), new Integer(2)).Value);
                        var condition = new LinearPolynomial<Flt64>(
                            new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), x) }, new Flt64(offset)
                        );
                        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
                            condition: condition,
                            converter: IIntoValue<Flt64>.Identity,
                            strictBoundary: new Flt64(0.5),
                            relation: relation,
                            delta: new Flt64(delta)
                        );
                        if (created.IsFailed) {
                            continue;
                        }
                        ++accepted;
                        IfFunction<Flt64> function = created.Value;
                        using var parent = new LinearMetaModel<Flt64>();
                        Assert.True(parent.Add(x).IsOk);
                        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
                        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
                            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
                        Assert.True(built.IsOk, built.ToString());
                        using LinearMechanismModel<Flt64> model = built.Value;
                        for (int input = -2; input <= 2; ++input) {
                            double difference = coefficient * input + offset;
                            TruthValue expected = ConditionClassification.Classify(
                                difference: new Flt64(difference),
                                relation: relation,
                                strictBoundary: new Flt64(0.5)
                            ).Value;
                            Assert.NotEqual(TruthValue.Undefined, expected);
                            for (int result = 0; result <= 1; ++result) {
                                var values = new Dictionary<IVariableItem, Flt64> {
                                    [x] = new Flt64(input),
                                    [function.ConditionVar] = new Flt64(difference),
                                    [function.ResultVar] = new Flt64(result),
                                    [function.FalseVar] = new Flt64(1 - result),
                                };
                                Assert.Equal(result == (expected == TruthValue.True ? 1 : 0),
                                    SatisfiedAmountResidualTest.ExactFeasible(model, values));
                            }
                        }
                    }
                }
            }
        }
        Assert.True(accepted > 0);
    }

    [Fact]
    public void Creation_ShouldRejectUnprovenInputsAndNumericConversions() {
        var x = new RealVar("if-unbounded");
        x.Range.SetRange(ValueRange<Flt64>.Full());
        IIntoValue<Flt64> converter = IIntoValue<Flt64>.Identity;
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: converter,
            strictBoundary: Flt64.One
        ).IsFailed);
        var bounds = new LinearPolynomialBounds<Flt64>(new Flt64(-2), new Flt64(2));
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: converter,
            strictBoundary: Flt64.Zero,
            bounds: bounds
        ).IsFailed);
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: converter,
            strictBoundary: Flt64.One,
            delta: new Flt64(0.5),
            bounds: bounds
        ).IsFailed);
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: converter,
            strictBoundary: Flt64.One,
            relation: Comparison.EQ,
            bounds: bounds
        ).IsFailed);
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: converter,
            strictBoundary: Flt64.One,
            delta: new Flt64(2),
            bounds: bounds
        ).IsFailed);
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: converter,
            strictBoundary: Flt64.One,
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(0.25), new Flt64(0.5))
        ).IsFailed);
        Assert.True(IfFunction<Flt64>.Create(
            condition: null,
            converter: converter,
            strictBoundary: Flt64.One
        ).IsFailed);
        var rational = new LinearPolynomial<RtnX>(
            new[] { new LinearMonomial<RtnX>(new RtnX(1, 10), x) }, RtnX.Zero
        );
        Assert.True(IfFunction<RtnX>.Create(
            condition: rational,
            converter: new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)),
            strictBoundary: RtnX.One,
            bounds: new LinearPolynomialBounds<RtnX>(new RtnX(-2, 1), new RtnX(2, 1))
        ).IsFailed);
        var intermediate = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero)
        );
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(intermediate),
            converter: converter,
            strictBoundary: Flt64.One,
            bounds: bounds
        ).IsOk);
    }

    [Fact]
    public async Task ExplicitBounds_ShouldConstrainInputAndAllowWiderVariableDomain() {
        var x = new RealVar("if-explicit");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(x, 0.5),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1), new Flt64(1))
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = Flt64.One, [function.ConditionVar] = new Flt64(1.5),
            [function.ResultVar] = Flt64.One, [function.FalseVar] = Flt64.Zero,
        };
        Assert.Equal(Flt64.One, function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }));
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-8), new Flt64(8)).Value);
        Assert.True(model.ToLinearTriadModel().IsOk);
    }

    [Fact]
    public async Task TrackedBounds_ShouldInvalidateLinearAndQuadraticDumps() {
        var x = new RealVar("if-stale");
        ValueRange<Flt64> original = ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value;
        x.Range.SetRange(original);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        Assert.True(linearParent.Add(x).IsOk);
        Assert.True(linearParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Assert.True(quadraticParent.Add(x).IsOk);
        Assert.True(quadraticParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        Assert.True(linear.ToLinearTriadModel().IsFailed);
        Assert.True(quadratic.ToQuadraticTetradModel().IsFailed);
        using var tokens = new ManualTokenList<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
        Assert.Empty(tokens.Tokens);
        x.Range.SetRange(original);
        using LinearTriadModel restored = linear.ToLinearTriadModel().Value;
        Assert.NotNull(restored);
    }

    [Fact]
    public async Task RegistrationFailure_ShouldPreserveExistingTokensAndRows() {
        var x = new RealVar("if-registration");
        ValueRange<Flt64> original = ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value;
        x.Range.SetRange(original);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(model.AddConstraint(
            relation: Polynomial(x).Le(Flt64.One),
            from: (IIntermediateSymbol?)null
        ).IsOk);
        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Single(((IMechanismModel<Flt64>)model).Constraints);
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsFailed);
        Assert.Empty(parent.Tokens.Symbols);
        Assert.NotNull(parent.Tokens.Find(x));
        Assert.All(function.HelperVariables, variable => Assert.Null(parent.Tokens.Find(variable)));
    }

    [Fact]
    public async Task SingleVariableAdapter_ShouldRequireRegistrationAndSupportNestedEvaluation() {
        var x = new RealVar("if-nested");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var abs = new AbsFunction<Flt64>(Polynomial(x), IIntoValue<Flt64>.Identity);
        var inner = new LinearFunctionSymbolAdapter<Flt64>(abs);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(inner, -0.5),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        using var missing = new LinearMetaModel<Flt64>();
        Assert.True(missing.Add(x).IsOk);
        Assert.True(missing.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Assert.True((await LinearMechanismModel<Flt64>.InvokeAsync(missing)).IsFailed);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.Equal(Flt64.One, function.Evaluate(new Dictionary<ISymbol, Flt64> { [inner] = Flt64.One }));
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1), [abs.ResultVar] = Flt64.One,
            [abs.PositiveVar] = Flt64.Zero, [abs.NegativeVar] = Flt64.One, [abs.SignVar] = Flt64.Zero,
            [function.ConditionVar] = new Flt64(0.5), [function.ResultVar] = Flt64.One, [function.FalseVar] = Flt64.Zero,
        };
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        assignment[function.ResultVar] = Flt64.Zero;
        assignment[function.FalseVar] = Flt64.One;
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
    }

    [Fact]
    public void DuplicateCoefficients_ShouldFailBeforeRegistration() {
        var x = new RealVar("if-coefficients");
        var condition = new LinearPolynomial<Flt64>(new[] {
            new LinearMonomial<Flt64>(new Flt64(1e16), x),
            new LinearMonomial<Flt64>(Flt64.One, x),
        }, Flt64.Zero);
        Result<IfFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1e16), new Flt64(1e16))
        );
        Assert.True(created.IsFailed);
    }

    [Fact]
    public void InputUnderflow_ShouldBeAResultFailureRatherThanAFalseBranch() {
        var x = new RealVar("if-underflow");
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(0.5), x) }, Flt64.Zero
            ),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1), Flt64.One)
        ).Value;
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.Epsilon) };
        Assert.True(function.TryEvaluate(values).IsFailed);
        Assert.Null(function.Evaluate(values));
    }

    [Fact]
    public void InputAndAdapterResult_ShouldExposeReadOnlySnapshots() {
        var x = new RealVar("if-snapshot");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        terms[0] = new LinearMonomial<Flt64>(new Flt64(-1), x);
        Assert.Equal(Flt64.One, function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }));
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        IList<LinearMonomial<Flt64>> resultTerms = Assert.IsAssignableFrom<IList<LinearMonomial<Flt64>>>(adapter.Polynomial.Monomials);
        Assert.Throws<NotSupportedException>(() => resultTerms[0] = new LinearMonomial<Flt64>(Flt64.One, x));
        Assert.Same(function.ResultVar, adapter.Polynomial.Monomials[0].Symbol);
    }

    [Fact]
    public void Creation_ShouldDetectConverterChangingCapturedBounds() {
        var x = new RealVar("if-mutating-converter");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var converter = new MutatingConverter(() =>
            x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value));
        Assert.True(IfFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: converter,
            strictBoundary: Flt64.One
        ).IsFailed);
        Assert.True(converter.Mutated);
    }

    [Fact]
    public async Task RepresentativeNumericTypes_ShouldBuildAndEvaluate() {
        await VerifyNumeric(IIntoValue<Flt64>.Identity);
        await VerifyNumeric(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyNumeric(new TestNumberConverter<Integer>(value => value.ToInt64()));
        await VerifyNumeric(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await VerifyNumeric(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    private static async Task VerifyNumeric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var x = new RealVar($"if-{typeof(V).Name}");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var condition = new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, x) }, converter.Zero);
        Result<IfFunction<V>, ErrorCode, Error<ErrorCode>> created = IfFunction<V>.Create(
            condition: condition,
            converter: converter,
            strictBoundary: converter.One,
            relation: Comparison.GE
        );
        Assert.True(created.IsOk, created.ToString());
        IfFunction<V> function = created.Value;
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        Assert.True(function.Evaluate(new Dictionary<ISymbol, V> { [x] = converter.Zero })!.Value.Eq(converter.One));
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, V>()));
        Assert.True(function.TryEvaluate(null!).IsFailed);
        IReadOnlyList<IConstraint<V, LinearCategory>> rows = ((IMechanismModel<V>)model).Constraints;
        Assert.Equal(6, rows.Count);
        foreach (double number in new[] { -1.0, 0.0, 1.0 }) {
            V value = converter.IntoValue(new Flt64(number));
            V result = number >= 0 ? converter.One : converter.Zero;
            var values = new Dictionary<IVariableItem, V> {
                [x] = value,
                [function.ConditionVar] = value,
                [function.ResultVar] = result,
                [function.FalseVar] = number >= 0 ? converter.Zero : converter.One,
            };
            var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
            Assert.All(rows, row => Assert.True(row.IsTrue(solution) == true));
            values[function.ResultVar] = number >= 0 ? converter.Zero : converter.One;
            values[function.FalseVar] = result;
            solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
            Assert.Contains(rows, row => row.IsTrue(solution) == false);
        }
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol, double constant = 0) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) },
        new Flt64(constant)
    );

    private sealed class MutatingConverter(Action mutate) : IIntoValue<Flt64> {
        public bool Mutated { get; private set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) {
            if (!Mutated) {
                Mutated = true;
                mutate();
            }
            return value;
        }
    }
}
