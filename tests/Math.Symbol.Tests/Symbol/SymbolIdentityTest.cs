#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Runtime.CompilerServices;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Tests;
/// <summary>
/// 测试符号身份工具 / Tests for symbol identity helpers.
/// </summary>
public class SymbolIdentityTest {
    private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;

    [Fact]
    public void StableIdShouldPreferIdentifiedAndOwned() {
        var owned = new OwnedSymbol(new SymbolId("my-id"), "x");
        Assert.Equal(new SymbolId("my-id"), SymbolIdentity.StableId(owned));
        Assert.True(SymbolIdentity.HasStableId(owned));
    }

    [Fact]
    public void StableIdShouldFallbackToObjectIdentity() {
        var sym = new TestSymbol("x");
        SymbolId id = SymbolIdentity.StableId(sym);
        Assert.Contains("x#", id.Value);
        Assert.False(SymbolIdentity.HasStableId(sym));
    }

    [Fact]
    public void DefaultComparatorShouldUseNameThenStableId() {
        var a = new TestSymbol("a");
        var b = new TestSymbol("b");
        Assert.True(SymbolIdentity.DefaultSymbolComparator.Compare(a, b) < 0);
        Assert.True(SymbolIdentity.DefaultSymbolComparator.Compare(b, a) > 0);
    }

    [Fact]
    public void StableIdentityHelpersShouldExposeExplicitIdentityOnly() {
        var sym = new TestSymbol("x");
        Assert.Null(SymbolIdentity.StableIdOrNull(sym));
        Assert.False(SymbolIdentity.HasStableId(sym));

        var owned = new OwnedSymbol(new SymbolId("id1"), "y");
        Assert.Equal(new SymbolId("id1"), SymbolIdentity.StableIdOrNull(owned));
        Assert.True(SymbolIdentity.HasStableId(owned));
    }

    [Fact]
    public void StableComparatorShouldRequireExplicitStableIds() {
        var a = new OwnedSymbol(new SymbolId("a-id"), "x");
        var b = new OwnedSymbol(new SymbolId("b-id"), "x");
        // Same name, different stable ids
        Assert.True(SymbolIdentity.DefaultStableSymbolComparator.Compare(a, b) < 0);
    }

    [Fact]
    public void OwnedSymbolShouldWrapExistingSymbol() {
        var inner = new TestSymbol("z", "Zee");
        OwnedSymbol owned = SymbolIdentity.Owned(inner);
        Assert.Equal("z", owned.Name);
        Assert.Equal("Zee", owned.DisplayName);
        Assert.True(SymbolIdentity.HasStableId(owned));
    }

    [Fact]
    public void RequireStableIdShouldReturnOkForOwned() {
        var owned = new OwnedSymbol(new SymbolId("id"), "x");
        Result<SymbolId, ErrorCode, Error<ErrorCode>> result = SymbolIdentity.RequireStableId(owned);
        Assert.True(result.IsOk);
        Assert.Equal(new SymbolId("id"), result.Value);
    }

    [Fact]
    public void RequireStableIdShouldReturnFailedForPlainSymbol() {
        var sym = new TestSymbol("x");
        Result<SymbolId, ErrorCode, Error<ErrorCode>> result = SymbolIdentity.RequireStableId(sym);
        Assert.True(result.IsFailed);
    }
}
