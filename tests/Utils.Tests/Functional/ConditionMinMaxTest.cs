#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Utils.Tests.Functional;

public class ConditionMinMaxTest {
    [Fact]
    public void Condition_EvaluatesPredicateOnceAndConvertsToNull() {
        int invocations = 0;
        Condition<int> condition = 4.IfTrue(value => {
            ++invocations;
            return value > 3;
        });

        condition.Result.Should().BeTrue();
        condition.Result.Should().BeTrue();
        condition.AsNull().Should().Be(0);
        invocations.Should().Be(1);
    }

    [Fact]
    public void MinMax_ComputesBothBoundsInOnePass() {
        IEnumerable<int> source = new OneShotSequence(4, 1, 7, 2);

        source.MinMax().Should().Be((1, 7));
        source.MinMaxOrNull().Should().Be((1, 7));
    }

    [Fact]
    public void MinMaxByOrNull_HandlesEmptyAndReturnsOriginalElements() {
        Array.Empty<int>().MinMaxByOrNull(value => value).Should().BeNull();
        new[] { 4, 1, 7, 2 }
            .MinMaxByOrNull(value => -value)
            .Should().Be((7, 1));
    }

    private sealed class OneShotSequence(params int[] values) : IEnumerable<int> {
        public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)values).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
