#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Token;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Token;

public class TokenCacheKeyTests {
    [Fact]
    public void TokenCacheKey_Equality_Works() {
        var key1 = new TokenCacheKey(1, "dim1");
        var key2 = new TokenCacheKey(1, "dim1");

        key1.Should().Be(key2);
        (key1 == key2).Should().BeTrue();
        key1.GetHashCode().Should().Be(key2.GetHashCode());
    }

    [Fact]
    public void TokenCacheKey_DifferentKeys_NotEqual() {
        var key1 = new TokenCacheKey(1, "dim1");
        var key2 = new TokenCacheKey(2, "dim1");
        var key3 = new TokenCacheKey(1, "dim2");

        key1.Should().NotBe(key2);
        key1.Should().NotBe(key3);
        (key1 == key2).Should().BeFalse();
        (key1 == key3).Should().BeFalse();
    }

    [Fact]
    public void TokenCacheKey_Properties_AreAccessible() {
        var key = new TokenCacheKey(42, "cacheDim");

        key.SymbolIndex.Should().Be(42);
        key.CacheDimension.Should().Be("cacheDim");
    }

    [Fact]
    public void TokenCacheKey_WithExpression_ReturnsNotEqual() {
        var key1 = new TokenCacheKey(1, "dimA");
        var key2 = new TokenCacheKey(1, "dimB");

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void TokenCacheKey_ZeroIndex_Works() {
        var key = new TokenCacheKey(0, "default");

        key.SymbolIndex.Should().Be(0);
        key.CacheDimension.Should().Be("default");
    }
}
