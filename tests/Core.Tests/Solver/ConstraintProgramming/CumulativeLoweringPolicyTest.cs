#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Operation;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Solver.ConstraintProgramming;

public class CumulativeLoweringPolicyTest {
    [Fact]
    public void ZeroDemandOrZeroDurationShouldSkipTimeAndWorkBudgets() {
        using var model = new ConstraintProgrammingModel("cumulative-zero-work");
        IntervalVariable zeroDuration = FixedInterval(
            model,
            id: "zero-duration",
            startValue: 0,
            size: 0);
        IntervalVariable zeroDemand = FixedInterval(
            model,
            id: "zero-demand",
            startValue: 0,
            size: 1);
        model.AddConstraint(Cumulative.Create(
            intervals: new[] { zeroDuration, zeroDemand },
            demands: new[] {
                ConstraintProgrammingExpression.CreateConstant(1),
                ConstraintProgrammingExpression.CreateConstant(0),
            },
            capacity: ConstraintProgrammingExpression.CreateConstant(0)).Value)
            .IsOk.Should().BeTrue();

        using ConstraintProgrammingLoweredLinearModel lowered = Lower(
            model,
            new ConstraintProgrammingLoweringPolicy(
                AllowCumulative: true,
                MaxCumulativeTimeSlots: 1,
                MaxCumulativeWork: 1));

        SlotRows(lowered).Should().BeEmpty();
    }

    [Fact]
    public void SlotRowsShouldRejectOverlapAndTreatAdjacentIntervalsAsHalfOpen() {
        using var overloadModel = new ConstraintProgrammingModel("cumulative-overload");
        IntervalVariable first = FixedInterval(
            overloadModel,
            id: "first",
            startValue: 0,
            size: 1);
        IntervalVariable second = FixedInterval(
            overloadModel,
            id: "second",
            startValue: 0,
            size: 1);
        overloadModel.AddConstraint(Cumulative.Create(
            intervals: new[] { first, second },
            demands: new[] {
                ConstraintProgrammingExpression.CreateConstant(1),
                ConstraintProgrammingExpression.CreateConstant(1),
            },
            capacity: ConstraintProgrammingExpression.CreateConstant(1)).Value)
            .IsOk.Should().BeTrue();

        using ConstraintProgrammingLoweredLinearModel overload =
            Lower(overloadModel, AllowCumulative());
        IReadOnlyList<LinearInequalityConstraint<Flt64>> overloadRows =
            SlotRows(overload);
        overloadRows.Should().ContainSingle();
        overloadRows.Should().OnlyContain(
            row => row.Inequality.IsSatisfied(
                new Dictionary<ISymbol, Flt64>()) == false);

        using var adjacentModel = new ConstraintProgrammingModel("cumulative-half-open");
        IntervalVariable adjacentFirst = FixedInterval(
            adjacentModel,
            id: "first",
            startValue: 0,
            size: 1);
        IntervalVariable adjacentSecond = FixedInterval(
            adjacentModel,
            id: "second",
            startValue: 1,
            size: 1);
        adjacentModel.AddConstraint(Cumulative.Create(
            intervals: new[] { adjacentFirst, adjacentSecond },
            demands: new[] {
                ConstraintProgrammingExpression.CreateConstant(1),
                ConstraintProgrammingExpression.CreateConstant(1),
            },
            capacity: ConstraintProgrammingExpression.CreateConstant(1)).Value)
            .IsOk.Should().BeTrue();

        using ConstraintProgrammingLoweredLinearModel adjacent =
            Lower(adjacentModel, AllowCumulative());
        IReadOnlyList<LinearInequalityConstraint<Flt64>> adjacentRows =
            SlotRows(adjacent);
        adjacentRows.Should().HaveCount(2);
        adjacentRows.Should().OnlyContain(
            row => row.Inequality.IsSatisfied(
                new Dictionary<ISymbol, Flt64>()) == true);
    }

