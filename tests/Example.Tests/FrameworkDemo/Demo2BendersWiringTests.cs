using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Core.Plugin.Scip;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B831：另外两条路径没有 Benders 接线，必须说出来 / The other two paths have no Benders wiring and must say so.
/// </summary>
/// <remarks>
/// 这两条路径此前是 **先 `ResolveSolveMode` 算模式、然后什么都不做**：模式被丢掉，永远走 MILP。
/// 于是"要了 Benders"的请求会拿到一个**看起来成功**的 MILP 答案，而备注里还留着
/// `Benders requested; adaptive Benders path enabled in application layer` —— **说一套、做一套**。
/// 这是本仓记录过的最坏缺陷形态：不是报错，是**给出一个不是用户要的、却看起来成功的结果**。
///
/// These two paths used to **resolve the mode and then do nothing with it**: the mode was discarded and MILP always ran. A request that asked for Benders got **a
/// MILP answer that looked successful**, with the note `Benders requested; adaptive Benders path enabled in application layer` still in the diagnostics —
/// **saying one thing and doing another.** That is the worst defect shape this port has recorded: not an error, but **an answer that is not what was asked for
/// and looks successful.**
/// </remarks>
public sealed class Demo2BendersWiringTests
{
    [Theory]
    [InlineData("Predistribution")]
    [InlineData("WeightRecommendation")]
    public void ASuppliedMilpSolver_ShouldNotHideTheMissingBendersWiring(string path) {
        RequestDTO request = path == "Predistribution"
            ? AircraftLoadingDemo.CreatePredistributionSampleRequest() with { SolvePolicy = new SolvePolicy(PreferBenders: true) }
            : AircraftLoadingDemo.CreateSampleRequest() with { SolvePolicy = new SolvePolicy(PreferBenders: true) };

        // 两条路径都带着求解器调用：**带了求解器更危险**——它会让"静默走 MILP"的结果看起来完全成功。
        // Both paths are called with a solver: **that is the dangerous case** — it makes the silent MILP result look perfectly successful.
        (ResponseDTO Response, RenderDTO? Render) executed = path == "Predistribution"
            ? new PredistributionApplication().Execute(request, new ScipLinearSolver())
            : new WeightRecommendationApplication().Execute(request, new ScipLinearSolver());

        executed.Response.Succeed.Should().BeFalse("this path cannot run Benders, and pretending otherwise is the defect being fixed");
        executed.Response.Status.Should().Be("BendersNotWiredForMode");
        executed.Response.Status.Should().NotBe("Optimal", "a MILP optimum is not an answer to a Benders request");

        // 备注要把真相写明白：请求了 Benders、本路径没接线。
        // The note must state the truth: Benders was requested and this path has no wiring for it.
        executed.Response.Notes.Should().Contain(note => note.Contains("benders_mode_not_wired"),
            "the diagnostics must name the reason, not just fail");
    }

    [Theory]
    [InlineData("Predistribution")]
    [InlineData("WeightRecommendation")]
    public void WithoutPreferBenders_TheSamePaths_ShouldStillSolve(string path) {
        // **成对判据**：拒绝只在"真要求 Benders"时发生。如果实现把两条路径无条件拒掉，上面那条也会绿——
        // 而那是把两个可用的模式一起弄坏了。
        // **Paired criterion**: the refusal happens only when Benders is actually requested. An unconditional refusal would keep the criteria above green
        // while breaking two working modes.
        RequestDTO request = path == "Predistribution"
            ? AircraftLoadingDemo.CreatePredistributionSampleRequest()
            : AircraftLoadingDemo.CreateSampleRequest();

        (ResponseDTO Response, RenderDTO? Render) executed = path == "Predistribution"
            ? new PredistributionApplication().Execute(request, new ScipLinearSolver())
            : new WeightRecommendationApplication().Execute(request, new ScipLinearSolver());

        executed.Response.Status.Should().Be("Optimal");
        executed.Response.Notes.Should().NotContain(note => note.Contains("benders_mode_not_wired"));
    }

