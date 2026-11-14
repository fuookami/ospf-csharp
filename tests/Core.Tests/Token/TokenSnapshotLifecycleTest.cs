#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
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
namespace Fuookami.Ospf.Core.Tests.Token;

public class TokenSnapshotLifecycleTest {
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MutableTable_ShouldRefreshOrderAndSolutionAfterChanges(bool manual) {
        using ConcurrentMutableTokenTable<Flt64> table = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var x = new RealVar("mutable-x");
        var y = new RealVar("mutable-y");

        table.TokenList.TokensInSolver.Should().BeEmpty();
        table.TokenList.CachedSolution.Should().BeFalse();
        table.Add(x).IsOk.Should().BeTrue();
        table.TokenList.TokensInSolver.Should().ContainSingle().Which.Variable.Should().BeSameAs(x);
        Token<Flt64> token = table.TokenList.Find(x)!;
        token.RefreshCallbacks.Should().ContainKey(table.TokenList);
        token.SetResult(Flt64.One);
        table.TokenList.CachedSolution.Should().BeTrue();
        table.Add(y).IsOk.Should().BeTrue();
        table.TokenList.TokensInSolver.Select(t => t.Variable).Should().BeEquivalentTo(new[] { x, y });
        table.Remove(x);
        token.RefreshCallbacks.Should().NotContainKey(table.TokenList);
        table.TokenList.TokensInSolver.Should().ContainSingle().Which.Variable.Should().BeSameAs(y);
        table.TokenList.CachedSolution.Should().BeFalse();
        table.TokenList.Find(y)!.SetResult(Flt64.One);
        table.TokenList.CachedSolution.Should().BeTrue();
        table.TokenList.ClearSolution();
        table.TokenList.CachedSolution.Should().BeFalse();
        Token<Flt64> remaining = table.TokenList.Find(y)!;
        table.Dispose();
        table.Dispose();
        remaining.RefreshCallbacks.Should().NotContainKey(table.TokenList);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Snapshots_ShouldIsolateStructureShareValuesAndReleaseCallbacks(bool manual) {
        using ConcurrentMutableTokenTable<Flt64> parent = manual
            ? new ConcurrentManualTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>())
            : new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var x = new RealVar("snapshot-x");
        var y = new RealVar("snapshot-y");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.TokenList.Find(x)!;
        using var first = new ConcurrentTokenTable<Flt64>(parent);
        using var second = new ConcurrentTokenTable<Flt64>(parent);
        first.TokenList.CachedSolution.Should().BeFalse();
        second.TokenList.CachedSolution.Should().BeFalse();
        first.TokenList.TokensInSolver.Should().ContainSingle();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.Remove(x);

        first.TokenList.Find(x).Should().BeSameAs(token);
        first.TokenList.Find(y).Should().BeNull();
        second.TokenList.Find(x).Should().BeSameAs(token);
        token.SetResult(Flt64.One);
        first.TokenList.CachedSolution.Should().BeTrue();
        second.TokenList.CachedSolution.Should().BeTrue();
        first.Dispose();
        first.Dispose();
        token.RefreshCallbacks.Should().NotContainKey(first.TokenList);
        token.RefreshCallbacks.Should().ContainKey(second.TokenList);
        second.TokenList.ClearSolution();
        second.TokenList.CachedSolution.Should().BeFalse();
        second.Dispose();
        token.RefreshCallbacks.Should().NotContainKey(second.TokenList);
        parent.TokenList.Find(y).Should().NotBeNull();
    }

    [Fact]
    public void TokenListConstructor_ShouldCopyTheSuppliedMapping() {
        var x = new RealVar("dictionary-x");
        var token = new Token<Flt64>(
            variable: x,
            solverIndex: 0,
            refreshCallbacks: new Dictionary<object, Action<bool>>()
        );
        var source = new Dictionary<VariableItemKey, Token<Flt64>> { [x.Key] = token };
        using var snapshot = new TokenList<Flt64>(source);
        source.Clear();
        snapshot.Find(x).Should().BeSameAs(token);
    }

    [Fact]
    public async Task FailedLinearBuild_ShouldReleaseItsSnapshotCallbacks() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("failed-build-x");
        parent.Add(x).IsOk.Should().BeTrue();
        var input = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero);
        var symbol = new LinearFunctionSymbolAdapter<Flt64>(
            new AbsFunction<Flt64>(input, IIntoValue<Flt64>.Identity));
        var lhs = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero);
        var rhs = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
        parent.AddConstraint(new LinearInequality<Flt64>(lhs, rhs, Comparison.LE), null).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.Tokens.TokenList.Find(x)!;
        int before = token.RefreshCallbacks.Count;

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        result.IsFailed.Should().BeTrue();
        token.RefreshCallbacks.Should().HaveCount(before);
        parent.Tokens.TokenList.Find(x).Should().BeSameAs(token);
    }

    [Fact]
    public async Task Models_ShouldOwnIndependentSnapshotsWhenParentChangesAndCloses() {
        var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("model-x");
        var y = new RealVar("model-y");
        parent.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> first = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearMechanismModel<Flt64> second = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Token<Flt64> token = parent.Tokens.TokenList.Find(x)!;
        parent.Remove(x);
        parent.Add(y).IsOk.Should().BeTrue();
        parent.Dispose();

        first.Tokens.Find(x).Should().BeSameAs(token);
        second.Tokens.Find(x).Should().BeSameAs(token);
        first.Tokens.Find(y).Should().BeNull();
        second.Tokens.Find(y).Should().BeNull();
        first.Dispose();
        second.Tokens.TokenList.CachedSolution.Should().BeFalse();
        token.SetResult(Flt64.One);
        second.Tokens.TokenList.CachedSolution.Should().BeTrue();
        token.RefreshCallbacks.Should().ContainKey(second.Tokens.TokenList);
    }

    [Fact]
    public async Task ConcurrentSnapshotDisposalAndResultUpdates_ShouldNotRaceCallbackEnumeration() {
        using var parent = new ConcurrentAutoTokenTable<Flt64>(LinearCategory.Instance, new List<IIntermediateSymbol>());
        var x = new RealVar("concurrent-x");
        parent.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> token = parent.TokenList.Find(x)!;
        var writes = Task.Run(() => {
            for (int i = 0; i < 3000; i++) {
                token.SetResult(new Flt64(i));
                token.ResultFlt64 = null;
            }
        });
        var snapshots = Task.Run(() => {
            for (int i = 0; i < 3000; i++) {
                using var snapshot = new ConcurrentTokenTable<Flt64>(parent);
                _ = snapshot.TokenList.CachedSolution;
            }
        });
        await Task.WhenAll(writes, snapshots);
        token.RefreshCallbacks.Should().ContainSingle().Which.Key.Should().BeSameAs(parent.TokenList);
        using var last = new ConcurrentTokenTable<Flt64>(parent);
        last.TokenList.CachedSolution.Should().BeFalse();
        token.SetResult(Flt64.One);
        last.TokenList.CachedSolution.Should().BeTrue();
    }
}
