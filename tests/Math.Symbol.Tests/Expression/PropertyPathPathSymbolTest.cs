#nullable enable

using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Expression;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Expression.Tests;
/// <summary>
/// PropertyPath 测试 / PropertyPath Tests.
/// </summary>
public class PropertyPathTests {
    [Fact]
    public void ParseFromString_shouldSplitDots() {
        var path = PropertyPath.Parse("user.address.city");
        Assert.Equal("user.address.city", path.Value);
        Assert.Equal(3, path.Depth);
        Assert.Equal("user", path.Root);
        Assert.Equal("city", path.Leaf);
        Assert.False(path.IsEmpty);
        Assert.True(path.IsNotEmpty);
    }

    [Fact]
    public void ParseSingleSegment_shouldWork() {
        var path = PropertyPath.Parse("age");
        Assert.Equal(1, path.Depth);
        Assert.Equal("age", path.Root);
        Assert.Equal("age", path.Leaf);
    }

    [Fact]
    public void CreateFromSegments_shouldJoin() {
        var path = PropertyPath.Of("user", "address", "city");
        Assert.Equal("user.address.city", path.Value);
        Assert.Equal(3, path.Depth);
    }

    [Fact]
    public void EmptyPath_shouldBeEmpty() {
        PropertyPath path = PropertyPath.Empty;
        Assert.True(path.IsEmpty);
        Assert.False(path.IsNotEmpty);
        Assert.Equal(0, path.Depth);
        Assert.Null(path.Root);
        Assert.Null(path.Leaf);
    }

    [Fact]
    public void ParentAndChild_shouldNavigate() {
        var path = PropertyPath.Parse("a.b.c");
        Assert.Equal("a.b", path.Parent!.Value.Value);
        Assert.Equal("b.c", path.Child!.Value.Value);
    }

    [Fact]
    public void Concat_shouldJoin() {
        var a = PropertyPath.Parse("user");
        var b = PropertyPath.Parse("address.city");
        PropertyPath result = a.Concat(b);
        Assert.Equal("user.address.city", result.Value);
    }

    [Fact]
    public void SubPath_shouldDetect() {
        var parent = PropertyPath.Parse("user");
        var child = PropertyPath.Parse("user.address.city");
        Assert.True(child.IsSubPathOf(parent));
        Assert.False(parent.IsSubPathOf(child));
        Assert.True(parent.IsParentPathOf(child));
    }

    [Fact]
    public void ParseInvalid_shouldReturnNull() {
        Assert.Null(PropertyPath.ParseOrNull(""));
        Assert.Null(PropertyPath.ParseOrNull("123invalid"));
        Assert.Null(PropertyPath.ParseOrNull("a..b"));
    }
}

/// <summary>
/// PathSymbol 测试 / PathSymbol Tests.
/// </summary>
public class PathSymbolTests {
    [Fact]
    public void Identity_shouldHaveCorrectFormat() {
        var sym = PathSymbol.From(PropertyPath.Parse("age"));
        Assert.Equal("age", sym.Name);
        Assert.Equal("age", sym.DisplayName);
        Assert.Equal("path:age", sym.SymbolId);
    }

    [Fact]
    public void Equality_shouldWork() {
        var a = PathSymbol.From("age");
        var b = PathSymbol.From("age");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Inequality_shouldDetectDifferentPaths() {
        var a = PathSymbol.From("age");
        var b = PathSymbol.From("name");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FromSegments_shouldBuild() {
        var sym = PathSymbol.Of("user", "age");
        Assert.Equal("user.age", sym.Path.Value);
        Assert.Equal("path:user.age", sym.SymbolId);
    }
}

/// <summary>
/// 双向转换测试 / Bidirectional Conversion Tests.
/// </summary>
public class BidirectionalConversionTests {
    [Fact]
    public void PropertyPathToPathSymbol_shouldConvert() {
        var path = PropertyPath.Parse("age");
        var sym = path.ToPathSymbol();
        Assert.Equal("path:age", sym.SymbolId);
    }

    [Fact]
    public void StringToPathSymbol_shouldConvert() {
        var sym = "age".ToPathSymbol();
        Assert.Equal("path:age", sym.SymbolId);
    }

    [Fact]
    public void PathSymbolToPropertyPath_shouldConvert() {
        var sym = PathSymbol.From("age");
        ISymbol asSymbol = sym;
        PropertyPath? path = asSymbol.ToPropertyPathOrNull();
        Assert.NotNull(path);
        Assert.Equal("age", path!.Value.Value);
    }

    [Fact]
    public void NonPathSymbolToPropertyPath_shouldReturnNull() {
        ISymbol sym = new TestNonPathSymbol("x");
        Assert.Null(sym.ToPropertyPathOrNull());
    }

    private sealed record TestNonPathSymbol(string Name, string? DisplayName = null) : ISymbol;

    [Fact]
    public void IdentifiedSymbolWithPathPrefix_shouldParse() {
        var sym = PathSymbol.From("user.age");
        IIdentifiedSymbol identified = sym;
        PropertyPath? path = identified.ToPropertyPathFromIdOrNull();
        Assert.NotNull(path);
        Assert.Equal("user.age", path!.Value.Value);
    }

    [Fact]
    public void IdentifiedSymbolWithoutPathPrefix_shouldReturnNull() {
        var sym = new TestIdentifiedSymbol("x", "custom-id");
        Assert.Null(sym.ToPropertyPathFromIdOrNull());
    }

    private sealed record TestIdentifiedSymbol(string Name, string SymbolId, string? DisplayName = null) : IIdentifiedSymbol;
}