    [Fact]
    public void OptionalIntervalShouldMatchCpSemanticsAcrossSmallDomains() {
        using var model = new ConstraintProgrammingModel("cumulative-small-domain");
        IntervalVariable blocker = FixedInterval(
            model,
            id: "blocker",
            startValue: 1,
            size: 1);
        var start = new IntVar("optional-start");
        var end = new IntVar("optional-end");
        var present = new BinVar("optional-present");
        ConstraintProgrammingExpression startExpression = Register(
            model,
            start,
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(1)).Value);
        ConstraintProgrammingExpression endExpression = Register(
            model,
            end,
            IntegerDomain.CreateInterval(
                lowerBound: new Int64(1),
                upperBound: new Int64(2)).Value);
        model.RegisterVariable(present, IntegerDomain.Boolean).IsOk.Should().BeTrue();
        IntervalVariable optional = IntervalVariable.CreateFixed(
            id: "optional",
            start: startExpression,
            size: new Int64(1),
            end: endExpression,
            presence: new BooleanLiteral.VariableLiteral(present)).Value;
        model.RegisterInterval(optional).IsOk.Should().BeTrue();
        model.AddConstraint(Cumulative.Create(
            intervals: new[] { blocker, optional },
            demands: new[] {
                ConstraintProgrammingExpression.CreateConstant(1),
                ConstraintProgrammingExpression.CreateConstant(1),
            },
            capacity: ConstraintProgrammingExpression.CreateConstant(1)).Value)
            .IsOk.Should().BeTrue();

        using ConstraintProgrammingLoweredLinearModel lowered = Lower(
            model,
            new ConstraintProgrammingLoweringPolicy(
                AllowCumulative: true,
                MaxCumulativeTimeSlots: 2,
                MaxCumulativeWork: 16));

