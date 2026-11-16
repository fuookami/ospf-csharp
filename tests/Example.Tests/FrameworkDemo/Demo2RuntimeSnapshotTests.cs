// 运行时快照：真实求解留下的可观测数字 / Runtime snapshot: the observable numbers a real solve leaves behind.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>**真实求解留下的数字** / The numbers a real solve leaves behind.</summary>
/// <remarks>
/// 前面几批比的是"字段在不在"和"属性挂在哪"，**都比不上"解出来是什么"**：一条约束没进模型时，
/// 字段和属性照样齐全，**只有结果会变**。
///
/// 这里钉三样东西，都从**响应本身**读，不读源码：
///
/// | 量 | 抓什么 |
/// | --- | --- |
/// | `direct_model=rows/symbols/objective` | **约束、变量、目标各自的条数**——少一条就变 |
/// | `direct_solution=nonzero` | 非零变量个数 |
/// | `status` + `objective` | 求解状态与目标值 |
///
/// **数字是先探针量出来的，不是猜的**：探针跑完才写下这些值。
///
/// Earlier work compared field presence and property placement; **neither beats "what came out of the solve"** — when a constraint never reaches
/// the model, the fields and properties stay complete and **only the result changes**. Three things are pinned here, all read from the **response itself**, never from
/// source: the row/symbol/objective counts (**one fewer constraint and it moves**), the non-zero variable count, and the status with its objective.
/// **The numbers were measured by a probe first, not guessed.**
/// </remarks>
public class Demo2RuntimeSnapshotTests
{
#if SCIP_INTEGRATION
    /// <summary>**全配载的模型形状与解** / The full-load model shape and solution.</summary>
    /// <remarks>
    /// `rows52` 是**这个请求**下所有约束的条数。它挡住的是"某条约束悄悄没注册"——那种缺口不会报错，
    /// 只会给出**一个不完全的装载方案**，而方案看起来完全正常。
    ///
    /// `rows52` is how many constraints **this request** produces. What it stops is "a constraint quietly failed to register": that gap raises nothing and yields **an
    /// incomplete loading plan that looks entirely normal**.
    /// </remarks>
    [Fact]
    public void TheFullLoadSample_ShouldKeepItsModelShapeAndSolution() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        ResponseDTO response = Demo2ApplicationService.Solve(
            request, StowageMode.FullLoad, milpSolver: new ScipLinearSolver()).Response;

