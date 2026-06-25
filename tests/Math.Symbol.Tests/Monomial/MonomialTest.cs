#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Tests;
/// <summary>
/// 测试单项式运算 / Tests for monomial arithmetic.
/// </summary>
public class MonomialTest {
    private static readonly ISymbol X = new OwnedSymbol(new SymbolId("x"), "x");
    private static readonly ISymbol Y = new OwnedSymbol(new SymbolId("y"), "y");

    [Fact]
    public void LinearMonomialShouldKeepArguments() {
        var m = new LinearMonomial<Flt64>(new Flt64(3.0), X);
        Assert.Equal(new Flt64(3.0), m.Coefficient);
        Assert.Equal("x", m.Symbol.Name);
        Assert.IsType<LinearCategory>(m.Category);
    }

    [Fact]
    public void QuadraticMonomialFlagAndCategory() {
        var linear = QuadraticMonomial<Flt64>.Linear(new Flt64(2.0), X);
        Assert.False(linear.IsQuadratic);
        Assert.IsType<LinearCategory>(linear.Category);

        var quad = QuadraticMonomial<Flt64>.Quadratic(new Flt64(5.0), X, Y);
        Assert.True(quad.IsQuadratic);
        Assert.IsType<QuadraticCategory>(quad.Category);
    }

    [Fact]
    public void LinearMonomialOperatorsShouldComposeToPolynomial() {
        var a = new LinearMonomial<Flt64>(new Flt64(2.0), X);
        var b = new LinearMonomial<Flt64>(new Flt64(3.0), Y);

        // monomial + monomial = polynomial
        LinearPolynomial<Flt64> sum = a + b;
        Assert.Equal(2, sum.Monomials.Count);

        // monomial * scalar
        LinearMonomial<Flt64> scaled = a * new Flt64(4.0);
        Assert.Equal(new Flt64(8.0), scaled.Coefficient);

        // unary negation
        LinearMonomial<Flt64> neg = -a;
        Assert.Equal(new Flt64(-2.0), neg.Coefficient);

        // monomial * monomial = quadratic monomial
        QuadraticMonomial<Flt64> product = a * b;
        Assert.Equal(new Flt64(6.0), product.Coefficient);
        Assert.True(product.IsQuadratic);
    }

    [Fact]
    public void QuadraticMonomialOperatorsShouldSupportLinearAndConstant() {
        var q = QuadraticMonomial<Flt64>.Quadratic(new Flt64(2.0), X, Y);
        var l = new LinearMonomial<Flt64>(new Flt64(3.0), X);

        // quadratic + linear = quadratic polynomial
        QuadraticPolynomial<Flt64> sum = q + l;
        Assert.Equal(2, sum.Monomials.Count);

        // quadratic + scalar
        QuadraticPolynomial<Flt64> sumScalar = q + new Flt64(1.0);
        Assert.Equal(new Flt64(1.0), sumScalar.Constant);
    }

    [Fact]
    public void LinearMonomialScalarLeftMultiplication() {
        var m = new LinearMonomial<Flt64>(new Flt64(3.0), X);
        LinearMonomial<Flt64> result = new Flt64(2.0) * m;
        Assert.Equal(new Flt64(6.0), result.Coefficient);
    }

    [Fact]
    public void LinearMonomialPlusMinusScalar() {
        var m = new LinearMonomial<Flt64>(new Flt64(3.0), X);

        LinearPolynomial<Flt64> plusS = m + new Flt64(5.0);
        Assert.Equal(new Flt64(5.0), plusS.Constant);

        LinearPolynomial<Flt64> minusS = m - new Flt64(1.0);
        Assert.Equal(new Flt64(-1.0), minusS.Constant);

        LinearPolynomial<Flt64> sMinus = new Flt64(10.0) - m;
        Assert.Equal(new Flt64(10.0), sMinus.Constant);
        Assert.Equal(new Flt64(-3.0), sMinus.Monomials[0].Coefficient);
    }
}
