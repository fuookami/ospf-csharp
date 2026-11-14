// 直连 MILP 路径的判据（B790）/ Criteria for the direct MILP path (B790).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **直连 MILP 路径真的接线了** / The direct MILP path is really wired (B790).
/// </summary>
/// <remarks>
/// 三点成对（默认全量，不依赖后端）：`solver_path` 诊断码是 `milp_direct` / 没带求解器时状态是 **`MilpSolverNotSupplied`** /
/// 备注里**不再出现** `NotYetPorted`。
///
/// 第二点最关键：它把"没带后端"与"没移植"区分开。B790 之前这条路径一律返回 `NotYetPorted`——**而模型装配与求解链路
/// 早就都在本仓可用了**，那句状态码会把一个接线工作说成一个移植缺口。
///
/// Three paired points, in the default suite and without any backend: the `solver_path` diagnostic is `milp_direct`; the status without a solver is
/// **`MilpSolverNotSupplied`**; and `NotYetPorted` **no longer appears** in the notes. The second point matters most: it separates "no backend supplied" from
/// "not ported". Before B790 this path always returned `NotYetPorted` — **while both the model assembly and the solve chain already existed in this port**,
/// so that status turned a wiring task into a bogus migration gap.
/// </remarks>
public sealed class Demo2DirectMilpTests {
    [Fact]
    public void Execute_ShouldSayTheSolverIsMissingInsteadOfNotPorted() {
        FullLoadApplication application = new();
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();

        (ResponseDTO Response, RenderDTO? Render) result = application.Execute(request);
        ResponseDTO response = result.Response;
        RenderDTO? render = result.Render;

        render.Should().BeNull();
        response.Succeed.Should().BeFalse();
        response.Status.Should().Be("MilpSolverNotSupplied");
        response.Notes.Should().Contain(note => note.Contains("solver_path=milp_direct"),
            "the solve mode diagnostic must reach the response");
        response.Notes.Should().NotContain(note => note.Contains("NotYetPorted"),
            "the direct path is wired; claiming otherwise misreports the state of the port");
    }

    [Theory]
    [InlineData("predistribution")]
    [InlineData("weight-recommendation")]
    public void OtherPaths_ShouldSayTheSolverIsMissingInsteadOfNotPorted(string path) {
        // **预配载要用带谓词规格的请求**（B940）：B940 补上基线的"预配载必须有下限"校验之后，
        // 用全配载形状的请求跑预配载会在装配阶段就被拒——**那正是基线行为**，不是回归。
        //
        // **Predistribution needs the request that carries the predicate spec** (B940): once B940 added the baseline's "predistribution requires a bound" check,
        // running a full-load-shaped request through it is refused during assembly — **which is the baseline's behaviour**, not a regression.
        RequestDTO request = path == "predistribution"
            ? AircraftLoadingDemo.CreatePredistributionSampleRequest()
            : AircraftLoadingDemo.CreateSampleRequest();
        ResponseDTO response = path switch {
            "predistribution" => new PredistributionApplication().Execute(request).Response,
            _ => new WeightRecommendationApplication().Execute(request).Response,
        };

        // 三条路径**一起接线**（B791）：只接一条会让另外两条继续撒谎，而调用方看不出差别。
        // **All three paths wired together** (B791): wiring one leaves the other two lying, and the caller cannot tell the difference.
        response.Status.Should().Be("MilpSolverNotSupplied");
        response.Notes.Should().NotContain(note => note.Contains("NotYetPorted"));
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **预配载会拒收全配载形状的示例请求** / Predistribution rejects the full-load shaped sample request.
    /// </summary>
    /// <remarks>
    /// **这条判据在本批翻转了**（B815）。它以前钉的是"示例请求在预配载模式下 `ORModelInfeasible`"，并把这归因为"数据形状问题"。
    /// 那个归因**是错的**：真正的原因是 `StowageContext.Init` 没收到模式、默认按全配载建模，而谓词变量又照注册，
    /// 于是模型带着两套互相矛盾的约束。模式传到之后，全配载形状的请求**在注册阶段就被挡下**（缺谓词规格），
    /// 而不是"解出来不可行"——**早一步，也说清了原因**。
    ///
    /// **This criterion flipped in this batch** (B815). It used to pin "the sample request is `ORModelInfeasible` in predistribution mode" and blamed a "data-shape
    /// problem". That attribution **was wrong**: the real cause was `StowageContext.Init` never receiving the mode and defaulting to full load while the predicate
    /// variables were registered anyway, so the model carried two contradictory sets of constraints. With the mode threaded, the full-load shaped request is **stopped at
    /// registration** (missing predicate specs) instead of "solving to infeasible" — one step earlier, and it says why.
    /// </remarks>
    [Fact]
    public void Predistribution_ShouldRejectTheFullLoadSampleForMissingSpecs() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        ScipLinearSolver solver = new();

        ResponseDTO response = new PredistributionApplication().Execute(request, milpSolver: solver).Response;

        response.Succeed.Should().BeFalse("the full-load sample carries no predicate load weight specification");
        // **B940 把拒绝点提前到装配阶段**：去读基线，那道守卫就在 `AggregationInitializer` 里。
        // 旧判据钉的 `DomainRegisterFailed` 是本仓守卫放晚了的产物，不是基线行为。
        //
        // **B940 moves the refusal to assembly**: the baseline keeps that guard in `AggregationInitializer`. The old `DomainRegisterFailed` pinned a local
        // deviation (the guard sat a step late), not the baseline's behaviour.
        response.Status.Should().Be("InitFailed",
            "the missing specification is refused where the baseline refuses it: during assembly");
        response.Notes.Should().NotContain(note => note.Contains("NotYetPorted"));
        response.Notes.Should().NotContain(note => note.Contains("ORModelInfeasible"),
            "an infeasibility here would mean the model is still being built as full load");
    }

