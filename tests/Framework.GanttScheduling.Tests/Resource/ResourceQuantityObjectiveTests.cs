// 资源超量 / 不足量目标/ The resource over / less quantity objectives.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Resource;

/// <summary>
/// **资源超量 / 不足量目标** / The resource over / less quantity objectives.
/// </summary>
/// <remarks>
/// 这两个目标以前都是 `Invoke => Ok`——模型里连一个目标项都没有，而"最小化超量"这句话听起来又完全成立。
/// 把超量 / 不足两张表补齐，这里才轮到它们。
///
/// 三件照做：**阈值 0 时目标项是松弛符号本身**（每个槽一个单项式）、**阈值非零时另建带阈值的松弛并取正部**、
/// **权重乘在目标项上而不是松弛内部**。
///
/// Both objectives used to be `Invoke => Ok` — not a single term in the model, while "minimise the over quantity" sounds entirely true. built the over and less tables; is
/// where they pay off. Three things are copied from the code: **at a zero threshold the term is the slack symbol itself** (one monomial per slot), **a positive threshold builds a
/// separate thresholded slack whose positive part becomes the term**, and **the weight multiplies the term** rather than sitting inside the slack.
/// </remarks>
public class ResourceQuantityObjectiveTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class TestResource : ExecutionResource<ResourceCapacity<Flt64>, Flt64> {
        public TestResource(IReadOnlyList<ResourceCapacity<Flt64>> capacities) {
            Capacities = capacities;
        }

        public override string Id => "R1";

        public override string Name => "R1";

        public override IReadOnlyList<ResourceCapacity<Flt64>> Capacities { get; }

        public override Flt64 InitialQuantityValue => new(3.0);

