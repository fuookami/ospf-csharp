#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Math.Tests;
/// <summary>
/// 比较运算符测试 / Comparison operator tests
/// </summary>
public class ComparisonOperatorTest {
    [Fact]
    public void Equal_Without_Precision() {
        var op = new ComparisonEqual<Int8>();
        Assert.True(op[Int8.One, Int8.One]);
        Assert.False(op[Int8.One, Int8.Zero]);
    }

    [Fact]
    public void Unequal_Without_Precision() {
        var op = new ComparisonUnequal<Int8>();
        Assert.True(op[Int8.One, Int8.Zero]);
        Assert.False(op[Int8.One, Int8.One]);
    }

    [Fact]
    public void Less_Without_Precision() {
        var op = new ComparisonLess<Int8>();
        Assert.True(op[Int8.Zero, Int8.One]);
        Assert.False(op[Int8.One, Int8.Zero]);
        Assert.False(op[Int8.One, Int8.One]);
    }

    [Fact]
    public void LessEqual_Without_Precision() {
        var op = new ComparisonLessEqual<Int8>();
        Assert.True(op[Int8.Zero, Int8.One]);
        Assert.True(op[Int8.One, Int8.One]);
        Assert.False(op[Int8.One, Int8.Zero]);
    }

    [Fact]
    public void Greater_Without_Precision() {
        var op = new ComparisonGreater<Int8>();
        Assert.True(op[Int8.One, Int8.Zero]);
        Assert.False(op[Int8.Zero, Int8.One]);
        Assert.False(op[Int8.One, Int8.One]);
    }

    [Fact]
    public void GreaterEqual_Without_Precision() {
        var op = new ComparisonGreaterEqual<Int8>();
        Assert.True(op[Int8.One, Int8.Zero]);
        Assert.True(op[Int8.One, Int8.One]);
        Assert.False(op[Int8.Zero, Int8.One]);
    }

    [Fact]
    public void Equal_With_Precision() {
        var op = new ComparisonEqual<Int8>(new Int8(1));
        Assert.True(op[Int8.One, Int8.One]);
        Assert.True(op[Int8.One, new Int8(2)]);
    }

    [Fact]
    public void Combined_Operator() {
        var op = new ComparisonOperator<Int8>();
        Assert.True(op.Eq(Int8.One, Int8.One));
        Assert.True(op.Neq(Int8.One, Int8.Zero));
        Assert.True(op.Ls(Int8.Zero, Int8.One));
        Assert.True(op.Leq(Int8.One, Int8.One));
        Assert.True(op.Gr(Int8.One, Int8.Zero));
        Assert.True(op.Geq(Int8.One, Int8.One));
    }
}
