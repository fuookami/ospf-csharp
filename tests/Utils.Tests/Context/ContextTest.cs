#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Context;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Context;

public class ContextTest {
    [Fact]
    public void ContextVar_SetAndGet_ReturnsValue() {
        var ctxVar = new ContextVar<int>(0);
        using ContextScope<int> scope = ctxVar.Set(42);
        ctxVar.Get().Should().Be(42);
    }

    [Fact]
    public void ContextVar_DefaultValue_ReturnsWhenNotSet() {
        var ctxVar = new ContextVar<int>(-1);
        ctxVar.Get().Should().Be(-1);
    }

    [Fact]
    public void ContextVar_Dispose_RevertsToDefault() {
        var ctxVar = new ContextVar<int>(0);
        using (ContextScope<int> scope = ctxVar.Set(42)) {
            ctxVar.Get().Should().Be(42);
        }
        ctxVar.Get().Should().Be(0);
    }

    [Fact]
    public void ContextVar_NestedScopes_InnermostWins() {
        var ctxVar = new ContextVar<int>(0);
        using ContextScope<int> outer = ctxVar.Set(10);
        using ContextScope<int> inner = ctxVar.Set(20);
        ctxVar.Get().Should().Be(20);
    }

    [Fact]
    public void ContextVar_NestedScopes_OuterRestoresAfterDispose() {
        var ctxVar = new ContextVar<int>(0);
        using ContextScope<int> outer = ctxVar.Set(10);
        using (ContextScope<int> inner = ctxVar.Set(20)) {
            ctxVar.Get().Should().Be(20);
        }
        ctxVar.Get().Should().Be(10);
    }
}
