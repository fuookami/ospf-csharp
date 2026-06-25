#nullable enable

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Math.Symbol.Operation;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Operation.Tests
{
    public class MatrixFormTest
    {
        private readonly ISymbol _x = new TestSymbol("x");
        private readonly ISymbol _y = new TestSymbol("y");

        [Fact]
        public void LinearPolynomial_ToMatrixForm()
        {
            // 3x + 2y + 5 => c=[3,2], d=5, order=[x,y]
            var p = new LinearPolynomial<Flt64>(
                new[]
                {
                    new LinearMonomial<Flt64>(new Flt64(3.0), _x),
                    new LinearMonomial<Flt64>(new Flt64(2.0), _y),
                },
                new Flt64(5.0));

            var order = new List<ISymbol> { _x, _y };
            var result = p.ToMatrixForm(order, Flt64.Zero);
            result.IsOk.Should().BeTrue();
            result.Value.C[0].Should().Be(new Flt64(3.0));
            result.Value.C[1].Should().Be(new Flt64(2.0));
            result.Value.D.Should().Be(new Flt64(5.0));
        }

        [Fact]
        public void LinearMatrixForm_RoundTrip()
        {
            var p = new LinearPolynomial<Flt64>(
                new[]
                {
                    new LinearMonomial<Flt64>(new Flt64(3.0), _x),
                    new LinearMonomial<Flt64>(new Flt64(2.0), _y),
                },
                new Flt64(5.0));

            var order = new List<ISymbol> { _x, _y };
            var form = p.ToMatrixForm(order, Flt64.Zero).Value;
            var reconstructed = MatrixFormOps.LinearPolynomialFromMatrixForm(form, Flt64.Zero);

            // Compare monomial coefficients (order may differ)
            var origDict = p.Monomials.ToDictionary(m => m.Symbol.Name, m => m.Coefficient);
            var reconDict = reconstructed.Monomials.ToDictionary(m => m.Symbol.Name, m => m.Coefficient);
            reconDict["x"].Should().Be(origDict["x"]);
            reconDict["y"].Should().Be(origDict["y"]);
            reconstructed.Constant.Should().Be(p.Constant);
        }

        [Fact]
        public void Flt64LinearMatrixForm_RoundTrip()
        {
            var p = new LinearPolynomial<Flt64>(
                new[]
                {
                    new LinearMonomial<Flt64>(new Flt64(3.0), _x),
                    new LinearMonomial<Flt64>(new Flt64(2.0), _y),
                },
                new Flt64(5.0));

            var order = new List<ISymbol> { _x, _y };
            var result = p.ToFlt64MatrixForm(order);
            result.IsOk.Should().BeTrue();

            var form = result.Value;
            form.C.Should().Equal(3.0, 2.0);
            form.D.Should().Be(new Flt64(5.0));

            var reconstructed = Flt64MatrixFormOps.Flt64LinearPolynomialFromMatrixForm(form);
            reconstructed.Constant.Should().Be(new Flt64(5.0));
        }

        [Fact]
        public void QuadraticPolynomial_ToMatrixForm()
        {
            // x^2 + 2xy + 3y^2 + 4x + 5y + 6
            var p = new QuadraticPolynomial<Flt64>(
                new[]
                {
                    QuadraticMonomial<Flt64>.Quadratic(new Flt64(1.0), _x, _x),
                    QuadraticMonomial<Flt64>.Quadratic(new Flt64(2.0), _x, _y),
                    QuadraticMonomial<Flt64>.Quadratic(new Flt64(3.0), _y, _y),
                    QuadraticMonomial<Flt64>.Linear(new Flt64(4.0), _x),
                    QuadraticMonomial<Flt64>.Linear(new Flt64(5.0), _y),
                },
                new Flt64(6.0));

            var order = new List<ISymbol> { _x, _y };
            var result = p.ToMatrixForm(order, Flt64.Zero, c =>
            {
                var half = c / new Flt64(2.0);
                return (half, half);
            });

            result.IsOk.Should().BeTrue();
            result.Value.D.Should().Be(new Flt64(6.0));
            result.Value.C[0].Should().Be(new Flt64(4.0));
            result.Value.C[1].Should().Be(new Flt64(5.0));
            // Q[0][0] = 1 (x^2), Q[0][1] = Q[1][0] = 1 (2xy split), Q[1][1] = 3 (3y^2)
            result.Value.Q[0][0].Should().Be(new Flt64(1.0));
            result.Value.Q[1][1].Should().Be(new Flt64(3.0));
        }

        private sealed record TestSymbol(string Name, string? DisplayName = null) : ISymbol;
    }
}
