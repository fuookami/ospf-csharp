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

public class SigmoidFunctionTest {
    [Theory]
    [InlineData(Comparison.GT)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.LE)]
    public async Task BoundedCondition_ShouldMatchThreeValuedEvaluationAndRows(Comparison relation) {
        RealVar x = BoundedVariable("sigmoid-x");
        Result<SigmoidFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = SigmoidFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        );
        Assert.True(created.IsOk, created.ToString());
        SigmoidFunction<Flt64> function = created.Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        Assert.Same(function.IndicatorVar, adapter.Polynomial.Monomials.Single().Symbol);
        Assert.Contains(x, function.InputSymbols);
        Assert.Equal(3, function.HelperVariables.Count);
        Assert.Same(function.IndicatorVar, function.HelperVariables[0]);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        foreach (double number in new[] { -2.0, -0.5, -0.25, 0.0, 0.25, 0.5, 2.0 }) {
            var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(number) };
            TruthValue expected = ConditionClassification.Classify(
                difference: new Flt64(number),
                relation: relation,
                strictBoundary: new Flt64(0.5)
            ).Value;
            Assert.Equal(expected, function.Classify(values).Value);
            Flt64? expectedResult = expected == TruthValue.Undefined
                ? null : expected == TruthValue.True ? Flt64.One : Flt64.Zero;
            Assert.Equal(expectedResult, function.Evaluate(values));
            Assert.Equal(expectedResult, function.TryEvaluate(values).Value);
            for (int result = 0; result <= 1; ++result) {
                for (int other = 0; other <= 1; ++other) {
                    var assignment = new Dictionary<IVariableItem, Flt64> {
                        [x] = new Flt64(number),
                        [function.IndicatorVar] = new Flt64(result),
                        [function.HelperVariables[1]] = new Flt64(other),
                        [function.HelperVariables[2]] = new Flt64(number),
                    };
                    bool feasible = expected != TruthValue.Undefined && result + other == 1 &&
                        result == (expected == TruthValue.True ? 1 : 0);
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(matrix, assignment));
                }
            }
        }
        Assert.True(function.Classify(new Dictionary<ISymbol, Flt64>()).IsFailed);
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64>()).IsOk);
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64>()));
        Assert.True(function.Classify(null!).IsFailed);
        Assert.True(function.TryEvaluate(null!).IsFailed);
        Assert.True(function.Classify(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.NaN) }).IsFailed);
    }

    [Fact]
    public void Creation_ShouldValidateBoundsRelationsAndLegacyBigM() {
        var x = new RealVar("sigmoid-unbounded");
        x.Range.SetRange(ValueRange<Flt64>.Full());
        LinearPolynomial<Flt64> condition = Polynomial(x);
        Assert.True(SigmoidFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        ).IsFailed);
        var valid = new LinearPolynomialBounds<Flt64>(new Flt64(-2), new Flt64(2));
        foreach (Flt64 bigM in new[] { Flt64.Zero, new Flt64(-1), new Flt64(double.NaN), new Flt64(double.PositiveInfinity) }) {
            Assert.True(SigmoidFunction<Flt64>.Create(
                condition: condition,
                converter: IIntoValue<Flt64>.Identity,
                bounds: valid,
                bigM: bigM
            ).IsFailed);
        }
        Assert.True(SigmoidFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            bounds: valid,
            bigM: new Flt64(0.01),
            strictBoundary: new Flt64(0.5)
        ).IsOk);
        Assert.True(SigmoidFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            bounds: new LinearPolynomialBounds<Flt64>(Flt64.One, Flt64.Zero)
        ).IsFailed);
        Assert.True(SigmoidFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            bounds: valid,
            relation: Comparison.EQ
        ).IsFailed);
        Assert.True(SigmoidFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            bounds: valid,
            strictBoundary: Flt64.Zero
        ).IsFailed);
        Assert.True(SigmoidFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            bounds: valid,
            strictBoundary: Flt64.One,
            delta: new Flt64(0.5)
        ).IsFailed);
        Assert.True(SigmoidFunction<Flt64>.Create(
            condition: condition,
            converter: IIntoValue<Flt64>.Identity,
            bounds: valid,
            strictBoundary: new Flt64(0.5),
            delta: Flt64.One
        ).IsFailed);
        Assert.True(SigmoidFunction<Flt64>.Create(condition: null, converter: IIntoValue<Flt64>.Identity).IsFailed);
        Assert.True(SigmoidFunction<Flt64>.Create(condition: condition, converter: null).IsFailed);
    }

    [Fact]
    public void DefaultAndExplicitGaps_ShouldMatchKotlinPrecedence() {
        RealVar x = BoundedVariable("sigmoid-default");
        SigmoidFunction<Flt64> standard = SigmoidFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        Assert.Equal(new Flt64(1e-10), standard.StrictBoundary);
        Assert.Equal(standard.StrictBoundary, standard.Delta);
        SigmoidFunction<Flt64> legacy = SigmoidFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            tolerance: new Flt64(0.25)
        ).Value;
        Assert.Equal(new Flt64(0.25), legacy.StrictBoundary);
        SigmoidFunction<Flt64> explicitGap = SigmoidFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            tolerance: Flt64.Zero
        ).Value;
        Assert.Equal(new Flt64(0.5), explicitGap.StrictBoundary);
        Assert.Equal(TruthValue.Undefined,
            explicitGap.Classify(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.25) }).Value);
    }

    [Fact]
    public async Task ExplicitBounds_ShouldBeEnforcedWithoutTrackingVariableBounds() {
        RealVar x = BoundedVariable("sigmoid-explicit");
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1), Flt64.One)
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(1.5),
            [function.IndicatorVar] = Flt64.One,
            [function.HelperVariables[1]] = Flt64.Zero,
            [function.HelperVariables[2]] = new Flt64(1.5),
        };
        Assert.Equal(Flt64.One, function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(1.5) }));
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Assert.NotNull(matrix);
    }

    [Fact]
    public async Task ChangedInferredBounds_ShouldInvalidateBothModelsAndPreventRegistration() {
        RealVar x = BoundedVariable("sigmoid-stale");
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
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
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
        Assert.True(linear.ToLinearTriadModel().IsFailed);
        Assert.True(quadratic.ToQuadraticTetradModel().IsFailed);
        using var tokens = new ManualTokenList<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
        Assert.Empty(tokens.Tokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateHelper_ShouldNotPartiallyRegister(bool manual) {
        RealVar x = BoundedVariable("sigmoid-duplicate");
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        using var parent = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: manual, CheckTokenExists: true)
        );
        Assert.True(parent.Add(function.HelperVariables[1]).IsOk);
        Token<Flt64> existing = parent.Tokens.Find(function.HelperVariables[1])!;
        Assert.True(function.RegisterAuxiliaryTokens(parent.Tokens).IsFailed);
        Assert.Same(existing, parent.Tokens.Find(function.HelperVariables[1]));
        Assert.Null(parent.Tokens.Find(function.IndicatorVar));
        Assert.Null(parent.Tokens.Find(function.HelperVariables[2]));
    }

    [Fact]
    public async Task MissingHelper_ShouldNotWriteRows() {
        RealVar x = BoundedVariable("sigmoid-missing-helper");
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(model.AddConstraint(relation: Polynomial(x).Le(Flt64.One), from: (IIntermediateSymbol?)null).IsOk);
        IConstraint<Flt64, LinearCategory> original = Assert.Single(((IMechanismModel<Flt64>)model).Constraints);
        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Same(original, Assert.Single(((IMechanismModel<Flt64>)model).Constraints));
    }

    [Fact]
    public void InputSnapshotAndResult_ShouldRemainStable() {
        RealVar x = BoundedVariable("sigmoid-snapshot");
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        terms[0] = new LinearMonomial<Flt64>(new Flt64(-1), x);
        function.Name = "renamed";
        function.DisplayName = "step";
        Assert.Equal("renamed", adapter.Name);
        Assert.Equal("step", adapter.DisplayName);
        Assert.Equal(Flt64.One, function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }));
        IList<LinearMonomial<Flt64>> result = Assert.IsAssignableFrom<IList<LinearMonomial<Flt64>>>(adapter.Polynomial.Monomials);
        Assert.Throws<NotSupportedException>(() => result[0] = new LinearMonomial<Flt64>(Flt64.One, x));
        Assert.Same(function.IndicatorVar, result[0].Symbol);
    }

    [Fact]
    public void DiscreteDelta_ShouldRequireAValidLatticeProof() {
        var x = new IntVar("sigmoid-discrete");
        x.Range.SetRange(ValueRange<Integer>.Of(new Integer(-2), new Integer(2)).Value);
        Assert.True(SigmoidFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            relation: Comparison.GE,
            strictBoundary: new Flt64(0.5),
            delta: Flt64.One
        ).IsOk);
        var shifted = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, new Flt64(-0.25)
        );
        Assert.True(SigmoidFunction<Flt64>.Create(
            condition: shifted,
            converter: IIntoValue<Flt64>.Identity,
            relation: Comparison.GE,
            strictBoundary: new Flt64(0.5),
            delta: Flt64.One
        ).IsFailed);
    }

    [Fact]
    public async Task NestedFunction_ShouldPrepareAndRequireItsRegistration() {
        RealVar x = BoundedVariable("sigmoid-nested-abs");
        var abs = new AbsFunction<Flt64>(Polynomial(x), IIntoValue<Flt64>.Identity);
        var inner = new LinearFunctionSymbolAdapter<Flt64>(abs);
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, inner) }, new Flt64(-0.5)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
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
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1), [abs.ResultVar] = Flt64.One,
            [abs.PositiveVar] = Flt64.Zero, [abs.NegativeVar] = Flt64.One, [abs.SignVar] = Flt64.Zero,
            [function.HelperVariables[2]] = new Flt64(0.5),
            [function.IndicatorVar] = Flt64.One, [function.HelperVariables[1]] = Flt64.Zero,
        };
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        assignment[function.IndicatorVar] = Flt64.Zero;
        assignment[function.HelperVariables[1]] = Flt64.One;
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 1)]
    public async Task NestedSigmoidConsumer_ShouldCacheProvenIndicatorEvidence(double input, int expected) {
        RealVar x = BoundedVariable("sigmoid-producer-x");
        SigmoidFunction<Flt64> sigmoid = SigmoidFunction<Flt64>.Create(
            condition: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var inner = new LinearFunctionSymbolAdapter<Flt64>(sigmoid);
        IfFunction<Flt64> consumer = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), inner) }, new Flt64(-1)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var outer = new LinearFunctionSymbolAdapter<Flt64>(consumer);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input) }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(new Flt64(expected), model.Tokens.CachedValue(inner, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(new Flt64(expected), model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task NestedPreparation_ShouldCheckTheExpandedBranch() {
        RealVar x = BoundedVariable("sigmoid-nested");
        var inner = new LinearExpressionSymbol(
            name: "sigmoid-inner",
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, new Flt64(1e16))
        );
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, inner) }, new Flt64(-1e16)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        var direct = new Dictionary<ISymbol, Flt64> { [inner] = new Flt64(1e16) };
        Assert.Equal(Flt64.Zero, function.Evaluate(direct));
        Assert.Null(adapter.Prepare(
            values: direct,
            tokenTable: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        ));
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
            );
        Assert.True(built.IsFailed);
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
        RealVar x = BoundedVariable($"sigmoid-{typeof(V).Name}");
        var input = new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, x) }, converter.Zero);
        Result<SigmoidFunction<V>, ErrorCode, Error<ErrorCode>> created = SigmoidFunction<V>.Create(
            condition: input,
            converter: converter,
            strictBoundary: converter.One,
            relation: Comparison.GE
        );
        Assert.True(created.IsOk, created.ToString());
        SigmoidFunction<V> function = created.Value;
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        Assert.Equal(TruthValue.True, function.Classify(new Dictionary<ISymbol, V> { [x] = converter.Zero }).Value);
        Assert.True(function.Evaluate(new Dictionary<ISymbol, V> { [x] = converter.Zero })!.Value.Eq(converter.One));
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, V>()));
        Assert.Equal(6, ((IMechanismModel<V>)model).Constraints.Count);
    }

    private static RealVar BoundedVariable(string name) {
        var x = new RealVar(name);
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        return x;
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero
    );
}
