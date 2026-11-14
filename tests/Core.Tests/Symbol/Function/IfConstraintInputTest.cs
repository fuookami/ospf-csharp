#nullable enable

using System;
using System.Collections.Generic;
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
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class IfConstraintInputTest {
    [Theory]
    [InlineData(Comparison.GT)]
    [InlineData(Comparison.GE)]
    [InlineData(Comparison.LT)]
    [InlineData(Comparison.LE)]
    public async Task FromConstraint_ShouldUseNormalizedExpressionAndRelation(Comparison relation) {
        var x = new RealVar("if-input-x");
        var y = new RealVar("if-input-y");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var inequality = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(new Flt64(2), x)], Flt64.One),
            Rhs: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(Flt64.One, y)], new Flt64(2)),
            Comparison: relation
        );
        Result<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>> input = LinearConstraintInput<Flt64>.From(
            relation: inequality,
            lhsRange: ValueRange<Flt64>.Of(new Flt64(100), new Flt64(200)).Value,
            rhsConstant: new Flt64(99),
            name: "input-name",
            displayName: "input-display"
        );
        Assert.True(input.IsOk, input.ToString());
        Result<LinearFunctionSymbolAdapter<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.From(
            inequality: input.Value,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            name: "condition-name",
            displayName: "condition-display"
        );
        Assert.True(created.IsOk, created.ToString());
        LinearFunctionSymbolAdapter<Flt64> adapter = created.Value;
        IfFunction<Flt64> function = Assert.IsType<IfFunction<Flt64>>(adapter.Function);
        Assert.Equal("condition-name", adapter.Name);
        Assert.Equal("condition-display", adapter.DisplayName);
        Assert.Equal(relation, function.Relation);
        Assert.Equal(new Flt64(-7), function.Bounds.LowerBound);
        Assert.Equal(new Flt64(5), function.Bounds.UpperBound);
        Assert.Equal(2, function.InputSymbols.Count);
        Assert.Contains(x, function.InputSymbols);
        Assert.Contains(y, function.InputSymbols);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        foreach (double second in new[] { 1.0, 0.0, -0.5, -1.0, -1.25, -1.5, -2.0 }) {
            double difference = -second - 1.0;
            TruthValue expected = ConditionClassification.Classify(
                difference: new Flt64(difference),
                relation: relation,
                strictBoundary: new Flt64(0.5)
            ).Value;
            Flt64? result = function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero, [y] = new Flt64(second) });
            Assert.Equal(expected == TruthValue.Undefined ? null : expected == TruthValue.True ? Flt64.One : Flt64.Zero, result);
            for (int flag = 0; flag <= 1; ++flag) {
                var values = new Dictionary<IVariableItem, Flt64> {
                    [x] = Flt64.Zero, [y] = new Flt64(second), [function.ConditionVar] = new Flt64(difference),
                    [function.ResultVar] = new Flt64(flag), [function.FalseVar] = new Flt64(1 - flag),
                };
                bool feasible = expected != TruthValue.Undefined && flag == (expected == TruthValue.True ? 1 : 0);
                Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, values));
                Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
            }
        }
    }

    [Fact]
    public async Task FromConstraint_ShouldSnapshotTermsAndTrackBounds() {
        var x = new RealVar("if-input-snapshot");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        LinearConstraintInput<Flt64> input = Input(terms, Comparison.GT);
        Result<LinearFunctionSymbolAdapter<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.From(
            inequality: input,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        );
        Assert.True(created.IsOk, created.ToString());
        LinearFunctionSymbolAdapter<Flt64> adapter = created.Value;
        IfFunction<Flt64> function = Assert.IsType<IfFunction<Flt64>>(adapter.Function);
        terms.Clear();
        Assert.Single(function.Condition.Monomials);
        Assert.Equal(Flt64.One, function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }));
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel();
        Assert.True(dumped.IsOk, dumped.ToString());
        using LinearTriadModel matrix = dumped.Value;
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
        Assert.True(model.ToLinearTriadModel().IsFailed);
    }

    [Fact]
    public async Task FromConstraint_ShouldRetainManuallySuppliedFunctionDependencies() {
        var x = new RealVar("if-input-abs-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var abs = new AbsFunction<Flt64>(
            new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(Flt64.One, x)], Flt64.Zero),
            IIntoValue<Flt64>.Identity
        );
        var absAdapter = new LinearFunctionSymbolAdapter<Flt64>(abs);
        LinearConstraintInput<Flt64> input = Input([new LinearMonomial<Flt64>(Flt64.One, absAdapter)], Comparison.GT);
        Result<LinearFunctionSymbolAdapter<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.From(
            inequality: input,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        );
        Assert.True(created.IsOk, created.ToString());
        using var missing = new LinearMetaModel<Flt64>();
        Assert.True(missing.Add(x).IsOk);
        Assert.True(missing.Add(created.Value).IsOk);
        Assert.True((await LinearMechanismModel<Flt64>.InvokeAsync(missing)).IsFailed);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(absAdapter).IsOk);
        Assert.True(parent.Add(created.Value).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel();
        Assert.True(dumped.IsOk, dumped.ToString());
        using LinearTriadModel matrix = dumped.Value;
    }

    [Fact]
    public async Task FromConstraint_ShouldForwardDiscreteStepAndEnforceExplicitBounds() {
        var x = new IntVar("if-input-bounds");
        x.Range.SetRange(ValueRange<Integer>.Of(new Integer(-2), new Integer(2)).Value);
        LinearConstraintInput<Flt64> input = Input([new LinearMonomial<Flt64>(Flt64.One, x)], Comparison.GT);
        Result<LinearFunctionSymbolAdapter<Flt64>, ErrorCode, Error<ErrorCode>> created = IfFunction<Flt64>.From(
            inequality: input,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            delta: Flt64.One,
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1), Flt64.One)
        );
        Assert.True(created.IsOk, created.ToString());
        IfFunction<Flt64> function = Assert.IsType<IfFunction<Flt64>>(created.Value.Function);
        Assert.Equal(Flt64.One, function.Delta);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(created.Value).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = Flt64.One, [function.ConditionVar] = Flt64.One,
            [function.ResultVar] = Flt64.One, [function.FalseVar] = Flt64.Zero,
        };
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, values));
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
        values[x] = new Flt64(2);
        values[function.ConditionVar] = new Flt64(2);
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, values));
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
    }

    [Fact]
    public void FromConstraint_ShouldRejectInvalidInputsAndUnsupportedRelations() {
        var x = new RealVar("if-input-invalid");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        LinearConstraintInput<Flt64> valid = Input([new LinearMonomial<Flt64>(Flt64.One, x)], Comparison.GT);
        foreach (LinearConstraintInput<Flt64>? input in new[] {
            null,
            valid with { FlattenData = null! },
            valid with { FlattenData = new LinearFlattenData<Flt64>(null!, Flt64.Zero) },
            valid with { Sign = Comparison.EQ },
            valid with { Sign = Comparison.NE },
            valid with { Sign = (Comparison)999 },
        }) {
            Result<LinearFunctionSymbolAdapter<Flt64>, ErrorCode, Error<ErrorCode>> result = IfFunction<Flt64>.From(
                inequality: input,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            );
            Assert.Equal(ErrorCode.IllegalArgument,
                Assert.IsType<Failed<LinearFunctionSymbolAdapter<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
        }
        Assert.True(IfFunction<Flt64>.From(
            inequality: valid,
            converter: null,
            strictBoundary: Flt64.One
        ).IsFailed);
        Assert.True(IfFunction<Flt64>.From(
            inequality: valid,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            delta: Flt64.Zero
        ).IsFailed);
    }

    [Fact]
    public void FromConstraint_ShouldSupportRepresentativeNumericTypes() {
        VerifyNumeric(IIntoValue<Flt64>.Identity);
        VerifyNumeric(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        VerifyNumeric(new TestNumberConverter<Integer>(value => value.ToInt64()));
        VerifyNumeric(new TestNumberConverter<FltX>(value => value.ToFltX()));
        VerifyNumeric(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    private static void VerifyNumeric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var x = new RealVar($"if-constraint-{typeof(V).Name}");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var input = new LinearConstraintInput<V>(
            FlattenData: new LinearFlattenData<V>([new LinearMonomial<V>(converter.One, x)], converter.Zero),
            Sign: Comparison.GE,
            LhsRange: ValueRange<V>.Full(),
            Name: "",
            DisplayName: null,
            RhsConstant: converter.Zero
        );
        Result<LinearFunctionSymbolAdapter<V>, ErrorCode, Error<ErrorCode>> result = IfFunction<V>.From(
            inequality: input,
            converter: converter,
            strictBoundary: converter.One
        );
        Assert.True(result.IsOk, result.ToString());
        IMathFunctionSymbol<V> function = result.Value.Function;
        Assert.True(function.Evaluate(new Dictionary<ISymbol, V> { [x] = converter.One })!.Value.Eq(converter.One));
        Assert.True(function.Evaluate(new Dictionary<ISymbol, V> { [x] = converter.IntoValue(new Flt64(-1)) })!.Value.Eq(converter.Zero));
    }

    private static LinearConstraintInput<Flt64> Input(IReadOnlyList<LinearMonomial<Flt64>> terms, Comparison sign) => new(
        FlattenData: new LinearFlattenData<Flt64>(terms, Flt64.Zero),
        Sign: sign,
        LhsRange: ValueRange<Flt64>.Full(),
        Name: "input",
        DisplayName: null,
        RhsConstant: Flt64.Zero
    );
}