    /// <summary>
    /// **预配载自己的示例请求解得出** / The predistribution sample request solves.
    /// </summary>
    /// <remarks>
    /// 与上一条成对：拒收错形状的请求，同时**收下对形状的请求并解出来**。只有前一条的话，"预配载永远失败"也能让它绿。
    ///
    /// Paired with the criterion above: reject the wrong shape **and accept the right shape and solve it**. With the first alone, "predistribution always fails"
    /// would keep it green.
    /// </remarks>
    [Fact]
    public void Predistribution_ShouldSolveItsOwnSample() {
        RequestDTO request = AircraftLoadingDemo.CreatePredistributionSampleRequest();
        ScipLinearSolver solver = new();

        ResponseDTO response = new PredistributionApplication().Execute(request, milpSolver: solver).Response;

        response.Succeed.Should().BeTrue(
            "notes: " + string.Join(" | ", (response.Notes ?? Array.Empty<string>()).ToArray()));
        response.Status.Should().Be("Optimal");
        response.Objective.Should().Be(1.0, "the predicate load weights are minimised, so the optimum is positive");
    }

    [Fact]
    public void WeightRecommendation_ShouldSolveThroughTheDirectMilpPath() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        ScipLinearSolver solver = new();

        ResponseDTO response = new WeightRecommendationApplication().Execute(request, milpSolver: solver).Response;

        response.Succeed.Should().BeTrue(
            "notes: " + string.Join(" | ", (response.Notes ?? Array.Empty<string>()).ToArray()));
        response.Status.Should().Be("Optimal");
        response.Assignments.Should().NotBeEmpty();
    }
#endif

#if SCIP_INTEGRATION
    [Fact]
    public void Execute_ShouldSolveThroughTheDirectMilpPath() {
        FullLoadApplication application = new();
        ScipLinearSolver solver = new();
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();

        ResponseDTO response = application.Execute(request, milpSolver: solver).Response;

        response.Succeed.Should().BeTrue(
            "the direct path assembles the model and the solver returns a plan; notes: "
            + string.Join(" | ", (response.Notes ?? Array.Empty<string>()).ToArray()));
        response.Status.Should().Be("Optimal");
        response.Objective.Should().NotBeNull();
        response.Assignments.Should().NotBeEmpty("a solve that assigns nothing demonstrates nothing");
        response.Notes.Should().Contain(note => note.Contains("direct_model=rows"));
    }
#endif
}
