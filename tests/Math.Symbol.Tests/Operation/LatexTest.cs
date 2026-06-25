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
    public class LatexTest
    {
        private readonly ISymbol _x = new TestSymbol("x");
        private readonly ISymbol _y = new TestSymbol("y");

        [Fact]
        public void LinearMonomial_ToLatex()
        {
            var m = new LinearMonomial<Flt64>(new Flt64(3.0), _x);
            var latex = m.ToLatex();
            latex.Should().Contain("x");
        }

        [Fact]
        public void LinearPolynomial_ToLatex()
        {
            var p = new LinearPolynomial<Flt64>(
                new[]
                {
                    new LinearMonomial<Flt64>(new Flt64(3.0), _x),
                    new LinearMonomial<Flt64>(new Flt64(2.0), _y),
                },
                new Flt64(5.0));

            var latex = p.ToLatex();
            latex.Should().Contain("x");
            latex.Should().Contain("y");
        }

        [Fact]
        public void QuadraticMonomial_ToLatex()
        {
            var m = QuadraticMonomial<Flt64>.Quadratic(new Flt64(2.0), _x, _x);
            var latex = m.ToLatex();
            latex.Should().Contain("x");
            latex.Should().Contain("^{2}");
        }

        [Fact]
        public void LatexOptions_ShowOneCoefficient()
        {
            var m = new LinearMonomial<Flt64>(Flt64.One, _x);
            var latex = m.ToLatex(new LatexOptions { ShowOneCoefficient = true });
            latex.Should().Contain("1");
        }

        private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;
    }
}
