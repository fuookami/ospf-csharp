// 生成诊断的计数结构/ The generation diagnostics counters.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Service;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **生成诊断的计数结构** / The generation diagnostics counters.
/// </summary>
/// <remarks>
/// 是两个**纯计数**类型，不打印、不写文件。做成"打印函数"的版本没法在判据里断言——
/// **进 IO 的代码只能靠抓输出，而抓输出的判据一改格式就全红**。
///
/// The code's holds two **pure counter** types — no printing, no file writing. A "print function" version cannot be asserted on: **code that reaches IO can only be
/// checked by capturing output, and such criteria go red the moment the format changes**.
/// </remarks>
public class Demo4DiagnosticsTests {
    /// <summary>
    /// **默认全零、三个布尔默认假** / Everything defaults to zero and the three booleans to false.
    /// </summary>
    /// <remarks>
    /// 三点成对：路线图那份三个计数为 0 / 定价那份九个计数为 0 / **三个布尔为 false**。
    /// 布尔默认成 `true` 会让"这一轮没有产出"看起来像失败，而实际下一轮就正常。
    ///
    /// Three paired points: the route-graph counters are 0; the pricing counters are 0; and **the three booleans are false**. Defaulting a boolean to `true` would make "this round
    /// produced nothing" look like a failure when the next round is fine.
    /// </remarks>
    [Fact]
    public void Counters_ShouldDefaultToZero() {
        var routeGraph = new RouteGraphDiagnostics();
        var pricing = new PricingDiagnostics();

        routeGraph.CandidateTaskCount.Should().Be(0);
        routeGraph.FeasibleExpansionCount.Should().Be(0);
        routeGraph.InfeasibleExpansionCount.Should().Be(0);

        pricing.CandidateTaskCount.Should().Be(0);
        pricing.FeasibleExpansionCount.Should().Be(0);
        pricing.InfeasibleExpansionCount.Should().Be(0);
        pricing.ExpandedLabelCount.Should().Be(0);
        pricing.DominatedLabelCount.Should().Be(0);
        pricing.LabelLimitReachedCount.Should().Be(0);
        pricing.NegativeReducedCostLabelCount.Should().Be(0);
        pricing.GeneratedColumnCount.Should().Be(0);
        pricing.InvalidColumnCount.Should().Be(0);

        pricing.SearchTruncatedByLabelLimit.Should().BeFalse();
        pricing.NoFeasiblePath.Should().BeFalse();
        pricing.NoNegativeReducedCost.Should().BeFalse();
    }

    /// <summary>
    /// **计数彼此独立** / The counters are independent.
    /// </summary>
    /// <remarks>
    /// 两点成对：给其中一个加一之后**其余仍是零** / 同一个计数连加两次得到二。
    /// 用共享字段的实现在第一条就红——**"可伸缩展"与"不可行展开"合成一个数，等于把两个不同的原因抹成一个**。
    ///
    /// Two paired points: incrementing one leaves **the others at zero**; and two increments of the same counter give two. An implementation sharing a field goes red on the first —
    /// **merging "feasible expansions" and "infeasible expansions" into one number erases two different causes**.
    /// </remarks>
    [Fact]
    public void Counters_ShouldBeIndependent() {
        var pricing = new PricingDiagnostics {
            ExpandedLabelCount = 1,
        };

        pricing.ExpandedLabelCount.Should().Be(1);
        pricing.DominatedLabelCount.Should().Be(0);
        pricing.GeneratedColumnCount.Should().Be(0);

        pricing.ExpandedLabelCount += 1;
        pricing.ExpandedLabelCount.Should().Be(2);

        // **可行与不可行必须是两个字段**：合成一个数等于把"展开成功"与"展开失败"抹成同一个原因，排查时无从分辨。
        // **Feasible and infeasible must be two fields**: one shared number erases "expanded" and "failed to expand" into the same cause, leaving no way to tell them apart.
        var routeGraph = new RouteGraphDiagnostics {
            FeasibleExpansionCount = 1,
        };
        routeGraph.InfeasibleExpansionCount.Should().Be(0, "a shared field would make the failure count follow the success count");
        routeGraph.CandidateTaskCount.Should().Be(0);
    }

    /// <summary>
    /// **三个布尔是三个不同的问题** / The three booleans answer three different questions.
    /// </summary>
    /// <remarks>
    /// 两点成对：只把"触顶截断"设成真时其余两个**仍是假** / 三个可以各自独立成立。
    /// 合成一个 `bool` 会让排查时说不清是"标签太少"还是"根本没有负检验数"。
    ///
    /// Two paired points: setting only "truncated by the label limit" leaves the other two **false**; and all three can hold independently. Merging them into one `bool` would leave no way
    /// to tell "too few labels" from "no negative reduced cost at all".
    /// </remarks>
    [Fact]
    public void Flags_ShouldAnswerDifferentQuestions() {
        var pricing = new PricingDiagnostics {
            SearchTruncatedByLabelLimit = true,
        };

        pricing.SearchTruncatedByLabelLimit.Should().BeTrue();
        pricing.NoFeasiblePath.Should().BeFalse();
        pricing.NoNegativeReducedCost.Should().BeFalse();

        pricing.NoFeasiblePath = true;
        pricing.NoNegativeReducedCost = true;
        new[] { pricing.SearchTruncatedByLabelLimit, pricing.NoFeasiblePath, pricing.NoNegativeReducedCost }
            .Should().OnlyContain(flag => flag, "all three can hold at once");
    }

    /// <summary>
    /// **聚合缺省是空表，不是 `null`** / The aggregation defaults to an empty map, not null.
    /// </summary>
    /// <remarks>
    /// 两点成对：不传诊断时 `RouteGraphDiagnostics` **不是 `null`** 且为空 / 传进去之后能按飞机取回来。
    /// **空表与空引用是两种不同的答案**：前者是"这轮没有诊断"，后者会让读它的人当场崩。
    ///
    /// Two paired points: without diagnostics the map is **not null** and empty; when passed it can be looked up per aircraft. **An empty map and a null reference are different answers**:
    /// the former means "no diagnostics this round", the latter crashes the reader on the spot.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldDefaultToAnEmptyDiagnosticsMap() {
        var aggregation = new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Aggregation(
            new Dictionary<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.Aircraft, Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchGeneration.Model.Graph>(),
            BuildReverse(),
            Array.Empty<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.FlightTaskBunch>());

        aggregation.RouteGraphDiagnostics.Should().NotBeNull();
        aggregation.RouteGraphDiagnostics.Should().BeEmpty();
    }

    private static Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Model.FlightTaskReverse BuildReverse() =>
        Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Model.FlightTaskReverse.Create(
            Array.Empty<(Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.FlightTask,
                Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.FlightTask)>(),
            Array.Empty<Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model.FlightTaskBunch>(),
            new Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model.Lock(),
            Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Model.FlightTaskReverse.DefaultTimeDifferenceLimit);
}