        VariableId startId = new($"{start.Identifier}:{start.Index}");
        VariableId endId = new($"{end.Identifier}:{end.Index}");
        VariableId presentId = new($"{present.Identifier}:{present.Index}");
        for (long startValue = 0; startValue <= 1; ++startValue) {
            for (long endValue = 1; endValue <= 2; ++endValue) {
                for (long presentValue = 0; presentValue <= 1; ++presentValue) {
                    bool expected = presentValue == 0 ||
                        (endValue == startValue + 1 && endValue <= 1);
                    var fixedValues = new Dictionary<ISymbol, Flt64> {
                        [lowered.Variables[startId]] = new Flt64(startValue),
                        [lowered.Variables[endId]] = new Flt64(endValue),
                        [lowered.Variables[presentId]] = new Flt64(presentValue),
                        [lowered.Variables[blocker.Start.Variables.Single()]] =
                            new Flt64(1.0),
                        [lowered.Variables[blocker.End.Variables.Single()]] =
                            new Flt64(2.0),
                    };
                    HasFeasibleAuxiliaryAssignment(lowered, fixedValues)
                        .Should()
                        .Be(
                            expected,
                            $"start={startValue}, end={endValue}, present={presentValue}");
                }
            }
        }
    }

    [Fact]
    public void CumulativeShouldRequireOptInAndRespectTimeAndWorkBudgets() {
        using ConstraintProgrammingModel strictModel =
            SingleCumulativeModel("cumulative-strict-policy");
        new ConstraintProgrammingToLinearModelLowerer()
            .Lower(strictModel)
            .IsFailed.Should().BeTrue();

        using ConstraintProgrammingModel timeBudgetModel =
            SingleCumulativeModel("cumulative-time-budget");
        new ConstraintProgrammingToLinearModelLowerer(
            new ConstraintProgrammingLoweringPolicy(
                AllowCumulative: true,
                MaxCumulativeTimeSlots: 1,
                MaxCumulativeWork: 10))
            .Lower(timeBudgetModel)
            .IsFailed.Should().BeTrue();

        using ConstraintProgrammingModel workBudgetModel =
            SingleCumulativeModel("cumulative-work-budget");
        new ConstraintProgrammingToLinearModelLowerer(
            new ConstraintProgrammingLoweringPolicy(
                AllowCumulative: true,
                MaxCumulativeTimeSlots: 2,
                MaxCumulativeWork: 1))
            .Lower(workBudgetModel)
            .IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ImplicationAndReificationShouldRejectWrappedCumulative() {
        foreach (bool reified in new[] { false, true }) {
            using var model = new ConstraintProgrammingModel(
                reified ? "reified-cumulative" : "implied-cumulative");
            IntervalVariable interval = FixedInterval(
                model,
                id: "wrapped",
                startValue: 0,
                size: 1);
            Cumulative cumulative = Cumulative.Create(
                intervals: new[] { interval },
                demands: new[] {
                    ConstraintProgrammingExpression.CreateConstant(1),
                },
                capacity: ConstraintProgrammingExpression.CreateConstant(1)).Value;
            var gate = new BinVar("gate");
            model.RegisterVariable(gate, IntegerDomain.Boolean).IsOk.Should().BeTrue();
            BooleanLiteral literal = new BooleanLiteral.VariableLiteral(gate);
            ConstraintProgrammingConstraint wrapped = reified
                ? ConstraintProgrammingConstraint.CreateReified(
                    literal: literal,
                    constraint: cumulative,
                    direction: ReificationDirection.Equivalent).Value
                : ConstraintProgrammingConstraint.CreateImplication(
                    enforcement: literal,
                    constraint: cumulative).Value;
            model.AddConstraint(wrapped).IsOk.Should().BeTrue();

            new ConstraintProgrammingToLinearModelLowerer(AllowCumulative())
                .Lower(model)
                .IsFailed.Should().BeTrue();
        }
    }

    private static ConstraintProgrammingLoweringPolicy AllowCumulative() =>
        new(AllowCumulative: true);

    private static ConstraintProgrammingLoweredLinearModel Lower(
        ConstraintProgrammingModel model,
        ConstraintProgrammingLoweringPolicy policy) {
        Result<
            ConstraintProgrammingLoweredLinearModel,
            ErrorCode,
            Error<ErrorCode>> result =
            new ConstraintProgrammingToLinearModelLowerer(policy).Lower(model);
        result.IsOk.Should().BeTrue(
            result is Failed<
                ConstraintProgrammingLoweredLinearModel,
                ErrorCode,
                Error<ErrorCode>> failure
                ? failure.Error.Message
                : "CP lowering failed");
        return result.Value;
    }

    private static ConstraintProgrammingExpression Register(
        ConstraintProgrammingModel model,
        IVariableItem variable,
        IntegerDomain domain) {
        model.RegisterVariable(variable, domain).IsOk.Should().BeTrue();
        return ConstraintProgrammingExpression.CreateVariable(
            variable,
            domain).Value;
    }

    private static IntervalVariable FixedInterval(
        ConstraintProgrammingModel model,
        string id,
        int startValue,
        int size) {
        var start = new IntVar($"{id}-start");
        var end = new IntVar($"{id}-end");
        ConstraintProgrammingExpression startExpression = Register(
            model,
            start,
            IntegerDomain.CreateSingleton(new Int64(startValue)));
        ConstraintProgrammingExpression endExpression = Register(
            model,
            end,
            IntegerDomain.CreateSingleton(new Int64(startValue + size)));
        IntervalVariable interval = IntervalVariable.CreateFixed(
            id: id,
            start: startExpression,
            size: new Int64(size),
            end: endExpression).Value;
        model.RegisterInterval(interval).IsOk.Should().BeTrue();
        return interval;
    }

    private static ConstraintProgrammingModel SingleCumulativeModel(string name) {
        var model = new ConstraintProgrammingModel(name);
        IntervalVariable interval = FixedInterval(
            model,
            id: "interval",
            startValue: 0,
            size: 2);
        model.AddConstraint(Cumulative.Create(
            intervals: new[] { interval },
            demands: new[] {
                ConstraintProgrammingExpression.CreateConstant(1),
            },
            capacity: ConstraintProgrammingExpression.CreateConstant(1)).Value)
            .IsOk.Should().BeTrue();
        return model;
    }

    private static IReadOnlyList<LinearInequalityConstraint<Flt64>> SlotRows(
        ConstraintProgrammingLoweredLinearModel lowered) =>
        lowered.Model.RelationConstraints
            .Where(row => row.Name.Contains("-slot-", StringComparison.Ordinal))
            .ToList();

    private static bool HasFeasibleAuxiliaryAssignment(
        ConstraintProgrammingLoweredLinearModel lowered,
        IReadOnlyDictionary<ISymbol, Flt64> fixedValues) {
        IReadOnlyList<LinearInequalityConstraint<Flt64>> rows =
            lowered.Model.RelationConstraints;
        var symbols = rows
            .SelectMany(row =>
                row.Inequality.Lhs.Monomials.Select(monomial => monomial.Symbol)
                    .Concat(row.Inequality.Rhs.Monomials.Select(
                        monomial => monomial.Symbol)))
            .Distinct()
            .ToList();
        var auxiliaries = symbols
            .Where(symbol => !fixedValues.ContainsKey(symbol))
            .ToList();
        auxiliaries.Any(symbol => symbol is not BinVar).Should().BeFalse();
        long assignmentCount = 1L << auxiliaries.Count;
        for (long mask = 0; mask < assignmentCount; ++mask) {
            var values = new Dictionary<ISymbol, Flt64>(fixedValues);
            for (int index = 0; index < auxiliaries.Count; ++index) {
                values[auxiliaries[index]] =
                    new Flt64((mask & (1L << index)) == 0 ? 0.0 : 1.0);
            }
            if (rows.All(row => row.Inequality.IsSatisfied(values) == true)) {
                return true;
            }
        }
        return false;
    }
}
