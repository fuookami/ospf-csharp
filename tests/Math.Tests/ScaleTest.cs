#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Math.Tests;
/// <summary>
/// 缩放因子测试 / Scale tests
/// </summary>
public class ScaleTest {
    [Fact]
    public void Kilo_Is_Defined() => Assert.NotNull(Scale.Kilo);

    [Fact]
    public void Milli_Is_Defined() => Assert.NotNull(Scale.Milli);

    [Fact]
    public void All_Prefixes_Defined() {
        Assert.NotNull(Scale.Atto);
        Assert.NotNull(Scale.Femto);
        Assert.NotNull(Scale.Pico);
        Assert.NotNull(Scale.Nano);
        Assert.NotNull(Scale.Micro);
        Assert.NotNull(Scale.Milli);
        Assert.NotNull(Scale.Centi);
        Assert.NotNull(Scale.Deci);
        Assert.NotNull(Scale.Deca);
        Assert.NotNull(Scale.Hecto);
        Assert.NotNull(Scale.Kilo);
        Assert.NotNull(Scale.Mega);
        Assert.NotNull(Scale.Giga);
        Assert.NotNull(Scale.Tera);
        Assert.NotNull(Scale.Peta);
        Assert.NotNull(Scale.Exa);
    }

    [Fact]
    public void Invoke_With_Int() {
        var scale = Scale.Invoke(10, 3);
        Assert.NotNull(scale);
    }

    [Fact]
    public void Invoke_With_Double() {
        var scale = Scale.Invoke(2.5, 1);
        Assert.NotNull(scale);
    }

    [Fact]
    public void Multiply_Scales() {
        Scale result = Scale.Kilo * Scale.Milli;
        Assert.NotNull(result);
    }

    [Fact]
    public void Unary_Plus() {
        Scale result = +Scale.Kilo;
        Assert.Same(Scale.Kilo, result);
    }
}
