using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B816：建议打板的管线不是"全配载的七域" / The weight-recommendation pipeline is not "full load's seven domains".
/// </summary>
/// <remarks>
/// 基线 `WeightRecommendationPipelineContext` 注册的是：配载、重心、适航、**时效**、**推荐重量均衡**、载荷最大化——
/// **没有**软安全、重心优化、装卸效率。本仓以前只注册前三个加载荷最大化，`Init` 里还留着
/// `// TODO: RecommendedWeightEqualizationContext`，于是"建议打板"其实只是"最大化载荷"。
///
/// The baseline's `WeightRecommendationPipelineContext` registers stowage, mac, airworthiness, **express effectiveness**, **recommended-weight equalization** and
/// payload maximization — **no** soft security, mac optimization or loading effectiveness. The port used to register the first three plus payload maximization and left
/// `// TODO: RecommendedWeightEqualizationContext` in `Init`, so "weight recommendation" was really just "maximise payload".
/// </remarks>
public sealed class Demo2WeightRecommendationPipelineTests
{
#if SCIP_INTEGRATION
    /// <summary>
    /// **均衡域必须真的进模型** / The equalization domain must really reach the model.
    /// </summary>
    /// <remarks>
    /// 锚点用 `direct_model=rows55`：比预配载的 54、全配载的 47 都不同。**三种模式三种形状**，
    /// 所以"分派对了"这件事在模型层也分得开，不必只靠解的数值。少注册一个域，行数就掉，判据红。
    ///
    /// The anchor is `direct_model=rows55`, different from predistribution's 54 and full load's 47. **Three modes, three shapes**, so "the dispatch is right" is also
    /// distinguishable at the model layer, not only through solution values. Drop a domain and the row count falls and this turns red.
    /// </remarks>
    [Fact]
    public void WeightRecommendation_ShouldRegisterTheEqualizationDomain() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();

        (ResponseDTO Response, RenderDTO? Render) executed =
            new WeightRecommendationApplication().Execute(request, milpSolver: new ScipLinearSolver());

        executed.Response.Notes.Should().Contain(note => note.Contains("direct_model=rows55"),
            "six domains including equalization; without it the model is 4 domains and a different shape");
        executed.Response.Succeed.Should().BeTrue(
            "notes: " + (executed.Response.Notes is null ? "<no notes>" : string.Join(" | ", System.Linq.Enumerable.ToArray(executed.Response.Notes))));
        executed.Response.Status.Should().Be("Optimal");
        executed.Response.Objective.Should().Be(-14.0,
            "the optimum moved from 0 to -14 when the equalization domain joined the model");
    }

