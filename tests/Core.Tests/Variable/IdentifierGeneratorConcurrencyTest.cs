#nullable enable

using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Variable;

public class IdentifierGeneratorConcurrencyTest {
    [Fact]
    public void IndependentVariablesAndSymbols_ShouldHaveDistinctIdentifiersWhenCreatedConcurrently() {
        var identifiers = new UInt64[40_000];
        Parallel.For(
            fromInclusive: 0,
            toExclusive: identifiers.Length,
            body: index => identifiers[index] = index % 2 == 0
                ? new RealVar().Identifier
                : LinearExpressionSymbol.FromConstant(Flt64.Zero).Identifier
        );

        identifiers.Distinct().Should().HaveCount(identifiers.Length);
    }
}
