#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Math.Symbol.Operation;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Operation.Tests
{
    public class ConvertTest
    {
        private readonly ISymbol _x = new TestSymbol("x");
        private readonly ISymbol _y = new TestSymbol("y");

        [Fact]
        public void LinearMonomial_ToQuadraticMonomial()
        {
            var lm = new LinearMonomial<Flt64>(new Flt64(3.0), _x);
            var qm = lm.ToQuadraticMonomial();
            qm.IsQuadratic.Should().BeFalse();
            qm.Coefficient.Should().Be(new Flt64(3.0));
            qm.Symbol1.Should().Be(_x);
        }

        [Fact]
        public void LinearMonomial_ToCanonicalMonomial()
        {
            var lm = new LinearMonomial<Flt64>(new Flt64(2.0), _x);
            var cm = lm.ToCanonicalMonomial();
            cm.Degree.Should().Be(1);
            cm.Coefficient.Should().Be(new Flt64(2.0));
        }

        [Fact]
        public void QuadraticMonomial_TryToLinearMonomial_Linear()
        {
            var qm = QuadraticMonomial<Flt64>.Linear(new Flt64(5.0), _x);
            var result = qm.TryToLinearMonomial();
            result.IsOk.Should().BeTrue();
            result.Value.Coefficient.Should().Be(new Flt64(5.0));
            result.Value.Symbol.Should().Be(_x);
        }

        [Fact]
        public void QuadraticMonomial_TryToLinearMonomial_Quadratic_Fails()
        {
            var qm = QuadraticMonomial<Flt64>.Quadratic(new Flt64(1.0), _x, _y);
            var result = qm.TryToLinearMonomial();
            result.IsFailed.Should().BeTrue();
        }

        [Fact]
        public void QuadraticPolynomial_TryToLinearPolynomial()
        {
            var qp = new QuadraticPolynomial<Flt64>(
                new[] { QuadraticMonomial<Flt64>.Linear(new Flt64(2.0), _x) },
                new Flt64(3.0));
            var result = qp.TryToLinearPolynomial();
            result.IsOk.Should().BeTrue();
            result.Value.Monomials.Should().HaveCount(1);
            result.Value.Constant.Should().Be(new Flt64(3.0));
        }

        [Fact]
        public void CanonicalPolynomial_TryToLinearPolynomial_FailsForQuadratic()
        {
            var cp = new CanonicalPolynomial<Flt64>(
                new[] { new CanonicalMonomial<Flt64>(new Flt64(1.0),
                    new System.Collections.Generic.Dictionary<ISymbol, int> { [_x] = 2 }) },
                Flt64.Zero);
            var result = cp.TryToLinearPolynomial();
            result.IsFailed.Should().BeTrue();
        }

        private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;
    }
}