        public override Flt64 UsedBy<E, A>(IAbstractTask<E, A> task, TimeRange time) => Flt64.Zero;
    }

    private static BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64> Usage(
        int slots, LinearMetaModel<Flt64> model) {
        var window = new TimeWindow<Flt64>(
            new TimeRange(Origin, Origin.AddHours(slots * 8)), true, TimeSpan.FromHours(8));
        var capacities = new List<ResourceCapacity<Flt64>>();
        for (int index = 0; index < slots; ++index) {
            capacities.Add(new ResourceCapacity<Flt64>(
                new TimeRange(Origin.AddHours(index * 8), Origin.AddHours((index + 1) * 8)),
                new Flt64(2.0), new Flt64(10.0), new Flt64(1.0), new Flt64(2.0), TimeSpan.FromHours(8)));
        }
        var usage = new BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64>(
            window, new List<TestResource> { new(capacities) }, "execution");
        usage.Register(model).IsOk.Should().BeTrue();
        return usage;
    }

    /// <summary>
    /// **阈值 0 时每个槽一个目标项，权重乘在系数上** / At a zero threshold there is one term per slot and the weight multiplies the coefficient.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 目标项数**等于槽数**——少一项就有槽不被计费。
    /// 2. 类别是 `Minimum`，名字固定，不随阈值分支变。
    /// 3. 每个单项式的系数**正好是权重 2.5**（不是 1，也不是 2.5 的倍数——"是某个数的倍数"是弱断言，的教训）。
    /// 4. 模型里**没有**阈值松弛符号，因为阈值是 0。
    ///
    /// Four paired points: the term count **equals the slot count**, since one missing term leaves a slot uncharged; the category is `Minimum` with a fixed name that does not vary with the
    /// threshold branch; every monomial's coefficient is **exactly the weight 2.5** (not 1, and not "a multiple of 2.5" — a weak assertion, the lesson from ); and the model holds **no**
    /// threshold slack, because the threshold is zero.
    /// </remarks>
    [Fact]
    public void OverObjective_ShouldChargeEverySlotAtTheGivenWeight() {
        using var model = new LinearMetaModel<Flt64>();
        BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64> usage = Usage(2, model);
        var objective = new ResourceOverQuantityMinimization<TestExecutor, TestAssignmentPolicy,
            ExecutionResourceTimeSlot<TestResource, ResourceCapacity<Flt64>, Flt64>,
            TestResource, ResourceCapacity<Flt64>, Flt64>(
            usage, coefficient: _ => new Flt64(2.5));

        objective.Invoke(model).IsOk.Should().BeTrue();

        MetaSubObject<Flt64> added = model.MetaSubObjects.Last();
        added.Category.Should().Be(ObjectCategory.Minimum);
        added.Name.Should().Be("resource over quantity");
        added.Polynomial.Monomials.Should().HaveCount(usage.TimeSlots.Count, "one term per slot");
        added.Polynomial.Monomials.Should().OnlyContain(monomial =>
            System.Math.Abs(monomial.Coefficient.ToDouble() - 2.5) < 1e-9);
        model.Tokens.Symbols.Select(symbol => symbol.Name)
            .Should().NotContain(name => name.Contains("_threshold"), "a zero threshold builds no slack");
    }

    /// <summary>
    /// **阈值非零时目标项是阈值松弛的正部** / A non-zero threshold makes the term the positive part of a thresholded slack.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 这一分支下模型里**出现**阈值松弛符号（名字带 `_threshold`）。
    /// 2. 目标项是它的**正部**（名字以 `_pos` 结尾），不是松弛适配器本身。
    /// 3. 项数仍等于槽数。
    /// 4. 阈值分支**不改变**目标名与类别。
    ///
    /// Four paired points: this branch **introduces** threshold slack symbols (their names carry `_threshold`); the term is their **positive part** (name ending in `_pos`) rather than the slack
    /// adapter itself; the term count still equals the slot count; and the branch **does not change** the objective name or category.
    /// </remarks>
    [Fact]
    public void OverObjective_ShouldUseThePositivePartAboveTheThreshold() {
        using var model = new LinearMetaModel<Flt64>();
        BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64> usage = Usage(2, model);
        var objective = new ResourceOverQuantityMinimization<TestExecutor, TestAssignmentPolicy,
            ExecutionResourceTimeSlot<TestResource, ResourceCapacity<Flt64>, Flt64>,
            TestResource, ResourceCapacity<Flt64>, Flt64>(
            usage, threshold: _ => new Flt64(0.5));

        objective.Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> added = model.MetaSubObjects.Last();
        added.Name.Should().Be("resource over quantity", "the name does not vary with the branch");
        added.Category.Should().Be(ObjectCategory.Minimum);
        added.Polynomial.Monomials.Should().HaveCount(usage.TimeSlots.Count);
        added.Polynomial.Monomials.Should().OnlyContain(monomial => monomial.Symbol.Name.EndsWith("_pos"),
            "the term is the slack's positive part, not the adapter itself");

        // **前缀要说"over"**：两个目标各建各的松弛，共用一个前缀时先加的那个赢、后加的直接 "Repeated" 失败，
        // 而单看"有没有阈值松弛"分不出。
        // **The prefix must say "over"**: the two objectives build separate slacks, so a shared prefix makes the first win and the second fail outright with "Repeated", while
        // "is there a threshold slack" alone cannot tell (this mutation survived once).
        model.Tokens.Symbols.Select(symbol => symbol.Name)
            .Should().Contain(name => name.Contains("over_quantity_minimization_threshold"));
        model.Tokens.Symbols.Select(symbol => symbol.Name)
            .Should().NotContain(name => name.Contains("less_quantity_minimization_threshold"));
    }

    /// <summary>
    /// **不足量目标同样工作，且与超量目标不共用名字** / The less objective works too and does not share the over objective's name.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 不足量目标也真的加了项（项数等于槽数）。
    /// 2. 它的前缀是 `execution_less_quantity_minimization_threshold`，**不是**超量那一份——两个目标必须能同时存在。
    /// 3. 目标是 `Minimum`。
    /// 4. 名字里说的是"less"，不是"over"。
    ///
    /// Four paired points: the less objective also really adds terms (count equals the slot count); its prefix is `execution_less_quantity_minimization_threshold` and **not** the over one's, so
    /// both objectives can coexist; the category is `Minimum`; and its name says "less" rather than "over".
    /// </remarks>
    [Fact]
    public void LessObjective_ShouldHaveItsOwnPrefix() {
        using var model = new LinearMetaModel<Flt64>();
        BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64> usage = Usage(2, model);
        var objective = new ResourceLessQuantityMinimization<TestExecutor, TestAssignmentPolicy,
            ExecutionResourceTimeSlot<TestResource, ResourceCapacity<Flt64>, Flt64>,
            TestResource, ResourceCapacity<Flt64>, Flt64>(
            usage, threshold: _ => new Flt64(0.5));

        objective.Invoke(model).IsOk.Should().BeTrue();

        MetaSubObject<Flt64> added = model.MetaSubObjects.Last();
        added.Category.Should().Be(ObjectCategory.Minimum);
        added.Name.Should().Be("resource less quantity");
        added.Polynomial.Monomials.Should().HaveCount(usage.TimeSlots.Count);
        model.Tokens.Symbols.Select(symbol => symbol.Name)
            .Should().Contain(name => name.Contains("less_quantity_minimization_threshold"));
        model.Tokens.Symbols.Select(symbol => symbol.Name)
            .Should().NotContain(name => name.Contains("over_quantity_minimization_threshold"));
    }

    /// <summary>
    /// **表未注册就失败，非模型也失败** / An unregistered table fails, and so does a non-model.
    /// </summary>
    /// <remarks>
    /// 三点成对：未注册 → `Failed`（当成零计费会让模型照样可解，缺陷只在产出上留痕）/ 非模型 → `Failed` /
    /// `null` → 抛 `ArgumentNullException`（与 `Failed` 不是同一条路径，的同一课）。
    ///
    /// Three paired points: an unregistered table gives `Failed` (charging zero leaves the model solvable with the defect visible only in output); a non-model gives `Failed`; and `null`
    /// throws `ArgumentNullException`, which is not the same path as `Failed` — the same lesson as.
    /// </remarks>
    [Fact]
    public void Objectives_ShouldRejectAnUnregisteredTableAndANonModel() {
        var window = new TimeWindow<Flt64>(
            new TimeRange(Origin, Origin.AddHours(16)), true, TimeSpan.FromHours(8));
        var resource = new TestResource(new List<ResourceCapacity<Flt64>> {
            new(new TimeRange(Origin, Origin.AddHours(8)), new Flt64(2.0), new Flt64(10.0),
                new Flt64(1.0), new Flt64(2.0), TimeSpan.FromHours(8)),
        });
        var usage = new BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64>(
            window, new List<TestResource> { resource }, "execution");
        var objective = new ResourceOverQuantityMinimization<TestExecutor, TestAssignmentPolicy,
            ExecutionResourceTimeSlot<TestResource, ResourceCapacity<Flt64>, Flt64>,
            TestResource, ResourceCapacity<Flt64>, Flt64>(usage);
        using var model = new LinearMetaModel<Flt64>();

        objective.Invoke(model).IsFailed.Should().BeTrue("a missing table must not be charged as zero");
        objective.Invoke(new object()).IsFailed.Should().BeTrue("the objective needs a linear meta model");
        Action nullModel = () => objective.Invoke(null!);
        nullModel.Should().Throw<ArgumentNullException>();
    }
}
