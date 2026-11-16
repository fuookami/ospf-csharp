using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 渲染输出的判据 / Criteria for the render output.
/// </summary>
public sealed class Demo2RenderTests
{
    [Fact]
    public void Render_ShouldTakeTheUnionOfAllThreeTables() {
        // **只从分配表出发会漏掉"没装货但有重量"的舱位**，而那正是谓词/推荐重量模式下的常态。
        // 这条判据造一个"只在谓词重量表里"的舱位，它必须出现在渲染结果里。
        //
        // **Starting from the assignment table alone would drop positions that hold no cargo yet carry a weight**, which is exactly the normal case under the predicate
        // and recommended weight modes. This builds a position that only exists in the predicate table, and it must show up in the render.
        Solution solution = SampleSolution();

        RenderDTO rendered = new SolutionRenderer().Invoke(solution);

        rendered.Positions.Select(row => row.PositionName).Should().Contain("P2",
            "P2 is only present in the predicate weight table and must still be rendered");
    }

    [Fact]
    public void Render_ShouldOrderByTheLoadingOrderRatherThanTheId() {
        // **第二级键（装载顺序）必须压过第三级键（舱位 id）**。造一个"顺序与 id 相反"的请求：P1 的装载顺序是 1、P2 是 0，
        // 于是正确的渲染顺序是 P2、P1。**按 id 排的实现会给出 P1、P2，这条会红。**
        //
        // **第三级键（id 的字符串字典序）在这份示例里观察不到**：示例的 id 是单位数，字典序与数值序一致，任何实现都排得对。
        // 的 `LoadingOrderOutputExporter` 有同样的盲区。**不写一条假装能测它的判据**——那是"绿但空转"。
        //
        // **The second key (loading order) must beat the third (position id)**. The request is built with the order reversed: P1 loads at order 1 and P2 at order 0,
        // so the correct render order is P2, P1. **An implementation sorting by id would give P1, P2 and turn this red.**
        //
        // **The third key (the id's string order) is not observable in this sample**: the ids are single digits, so lexicographic and numeric order agree and every
        // implementation gets it right. the earlier `LoadingOrderOutputExporter` has the same blind spot. **No criterion pretends to test it** — that would be green but idle.
        Solution solution = SwappedLoadingOrderSolution();

        RenderDTO rendered = new SolutionRenderer().Invoke(solution);

        rendered.Positions.Select(row => row.PositionName).Should().Equal(new[] { "P2", "P1" },
            "loading order decides before the position id does");
    }

    [Fact]
    public void Render_ShouldSkipAssignmentsThatAreNotStowed() {
        // **`Stowed == false` 是模型的候选而不是解里的结果**，渲染出来会凭空多出货物。
        // **`Stowed == false` is a model candidate rather than part of the solution**; rendering it would invent cargo.
        Solution solution = SampleSolution();

        RenderDTO rendered = new SolutionRenderer().Invoke(solution);

        foreach (RenderPositionDTO row in rendered.Positions) {
            row.ItemIds.Should().NotContain("ghost");
        }
    }

    [Fact]
    public void Render_ShouldKeepMissingWeightsNullRatherThanZero() {
        // **`null` 表示"没启用这个模式"，`0` 是一个真实重量**。压成 0 会让渲染侧分不清两者。
        // **`null` means "this mode is not enabled", `0` is a real weight.** Collapsing to zero would leave the rendering side unable to tell them apart.
        Solution solution = SampleSolution();

        RenderDTO rendered = new SolutionRenderer().Invoke(solution);

        RenderPositionDTO first = rendered.Positions.Single(row => row.PositionName == "P1");
        first.PredicateLoadWeight.Should().BeNull("P1 carries no predicate weight");
        first.RecommendedLoadWeight.Should().NotBeNull();
        first.RecommendedLoadWeight!.Value.Should().Be(6.0);
        first.RecommendedLoadWeight.Unit.Should().NotBeNullOrEmpty();
    }

#if SCIP_INTEGRATION
    [Fact]
    public async Task Execute_ShouldReturnARenderOnTheDirectPath() {
        // **端到端**：求解成功且调用方没要渲染时 `Render` 为 `null`；直连路径接了渲染之后，同样一次求解会带回舱位行。
        // 这条判据证明渲染链**有生产消费者**—— 之前 `RenderDTO` 是个空占位、`SolutionAnalyzer` 没有任何调用点。
        //
        // **End to end**: a successful solve returns a `null` render when nobody asked for one, and the same solve brings back position rows once the direct path is
        // wired. This proves the render chain **has a production consumer** — before `RenderDTO` was an empty placeholder and `SolutionAnalyzer` had no call site.
        var application = new FullLoadApplication();
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        (ResponseDTO Response, RenderDTO? Render) executed = application.Execute(
            request,
            milpSolver: new Fuookami.Ospf.Core.Plugin.Scip.ScipLinearSolver());

        ResponseDTO response = executed.Response;
        RenderDTO? render = executed.Render;
        response.Succeed.Should().BeTrue(response.Status);
        render.Should().NotBeNull("the direct path renders the plan it just computed");
        render!.Positions.Should().NotBeEmpty();
        render.Positions.Select(row => row.PositionName).Should().OnlyHaveUniqueItems();
        await Task.CompletedTask;
    }
#endif

    /// <summary>
    /// 构造一个小方案 / Build a small solution.
    /// </summary>
    /// <returns>方案 / The solution.</returns>
    private static Solution SampleSolution() {
        StowageAggregation aggregation = Aggregation();
        StowagePosition first = aggregation.Positions[0];
        StowagePosition second = aggregation.Positions[1];
        Item cargo = aggregation.Items[0];

        List<ItemAssignment> assignments = [
            new(cargo, first, true),
            new(aggregation.Items[1], first, false),
        ];
        return new Solution(
            assignments,
            new Dictionary<StowagePosition, Quantity<Flt64>> { [second] = new Quantity<Flt64>(new Flt64(2.0), AircraftModel.WeightUnit) },
            new Dictionary<StowagePosition, Quantity<Flt64>> { [first] = new Quantity<Flt64>(new Flt64(6.0), AircraftModel.WeightUnit) });
    }

    private static StowageAggregation Aggregation() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        var pipeline = new DomainPipelineContext();
        pipeline.Init(request).IsOk.Should().BeTrue();
        return pipeline.StowageContext.Aggregation!;
    }

    /// <summary>
    /// 装载顺序与舱位 id 相反的小方案 / A small solution whose loading order runs against the position ids.
    /// </summary>
    /// <returns>方案 / The solution.</returns>
    private static Solution SwappedLoadingOrderSolution() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        List<PositionInput> positions = [
            sample.Positions[0] with { LoadingOrder = 1 },
            sample.Positions[1] with { LoadingOrder = 0 },
        ];
        var pipeline = new DomainPipelineContext();
        pipeline.Init(sample with { Positions = positions }).IsOk.Should().BeTrue();
        StowageAggregation aggregation = pipeline.StowageContext.Aggregation!;

        return new Solution([
            new ItemAssignment(aggregation.Items[0], aggregation.Positions[0], true),
            new ItemAssignment(aggregation.Items[0], aggregation.Positions[1], true),
        ]);
    }
}