    /// <summary>
    /// **请求 Benders 但阈值没到 → 走 MILP 是基线的正确行为，不是缺陷** / Asking for Benders below the threshold falls back to MILP by design.
    /// </summary>
    /// <remarks>
    /// 这条与上面两条划清界限：**低于阈值回落是基线明写的**（`Benders requested but skipped: binary_variables=… &lt; threshold=…`），
    /// 而"本路径没接线"是**移植缺口**。两种都要说清楚，但**不能混成一句**——否则读者分不清"我调阈值就能用"还是"这条路径根本不行"。
    ///
    /// This draws the line: **falling back below the threshold is the baseline's own behaviour** (with the `Benders requested but skipped` note), while "this
    /// path has no wiring" is **a port gap.** Both must be stated, but **never as the same message** — otherwise the reader cannot tell "raising the threshold
    /// helps" from "this path simply cannot do it."
    /// </remarks>
    [Fact]
    public void BelowTheThreshold_TheFallback_ShouldNotBeBlamedOnTheWiring() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        // 阈值抬到 8，而样例只有 3 货 × 2 舱 = 6 个二值变量 → 落回 MILP。
        // Raise the threshold to 8 while the sample has only 3 cargos times 2 positions = 6 binary variables, so it falls back to MILP.
        RequestDTO request = sample with {
            SolvePolicy = new SolvePolicy(PreferBenders: true),
            BendersAdaptive = new BendersAdaptiveConfig(MinBinaryVariables: 8),
        };

        (ResponseDTO Response, RenderDTO? Render) executed =
            new WeightRecommendationApplication().Execute(request, new ScipLinearSolver());

