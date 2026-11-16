// 决策归并与观测测试 / Tests for merging and observing a decision.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 一次决策的归并与观测 / Merging and observing one decision.
/// </summary>
public sealed class HyperHeuristicCommitTests {
    /// <summary>**失败的报告必须禁用该算法并记一条失败转移** / A failed report disables the algorithm and records a failed transition.</summary>
    /// <remarks>
    /// 不禁用的话，下一轮还会选中它，**于是每轮都以同样的方式失败**，而诊断只增不减。
    /// Without disabling, the next round picks it again, **so every round fails the same way** while diagnostics only accumulate.
    /// </remarks>
    [Fact]
    public void Merge_ShouldDisableOnFailure() {
        HyperHeuristicMerge<Flt64, Flt64> merged = Merge(Report(
            Array.Empty<IIndividual<Flt64, Flt64>>(), 5L, 1L, SearchTermination.Failed,
            new[] { "boom" }));

        merged.Disable.Should().BeTrue();
        merged.Transition!.Failed.Should().BeTrue();
        merged.Transition.Failed.Should().BeTrue();
        merged.Diagnostics.Should().Contain("boom");
        merged.Reward.ToDouble().Should().Be(0.0);
    }

    /// <summary>**零步且完全没有全局预算时禁用** / Zero steps with no global budget at all disables the algorithm.</summary>
    [Fact]
    public void Merge_ShouldDisableWhenSliceCannotCommitAStep() {
        HyperHeuristicMerge<Flt64, Flt64> merged = Merge(
            Report(Array.Empty<IIndividual<Flt64, Flt64>>(), 0L, 0L,
                SearchTermination.EvaluationLimit),
            budget: new BudgetConfig());

        merged.Disable.Should().BeTrue();
        merged.Diagnostics.Should().ContainSingle(
            "配置错误必须留一条诊断 / a configuration error must leave a diagnostic");
    }

    /// <summary>**零步但真的消耗了额度时，那是正常的切片边界，不禁用** / Zero steps that still consumed allowance is an ordinary boundary and does not disable.</summary>
    /// <remarks>
 /// **这里有一处最初写错的预期**：以为"有全局预算就一律不禁用"。条是
    /// `result.evaluations == 0L && result.steps == 0L`——**两个都是 0 才禁用**，与有没有预算无关。
    /// 语义上讲得通：消耗了额度却没有产出，说明该算法还在工作、只是这一步没攒够一个完整步；
    /// 而"零消耗零产出"才是**空转**，继续派发它只会把决策次数烧光。
    ///
    /// **This is a case where my first expectation was wrong**: I assumed "having a global budget means never disabling". The code condition is
    /// `result.evaluations == 0L && result.steps == 0L` — **both must be zero**, budget or not. That makes sense: spending allowance without output
    /// means the algorithm is still working and merely did not accumulate a whole step, whereas "zero spent, zero produced" is **spinning**, and
    /// dispatching it again only burns decision count.
    /// </remarks>
    [Fact]
    public void Merge_ShouldNotDisableZeroStepThatConsumedAllowance() {
        HyperHeuristicMerge<Flt64, Flt64> merged = Merge(
            Report(Array.Empty<IIndividual<Flt64, Flt64>>(), 3L, 0L,
                SearchTermination.SliceComplete));

        merged.Disable.Should().BeFalse(
            "消耗了额度说明算法还在工作 / having spent allowance means the algorithm is still working");
    }

    /// <summary>**`NoCandidate` 禁用；有产出的正常切片不禁用** / `NoCandidate` disables; a productive slice does not.</summary>
    [Fact]
    public void Merge_ShouldDisableOnlyForNoCandidate() {
        Merge(Report(Array.Empty<IIndividual<Flt64, Flt64>>(), 0L, 0L,
                SearchTermination.NoCandidate))
            .Disable.Should().BeTrue();
        Merge(Report(new[] { Individual(5.0) }, 5L, 1L, SearchTermination.SliceComplete))
            .Disable.Should().BeFalse();
    }

    /// <summary>**零消耗零产出是空转，必须禁用——哪怕终止原因不是 `NoCandidate`** / Zero spent with zero produced is spinning and must disable, even when the reason is not `NoCandidate`.</summary>
    /// <remarks>
    /// **这条测试是补的**：原来"零消耗零产出"那一支只被 `NoCandidate` 用例顺带覆盖，而那条用例是被 `NoCandidate` 分支禁用的，
    /// 于是把 `evaluations == 0 && steps == 0` 整支删掉，测试照样通过——**变异存活暴露了覆盖空洞**。
    ///
    /// **This test was added afterwards**: the "zero spent, zero produced" branch was only covered incidentally by the `NoCandidate` case, which is
    /// disabled by the `NoCandidate` branch instead — so deleting the `evaluations == 0 && steps == 0` branch left the tests green. **The surviving
    /// mutation exposed the coverage hole.**
    /// </remarks>
    [Fact]
    public void Merge_ShouldDisableOnZeroSpentZeroProduced() {
        HyperHeuristicMerge<Flt64, Flt64> merged = Merge(
            Report(Array.Empty<IIndividual<Flt64, Flt64>>(), 0L, 0L,
                SearchTermination.SliceComplete));

        merged.Disable.Should().BeTrue(
            "空转的算法必须禁用 / a spinning algorithm must be disabled");
    }

