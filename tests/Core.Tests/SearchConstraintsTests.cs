// 约束处理测试 / Tests for unified constraint handling.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 约束处理 / Unified constraint handling.
/// </summary>
public sealed class SearchConstraintsTests {
    /// <summary>**可行解优先于更优但不可行的解** / A feasible solution outranks a better but infeasible one.</summary>
    [Fact]
    public void Compare_ShouldPutFeasibilityFirst() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(violation: value => value[0].ToDouble());
        IIndividual<Flt64, Flt64> feasible = Individual(objective: 10.0, violation: 0.0);
        IIndividual<Flt64, Flt64> infeasible = Individual(objective: 1.0, violation: 5.0);
        handling.Begin();

        handling.Compare(feasible, infeasible).Should().BeLessThan(0,
            "可行解优先，即使目标更差 / the feasible one wins even with a worse objective");
    }

    /// <summary>**同为可行时比较原目标** / Within the same tier the original objective decides.</summary>
    [Fact]
    public void Compare_ShouldUseObjectiveWithinTier() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(violation: value => value[0].ToDouble());
        IIndividual<Flt64, Flt64> better = Individual(objective: 1.0, violation: 0.0);
        IIndividual<Flt64, Flt64> worse = Individual(objective: 9.0, violation: 0.0);
        handling.Begin();

        handling.Compare(better, worse).Should().BeLessThan(0, "原目标仍然决定层内次序 / the objective still decides within a tier");
    }

    /// <summary>**ε 模式下违约度在容差内归入可行层** / Under epsilon mode a violation within tolerance enters the feasible tier.</summary>
    [Fact]
    public void Compare_ShouldRelaxWithinEpsilon() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(
            violation: value => value[0].ToDouble(), initialEpsilon: 2.0, mode: ConstraintMode.Epsilon);
        IIndividual<Flt64, Flt64> relaxed = Individual(objective: 1.0, violation: 1.0);
        IIndividual<Flt64, Flt64> feasible = Individual(objective: 5.0, violation: 0.0);
        handling.Begin();

        handling.CompareTier(relaxed, feasible).Should().Be(0,
            "ε 之内的违约仍算第一层 / a violation inside epsilon still sits in tier 0");
    }

    /// <summary>**ε 归零后不再放宽** / Epsilon stops relaxing once it reaches zero.</summary>
    [Fact]
    public void Compare_ShouldStopRelaxingAtHorizon() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(
            violation: value => value[0].ToDouble(), initialEpsilon: 2.0,
            mode: ConstraintMode.Epsilon, horizon: 1);
        IIndividual<Flt64, Flt64> relaxed = Individual(objective: 1.0, violation: 1.0);
        IIndividual<Flt64, Flt64> feasible = Individual(objective: 5.0, violation: 0.0);
        handling.Begin();
        handling.Commit();

        handling.Epsilon.Should().Be(0.0);
        handling.CompareTier(relaxed, feasible).Should().BeGreaterThan(0,
            "ε 归零后违约解不再算第一层 / with epsilon at zero the violating solution leaves tier 0");
    }

    /// <summary>**ε 随步数单调收紧** / Epsilon tightens monotonically with steps.</summary>
    [Fact]
    public void Epsilon_ShouldTightenOverSteps() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(
            violation: value => value[0].ToDouble(), initialEpsilon: 10.0, horizon: 4);
        handling.Begin();
        double first = handling.Epsilon;
        handling.Commit();

        handling.Epsilon.Should().BeLessThan(first);
    }

    /// <summary>**尺度只在首次提交时校准** / The scale is calibrated only on the first commit.</summary>
    [Fact]
    public void Commit_ShouldCalibrateScaleOnlyOnce() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(violation: _ => 3.0);
        handling.Begin();
        handling.Observe(new[] { new Flt64(1.0) });
        handling.Commit();
        double afterFirst = handling.Epsilon;

        handling.Begin();
        handling.Observe(new[] { new Flt64(100.0) });
        handling.Commit();

        handling.Epsilon.Should().BeLessThanOrEqualTo(afterFirst,
            "重新校准会让 ε 随种群跳动 / recalibrating would make epsilon jump with the population");
    }

    /// <summary>**分组按层级从优到劣** / Groups are ordered from best tier to worst.</summary>
    [Fact]
    public void Groups_ShouldOrderByTierThenMeasure() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(violation: value => value[0].ToDouble());
        IIndividual<Flt64, Flt64> feasible = Individual(objective: 1.0, violation: 0.0);
        IIndividual<Flt64, Flt64> small = Individual(objective: 2.0, violation: 1.0);
        IIndividual<Flt64, Flt64> large = Individual(objective: 3.0, violation: 9.0);
        handling.Begin();

        IReadOnlyList<IReadOnlyList<IIndividual<Flt64, Flt64>>> groups =
            handling.Groups(new[] { large, feasible, small });

        groups.Should().HaveCount(3);
        groups[0].Should().Contain(feasible);
        groups[1].Should().Contain(small);
        groups[2].Should().Contain(large);
    }

    /// <summary>**缺失违约度回调时装配失败** / Attaching fails without a violation callback.</summary>
    [Fact]
    public void WithConstraints_ShouldRejectMissingCallback() {
        var problem = new SearchProblem<Flt64, Flt64>(
            "p", "fp", new StubModel(value => value[0]), SearchCodecs.Flt64, SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() > rhs.ToDouble(),
            validSolution: _ => true, admissible: _ => true);

        problem.WithConstraints(new ConstraintHandlingConfig(), new SearchRandom(1)).IsOk
            .Should().BeFalse("没有回调就无从计算违约度 / without a callback the violation cannot be computed");
    }

    /// <summary>**非法配置被拒** / An illegal configuration is rejected.</summary>
    [Theory]
    [InlineData(0L, 2.0, 0.45)]
    [InlineData(-1L, 2.0, 0.45)]
    [InlineData(10L, 0.0, 0.45)]
    [InlineData(10L, -1.0, 0.45)]
    [InlineData(10L, 2.0, -0.1)]
    [InlineData(10L, 2.0, 1.1)]
    public void Config_ShouldRejectIllegalValues(long horizon, double exponent, double probability) =>
        new ConstraintHandlingConfig(Horizon: horizon, Exponent: exponent, ObjectiveProbability: probability)
            .Validate().IsOk.Should().BeFalse();

    /// <summary>**负初始容差被拒** / A negative initial tolerance is rejected.</summary>
    [Fact]
    public void Config_ShouldRejectNegativeEpsilon() =>
        new ConstraintHandlingConfig(InitialEpsilon: -1.0).Validate().IsOk.Should().BeFalse();

    /// <summary>默认配置合法 / The default configuration is legal.</summary>
    [Fact]
    public void Config_ShouldAcceptDefaults() => new ConstraintHandlingConfig().Validate().IsOk.Should().BeTrue();

    /// <summary>**违约度回调失败后该步不再重试** / After a callback failure no retry happens in that step.</summary>
    [Fact]
    public void Reading_ShouldRecordCallbackFailureOnlyOnce() {
        int calls = 0;
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(violation: _ => {
            calls++;
            throw new InvalidOperationException("boom");
        });
        handling.Begin();

        handling.Observe(new[] { new Flt64(1.0) });
        handling.Observe(new[] { new Flt64(2.0) });

        handling.Failure.Should().Contain("boom");
        calls.Should().Be(1, "失败后该步不再重试 / no retry within the step after a failure");
    }

    /// <summary>**非有限违约度被拒** / A non-finite violation is rejected.</summary>
    [Fact]
    public void Reading_ShouldRejectNonFiniteViolation() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(violation: _ => double.NaN);
        handling.Begin();

        handling.Observe(new[] { new Flt64(1.0) });

        handling.Failure.Should().Contain("有限且非负");
    }

    /// <summary>**负违约度被拒** / A negative violation is rejected.</summary>
    [Fact]
    public void Reading_ShouldRejectNegativeViolation() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(violation: _ => -1.0);
        handling.Begin();

        handling.Observe(new[] { new Flt64(1.0) });

        handling.Failure.Should().Contain("有限且非负");
    }

    /// <summary>**状态保存与恢复一致** / State saves and restores consistently.</summary>
    [Fact]
    public void State_ShouldRoundTrip() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(violation: _ => 1.0, initialEpsilon: 4.0);
        handling.Begin();
        handling.Commit();
        ConstraintSearchState state = handling.Save();

        SearchConstraintHandling<Flt64, Flt64> restored = Handling(violation: _ => 1.0, initialEpsilon: 4.0);
        restored.Load(state).IsOk.Should().BeTrue();
        restored.Epsilon.Should().Be(handling.Epsilon);
    }

    /// <summary>**不兼容的配置拒绝恢复** / An incompatible configuration refuses restoration.</summary>
    [Fact]
    public void State_ShouldRejectIncompatibleConfig() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(violation: _ => 1.0, initialEpsilon: 4.0);
        ConstraintSearchState state = handling.Save();
        SearchConstraintHandling<Flt64, Flt64> other = Handling(violation: _ => 1.0, initialEpsilon: 9.0);

        other.Load(state).IsOk.Should().BeFalse("配置不同就不能恢复 / a different configuration cannot be restored");
    }

    /// <summary>**随机排序是排列而非比较器** / Stochastic ranking is a permutation, not a comparator.</summary>
    [Fact]
    public void Rank_ShouldPermuteWithoutLosingEntries() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(
            violation: value => value[0].ToDouble(), mode: ConstraintMode.StochasticRanking);
        var entries = Enumerable.Range(0, 8)
            .Select(index => (IIndividual<Flt64, Flt64>)Individual(index, index % 3))
            .ToList();
        handling.Begin();

        IReadOnlyList<IIndividual<Flt64, Flt64>> ranked = handling.Rank(entries);

        ranked.Should().BeEquivalentTo(entries, options => options.WithStrictOrdering().IgnoringCyclicReferences());
        ranked.Should().HaveCount(8, "排序不能丢个体 / ranking must not drop individuals");
    }

    /// <summary>按层级选取幸存者 / Select survivors tier by tier.</summary>
    [Fact]
    public void SelectSurvivors_ShouldFillFromBestTierFirst() {
        SearchConstraintHandling<Flt64, Flt64> handling = Handling(violation: value => value[0].ToDouble());
        ConstrainedSearch<Flt64, Flt64> search = WithConstraints(
            violation: value => value[0].ToDouble(), initialEpsilon: null);
        search.Handling.Begin();
        var entries = new List<IIndividual<Flt64, Flt64>> {
            Individual(1.0, 5.0), Individual(2.0, 0.0), Individual(3.0, 7.0),
        };

        IReadOnlyList<IIndividual<Flt64, Flt64>> survivors = SearchConstraints.SelectSurvivors(
            search, entries, 1, (group, amount) => (IReadOnlyList<IIndividual<Flt64, Flt64>>)group.Take(amount).ToList());

        survivors.Should().HaveCount(1);
        survivors[0].Solution[0].Should().Be(new Flt64(0.0),
            "零违约的那个进入第一层 / the zero-violation one lands in tier 0");
    }

    /// <summary>构造处理器 / Build a handler.</summary>
    private static SearchConstraintHandling<Flt64, Flt64> Handling(
        Func<IReadOnlyList<Flt64>, double> violation,
        double? initialEpsilon = null,
        ConstraintMode mode = ConstraintMode.FeasibilityFirst,
        long horizon = 1000) =>
        new(
            new ConstraintHandlingConfig(mode, initialEpsilon, horizon, 2.0, 0.45),
            Problem(violation),
            new SearchRandom(17));

    /// <summary>构造约束搜索 / Build a constraint-aware search.</summary>
    private static ConstrainedSearch<Flt64, Flt64> WithConstraints(
        Func<IReadOnlyList<Flt64>, double> violation,
        double? initialEpsilon) =>
        ((Ok<ConstrainedSearch<Flt64, Flt64>, ErrorCode, Error<ErrorCode>>)Problem(violation)
            .WithConstraints(new ConstraintHandlingConfig(InitialEpsilon: initialEpsilon), new SearchRandom(17))).Value;

    /// <summary>构造带违约度回调的问题 / Build a problem with a violation callback.</summary>
    private static SearchProblem<Flt64, Flt64> Problem(Func<IReadOnlyList<Flt64>, double> violation) =>
        new("p", "fp", new StubModel(value => value[0]), SearchCodecs.Flt64, SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() > rhs.ToDouble(),
            validSolution: _ => true,
            admissible: _ => true,
            constraintViolation: solution => Results.Ok(new Flt64(violation(solution))));

    /// <summary>构造个体 / Build an individual.</summary>
    private static IIndividual<Flt64, Flt64> Individual(double objective, double violation) =>
        new EvaluatedIndividual<Flt64, Flt64>(new[] { new Flt64(violation) }, new Flt64(objective));
}