        executed.Response.Status.Should().Be("Optimal", "below the threshold the baseline solves as MILP");
        executed.Response.Notes.Should().Contain(note => note.Contains("Benders requested but skipped"),
            "the fallback must state why it happened");
        executed.Response.Notes.Should().NotContain(note => note.Contains("benders_mode_not_wired"),
            "a below-threshold fallback is not a missing-wiring gap");
    }

    /// <summary>
    /// **不许徒手拼 `LinearBendersModels`** / Do not build `LinearBendersModels` by hand.
    /// </summary>
    /// <remarks>
    /// 接上框架路径的**唯一合法入口**是 `LinearBendersPartition.BuildAsync`：它负责展开子问题、收集来源变量顺序，
    /// 并生成那个**会真的复核解**的 `verify` 回调。手搓 `new LinearBendersModels(...)` 要自己传 `verify`，
    /// 而 demo2 拿不到框架里那个私有的 `VerifySolution`——于是最省事的写法就是传一个**永远通过的**回调。
    /// 那一刻 Benders 的合并解不再被复核，**结果看上去正常**。
    ///
    /// 这条判据挡的正是那个捷径：`LinearBendersModels` 是 public 的，`BuildAsync` 需要 `Stages`，
    /// 而 demo2 的域还不产出 `Stages`——于是"手搓"成了唯一走得通的写法，也就成了唯一值得提前挡住的写法。
    ///
    /// The **only legal way** onto the framework path is `LinearBendersPartition.BuildAsync`: it expands the subproblem, collects the source-variable order and
    /// builds that `verify` callback which **really re-checks the solution**. Building `new LinearBendersModels(...)` by hand means supplying `verify` yourself, and
    /// demo2 cannot reach the private `VerifySolution`, so the easiest shape is an **always-passing** callback. From that moment the merged Benders solution is no
    /// longer verified while **the result still looks normal**. This criterion blocks exactly that shortcut: `LinearBendersModels` is public while `BuildAsync` needs
    /// `Stages`, which the demo2 domains do not produce yet — so "by hand" is the only reachable shape, and therefore the one worth blocking in advance.
    /// </remarks>
    [Fact]
    public void TheFrameworkBendersModels_ShouldNotBeAssembledByHand() {
        var directory = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (directory is not null) {
            string root = global::System.IO.Path.Combine(directory.FullName, "src", "Example", "FrameworkDemo", "Demo2");
            if (global::System.IO.Directory.Exists(root)) {
                foreach (string file in global::System.IO.Directory.EnumerateFiles(root, "*.cs", global::System.IO.SearchOption.AllDirectories)) {
                    var code = new global::System.Text.StringBuilder();
                    foreach (string line in global::System.IO.File.ReadAllLines(file)) {
                        if (line.TrimStart().StartsWith("//", global::System.StringComparison.Ordinal)) {
                            continue;
                        }
                        code.AppendLine(line);
                    }
                    // **不写 `new `**：B863 的变异用**全限定名** `new Fuookami.Ospf.Framework.Solver.LinearBendersModels(` 绕过了第一版判据。
                    // 只钉类型名加括号，限定与否都算。
                    // **No `new ` prefix**: B863's mutation slipped past the first version by using the **fully qualified** type name; pinning the type name plus a
                    // parenthesis catches both spellings.
                    code.ToString().Should().NotContain("LinearBendersModels(", "assembling the models by hand needs an always-passing verify callback");
                }
                return;
            }
            directory = directory.Parent;
        }
        throw new global::System.IO.DirectoryNotFoundException("could not locate the Demo2 sources");
    }

    /// <summary>
    /// **结构性分歧：框架要一个模型，demo2 建了两个** / A structural divergence: the framework takes one model, demo2 builds two.
    /// </summary>
    /// <remarks>
    /// 基线 `DiscreteLoadingApplicationService.kt:90` 是：
    ///
    /// ```kotlin
    /// val models = LinearBendersPartition.build(registration.model, registration.bendersStages)
    /// ```
    ///
    /// 也就是**一个注册模型 + 一张显式阶段表**：哪些变量属于主问题、哪些属于连续追索，由调用方标出来，
    /// 框架负责拆分。本仓的 `LinearBendersPartition.BuildAsync(model, stages)` 形状一致。
    ///
    /// 而 demo2 走的是**两个模型**：主问题与子问题分别注册、分别组装。这两件事没法直接对接——
    /// `BuildAsync` 没有"给我两个模型"的重载。所以"换掉 demo2 那条路径"不是接线活，是**把两个模型并成一个**，
    /// 而两个注册路径的符号是重叠的（`Payload` 两边都注册）。
    ///
    /// 这条判据钉住**分歧本身**，这样下次有人以为这只是接线时，判据会把这两句话摆在他面前。
    /// 它不钉"哪个对"——对的是基线那边，本仓这边是待改的。
    ///
    /// The baseline at `DiscreteLoadingApplicationService.kt:90` is `LinearBendersPartition.build(registration.model, registration.bendersStages)`: **one registered model
    /// plus an explicit stage table**. The port's `LinearBendersPartition.BuildAsync(model, stages)` has the same shape. demo2 instead runs on **two models**, each
    /// registered and assembled separately, and the two cannot be joined directly — `BuildAsync` has no "give me two models" overload. So replacing demo2's path is
    /// not wiring, it is **merging the two models into one**, and the two registration paths overlap in their symbols (`Payload` is registered by both). This criterion
    /// pins **the divergence itself**, so the next reader who assumes it is a wiring job gets both sentences in front of them. It does not decide which is right — the
    /// baseline is right and this port is the one to change.
    /// </remarks>
    [Fact]
    public void TheStructuralDivergence_ShouldStayVisible() {
        System.Reflection.MethodInfo? build = typeof(Fuookami.Ospf.Framework.Solver.LinearBendersPartition).GetMethod("BuildAsync");
        build.Should().NotBeNull();
        build!.GetParameters().Should().HaveCount(2,
            "the partition takes one model plus a stage table, exactly like the baseline");
        build.GetParameters()[1].ParameterType.Name.Should().Contain("Dictionary",
            "the second argument is the stage table");

        var directory = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = global::System.IO.Path.Combine(
                directory.FullName, "src", "Example", "FrameworkDemo", "Demo2", "Application", "FullLoadApplication.cs");
            if (global::System.IO.File.Exists(candidate)) {
                string code = StripComments(candidate);
                // **同一个文件里有两条构建路径，B893 之后分派走的是单模型那条**：`demo2_full_load_single` 是基线形状、也是现在的分派；
                // `demo2_full_load_master` / `_sub` 仍在（对照判据还用得上），但**不再被分派调用**。这里按**名字**数，而不是数前缀——
                // 前缀计数在 B866 当天就红了一次，红的原因是"多了一条正确的路径"。
                //
                // **Two construction paths live in one file, and since B893 dispatch takes the single-model one**: `demo2_full_load_single` is the baseline shape and what
                // dispatch uses; `demo2_full_load_master` / `_sub` remain (the comparison criteria still need them) but **are no longer called by dispatch**. Counted **by
                // name**, not by prefix — the prefix count went red the day B866 landed, and it went red because **a correct path had been added**.
                //
                // **After B866 there are two construction paths in one file**: dispatch still runs "master + sub" (`demo2_full_load_master` / `_sub`) while a
                // single-model builder (`demo2_full_load_single`) sits beside it. So this counts **by name**, not by prefix — the prefix count went red the day B866
                // landed, and it went red because **a correct path had been added**.
                CountOccurrences(code, "new LinearMetaModel<Flt64>(\"demo2_full_load_master\"").Should().Be(1);
                CountOccurrences(code, "new LinearMetaModel<Flt64>(\"demo2_full_load_sub\"").Should().Be(1);
                CountOccurrences(code, "new LinearMetaModel<Flt64>(\"demo2_full_load_single\"").Should().Be(1,
                    "the single-model builder is the baseline shape **and** what dispatch uses since B893");
                // **旧路径没有生产调用方**（B913）：`SolveWithBenders` 在代码里只出现**一次**（它自己的声明）。
                // 它此刻唯一的消费者是对照判据（`Demo2BendersAssignmentFabricationTests` 两条直调）。
                // **一旦这里变成 2，说明有人在分派里又把它接回去了**——那正是 B881/B889 被驳回两次的那件事。
                //
                // **The old route has no production caller** (B913): `SolveWithBenders` appears **once** in code — its own declaration. Its only consumers now are the
                // comparison criteria (two direct calls). **A count of 2 would mean someone wired it back into dispatch** — the very thing rejected twice in B881/B889.
                CountOccurrences(code, "SolveWithBenders(").Should().Be(1,
                    "dispatch takes the single-model chain since B893; the old route stays only for the comparison criteria");
                return;
            }
            directory = directory.Parent;
        }
        throw new global::System.IO.FileNotFoundException("could not locate FullLoadApplication.cs");
    }

    /// <summary>去掉注释行 / Strip comment lines.</summary>
    /// <param name="path">文件 / The file.</param>
    /// <returns>代码 / The code.</returns>
    private static string StripComments(string path) {
        var code = new global::System.Text.StringBuilder();
        foreach (string line in global::System.IO.File.ReadAllLines(path)) {
            if (line.TrimStart().StartsWith("//", global::System.StringComparison.Ordinal)) {
                continue;
            }
            code.AppendLine(line);
        }
        return code.ToString();
    }

    /// <summary>数出现次数 / Count occurrences.</summary>
    /// <param name="text">文本 / The text.</param>
    /// <param name="token">要找的串 / The token.</param>
    /// <returns>次数 / The count.</returns>
    private static int CountOccurrences(string text, string token) {
        int count = 0;
        int index = text.IndexOf(token, global::System.StringComparison.Ordinal);
        while (index >= 0) {
            count++;
            index = text.IndexOf(token, index + token.Length, global::System.StringComparison.Ordinal);
        }
        return count;
    }

    /// <summary>
    /// **分派仍走两模型路径，且原因写在源码里** / Dispatch still takes the two-model route, with the reason in the source.
    /// </summary>
    /// <remarks>
    /// B881 试过切到单模型路径，被实测驳回：脚本求解器在旧算法上收敛、在 `LinearBendersAlgorithm` 上直接失败。
    /// 这条判据钉住"分派**暂时**留在旧路径"这个事实——**不钉的话，下一个人会以为已经切了**。
    /// 等两套算法的调用契约对齐，这条就该改（那时才会删掉旧路径）。
    ///
    /// B881 tried switching to the single-model route and measurement rejected it. This criterion pins the fact that dispatch **still** takes the old route — **without
    /// it, the next reader believes the switch already happened**. Once the two algorithms' calling contracts agree, this criterion changes (that is when the old route goes away).
    /// </remarks>
    [Fact]
    public void TheDispatch_ShouldStillUseTheTwoModelRoute() {
        string? path = null;
        var directory = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = global::System.IO.Path.Combine(
                directory.FullName, "src", "Example", "FrameworkDemo", "Demo2", "Application", "FullLoadApplication.cs");
            if (global::System.IO.File.Exists(candidate)) {
                path = candidate;
                break;
            }
            directory = directory.Parent;
        }
        global::System.IO.File.Exists(path).Should().BeTrue("the source must be locatable for this criterion");

        // **代码里要有调用，注释里要有理由**：两处分开查，因为 `StripComments` 会把理由剥掉。
        // **A call in the code and a reason in the comments**: checked separately, because `StripComments` removes the reason.
        StripComments(path!).Should().Contain("SolveSingleModelBendersAsync(",
            "the dispatch must call the single-model chain; the old route stays reachable for comparison");
        // **两次尝试都要在理由里留痕**（B889）：第一次是脚本不收敛，第二次是**默认门禁的假脚本不收敛而真后端收敛**——
        // 两个理由不同，只留第一个会让人以为"再试一次就行了"。
        //
        // **Both attempts must leave a trace** (B889): the first was a non-converging script, the second is **the default gate's fake script not converging while the real
        // backend does** — different reasons, and keeping only the first makes the next reader think "just try again".
        global::System.IO.File.ReadAllText(path!).Should().Contain("B889 tried the switch a second time",
            "the second rejection needs its own reason next to the code");
        global::System.IO.File.ReadAllText(path!).Should().Contain("B881 tried switching this line to the single-model route and measurement rejected it",
            "the reason has to live next to the code, otherwise the revert looks like an oversight");
    }

    /// <summary>**旧路径必须能被对照判据直接调用** / The old route must be callable by the comparison criteria.</summary>
    /// <remarks>
    /// B892 把 `SolveWithBenders` 从 `private` 改成 `internal`，理由只有一个：**分派切走之后，对照关系还在**。
    /// 如果它留在 `private`，切换当天那些判据只能被删或改写，**"新旧对比"这件事就消失了**——而对比正是切换之后最需要的东西。
    ///
    /// B892 changed `SolveWithBenders` from `private` to `internal` for one reason: **the comparison survives the switch**. Left private, the criteria could only be deleted or
    /// rewritten, and **the old-versus-new comparison would disappear** — exactly the thing that matters most after switching.
    /// </remarks>
    [Fact]
    public void TheOldRoute_ShouldStayCallableForComparison() {
        typeof(FullLoadApplication)
            .GetMethod("SolveWithBenders", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Should().NotBeNull();
        typeof(FullLoadApplication)
            .GetMethod("SolveWithBenders", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .IsAssembly.Should().BeTrue("internal keeps the route reachable for the comparison criteria, private would hide it");
    }
}
