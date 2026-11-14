#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Token;

public class TokenRegistrationTransactionTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedProgress_ShouldRestoreReplacedTokenSymbolsAndAllCaches(bool manual, bool throws) {
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var existing = new RealVar("progress-original");
        var extra = new RealVar("progress-extra");
        var oldSymbol = LinearExpressionSymbol.FromConstant(Flt64.One, "progress-old");
        var addedSymbol = LinearExpressionSymbol.FromConstant(Flt64.One, "progress-added");
        Assert.True(table.Add(existing).IsOk);
        Assert.True(table.Add(oldSymbol).IsOk);
        Token<Flt64> original = table.TokenList.Find(existing)!;
        original.SetResult(Flt64.One);
        Action<bool> ownerCallback = original.RefreshCallbacks[table.TokenList];
        var fixedValues = new Dictionary<ISymbol, Flt64> { [existing] = Flt64.One };
        table.Cache(
            cacheKey: "old-value",
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(7)
        );
        table.Cache(
            cacheKey: "old-fixed",
            fixedValues: fixedValues,
            value: new Flt64(8)
        );
        table.CacheLinearFlatten("old-null-linear", null);
        table.CacheQuadraticFlatten("old-null-quadratic", null);

        Try result = new IIntermediateSymbol[] { addedSymbol }.Register(
            tokenTable: table,
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 0) {
                    return Results.OkInstance;
                }
                Assert.True(table.Add(existing).IsOk);
                Assert.True(table.Add(extra).IsOk);
                table.Remove(oldSymbol);
                table.Flush();
                table.Cache(
                    cacheKey: "new-value",
                    solution: (IReadOnlyList<Flt64>?)null,
                    value: Flt64.One
                );
                return throws
                    ? throw new InvalidOperationException("progress failed")
                    : Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "progress failed"));
            }
        );

        Assert.True(result.IsFailed);
        Assert.Same(original, table.TokenList.Find(existing));
        Assert.Null(table.TokenList.Find(extra));
        Assert.Single(table.Symbols);
        Assert.Contains(oldSymbol, table.Symbols);
        Assert.Same(ownerCallback, original.RefreshCallbacks[table.TokenList]);
        Assert.True(table.TokenList.CachedSolution);
        Assert.Equal(new Flt64(7), table.CachedValue("old-value", (IReadOnlyList<Flt64>?)null));
        Assert.Equal(new Flt64(8), table.CachedValue("old-fixed", fixedValues));
        Assert.True(table.CachedLinearFlatten("old-null-linear"));
        Assert.Null(table.CachedLinearFlattenValue("old-null-linear"));
        Assert.True(table.CachedQuadraticFlatten("old-null-quadratic"));
        Assert.Null(table.CachedQuadraticFlattenValue("old-null-quadratic"));
        Assert.False(table.Cached("new-value", (IReadOnlyList<Flt64>?)null));
        var next = new RealVar("progress-next");
        Assert.True(table.Add(next).IsOk);
        Assert.Equal(1, table.TokenList.Find(next)!.SolverIndex);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedAuxiliaryRegistration_ShouldRestoreMetaModelState(bool manual, bool throws) {
        using var model = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: manual)
        );
        var existing = new RealVar("meta-original");
        var helper = new RealVar("meta-helper");
        var extraSymbol = LinearExpressionSymbol.FromConstant(Flt64.One, "meta-extra");
        Assert.True(model.Add(existing).IsOk);
        Token<Flt64> original = model.Tokens.Find(existing)!;
        Action<bool> ownerCallback = original.RefreshCallbacks[model.Tokens.TokenList];
        model.Tokens.Cache(
            cacheKey: "meta-cache",
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(9)
        );
        var function = new FailingFunction(tokens => {
            var table = (IAbstractMutableTokenTable<Flt64>)tokens;
            table.Remove(existing);
            Assert.True(table.Add(existing).IsOk);
            Assert.True(table.Add(helper).IsOk);
            Assert.True(table.Add(extraSymbol).IsOk);
            table.Flush();
            return throws
                ? throw new InvalidOperationException("auxiliary failed")
                : Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "auxiliary failed"));
        });

        Try result = model.Add(new LinearFunctionSymbolAdapter<Flt64>(function));

        Assert.True(result.IsFailed);
        Assert.Same(original, model.Tokens.Find(existing));
        Assert.Null(model.Tokens.Find(helper));
        Assert.Empty(model.Tokens.Symbols);
        Assert.Same(ownerCallback, original.RefreshCallbacks[model.Tokens.TokenList]);
        Assert.Equal(new Flt64(9), model.Tokens.CachedValue("meta-cache", (IReadOnlyList<Flt64>?)null));
        var next = new RealVar("meta-next");
        Assert.True(model.Add(next).IsOk);
        Assert.Equal(1, model.Tokens.Find(next)!.SolverIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SymbolBatchFailure_ShouldRestoreEarlierSymbolsAndHelpers(bool throws) {
        using var model = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: false)
        );
        var helper = new RealVar("batch-helper");
        var first = new LinearFunctionSymbolAdapter<Flt64>(new FailingFunction(tokens => tokens.Add(helper)) {
            Name = "batch-first",
        });
        var second = new LinearFunctionSymbolAdapter<Flt64>(new FailingFunction(_ =>
            Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "second failed"))) {
            Name = "batch-second",
        });

        Try result = model.Add(throws ? BrokenSymbols(first) : new IIntermediateSymbol[] { first, second });

        Assert.True(result.IsFailed);
        Assert.Empty(model.Tokens.Symbols);
        Assert.Null(model.Tokens.Find(helper));
        var next = new RealVar("batch-next");
        Assert.True(model.Add(next).IsOk);
        Assert.Equal(0, model.Tokens.Find(next)!.SolverIndex);
    }

    [Fact]
    public void UnsupportedCustomTable_ShouldFailBeforeMutation() {
        using var table = new CustomTable();
        var symbol = LinearExpressionSymbol.FromConstant(Flt64.One, "unsupported");
        Try result = new IIntermediateSymbol[] { symbol }.Register(table);
        Assert.True(result.IsFailed);
        Assert.Equal(0, table.Writes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CustomTransactionFailure_ShouldPreserveErrors(bool fatal, bool rollbackThrows) {
        using var table = new CustomTable {
            Transaction = new FaultingTransaction(rollbackThrows),
        };
        Try result = TokenRegistrationTransactionSupport.Run(table, () => fatal
            ? new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.ApplicationStopped, "first failure"),
                new Err<ErrorCode>(ErrorCode.ApplicationFailed, "second failure")
            )
            : Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "first failure")));

        Fatal<Success, ErrorCode, Error<ErrorCode>> combined = Assert.IsType<Fatal<Success, ErrorCode, Error<ErrorCode>>>(result);
        Assert.Equal(fatal ? 3 : 2, combined.Errors.Count);
        Assert.Contains("first failure", combined.Errors[0].Message);
        Assert.Contains("rollback failure", combined.Errors[^1].Message);
    }

    [Fact]
    public void NullProgressResult_ShouldFailAndRollBack() {
        using var table = new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var symbol = LinearExpressionSymbol.FromConstant(Flt64.One, "null-progress");
        Try result = new IIntermediateSymbol[] { symbol }.Register(table, callBack: _ => null!);
        Assert.True(result.IsFailed);
        Assert.Empty(table.Symbols);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DirectSymbolBatchFailure_ShouldRestoreEarlierSymbols(bool manual, bool iteratorFailure) {
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var existing = LinearExpressionSymbol.FromConstant(Flt64.One, "direct-existing");
        var first = LinearExpressionSymbol.FromConstant(Flt64.One, "direct-first");
        var duplicate = LinearExpressionSymbol.FromConstant(Flt64.One, "direct-existing");
        Assert.True(table.Add(existing).IsOk);

        Try result = table.Add(iteratorFailure ? BrokenSymbols(first) : new IIntermediateSymbol[] { first, duplicate });

        Assert.True(result.IsFailed);
        Assert.Same(existing, Assert.Single(table.Symbols));
        Assert.True(table.Add(new IIntermediateSymbol[] { first }).IsOk);
        Assert.Equal(2, table.Symbols.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosedTable_ShouldRejectSingleAndBatchSymbols(bool manual) {
        ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var symbol = LinearExpressionSymbol.FromConstant(Flt64.One, "closed-symbol");
        table.Dispose();

        Assert.True(table.Add(symbol).IsFailed);
        Assert.True(table.Add(new IIntermediateSymbol[] { symbol }).IsFailed);
        Assert.Empty(table.Symbols);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullSingleSymbol_ShouldReturnFailureWithoutMutation(bool manual) {
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        Assert.True(table.Add((IIntermediateSymbol)null!).IsFailed);
        Assert.Empty(table.Symbols);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuiltInTransaction_ShouldCommitOrRollBackOnlyOnce(bool manual) {
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var kept = new RealVar("transaction-kept");
        var removed = new RealVar("transaction-removed");
        ITokenRegistrationTransaction commit = table.BeginTokenRegistration().Value;
        Assert.True(table.Add(kept).IsOk);
        Assert.True(commit.Commit().IsOk);
        Assert.True(commit.Commit().IsFailed);
        Assert.True(commit.Rollback().IsFailed);

        var gap = new RealVar("transaction-gap");
        Assert.True(table.Add(gap).IsOk);
        table.Remove(gap);
        ITokenRegistrationTransaction rollback = table.BeginTokenRegistration().Value;
        Assert.True(table.Add(removed).IsOk);
        Assert.True(rollback.Rollback().IsOk);
        Assert.True(rollback.Rollback().IsFailed);
        Assert.True(rollback.Commit().IsFailed);
        Assert.NotNull(table.TokenList.Find(kept));
        Assert.Null(table.TokenList.Find(removed));
        var next = new RealVar("transaction-next");
        Assert.True(table.Add(next).IsOk);
        Assert.Equal(2, table.TokenList.Find(next)!.SolverIndex);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CommitFailure_ShouldAttemptRollbackAndPreserveErrors(bool throws, bool rollbackFails) {
        var transaction = new CommitFaultingTransaction(throws, rollbackFails);
        using var table = new CustomTable { Transaction = transaction };

        Try result = TokenRegistrationTransactionSupport.Run(table, () => Results.OkInstance);

        Assert.Equal(1, transaction.Rollbacks);
        if (rollbackFails) {
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal = Assert.IsType<Fatal<Success, ErrorCode, Error<ErrorCode>>>(result);
            Assert.Equal(2, fatal.Errors.Count);
            Assert.Contains("commit failure", fatal.Errors[0].Message);
            Assert.Contains("rollback failure", fatal.Errors[1].Message);
        }
        else {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed = Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result);
            Assert.Contains("commit failure", failed.Error.Message);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SymbolCollectionRollbackFailure_ShouldReturnFatal(bool manual) {
        var symbols = new FaultingSymbols();
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, symbols)
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, symbols);
        var first = LinearExpressionSymbol.FromConstant(Flt64.One, "fatal-first");
        var duplicate = LinearExpressionSymbol.FromConstant(Flt64.One, "fatal-first");
        symbols.FailClear = true;

        Try result = table.Add(new IIntermediateSymbol[] { first, duplicate });

        Fatal<Success, ErrorCode, Error<ErrorCode>> fatal = Assert.IsType<Fatal<Success, ErrorCode, Error<ErrorCode>>>(result);
        Assert.Equal(2, fatal.Errors.Count);
        Assert.Equal(ErrorCode.SymbolRepetitive, fatal.Errors[0].Code);
        Assert.Contains("clear failure", fatal.Errors[1].Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SingleSymbolWriteException_ShouldRollBackOrReportFatal(bool manual, bool rollbackFails) {
        var symbols = new FaultingSymbols { FailAdd = true, FailClear = rollbackFails };
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, symbols)
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, symbols);
        var symbol = LinearExpressionSymbol.FromConstant(Flt64.One, "single-failure");

        Try result = table.Add(symbol);

        if (rollbackFails) {
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal = Assert.IsType<Fatal<Success, ErrorCode, Error<ErrorCode>>>(result);
            Assert.Contains("add failure", fatal.Errors[0].Message);
            Assert.Contains("clear failure", fatal.Errors[1].Message);
        }
        else {
            Assert.True(result.IsFailed);
            Assert.Empty(symbols);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rollback_ShouldPreserveOtherSnapshotsAndSharedTokenValues(bool manual) {
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var originalVariable = new RealVar("snapshot-original");
        var addedVariable = new RealVar("snapshot-added");
        Assert.True(table.Add(originalVariable).IsOk);
        Token<Flt64> original = table.TokenList.Find(originalVariable)!;
        using var before = new ConcurrentTokenTable<Flt64>(table);
        ConcurrentTokenTable<Flt64>? during = null;
        Token<Flt64>? added = null;

        Try result = TokenRegistrationTransactionSupport.Run(table, () => {
            original.SetResult(Flt64.One);
            table.Remove(originalVariable);
            Assert.True(table.Add(addedVariable).IsOk);
            added = table.TokenList.Find(addedVariable);
            during = new ConcurrentTokenTable<Flt64>(table);
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "expected failure"));
        });

        using (during) {
            Assert.True(result.IsFailed);
            Assert.Same(original, table.TokenList.Find(originalVariable));
            Assert.Same(original, before.TokenList.Find(originalVariable));
            Assert.Equal(Flt64.One, original.Result);
            Assert.True(table.TokenList.CachedSolution);
            Assert.True(before.TokenList.CachedSolution);
            Assert.Contains(table.TokenList, original.RefreshCallbacks.Keys);
            Assert.Contains(before.TokenList, original.RefreshCallbacks.Keys);
            Assert.NotNull(added);
            Assert.Null(table.TokenList.Find(addedVariable));
            Assert.NotNull(during);
            Assert.Same(added, during.TokenList.Find(addedVariable));
            Assert.DoesNotContain(table.TokenList, added.RefreshCallbacks.Keys);
            Assert.Contains(during.TokenList, added.RefreshCallbacks.Keys);
        }
    }

    private static IEnumerable<IIntermediateSymbol> BrokenSymbols(IIntermediateSymbol first) {
        yield return first;
        throw new InvalidOperationException("symbol iterator failed");
    }

    private sealed class FaultingTransaction(bool throws) : ITokenRegistrationTransaction {
        public Try Commit() => Results.OkInstance;
        public Try Rollback() => throws
            ? throw new InvalidOperationException("rollback failure")
            : Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "rollback failure"));
    }

    private sealed class CommitFaultingTransaction(bool throws, bool rollbackFails) : ITokenRegistrationTransaction {
        public int Rollbacks { get; private set; }
        public Try Commit() => throws
            ? throw new InvalidOperationException("commit failure")
            : Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationStopped, "commit failure"));
        public Try Rollback() {
            ++Rollbacks;
            return rollbackFails
                ? Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "rollback failure"))
                : Results.OkInstance;
        }
    }

    private sealed class FaultingSymbols : ICollection<IIntermediateSymbol> {
        private readonly List<IIntermediateSymbol> _symbols = new();
        public bool FailAdd { get; set; }
        public bool FailClear { get; set; }
        public int Count => _symbols.Count;
        public bool IsReadOnly => false;
        public void Add(IIntermediateSymbol item) {
            _symbols.Add(item);
            if (FailAdd) {
                throw new InvalidOperationException("add failure");
            }
        }
        public void Clear() {
            if (FailClear) {
                throw new InvalidOperationException("clear failure");
            }
            _symbols.Clear();
        }
        public bool Contains(IIntermediateSymbol item) => _symbols.Contains(item);
        public void CopyTo(IIntermediateSymbol[] array, int arrayIndex) => _symbols.CopyTo(array, arrayIndex);
        public bool Remove(IIntermediateSymbol item) => _symbols.Remove(item);
        public IEnumerator<IIntermediateSymbol> GetEnumerator() => _symbols.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CustomTable() : ConcurrentMutableTokenTable<Flt64>(
        LinearCategory.Instance,
        new List<IIntermediateSymbol>()) {
        public int Writes { get; private set; }
        public ITokenRegistrationTransaction? Transaction { get; init; }
        public override Result<ITokenRegistrationTransaction, ErrorCode, Error<ErrorCode>> BeginTokenRegistration() =>
            Transaction is null ? base.BeginTokenRegistration() : Results.Ok(Transaction);
        public override Try Add(IVariableItem item) { ++Writes; return Results.OkInstance; }
        public override Try Add(IEnumerable<IVariableItem> items) { ++Writes; return Results.OkInstance; }
        public override void Remove(IVariableItem item) => ++Writes;
        public override Try Add(IIntermediateSymbol symbol) { ++Writes; return Results.OkInstance; }
        public override Try Add(IEnumerable<IIntermediateSymbol> symbols) { ++Writes; return Results.OkInstance; }
        public override void Remove(IIntermediateSymbol symbol) => ++Writes;
    }

    private sealed class FailingFunction(Func<IAddableTokenCollection<Flt64>, Try> register)
        : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        public string Name { get; set; } = "transaction-function";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public IReadOnlyCollection<ISymbol> InputSymbols => Array.Empty<ISymbol>();
        public LinearPolynomial<Flt64> ResultPolynomial => new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => Flt64.Zero;
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) => register(tokens);
        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) => Results.OkInstance;
    }
}
