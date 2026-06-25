#nullable enable

using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Tests
{
    /// <summary>
    /// 测试多项式运算 / Tests for polynomial arithmetic.
    /// </summary>
    public class PolynomialTest
    {
        private static readonly ISymbol X = new OwnedSymbol(new SymbolId("x"), "x");
        private static readonly ISymbol Y = new OwnedSymbol(new SymbolId("y"), "y");

        [Fact]
        public void LinearPolynomialCategoryShouldBeLinear()
        {
            var mx = new LinearMonomial<Flt64>(new Flt64(2.0), X);
            var my = new LinearMonomial<Flt64>(new Flt64(3.0), Y);
            var poly = new LinearPolynomial<Flt64>(new[] { mx, my }, new Flt64(1.0));
            Assert.IsType<LinearCategory>(poly.Category);
        }

        [Fact]
        public void QuadraticPolynomialCategoryShouldFollowMonomials()
        {
            var qx = QuadraticMonomial<Flt64>.Linear(new Flt64(2.0), X);
            var poly = new QuadraticPolynomial<Flt64>(new[] { qx }, Flt64.Zero);
            Assert.IsType<LinearCategory>(poly.Category);

            var qq = QuadraticMonomial<Flt64>.Quadratic(new Flt64(1.0), X, Y);
            var poly2 = new QuadraticPolynomial<Flt64>(new[] { qq }, Flt64.Zero);
            Assert.IsType<QuadraticCategory>(poly2.Category);
        }

        [Fact]
        public void LinearPolynomialOperatorsShouldWork()
        {
            var mx = new LinearMonomial<Flt64>(new Flt64(2.0), X);
            var my = new LinearMonomial<Flt64>(new Flt64(3.0), Y);
            var p1 = new LinearPolynomial<Flt64>(new[] { mx }, new Flt64(1.0));
            var p2 = new LinearPolynomial<Flt64>(new[] { my }, new Flt64(2.0));

            // poly + poly
            var sum = p1 + p2;
            Assert.Equal(2, sum.Monomials.Count);
            Assert.Equal(new Flt64(3.0), sum.Constant);

            // poly - poly
            var diff = p1 - p2;
            Assert.Equal(new Flt64(-1.0), diff.Constant);

            // poly * scalar
            var scaled = p1 * new Flt64(3.0);
            Assert.Equal(new Flt64(6.0), scaled.Monomials[0].Coefficient);
            Assert.Equal(new Flt64(3.0), scaled.Constant);

            // unary negation
            var neg = -p1;
            Assert.Equal(new Flt64(-2.0), neg.Monomials[0].Coefficient);
            Assert.Equal(new Flt64(-1.0), neg.Constant);

            // poly + scalar
            var plusS = p1 + new Flt64(5.0);
            Assert.Equal(new Flt64(6.0), plusS.Constant);

            // poly + monomial
            var plusM = p1 + my;
            Assert.Equal(2, plusM.Monomials.Count);
        }

        [Fact]
        public void QuadraticPolynomialOperatorsShouldSupportLinearAndConstant()
        {
            var qx = QuadraticMonomial<Flt64>.Quadratic(new Flt64(2.0), X, Y);
            var p1 = new QuadraticPolynomial<Flt64>(new[] { qx }, new Flt64(1.0));

            // + scalar
            var sum = p1 + new Flt64(3.0);
            Assert.Equal(new Flt64(4.0), sum.Constant);

            // * scalar
            var scaled = p1 * new Flt64(2.0);
            Assert.Equal(new Flt64(4.0), scaled.Monomials[0].Coefficient);

            // unary negation
            var neg = -p1;
            Assert.Equal(new Flt64(-2.0), neg.Monomials[0].Coefficient);
        }

        [Fact]
        public void SumAggregationShouldWork()
        {
            var mx = new LinearMonomial<Flt64>(new Flt64(1.0), X);
            var my = new LinearMonomial<Flt64>(new Flt64(2.0), Y);
            var sum = PolynomialDsl.Sum(new[] { mx, my });
            Assert.Equal(2, sum.Monomials.Count);
        }

        [Fact]
        public void QSumAggregationShouldWork()
        {
            var qx = QuadraticMonomial<Flt64>.Linear(new Flt64(1.0), X);
            var qy = QuadraticMonomial<Flt64>.Linear(new Flt64(2.0), Y);
            var sum = PolynomialDsl.QSum(new[] { qx, qy });
            Assert.Equal(2, sum.Monomials.Count);
        }

        [Fact]
        public void SumShouldThrowOnEmpty()
        {
            Assert.Throws<System.InvalidOperationException>(() =>
                PolynomialDsl.Sum(System.Array.Empty<LinearMonomial<Flt64>>()));
        }

        [Fact]
        public void SumSafeShouldReturnFailedOnEmpty()
        {
            var result = PolynomialDsl.SumSafe<Flt64, Flt64>(
                System.Array.Empty<Flt64>(), _ => null);
            Assert.True(result.IsFailed);
        }

        [Fact]
        public void SumOrNullShouldReturnNullOnEmpty()
        {
            var result = PolynomialDsl.SumOrNull<Flt64, Flt64>(
                System.Array.Empty<Flt64>(), _ => null);
            Assert.Null(result);
        }

        [Fact]
        public void ToLinearPolynomialOrNullShouldWorkForLinearQuadratic()
        {
            var qx = QuadraticMonomial<Flt64>.Linear(new Flt64(2.0), X);
            var poly = new QuadraticPolynomial<Flt64>(new[] { qx }, new Flt64(1.0));
            var linear = poly.ToLinearPolynomialOrNull();
            Assert.NotNull(linear);
            Assert.Single(linear!.Monomials);
        }

        [Fact]
        public void ToLinearPolynomialOrNullShouldReturnNullForQuadratic()
        {
            var qx = QuadraticMonomial<Flt64>.Quadratic(new Flt64(2.0), X, Y);
            var poly = new QuadraticPolynomial<Flt64>(new[] { qx }, Flt64.Zero);
            Assert.Null(poly.ToLinearPolynomialOrNull());
        }
    }
}
