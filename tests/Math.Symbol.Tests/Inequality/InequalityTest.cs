#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Tests
{
    /// <summary>
    /// 测试不等式 / Tests for inequality types.
    /// </summary>
    public class InequalityTest
    {
        private static readonly ISymbol X = new OwnedSymbol(new SymbolId("x"), "x");
        private static readonly ISymbol Y = new OwnedSymbol(new SymbolId("y"), "y");

        [Fact]
        public void LinearInequalityShouldKeepArguments()
        {
            var mx = new LinearMonomial<Flt64>(new Flt64(1.0), X);
            var lhs = new LinearPolynomial<Flt64>(new[] { mx }, Flt64.Zero);
            var rhs = new LinearPolynomial<Flt64>(System.Array.Empty<LinearMonomial<Flt64>>(), new Flt64(5.0));
            var ineq = new LinearInequality<Flt64>(lhs, rhs, Comparison.LE, "c1");
            Assert.Equal(Comparison.LE, ineq.Comparison);
            Assert.Equal("c1", ineq.Name);
        }

        [Fact]
        public void QuadraticInequalityShouldKeepArguments()
        {
            var qx = QuadraticMonomial<Flt64>.Quadratic(new Flt64(1.0), X, Y);
            var lhs = new QuadraticPolynomial<Flt64>(new[] { qx }, Flt64.Zero);
            var rhs = new QuadraticPolynomial<Flt64>(System.Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(10.0));
            var ineq = new QuadraticInequalityOf<Flt64>(lhs, rhs, Comparison.GE);
            Assert.Equal(Comparison.GE, ineq.Comparison);
        }

        [Fact]
        public void ComparisonShouldExposeBehaviorFlagsAndReverse()
        {
            Assert.True(Comparison.LT.IsStrict());
            Assert.False(Comparison.LE.IsStrict());
            Assert.True(Comparison.LE.IncludesEquality());
            Assert.True(Comparison.LT.IsLessLike());
            Assert.False(Comparison.LT.IsGreaterLike());
            Assert.True(Comparison.GE.IsGreaterLike());

            Assert.Equal(Comparison.GT, Comparison.LT.Reverse());
            Assert.Equal(Comparison.GE, Comparison.LE.Reverse());
            Assert.Equal(Comparison.EQ, Comparison.EQ.Reverse());
            Assert.Equal(Comparison.NE, Comparison.NE.Reverse());
        }

        [Fact]
        public void ComparisonSymbolShouldWork()
        {
            Assert.Equal("<", Comparison.LT.Symbol());
            Assert.Equal("<=", Comparison.LE.Symbol());
            Assert.Equal("=", Comparison.EQ.Symbol());
            Assert.Equal(">=", Comparison.GE.Symbol());
            Assert.Equal(">", Comparison.GT.Symbol());
        }

        [Fact]
        public void LinearInequalityOperatorsShouldBuildComparison()
        {
            var mx = new LinearMonomial<Flt64>(new Flt64(1.0), X);
            var my = new LinearMonomial<Flt64>(new Flt64(2.0), Y);
            var p1 = new LinearPolynomial<Flt64>(new[] { mx }, Flt64.Zero);
            var p2 = new LinearPolynomial<Flt64>(new[] { my }, Flt64.Zero);

            var le = p1.Le(p2);
            Assert.Equal(Comparison.LE, le.Comparison);

            var gt = p1.Gt(p2);
            Assert.Equal(Comparison.GT, gt.Comparison);

            var eq = p1.Eq(p2);
            Assert.Equal(Comparison.EQ, eq.Comparison);
        }

        [Fact]
        public void LinearInequalityAliasNamesShouldWork()
        {
            var mx = new LinearMonomial<Flt64>(new Flt64(1.0), X);
            var my = new LinearMonomial<Flt64>(new Flt64(2.0), Y);
            var p1 = new LinearPolynomial<Flt64>(new[] { mx }, Flt64.Zero);
            var p2 = new LinearPolynomial<Flt64>(new[] { my }, Flt64.Zero);

            Assert.Equal(Comparison.LE, p1.Leq(p2).Comparison);
            Assert.Equal(Comparison.GE, p1.Geq(p2).Comparison);
            Assert.Equal(Comparison.NE, p1.Neq(p2).Comparison);
            Assert.Equal(Comparison.LT, p1.Ls(p2).Comparison);
            Assert.Equal(Comparison.GT, p1.Gr(p2).Comparison);
        }

        [Fact]
        public void QuadraticInequalityOperatorsShouldSupportMixedOrder()
        {
            var qx = QuadraticMonomial<Flt64>.Quadratic(new Flt64(1.0), X, Y);
            var lhs = new QuadraticPolynomial<Flt64>(new[] { qx }, Flt64.Zero);
            var rhs = new QuadraticPolynomial<Flt64>(System.Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(10.0));

            var le = lhs.Le(rhs);
            Assert.Equal(Comparison.LE, le.Comparison);

            var ge = lhs.Ge(new Flt64(5.0));
            Assert.Equal(Comparison.GE, ge.Comparison);
        }

        [Fact]
        public void LinearInequalityReverseShouldSwapSides()
        {
            var mx = new LinearMonomial<Flt64>(new Flt64(1.0), X);
            var lhs = new LinearPolynomial<Flt64>(new[] { mx }, Flt64.Zero);
            var rhs = new LinearPolynomial<Flt64>(System.Array.Empty<LinearMonomial<Flt64>>(), new Flt64(5.0));
            var ineq = lhs.Le(rhs);
            var reversed = ineq.Reverse();
            Assert.Equal(Comparison.GE, reversed.Comparison);
            Assert.Same(rhs, reversed.Lhs);
            Assert.Same(lhs, reversed.Rhs);
        }

        [Fact]
        public void CanonicalInequalityReverseShouldWork()
        {
            var cx = new CanonicalMonomial<Flt64>(new Flt64(1.0), new System.Collections.Generic.Dictionary<ISymbol, int> { [X] = 1 });
            var lhs = new CanonicalPolynomial<Flt64>(new[] { cx }, Flt64.Zero);
            var rhs = new CanonicalPolynomial<Flt64>(System.Array.Empty<CanonicalMonomial<Flt64>>(), new Flt64(5.0));
            var ineq = new CanonicalInequality<Flt64>(lhs, rhs, Comparison.LE);
            var reversed = ineq.Reverse();
            Assert.Equal(Comparison.GE, reversed.Comparison);
        }

        [Fact]
        public void NamedOverloadsShouldWork()
        {
            var mx = new LinearMonomial<Flt64>(new Flt64(1.0), X);
            var p1 = new LinearPolynomial<Flt64>(new[] { mx }, Flt64.Zero);
            var p2 = new LinearPolynomial<Flt64>(System.Array.Empty<LinearMonomial<Flt64>>(), new Flt64(5.0));
            var ineq = p1.Le(p2, "capacity", "Capacity Constraint");
            Assert.Equal("capacity", ineq.Name);
            Assert.Equal("Capacity Constraint", ineq.DisplayName);
        }

        [Fact]
        public void ScalarOverloadsShouldWork()
        {
            var mx = new LinearMonomial<Flt64>(new Flt64(1.0), X);
            var p = new LinearPolynomial<Flt64>(new[] { mx }, Flt64.Zero);

            var le = p.Le(new Flt64(10.0));
            Assert.Equal(Comparison.LE, le.Comparison);

            var ge = new Flt64(0.0).Ge(p);
            Assert.Equal(Comparison.GE, ge.Comparison);
        }
    }
}
