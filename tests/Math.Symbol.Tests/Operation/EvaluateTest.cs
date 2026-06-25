#nullable enable

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Math.Symbol.Operation;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Operation.Tests
{
    public class EvaluateTest
    {
        private readonly ISymbol _x = new TestSymbol("x");
        private readonly ISymbol _y = new TestSymbol("y");

        [Fact]
        public void LinearPolynomial_Evaluate()
        {
            // 3x + 2y + 1, x=2, y=3 => 6 + 6 + 1 = 13
            var p = new LinearPolynomial<Flt64>(
                new[]
                {
                    new LinearMonomial<Flt64>(new Flt64(3.0), _x),
                    new LinearMonomial<Flt64>(new Flt64(2.0), _y),
                },
                new Flt64(1.0));

            var values = new Dictionary<ISymbol, Flt64> { [_x] = new(2.0), [_y] = new(3.0) };
            var result = p.Evaluate(values);
            result.Should().Be(new Flt64(13.0));
        }

        [Fact]
        public void QuadraticPolynomial_Evaluate()
        {
            // x^2 + 2xy + 3, x=2, y=3 => 4 + 12 + 3 = 19
            var p = new QuadraticPolynomial<Flt64>(
                new[]
                {
                    QuadraticMonomial<Flt64>.Quadratic(new Flt64(1.0), _x, _x),
                    QuadraticMonomial<Flt64>.Quadratic(new Flt64(2.0), _x, _y),
                },
                new Flt64(3.0));

            var values = new Dictionary<ISymbol, Flt64> { [_x] = new(2.0), [_y] = new(3.0) };
            var result = p.Evaluate(values);
            result.Should().Be(new Flt64(19.0));
        }

        [Fact]
        public void CanonicalPolynomial_Evaluate()
        {
            // 2*x^2*y + 5, x=2, y=3 => 2*4*3 + 5 = 29
            var p = new CanonicalPolynomial<Flt64>(
                new[]
                {
                    new CanonicalMonomial<Flt64>(new Flt64(2.0),
                        new Dictionary<ISymbol, int> { [_x] = 2, [_y] = 1 }),
                },
                new Flt64(5.0));

            var values = new Dictionary<ISymbol, Flt64> { [_x] = new(2.0), [_y] = new(3.0) };
            var result = p.Evaluate(values);
            result.Should().Be(new Flt64(29.0));
        }

        [Fact]
        public void LinearPolynomial_PartialEvaluate()
        {
            // 3x + 2y + 1, fix x=2 => 2y + 7
            var p = new LinearPolynomial<Flt64>(
                new[]
                {
                    new LinearMonomial<Flt64>(new Flt64(3.0), _x),
                    new LinearMonomial<Flt64>(new Flt64(2.0), _y),
                },
                new Flt64(1.0));

            var values = new Dictionary<ISymbol, Flt64> { [_x] = new(2.0) };
            var result = p.PartialEvaluate(values);
            result.Monomials.Should().HaveCount(1);
            result.Constant.Should().Be(new Flt64(7.0));
        }

        [Fact]
        public void LinearPolynomial_EvaluateWithMapValueProvider()
        {
            var p = new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(3.0), _x) },
                new Flt64(1.0));

            var provider = new MapValueProvider<Flt64>(
                new Dictionary<ISymbol, Flt64> { [_x] = new(2.0) });
            var result = p.Evaluate(provider);
            result.Should().Be(new Flt64(7.0));
        }

        private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;
    }
}
