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
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class NestedConditionalEvidenceTest {
    [Theory]
    [InlineData(false, Comparison.GT, -1, 0)]
    [InlineData(false, Comparison.GT, 1, 1)]
    [InlineData(false, Comparison.GE, -1, 0)]
    [InlineData(false, Comparison.GE, 0, 1)]
    [InlineData(false, Comparison.LT, -1, 1)]
    [InlineData(false, Comparison.LT, 1, 0)]
    [InlineData(false, Comparison.LE, 0, 1)]
    [InlineData(false, Comparison.LE, 1, 0)]
    [InlineData(true, Comparison.GT, -1, 0)]
    [InlineData(true, Comparison.GT, 1, 1)]
    [InlineData(true, Comparison.GE, -1, 0)]
    [InlineData(true, Comparison.GE, 0, 1)]
    [InlineData(true, Comparison.LT, -1, 1)]
    [InlineData(true, Comparison.LT, 1, 0)]
    [InlineData(true, Comparison.LE, 0, 1)]
    [InlineData(true, Comparison.LE, 1, 0)]
    public void ProvenBranch_ShouldPrepareDownstream(bool sigmoid, Comparison relation, double input, int expected) {
        RealVar x = Variable();
        var producer = new LinearFunctionSymbolAdapter<Flt64>(Producer(
            input: x,
            sigmoid: sigmoid,
            relation: relation
        ));
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: !sigmoid);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);

        Try result = new IIntermediateSymbol[] { consumer, producer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(input) }
        );

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(new Flt64(expected), Cached(parent, producer));
        Assert.Equal(new Flt64(expected), Cached(parent, consumer));
    }

    [Theory]
    [InlineData(false, -1, 0)]
    [InlineData(false, 1, 1)]
    [InlineData(true, -1, 0)]
    [InlineData(true, 1, 1)]
    public async Task MultiLayerBranches_ShouldPrepareInBothModelTypes(bool quadratic, double input, int expected) {
        RealVar x = Variable();
        var first = new LinearFunctionSymbolAdapter<Flt64>(Producer(x, sigmoid: true));
        LinearFunctionSymbolAdapter<Flt64> second = Consumer(first, sigmoid: false);
        LinearFunctionSymbolAdapter<Flt64> third = Consumer(second, sigmoid: true);
        var fixedValues = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(input) };
        if (quadratic) {
            using var parent = new QuadraticMetaModel<Flt64>();
            Assert.True(parent.Add(x).IsOk);
            Assert.True(parent.Add(third).IsOk);
            Assert.True(parent.Add(first).IsOk);
            Assert.True(parent.Add(second).IsOk);
            Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
                await QuadraticMechanismModel<Flt64>.InvokeAsync(metaModel: parent, fixedVariables: fixedValues);
            Assert.True(built.IsOk, built.ToString());
            using QuadraticMechanismModel<Flt64> model = built.Value;
            Assert.Equal(new Flt64(expected), model.Tokens.CachedValue(third, (IReadOnlyList<Flt64>?)null));
        }
        else {
            using var parent = new LinearMetaModel<Flt64>();
            Assert.True(parent.Add(x).IsOk);
            Assert.True(parent.Add(third).IsOk);
            Assert.True(parent.Add(first).IsOk);
            Assert.True(parent.Add(second).IsOk);
            Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
                await LinearMechanismModel<Flt64>.InvokeAsync(metaModel: parent, fixedVariables: fixedValues);
            Assert.True(built.IsOk, built.ToString());
            using LinearMechanismModel<Flt64> model = built.Value;
            Assert.Equal(new Flt64(expected), model.Tokens.CachedValue(third, (IReadOnlyList<Flt64>?)null));
        }
    }

    [Fact]
    public void IndependentBranches_ShouldPublishEvidenceTogether() {
        RealVar x = Variable();
        RealVar y = Variable();
        var left = new LinearFunctionSymbolAdapter<Flt64>(Producer(x, sigmoid: false));
        var right = new LinearFunctionSymbolAdapter<Flt64>(Producer(y, sigmoid: true));
        IfFunction<Flt64> function = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, left), new LinearMonomial<Flt64>(Flt64.One, right) },
                new Flt64(-1)
            ),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One,
            name: "aggregate"
        ).Value;
        var consumer = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Try result = new IIntermediateSymbol[] { consumer, right, left }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [y] = Flt64.One }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(Flt64.One, Cached(parent, consumer));
    }

    [Theory]
    [InlineData(false, -1, 0)]
    [InlineData(false, 1, 1)]
    [InlineData(true, -1, 0)]
    [InlineData(true, 1, 1)]
    public void OtherModelAwareConsumers_ShouldUseButNotPromoteConditionalEvidence(bool sigmoid, double input, int expected) {
        RealVar x = Variable();
        var producer = new LinearFunctionSymbolAdapter<Flt64>(Producer(x, sigmoid));
        var signal = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(2), producer) }, new Flt64(-1)
        );
        var interval = new LinearFunctionSymbolAdapter<Flt64>(IfInFunction<Flt64>.Create(
            x: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, producer) }, Flt64.Zero),
            lower: Flt64.One,
            upper: Flt64.One,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value);
        var conditionalValue = new LinearFunctionSymbolAdapter<Flt64>(IfThenFunction<Flt64>.Create(
            condition: signal,
            then: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(2)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value);
        var implication = new LinearFunctionSymbolAdapter<Flt64>(ImplyFunction<Flt64>.Create(
            antecedent: signal,
            consequent: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(-1)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value);
        LinearFunctionSymbolAdapter<Flt64> next = Consumer(interval, sigmoid: true);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { next, implication, conditionalValue, interval, producer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(input) }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(new Flt64(expected), Cached(parent, interval));
        Assert.Equal(new Flt64(2 * expected), Cached(parent, conditionalValue));
        Assert.Equal(expected == 0 ? Flt64.One : null, Cached(parent, implication));
        Assert.Null(Cached(parent, next));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingUndefinedOrOutOfBoundsInput_ShouldNotPublishEvidence(bool sigmoid) {
        foreach (double? input in new double?[] { null, 0.5, 2 }) {
            RealVar x = Variable();
            IMathFunctionSymbol<Flt64> function = Producer(
                input: x,
                sigmoid: sigmoid,
                bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1), Flt64.One)
            );
            var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
            LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: false);
            using var parent = new LinearMetaModel<Flt64>();
            Assert.True(parent.Add(x).IsOk);
            var values = new Dictionary<ISymbol, Flt64> { [new RealVar("anchor")] = Flt64.Zero };
            if (input is { } number) {
                values[x] = new Flt64(number);
            }
            Try result = new IIntermediateSymbol[] { producer, consumer }.Register(
                tokenTable: parent.Tokens,
                fixedValues: values
            );
            Assert.True(result.IsOk, result.ToString());
            Assert.Null(Cached(parent, consumer));
            if (input == 2) {
                Assert.Equal(Flt64.One, Cached(parent, producer));
            }
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void ConflictingAuxiliaryInput_ShouldNotPublishEvidence(bool sigmoid, int helperIndex) {
        RealVar x = Variable();
        IMathFunctionSymbol<Flt64> function = Producer(x, sigmoid);
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: false);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        var values = new Dictionary<ISymbol, Flt64> {
            [x] = Flt64.One,
            [function.HelperVariables[helperIndex]] = helperIndex == 1 ? Flt64.One : Flt64.Zero,
        };
        Try result = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: values
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(Flt64.One, Cached(parent, producer));
        Assert.Null(Cached(parent, consumer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitResultConflict_ShouldRemainAnError(bool sigmoid) {
        RealVar x = Variable();
        IMathFunctionSymbol<Flt64> function = Producer(x, sigmoid);
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: false);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [function.HelperVariables[0]] = Flt64.Zero }
        );
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result).Code);
        Assert.Null(Cached(parent, consumer));
        Assert.Null(parent.Tokens.Find(function.HelperVariables[0]));
    }

    [Fact]
    public void ConsistentAuxiliaryInputs_ShouldAllowEvidence() {
        RealVar x = Variable();
        IMathFunctionSymbol<Flt64> function = Producer(x, sigmoid: true);
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: false);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> {
                [x] = Flt64.One,
                [function.HelperVariables[1]] = Flt64.Zero,
                [function.HelperVariables[2]] = Flt64.One,
            }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(Flt64.One, Cached(parent, consumer));
    }

    [Fact]
    public async Task FixedSource_ShouldNotReuseAStaleHelperToken() {
        RealVar x = Variable();
        IMathFunctionSymbol<Flt64> function = Producer(x, sigmoid: true);
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: false);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(function.HelperVariables[0]).IsOk);
        Assert.True(parent.Add(producer).IsOk);
        Assert.True(parent.Add(consumer).IsOk);
        parent.Tokens.Find(function.HelperVariables[0])!.SetResult(Flt64.Zero);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
            );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(consumer, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(Flt64.Zero, model.Tokens.Find(function.HelperVariables[0])!.ResultWith(IIntoValue<Flt64>.Identity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgressCallbackChangingInput_ShouldInvalidateEarlierEvidence(bool changeRange) {
        RealVar x = Variable();
        var producer = new LinearFunctionSymbolAdapter<Flt64>(Producer(x, sigmoid: true));
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: false);
        var values = new Dictionary<ISymbol, Flt64> { [x] = Flt64.One };
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: values,
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 1) {
                    if (changeRange) {
                        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
                    }
                    else {
                        values[x] = new Flt64(-1);
                    }
                }
                return Results.OkInstance;
            }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Null(Cached(parent, consumer));
    }

    [Fact]
    public void ProgressCallbackChangingInput_ShouldInvalidateTheEntireProofChain() {
        RealVar x = Variable();
        var first = new LinearFunctionSymbolAdapter<Flt64>(Producer(x, sigmoid: false));
        LinearFunctionSymbolAdapter<Flt64> second = Consumer(first, sigmoid: true);
        LinearFunctionSymbolAdapter<Flt64> third = Consumer(second, sigmoid: false);
        var values = new Dictionary<ISymbol, Flt64> { [x] = Flt64.One };
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { first, second, third }.Register(
            tokenTable: parent.Tokens,
            fixedValues: values,
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 2) {
                    values[x] = new Flt64(-1);
                }
                return Results.OkInstance;
            }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(Flt64.One, Cached(parent, second));
        Assert.Null(Cached(parent, third));
    }

    [Fact]
    public void ChangedAbsoluteValueBounds_ShouldInvalidateEarlierEvidence() {
        RealVar x = Variable();
        var absolute = new AbsFunction<Flt64>(
            polynomial: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity
        );
        var producer = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        IfFunction<Flt64> condition = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, producer) }, new Flt64(-0.5)
            ),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var consumer = new LinearFunctionSymbolAdapter<Flt64>(condition);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = Flt64.One },
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 1) {
                    x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
                }
                return Results.OkInstance;
            }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Null(Cached(parent, consumer));
    }

    [Fact]
    public void ChangedNestedSource_ShouldInvalidateEarlierEvidence() {
        RealVar x = Variable();
        var expression = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero
            ),
            name: "conditional-evidence-expression"
        );
        var producer = new LinearFunctionSymbolAdapter<Flt64>(Producer(expression, sigmoid: true));
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: false);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { expression, producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = Flt64.One },
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 2) {
                    expression.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, x));
                }
                return Results.OkInstance;
            }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Null(Cached(parent, consumer));
    }

    [Fact]
    public void ConverterFailureAfterPreparation_ShouldNotPublishEarlierEvidence() {
        RealVar x = Variable();
        var converter = new SwitchingConverter();
        var producer = new LinearFunctionSymbolAdapter<Flt64>(SigmoidFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: converter,
            strictBoundary: Flt64.One
        ).Value);
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: false);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = Flt64.One },
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 1) {
                    converter.Reject = true;
                }
                return Results.OkInstance;
            }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Null(Cached(parent, consumer));
    }

    [Fact]
    public void ChangedReverseCoefficientConversion_ShouldNotPublishEvidence() {
        RealVar x = Variable();
        var converter = new SwitchingConverter();
        var producer = new LinearFunctionSymbolAdapter<Flt64>(IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) }, Flt64.Zero
            ),
            converter: converter,
            strictBoundary: new Flt64(0.5),
            relation: Comparison.LT
        ).Value);
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: true);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-2) },
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 1) {
                    converter.CorruptNegativeOne = true;
                }
                return Results.OkInstance;
            }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Null(Cached(parent, consumer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameLayerInputChange_ShouldNotPublishStaleEvidence(bool outsideBounds) {
        RealVar x = Variable();
        var values = new Dictionary<ISymbol, Flt64> { [x] = Flt64.One };
        var producer = new LinearFunctionSymbolAdapter<Flt64>(Producer(
            input: x,
            sigmoid: false,
            bounds: new LinearPolynomialBounds<Flt64>(new Flt64(-1), Flt64.One)
        ));
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: true);
        var mutator = new LinearFunctionSymbolAdapter<Flt64>(new MutatingFunction(x, () =>
            values[x] = new Flt64(outsideBounds ? 2 : -1)));
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { producer, mutator, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: values
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(Flt64.One, Cached(parent, producer));
        Assert.Null(Cached(parent, consumer));
    }

    [Fact]
    public void OutOfBoundsNaturalInteger_ShouldNotUseItsRoundedSolverValue() {
        var x = new IntVar("conditional-evidence-large-integer");
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var producer = new LinearFunctionSymbolAdapter<Integer>(IfFunction<Integer>.Create(
            condition: new LinearPolynomial<Integer>(new[] { new LinearMonomial<Integer>(Integer.One, x) }, Integer.Zero),
            converter: converter,
            strictBoundary: Integer.One,
            bounds: new LinearPolynomialBounds<Integer>(Integer.Zero, new Integer(9007199254740992L))
        ).Value);
        var consumer = new LinearFunctionSymbolAdapter<Integer>(SigmoidFunction<Integer>.Create(
            condition: new LinearPolynomial<Integer>(
                new[] { new LinearMonomial<Integer>(new Integer(2), producer) }, new Integer(-1)
            ),
            converter: converter,
            strictBoundary: Integer.One
        ).Value);
        using var parent = new LinearMetaModel<Integer>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            converter: converter,
            fixedValues: new Dictionary<ISymbol, Integer> { [x] = new Integer(9007199254740993L) }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(Integer.One, parent.Tokens.CachedValue(producer, (IReadOnlyList<Integer>?)null));
        Assert.Null(parent.Tokens.CachedValue(consumer, (IReadOnlyList<Integer>?)null));
    }

    [Fact]
    public void FailingProgressCallback_ShouldRollBackEvidenceAndHelpers() {
        RealVar x = Variable();
        IMathFunctionSymbol<Flt64> function = Producer(x, sigmoid: true);
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        LinearFunctionSymbolAdapter<Flt64> consumer = Consumer(producer, sigmoid: false);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Cache(
            cacheKey: consumer,
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(7)
        );
        Try result = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = Flt64.One },
            callBack: status => status.ReadySymbolAmount.ToULong() == 2
                ? Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "test"))
                : Results.OkInstance
        );
        Assert.True(result.IsFailed);
        Assert.Equal(new Flt64(7), Cached(parent, consumer));
        Assert.Null(parent.Tokens.Find(function.HelperVariables[0]));
        Assert.Empty(parent.Tokens.Symbols);
    }

    [Fact]
    public void RepresentativeNumericTypes_ShouldPropagateEvidence() {
        VerifyNumeric(IIntoValue<Flt64>.Identity);
        VerifyNumeric(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        VerifyNumeric(new TestNumberConverter<Integer>(value => value.ToInt64()));
        VerifyNumeric(new TestNumberConverter<FltX>(value => value.ToFltX()));
        VerifyNumeric(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    private static void VerifyNumeric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        RealVar x = Variable();
        var first = new LinearFunctionSymbolAdapter<V>(IfFunction<V>.Create(
            condition: new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, x) }, converter.Zero),
            converter: converter,
            strictBoundary: converter.One
        ).Value);
        var second = new LinearFunctionSymbolAdapter<V>(SigmoidFunction<V>.Create(
            condition: new LinearPolynomial<V>(
                new[] { new LinearMonomial<V>(converter.IntoValue(new Flt64(2)), first) },
                converter.IntoValue(new Flt64(-1))
            ),
            converter: converter,
            strictBoundary: converter.One
        ).Value);
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Try result = new IIntermediateSymbol[] { second, first }.Register(
            tokenTable: parent.Tokens,
            converter: converter,
            fixedValues: new Dictionary<ISymbol, V> { [x] = converter.One }
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.True(parent.Tokens.CachedValue(second, (IReadOnlyList<V>?)null) is { } cached && cached.Eq(converter.One));
    }

    private static IMathFunctionSymbol<Flt64> Producer(
        ISymbol input,
        bool sigmoid,
        Comparison relation = Comparison.GT,
        LinearPolynomialBounds<Flt64>? bounds = null) {
        var condition = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, Flt64.Zero);
        return sigmoid
            ? SigmoidFunction<Flt64>.Create(
                condition: condition,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One,
                relation: relation,
                bounds: bounds,
                name: $"producer-{Guid.NewGuid()}"
            ).Value
            : IfFunction<Flt64>.Create(
                condition: condition,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One,
                relation: relation,
                bounds: bounds,
                name: $"producer-{Guid.NewGuid()}"
            ).Value;
    }

    private static LinearFunctionSymbolAdapter<Flt64> Consumer(LinearFunctionSymbolAdapter<Flt64> input, bool sigmoid) {
        var condition = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(2), input) }, new Flt64(-1)
        );
        IMathFunctionSymbol<Flt64> function = sigmoid
            ? SigmoidFunction<Flt64>.Create(
                condition: condition,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One,
                name: $"consumer-{Guid.NewGuid()}"
            ).Value
            : IfFunction<Flt64>.Create(
                condition: condition,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One,
                name: $"consumer-{Guid.NewGuid()}"
            ).Value;
        return new LinearFunctionSymbolAdapter<Flt64>(function);
    }

    private static RealVar Variable() {
        var variable = new RealVar($"conditional-evidence-{Guid.NewGuid()}");
        variable.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        return variable;
    }

    private static Flt64? Cached(LinearMetaModel<Flt64> parent, IIntermediateSymbol symbol) =>
        parent.Tokens.CachedValue(symbol, (IReadOnlyList<Flt64>?)null);

    private sealed class MutatingFunction(ISymbol input, Action mutate) : IMathFunctionSymbol<Flt64> {
        public string Name { get; set; } = "mutating-function";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public IReadOnlyCollection<ISymbol> InputSymbols => new[] { input };
        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
            mutate();
            return Flt64.Zero;
        }
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) => Results.OkInstance;
        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) => Results.OkInstance;
    }

    private sealed class SwitchingConverter : IIntoValue<Flt64> {
        public bool Reject { get; set; }
        public bool CorruptNegativeOne { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => Reject
            ? throw new InvalidOperationException("test")
            : CorruptNegativeOne && value == new Flt64(-1) ? Flt64.One : value;
    }
}
