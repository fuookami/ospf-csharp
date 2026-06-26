#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.EfCore;
using System;
using System.Linq.Expressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence.Expression;

public class TranslatorTests {
    private record TestEntity(string Name, int Value);

    [Fact]
    public void DefaultBooleanTranslator_Should_ReturnNull_ForUnmapped() {
        var translator = new DefaultBooleanTranslator<TestEntity>();
        Expression<Func<TestEntity, bool>>? result = translator.Translate(new object());
        result.Should().BeNull();
    }

    [Fact]
    public void DefaultOrderByTranslator_Should_Bind() {
        var translator = new DefaultOrderByTranslator<TestEntity>();
        Expression<Func<TestEntity, object>>? expr = translator.Translate("Name");
        expr.Should().NotBeNull();
    }

    [Fact]
    public void DefaultScalarTranslator_Should_Bind() {
        var translator = new DefaultScalarTranslator<TestEntity>();
        Expression<Func<TestEntity, int>>? expr = translator.Translate<int>("Value");
        expr.Should().NotBeNull();
    }
}
