#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

public class TokenCacheContextTests {
    [Fact]
    public void ValueCacheContext_PutAndGet() {
        var ctx = new ValueCacheContext<Flt64>();
        ctx.Put("key", (IReadOnlyList<Flt64>?)null, Flt64.One);
        ctx.Value("key", (IReadOnlyList<Flt64>?)null).Should().Be(Flt64.One);
    }

    [Fact]
    public void ValueCacheContext_Cached_ReturnsTrue() {
        var ctx = new ValueCacheContext<Flt64>();
        ctx.Cached("key", (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        ctx.Put("key", (IReadOnlyList<Flt64>?)null, Flt64.One);
        ctx.Cached("key", (IReadOnlyList<Flt64>?)null).Should().BeTrue();
    }

    [Fact]
    public void ValueCacheContext_Remove() {
        var ctx = new ValueCacheContext<Flt64>();
        ctx.Put("key", (IReadOnlyList<Flt64>?)null, Flt64.One);
        ctx.Remove("key");
        ctx.Cached("key", (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    [Fact]
    public void ValueCacheContext_Clear() {
        var ctx = new ValueCacheContext<Flt64>();
        ctx.Put("k1", (IReadOnlyList<Flt64>?)null, Flt64.One);
        ctx.Put("k2", (IReadOnlyList<Flt64>?)null, new Flt64(2));
        ctx.Clear();
        ctx.Cached("k1", (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        ctx.Cached("k2", (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    [Fact]
    public void LinearFlattenContext_PutAndGet() {
        var ctx = new LinearFlattenContext<Flt64>();
        ctx.Contains("key").Should().BeFalse();
        ctx.Put("key", null);
        ctx.Contains("key").Should().BeTrue();
        ctx.Get("key").Should().BeNull();
    }

    [Fact]
    public void LinearFlattenContext_Remove() {
        var ctx = new LinearFlattenContext<Flt64>();
        ctx.Put("key", null);
        ctx.Remove("key");
        ctx.Contains("key").Should().BeFalse();
    }

    [Fact]
    public void QuadraticFlattenContext_PutAndGet() {
        var ctx = new QuadraticFlattenContext<Flt64>();
        ctx.Put("key", null);
        ctx.Contains("key").Should().BeTrue();
    }

    [Fact]
    public void RangeCacheContext_PutAndGet() {
        var ctx = new RangeCacheContext<Flt64>();
        ctx.Put("key", null);
        ctx.Contains("key").Should().BeTrue();
        ctx.Get("key").Should().BeNull();
    }

    [Fact]
    public void TokenCacheContexts_ClearAll() {
        var contexts = new TokenCacheContexts<Flt64>();
        contexts.LinearFlatten.Put("lf", null);
        contexts.QuadraticFlatten.Put("qf", null);
        contexts.Range.Put("r", null);

        contexts.ClearAll();

        contexts.LinearFlatten.Contains("lf").Should().BeFalse();
        contexts.QuadraticFlatten.Contains("qf").Should().BeFalse();
        contexts.Range.Contains("r").Should().BeFalse();
    }

    [Fact]
    public void TokenCacheContexts_ClearFlatten() {
        var contexts = new TokenCacheContexts<Flt64>();
        contexts.LinearFlatten.Put("lf", null);
        contexts.QuadraticFlatten.Put("qf", null);
        contexts.Range.Put("r", null);

        contexts.ClearFlatten();

        contexts.LinearFlatten.Contains("lf").Should().BeFalse();
        contexts.QuadraticFlatten.Contains("qf").Should().BeFalse();
        contexts.Range.Contains("r").Should().BeTrue();
    }

    [Fact]
    public void TokenCacheContexts_BoundSymbols() {
        var contexts = new TokenCacheContexts<Flt64>();
        contexts.LinearFlatten.Put("a", null);
        contexts.Range.Put("b", null);

        IReadOnlySet<object> bound = contexts.BoundSymbols();
        bound.Should().Contain("a");
        bound.Should().Contain("b");
        bound.Should().NotContain("c");
    }
}