        response.Status.Should().Be("Optimal");
        Notes(response).Should().Contain("direct_model=rows52,symbols76,objective7",
            "a missing constraint or variable moves these counts, and the solve still returns Optimal");
        Notes(response).Should().Contain("direct_solution=nonzero38");
        response.Objective.Should().Be(0.0,
            "a full load needs no adjustment, so the objective is zero; a non-zero value means something is being adjusted");
        response.Assignments.Should().HaveCount(38);
    }

    /// <summary>**预配载的模型形状与解** / The predistribution model shape and solution.</summary>
    /// <remarks>
    /// 和全配载**不同的一组数**：预配载多出谓词变量与其上下界（`rows54`/`symbols82`），目标也多三项（`objective10`）。
    /// **两种模式的形状必须不同**，否则"模式真的分开了"就只是一句话。
    ///
    /// **A different set of numbers**: predistribution adds the predicate variables with their bounds (`rows54`/`symbols82`) and three more objective terms
    /// (`objective10`). **The two modes must differ in shape**, otherwise "the modes really are separate" is just a claim.
    /// </remarks>
    [Fact]
    public void ThePredistributionSample_ShouldKeepItsModelShapeAndSolution() {
        RequestDTO request = AircraftLoadingDemo.CreatePredistributionSampleRequest();
        ResponseDTO response = Demo2ApplicationService.Solve(
            request, StowageMode.Predistribution, milpSolver: new ScipLinearSolver()).Response;

        response.Status.Should().Be("Optimal");
        Notes(response).Should().Contain("direct_model=rows54,symbols82,objective10");
        Notes(response).Should().Contain("direct_solution=nonzero42");
        response.Objective.Should().Be(1.0,
            "predistribution minimises deviation from the predicate weight, so exactly one unit of deviation remains");
        response.Assignments.Should().HaveCount(42);
    }

    /// <summary>**两种模式的模型形状必须不同** / The three modes must not produce the same model.</summary>
    /// <remarks>
    /// **这条是前两条的交叉验证**：如果两个请求都走同一条建模路径，前两条会各自绿，而模式其实没有分开。
    /// 反过来，只有形状真的不同，前两条钉的数字才有意义。
    ///
    /// **This cross-checks the previous two**: if both requests took the same modelling path, those two would pass individually while the modes were never separated.
    /// The numbers they pin only mean something because the shapes really differ.
    /// </remarks>
    [Fact]
    public void TheThreeModes_ShouldBuildThreeDifferentModels() {
        string fullLoad = ModelShape(AircraftLoadingDemo.CreateSampleRequest(), StowageMode.FullLoad);
        string predistribution = ModelShape(AircraftLoadingDemo.CreatePredistributionSampleRequest(), StowageMode.Predistribution);
        string recommendation = ModelShape(AircraftLoadingDemo.CreateSampleRequest(), StowageMode.WeightRecommendation);

        // **三种模式两两不同**：只要有一对相同，"模式真的分开了"就只是一句话。
        // **All three must differ pairwise**: any identical pair and "the modes really are separate" is just a claim.
        new[] { fullLoad, predistribution, recommendation }.Distinct().Should().HaveCount(3,
            "a mode that never reaches the model would leave two of these shapes identical");
    }

    /// <summary>**同样的请求解两次，数字必须一样** / The same request must solve to the same numbers twice.</summary>
    /// <remarks>
    /// **快照的前提是确定性**：如果解一次一个样，上面钉的数字就只是"今天恰好如此"，钉了也没用。
    /// 这条先证明前提成立——**顺序也一起比**，因为方案顺序不同会让"同样的解"看起来不同。
    ///
    /// **A snapshot needs determinism**: if every solve differed, the numbers above would only mean "that is what happened today" and pinning them would prove nothing.
    /// This establishes the premise — **including order**, since a different ordering would make the same solution look different.
    /// </remarks>
    [Fact]
    public void SolvingTwice_ShouldGiveTheSameNumbers() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        ResponseDTO first = Demo2ApplicationService.Solve(
            request, StowageMode.FullLoad, milpSolver: new ScipLinearSolver()).Response;
        ResponseDTO second = Demo2ApplicationService.Solve(
            request, StowageMode.FullLoad, milpSolver: new ScipLinearSolver()).Response;

        second.Status.Should().Be(first.Status);
        second.Objective.Should().Be(first.Objective);
        second.Assignments.Should().Equal(first.Assignments,
            "a nondeterministic solve makes every number above meaningless as a snapshot");
    }

    /// <summary>**重量推荐的模型形状与解** / The weight-recommendation model shape and solution.</summary>
    /// <remarks>
    /// 和前两条**又是另一组数**：`symbols64` 比全配载的 `symbols76` 少（没有装载变量），
    /// `objective2` 比全配载的 `objective7` 少，而目标值是**负数**（`-14`）——这模式下目标求的是"最大化"。
    ///
    /// **目标值的符号不是小事**：`ObjectCategory.Minimum` 里塞一个该最大化的目标，解出来就是一个更差的方案，
    /// **而且它看起来是最优的**。
    ///
    /// **Yet another set of numbers**: `symbols64` is fewer than full load's `symbols76` (no loading variables), `objective2` fewer than `objective7`, and the
    /// objective is **negative** (`-14`) — this mode maximises. **The sign is not a detail**: a maximise-style objective inside `ObjectCategory.Minimum` yields a worse
    /// plan **that still reports itself optimal**.
    /// </remarks>
    [Fact]
    public void TheWeightRecommendationSample_ShouldKeepItsModelShapeAndSolution() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        ResponseDTO response = Demo2ApplicationService.Solve(
            request, StowageMode.WeightRecommendation, milpSolver: new ScipLinearSolver()).Response;

        response.Status.Should().Be("Optimal");
        Notes(response).Should().Contain("direct_model=rows55,symbols64,objective2",
            "this path recommends weights instead of choosing assignments, so its variable set differs from full load");
        Notes(response).Should().Contain("direct_solution=nonzero22");
        response.Objective.Should().Be(-14.0,
            "a non-negative value here would mean the maximising objective lost its sign");
        response.Assignments.Should().HaveCount(22);
    }

    private static IReadOnlyList<string> Notes(ResponseDTO response) => response.Notes ?? Array.Empty<string>();

    private static string ModelShape(RequestDTO request, StowageMode mode) =>
        Notes(Demo2ApplicationService.Solve(request, mode, milpSolver: new ScipLinearSolver()).Response)
            .First(note => note.Contains("direct_model=", StringComparison.Ordinal));
#endif
}
