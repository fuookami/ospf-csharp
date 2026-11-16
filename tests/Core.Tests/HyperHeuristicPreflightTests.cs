// 超启发式配置预检测试 / Tests for the hyper-heuristic preflight.

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 超启发式配置的预检 / The hyper-heuristic preflight.
/// </summary>
public sealed class HyperHeuristicPreflightTests {
    /// <summary>**只有必填字段的配置必须能通过预检** / A configuration with only the required fields must pass.</summary>
    /// <remarks>
    /// 这条测试守的是**默认值表**：`HyperHeuristicConfig` 的六个可选组件都为空时，预检必须用默认值填充。
    /// 默认值写错一个，用户那份完全合法的配置就会报"缺字段"——**而错误指向配置，问题在默认值表里**。
    ///
    /// This test guards the **default table**: with all six optional components empty, the preflight must fill in its defaults. Get one
    /// default wrong and a perfectly legal user configuration reports a missing field — **the error points at the configuration while the problem sits
    /// in the default table**.
    /// </remarks>
    [Fact]
    public void Check_ShouldAcceptTheMinimalConfiguration() =>
        HyperHeuristicPreflight<Flt64, Flt64>.Check(Config(), Registry(coordinates: true), new SearchRandom(1L))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**未注册的问题规格必须失败** / An unregistered problem spec must fail.</summary>
    [Fact]
    public void Check_ShouldRejectUnregisteredProblem() =>
        HyperHeuristicPreflight<Flt64, Flt64>.Check(
                Config(problem: new ComponentSpec("nope", new JsonObject())),
                Registry(coordinates: true), new SearchRandom(1L))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**未知的选择策略必须失败** / An unknown selection policy must fail.</summary>
    [Fact]
    public void Check_ShouldRejectUnknownSelection() =>
        HyperHeuristicPreflight<Flt64, Flt64>.Check(
                Config(selection: new ComponentSpec("my-selector", new JsonObject())),
                Registry(coordinates: true), new SearchRandom(1L))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**标量回报不接受向量目标** / A scalar-gain reward does not accept vector objectives.</summary>
    [Fact]
    public void Check_ShouldRejectScalarRewardForMultiObjective() =>
        HyperHeuristicPreflight<Flt64, Flt64>.Check(
                Config(reward: new ComponentSpec("time-normalized", new JsonObject())),
                Registry(coordinates: true, multiObjective: true), new SearchRandom(1L))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();

    /// <summary>**依赖坐标的配置在无坐标时必须失败** / A coordinate-dependent configuration must fail without coordinates.</summary>
    /// <remarks>
    /// **这条测试抓不住"回报的坐标检查"**：把那条检查删掉，配置照样失败——因为默认接受策略 `not-worse` 也要坐标，
    /// 它先一步报错。这是第四次遇到"守卫相互遮挡"。
    ///
    /// 保留测试，因为它固化的结论仍然成立（依赖坐标的配置不能在没有坐标时通过预检）；但**不能声称它守着回报那一行**。
    /// 那条检查属于纵深防御：一旦某个接受策略不需要坐标，它才成为唯一的防线。
    ///
    /// **This test cannot catch the reward's coordinate check**: delete that check and the configuration still fails, because the default acceptance
    /// policy `not-worse` also needs coordinates and errors first. This is the fourth guard-collision of the session.
    ///
    /// The test stays because the claim it pins is still true (a coordinate-dependent configuration must not pass without coordinates); but **it cannot be
    /// claimed to guard the reward line**. That check is defence in depth: it becomes the only line once some acceptance policy needs no coordinates.
    /// </remarks>
    [Fact]
    public void Check_ShouldRejectCoordinateDependentConfigurationWithoutCoordinates() =>
        HyperHeuristicPreflight<Flt64, Flt64>.Check(
                Config(reward: new ComponentSpec("dominance", new JsonObject())),
                Registry(coordinates: false), new SearchRandom(1L))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    /// <summary>构造一份配置 / Build a configuration.</summary>
    /// <param name="problem">问题规格 / The problem spec.</param>
    /// <param name="selection">选择策略 / The selection policy.</param>
    /// <param name="reward">回报策略 / The reward policy.</param>
    /// <returns>配置 / The configuration.</returns>
    private static HyperHeuristicConfig Config(
        ComponentSpec? problem = null,
        ComponentSpec? selection = null,
        ComponentSpec? reward = null) =>
        new(
            problem ?? new ComponentSpec("quadratic", new JsonObject()),
            new[] { new AlgorithmConfig("a", new ComponentSpec("pso", new JsonObject()), 20) },
            new BudgetConfig(Evaluations: 1000L),
            selection,
            null,
            null,
            reward is null ? null : new LearningConfig(Reward: reward),
            null);

    /// <summary>构造注册表 / Build a registry.</summary>
    /// <param name="coordinates">是否提供坐标 / Whether coordinates are supplied.</param>
    /// <param name="multiObjective">是否为多目标 / Whether multi-objective.</param>
    /// <returns>注册表 / The registry.</returns>
    private static SearchRegistry<Flt64, Flt64> Registry(
        bool coordinates, bool multiObjective = false) {
        var registry = new SearchRegistry<Flt64, Flt64>(IIntoValue<Flt64>.Identity);
        registry.RegisterProblem("quadratic", true,
            (spec, random) => Results.Ok(Problem(coordinates, multiObjective)));
        return registry;
    }

    /// <summary>构造一个问题边界 / Build a problem boundary.</summary>
    /// <param name="coordinates">是否提供坐标 / Whether coordinates are supplied.</param>
    /// <param name="multiObjective">是否为多目标 / Whether multi-objective.</param>
    /// <returns>问题边界 / The problem boundary.</returns>
    private static SearchProblem<Flt64, Flt64> Problem(bool coordinates, bool multiObjective) =>
        new(
            "p", "p",
            new BoundedStubModel<object?>(1, -10.0, 10.0),
            SearchCodecs.Flt64,
            SearchCodecs.Flt64,
            (lhs, rhs) => lhs.ToDouble() < rhs.ToDouble(),
            multiObjective: multiObjective,
            validSolution: _ => true,
            objectiveCoordinates: coordinates ? value => new[] { value } : null,
            admissible: _ => true);
}
