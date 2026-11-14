#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Token;

public class TokenBatchAtomicityTest {
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IteratorFailure_ShouldPreserveTokensIndexesAndCallbacks(int kind) {
        (IAddableTokenCollection<Flt64> collection, AbstractTokenList<Flt64> list, IDisposable owner) = Create(kind);
        using (owner) {
            var existing = new RealVar("batch-existing");
            var first = new RealVar("batch-first");
            Assert.True(collection.Add(existing).IsOk);
            Token<Flt64> original = Assert.Single(list.Tokens);
            original.SetResult(Flt64.One);
            IReadOnlyList<Token<Flt64>> originalOrder = list.TokensInSolver;
            Action<bool>? callback = original.RefreshCallbacks.TryGetValue(list, out Action<bool>? found) ? found : null;

            Try result = collection.Add(BrokenItems(first));

            Assert.True(result.IsFailed);
            Assert.Same(original, Assert.Single(list.Tokens));
            Assert.Equal(originalOrder, list.TokensInSolver);
            Assert.True(list.CachedSolution);
            Assert.Equal(Flt64.One, original.Result);
            Assert.Equal(callback, original.RefreshCallbacks.TryGetValue(list, out found) ? found : null);
            Assert.True(collection.Add(first).IsOk);
            Token<Flt64> added = Assert.Single(list.Tokens, token => token.Variable == first);
            Assert.Equal(1, added.SolverIndex);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void NullItemAfterValidItem_ShouldReturnFailureWithoutAddingEither(int kind) {
        (IAddableTokenCollection<Flt64> collection, AbstractTokenList<Flt64> list, IDisposable owner) = Create(kind);
        using (owner) {
            var first = new RealVar("batch-null-first");
            Try result = collection.Add(new IVariableItem[] { first, null! });
            Assert.True(result.IsFailed);
            Assert.Empty(list.Tokens);
            Assert.True(collection.Add(first).IsOk);
            Assert.Equal(0, Assert.Single(list.Tokens).SolverIndex);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ExistingAndRepeatedItems_ShouldReplaceTokensAndConsumeEveryIndex(int kind) {
        (IAddableTokenCollection<Flt64> collection, AbstractTokenList<Flt64> list, IDisposable owner) = Create(kind);
        using (owner) {
            var existing = new RealVar("batch-duplicate-existing");
            var added = new RealVar("batch-duplicate-added");
            Assert.True(collection.Add(existing).IsOk);
            Token<Flt64> original = Assert.Single(list.Tokens);
            original.SetResult(Flt64.One);

            Try result = collection.Add(new IVariableItem[] { added, existing, added });

            Assert.True(result.IsOk);
            Assert.Equal(2, list.Tokens.Count);
            Assert.NotSame(original, list.Find(existing));
            Assert.Equal(new[] { existing, added }, list.TokensInSolver.Select(token => token.Variable));
            Assert.Equal(new[] { 2, 3 }, list.TokensInSolver.Select(token => token.SolverIndex));
            Assert.Null(list.Find(existing)!.Result);
            Assert.False(list.CachedSolution);
            Assert.Equal(Flt64.One, original.Result);
            list.SetSolution(new[] { new Flt64(2), new Flt64(3) });
            Assert.Equal(new Flt64(2), list.Find(existing)!.Result);
            Assert.Equal(new Flt64(3), list.Find(added)!.Result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModelConfiguration_ShouldRejectDuplicatesAtomically(bool manual) {
        using var model = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: manual, CheckTokenExists: true)
        );
        var existing = new RealVar("checked-existing");
        var added = new RealVar("checked-added");
        Assert.True(model.Add(existing).IsOk);
        Token<Flt64> original = model.Tokens.Find(existing)!;
        original.SetResult(Flt64.One);
        model.Tokens.Cache(
            cacheKey: "checked-cache",
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );

        Try result = model.Add(new IVariableItem[] { added, existing });

        Failed<Success, ErrorCode, Error<ErrorCode>> failed = Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result);
        Assert.Equal(ErrorCode.TokenExisted, failed.Error.Code);
        Assert.Same(original, model.Tokens.Find(existing));
        Assert.Null(model.Tokens.Find(added));
        Assert.Equal(Flt64.One, original.Result);
        Assert.Equal(Flt64.One, model.Tokens.CachedValue("checked-cache", (IReadOnlyList<Flt64>?)null));
        Assert.True(model.Add(added).IsOk);
        Assert.Equal(1, model.Tokens.Find(added)!.SolverIndex);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CheckingDuplicates_ShouldRejectExistingAndRepeatedKeysWithoutPublishing(int kind) {
        (IAddableTokenCollection<Flt64> collection, AbstractTokenList<Flt64> list, IDisposable owner) = Create(
            kind, checkTokenExists: true);
        using (owner) {
            var existing = new RealVar("checked-list-existing");
            var added = new RealVar("checked-list-added");
            Assert.True(collection.Add(existing).IsOk);
            Token<Flt64> original = list.Find(existing)!;
            original.SetResult(Flt64.One);
            Action<bool>? callback = original.RefreshCallbacks.TryGetValue(list, out Action<bool>? current) ? current : null;

            Try existingResult = collection.Add(new IVariableItem[] { added, existing });
            Try repeatedResult = collection.Add(new IVariableItem[] { added, added });

            Assert.Equal(ErrorCode.TokenExisted, Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(existingResult).Error.Code);
            Assert.Equal(ErrorCode.TokenExisted, Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(repeatedResult).Error.Code);
            Assert.Equal(ErrorCode.TokenExisted, Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(collection.Add(existing)).Error.Code);
            Assert.Same(original, Assert.Single(list.Tokens));
            Assert.Equal(Flt64.One, original.Result);
            Assert.True(list.CachedSolution);
            Assert.Equal(callback, original.RefreshCallbacks.TryGetValue(list, out current) ? current : null);
            Assert.True(collection.Add(added).IsOk);
            Assert.Equal(1, list.Find(added)!.SolverIndex);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FailedIteratorAfterExistingKey_ShouldKeepOriginalAndItsIndex(int kind) {
        (IAddableTokenCollection<Flt64> collection, AbstractTokenList<Flt64> list, IDisposable owner) = Create(kind);
        using (owner) {
            var existing = new RealVar("failed-replacement");
            var next = new RealVar("failed-replacement-next");
            Assert.True(collection.Add(existing).IsOk);
            Token<Flt64> original = list.Find(existing)!;
            original.SetResult(Flt64.One);
            Action<bool>? callback = original.RefreshCallbacks.TryGetValue(list, out Action<bool>? current) ? current : null;

            Assert.True(collection.Add(BrokenItems(existing)).IsFailed);

            Assert.Same(original, list.Find(existing));
            Assert.Equal(Flt64.One, original.Result);
            Assert.True(list.CachedSolution);
            Assert.Equal(callback, original.RefreshCallbacks.TryGetValue(list, out current) ? current : null);
            Assert.True(collection.Add(next).IsOk);
            Assert.Equal(1, list.Find(next)!.SolverIndex);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replacement_ShouldKeepSnapshotCallbacksAndInvalidateTableValueCache(bool manual) {
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var variable = new RealVar("replacement-snapshot");
        Assert.True(table.Add(variable).IsOk);
        Token<Flt64> original = table.TokenList.Find(variable)!;
        original.SetResult(Flt64.One);
        using var snapshot = new ConcurrentTokenTable<Flt64>(table);
        Flt64[] solution = { Flt64.One };
        var fixedValues = new Dictionary<ISymbol, Flt64> { [variable] = Flt64.One };
        table.Cache(
            cacheKey: "replacement-value",
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        table.Cache(cacheKey: "vector-value", solution: solution, value: Flt64.One);
        table.Cache(cacheKey: "fixed-value", fixedValues: fixedValues, value: Flt64.One);
        table.CacheLinearFlatten("flatten", null);
        snapshot.Cache(cacheKey: "snapshot-value", solution: solution, value: Flt64.One);

        Assert.True(table.Add(variable).IsOk);

        Token<Flt64> replacement = table.TokenList.Find(variable)!;
        Assert.NotSame(original, replacement);
        Assert.Same(original, snapshot.TokenList.Find(variable));
        Assert.DoesNotContain(table.TokenList, original.RefreshCallbacks.Keys);
        Assert.Contains(snapshot.TokenList, original.RefreshCallbacks.Keys);
        Assert.False(table.TokenList.CachedSolution);
        Assert.True(snapshot.TokenList.CachedSolution);
        Assert.False(table.Cached("replacement-value", (IReadOnlyList<Flt64>?)null));
        Assert.False(table.Cached("vector-value", solution));
        Assert.False(table.Cached("fixed-value", fixedValues));
        Assert.True(table.CachedLinearFlatten("flatten"));
        Assert.Equal(Flt64.One, snapshot.CachedValue("snapshot-value", solution));
        replacement.SetResult(new Flt64(2));
        original.SetResult(new Flt64(3));
        Assert.Equal(new Flt64(2), replacement.Result);
        Assert.Equal(new Flt64(3), snapshot.TokenList.Find(variable)!.Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void KeyFailureOrIteratorDisposalFailure_ShouldNotPublishEarlierTokens(int kind) {
        (IAddableTokenCollection<Flt64> collection, AbstractTokenList<Flt64> list, IDisposable owner) = Create(kind);
        using (owner) {
            var first = new RealVar("batch-key-first");
            var broken = new BrokenVariable(throwKey: true);
            Assert.True(collection.Add(new IVariableItem[] { first, broken }).IsFailed);
            Assert.Empty(list.Tokens);
            Assert.True(collection.Add(BrokenOnDispose(first)).IsFailed);
            Assert.Empty(list.Tokens);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ManualRegistration_ShouldNotReadVariableIndex(int kind) {
        (IAddableTokenCollection<Flt64> collection, AbstractTokenList<Flt64> list, IDisposable owner) = Create(kind);
        using (owner) {
            var first = new RealVar("batch-index-first");
            var broken = new BrokenVariable(throwKey: false);
            Assert.True(collection.Add(new IVariableItem[] { first, broken }).IsOk);
            Assert.Equal(0, list.Find(first)!.SolverIndex);
            Assert.Equal(1, list.Find(broken)!.SolverIndex);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCommit_ShouldRollBackOrReportBothErrors(bool failRollback) {
        var items = new IVariableItem[] { new RealVar("commit-first"), new RealVar("commit-second") };
        var stored = new Dictionary<VariableItemKey, Token<Flt64>>();
        int writes = 0;
        Try result = TokenBatchSupport.Add<Flt64>(
            items: items,
            contains: stored.ContainsKey,
            add: (key, token) => {
                stored.Add(key, token);
                if (++writes == 2) {
                    throw new InvalidOperationException("commit failed");
                }
            },
            captureRollback: key => {
                stored.TryGetValue(key, out Token<Flt64>? original);
                return () => {
                    if (failRollback) {
                        throw new InvalidOperationException("rollback failed");
                    }
                    if (original is null) { stored.Remove(key); }
                    else { stored[key] = original; }
                };
            },
            checkTokenExists: false,
            skipExisting: null,
            firstIndex: 3,
            converter: null,
            nextIndex: out int nextIndex,
            replaced: out _
        );

        Assert.Equal(3, nextIndex);
        if (failRollback) {
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal = Assert.IsType<Fatal<Success, ErrorCode, Error<ErrorCode>>>(result);
            Assert.Equal(2, fatal.Errors.Count);
            Assert.Contains("commit failed", fatal.Errors[0].Message);
            Assert.Contains("rollback failed", fatal.Errors[1].Message);
        }
        else {
            Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result);
            Assert.Empty(stored);
        }
    }

    [Fact]
    public void FailedCommitAfterRepeatedReplacement_ShouldRestoreOriginalIdentity() {
        var variable = new RealVar("commit-replaced");
        var original = new Token<Flt64>(
            variable: variable,
            solverIndex: 0,
            refreshCallbacks: new Dictionary<object, Action<bool>>()
        );
        original.SetResult(Flt64.One);
        var stored = new Dictionary<VariableItemKey, Token<Flt64>> { [variable.Key] = original };
        int writes = 0;

        Try result = TokenBatchSupport.Add<Flt64>(
            items: new IVariableItem[] { variable, variable },
            contains: stored.ContainsKey,
            add: (key, token) => {
                stored[key] = token;
                if (++writes == 2) { throw new InvalidOperationException("replacement failed"); }
            },
            captureRollback: key => {
                stored.TryGetValue(key, out Token<Flt64>? previous);
                return () => {
                    if (previous is null) { stored.Remove(key); }
                    else { stored[key] = previous; }
                };
            },
            checkTokenExists: false,
            skipExisting: null,
            firstIndex: 1,
            converter: null,
            nextIndex: out int nextIndex,
            replaced: out bool replaced
        );

        Assert.True(result.IsFailed);
        Assert.Same(original, stored[variable.Key]);
        Assert.Equal(Flt64.One, original.Result);
        Assert.Equal(1, nextIndex);
        Assert.False(replaced);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexOverflow_ShouldFailBeforePublishingAndKeepNextIndex(bool manual) {
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var first = new RealVar("overflow-first");
        var second = new RealVar("overflow-second");
        table.RestoreNextIndex(int.MaxValue - 1);

        Assert.True(table.Add(new IVariableItem[] { first, second }).IsFailed);
        Assert.Empty(table.TokenList.Tokens);
        Assert.Equal(int.MaxValue - 1, table.NextIndex);
        Assert.True(table.Add(first).IsOk);
        Assert.Equal(int.MaxValue - 1, table.TokenList.Find(first)!.SolverIndex);
        Assert.True(table.Add(second).IsFailed);
        Assert.Same(first, Assert.Single(table.TokenList.Tokens).Variable);
        Assert.Equal(int.MaxValue, table.NextIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementOverflow_ShouldPreserveOriginalTokenAndCaches(bool manual) {
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var variable = new RealVar("overflow-replacement");
        Assert.True(table.Add(variable).IsOk);
        Token<Flt64> original = table.TokenList.Find(variable)!;
        original.SetResult(Flt64.One);
        Action<bool> callback = original.RefreshCallbacks[table.TokenList];
        table.Cache(
            cacheKey: "overflow-cache",
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        table.RestoreNextIndex(int.MaxValue - 1);

        Assert.True(table.Add(new IVariableItem[] { variable, variable }).IsFailed);

        Assert.Same(original, table.TokenList.Find(variable));
        Assert.Same(callback, original.RefreshCallbacks[table.TokenList]);
        Assert.Equal(Flt64.One, original.Result);
        Assert.True(table.TokenList.CachedSolution);
        Assert.Equal(Flt64.One, table.CachedValue("overflow-cache", (IReadOnlyList<Flt64>?)null));
        Assert.Equal(int.MaxValue - 1, table.NextIndex);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ManualIndex_ShouldFollowRegistrationOrderForScalarVariables(int kind) {
        (IAddableTokenCollection<Flt64> collection, AbstractTokenList<Flt64> list, IDisposable owner) = Create(kind);
        using (owner) {
            var first = new RealVar("manual-first");
            var second = new RealVar("manual-second");
            Assert.True(collection.Add(new IVariableItem[] { first, second }).IsOk);
            Assert.Equal(0, list.Find(first)!.SolverIndex);
            Assert.Equal(1, list.Find(second)!.SolverIndex);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RemovedIndex_ShouldNotBeReusedAndSolutionsUseCompactOrder(int kind) {
        (IAddableTokenCollection<Flt64> collection, AbstractTokenList<Flt64> list, IDisposable owner) = Create(kind);
        using (owner) {
            var first = new RealVar("ordered-first");
            var removed = new RealVar("ordered-removed");
            var third = new RealVar("ordered-third");
            var last = new RealVar("ordered-last");
            Assert.True(collection.Add(new IVariableItem[] { first, removed, third }).IsOk);
            if (owner is ConcurrentMutableTokenTable<Flt64> table) {
                table.Remove(removed);
            }
            else {
                ((AbstractMutableTokenList<Flt64>)list).Remove(removed);
            }
            Assert.True(collection.Add(last).IsOk);

            Assert.Equal(new[] { first, third, last }, list.TokensInSolver.Select(token => token.Variable));
            Assert.Equal(new[] { 0, 2, 3 }, list.TokensInSolver.Select(token => token.SolverIndex));
            Assert.Equal(0, list.IndexOf(first));
            Assert.Equal(1, list.IndexOf(third));
            Assert.Equal(2, list.IndexOf(last));
            Assert.Same(list.Find(third), list.Find(1));

            list.SetSolverSolution(new[] { new Flt64(10), new Flt64(20), new Flt64(30) });
            Assert.Equal(new Flt64(10), list.Find(first)!.Result);
            Assert.Equal(new Flt64(20), list.Find(third)!.Result);
            Assert.Equal(new Flt64(30), list.Find(last)!.Result);
            list.SetSolution(new[] { new Flt64(40), new Flt64(50), new Flt64(60) });
            Assert.Equal(new Flt64(40), list.Find(first)!.Result);
            Assert.Equal(new Flt64(50), list.Find(third)!.Result);
            Assert.Equal(new Flt64(60), list.Find(last)!.Result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConcurrentTableAdditions_ShouldAllocateDistinctSequentialIndexes(bool manual) {
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        RealVar[] variables = Enumerable.Range(0, 256).Select(index => new RealVar($"concurrent-{index}")).ToArray();
        var failures = new ConcurrentBag<int>();
        Parallel.For(
            fromInclusive: 0,
            toExclusive: variables.Length,
            body: index => {
                if (!table.Add(variables[index]).IsOk) {
                    failures.Add(index);
                }
            }
        );

        Assert.Empty(failures);
        Assert.Equal(Enumerable.Range(0, variables.Length), table.TokenList.TokensInSolver.Select(token => token.SolverIndex));
    }

    private static IEnumerable<IVariableItem> BrokenItems(IVariableItem first) {
        yield return first;
        throw new InvalidOperationException("iterator failure");
    }

    private static IEnumerable<IVariableItem> BrokenOnDispose(IVariableItem item) {
        try {
            yield return item;
        }
        finally {
            throw new InvalidOperationException("iterator disposal failure");
        }
    }

    private sealed class BrokenVariable(bool throwKey) : IVariableItem {
        private readonly RealVar _variable = new("broken-variable");
        public UInt64 Identifier => _variable.Identifier;
        public int Index => throw new InvalidOperationException("index failure");
        public VariableItemKey Key => throwKey ? throw new InvalidOperationException("key failure") : _variable.Key;
        public string Name => _variable.Name;
        public string? DisplayName => _variable.DisplayName;
        public IVariableTypeKind TypeKind => ((IVariableItem)_variable).TypeKind;
        public Bound<Flt64>? LowerBound => _variable.LowerBound;
        public Bound<Flt64>? UpperBound => _variable.UpperBound;
        public bool BelongsTo(IVariableItem item) => _variable.BelongsTo(item);
        public bool BelongsTo(IVariableCombination combination) => _variable.BelongsTo(combination);
    }

    private static (IAddableTokenCollection<Flt64>, AbstractTokenList<Flt64>, IDisposable) Create(
        int kind, bool checkTokenExists = false) {
        if (kind == 0) {
            var list = new AutoTokenList<Flt64>(checkTokenExists);
            return (list, list, list);
        }
        if (kind == 1) {
            var list = new ManualTokenList<Flt64>(checkTokenExists);
            return (list, list, list);
        }
        ConcurrentMutableTokenTable<Flt64> table = kind == 2
            ? new ConcurrentAutoTokenTable<Flt64>(
                category: LinearCategory.Instance,
                symbols: new List<IIntermediateSymbol>(),
                checkTokenExists: checkTokenExists
            )
            : new ConcurrentManualTokenTable<Flt64>(
                category: LinearCategory.Instance,
                symbols: new List<IIntermediateSymbol>(),
                checkTokenExists: checkTokenExists
            );
        return (table, table.TokenList, table);
    }
}
