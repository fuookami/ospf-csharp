#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class NestedConditionalPreparationSafetyTest {
    [Theory]
    [InlineData(Comparison.GT, 1)]
    [InlineData(Comparison.LE, 1)]
    [InlineData(Comparison.GE, -1)]
    [InlineData(Comparison.LT, -1)]
    public async Task ExpandedNestedCondition_ShouldRejectAChangedBranch(Comparison relation, double input) {
        RealVar x = Variable("nested-condition-x");
        LinearExpressionSymbol inner = Expression(x, 1e16);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(inner, -1e16),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input) }
            );
        AssertIllegal(built);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task ExpandedNestedInterval_ShouldRejectAChangedSide(double input) {
        RealVar x = Variable("nested-interval-x");
        LinearExpressionSymbol inner = Expression(x, 1e16);
        IfInFunction<Flt64> function = IfInFunction<Flt64>.Create(
            x: Polynomial(inner),
            lower: new Flt64(1e16),
            upper: new Flt64(1e16),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        using var parent = new QuadraticMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input) }
            );
        AssertIllegal(built);
    }

    [Fact]
    public async Task HarmlessNestedRounding_ShouldStillPrepareTheBranch() {
        RealVar x = Variable("nested-harmless-x");
        LinearExpressionSymbol inner = Expression(x, 1e16);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(inner, -(1e16 - 4)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public void ExplicitPreparation_ShouldCheckExpandedValuesBeforeCaching() {
        RealVar x = Variable("nested-manual-x");
        LinearExpressionSymbol inner = Expression(x, 1e16);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(inner, -1e16),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        var values = new Dictionary<ISymbol, Flt64> { [inner] = new Flt64(1e16), [x] = Flt64.One };
        Assert.Null(adapter.Prepare(
            values: values,
            tokenTable: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        ));
        parent.Tokens.Cache(
            cacheKey: adapter,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        adapter.PrepareAndCache(
            values: values,
            tokenTable: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.Null(parent.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingExpandedInput_ShouldNotCacheTheDirectBranch(bool interval) {
        RealVar x = Variable("nested-missing-x");
        LinearExpressionSymbol inner = Expression(x, 1e16);
        IMathFunctionSymbol<Flt64> function = interval
            ? IfInFunction<Flt64>.Create(
                x: Polynomial(inner),
                lower: new Flt64(1e16),
                upper: new Flt64(1e16),
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: new Flt64(0.5)
            ).Value
            : IfFunction<Flt64>.Create(
                condition: Polynomial(inner, -1e16),
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: new Flt64(0.5)
            ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        var values = new Dictionary<ISymbol, Flt64> { [inner] = new Flt64(1e16) };
        Assert.NotNull(function.TryEvaluate(values).Value);
        Assert.Null(adapter.TryPrepare(values, values).Value);
        parent.Tokens.Cache(
            cacheKey: adapter,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        adapter.PrepareAndCache(
            values: values,
            tokenTable: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.Null(parent.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public void IntervalPreparation_ShouldCheckBothSidesEvenWhenTheCombinedResultIsFalse() {
        RealVar x = Variable("nested-both-sides-x");
        LinearExpressionSymbol inner = Expression(x, 1e16);
        IfInFunction<Flt64> function = IfInFunction<Flt64>.Create(
            x: Polynomial(inner),
            lower: new Flt64(1e16),
            upper: new Flt64(1e16),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        var values = new Dictionary<ISymbol, Flt64> { [inner] = new Flt64(1e16 + 2), [x] = new Flt64(-1) };
        Assert.Equal(Flt64.Zero, function.TryEvaluate(values).Value);
        AssertIllegal(adapter.TryPrepare(values, values));
    }

    [Fact]
    public void Registration_ShouldNotUseAStaleTokenForMissingExpandedValues() {
        RealVar x = Variable("nested-partial-x");
        LinearExpressionSymbol inner = Expression(x, 1e16);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(inner, -1e16),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.One);
        Try result = new IIntermediateSymbol[] { adapter, inner }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [inner] = new Flt64(1e16) }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Null(parent.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public void ManualPreparation_ShouldClearCacheAfterNestedMutation() {
        RealVar x = Variable("nested-mutated-manual-x");
        LinearExpressionSymbol inner = Expression(x, 0);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(inner),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        var values = new Dictionary<ISymbol, Flt64> { [inner] = Flt64.One, [x] = Flt64.One };
        adapter.PrepareAndCache(
            values: values,
            tokenTable: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.Equal(Flt64.One, parent.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        inner.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, x));
        adapter.PrepareAndCache(
            values: values,
            tokenTable: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.Null(parent.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task ExpandedVerification_ShouldNotOverflowWhenExactTermsCancel() {
        var x = new RealVar("nested-cancel-x");
        var y = new RealVar("nested-cancel-y");
        var inner = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, x),
                    new LinearMonomial<Flt64>(new Flt64(-1), y),
                },
                Flt64.Zero
            ),
            name: "nested-cancellation"
        );
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), inner) }, Flt64.One
            ),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-10), new Flt64(10))
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> {
                    [x] = new Flt64(1e308), [y] = new Flt64(1e308),
                }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Theory]
    [InlineData(-2, 1)]
    [InlineData(2, 1)]
    [InlineData(-2, -1)]
    [InlineData(2, -1)]
    public async Task ProvenAbsoluteValue_ShouldPrepareDespiteAStaleToken(double input, double coefficient) {
        RealVar x = Variable("nested-abs-proven-x");
        var abs = new AbsFunction<Flt64>(
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), x) }, Flt64.Zero
            ),
            converter: IIntoValue<Flt64>.Identity
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(abs);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(inner, -1),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var outer = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        Assert.True(parent.Tokens.Add(abs.ResultVar).IsOk);
        parent.Tokens.Find(abs.ResultVar)!.SetResult(Flt64.Zero);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input) }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task IndependentAbsoluteValues_ShouldPublishEvidenceAfterTheirSharedLayer() {
        RealVar x = Variable("nested-layer-x");
        RealVar y = Variable("nested-layer-y");
        var left = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(
            polynomial: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity,
            name: "left-absolute"
        ));
        var right = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(
            polynomial: Polynomial(y),
            converter: IIntoValue<Flt64>.Identity,
            name: "right-absolute"
        ));
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, left),
                    new LinearMonomial<Flt64>(Flt64.One, right),
                },
                new Flt64(-2)
            ),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var outer = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        Assert.True(parent.Add(right).IsOk);
        Assert.True(parent.Add(left).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(-2), [y] = Flt64.One }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task ExplicitHelperValue_ShouldTakePriorityOverDerivedEvidence() {
        RealVar x = Variable("nested-fixed-helper-x");
        var abs = new AbsFunction<Flt64>(Polynomial(x), IIntoValue<Flt64>.Identity);
        var inner = new LinearFunctionSymbolAdapter<Flt64>(abs);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(inner, -1),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> {
                    [x] = new Flt64(-2), [abs.ResultVar] = Flt64.Zero,
                }
            );
        AssertIllegal(built);
    }

    [Theory]
    [InlineData(false, 9007199254740993L, false)]
    [InlineData(true, 9007199254740993L, false)]
    [InlineData(false, 9007199254740994L, true)]
    [InlineData(true, 9007199254740994L, true)]
    public async Task NestedIntegerSolverConversion_ShouldCheckTheExpandedBranch(bool interval, long input, bool succeeds) {
        var x = new IntVar("nested-integer-x");
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var inner = new LinearFunctionSymbolAdapter<Integer>(new IntegerOffsetFunction(x));
        var polynomial = new LinearPolynomial<Integer>(
            new[] { new LinearMonomial<Integer>(Integer.One, inner) }, Integer.Zero
        );
        var bounds = new LinearPolynomialBounds<Integer>(new Integer(-2), new Integer(2));
        IMathFunctionSymbol<Integer> function = interval
            ? IfInFunction<Integer>.Create(
                x: polynomial,
                lower: Integer.Zero,
                upper: Integer.Zero,
                converter: converter,
                strictBoundary: Integer.One,
                bounds: bounds
            ).Value
            : IfFunction<Integer>.Create(
                condition: polynomial,
                converter: converter,
                strictBoundary: Integer.One,
                bounds: bounds
            ).Value;
        var outer = new LinearFunctionSymbolAdapter<Integer>(function);
        using var parent = new LinearMetaModel<Integer>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        Result<LinearMechanismModel<Integer>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Integer>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Integer> { [x] = new Integer(input) }
            );
        if (!succeeds) {
            AssertIllegal(built);
            return;
        }
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Integer> model = built.Value;
        Assert.Equal(interval ? Integer.Zero : Integer.One, model.Tokens.CachedValue(outer, (IReadOnlyList<Integer>?)null));
    }

    [Fact]
    public async Task UnverifiedNestedFunctionResult_ShouldNotBeCachedAsACondition() {
        RealVar x = Variable("nested-abs-rounded-x");
        var abs = new AbsFunction<Flt64>(Polynomial(x, 1e16), IIntoValue<Flt64>.Identity);
        var inner = new LinearFunctionSymbolAdapter<Flt64>(abs);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(inner, -1e16),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var outer = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(new Flt64(1e16), model.Tokens.CachedValue(inner, (IReadOnlyList<Flt64>?)null));
        Assert.Null(model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task AbsoluteValueOutsideTheProofSubset_ShouldRemainUncached() {
        RealVar x = Variable("nested-unproven-coefficient-x");
        var abs = new AbsFunction<Flt64>(
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) }, Flt64.Zero
            ),
            converter: IIntoValue<Flt64>.Identity
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(abs);
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: Polynomial(inner, -1),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var outer = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(new Flt64(2), model.Tokens.CachedValue(inner, (IReadOnlyList<Flt64>?)null));
        Assert.Null(model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public void UnrelatedAbsoluteValue_ShouldNotInvokeTheEvidenceConverter() {
        RealVar x = Variable("nested-unrelated-x");
        RealVar y = Variable("nested-condition-y");
        var converter = new CountingConverter();
        var absolute = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(Polynomial(x), converter));
        var condition = new LinearFunctionSymbolAdapter<Flt64>(IfFunction<Flt64>.Create(
            condition: Polynomial(y),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        converter.Calls = 0;
        Try result = new IIntermediateSymbol[] { absolute, condition }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-1), [y] = Flt64.One }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(0, converter.Calls);
        Assert.Equal(Flt64.One, parent.Tokens.CachedValue(absolute, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(Flt64.One, parent.Tokens.CachedValue(condition, (IReadOnlyList<Flt64>?)null));
    }

    private static RealVar Variable(string name) {
        var x = new RealVar(name);
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        return x;
    }

    private static LinearExpressionSymbol Expression(ISymbol input, double constant) => new(
        polynomial: new MutableLinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, new Flt64(constant)
        ),
        name: "nested-expression"
    );

    private static LinearPolynomial<Flt64> Polynomial(ISymbol input, double constant = 0) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, new Flt64(constant)
    );

    private static void AssertIllegal<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<T, ErrorCode, Error<ErrorCode>>>(result).Code);

    private sealed class CountingConverter : IIntoValue<Flt64> {
        public int Calls { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) {
            ++Calls;
            return value;
        }
    }

    private sealed class IntegerOffsetFunction(IVariableItem input) : IMathFunctionSymbol<Integer>, IHasResultPolynomial<Integer> {
        public string Name { get; set; } = "integer-offset";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public IReadOnlyCollection<ISymbol> InputSymbols => new ISymbol[] { input };
        public LinearPolynomial<Integer> ResultPolynomial => new(
            new[] { new LinearMonomial<Integer>(Integer.One, input) }, new Integer(-9007199254740992L)
        );
        public Integer? Evaluate(IReadOnlyDictionary<ISymbol, Integer> values) => TryEvaluate(values).Value;
        public Result<Integer?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, Integer> values) =>
            FunctionEvaluation.Polynomial(ResultPolynomial, values);
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Integer> tokens) => Results.OkInstance;
        public Try RegisterConstraints(IAbstractLinearMechanismModel<Integer> model) => Results.OkInstance;
    }
}
