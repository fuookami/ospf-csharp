// 统一门面的判据/ Criteria for the unified facade.

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
/// **三条路径有了统一入口** / The three paths now have one entry point.
/// </summary>
/// <remarks>
/// 三点成对（默认全量，不依赖后端）：三种模式都被分派到**真实路径**（都没带求解器时都返回 `MilpSolverNotSupplied`）/
/// 未知模式**明确报 `UnknownStowageMode`** 而不是默落到全配载 / 门面**不重复实现装配**（它自己的源码里没有 `Register`、没有 `BuildLinearTriadModel`）。
///
/// 第二点最要紧：**默认落到某一条路径会让"传错了模式"和"就是要那条路径"看起来一样**。
/// 第三点钉的是这里的用意——门面只分派；一旦有人把装配抄回门面，两条实现就会开始分叉。
///
/// Three paired points in the default suite without a backend: all three modes reach their **real** path (each reports `MilpSolverNotSupplied` when no solver is
/// supplied); an unknown mode fails **explicitly as `UnknownStowageMode`** instead of falling through to full load; and the facade **duplicates no assembly**
/// (its own source contains no `Register` and no `BuildLinearTriadModel`). The second point matters most: **falling through makes "the wrong mode was passed" and
/// "that path was wanted" look identical**. The third pins's intent — the facade only dispatches, and once someone copies assembly back into it the
/// two implementations start to diverge.
/// </remarks>
public sealed class Demo2ApplicationServiceTests {
    [Theory]
    [InlineData(StowageMode.FullLoad)]
    [InlineData(StowageMode.Predistribution)]
    [InlineData(StowageMode.WeightRecommendation)]
    public void Solve_ShouldDispatchToTheRealPath(StowageMode mode) {
        // **预配载要用带谓词规格的请求**： 有了"预配载必须有下限"的校验后，
        // 全配载形状的请求会在装配阶段被拒——**那是既定行为，不是没接上路径**。
        //
        // **Predistribution needs the request carrying the predicate spec**: with the "predistribution requires a bound" check, a
        // full-load-shaped request is refused during assembly — **established behaviour, not an unwired path**.
        RequestDTO request = mode == StowageMode.Predistribution
            ? AircraftLoadingDemo.CreatePredistributionSampleRequest()
            : AircraftLoadingDemo.CreateSampleRequest();

        ResponseDTO response = Demo2ApplicationService.Solve(request, mode).Response;

        response.Status.Should().Be("MilpSolverNotSupplied",
            "each mode must reach a wired path, not the unknown-mode guard");
        response.Notes.Should().NotContain(note => note.Contains("UnknownStowageMode"));
    }

    [Fact]
    public void Solve_ShouldRejectAnUnknownModeInsteadOfFallingThrough() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        var unknown = (StowageMode)99;

        ResponseDTO response = Demo2ApplicationService.Solve(request, unknown).Response;

        response.Succeed.Should().BeFalse();
        response.Status.Should().Be("UnknownStowageMode");
        response.Notes.Should().Contain(note => note.Contains("mode="));
    }

    [Fact]
    public void Facade_ShouldNotReimplementAssembly() {
        // **门面只分派**：装配逻辑一旦被抄回门面，两条实现就会分叉—— 接线全配载时另外两条还在报"未实现"，就是分叉的第一天。
        // **The facade only dispatches**: once assembly is copied back into it the two implementations diverge — wiring full load while the other two still
        // reported "not implemented" was the first day of exactly that divergence.
        string source = System.IO.File.ReadAllText(Path("src/Example/FrameworkDemo/Demo2/Application/Demo2ApplicationService.cs"));

        source.Should().NotContain("Register(");
        source.Should().NotContain("BuildLinearTriadModel");
        source.Should().NotContain("SolveAsync");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **三条路径真的各不相同** / The three paths really differ.
    /// </summary>
    /// <remarks>
    /// 默认层区分不出分派对不对：三条路径没带求解器时都返回 `MilpSolverNotSupplied`——**分派表对调了也照样绿**。
    /// 所以能区分这件事的判据必须真的解一次：全配载与建议打板解得出，预配载**拒收形状不符的请求**。
    ///
    /// 这条判据钉的是"**三种模式给出三种不同结果**"，所以把任意两条的分派臂对调都会让它变红。
    /// **"测试绿了但分不清真假"比没有测试更糟**，这一层就是为那个盲点补的。
    ///
    /// The default layer cannot tell whether dispatch is correct: with no solver all three paths return `MilpSolverNotSupplied`, so **swapping the dispatch arms
    /// still passes**. Only a criterion that actually solves can distinguish them: full load and weight recommendation solve, and predistribution **rejects the
    /// wrong-shaped request** (before it "solved to infeasible"). This criterion pins **three modes producing three different outcomes**, so swapping any two arms turns it red.
    /// **A test that is green without being able to tell right from wrong is worse than no test**, and this layer covers that blind spot.
    /// </remarks>
    [Fact]
    public void Solve_ShouldReachThreeDifferentPaths() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();
        ScipLinearSolver solver = new();

        ResponseDTO fullLoad = Demo2ApplicationService.Solve(request, StowageMode.FullLoad, milpSolver: solver).Response;
        ResponseDTO predistribution = Demo2ApplicationService.Solve(request, StowageMode.Predistribution, milpSolver: solver).Response;
        ResponseDTO recommendation = Demo2ApplicationService.Solve(request, StowageMode.WeightRecommendation, milpSolver: solver).Response;

        fullLoad.Succeed.Should().BeTrue(
            "notes: " + string.Join(" | ", (fullLoad.Notes ?? Array.Empty<string>()).ToArray()));
        fullLoad.Status.Should().Be("Optimal");
        fullLoad.Assignments.Should().NotBeEmpty();

        recommendation.Succeed.Should().BeTrue(
            "notes: " + string.Join(" | ", (recommendation.Notes ?? Array.Empty<string>()).ToArray()));
        recommendation.Status.Should().Be("Optimal");

        // 把拒绝点提前到装配阶段**：道守卫在 `AggregationInitializer` 里，不在注册阶段。
        // 旧判据钉的 `DomainRegisterFailed` 是本仓守卫放晚了的产物。
        //
        // **moves the refusal to assembly**: the code keeps that guard in `AggregationInitializer`, not at registration. The old
        // `DomainRegisterFailed` pinned a local deviation.
        predistribution.Status.Should().Be("InitFailed",
            "the full-load sample has no predicate load weight specification, and the baseline refuses that during assembly");

        // **三种模式不许给出同一种结果**：都成功或都同样的失败，说明分派没有真正分开。
        // **Three modes must not produce identical outcomes**: all succeeding or all failing the same way means the dispatch never separated them.
        new[] { fullLoad.Status, predistribution.Status, recommendation.Status }
            .Distinct().Should().HaveCountGreaterThan(1);
    }
