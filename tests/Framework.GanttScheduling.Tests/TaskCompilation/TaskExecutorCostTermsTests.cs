// 任务×执行器成本项的判据 / Criteria for the task-executor cost terms.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **求和域是全部组合；缺一项就整表失败；缺省系数是占位** / The domain is the full product, one gap fails the whole table, and the default coefficient is a placeholder.
/// </summary>
/// <remarks>
/// 求和域包含执行器对该任务不可用的组合。这看着浪费，但那些组合在 已被钉成 `x = 0`，**加进去的项恒为零**。
/// 「不漏」比「少算」重要：按可用集合过滤的话，一旦收紧规则改了就会静默漏掉本该计费的组合。
///
/// The domain includes combinations where the executor is unavailable for the task. That looks wasteful, but those are pinned to `x = 0` by，so **the added term is
/// identically zero**. "Miss nothing" beats "compute less": filtering by the available set would silently drop combinations that should be charged once the rule changes.
/// </remarks>
public class TaskExecutorCostTermsTests {
    /// **全部组合都进表** / Every combination enters the table.
    [Fact]
    public void Build_ShouldCoverTheFullProduct() {
        var tasks = new List<string> { "T1", "T2", "T3" };
        var executors = new List<string> { "E1", "E2" };

        Result<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>> built =
            TaskExecutorCostTerms.Build(tasks, executors, (_, _) => new Flt64(1.0));

        ((Ok<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>>)built)
            .Value.Should().HaveCount(6, "3 tasks × 2 executors");
    }

    /// **执行器不可用的组合也在表里** / Combinations with an unavailable executor are kept too.
    [Fact]
    public void Build_ShouldKeepCombinationsWithDisabledExecutors() {
        var tasks = new List<string> { "T1" };
        var executors = new List<string> { "E1", "E2" };

        // 提取器对 E2 给出零成本——表里仍然要有这一项。
        // The extractor charges nothing for E2 — the term must still be present.
        Result<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>> built =
            TaskExecutorCostTerms.Build(tasks, executors, (_, executor) => new Flt64(executor == "E2" ? 0.0 : 2.0));

        ((Ok<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>>)built)
            .Value.Should().HaveCount(2);
    }

    /// <summary>
    /// **一个缺项就让整表失败** / A single gap fails the whole table.
    /// </summary>
    /// <remarks>
    /// 判据让**第二个**组合才缺项：只检查第一项的实现会返回一张**部分成本表**，
    /// 而部分成本表比没有成本表更危险——缺的那几项在目标里等于零，优化会挑那些组合。
    ///
    /// The criterion makes the **second** combination the missing one: an implementation checking only the first returns a **partial cost table**, which is more dangerous than
    /// none — the missing entries count as zero in the objective, so the optimisation picks those combinations.
    /// </remarks>
    [Fact]
    public void Build_ShouldFailOnTheFirstGap() {
        var tasks = new List<string> { "T1", "T2" };
        var executors = new List<string> { "E1" };

        Result<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>> built =
            TaskExecutorCostTerms.Build(tasks, executors, (task, _) => task == "T2" ? null : new Flt64(3.0));

        built.Should().BeOfType<Failed<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// **系数原样保留** / The coefficients are kept unchanged.
    [Fact]
    public void Build_ShouldKeepTheCoefficients() {
        var tasks = new List<string> { "T1" };
        var executors = new List<string> { "E1" };

        Result<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>> built =
            TaskExecutorCostTerms.Build(tasks, executors, (_, _) => new Flt64(4.5));

        ((Ok<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>>)built)
            .Value[0].Weight.ToDouble().Should().BeApproximately(4.5, 1e-12);
    }

    /// **零成本是合法系数** / A zero cost is a legal coefficient.
    [Fact]
    public void Build_ShouldAcceptZeroCost() {
        var tasks = new List<string> { "T1" };
        var executors = new List<string> { "E1" };

        Result<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>> built =
            TaskExecutorCostTerms.Build(tasks, executors, (_, _) => Flt64.Zero);

        ((Ok<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>>)built)
            .Value[0].Weight.ToDouble().Should().BeApproximately(0.0, 1e-12);
    }

    /// <summary>
    /// **缺省系数 `1` 让目标和等于任数量** / A default coefficient of 1 makes the objective equal the assignment count.
    /// </summary>
    /// <remarks>
    /// 这不是"有意义的成本"，而是基线的占位值。任务都不可取消时目标和是个常数（什么都没优化）；
    /// 一旦有任务可取消，最小化它就变成**尽可能多取消**。判据把这件事算一遍，让后来的人一眼看到危险。
    ///
    /// This is not a meaningful cost but the baseline's placeholder. With nothing cancellable the sum is a constant (optimising nothing); as soon as cancellation is allowed,
    /// minimising it means **cancel as much as possible**. The criterion computes it so the hazard is visible at a glance.
    /// </remarks>
    [Fact]
    public void Build_ShouldExposeThePlaceholderDefault() {
        var tasks = new List<string> { "T1", "T2", "T3" };
        var executors = new List<string> { "E1", "E2" };

        Result<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>> built =
            TaskExecutorCostTerms.Build(tasks, executors, (_, _) => new Flt64(1.0));

        IReadOnlyList<TaskExecutorCostTerms.Term<string, string>> terms =
            ((Ok<IReadOnlyList<TaskExecutorCostTerms.Term<string, string>>, ErrorCode, Error<ErrorCode>>)built).Value;

        // 系数全为 1 时，权重之和恒等于组合数——它不携带任何成本信息。
        // With every coefficient at 1 the weights sum to the combination count — carrying no cost information at all.
        double weightSum = 0.0;
        foreach (TaskExecutorCostTerms.Term<string, string> term in terms) {
            weightSum += term.Weight.ToDouble();
        }
        weightSum.Should().BeApproximately(6.0, 1e-12,
            "the placeholder default makes the table sum a constant, independent of any real cost");
    }
}
