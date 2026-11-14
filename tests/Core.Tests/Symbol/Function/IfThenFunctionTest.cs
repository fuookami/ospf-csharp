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

public class IfThenFunctionTest {
    [Theory]
    [InlineData(Comparison.GT, -1.0, TruthValue.False)]
    [InlineData(Comparison.GT, 0.25, TruthValue.Undefined)]
    [InlineData(Comparison.GT, 0.5, TruthValue.True)]
    [InlineData(Comparison.GE, -0.5, TruthValue.False)]
    [InlineData(Comparison.GE, -0.25, TruthValue.Undefined)]
    [InlineData(Comparison.GE, 0.0, TruthValue.True)]
    [InlineData(Comparison.LT, -0.5, TruthValue.True)]
    [InlineData(Comparison.LT, -0.25, TruthValue.Undefined)]
    [InlineData(Comparison.LT, 0.0, TruthValue.False)]
    [InlineData(Comparison.LE, 0.0, TruthValue.True)]
    [InlineData(Comparison.LE, 0.25, TruthValue.Undefined)]
    [InlineData(Comparison.LE, 0.5, TruthValue.False)]
    public async Task ConditionAndProduct_ShouldMatchRowsAndMatrix(Comparison relation, double input, TruthValue truth) {
        RealVar x = Real("ifthen-x", -2, 2);
        RealVar y = Real("ifthen-y", -3, 4);
        IfThenFunction<Flt64> function = Create(x, y, relation);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(input), [y] = new Flt64(-2) };
        Assert.Equal(truth, function.Classify(values).Value);
        Assert.Equal(truth == TruthValue.Undefined ? null : new Flt64(truth == TruthValue.True ? -2 : 0), function.Evaluate(values));
        for (int indicator = 0; indicator <= 1; ++indicator) {
            for (int bit = 0; bit <= 1; ++bit) {
                foreach (double result in new[] { -2.0, 0.0, 1.0 }) {
                    var assignment = new Dictionary<IVariableItem, Flt64> {
                        [x] = new Flt64(input),
                        [y] = new Flt64(-2),
                        [function.ConditionEncoding.ResultVar] = new Flt64(indicator),
                        [function.ConditionEncoding.FalseVar] = new Flt64(1 - indicator),
                        [function.ConditionEncoding.ConditionVar] = new Flt64(input),
                        [function.ProductEncoding.InputVar] = new Flt64(-2),
                        [function.ProductEncoding.BitVars[0]] = new Flt64(bit),
                        [function.ProductEncoding.MaskedVars[0]] = new Flt64(bit * -2),
                        [function.ResultVar] = new Flt64(result),
                    };
                    bool feasible = truth != TruthValue.Undefined && indicator == (truth == TruthValue.True ? 1 : 0) &&
                        bit == indicator && result == (truth == TruthValue.True ? -2 : 0);
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
                    Assert.Equal(feasible, SatisfiedAmountResidualTest.ExactFeasible(matrix, assignment));
                }
            }
        }
    }

    [Fact]
    public void Evaluation_ShouldShortCircuitAndRejectUnsafeThenArithmetic() {
        RealVar x = Real("ifthen-short-x", -2, 2);
        RealVar y = Real("ifthen-short-y", -2, 2);
        IfThenFunction<Flt64> function = Create(x, y);
        Assert.True(function.Classify(new Dictionary<ISymbol, Flt64>()).IsFailed);
        Assert.Null(function.TryEvaluate(new Dictionary<ISymbol, Flt64>()).Value);
        Assert.Equal(Flt64.Zero, function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }).Value);
        Assert.Null(function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.25) }).Value);
        Assert.Null(function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }).Value);
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [y] = new Flt64(double.NaN) }).IsFailed);

        var rounded = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, y) }, new Flt64(1e16));
        IfThenFunction<Flt64> unsafeThen = IfThenFunction<Flt64>.Create(
            condition: Polynomial(x),
            then: rounded,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            thenBounds: new LinearPolynomialBounds<Flt64>(new Flt64(1e16 - 4), new Flt64(1e16 + 4))
        ).Value;
        Assert.True(unsafeThen.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [y] = Flt64.One }).IsFailed);
        Assert.Equal(Flt64.Zero, unsafeThen.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }).Value);
    }

    [Fact]
    public async Task MissingProductHelper_ShouldFailBeforeConditionRowsAreAdded() {
        RealVar x = Real("ifthen-missing-x", -2, 2);
        RealVar y = Real("ifthen-missing-y", -3, 4);
        IfThenFunction<Flt64> function = Create(x, y);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        foreach (IVariableItem helper in function.HelperVariables.Where(item => item != function.ProductEncoding.InputVar)) {
            Assert.True(parent.Add(helper).IsOk);
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(function.RegisterConstraints(model).IsFailed);
        Assert.Empty(((IMechanismModel<Flt64>)model).Constraints);
    }

    [Fact]
    public async Task BoundsAndNestedThen_ShouldTrackRegistrationAndPreparation() {
        RealVar x = Real("ifthen-nested-x", -2, 2);
        RealVar y = Real("ifthen-nested-y", -2, 2);
        var inner = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, y) }, new Flt64(1e16)),
            name: "ifthen-nested"
        );
        IfThenFunction<Flt64> function = IfThenFunction<Flt64>.Create(
            condition: Polynomial(x),
            then: Polynomial(inner),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            thenBounds: new LinearPolynomialBounds<Flt64>(new Flt64(1e16 - 4), new Flt64(1e16 + 4))
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        parent.Tokens.Cache(
            cacheKey: adapter,
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(7)
        );
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [y] = Flt64.One }
        );
        Assert.Equal(ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>(built).Code);
        Assert.Equal(new Flt64(7), parent.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        var direct = new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [inner] = new Flt64(1e16) };
        Assert.Equal(new Flt64(1e16), function.TryEvaluate(direct).Value);
        Assert.Null(adapter.TryPrepare(direct, direct).Value);
        direct[y] = Flt64.One;
        Assert.Equal(ErrorCode.IllegalArgument,
            Assert.IsType<Failed<Flt64?, ErrorCode, Error<ErrorCode>>>(adapter.TryPrepare(direct, direct)).Code);
        inner.AsMutable().SetConstant(new Flt64(1e16 + 4));
        using var tokens = new ManualTokenList<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
        Assert.Empty(tokens.Tokens);
    }

    [Fact]
    public async Task NestedAbsThen_ShouldUseProvenHelperForFixedValuePreparation() {
        RealVar x = Real("ifthen-abs-x", -2, 2);
        RealVar y = Real("ifthen-abs-y", -3, 4);
        var abs = new AbsFunction<Flt64>(Polynomial(y), IIntoValue<Flt64>.Identity);
        var inner = new LinearFunctionSymbolAdapter<Flt64>(abs);
        IfThenFunction<Flt64> function = Create(x, inner);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [y] = new Flt64(-2) }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(new Flt64(2), model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public void MissingThenProof_ShouldClearAnEarlierManualCache() {
        RealVar x = Real("ifthen-cache-x", -2, 2);
        RealVar y = Real("ifthen-cache-y", -2, 2);
        var inner = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, y) }, Flt64.Zero),
            name: "ifthen-cache-inner"
        );
        IfThenFunction<Flt64> function = IfThenFunction<Flt64>.Create(
            condition: Polynomial(x),
            then: Polynomial(inner),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Tokens.Cache(
            cacheKey: adapter,
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(7)
        );
        adapter.PrepareAndCache(
            values: new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [inner] = Flt64.One },
            tokenTable: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.Null(parent.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task ExplicitThenBounds_ShouldConstrainEvenTheFalseBranch() {
        RealVar x = Real("ifthen-bounds-x", -2, 2);
        RealVar y = Real("ifthen-bounds-y", -3, 4);
        IfThenFunction<Flt64> function = IfThenFunction<Flt64>.Create(
            condition: Polynomial(x),
            then: Polynomial(y),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            thenBounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1), Flt64.One)
        ).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = Flt64.Zero, [y] = new Flt64(2),
            [function.ConditionEncoding.ResultVar] = Flt64.Zero,
            [function.ConditionEncoding.FalseVar] = Flt64.One,
            [function.ConditionEncoding.ConditionVar] = Flt64.Zero,
            [function.ProductEncoding.InputVar] = new Flt64(2),
            [function.ProductEncoding.BitVars[0]] = Flt64.Zero,
            [function.ProductEncoding.MaskedVars[0]] = Flt64.Zero,
            [function.ResultVar] = Flt64.Zero,
        };
        Assert.Equal(Flt64.Zero, function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }).Value);
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(5)).Value);
        Assert.True(model.ToLinearTriadModel().IsOk);
    }

    [Fact]
    public async Task InferredThenBounds_ShouldInvalidateRegistrationAndMatrix() {
        RealVar x = Real("ifthen-stale-x", -2, 2);
        RealVar y = Real("ifthen-stale-y", -3, 4);
        IfThenFunction<Flt64> function = Create(x, y);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(model.ToLinearTriadModel().IsOk);
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(5)).Value);
        Assert.True(model.ToLinearTriadModel().IsFailed);
        using var tokens = new ManualTokenList<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
        Assert.Empty(tokens.Tokens);
    }

    [Theory]
    [InlineData(Comparison.GT, 2, 1)]
    [InlineData(Comparison.GE, 0, 1)]
    [InlineData(Comparison.LT, -2, 1)]
    [InlineData(Comparison.LE, 0, 1)]
    public async Task ConstraintInputFactory_ShouldUseRelationAndDefaultThen(Comparison relation, double value, int expected) {
        RealVar x = Real("ifthen-input-x", -2, 2);
        var inequality = new LinearInequality<Flt64>(
            Lhs: Polynomial(x),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            Comparison: relation
        );
        Result<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>> input = LinearConstraintInput<Flt64>.From(
            relation: inequality,
            lhsRange: ValueRange<Flt64>.Full(),
            rhsConstant: Flt64.Zero
        );
        Assert.True(input.IsOk, input.ToString());
        Result<LinearFunctionSymbolAdapter<Flt64>, ErrorCode, Error<ErrorCode>> created = IfThenFunction<Flt64>.From(
            inequality: input.Value,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        );
        Assert.True(created.IsOk, created.ToString());
        Assert.Equal(new Flt64(expected), created.Value.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(value) }).Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(created.Value).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        built.Value.Dispose();
    }

    [Fact]
    public void Creation_ShouldRejectMutationDuringConditionFactoryAndInvalidThen() {
        RealVar x = Real("ifthen-factory-x", -2, 2);
        RealVar y = Real("ifthen-factory-y", -3, 4);
        var converter = new MutatingConverter(() => y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(5)).Value));
        Assert.True(IfThenFunction<Flt64>.Create(
            condition: Polynomial(x),
            then: Polynomial(y),
            converter: converter,
            strictBoundary: new Flt64(0.5)
        ).IsFailed);
        Assert.True(converter.Mutated);
        Assert.True(IfThenFunction<Flt64>.Create(
            condition: Polynomial(x),
            then: Polynomial(y),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            thenBounds: new LinearPolynomialBounds<Flt64>(Flt64.One, Flt64.Zero)
        ).IsFailed);
        Assert.True(IfThenFunction<Flt64>.Create(
            condition: Polynomial(x),
            then: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(new Flt64(double.NaN), y) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).IsFailed);
    }

    [Fact]
    public void InputSnapshots_ShouldNotFollowCallerListChanges() {
        RealVar x = Real("ifthen-snapshot-x", -2, 2);
        RealVar y = Real("ifthen-snapshot-y", -3, 4);
        var condition = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var then = new List<LinearMonomial<Flt64>> { new(Flt64.One, y) };
        IfThenFunction<Flt64> function = IfThenFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(condition, Flt64.Zero),
            then: new LinearPolynomial<Flt64>(then, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        condition.Clear();
        then.Clear();
        Assert.Equal(new Flt64(2), function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [y] = new Flt64(2) }).Value);
        Assert.Throws<NotSupportedException>(() => ((IList<LinearMonomial<Flt64>>)function.Condition.Monomials).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<LinearMonomial<Flt64>>)function.Then.Monomials).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<LinearMonomial<Flt64>>)function.ResultPolynomial.Monomials).Clear());
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
        RealVar x = Real($"ifthen-{typeof(V).Name}", -2, 2);
        var polynomial = new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, x) }, converter.Zero);
        Result<IfThenFunction<V>, ErrorCode, Error<ErrorCode>> created = IfThenFunction<V>.Create(
            condition: polynomial,
            then: polynomial,
            converter: converter,
            strictBoundary: converter.One
        );
        Assert.True(created.IsOk, created.ToString());
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(created.Value)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        Assert.True(created.Value.Evaluate(new Dictionary<ISymbol, V> { [x] = converter.One })!.Value.Eq(converter.One));
        Assert.True(created.Value.Evaluate(new Dictionary<ISymbol, V> { [x] = converter.Zero })!.Value.Eq(converter.Zero));
    }

    private static RealVar Real(string name, double lower, double upper) {
        var variable = new RealVar(name);
        variable.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(lower), new Flt64(upper)).Value);
        return variable;
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero
    );

    private static IfThenFunction<Flt64> Create(ISymbol condition, ISymbol then, Comparison relation = Comparison.GT) =>
        IfThenFunction<Flt64>.Create(
            condition: Polynomial(condition),
            then: Polynomial(then),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5),
            relation: relation
        ).Value;

    private sealed class NumberConverter<V>(Func<Flt64, V> convert) : IIntoValue<V>
        where V : struct, IRealNumber<V> {
        public V Zero => convert(Flt64.Zero);
        public V One => convert(Flt64.One);
        public V IntoValue(Flt64 value) => convert(value);
        public Flt64 FromValue(V value) => value.ToFlt64();
    }

    private sealed class MutatingConverter(Action mutation) : IIntoValue<Flt64> {
        public bool Mutated { get; private set; }
        public Flt64 Zero {
            get {
                if (!Mutated) {
                    Mutated = true;
                    mutation();
                }
                return Flt64.Zero;
            }
        }
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => value;
    }
}