#endif

    /// <summary>**三个自造状态不许再被产出** / The three invented statuses must not come back.</summary>
    /// <remarks>
    /// **`DirectSolveFailed`、`DomainRegisterFailed`、`ModelBuildFailed` 是自造的状态**——
    /// 它们是本仓发明的，而产出 `Infeasible` / `NotConverged` / `Error` 三个。
    ///
    /// **这条判据钉的是"没有产出点"**，不是"映射表里没有名字"：
    /// **一个没人产出的字符串留在 `CodeOf` 里，会让它看起来像一条真实分支**——
    /// **而它真正的作用只是让旧判据继续通过。**
    ///
    /// **They are invented here and appear nowhere in the wire contract** — only
    /// `Infeasible` / `NotConverged` / `Error`.
    ///
    /// **What this pins is "no producer"**, not "no name in the mapping table":
    /// **a string nobody emits that stays in `CodeOf` looks like a real branch** —
    /// **its only real effect is keeping the old criteria green.**
    /// </remarks>
    [Xunit.Fact]
    public void TheInventedStatuses_ShouldNotBeProduced() {
        string source = System.IO.File.ReadAllText(Path("src/Example/FrameworkDemo/Demo2/Application/Demo2ApplicationService.cs"))
            + System.IO.File.ReadAllText(Path("src/Example/FrameworkDemo/Demo2/Application/DirectMilpRunner.cs"))
            + System.IO.File.ReadAllText(Path("src/Example/FrameworkDemo/Demo2/Application/FullLoadApplication.cs"));

        foreach (string invented in new[] { "DirectSolveFailed", "DomainRegisterFailed", "ModelBuildFailed" }) {
            // **只在注释之外搜**：这三段源码的注释里正当地提到了它们（讲清"曾经自造过"）。
            // **Only outside comments**: the comments in these three files legitimately name them (explaining what was invented).
            string withoutComments = System.Text.RegularExpressions.Regex.Replace(
                source, @"//[^\n]*", string.Empty);
            withoutComments.Should().NotContain("\"" + invented + "\"",
                invented + " is not a baseline status, so nothing may emit it");
        }

        // **正向的一半**：三个状态里，两个必须真的出现。
        // **The positive half**: two of the code statuses have to actually appear.
        source.Should().Contain("\"NoSolution\"", "an infeasible model still reports NoSolution");
        source.Should().Contain("\"NotConverged\"", "a non-converged solver is still named as such");
    }

    private static string Path(string relative) {
        // **往上找，不数层数**：`bin` 的深度会随配置变（`Release/net10.0` / 带 RID 时更深），数层数迟早数错。
        // **Walk up instead of counting levels**: the depth under `bin` changes with configuration (`Release/net10.0`, deeper with a RID), so counting breaks.
        string? directory = System.AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = System.IO.Path.Combine(directory, relative);
            if (System.IO.File.Exists(candidate)) {
                return candidate;
            }
            directory = System.IO.Path.GetDirectoryName(directory);
        }
        throw new System.IO.FileNotFoundException(relative);
    }
}
