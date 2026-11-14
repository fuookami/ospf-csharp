#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using System;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.ConstraintProgramming;

public class IntegerDomainTest {
    [Fact]
    public void IntervalUsesExactInclusiveBoundsAndDetectsCardinalityOverflow() {
        IntegerDomain.Interval domain = IntegerDomain
            .CreateInterval(new Int64(-2), new Int64(3)).Value;

        domain.Contains(new Int64(-3)).Should().BeFalse();
        domain.Contains(new Int64(-2)).Should().BeTrue();
        domain.Contains(new Int64(3)).Should().BeTrue();
        domain.Contains(new Int64(4)).Should().BeFalse();
        domain.Cardinality.Should().Be(6);
        domain.Enumerate(5).Should().BeNull();
        domain.Enumerate(6).Should().HaveCount(6);

        IntegerDomain full = IntegerDomain
            .CreateInterval(new Int64(long.MinValue), new Int64(long.MaxValue)).Value;
        full.Cardinality.Should().BeNull();
        full.Enumerate(int.MaxValue).Should().BeNull();
    }

    [Fact]
    public void SparseDomainCanonicalizesInputAndRejectsEmptyCollections() {
        IntegerDomain.Values domain = IntegerDomain
            .CreateValues(new[] { new Int64(3), new Int64(-1), new Int64(3) }).Value;

        domain.Items.Should().Equal(new Int64(-1), new Int64(3));
        domain.LowerBound.Should().Be(new Int64(-1));
        domain.UpperBound.Should().Be(new Int64(3));
        domain.Contains(new Int64(3)).Should().BeTrue();
        domain.Contains(new Int64(1)).Should().BeFalse();
        IntegerDomain.CreateValues(Array.Empty<Int64>()).IsOk.Should().BeFalse();
        IntegerDomain.CreateInterval(new Int64(2), new Int64(1)).IsOk.Should().BeFalse();
    }

    [Fact]
    public void BooleanDomainAndSingletonAreImmutableAndDeterministic() {
        IntegerDomain.Boolean.Items.Should().Equal(new Int64(0), new Int64(1));
        IntegerDomain.CreateSingleton(new Int64(7)).Singleton.Should().BeTrue();
        IntegerDomain.Boolean.Contains(new Int64(2)).Should().BeFalse();
    }
}
