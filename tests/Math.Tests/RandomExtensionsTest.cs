#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math;
using System;
using System.Linq;
using Xunit;
using N = Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Tests;

public class RandomExtensionsTest {
    [Fact]
    public void UInt64GenerationUsesTheFullDomain() {
        var random = new Random(42);

        ulong[] values = Enumerable.Range(0, 256)
            .Select(_ => random.NextUInt64().ToULong())
            .ToArray();

        values.Should().Contain(value => value > long.MaxValue);
    }

    [Fact]
    public void UInt64RangesRemainUnsigned() {
        var random = new Random(7);
        ulong lower = (ulong)long.MaxValue + 1UL;
        ulong upper = ulong.MaxValue;

        for (int i = 0; i < 128; i++) {
            ulong value = random.NextUInt64(lower, upper).ToULong();
            value.Should().BeGreaterThanOrEqualTo(lower).And.BeLessThan(upper);
        }

        var fullRange = new IntegerRange<N.UInt64>(
            new N.UInt64(0),
            new N.UInt64(ulong.MaxValue),
            new N.UInt64(1));
        Enumerable.Range(0, 128)
            .Select(_ => random.NextUInt64(fullRange).ToULong())
            .Should()
            .OnlyHaveUniqueItems();
    }

    [Fact]
    public void SignedRangeSupportsInclusiveMaximum() {
        var random = new Random(11);
        var range = new IntegerRange<N.Int64>(
            new N.Int64(long.MinValue),
            new N.Int64(long.MaxValue),
            new N.Int64(1));

        long[] values = Enumerable.Range(0, 128)
            .Select(_ => random.NextInt64(range).ToLong())
            .ToArray();

        values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void FloatingPointRangesUseNumericValues() {
        var random = new Random(19);
        var lower = new N.Flt64(1.25);
        var upper = new N.Flt64(9.75);

        for (int i = 0; i < 128; i++) {
            double value = random.NextFlt64(lower, upper).ToDouble();
            value.Should().BeGreaterThanOrEqualTo(1.25).And.BeLessThan(9.75);
        }
    }

    [Fact]
    public void ZeroUInt64BoundIsRejected() {
        var random = new Random(1);
        Action act = () => random.NextUInt64(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