#endif

    /// <summary>
    /// **三种模式的模型形状互不相同** / The three modes have three distinct model shapes.
    /// </summary>
    /// <remarks>
    /// 三条路各自各跑一次，把 `direct_model=...` 那条备注取出来去重。**只断言"都不为空"是不够的**——三条路都返回同一条备注也能过。
    ///
    /// **这条判据在默认层也跑**（B822）：形状备注在求解之前就写好（B821 实测），所以不开启 SCIP 集成时求解失败、备注照样在。
    /// 需要求解的只剩"目标值 / 非零个数"那一维。
    ///
    /// Run each path once and de-duplicate the `direct_model=...` note. **Asserting "none is empty" is not enough** — three paths returning the same note would pass.
    ///
    /// **This criterion also runs in the default layer** (B822): the shape note is written before solving (measured by B821), so without SCIP integration the solve fails and
    /// the note is still there. Only the "objective value / non-zero count" dimension genuinely needs a solve.
    /// </remarks>
    [Fact]
    public void TheThreePipelines_ShouldHaveDistinctShapes() {
        ScipLinearSolver solver = new();
        var shapes = new List<string>();
        foreach (string shape in new[] {
            ShapeOf(new FullLoadApplication().Execute(AircraftLoadingDemo.CreateSampleRequest(), milpSolver: solver).Response),
            ShapeOf(new PredistributionApplication().Execute(AircraftLoadingDemo.CreatePredistributionSampleRequest(), milpSolver: solver).Response),
            ShapeOf(new WeightRecommendationApplication().Execute(AircraftLoadingDemo.CreateSampleRequest(), milpSolver: solver).Response),
        }) {
            shapes.Add(shape);
        }

        shapes.Should().OnlyContain(shape => shape.Length > 0, "every path must report its model shape");
        System.Linq.Enumerable.ToList(System.Linq.Enumerable.Distinct(shapes)).Should().HaveCount(3,
            "the three pipelines are composed differently; sharing one shape means one of them is running another's domains");

        // **每一条都钉成具体的形状，而不是"三条互不相同"**（B819）：三条各不相同但**全都错**同样能过上一句。
        // 三个数字分别对应管线指纹的最后一行 / 预配载八域 / 建议打板六域，所以**应用路径与管线路径任一走偏都会红**。
        //
        // **Pin each one as a concrete shape rather than "all three differ"** (B819): three different shapes that are **all wrong** would pass the assertion above.
        // The three numbers correspond to the fingerprint's last row / predistribution's eight domains / weight recommendation's six domains, so **either path drifting
        // turns this red**.
        shapes[0].Should().Contain("direct_model=rows52,symbols76,objective7",
            "full load is seven domains; five of them is rows47, which is what the application path used to build");
        shapes[1].Should().Contain("direct_model=rows54,symbols82,objective10",
            "predistribution is eight domains including redundancy");
        shapes[2].Should().Contain("direct_model=rows55,symbols64,objective2",
            "weight recommendation is six domains, not full load's seven");
    }


    /// <summary>
    /// **应用路径与管线路径必须建出同一个模型** / The application path and the pipeline path must build the same model.
    /// </summary>
    /// <remarks>
    /// 上一条判据把三个形状**写死**在测试里。写死有两个问题：两边同时走偏会一起绿；而 B814/B815/B816/B819 这四次缺陷**全都出在两边不一致**。
    ///
    /// 所以这一条**不写死任何数字**：同一份请求、同一模式，分别走
    /// 1. `Demo2ApplicationService`（三个 Application）；
    /// 2. `DomainPipelineContext` + `BuildLinearTriadModelAsync`；
    /// 然后把两侧的"行/符号/目标"放在一起比。**两侧都由模型自己算，测试里没有常量可以漂移**。
    ///
    /// The criterion above **hard-codes** the three shapes. Two problems: both sides drifting together stays green, and the four defects B814/B815/B816/B819 **were all
    /// about the two sides disagreeing**. This one **hard-codes nothing**: for one request and one mode it runs (1) `Demo2ApplicationService` (the three applications) and
    /// (2) `DomainPipelineContext` plus `BuildLinearTriadModelAsync`, then compares the row/symbol/objective counts **computed by the models themselves** — no constant here
    /// can drift.
    /// </remarks>
    [Theory]
    [InlineData(StowageMode.FullLoad)]
    [InlineData(StowageMode.Predistribution)]
    [InlineData(StowageMode.WeightRecommendation)]
    public void TheApplicationPath_ShouldMatchThePipelinePath(StowageMode mode) {
        RequestDTO request = AircraftLoadingDemo.CreatePredistributionSampleRequest();

        // **这条判据搬到了默认层**（B821）：`direct_model=...` 那一行备注是在**求解之前**写进 notes 的，
        // 所以形状根本不需要求解成功——不开启 SCIP 集成时求解会失败，但备注已经在了。**默认全量测试因此也覆盖它。**
        //
        // **This criterion moved to the default layer** (B821): the `direct_model=...` note is written **before** solving, so the shape does not need a successful solve —
        // without SCIP integration the solve fails but the note is already there. **The default full-suite run therefore covers it too.**
        (ResponseDTO Response, RenderDTO? Render) executed =
            Demo2ApplicationService.Solve(request, mode, milpSolver: new ScipLinearSolver());
        string applicationShape = ShapeOf(executed.Response);

        var pipeline = new DomainPipelineContext();
        pipeline.Init(request, mode).IsOk.Should().BeTrue();
        using LinearMetaModel<Flt64> model = new();
        pipeline.Register(mode, request.ResolvedParameter, model).IsOk.Should().BeTrue();
        string pipelineShape =
            "direct_model=rows" + model.Constraints.Count
            + ",symbols" + model.Tokens.Symbols.Count
            + ",objective" + model.MetaSubObjects.Count;

        applicationShape.Should().Contain(pipelineShape,
            "the application path and the pipeline path are two hands writing the same thing; if they disagree, one of them is missing domains (B814/B816/B819) "
            + "or is not receiving the mode (B815)");
    }


    private static string ShapeOf(ResponseDTO response) {
        foreach (string note in response.Notes ?? []) {
            if (note.Contains("direct_model=")) {
                return note;
            }
        }
        return string.Empty;
    }
}
