using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Variable;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Benders 路径**不许**把主问题的解当成配载分配 / The Benders path must not read the master solution as a loading plan.
/// </summary>
/// <remarks>
/// `BendersSolver` 交回来的 `Solution` 是**主问题模型 token 顺序**的值（theta 加二元变量），而 `FullLoadApplication.BuildAssignments`
/// 按"货 × 舱位"读它。长度碰巧相等时它会"成功"地读出**错位**的分配；不相等时读出空分配。
///
/// 这里把最后那句 `BuildAssignments(request, result.Solution)` 去掉：目标值与状态照报，**分配留空并写明原因**
/// （`benders_assignments=not_decoded:master_solution_length=N`）。等到 demo2 换成框架的 `DiscreteLoadingApplicationService`
/// ，分配才由框架侧按变量身份合并解码。
///
/// `BendersSolver` hands back the **master model's token-ordered** values (theta plus binaries), while `FullLoadApplication.BuildAssignments` reads them as
/// "cargo x position". On a length coincidence it "succeeds" at reading a **misaligned** plan; otherwise it reads an empty one. 
/// drops the final `BuildAssignments(request, result.Solution)`: the objective and status are still reported, **assignments stay empty with a stated reason**
/// (`benders_assignments=not_decoded:master_solution_length=N`). Only when demo2 moves to the framework `DiscreteLoadingApplicationService` ( established that
/// as the baseline path) does the framework merge and decode assignments by variable identity.
/// </remarks>
public sealed class Demo2BendersAssignmentFabricationTests
{
    /// <summary>
    /// **收敛的 Benders 也不交分配** / A converged Benders run still hands back no assignments.
    /// </summary>
    /// <remarks>
    /// 用一条**收敛**的脚本（两次迭代、目标 100 → 100）——
    /// 判据钉的是"就算收敛也不编造"，而这正是最容易被写成 `Optimal + 分配` 的那条分支。
    ///
    /// A **converged** script (two iterations, objective 100 → 100) drives it: the criterion pins "no fabrication even when converged", which is exactly the branch
    /// most easily written as `Optimal plus assignments`.
    /// </remarks>
    [Fact]
    public void AConvergedBendersRun_ShouldNotInventAssignments() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PreferBenders: true, BendersFallbackToMilp: false),
        };

        // **这条判据守的是旧路径的"不解码"行为**（ 分派切到单模型链之后，必须**直接调用**旧路径才量得到）：
        // `SolveWithBenders` 在 改成 `internal`，正是为了这一刻。
        //
        // **This criterion guards the old route's "no decoding" behaviour** (after switched dispatch it must call the old route **directly**); `SolveWithBenders` was made
        // `internal` exactly for this.
        var application = new FullLoadApplication();
        application.Init(request).IsOk.Should().BeTrue();
        List<string> notes = [];
        SolveMode mode = BendersStrategy.ResolveSolveMode(request, notes);
        ResponseDTO executed = application.SolveWithBenders(
            request,
            ((SolveMode.Benders)mode).Config,
            new ScriptedBendersSolver([(100.0, 100.0), (100.0, 100.0)], solutionLength: 3),
            milpSolver: null,
            notes);

        executed.Status.Should().Be("Optimal",
            "the scripted run converges, so the earlier guards are not what stops it");
        executed.Assignments.Should().BeNull(
            "the master's token-ordered vector is not a cargo-by-position layout");
        notes.Should().Contain(note => note.Contains("benders_assignments=not_decoded"),
            "leaving assignments empty silently would be indistinguishable from 'nothing loaded'");
        notes.Should().Contain(note => note.Contains("master_solution_length=16"),
            "the note must say how long the vector it refused to decode was; B883 made the script follow the master token count (16)");
    }

    /// <summary>
    /// **目标值照报** / The objective is still reported.
    /// </summary>
    /// <remarks>
    /// 与上一条成对：**不能靠"整条路径都返回失败"来通过**。目标值必须真的来自求解结果。
    ///
    /// Paired with the criterion above: it must not pass by **failing the whole path**. The objective has to come from the solve.
    /// </remarks>
    [Fact]
    public void AConvergedBendersRun_ShouldStillReportItsObjective() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PreferBenders: true, BendersFallbackToMilp: false),
        };

        // 同上：**直接调旧路径**/ same as above: call the old route directly.
        var application = new FullLoadApplication();
        application.Init(request).IsOk.Should().BeTrue();
        List<string> notes = [];
        SolveMode mode = BendersStrategy.ResolveSolveMode(request, notes);
        ResponseDTO executed = application.SolveWithBenders(
            request,
            ((SolveMode.Benders)mode).Config,
            new ScriptedBendersSolver([(7.5, 7.5), (7.5, 7.5)], solutionLength: 2),
            milpSolver: null,
            notes);

        executed.Objective.Should().BeApproximately(7.5, 1e-9,
            "the objective must come from the solve, not from a default");
        notes.Should().Contain(note => note.Contains("benders_status=converged"));
    }

    /// <summary>
    /// **凭空的解码器不许回来** / The fabricating decoder must not come back.
    /// </summary>
    /// <remarks>
    /// 删掉了 `BuildAssignments`：它**零调用点**（只有注释在提它），而且它读的布局不是任何模型产出的——
    /// `c * numPositions + p` 假设解向量前若干项是"货 × 舱位"的 0/1 决策，而主模型里只有一个 theta。
    /// 留着它就是留一把**随时能被别人捡起来的假分配工具**。
    ///
    /// 判据扫**代码行**、注释不算（这条约定本轮踩过三次）。
    ///
    /// deleted `BuildAssignments`: it had **zero call sites** (only comments named it) and read a layout no model produces — `c * numPositions + p` assumes
    /// the leading entries are 0/1 "cargo × position" decisions while the master holds a single theta. Keeping it meant keeping **a fabrication tool anyone could pick
    /// up again**. The criterion scans **code lines only**; comments do not count (a convention this round has tripped over three times).
    /// </remarks>
    [Fact]
    public void TheFabricatingDecoder_ShouldNotComeBack() {
        var directory = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = global::System.IO.Path.Combine(
                directory.FullName, "src", "Example", "FrameworkDemo", "Demo2", "Application", "FullLoadApplication.cs");
            if (global::System.IO.File.Exists(candidate)) {
                var code = new global::System.Text.StringBuilder();
                foreach (string line in global::System.IO.File.ReadAllLines(candidate)) {
                    if (line.TrimStart().StartsWith("//", global::System.StringComparison.Ordinal)) {
                        continue;
                    }
                    code.AppendLine(line);
                }
                string content = code.ToString();
                content.Should().NotContain("numPositions + p",
                    "that formula is the cargo-by-position assumption no model produces");
                content.Should().NotContain("BuildAssignments",
                    "the decoder was deleted because it fabricated assignments");
                return;
            }
            directory = directory.Parent;
        }
        throw new global::System.IO.FileNotFoundException("could not locate FullLoadApplication.cs");
    }
}
