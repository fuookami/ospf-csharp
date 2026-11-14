#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using MathUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class MechanismModelPreparationTest {
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Factories_ShouldSkipExistingAndRepeatedAuxiliaries(
        bool quadratic, bool manual, bool checkTokenExists) {
        var configuration = new MetaModelConfiguration(
            ManualTokenAddition: manual,
            CheckTokenExists: checkTokenExists
        );
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(configuration: configuration)
            : new LinearMetaModel<Flt64>(configuration: configuration);
        var function = new RepeatedAuxiliaryFunction();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(function.Helper)!;
        original.SetResult(new Flt64(9));

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(parent);

        result.IsOk.Should().BeTrue(because: result.ToString());
        using IMechanismModel<Flt64> model = result.Value;
        function.ReceivedTable.Should().BeTrue();
        parent.Tokens.Find(function.Helper).Should().BeSameAs(original);
        model.Tokens.Find(function.Helper).Should().BeSameAs(original);
        original.Result.Should().Be(new Flt64(9));
        Token<Flt64> extra = parent.Tokens.Find(function.Extra)!;
        model.Tokens.Find(function.Extra).Should().BeSameAs(extra);
        extra.SolverIndex.Should().Be(1);
        parent.Tokens.Tokens.Should().HaveCount(2);
        var next = new RealVar("repeated-auxiliary-next");
        parent.Add(next).IsOk.Should().BeTrue();
        parent.Tokens.Find(next)!.SolverIndex.Should().Be(2);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Factories_WhenParentReplacesTokenDuringPreparation_ShouldRejectCommit(bool quadratic, bool manual) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var variable = new RealVar("replacement-parent");
        parent.Add(variable).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(variable)!;
        original.SetResult(Flt64.One);
        using var before = new ConcurrentTokenTable<Flt64>(parent.Tokens);
        var function = new DelayedFunction("replacement-parent-function");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        bool changed = false;

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(
            parent: parent,
            callBack: status => {
                if (!changed) {
                    parent.Add(variable).IsOk.Should().BeTrue();
                    changed = true;
                }
                return Results.OkInstance;
            }
        );

        result.Should().BeOfType<Failed<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        Token<Flt64> replacement = parent.Tokens.Find(variable)!;
        replacement.Should().NotBeSameAs(original);
        replacement.SolverIndex.Should().Be(1);
        replacement.Result.Should().BeNull();
        before.TokenList.Find(variable).Should().BeSameAs(original);
        original.Result.Should().Be(Flt64.One);
        parent.Tokens.Find(function.Helper).Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Factories_ShouldPrepareFixedValuesWithoutChangingParent(bool quadratic, bool manual) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var x = new RealVar("prepared-x");
        var y = new RealVar("prepared-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.Tokens.Find(x)!.SetResult(Flt64.One);
        parent.Tokens.Find(y)!.SetResult(new Flt64(8));
        var leaf = LinearExpressionSymbol.FromVariable(x, "prepared-leaf");
        var outer = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), leaf) }, Flt64.One),
            name: "prepared-outer"
        );
        var unknown = LinearExpressionSymbol.FromVariable(y, "prepared-unknown");
        parent.Add(outer).IsOk.Should().BeTrue();
        parent.Add(unknown).IsOk.Should().BeTrue();
        parent.Add(leaf).IsOk.Should().BeTrue();
        parent.Tokens.Cache(
            cacheKey: leaf,
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(44)
        );
        var fixedValues = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(3) };
        var progress = new List<ulong>();

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(
            parent: parent,
            fixedValues: fixedValues,
            callBack: status => {
                progress.Add(status.ReadySymbolAmount.ToULong());
                return Results.OkInstance;
            }
        );

        result.IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> model = result.Value;
        model.Tokens.CachedValue(leaf, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(3));
        model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(7));
        model.Tokens.Cached(unknown, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        parent.Tokens.CachedValue(leaf, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(44));
        parent.Tokens.Cached(outer, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        model.Tokens.Find(x).Should().BeSameAs(parent.Tokens.Find(x));
        model.Tokens.Tokens.Should().HaveCount(2);
        parent.Tokens.Find(x)!.Result.Should().Be(Flt64.One);
        fixedValues.Should().ContainSingle();
        progress.Should().Equal(new[] { 0UL, 2UL, 3UL });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factories_ShouldPrepareNestedFunctionsInNativeNumericTypes(bool quadratic) {
        var decimalConverter = new NumberConverter<FltX>(value => value.ToFltX());
        using AbstractMetaModel<FltX> decimalParent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: decimalConverter)
            : new LinearMetaModel<FltX>(converter: decimalConverter);
        var x = new RealVar("decimal-fixed-x");
        decimalParent.Add(x).IsOk.Should().BeTrue();
        LinearFunctionSymbolAdapter<FltX> inner = Absolute(
            name: "decimal-inner",
            input: x,
            converter: decimalConverter
        );
        LinearFunctionSymbolAdapter<FltX> outer = Absolute(
            name: "decimal-outer",
            input: inner,
            converter: decimalConverter,
            bigM: new FltX(10000000000000000m)
        );
        decimalParent.Add(outer).IsOk.Should().BeTrue();
        decimalParent.Add(inner).IsOk.Should().BeTrue();
        var decimals = new Dictionary<IVariableItem, FltX> { [x] = new FltX(-9007199254740993.125m) };

        Result<IMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> decimalResult = await Build(
            decimalParent, fixedValues: decimals);
        decimalResult.IsOk.Should().BeTrue(because: decimalResult.ToString());
        using IMechanismModel<FltX> decimalModel = decimalResult.Value;
        decimalModel.Tokens.CachedValue(inner, (IReadOnlyList<FltX>?)null)
            .Should().Be(new FltX(9007199254740993.125m));
        decimalModel.Tokens.CachedValue(outer, (IReadOnlyList<FltX>?)null)
            .Should().Be(new FltX(9007199254740993.125m));

        var integerConverter = new NumberConverter<Integer>(value => value.ToInt64());
        using AbstractMetaModel<Integer> integerParent = quadratic
            ? new QuadraticMetaModel<Integer>(converter: integerConverter)
            : new LinearMetaModel<Integer>(converter: integerConverter);
        integerParent.Add(x).IsOk.Should().BeTrue();
        LinearFunctionSymbolAdapter<Integer> integerFunction = Absolute(
            name: "integer-inner",
            input: x,
            converter: integerConverter
        );
        integerParent.Add(integerFunction).IsOk.Should().BeTrue();
        var integers = new Dictionary<IVariableItem, Integer> { [x] = new Integer(-9007199254740993L) };
        Result<IMechanismModel<Integer>, ErrorCode, Error<ErrorCode>> integerResult = await Build(
            integerParent, fixedValues: integers);
        integerResult.IsOk.Should().BeTrue();
        using IMechanismModel<Integer> integerModel = integerResult.Value;
        integerModel.Tokens.CachedValue(integerFunction, (IReadOnlyList<Integer>?)null)
            .Should().Be(new Integer(9007199254740993L));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factories_ShouldReadExistingResultsThroughTheTypedConverter(bool quadratic) {
        var converter = new NumberConverter<FltX>(value => value.ToFltX());
        using AbstractMetaModel<FltX> parent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: converter)
            : new LinearMetaModel<FltX>(converter: converter);
        var x = new RealVar("typed-result-x");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<FltX> original = parent.Tokens.Find(x)!;
        original.SetResult(new FltX(-3.5m));
        LinearFunctionSymbolAdapter<FltX> function = Absolute(
            name: "typed-result-function",
            input: x,
            converter: converter
        );
        parent.Add(function).IsOk.Should().BeTrue();

        Result<IMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> result = await Build(parent);

        result.IsOk.Should().BeTrue(because: result.ToString());
        using IMechanismModel<FltX> model = result.Value;
        model.Tokens.Find(x).Should().BeSameAs(original);
        model.Tokens.CachedValue(function, (IReadOnlyList<FltX>?)null).Should().Be(new FltX(3.5m));
        original.Result.Should().Be(new FltX(-3.5m));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factories_ShouldRespectFixedVariableComparerAndTokenFallback(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("comparer-x");
        var alias = new RealVar("comparer-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Tokens.Find(x)!.SetResult(new Flt64(2));
        var leaf = LinearExpressionSymbol.FromVariable(x, "comparer-leaf");
        parent.Add(leaf).IsOk.Should().BeTrue();
        var fixedValues = new Dictionary<IVariableItem, Flt64>(new VariableNameComparer()) {
            [alias] = new Flt64(7),
        };

        using IMechanismModel<Flt64> fixedModel = (await Build(parent, fixedValues)).Value;
        fixedModel.Tokens.CachedValue(leaf, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(7));
        using IMechanismModel<Flt64> defaultModel = (await Build(parent)).Value;
        defaultModel.Tokens.CachedValue(leaf, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(2));
        fixedValues.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Factories_ShouldIsolateTokenResultsDuringPreparation(bool quadratic, bool manual, bool fail) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var x = new RealVar("isolated-result-x");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;
        original.SetResult(new Flt64(2));
        var symbol = new MutatingSymbol(x);
        parent.Add(symbol).IsOk.Should().BeTrue();
        int callbacks = original.RefreshCallbacks.Count;

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(
            parent: parent,
            callBack: status => fail && status.ReadySymbolAmount.ToULong() == 1
                ? Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "stopped"))
                : Results.OkInstance
        );

        result.IsFailed.Should().Be(fail);
        symbol.Seen.Should().Be(new Flt64(2));
        symbol.Token.Should().NotBeSameAs(original);
        parent.Tokens.Find(x).Should().BeSameAs(original);
        original.Result.Should().Be(new Flt64(2));
        if (!fail) {
            using IMechanismModel<Flt64> model = result.Value;
            model.Tokens.Find(x).Should().BeSameAs(original);
            model.Tokens.CachedValue(symbol, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(2));
        }
        original.RefreshCallbacks.Should().HaveCount(callbacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factories_ShouldPrepareUnregisteredExpressionDependenciesWithoutRegisteringThem(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("implicit-x");
        parent.Add(x).IsOk.Should().BeTrue();
        LinearFunctionSymbolAdapter<Flt64> inner = Absolute(
            name: "implicit-inner",
            input: x,
            converter: IIntoValue<Flt64>.Identity
        );
        var first = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), inner) }, Flt64.One),
            name: "same-name"
        );
        var second = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, first) }, Flt64.Zero),
            name: "same-name"
        );
        LinearFunctionSymbolAdapter<Flt64> outer = Absolute(
            name: "implicit-outer",
            input: second,
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(1e16)
        );
        parent.Add(outer).IsOk.Should().BeTrue();
        parent.Add(inner).IsOk.Should().BeTrue();
        var progress = new List<ulong>();

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(
            parent: parent,
            fixedValues: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(-3) },
            callBack: status => {
                progress.Add(status.ReadySymbolAmount.ToULong());
                status.TotalSymbolAmount.ToULong().Should().Be(2);
                return Results.OkInstance;
            }
        );

        result.IsOk.Should().BeTrue(because: result.ToString());
        using IMechanismModel<Flt64> model = result.Value;
        model.Tokens.CachedValue(first, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(7));
        model.Tokens.CachedValue(second, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(7));
        model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(7));
        model.Tokens.Symbols.Should().Equal(outer, inner);
        parent.Tokens.Symbols.Should().Equal(outer, inner);
        progress.Should().Equal(new[] { 0UL, 1UL, 2UL });
    }

    [Fact]
    public async Task QuadraticFactory_ShouldPrepareHiddenQuadraticDependencies() {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("implicit-quadratic-x");
        parent.Add(x).IsOk.Should().BeTrue();
        var hidden = new QuadraticExpressionSymbol(
            polynomial: new MutableQuadraticPolynomial<Flt64>(
                monomials: [new QuadraticMonomial<Flt64>(
                    Coefficient: Flt64.One,
                    Symbol1: x,
                    Symbol2: x
                )],
                constant: Flt64.One),
            name: "implicit-quadratic"
        );
        var outer = new QuadraticExpressionSymbol(
            polynomial: new MutableQuadraticPolynomial<Flt64>(
                monomials: [QuadraticMonomial<Flt64>.Linear(new Flt64(2), hidden)],
                constant: Flt64.Zero),
            name: "implicit-quadratic-outer"
        );
        parent.Add(outer).IsOk.Should().BeTrue();

        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(3) }
            );

        result.IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> model = result.Value;
        model.Tokens.CachedValue(hidden, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(10));
        model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(20));
        model.Tokens.Symbols.Should().Equal(outer);
        parent.Tokens.Symbols.Should().Equal(outer);
    }

    [Fact]
    public async Task FixedPreparation_ShouldNotImplicitlyEliminateMatrixColumns() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("matrix-fixed-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) }, Flt64.One),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        var fixedValues = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(3) };

        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(
            parent, fixedVariables: fixedValues)).Value;
        using LinearTriadModel ordinary = model.ToLinearTriadModel().Value;
        ordinary.Variables.Should().ContainSingle();
        ordinary.Objective.Cells.Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(2));
        using LinearTriadModel eliminated = model.ToLinearTriadModel(fixedValues).Value;
        eliminated.Variables.Should().BeEmpty();
        eliminated.Objective.Cells.Should().BeEmpty();
        eliminated.Objective.Constant.Should().Be(new Flt64(7));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factories_ShouldRejectNonFiniteFixedValuesWithoutLeakingSnapshots(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("invalid-fixed-x");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.Tokens.Find(x)!;
        int callbacks = token.RefreshCallbacks.Count;

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(
            parent, new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(double.NaN) });

        result.Should().BeOfType<Failed<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        token.RefreshCallbacks.Should().HaveCount(callbacks);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Factories_WhenProgressFails_ShouldDiscardHelpers(bool quadratic, bool fatal) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("progress-x");
        parent.Add(x).IsOk.Should().BeTrue();
        var function = new DelayedFunction("delayed-progress");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.Tokens.Find(x)!;
        int callbacks = token.RefreshCallbacks.Count;

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(
            parent: parent,
            callBack: status => status.ReadySymbolAmount.ToULong() == 0
                ? Results.OkInstance
                : fatal
                    ? new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                        new Err<ErrorCode>(ErrorCode.ApplicationStopped, "stopped"))
                    : Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "stopped"))
        );

        result.IsFailed.Should().BeTrue();
        (result is Fatal<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>).Should().Be(fatal);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        token.RefreshCallbacks.Should().HaveCount(callbacks);
        var next = new RealVar("progress-next");
        parent.Add(next).IsOk.Should().BeTrue();
        parent.Tokens.Find(next)!.SolverIndex.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factories_WhenProgressThrows_ShouldReturnFailureAndReleaseSnapshots(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("throwing-progress-x");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.Tokens.Find(x)!;
        int callbacks = token.RefreshCallbacks.Count;
        var function = new DelayedFunction("throwing-progress");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(
            parent: parent,
            callBack: status => throw new InvalidOperationException("progress failed")
        );

        result.Should().BeOfType<Failed<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationError);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        token.RefreshCallbacks.Should().HaveCount(callbacks);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Factories_WhenFunctionChangesWorkingTableOrThrows_ShouldNotCommit(bool quadratic, bool throwing) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("working-change-x");
        var extra = new RealVar("working-change-extra");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.Tokens.Find(x)!;
        int callbacks = token.RefreshCallbacks.Count;
        var function = new DelayedFunction("working-change");
        function.OnConstraints = tokens => {
            if (throwing) {
                throw new InvalidOperationException("function failed");
            }
            tokens.Add(extra).IsOk.Should().BeTrue();
        };
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(parent);

        result.Should().BeOfType<Failed<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationError);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        parent.Tokens.Find(extra).Should().BeNull();
        token.RefreshCallbacks.Should().HaveCount(callbacks);
        var next = new RealVar("working-change-next");
        parent.Add(next).IsOk.Should().BeTrue();
        parent.Tokens.Find(next)!.SolverIndex.Should().Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Factories_WhenWorkingTokenIsReplaced_ShouldRejectCommit(bool quadratic, bool manual) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var x = new RealVar("replaced-working-x");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;
        original.SetResult(new Flt64(3));
        int callbacks = original.RefreshCallbacks.Count;
        var function = new DelayedFunction("replaced-working");
        function.OnConstraints = tokens => {
            var table = (IAbstractMutableTokenTable<Flt64>)tokens;
            table.Add(x).IsOk.Should().BeTrue();
        };
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(parent);

        result.Should().BeOfType<Failed<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationError);
        parent.Tokens.Find(x).Should().BeSameAs(original);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        original.Result.Should().Be(new Flt64(3));
        original.RefreshCallbacks.Should().HaveCount(callbacks);
        var next = new RealVar("replaced-working-next");
        parent.Add(next).IsOk.Should().BeTrue();
        parent.Tokens.Find(next)!.SolverIndex.Should().Be(1);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Factories_ShouldCommitDelayedHelpersWithSharedIdentity(bool quadratic, bool manual) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var x = new RealVar("delayed-x");
        var removed = new RealVar("delayed-removed");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(removed).IsOk.Should().BeTrue();
        parent.Remove(removed);
        var function = new DelayedFunction("delayed-success");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        parent.Tokens.Find(function.Helper).Should().BeNull();

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(parent);

        result.IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> model = result.Value;
        Token<Flt64> helper = parent.Tokens.Find(function.Helper)!;
        model.Tokens.Find(function.Helper).Should().BeSameAs(helper);
        helper.SolverIndex.Should().Be(2);
        helper.RefreshCallbacks.Should().HaveCount(2);
        var next = new RealVar("delayed-next");
        parent.Add(next).IsOk.Should().BeTrue();
        parent.Tokens.Find(next)!.SolverIndex.Should().Be(3);
        model.Dispose();
        helper.RefreshCallbacks.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task FactoryAndDump_ValidateFixedValuesAtDifferentStages(bool quadratic, int invalid) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new BinVar("dump-boundary-x");
        var unknown = new BinVar("dump-boundary-unknown");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Tokens.Find(x)!.SetResult(Flt64.One);
        var function = new DelayedFunction("dump-boundary");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        var fixedValues = new Dictionary<IVariableItem, Flt64> {
            [invalid == 0 ? unknown : x] = invalid switch {
                1 => new Flt64(double.NaN),
                2 => new Flt64(2),
                _ => Flt64.Zero,
            },
        };

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await Build(parent, fixedValues);

        if (invalid == 1) {
            built.Should().BeOfType<Failed<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
            parent.Tokens.Find(function.Helper).Should().BeNull();
            parent.Tokens.Find(x)!.Result.Should().Be(Flt64.One);
            return;
        }
        built.IsOk.Should().BeTrue(because: built.ToString());
        using IMechanismModel<Flt64> model = built.Value;
        bool dumpFailed = model is LinearMechanismModel<Flt64> linear
            ? linear.ToLinearTriadModel(fixedValues).IsFailed
            : ((QuadraticMechanismModel<Flt64>)model).ToQuadraticTetradModel(fixedValues).IsFailed;
        dumpFailed.Should().BeTrue();
        parent.Tokens.Find(function.Helper).Should().NotBeNull();
        parent.Tokens.Find(x)!.Result.Should().Be(Flt64.One);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 0)]
    [InlineData(false, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, true, 2)]
    public async Task BuildAndDump_InvalidFixedValuesShouldNotCommitHelpers(bool quadratic, bool manual, int invalid) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var x = new BinVar("atomic-dump-invalid-x");
        var unknown = new BinVar("atomic-dump-unknown");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;
        original.SetResult(Flt64.One);
        int callbacks = original.RefreshCallbacks.Count;
        var function = new DelayedFunction("atomic-dump-invalid");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        var fixedValues = new Dictionary<IVariableItem, Flt64> {
            [invalid == 0 ? unknown : x] = invalid switch {
                1 => new Flt64(double.NaN),
                2 => new Flt64(2),
                _ => Flt64.Zero,
            },
        };

        Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>> result =
            await BuildWithMatrix(parent, fixedValues);

        result.Should().BeOfType<Failed<(IMechanismModel<Flt64>, IDisposable), ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        parent.Tokens.Find(x).Should().BeSameAs(original);
        original.Result.Should().Be(Flt64.One);
        original.RefreshCallbacks.Should().HaveCount(callbacks);
        var next = new RealVar("atomic-dump-next");
        parent.Add(next).IsOk.Should().BeTrue();
        parent.Tokens.Find(next)!.SolverIndex.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildAndDump_UnsupportedFreeBoundShouldNotCommitHelpers(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("atomic-dump-open-bound");
        x.Range.SetRange(ValueRange<Flt64>.Of(
            lb: Flt64.Zero,
            ub: new Flt64(2),
            lbInterval: new Interval.Open(),
            ubInterval: new Interval.Closed()
        ).Value);
        parent.Add(x).IsOk.Should().BeTrue();
        var function = new DelayedFunction("atomic-dump-open");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>> result =
            await BuildWithMatrix(parent);

        result.Should().BeOfType<Failed<(IMechanismModel<Flt64>, IDisposable), ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        parent.Tokens.Find(function.Helper).Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BuildAndDump_ShouldCommitAndRetainCapturedMapping(bool quadratic, bool manual) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var x = new BinVar("atomic-dump-success-x");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;
        int callbacks = original.RefreshCallbacks.Count;
        var function = new DelayedFunction("atomic-dump-success");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        var fixedValues = new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One };
        bool called = false;

        Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>> result = await BuildWithMatrix(
            parent: parent,
            fixedValues: fixedValues,
            callBack: _ => {
                called = true;
                fixedValues[x] = Flt64.Zero;
                return Results.OkInstance;
            }
        );

        result.IsOk.Should().BeTrue(because: result.ToString());
        using IMechanismModel<Flt64> mechanism = result.Value.Model;
        using IDisposable matrix = result.Value.Matrix;
        called.Should().BeTrue();
        parent.Tokens.Find(function.Helper).Should().NotBeNull();
        mechanism.Tokens.Find(function.Helper).Should().BeSameAs(parent.Tokens.Find(function.Helper));
        Restore(
            model: mechanism,
            matrix: matrix,
            solution: new[] { new Flt64(7) }
        ).Value.Should().Equal(Flt64.One, new Flt64(7));
        bool applied = mechanism switch {
            LinearMechanismModel<Flt64> linear => linear.ApplySolverSolution(new[] { Flt64.One, new Flt64(7) }).IsOk,
            QuadraticMechanismModel<Flt64> quadraticModel => quadraticModel.ApplySolverSolution(new[] { Flt64.One, new Flt64(7) }).IsOk,
            _ => false,
        };
        applied.Should().BeTrue();
        parent.Tokens.Find(function.Helper)!.Result.Should().Be(new Flt64(7));
        fixedValues[x].Should().Be(Flt64.Zero);
        mechanism.Dispose();
        original.RefreshCallbacks.Should().HaveCount(callbacks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildAndDump_ShouldAllowFixedDelayedHelpers(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new BinVar("atomic-dump-fixed-helper-x");
        parent.Add(x).IsOk.Should().BeTrue();
        var function = new DelayedFunction("atomic-dump-fixed-helper");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        parent.Tokens.Find(function.Helper).Should().BeNull();

        Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>> result =
            await BuildWithMatrix(parent, new Dictionary<IVariableItem, Flt64> {
                [x] = Flt64.One,
                [function.Helper] = new Flt64(7),
            });

        result.IsOk.Should().BeTrue(because: result.ToString());
        using IMechanismModel<Flt64> mechanism = result.Value.Model;
        using IDisposable matrix = result.Value.Matrix;
        parent.Tokens.Find(function.Helper).Should().NotBeNull();
        Restore(
            model: mechanism,
            matrix: matrix,
            solution: Array.Empty<Flt64>()
        ).Value.Should().Equal(Flt64.One, new Flt64(7));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BuildAndDump_ShouldPreserveParentChangesOnFailure(bool quadratic, bool invalidFixedValue) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new BinVar("atomic-dump-parent-x");
        var extra = new RealVar("atomic-dump-parent-extra");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;
        int callbacks = original.RefreshCallbacks.Count;
        var function = new DelayedFunction("atomic-dump-parent");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        bool changed = false;

        Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>> result = await BuildWithMatrix(
            parent: parent,
            fixedValues: new Dictionary<IVariableItem, Flt64> { [x] = invalidFixedValue ? new Flt64(2) : Flt64.One },
            callBack: _ => {
                if (!changed) {
                    changed = true;
                    parent.Add(extra).IsOk.Should().BeTrue();
                }
                return Results.OkInstance;
            }
        );

        changed.Should().BeTrue();
        result.IsFailed.Should().BeTrue();
        parent.Tokens.Find(extra).Should().NotBeNull();
        parent.Tokens.Find(function.Helper).Should().BeNull();
        parent.Tokens.Find(x).Should().BeSameAs(original);
        original.RefreshCallbacks.Should().HaveCount(callbacks);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BuildAndDump_CancellationShouldNotCommitHelpers(bool quadratic, bool cancelDuringRegistration) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("atomic-dump-cancel-x");
        parent.Add(x).IsOk.Should().BeTrue();
        var function = new DelayedFunction("atomic-dump-cancel");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using var cancellation = new CancellationTokenSource();
        if (!cancelDuringRegistration) {
            cancellation.Cancel();
        }
        bool called = false;

        Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>> result = await BuildWithMatrix(
            parent: parent,
            callBack: _ => {
                called = true;
                cancellation.Cancel();
                return Results.OkInstance;
            },
            cancellationToken: cancellation.Token
        );

        result.Should().BeOfType<Failed<(IMechanismModel<Flt64>, IDisposable), ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        called.Should().Be(cancelDuringRegistration);
        parent.Tokens.Find(function.Helper).Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BuildAndDump_UnreadableOrCancelledFixedValuesShouldNotRegister(bool quadratic, bool unreadable) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("atomic-dump-map-x");
        parent.Add(x).IsOk.Should().BeTrue();
        var function = new DelayedFunction("atomic-dump-map");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using var cancellation = new CancellationTokenSource();
        bool called = false;
        var fixedValues = new ObservedFixedValues(x, () => {
            if (unreadable) {
                throw new InvalidOperationException("unreadable fixed values");
            }
            cancellation.Cancel();
        });

        Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>> result = await BuildWithMatrix(
            parent: parent,
            fixedValues: fixedValues,
            callBack: _ => {
                called = true;
                return Results.OkInstance;
            },
            cancellationToken: cancellation.Token
        );

        result.Should().BeOfType<Failed<(IMechanismModel<Flt64>, IDisposable), ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(unreadable ? ErrorCode.IllegalArgument : ErrorCode.ApplicationStopped);
        called.Should().BeFalse();
        parent.Tokens.Find(function.Helper).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuildAndDump_ShouldPropagateFatalRegistrationFailure(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var function = new DelayedFunction("atomic-dump-fatal");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>> result = await BuildWithMatrix(
            parent: parent,
            callBack: _ => new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.ApplicationFailed, "fatal"))
        );

        result.Should().BeOfType<Fatal<(IMechanismModel<Flt64>, IDisposable), ErrorCode, Error<ErrorCode>>>();
        parent.Tokens.Find(function.Helper).Should().BeNull();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task PreCommitFailure_ShouldPreserveParentTokens(bool quadratic, int failure) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("atomic-dump-precommit-x");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;
        int callbacks = original.RefreshCallbacks.Count;
        var function = new DelayedFunction("atomic-dump-precommit");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Try FailPreparation() => failure switch {
            0 => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "failed")),
            1 => new Fatal<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "fatal")),
            2 => throw new InvalidOperationException("pre-commit failed"),
            _ => null!,
        };
        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = parent is LinearMetaModel<Flt64> linear
            ? (await LinearMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                metaModel: linear,
                fixedVariables: null,
                registrationStatusCallBack: null,
                beforeCommit: _ => FailPreparation()
            )).Map<IMechanismModel<Flt64>>(model => model)
            : (await QuadraticMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                metaModel: (QuadraticMetaModel<Flt64>)parent,
                fixedVariables: null,
                registrationStatusCallBack: null,
                beforeCommit: _ => FailPreparation()
            )).Map<IMechanismModel<Flt64>>(model => model);

        result.IsFailed.Should().BeTrue();
        (result is Fatal<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>).Should().Be(failure == 1);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        parent.Tokens.Find(x).Should().BeSameAs(original);
        original.RefreshCallbacks.Should().HaveCount(callbacks);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task ParentStructureChangeDuringBuild_ShouldNotCommitHelpers(bool quadratic, bool preCommit, bool objective) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("structure-build-x");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;
        int callbacks = original.RefreshCallbacks.Count;
        var function = new DelayedFunction("structure-build");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        bool changed = false;

        Try Change() {
            if (changed) {
                return Results.OkInstance;
            }
            changed = true;
            if (parent is LinearMetaModel<Flt64> linear) {
                return objective
                    ? linear.AddObject(
                        category: ObjectCategory.Minimum,
                        flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                            new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
                        name: "added",
                        displayName: null
                    )
                    : linear.AddConstraint(x, group: null);
            }
            var quadraticParent = (QuadraticMetaModel<Flt64>)parent;
            return objective
                ? quadraticParent.AddObject(
                    category: ObjectCategory.Minimum,
                    flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
                    name: "added",
                    displayName: null
                )
                : quadraticParent.AddConstraint(x, group: null);
        }

        RegistrationStatusCallBack? progress = preCommit ? null : _ => Change();
        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = parent is LinearMetaModel<Flt64> linearParent
            ? (await LinearMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                metaModel: linearParent,
                fixedVariables: null,
                registrationStatusCallBack: progress,
                beforeCommit: preCommit ? _ => Change() : null
            )).Map<IMechanismModel<Flt64>>(model => model)
            : (await QuadraticMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                metaModel: (QuadraticMetaModel<Flt64>)parent,
                fixedVariables: null,
                registrationStatusCallBack: progress,
                beforeCommit: preCommit ? _ => Change() : null
            )).Map<IMechanismModel<Flt64>>(model => model);

        changed.Should().BeTrue();
        result.IsFailed.Should().BeTrue();
        parent.Constraints.Should().HaveCount(objective ? 0 : 1);
        int objectives = parent is LinearMetaModel<Flt64> linearModel
            ? linearModel.MetaSubObjects.Count
            : ((QuadraticMetaModel<Flt64>)parent).FlattenSubObjects.Count;
        objectives.Should().Be(objective ? 1 : 0);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        parent.Tokens.Find(x).Should().BeSameAs(original);
        original.RefreshCallbacks.Should().HaveCount(callbacks);
    }

    [Fact]
    public async Task BuildAndDump_ShouldRejectNullSources() {
        LinearMetaModel<Flt64>? linear = null;
        QuadraticMetaModel<Flt64>? quadratic = null;

        (await linear.BuildLinearTriadModelAsync()).IsFailed.Should().BeTrue();
        (await quadratic.BuildQuadraticTetradModelAsync()).IsFailed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BuildAndDump_CommitFailureRollsBackFallbackIdentities(bool quadratic, bool manual) {
        var registry = new ModelElementIdentityRegistry();
        var configuration = new MetaModelConfiguration(ManualTokenAddition: manual);
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(configuration: configuration, identityRegistry: registry)
            : new LinearMetaModel<Flt64>(configuration: configuration, identityRegistry: registry);
        object reserved = new();
        object stable = new();
        registry.VariableId(reserved, 0).Value.Should().Be("model-local-variable:0");
        registry.RegisterVariable(
            element: stable,
            id: new VariableId("variable:stable"),
            origin: new ModelElementOrigin("variable", "stable")
        ).IsOk.Should().BeTrue();
        ModelElementIdentity[] before = registry.Entries().ToArray();

        var x = new RealVar("identity-commit-x");
        var transient = new RealVar("identity-commit-transient");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;
        var function = new DelayedFunction("identity-commit-helper");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        bool changed = false;
        Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>> failed = await BuildWithMatrix(
            parent: parent,
            callBack: status => {
                if (!changed && status.ReadySymbolAmount.ToULong() == 1) {
                    changed = true;
                    parent.Add(transient).IsOk.Should().BeTrue();
                    parent.Remove(transient);
                }
                return Results.OkInstance;
            }
        );

        changed.Should().BeTrue();
        failed.IsFailed.Should().BeTrue();
        parent.Tokens.Find(x).Should().BeSameAs(original);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        registry.Entries().Should().Equal(before);
        registry.Identity(stable)!.Id.Value.Should().Be("variable:stable");
        registry.VariableId(new object(), 0).Value.Should().Be("model-local-variable:0:collision-1");
        registry.Validate().IsOk.Should().BeTrue();

        Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>> retry = await BuildWithMatrix(parent);
        retry.IsOk.Should().BeTrue(because: retry.ToString());
        using IMechanismModel<Flt64> mechanism = retry.Value.Model;
        using IDisposable matrix = retry.Value.Matrix;
        parent.Tokens.Find(function.Helper).Should().NotBeNull();
        Restore(mechanism, matrix, new[] { Flt64.Zero, Flt64.Zero }).IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    public async Task FailureAfterDumpRollsBackFallbackIdentities(bool quadratic, int failure) {
        var registry = new ModelElementIdentityRegistry();
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(identityRegistry: registry)
            : new LinearMetaModel<Flt64>(identityRegistry: registry);
        object reserved = new();
        registry.VariableId(reserved, 0).Value.Should().Be("model-local-variable:0");
        ModelElementIdentity[] before = registry.Entries().ToArray();
        var x = new RealVar("identity-precommit-x");
        var extra = new RealVar("identity-precommit-extra");
        parent.Add(x).IsOk.Should().BeTrue();
        var function = new DelayedFunction("identity-precommit-helper");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        IDisposable? matrix = null;
        bool changed = false;

        Try AfterDump<TMatrix>(Result<TMatrix, ErrorCode, Error<ErrorCode>> dumped)
            where TMatrix : class, IDisposable {
            if (!dumped.IsOk) {
                return dumped.Map(_ => Results.SuccessInstance);
            }
            matrix = dumped.Value;
            switch (failure) {
                case 0:
                    return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "failed"));
                case 1:
                    return new Fatal<Success, ErrorCode, Error<ErrorCode>>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "fatal"));
                case 2:
                    throw new InvalidOperationException("pre-commit failed");
                case 3:
                    changed = parent is LinearMetaModel<Flt64> linearParent
                        ? linearParent.AddConstraint(x, group: null).IsOk
                        : ((QuadraticMetaModel<Flt64>)parent).AddConstraint(x, group: null).IsOk;
                    return Results.OkInstance;
                case 4:
                    changed = parent.Add(extra).IsOk;
                    parent.Remove(extra);
                    return Results.OkInstance;
                default:
                    return null!;
            }
        }

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = parent is LinearMetaModel<Flt64> linear
            ? (await LinearMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                metaModel: linear,
                fixedVariables: null,
                registrationStatusCallBack: null,
                beforeCommit: mechanism => AfterDump(mechanism.ToLinearTriadModel())
            )).Map<IMechanismModel<Flt64>>(model => model)
            : (await QuadraticMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                metaModel: (QuadraticMetaModel<Flt64>)parent,
                fixedVariables: null,
                registrationStatusCallBack: null,
                beforeCommit: mechanism => AfterDump(mechanism.ToQuadraticTetradModel())
            )).Map<IMechanismModel<Flt64>>(model => model);

        matrix.Should().NotBeNull();
        matrix?.Dispose();
        result.IsFailed.Should().BeTrue();
        if (failure == 1) {
            result.Should().BeOfType<Fatal<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        }
        else {
            ErrorCode code = failure switch {
                0 or 3 => ErrorCode.IllegalArgument,
                _ => ErrorCode.ApplicationError,
            };
            result.Should().BeOfType<Failed<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(code);
        }
        changed.Should().Be(failure is 3 or 4);
        parent.Constraints.Should().HaveCount(failure == 3 ? 1 : 0);
        parent.Tokens.Find(extra).Should().BeNull();
        parent.Tokens.Find(function.Helper).Should().BeNull();
        registry.Entries().Should().Equal(before);
        registry.VariableId(new object(), 0).Value.Should().Be("model-local-variable:0:collision-1");
        registry.Validate().IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Factories_WhenFunctionConstraintsFail_ShouldNotCommitHelpers(bool quadratic, bool fatal) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("failed-x");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;
        int callbacks = original.RefreshCallbacks.Count;
        var function = new DelayedFunction(
            name: "delayed-failure",
            failConstraints: true,
            fatal: fatal
        );
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(parent);

        result.IsFailed.Should().BeTrue();
        (result is Fatal<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>).Should().Be(fatal);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        parent.Tokens.Find(x).Should().BeSameAs(original);
        original.RefreshCallbacks.Should().HaveCount(callbacks);
        var next = new RealVar("failed-next");
        parent.Add(next).IsOk.Should().BeTrue();
        parent.Tokens.Find(next)!.SolverIndex.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factories_WhenParentChangesDuringPreparation_ShouldNotLoseChanges(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("changed-x");
        var added = new RealVar("changed-added");
        parent.Add(x).IsOk.Should().BeTrue();
        var function = new DelayedFunction("delayed-changed");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;
        int callbacks = original.RefreshCallbacks.Count;

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(
            parent: parent,
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 1) {
                    parent.Add(added).IsOk.Should().BeTrue();
                }
                return Results.OkInstance;
            }
        );

        result.IsFailed.Should().BeTrue();
        parent.Tokens.Find(added).Should().NotBeNull();
        parent.Tokens.Find(function.Helper).Should().BeNull();
        original.RefreshCallbacks.Should().HaveCount(callbacks);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Factories_WhenParentConsumesIndexDuringPreparation_ShouldRejectCommit(bool quadratic, bool manual) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var original = new RealVar("index-change-original");
        var transient = new RealVar("index-change-transient");
        parent.Add(original).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.Tokens.Find(original)!;
        var function = new DelayedFunction("index-change-helper");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        bool changed = false;

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(
            parent: parent,
            callBack: status => {
                if (!changed && status.ReadySymbolAmount.ToULong() == 1) {
                    changed = true;
                    parent.Add(transient).IsOk.Should().BeTrue();
                    parent.Remove(transient);
                }
                return Results.OkInstance;
            }
        );

        changed.Should().BeTrue();
        result.Should().BeOfType<Failed<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        parent.Tokens.Find(original).Should().BeSameAs(token);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        var next = new RealVar("index-change-next");
        parent.Add(next).IsOk.Should().BeTrue();
        parent.Tokens.Find(next)!.SolverIndex.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factories_WhenParentClosesDuringPreparation_ShouldReleaseSnapshots(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("closed-x");
        parent.Add(x).IsOk.Should().BeTrue();
        var function = new DelayedFunction("delayed-closed");
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Token<Flt64> original = parent.Tokens.Find(x)!;

        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await Build(
            parent: parent,
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 1) {
                    parent.Close();
                }
                return Results.OkInstance;
            }
        );

        result.Should().BeOfType<Failed<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        parent.Tokens.Find(function.Helper).Should().BeNull();
        original.RefreshCallbacks.Should().BeEmpty();
    }

    private static AbstractMetaModel<Flt64> Parent(bool quadratic, bool manual) => quadratic
        ? new QuadraticMetaModel<Flt64>(configuration: new MetaModelConfiguration(ManualTokenAddition: manual))
        : new LinearMetaModel<Flt64>(configuration: new MetaModelConfiguration(ManualTokenAddition: manual));

    private static async Task<Result<(IMechanismModel<Flt64> Model, IDisposable Matrix), ErrorCode, Error<ErrorCode>>> BuildWithMatrix(
        AbstractMetaModel<Flt64> parent,
        IReadOnlyDictionary<IVariableItem, Flt64>? fixedValues = null,
        RegistrationStatusCallBack? callBack = null,
        CancellationToken cancellationToken = default) {
        if (parent is LinearMetaModel<Flt64> linear) {
            return (await linear.BuildLinearTriadModelAsync(
                fixedVariables: fixedValues,
                registrationStatusCallBack: callBack,
                cancellationToken: cancellationToken
            )).Map<(IMechanismModel<Flt64>, IDisposable)>(result => (result.Model, result.Matrix));
        }
        return (await ((QuadraticMetaModel<Flt64>)parent).BuildQuadraticTetradModelAsync(
            fixedVariables: fixedValues,
            registrationStatusCallBack: callBack,
            cancellationToken: cancellationToken
        )).Map<(IMechanismModel<Flt64>, IDisposable)>(result => (result.Model, result.Matrix));
    }

    private static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> Restore(
        IMechanismModel<Flt64> model,
        IDisposable matrix,
        IReadOnlyList<Flt64> solution) => (model, matrix) switch {
        (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.RestoreSolverSolution(triad, solution),
        (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.RestoreSolverSolution(tetrad, solution),
        _ => throw new InvalidOperationException("unexpected mechanism or matrix model"),
    };

    private static async Task<Result<IMechanismModel<V>, ErrorCode, Error<ErrorCode>>> Build<V>(
        AbstractMetaModel<V> parent,
        IReadOnlyDictionary<IVariableItem, V>? fixedValues = null,
        RegistrationStatusCallBack? callBack = null)
        where V : struct, IRealNumber<V>, INumberField<V> {
        if (parent is LinearMetaModel<V> linear) {
            return (await LinearMechanismModel<V>.InvokeAsync(
                metaModel: linear,
                fixedVariables: fixedValues,
                registrationStatusCallBack: callBack
            )).Map<IMechanismModel<V>>(model => model);
        }
        return (await QuadraticMechanismModel<V>.InvokeAsync(
            metaModel: (QuadraticMetaModel<V>)parent,
            fixedVariables: fixedValues,
            registrationStatusCallBack: callBack
        )).Map<IMechanismModel<V>>(model => model);
    }

    private static LinearFunctionSymbolAdapter<V> Absolute<V>(string name, ISymbol input, IIntoValue<V> converter, V? bigM = null)
        where V : struct, IRealNumber<V>, INumberField<V> => new(new AbsFunction<V>(
            polynomial: new LinearPolynomial<V>(
                new[] { new LinearMonomial<V>(converter.One, input) }, converter.Zero),
            converter: converter,
            bigM: bigM,
            name: name
        ));

    private sealed class NumberConverter<V>(Func<Flt64, V> convert) : IIntoValue<V>
        where V : struct, IRealNumber<V> {
        public V Zero => NumericConstantsRegistry.For<V>().Zero;
        public V One => NumericConstantsRegistry.For<V>().One;
        public V IntoValue(Flt64 value) => convert(value);
        public Flt64 FromValue(V value) => value.ToFlt64();
    }

    private sealed class VariableNameComparer : IEqualityComparer<IVariableItem> {
        public bool Equals(IVariableItem? x, IVariableItem? y) => x?.Name == y?.Name;
        public int GetHashCode(IVariableItem value) => StringComparer.Ordinal.GetHashCode(value.Name);
    }

    private sealed class ObservedFixedValues(IVariableItem variable, Action onEnumeration) : IReadOnlyDictionary<IVariableItem, Flt64> {
        public int Count => 1;
        public IEnumerable<IVariableItem> Keys => new[] { variable };
        public IEnumerable<Flt64> Values => new[] { Flt64.One };
        public Flt64 this[IVariableItem key] => key == variable ? Flt64.One : throw new KeyNotFoundException();
        public bool ContainsKey(IVariableItem key) => key == variable;
        public bool TryGetValue(IVariableItem key, out Flt64 value) {
            value = Flt64.One;
            return key == variable;
        }
        public IEnumerator<KeyValuePair<IVariableItem, Flt64>> GetEnumerator() {
            yield return new KeyValuePair<IVariableItem, Flt64>(variable, Flt64.One);
            onEnumeration();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class MutatingSymbol(IVariableItem variable) : IIntermediateSymbol<Flt64> {
        public string Name { get; set; } = "mutating-symbol";
        public string? DisplayName { get; set; }
        public MathUInt64 Identifier { get; } = IdentifierGenerator.Gen();
        public int Index => 0;
        public IIntermediateSymbol? Parent => null;
        public ExpressionRange<Flt64> Range => ExpressionRange<Flt64>.Create();
        public Category Category => LinearCategory.Instance;
        public bool Cached => false;
        public IReadOnlySet<IIntermediateSymbol> Dependencies => new HashSet<IIntermediateSymbol>();
        public Token<Flt64>? Token { get; private set; }
        public Flt64? Seen { get; private set; }

        public Flt64? Prepare(
            IReadOnlyDictionary<ISymbol, Flt64>? values,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter) {
            Token = tokenTable.Find(variable);
            Seen = Token?.Result;
            Token?.SetResult(new Flt64(99));
            return Seen;
        }

        public void PrepareAndCache(
            IReadOnlyDictionary<ISymbol, Flt64>? values,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter) { }

        public Flt64? Evaluate(
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) => null;

        public Flt64? Evaluate(
            IReadOnlyList<Flt64> results,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) => null;

        public Flt64? Evaluate(
            IReadOnlyDictionary<ISymbol, Flt64> values,
            IAbstractTokenTable<Flt64>? tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) => null;

        public void Flush(bool force = false) { }
        public string ToRawString(MathUInt64 unfold) => Name;
    }

    private sealed class RepeatedAuxiliaryFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        private int _registrations;
        public string Name { get; set; } = "repeated-auxiliary";
        public string? DisplayName { get; set; }
        public RealVar Helper { get; } = new("repeated-auxiliary-helper");
        public RealVar Extra { get; } = new("repeated-auxiliary-extra");
        public bool ReceivedTable { get; private set; }
        public IReadOnlyList<IVariableItem> HelperVariables => new IVariableItem[] { Helper, Extra };
        public IReadOnlyCollection<ISymbol> InputSymbols => Array.Empty<ISymbol>();
        public LinearPolynomial<Flt64> ResultPolynomial => new(
            new[] { new LinearMonomial<Flt64>(Flt64.One, Helper) }, Flt64.Zero);
        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => Flt64.One;

        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) {
            ReceivedTable = tokens is IAbstractMutableTokenTable<Flt64>;
            if (++_registrations == 1) {
                return tokens.Add(Helper);
            }
            Try batch = tokens.Add(new IVariableItem[] { Helper, Extra, Extra });
            return batch.IsOk ? tokens.Add(Extra) : batch;
        }

        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) => Results.OkInstance;
    }

    private sealed class DelayedFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        private readonly bool _failConstraints;
        private readonly bool _fatal;
        private int _registrations;
        private IAddableTokenCollection<Flt64>? _tokens;

        public string Name { get; set; }
        public string? DisplayName { get; set; }
        public Action<IAddableTokenCollection<Flt64>>? OnConstraints { get; set; }
        public RealVar Helper { get; }
        public IReadOnlyList<IVariableItem> HelperVariables => new IVariableItem[] { Helper };
        public IReadOnlyCollection<ISymbol> InputSymbols => Array.Empty<ISymbol>();
        public LinearPolynomial<Flt64> ResultPolynomial => new(
            new[] { new LinearMonomial<Flt64>(Flt64.One, Helper) }, Flt64.Zero);

        public DelayedFunction(string name, bool failConstraints = false, bool fatal = false) {
            Name = name;
            Helper = new RealVar($"{name}-helper");
            _failConstraints = failConstraints;
            _fatal = fatal;
        }

        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => Flt64.One;

        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) {
            _tokens = tokens;
            return ++_registrations == 1 ? Results.OkInstance : tokens.Add(Helper);
        }

        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
            if (_tokens is { } tokens) {
                OnConstraints?.Invoke(tokens);
            }
            return !_failConstraints
                ? Results.OkInstance
                : _fatal
                    ? new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                        new Err<ErrorCode>(ErrorCode.ApplicationFailed, "fatal"))
                    : Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "failed"));
        }
    }
}
