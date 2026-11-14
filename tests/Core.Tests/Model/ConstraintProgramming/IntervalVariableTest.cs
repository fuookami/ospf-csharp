#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Model.ConstraintProgramming;

public class IntervalVariableTest {
    [Fact]
    public void IntervalShouldValidateExactEndAndOptionalPresence() {
        IntervalVariable valid = Interval("valid", 0, 3, 3);
        IntervalVariable invalid = Interval("invalid", 0, 3, 4);
        ConstraintProgrammingExpression missing =
            ConstraintProgrammingExpression.CreateVariable(new IntVar("missing")).Value;
        IntervalVariable optional = IntervalVariable.Create(
            id: "optional",
            start: missing,
            size: ConstraintProgrammingExpression.CreateConstant(100),
            end: missing,
            presence: BooleanLiteral.False).Value;

        valid.Evaluate(new Dictionary<VariableId, Int64>()).Value.Should().Be(
            new IntervalValue(
                Start: Int64.Zero,
                Size: new Int64(3),
                End: new Int64(3)));
        invalid.Evaluate(new Dictionary<VariableId, Int64>()).IsFailed.Should().BeTrue();
        optional.Evaluate(new Dictionary<VariableId, Int64>()).Value.Present.Should().BeFalse();
        optional.IsOptional.Should().BeTrue();
    }

    [Fact]
    public void IntervalShouldRejectNegativeDurationAndStartPlusSizeOverflow() {
        IntervalVariable.CreateFixed(
            id: "negative",
            start: ConstraintProgrammingExpression.CreateConstant(0),
            size: new Int64(-1),
            end: ConstraintProgrammingExpression.CreateConstant(0)).IsFailed.Should().BeTrue();
        IntervalVariable overflow = Interval(
            id: "overflow",
            start: long.MaxValue,
            size: 1,
            end: long.MinValue);

        overflow.Evaluate(new Dictionary<VariableId, Int64>()).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void NoOverlapShouldUseHalfOpenIntervalsAndIgnoreAbsentOptionalIntervals() {
        IntervalVariable first = Interval("first", 0, 3, 3);
        IntervalVariable adjacent = Interval("adjacent", 3, 2, 5);
        IntervalVariable overlap = Interval("overlap", 2, 2, 4);
        IntervalVariable disabled = IntervalVariable.Create(
            id: "disabled",
            start: ConstraintProgrammingExpression.CreateConstant(1),
            size: ConstraintProgrammingExpression.CreateConstant(100),
            end: ConstraintProgrammingExpression.CreateConstant(101),
            presence: BooleanLiteral.False).Value;

        var values = new Dictionary<VariableId, Int64>();
        NoOverlap.Create(new[] { first, adjacent }).Value
            .IsSatisfied(values).Value.Should().BeTrue();
        NoOverlap.Create(new[] { first, overlap }).Value
            .IsSatisfied(values).Value.Should().BeFalse();
        NoOverlap.Create(new[] { first, disabled }).Value
            .IsSatisfied(values).Value.Should().BeTrue();
        NoOverlap.Create(Array.Empty<IntervalVariable>()).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void CumulativeShouldCheckPeakLoadWithoutOverflowingDemandSums() {
        IntervalVariable first = Interval("first", 0, 3, 3);
        IntervalVariable second = Interval("second", 1, 2, 3);
        ConstraintProgrammingExpression[] demands = {
            ConstraintProgrammingExpression.CreateConstant(2),
            ConstraintProgrammingExpression.CreateConstant(3),
        };
        var values = new Dictionary<VariableId, Int64>();

        Cumulative.Create(
            intervals: new[] { first, second },
            demands: demands,
            capacity: ConstraintProgrammingExpression.CreateConstant(4)).Value
            .IsSatisfied(values).Value.Should().BeFalse();
        Cumulative.Create(
            intervals: new[] { first, second },
            demands: demands,
            capacity: ConstraintProgrammingExpression.CreateConstant(5)).Value
            .IsSatisfied(values).Value.Should().BeTrue();

        IntervalVariable sameSlot = Interval("same-slot", 0, 1, 1);
        Cumulative.Create(
            intervals: new[] { sameSlot, sameSlot },
            demands: new[] {
                ConstraintProgrammingExpression.CreateConstant(long.MaxValue),
                ConstraintProgrammingExpression.CreateConstant(1),
            },
            capacity: ConstraintProgrammingExpression.CreateConstant(long.MaxValue)).Value
            .IsSatisfied(values).Value.Should().BeFalse();
        Cumulative.Create(
            intervals: new[] { first },
            demands: demands,
            capacity: ConstraintProgrammingExpression.CreateConstant(5)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void IntervalCollectionsShouldBeFrozenAtConstruction() {
        IntervalVariable first = Interval("first", 0, 1, 1);
        IntervalVariable second = Interval("second", 1, 1, 2);
        var intervals = new List<IntervalVariable> { first };
        NoOverlap noOverlap = NoOverlap.Create(intervals).Value;
        var demands = new List<ConstraintProgrammingExpression> {
            ConstraintProgrammingExpression.CreateConstant(1),
        };
        Cumulative cumulative = Cumulative.Create(
            intervals: new[] { first },
            demands: demands,
            capacity: ConstraintProgrammingExpression.CreateConstant(1)).Value;

        intervals.Add(second);
        demands.Clear();

        noOverlap.Intervals.Should().ContainSingle();
        cumulative.Demands.Should().ContainSingle();
    }

    private static IntervalVariable Interval(
        string id,
        long start,
        long size,
        long end) =>
        IntervalVariable.CreateFixed(
            id: id,
            start: ConstraintProgrammingExpression.CreateConstant(start),
            size: new Int64(size),
            end: ConstraintProgrammingExpression.CreateConstant(end)).Value;
}
