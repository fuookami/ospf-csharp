#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.EfCore;
using System;
using System.Linq.Expressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence.Expression;

public class ColumnBinderTests {
    private record TestEntity(string Name, int Value);

    [Fact]
    public void DefaultColumnBinder_Should_Bind_Property() {
        var binder = new DefaultColumnBinder<TestEntity>();
        Expression<Func<TestEntity, object?>>? expr = binder.BindColumn("Name");
        expr.Should().NotBeNull();
    }
}
