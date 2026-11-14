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

public class IfInFunctionTest {
    [Theory]
    [InlineData(-2.0, TruthValue.False)]
    [InlineData(-0.5, TruthValue.False)]
    [InlineData(-0.25, TruthValue.Undefined)]
    [InlineData(0.0, TruthValue.True)]
    [InlineData(0.5, TruthValue.True)]
    [InlineData(1.0, TruthValue.True)]
    [InlineData(1.25, TruthValue.Undefined)]
    [InlineData(1.5, TruthValue.False)]
    [InlineData(3.0, TruthValue.False)]
    public async Task Interval_ShouldMatchClassificationAndBothMatrixForms(double value, TruthValue expected) {
        RealVar x = Real(
            name: "ifin-truth",
            min: -2,
            max: 3
        );
        IfInFunction<Flt64> function = Create(x);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Assert.Equal(15, ((IMechanismModel<Flt64>)model).Constraints.Count);
        var input = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(value) };
        Assert.Equal(expected, function.Classify(input).Value);
        Assert.Equal(expected == TruthValue.Undefined ? null : expected == TruthValue.True ? Flt64.One : Flt64.Zero,
            function.Evaluate(input));
        for (int lower = 0; lower <= 1; ++lower) {
            for (int lowerFalse = 0; lowerFalse <= 1; ++lowerFalse) {
                for (int upper = 0; upper <= 1; ++upper) {
                    for (int upperFalse = 0; upperFalse <= 1; ++upperFalse) {
                        for (int result = 0; result <= 1; ++result) {
                            Dictionary<IVariableItem, Flt64> assignment = Assignment(
                                function: function,
                                x: x,
                                value: value,
                                lower: lower,
                                lowerFalse: lowerFalse,
                                upper: upper,
                                upperFalse: upperFalse,
                                result: result
                            );
                            bool feasible = expected != TruthValue.Undefined &&
                                lower == (value >= 0 ? 1 : 0) && lowerFalse == 1 - lower &&
                                upper == (value <= 1 ? 1 : 0) && upperFalse == 1 - upper &&
                                result == (expected == TruthValue.True ? 1 : 0);
                            Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
                            Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(matrix, assignment));
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(-2.0, -1.0, -1.0, false)]
    [InlineData(0.0, 1.0, 0.0, true)]
    [InlineData(0.0, 1.0, 1.0, true)]
    [InlineData(2.0, 3.0, 2.0, false)]
    public async Task FixedSideRanges_ShouldStillLinkBothIndicators(double min, double max, double value, bool inside) {
        RealVar x = Real(
            name: "ifin-fold",
            min: min,
            max: max
        );
        IfInFunction<Flt64> function = Create(x);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        int lower = value >= 0 ? 1 : 0;
        int upper = value <= 1 ? 1 : 0;
        Dictionary<IVariableItem, Flt64> assignment = Assignment(
            function: function,
            x: x,
            value: value,
            lower: lower,
            lowerFalse: 1 - lower,
            upper: upper,
            upperFalse: 1 - upper,
            result: inside ? 1 : 0
        );
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        assignment[function.ResultVar] = inside ? Flt64.Zero : Flt64.One;
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
    }

    [Fact]
    public async Task EqualEndpoints_ShouldIncludeOnlyTheEndpointAndKeepBothGaps() {
        RealVar x = Real(
            name: "ifin-singleton",
            min: -1,
            max: 1
        );
        IfInFunction<Flt64> function = IfInFunction<Flt64>.Create(
            x: Polynomial(x),
            lower: Flt64.Zero,
            upper: Flt64.Zero,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        foreach ((double value, TruthValue truth) in new[] {
            (-0.5, TruthValue.False), (-0.25, TruthValue.Undefined), (0.0, TruthValue.True),
            (0.25, TruthValue.Undefined), (0.5, TruthValue.False),
        }) {
            Assert.Equal(truth, function.Classify(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(value) }).Value);
            for (int lower = 0; lower <= 1; ++lower) {
                for (int upper = 0; upper <= 1; ++upper) {
                    for (int result = 0; result <= 1; ++result) {
                        Dictionary<IVariableItem, Flt64> assignment = Assignment(
                            function: function,
                            x: x,
                            value: value,
                            lower: lower,
                            lowerFalse: 1 - lower,
                            upper: upper,
                            upperFalse: 1 - upper,
                            result: result
                        );
                        assignment[function.UpperCondition.ConditionVar] = new Flt64(value);
                        bool feasible = truth != TruthValue.Undefined && lower == (value >= 0 ? 1 : 0) &&
                            upper == (value <= 0 ? 1 : 0) && result == (truth == TruthValue.True ? 1 : 0);
                        Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
                    }
                }
            }
        }
    }

    [Fact]
    public async Task FloatingEvaluationGrouping_CanDifferFromExactModelRows() {
        RealVar x = Real(
            name: "ifin-rounding",
            min: -2,
            max: 3
        );
        IfInFunction<Flt64> function = IfInFunction<Flt64>.Create(
            x: Polynomial(x, 1e16),
            lower: new Flt64(1e16),
            upper: new Flt64(1e16),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(function.Classify(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }).IsFailed);
        Dictionary<IVariableItem, Flt64> assignment = Assignment(
            function: function,
            x: x,
            value: 1,
            lower: 1,
            lowerFalse: 0,
            upper: 0,
            upperFalse: 1,
            result: 1
        );
        assignment[function.UpperCondition.ConditionVar] = Flt64.One;
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        assignment[function.ResultVar] = Flt64.Zero;
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
    }

    [Fact]
    public async Task ExplicitBounds_ShouldConstrainXWithoutRestrictingEvaluation() {
        RealVar x = Real(
            name: "ifin-bounds",
            min: -2,
            max: 3
        );
        IfInFunction<Flt64> function = IfInFunction<Flt64>.Create(
            x: Polynomial(x),
            lower: Flt64.Zero,
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-0.5), new Flt64(1.5))
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.Equal(Flt64.Zero, function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-1) }));
        Dictionary<IVariableItem, Flt64> assignment = Assignment(
            function: function,
            x: x,
            value: -1,
            lower: 0,
            lowerFalse: 1,
            upper: 1,
            upperFalse: 0,
            result: 0
        );
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(4)).Value);
        Assert.True(model.ToLinearTriadModel().IsOk);
    }

    [Fact]
    public async Task MissingUpperOrResultHelper_ShouldFailBeforeAddingAnyRows() {
        foreach (int missing in new[] { 0, 1 }) {
            RealVar x = Real(
                name: "ifin-missing",
                min: -2,
                max: 3
            );
            IfInFunction<Flt64> function = Create(x);
            IVariableItem absent = missing == 0 ? function.UpperCondition.FalseVar : function.ResultVar;
            using var parent = new LinearMetaModel<Flt64>();
            Assert.True(parent.Add(x).IsOk);
            foreach (IVariableItem helper in function.HelperVariables.Where(variable => variable != absent)) {
                Assert.True(parent.Add(helper).IsOk);
            }
            using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            Assert.True(model.AddConstraint(
                relation: Polynomial(x).Le(Flt64.One),
                from: (IIntermediateSymbol?)null
            ).IsOk);
            Assert.True(function.RegisterConstraints(model).IsFailed);
            Assert.Single(((IMechanismModel<Flt64>)model).Constraints);
        }
    }

    [Fact]
    public async Task CapturedBoundsAndNestedExpressionMutation_ShouldInvalidateRegistration() {
        RealVar x = Real(
            name: "ifin-stale",
            min: -2,
            max: 3
        );
        ValueRange<Flt64> original = x.Range.Range!;
        var inner = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) }, Flt64.One),
            name: "ifin-inner"
        );
        IfInFunction<Flt64> function = IfInFunction<Flt64>.Create(
            x: Polynomial(inner),
            lower: Flt64.Zero,
            upper: new Flt64(2),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        Assert.True(model.ToLinearTriadModel().IsFailed);
        using var tokens = new ManualTokenList<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
        Assert.Empty(tokens.Tokens);
        x.Range.SetRange(original);
        inner.AsMutable().SetConstant(new Flt64(2));
        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Equal(15, ((IMechanismModel<Flt64>)model).Constraints.Count);
        using var other = new LinearMetaModel<Flt64>();
        Assert.True(other.Add(x).IsOk);
        Assert.True(other.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsFailed);
        Assert.Empty(other.Tokens.Symbols);
        Assert.All(function.HelperVariables, variable => Assert.Null(other.Tokens.Find(variable)));
    }

    [Fact]
    public async Task NestedFunction_ShouldRequireRegistrationAndPrepareFixedValues() {
        RealVar x = Real(
            name: "ifin-nested",
            min: -2,
            max: 2
        );
        var abs = new AbsFunction<Flt64>(Polynomial(x), IIntoValue<Flt64>.Identity);
        var inner = new LinearFunctionSymbolAdapter<Flt64>(abs);
        IfInFunction<Flt64> function = IfInFunction<Flt64>.Create(
            x: Polynomial(inner),
            lower: new Flt64(0.5),
            upper: new Flt64(1.5),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.25)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var missing = new LinearMetaModel<Flt64>();
        Assert.True(missing.Add(x).IsOk);
        Assert.True(missing.Add(adapter).IsOk);
        Assert.True((await LinearMechanismModel<Flt64>.InvokeAsync(missing)).IsFailed);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(-1) }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(TruthValue.True, function.Classify(new Dictionary<ISymbol, Flt64> { [inner] = Flt64.One }).Value);
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1), [abs.ResultVar] = Flt64.One,
            [abs.PositiveVar] = Flt64.Zero, [abs.NegativeVar] = Flt64.One, [abs.SignVar] = Flt64.Zero,
            [function.LowerCondition.ConditionVar] = new Flt64(0.5),
            [function.LowerCondition.ResultVar] = Flt64.One,
            [function.LowerCondition.FalseVar] = Flt64.Zero,
            [function.UpperCondition.ConditionVar] = new Flt64(-0.5),
            [function.UpperCondition.ResultVar] = Flt64.One,
            [function.UpperCondition.FalseVar] = Flt64.Zero,
            [function.ResultVar] = Flt64.One,
        };
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        assignment[function.ResultVar] = Flt64.Zero;
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
    }

    [Fact]
    public void CreationAndEvaluation_ShouldRejectInvalidOrInexactInputs() {
        RealVar x = Real(
            name: "ifin-invalid",
            min: -2,
            max: 3
        );
        Assert.True(IfInFunction<Flt64>.Create(
            x: Polynomial(x),
            lower: Flt64.One,
            upper: Flt64.Zero,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).IsFailed);
        Assert.True(IfInFunction<Flt64>.Create(
            x: Polynomial(x),
            lower: new Flt64(double.NaN),
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).IsFailed);
        Assert.True(IfInFunction<Flt64>.Create(
            x: Polynomial(x),
            lower: Flt64.Zero,
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.Zero
        ).IsFailed);
        Assert.True(IfInFunction<Flt64>.Create(
            x: Polynomial(x, 1e16),
            lower: Flt64.One,
            upper: new Flt64(2),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1e16), new Flt64(1e16))
        ).IsFailed);
        Assert.True(IfInFunction<Flt64>.Create(
            x: Polynomial(x),
            lower: new Flt64(0.5),
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            bounds: new LinearPolynomialBounds<Flt64>(Flt64.Zero, new Flt64(1e16))
        ).IsFailed);
        IfInFunction<Flt64> function = Create(x);
        Assert.True(function.Classify(new Dictionary<ISymbol, Flt64>()).IsFailed);
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64>()).IsOk);
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64>()));
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.NaN) }).IsFailed);
    }

    [Fact]
    public void InputAndResult_ShouldKeepReadOnlySnapshots() {
        RealVar x = Real(
            name: "ifin-snapshot",
            min: -2,
            max: 3
        );
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        IfInFunction<Flt64> function = IfInFunction<Flt64>.Create(
            x: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
            lower: Flt64.Zero,
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        terms[0] = new LinearMonomial<Flt64>(new Flt64(-1), x);
        Assert.Equal(TruthValue.True, function.Classify(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.5) }).Value);
        IList<LinearMonomial<Flt64>> inputTerms = Assert.IsAssignableFrom<IList<LinearMonomial<Flt64>>>(function.X.Monomials);
        Assert.Throws<NotSupportedException>(() => inputTerms[0] = new LinearMonomial<Flt64>(Flt64.One, x));
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        IList<LinearMonomial<Flt64>> resultTerms = Assert.IsAssignableFrom<IList<LinearMonomial<Flt64>>>(adapter.Polynomial.Monomials);
        Assert.Throws<NotSupportedException>(() => resultTerms[0] = new LinearMonomial<Flt64>(Flt64.One, x));
        Assert.Same(function.ResultVar, adapter.Polynomial.Monomials[0].Symbol);
    }

    [Fact]
    public async Task DiscreteLattice_ShouldValidateBothEndpoints() {
        var x = new IntVar("ifin-lattice");
        x.Range.SetRange(ValueRange<Integer>.Of(new Integer(-2), new Integer(3)).Value);
        Result<IfInFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfInFunction<Flt64>.Create(
            x: Polynomial(x),
            lower: Flt64.Zero,
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            delta: Flt64.One
        );
        Assert.True(created.IsOk, created.ToString());
        IfInFunction<Flt64> function = created.Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        for (int value = -2; value <= 3; ++value) {
            int lower = value >= 0 ? 1 : 0;
            int upper = value <= 1 ? 1 : 0;
            Dictionary<IVariableItem, Flt64> assignment = Assignment(
                function: function,
                x: x,
                value: value,
                lower: lower,
                lowerFalse: 1 - lower,
                upper: upper,
                upperFalse: 1 - upper,
                result: lower * upper
            );
            Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
            Assert.Equal(new Flt64(lower * upper), function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(value) }));
        }
        Assert.True(IfInFunction<Flt64>.Create(
            x: Polynomial(x),
            lower: Flt64.Zero,
            upper: new Flt64(1.25),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            delta: Flt64.One
        ).IsFailed);
    }

    [Fact]
    public async Task RepresentativeNumericTypes_ShouldBuildAndEvaluate() {
        await VerifyNumeric(IIntoValue<Flt64>.Identity);
        await VerifyNumeric(new NumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyNumeric(new NumberConverter<Integer>(value => value.ToInt64()));
        await VerifyNumeric(new NumberConverter<FltX>(value => value.ToFltX()));
        await VerifyNumeric(new NumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    private static async Task VerifyNumeric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        RealVar x = Real(
            name: $"ifin-{typeof(V).Name}",
            min: -2,
            max: 3
        );
        var polynomial = new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, x) }, converter.Zero);
        Result<IfInFunction<V>, ErrorCode, Error<ErrorCode>> created = IfInFunction<V>.Create(
            x: polynomial,
            lower: converter.Zero,
            upper: converter.One,
            converter: converter,
            strictBoundary: converter.One
        );
        Assert.True(created.IsOk, created.ToString());
        IfInFunction<V> function = created.Value;
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        foreach (int value in new[] { -1, 0, 1, 2 }) {
            V input = converter.IntoValue(new Flt64(value));
            bool inside = value >= 0 && value <= 1;
            V lower = value >= 0 ? converter.One : converter.Zero;
            V upper = value <= 1 ? converter.One : converter.Zero;
            var values = new Dictionary<IVariableItem, V> {
                [x] = input,
                [function.LowerCondition.ConditionVar] = input,
                [function.LowerCondition.ResultVar] = lower,
                [function.LowerCondition.FalseVar] = value >= 0 ? converter.Zero : converter.One,
                [function.UpperCondition.ConditionVar] = converter.IntoValue(new Flt64(value - 1)),
                [function.UpperCondition.ResultVar] = upper,
                [function.UpperCondition.FalseVar] = value <= 1 ? converter.Zero : converter.One,
                [function.ResultVar] = inside ? converter.One : converter.Zero,
            };
            var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
            Assert.All(((IMechanismModel<V>)model).Constraints, row => Assert.True(row.IsTrue(solution) == true));
            Assert.True(function.Evaluate(new Dictionary<ISymbol, V> { [x] = input })!.Value.Eq(inside ? converter.One : converter.Zero));
        }
    }

    private static RealVar Real(string name, double min, double max) {
        var x = new RealVar(name);
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(min), new Flt64(max)).Value);
        return x;
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol x, double constant = 0) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, new Flt64(constant)
    );

    private static IfInFunction<Flt64> Create(ISymbol x) => IfInFunction<Flt64>.Create(
        x: Polynomial(x),
        lower: Flt64.Zero,
        upper: Flt64.One,
        converter: IIntoValue<Flt64>.Identity,
        strictBoundary: new Flt64(0.5)
    ).Value;

    private static Dictionary<IVariableItem, Flt64> Assignment(
        IfInFunction<Flt64> function,
        IVariableItem x,
        double value,
        int lower,
        int lowerFalse,
        int upper,
        int upperFalse,
        int result) => new() {
        [x] = new Flt64(value),
        [function.LowerCondition.ConditionVar] = new Flt64(value),
        [function.LowerCondition.ResultVar] = new Flt64(lower),
        [function.LowerCondition.FalseVar] = new Flt64(lowerFalse),
        [function.UpperCondition.ConditionVar] = new Flt64(value - 1),
        [function.UpperCondition.ResultVar] = new Flt64(upper),
        [function.UpperCondition.FalseVar] = new Flt64(upperFalse),
        [function.ResultVar] = new Flt64(result),
    };

    private sealed class NumberConverter<V>(Func<Flt64, V> convert) : IIntoValue<V>
        where V : struct, IRealNumber<V> {
        public V Zero => convert(Flt64.Zero);
        public V One => convert(Flt64.One);
        public V IntoValue(Flt64 value) => convert(value);
        public Flt64 FromValue(V value) => value.ToFlt64();
    }
}
