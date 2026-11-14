#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Token;

public class TokenTableRegistrationTest {
    [Fact]
    public void Register_ShouldPrepareConstantsFixedValuesAndUnknownsWithoutInventingZero() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("x");
        var missing = new RealVar("missing");
        table.Add(x).IsOk.Should().BeTrue();
        table.Add(missing).IsOk.Should().BeTrue();
        var zero = LinearExpressionSymbol.FromConstant(Flt64.Zero, "zero");
        var constant = LinearExpressionSymbol.FromConstant(new Flt64(5), "constant");
        var known = LinearExpressionSymbol.FromVariable(x, "known");
        var unknown = LinearExpressionSymbol.FromVariable(missing, "unknown");
        var quadratic = new QuadraticExpressionSymbol(
            polynomial: new MutableQuadraticPolynomial<Flt64>(
                monomials: new[] {
                    QuadraticMonomial<Flt64>.Quadratic(
                        coefficient: new Flt64(2),
                        s1: x,
                        s2: x
                    ),
                },
                linearMonomials: new[] { new LinearMonomial<Flt64>(new Flt64(4), x) },
                constant: Flt64.One
            ),
            name: "quadratic"
        );
        table.Cache(
            cacheKey: unknown,
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(99)
        );
        var fixedValues = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(3) };

        Try result = new IIntermediateSymbol[] { unknown, quadratic, zero, known, constant }.Register(
            tokenTable: table,
            fixedValues: fixedValues
        );

        result.IsOk.Should().BeTrue();
        Cached(table, zero).Should().Be(Flt64.Zero);
        Cached(table, constant).Should().Be(new Flt64(5));
        Cached(table, known).Should().Be(new Flt64(3));
        Cached(table, quadratic).Should().Be(new Flt64(31));
        table.Cached(unknown, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        table.CachedLinearFlattenValue(constant)!.Constant.Should().Be(new Flt64(5));
        table.CachedQuadraticFlattenValue(quadratic)!.Monomials.Should().HaveCount(2);
        table.CachedRange(constant, (IReadOnlyList<Flt64>?)null).Should().BeTrue();
        fixedValues.Should().ContainSingle();
    }

    [Fact]
    public void Register_ShouldPrepareDependenciesBeforeDependentsInStableLayers() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("layer-x");
        table.Add(x).IsOk.Should().BeTrue();
        var leaf = LinearExpressionSymbol.FromVariable(x, "leaf");
        LinearExpressionSymbol middle = Expression(
            name: "middle",
            term: leaf,
            coefficient: new Flt64(2),
            constant: Flt64.One
        );
        LinearExpressionSymbol last = Expression(
            name: "last",
            term: middle,
            coefficient: Flt64.One,
            constant: Flt64.One
        );
        var empty = LinearExpressionSymbol.FromConstant(Flt64.Zero, "empty");
        var statuses = new List<RegistrationStatus>();

        Try result = new IIntermediateSymbol[] { last, empty, middle, leaf }.Register(
            tokenTable: table,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(3) },
            callBack: status => {
                statuses.Add(status);
                return Results.Ok(Results.SuccessInstance);
            }
        );

        result.IsOk.Should().BeTrue();
        Cached(table, leaf).Should().Be(new Flt64(3));
        Cached(table, middle).Should().Be(new Flt64(7));
        Cached(table, last).Should().Be(new Flt64(8));
        statuses.Select(status => status.ReadySymbolAmount.ToULong()).Should().Equal(new[] { 1UL, 2UL, 3UL, 4UL });
        statuses.Select(status => status.EmptySymbolAmount.ToULong()).Should().OnlyContain(value => value == 1UL);
        statuses.Select(status => status.TotalSymbolAmount.ToULong()).Should().OnlyContain(value => value == 4UL);
    }

    [Fact]
    public void Register_ShouldRespectTheFixedMapComparer() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("comparer-x");
        var alias = new RealVar("comparer-x");
        table.Add(x).IsOk.Should().BeTrue();
        var symbol = LinearExpressionSymbol.FromVariable(x, "comparer-result");
        var fixedValues = new Dictionary<ISymbol, Flt64>(new SymbolNameComparer()) {
            [alias] = new Flt64(7),
        };

        Try result = new IIntermediateSymbol[] { symbol }.Register(
            tokenTable: table,
            fixedValues: fixedValues
        );

        result.IsOk.Should().BeTrue();
        Cached(table, symbol).Should().Be(new Flt64(7));
        fixedValues.Should().ContainSingle();
    }

    [Fact]
    public void Register_ShouldRespectTheFixedMapComparerForFunctionInputs() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("function-comparer-x");
        var alias = new RealVar("function-comparer-x");
        table.Add(x).IsOk.Should().BeTrue();
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(
            Polynomial(x), IIntoValue<Flt64>.Identity));
        var fixedValues = new Dictionary<ISymbol, Flt64>(new SymbolNameComparer()) {
            [alias] = new Flt64(-7),
        };

        Try result = new IIntermediateSymbol[] { adapter }.Register(
            tokenTable: table,
            fixedValues: fixedValues
        );

        result.IsOk.Should().BeTrue();
        Cached(table, adapter).Should().Be(new Flt64(7));
    }

    [Fact]
    public void Register_ShouldHandleLongReversedDependencyChains() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var symbols = new List<IIntermediateSymbol> {
            LinearExpressionSymbol.FromConstant(Flt64.One, "chain-0"),
        };
        for (int index = 1; index < 512; index++) {
            symbols.Add(Expression(
                name: $"chain-{index}",
                term: symbols[index - 1],
                coefficient: Flt64.One,
                constant: Flt64.One
            ));
        }

        Try result = symbols.AsEnumerable().Reverse().ToArray().Register(table);

        result.IsOk.Should().BeTrue();
        Cached(table, symbols[^1]).Should().Be(new Flt64(512));
    }

    [Fact]
    public void Register_ShouldSnapshotPolynomialCacheAfterPreparation() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("snapshot-x");
        var symbol = LinearExpressionSymbol.FromVariable(x, "snapshot-symbol");
        Try result = new IIntermediateSymbol[] { symbol }.Register(table);
        result.IsOk.Should().BeTrue();

        symbol.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, x));

        table.CachedLinearFlattenValue(symbol)!.Monomials.Should().ContainSingle();
        symbol.Polynomial.Monomials.Should().HaveCount(2);
    }

    [Fact]
    public void Register_ShouldPrepareFunctionFromFixedValuesAndRegisterItsHelpers() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("function-x");
        table.Add(x).IsOk.Should().BeTrue();
        var function = new AbsFunction<Flt64>(
            new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            IIntoValue<Flt64>.Identity
        );
        var symbol = new LinearFunctionSymbolAdapter<Flt64>(function);

        Try result = new IIntermediateSymbol[] { symbol }.Register(
            tokenTable: table,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-4) }
        );

        result.IsOk.Should().BeTrue();
        Cached(table, symbol).Should().Be(new Flt64(4));
        function.HelperVariables.Should().OnlyContain(variable => table.TokenList.Find(variable) != null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Register_WhenFunctionReadsAnUndeclaredInput_ShouldRollback(bool useIndexer) {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("same-layer-x");
        table.Add(x).IsOk.Should().BeTrue();
        var first = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero
            ),
            converter: IIntoValue<Flt64>.Identity,
            name: "first-function"
        ));
        var second = new LinearFunctionSymbolAdapter<Flt64>(new HiddenDependencyFunction(
            dependency: first,
            useIndexer: useIndexer
        ));
        second.Dependencies.Should().BeEmpty();

        Try result = new IIntermediateSymbol[] { first, second }.Register(
            tokenTable: table,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-2) }
        );

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().ContainSingle().Which.Variable.Should().BeSameAs(x);
        table.Cached(first, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        table.Cached(second, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    [Fact]
    public void Register_WhenFunctionInputMetadataIsUnknown_ShouldFailBeforeMutation() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var symbol = new LinearFunctionSymbolAdapter<Flt64>(
            new HiddenDependencyFunction(new RealVar("unreported"), declareInputs: false));
        var statuses = new List<RegistrationStatus>();

        Try result = new IIntermediateSymbol[] { LinearExpressionSymbol.FromConstant(Flt64.One), symbol }.Register(
            tokenTable: table,
            callBack: status => {
                statuses.Add(status);
                return Results.Ok(Results.SuccessInstance);
            }
        );

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().BeEmpty();
        statuses.Should().BeEmpty();
    }

    [Fact]
    public void Register_WhenDeclaredIndexerInputIsMissing_ShouldLeaveTheResultUnknown() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("missing-indexed-input");
        table.Add(x).IsOk.Should().BeTrue();
        var symbol = new LinearFunctionSymbolAdapter<Flt64>(new HiddenDependencyFunction(
            dependency: x,
            useIndexer: true,
            includeDependency: true
        ));

        Try result = new IIntermediateSymbol[] { symbol }.Register(table);

        result.IsOk.Should().BeTrue();
        table.Cached(symbol, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Register_ShouldPrepareReversedNestedFunctionsFromExistingTokens(bool emptyFixedValues) {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("nested-x");
        var unrelated = new RealVar("unrelated");
        table.Add(x).IsOk.Should().BeTrue();
        table.Add(unrelated).IsOk.Should().BeTrue();
        table.TokenList.Find(x)!.SetResult(new Flt64(-3.4));
        var leaf = LinearExpressionSymbol.FromVariable(x, "nested-leaf");
        var absolute = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(
            polynomial: Polynomial(leaf),
            converter: IIntoValue<Flt64>.Identity,
            name: "nested-absolute"
        ));
        var floor = new LinearFunctionSymbolAdapter<Flt64>(new FloorFunction<Flt64>(
            polynomial: Polynomial(absolute),
            converter: IIntoValue<Flt64>.Identity,
            name: "nested-floor"
        ));
        var maximum = new LinearFunctionSymbolAdapter<Flt64>(new MaxFunction<Flt64>(
            polynomials: new[] { Polynomial(floor), Polynomial(leaf) },
            converter: IIntoValue<Flt64>.Identity,
            name: "nested-maximum"
        ));

        Try result = new IIntermediateSymbol[] { maximum, floor, absolute, leaf }.Register(
            tokenTable: table,
            fixedValues: emptyFixedValues ? new Dictionary<ISymbol, Flt64>() : null
        );

        result.IsOk.Should().BeTrue();
        absolute.Dependencies.Should().ContainSingle().Which.Should().BeSameAs(leaf);
        floor.Dependencies.Should().ContainSingle().Which.Should().BeSameAs(absolute);
        maximum.Dependencies.Should().BeEquivalentTo(new IIntermediateSymbol[] { floor, leaf });
        Cached(table, leaf).Should().Be(new Flt64(-3.4));
        Cached(table, absolute).Should().Be(new Flt64(3.4));
        Cached(table, floor).Should().Be(new Flt64(3));
        Cached(table, maximum).Should().Be(new Flt64(3));
    }

    [Fact]
    public void Register_WithPartialFixedValues_ShouldNotReuseStaleTokenResults() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("fixed-x");
        var stale = new RealVar("stale-y");
        table.Add(x).IsOk.Should().BeTrue();
        table.Add(stale).IsOk.Should().BeTrue();
        table.TokenList.Find(stale)!.SetResult(new Flt64(-9));
        var known = LinearExpressionSymbol.FromVariable(x, "fixed-known");
        var unknown = LinearExpressionSymbol.FromVariable(stale, "fixed-unknown");
        var absolute = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(
            Polynomial(stale), IIntoValue<Flt64>.Identity));
        var quadratic = QuadraticExpressionSymbol.FromVariable(stale, "fixed-quadratic");

        Try result = new IIntermediateSymbol[] { known, unknown, absolute, quadratic }.Register(
            tokenTable: table,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(2) }
        );

        result.IsOk.Should().BeTrue();
        Cached(table, known).Should().Be(new Flt64(2));
        table.Cached(unknown, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        table.Cached(absolute, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        table.Cached(quadratic, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    [Fact]
    public void FunctionPrepare_WithoutValues_ShouldUseExistingTokensAndConstants() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("prepare-x");
        table.Add(x).IsOk.Should().BeTrue();
        table.TokenList.Find(x)!.SetResult(new Flt64(-5));
        var absolute = new LinearFunctionSymbolAdapter<Flt64>(new AbsFunction<Flt64>(
            Polynomial(x), IIntoValue<Flt64>.Identity));
        var constant = new LinearFunctionSymbolAdapter<Flt64>(new FloorFunction<Flt64>(
            new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(2.5)),
            IIntoValue<Flt64>.Identity));

        absolute.Prepare(
            values: null,
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(5));
        constant.Prepare(
            values: null,
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(2));
        absolute.Prepare(
            values: new Dictionary<ISymbol, Flt64>(),
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(5));
    }

    [Fact]
    public void ModAdapter_ShouldExposeItsResultVariable() {
        var function = new ModFunction(Polynomial(new RealVar("mod-x")), new Flt64(2));
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        table.Add(function.ResultVar).IsOk.Should().BeTrue();
        table.TokenList.Find(function.ResultVar)!.SetResult(Flt64.One);

        adapter.Polynomial.Constant.Should().Be(Flt64.Zero);
        adapter.Polynomial.Monomials.Should().ContainSingle()
            .Which.Symbol.Should().BeSameAs(function.ResultVar);
        adapter.Evaluate(table, IIntoValue<Flt64>.Identity).Should().Be(Flt64.One);
    }

    [Fact]
    public void QuadraticSymbol_ShouldIncludeSeparateLinearTermsInDependenciesAndEvaluation() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("quadratic-x");
        table.Add(x).IsOk.Should().BeTrue();
        var dependency = LinearExpressionSymbol.FromVariable(x, "quadratic-dependency");
        var symbol = new QuadraticExpressionSymbol(
            polynomial: new MutableQuadraticPolynomial<Flt64>(
                monomials: new[] {
                    QuadraticMonomial<Flt64>.Quadratic(
                        coefficient: new Flt64(2),
                        s1: x,
                        s2: x
                    ),
                },
                linearMonomials: new[] { new LinearMonomial<Flt64>(new Flt64(4), dependency) },
                constant: Flt64.One
            ),
            name: "mixed-quadratic"
        );
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(3) };

        symbol.Dependencies.Should().ContainSingle().Which.Should().BeSameAs(dependency);
        symbol.Prepare(
            values: values,
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().BeNull();
        Try result = new IIntermediateSymbol[] { symbol, dependency }.Register(
            tokenTable: table,
            fixedValues: values
        );
        result.IsOk.Should().BeTrue();
        Cached(table, symbol).Should().Be(new Flt64(31));
        table.TokenList.Find(x)!.SetResult(new Flt64(3));
        symbol.Evaluate(table, IIntoValue<Flt64>.Identity).Should().Be(new Flt64(31));
        symbol.Evaluate(
            results: new[] { new Flt64(3) },
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(31));
        values[dependency] = new Flt64(3);
        symbol.Evaluate(
            values: values,
            tokenTable: null,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(31));
        symbol.ToRawString(new UInt64(1)).Should().Contain("quadratic-dependency");
    }

    [Fact]
    public void Register_ShouldRejectCyclesAndMissingDependenciesBeforeMutation() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var first = LinearExpressionSymbol.FromConstant(Flt64.One, "cycle-first");
        LinearExpressionSymbol second = Expression(
            name: "cycle-second",
            term: first,
            coefficient: Flt64.One,
            constant: Flt64.Zero
        );
        first.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, second));
        var external = LinearExpressionSymbol.FromConstant(Flt64.One, "external");
        LinearExpressionSymbol dependent = Expression(
            name: "dependent",
            term: external,
            coefficient: Flt64.One,
            constant: Flt64.Zero
        );
        var statuses = new List<RegistrationStatus>();

        Try cycle = new IIntermediateSymbol[] { first, second }.Register(
            tokenTable: table,
            callBack: status => {
                statuses.Add(status);
                return Results.Ok(Results.SuccessInstance);
            }
        );
        Try missing = new IIntermediateSymbol[] { dependent }.Register(table);

        cycle.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        missing.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().BeEmpty();
        statuses.Should().BeEmpty();
    }

    [Fact]
    public void Register_ShouldRejectMissingAndCyclicFunctionInputsBeforeAddingHelpers() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var input = LinearExpressionSymbol.FromConstant(Flt64.One, "function-cycle-input");
        var function = new AbsFunction<Flt64>(
            polynomial: Polynomial(input),
            converter: IIntoValue<Flt64>.Identity,
            name: "function-cycle"
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);

        Try missing = new IIntermediateSymbol[] { adapter }.Register(table);
        input.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, adapter));
        Try cycle = new IIntermediateSymbol[] { input, adapter }.Register(table);

        missing.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        cycle.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().BeEmpty();
    }

    [Fact]
    public void Register_ShouldRejectUnsupportedOrRepeatedSymbols() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var untyped = new UntypedSymbol();
        var symbol = LinearExpressionSymbol.FromConstant(Flt64.One, "repeated-instance");

        Try unsupported = new IIntermediateSymbol[] { untyped }.Register(table);
        Try repeated = new IIntermediateSymbol[] { symbol, symbol }.Register(table);

        unsupported.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        repeated.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        table.Symbols.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Register_WhenFixedSymbolConflicts_ShouldRejectAndRestoreCaches(int expected) {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var symbol = LinearExpressionSymbol.FromConstant(new Flt64(expected), "fixed-conflict");
        table.Cache(
            cacheKey: symbol,
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(42)
        );

        Try result = new IIntermediateSymbol[] { symbol }.Register(
            tokenTable: table,
            fixedValues: new Dictionary<ISymbol, Flt64> { [symbol] = new Flt64(expected + 1) }
        );

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        table.Symbols.Should().BeEmpty();
        Cached(table, symbol).Should().Be(new Flt64(42));
        table.CachedLinearFlatten(symbol).Should().BeFalse();
    }

    [Fact]
    public void Register_WhenSymbolAdditionFails_ShouldRemoveEarlierAdditions() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var original = LinearExpressionSymbol.FromConstant(Flt64.One, "duplicate");
        table.Add(original).IsOk.Should().BeTrue();
        var added = LinearExpressionSymbol.FromConstant(Flt64.One, "added");
        var duplicate = LinearExpressionSymbol.FromConstant(Flt64.One, "duplicate");

        Try result = new IIntermediateSymbol[] { added, duplicate }.Register(table);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Code.Should().Be(ErrorCode.SymbolRepetitive);
        table.Symbols.Should().ContainSingle().Which.Should().BeSameAs(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Register_WhenLaterAuxiliaryRegistrationFails_ShouldRestoreTable(bool fatal) {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var existing = new RealVar("existing");
        table.Add(existing).IsOk.Should().BeTrue();
        Token<Flt64> originalToken = table.TokenList.Find(existing)!;
        var root = LinearExpressionSymbol.FromConstant(new Flt64(2), "root");
        var helper = new RealVar("helper");
        var function = new FailingFunction(
            dependency: root,
            helper: helper,
            fatal: fatal
        );
        var symbol = new LinearFunctionSymbolAdapter<Flt64>(function);
        table.Cache(
            cacheKey: root,
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(42)
        );
        var statuses = new List<ulong>();

        Try result = new IIntermediateSymbol[] { symbol, root }.Register(
            tokenTable: table,
            callBack: status => {
                statuses.Add(status.ReadySymbolAmount.ToULong());
                return Results.Ok(Results.SuccessInstance);
            }
        );

        if (fatal) {
            result.Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>();
        }
        else {
            result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        }
        statuses.Should().Equal(0UL, 1UL);
        table.Symbols.Should().BeEmpty();
        table.TokenList.Find(existing).Should().BeSameAs(originalToken);
        table.TokenList.Find(helper).Should().BeNull();
        Cached(table, root).Should().Be(new Flt64(42));
        table.CachedLinearFlatten(root).Should().BeFalse();
        table.CachedRange(root, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    [Fact]
    public void Register_WhenProgressCallbackFails_ShouldRestorePriorCachesAndTokens() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var x = new RealVar("callback-x");
        table.Add(x).IsOk.Should().BeTrue();
        var root = LinearExpressionSymbol.FromConstant(new Flt64(2), "callback-root");
        var function = new AbsFunction<Flt64>(
            new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            IIntoValue<Flt64>.Identity
        );
        var symbol = new LinearFunctionSymbolAdapter<Flt64>(function);
        var priorFlatten = new LinearFlattenData<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(99));
        ValueRange<Flt64> priorRange = ValueRange<Flt64>.Of(Flt64.One).Value;
        var fixedValues = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-4) };
        table.Cache(
            cacheKey: root,
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(42)
        );
        table.Cache(
            cacheKey: root,
            fixedValues: fixedValues,
            value: new Flt64(88)
        );
        table.CacheLinearFlatten(root, priorFlatten);
        table.CacheRange(
            cacheKey: root,
            solution: (IReadOnlyList<Flt64>?)null,
            range: priorRange
        );

        Try result = new IIntermediateSymbol[] { root, symbol }.Register(
            tokenTable: table,
            fixedValues: fixedValues,
            callBack: status => status.ReadySymbolAmount.ToULong() == 0
                ? Results.Ok(Results.SuccessInstance)
                : new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationStopped, "callback failure")
                )
        );

        result.Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>();
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().ContainSingle().Which.Variable.Should().BeSameAs(x);
        Cached(table, root).Should().Be(new Flt64(42));
        table.CachedValue(root, fixedValues).Should().Be(new Flt64(88));
        table.CachedLinearFlattenValue(root).Should().BeSameAs(priorFlatten);
        table.CachedRangeValue(root, (IReadOnlyList<Flt64>?)null).Should().BeSameAs(priorRange);
        table.Cached(symbol, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    [Fact]
    public void Register_WhenAuxiliaryAlreadyExists_ShouldKeepItsOriginalTokenOnFailure() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        var helper = new RealVar("preexisting-helper");
        table.Add(helper).IsOk.Should().BeTrue();
        Token<Flt64> original = table.TokenList.Find(helper)!;
        var root = LinearExpressionSymbol.FromConstant(Flt64.One, "preexisting-root");
        var symbol = new LinearFunctionSymbolAdapter<Flt64>(new FailingFunction(
            dependency: root,
            helper: helper,
            fatal: false
        ));

        Try result = new IIntermediateSymbol[] { symbol, root }.Register(table);

        result.IsFailed.Should().BeTrue();
        table.TokenList.Find(helper).Should().BeSameAs(original);
        table.Symbols.Should().BeEmpty();
    }

    [Fact]
    public void ClearCachedValue_ShouldOnlyRemoveTheSelectedSolutionEntry() {
        using ConcurrentAutoTokenTable<Flt64> table = Table();
        Flt64[] solution = new[] { Flt64.One };
        var fixedValues = new Dictionary<ISymbol, Flt64>();
        table.Cache(
            cacheKey: "key",
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        table.Cache(
            cacheKey: "key",
            solution: solution,
            value: new Flt64(2)
        );
        table.Cache(
            cacheKey: "key",
            fixedValues: fixedValues,
            value: new Flt64(3)
        );

        table.ClearCachedValue("key", (IReadOnlyList<Flt64>?)null).Should().BeTrue();
        table.ClearCachedValue("key", (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        table.CachedValue("key", solution).Should().Be(new Flt64(2));
        table.CachedValue("key", fixedValues).Should().Be(new Flt64(3));
        using var snapshot = new ConcurrentTokenTable<Flt64>(table);
        snapshot.Cache(
            cacheKey: "key",
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        snapshot.ClearCachedValue("key", (IReadOnlyList<Flt64>?)null).Should().BeTrue();
        snapshot.Cached("key", (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    private static ConcurrentAutoTokenTable<Flt64> Table() =>
        new(LinearCategory.Instance, new List<IIntermediateSymbol>());

    private static Flt64? Cached(IAbstractTokenTable<Flt64> table, IIntermediateSymbol symbol) =>
        table.CachedValue(symbol, (IReadOnlyList<Flt64>?)null);

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero);

    private static LinearExpressionSymbol Expression(string name, ISymbol term, Flt64 coefficient, Flt64 constant) =>
        new(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(coefficient, term) },
                constant
            ),
            name: name
        );

    private sealed class FailingFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        private readonly bool _fatal;
        public string Name { get; set; } = "failing-function";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables { get; }
        public LinearPolynomial<Flt64> ResultPolynomial { get; }
        public IReadOnlyCollection<ISymbol> InputSymbols => Array.Empty<ISymbol>();

        public FailingFunction(IIntermediateSymbol dependency, IVariableItem helper, bool fatal) {
            _fatal = fatal;
            HelperVariables = new[] { helper };
            ResultPolynomial = new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, dependency) },
                Flt64.Zero
            );
        }

        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => null;

        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) {
            Try added = tokens.Add(HelperVariables);
            if (added.IsFailed) {
                return added;
            }
            var error = new Err<ErrorCode>(ErrorCode.IllegalArgument, "test failure");
            return _fatal
                ? new Fatal<Success, ErrorCode, Error<ErrorCode>>(error)
                : Results.Failed<Success>(error);
        }

        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) =>
            Results.Ok(Results.SuccessInstance);
    }

    private sealed class HiddenDependencyFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        private readonly ISymbol _dependency;
        private readonly bool _useIndexer;
        public string Name { get; set; } = "hidden-dependency";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public LinearPolynomial<Flt64> ResultPolynomial => new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
        public IReadOnlyCollection<ISymbol>? InputSymbols { get; }

        public HiddenDependencyFunction(
            ISymbol dependency,
            bool declareInputs = true,
            bool useIndexer = false,
            bool includeDependency = false) {
            _dependency = dependency;
            _useIndexer = useIndexer;
            InputSymbols = declareInputs
                ? includeDependency ? new[] { dependency } : Array.Empty<ISymbol>()
                : null;
        }

        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) =>
            _useIndexer ? values[_dependency] : values.TryGetValue(_dependency, out Flt64 value) ? value : null;

        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
            Results.Ok(Results.SuccessInstance);

        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) =>
            Results.Ok(Results.SuccessInstance);
    }

    private sealed class UntypedSymbol : IIntermediateSymbol {
        public string Name { get; set; } = "untyped";
        public string? DisplayName { get; set; }
        public UInt64 Identifier { get; } = IdentifierGenerator.Gen();
        public int Index => 0;
        public IIntermediateSymbol? Parent => null;
        public string ToRawString(UInt64 unfold) => Name;
    }

    private sealed class SymbolNameComparer : IEqualityComparer<ISymbol> {
        public bool Equals(ISymbol? left, ISymbol? right) => left?.Name == right?.Name;
        public int GetHashCode(ISymbol symbol) => StringComparer.Ordinal.GetHashCode(symbol.Name);
    }
}