    /// <summary>**奖励算不出来时保留转移、按零奖励观测，并留一条诊断** / An incomputable reward keeps the transition, observes zero and leaves a diagnostic.</summary>
    /// <remarks>
    /// 丢弃整条转移会让策略少看一步，而"奖励是 NaN"通常是问题定义的问题，**不该连累调度**。
    /// Dropping the whole transition would hide one step from the policy, and a NaN reward is usually a problem-definition issue that **must not take
    /// the schedule down with it**.
    /// </remarks>
    [Fact]
    public void Merge_ShouldSurviveAnIncomputableReward() {
        HyperHeuristicMerge<Flt64, Flt64> merged = Merge(
            Report(new[] { Individual(5.0) }, 5L, 1L, SearchTermination.SliceComplete),
            reward: new NonFiniteReward());

        merged.Reward.ToDouble().Should().Be(0.0);
        merged.Transition.Should().NotBeNull();
        merged.Diagnostics.Should().ContainSingle(
            "必须留下诊断 / a diagnostic must be left behind");
    }

    /// <summary>**改进时奖励进入转移** / An improvement carries its reward into the transition.</summary>
    [Fact]
    public void Merge_ShouldCarryTheRewardIntoTheTransition() {
        HyperHeuristicMerge<Flt64, Flt64> merged = Merge(
            Report(new[] { Individual(5.0) }, 5L, 1L, SearchTermination.SliceComplete),
            reward: new ImprovementRewardPolicy());

        merged.Improved.Should().BeTrue();
        merged.Reward.ToDouble().Should().Be(1.0);
        merged.Transition!.Reward.ToDouble().Should().Be(1.0);
        merged.Transition.Evaluations.Should().Be(5L);
        merged.Transition.Decision.Should().Be(0L);
        merged.Transition.Action.Should().Be("a");
    }

    /// <summary>归并一次决策 / Merge one decision.</summary>
    /// <param name="report">步进报告 / The step report.</param>
    /// <param name="budget">预算配置 / The budget configuration.</param>
    /// <param name="reward">回报策略 / The reward policy.</param>
    /// <returns>归并结果 / The merge result.</returns>
    private static HyperHeuristicMerge<Flt64, Flt64> Merge(
        SearchStepResult<Flt64, Flt64> report,
        BudgetConfig? budget = null,
        IRewardPolicy? reward = null) {
        var memory = new SearchMemory<Flt64, Flt64>(
            100, 20,
            new NotWorseAcceptancePolicy<Flt64, Flt64>(
                new AnyArityMinimizationDominance(), value => new[] { value }),
            false,
            new AnyArityMinimizationDominance(),
            value => new[] { value },
            _ => true);
        var config = new HyperHeuristicConfig(
            new ComponentSpec("quadratic", new System.Text.Json.Nodes.JsonObject()),
            new[] { new AlgorithmConfig("a", new ComponentSpec("pso", new System.Text.Json.Nodes.JsonObject()), 20) },
            budget ?? new BudgetConfig(Evaluations: 100L));
        return HyperHeuristicCommit<Flt64, Flt64>.Merge(
            new HyperHeuristicDecision<Flt64, Flt64>(
                "ep", 0L, "a", new SelectionContext(), new[] { "a" },
                memory.Working, report, 5L, 10L, "progress:1", 0L),
            memory, reward ?? new ImprovementRewardPolicy(), config, value => new[] { value });
    }

    /// <summary>构造步进报告 / Build a step report.</summary>
    /// <param name="candidates">候选 / The candidates.</param>
    /// <param name="evaluations">评估 / The evaluations.</param>
    /// <param name="steps">步数 / The steps.</param>
    /// <param name="termination">终止原因 / The termination reason.</param>
    /// <param name="diagnostics">诊断 / The diagnostics.</param>
    /// <returns>步进报告 / The step report.</returns>
    private static SearchStepResult<Flt64, Flt64> Report(
        IReadOnlyList<IIndividual<Flt64, Flt64>> candidates, long evaluations, long steps,
        SearchTermination termination, IReadOnlyList<string>? diagnostics = null) =>
        new(candidates, evaluations, steps, termination, diagnostics);

    /// <summary>构造一个个体 / Build one individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<Flt64, Flt64> Individual(double objective) =>
        new SolutionWithFitness<Flt64, Flt64>(
            new[] { new Flt64(objective) }, new Flt64(objective), objective);

    /// <summary>永远给出非有限奖励 / Always yields a non-finite reward.</summary>
    private sealed class NonFiniteReward : IRewardPolicy {
        /// <inheritdoc/>
        public Flt64 Reward(RewardContext context) => new Flt64(double.NaN);
    }
}
