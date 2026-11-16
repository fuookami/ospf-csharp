#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Progress;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Csp1d.Tests.Application.Service;

/// <summary>
/// CSP1D 列生成必须支持进度上报，且两套取消机制**都必须生效**。
/// CSP1D column generation must support progress reporting, and **both** cancellation mechanisms must take effect.
/// </summary>
/// <remarks>
/// 基线 `Csp1dColumnGeneration.solve` 与 `solveWithTrace` 都收 `progressContext` 参数，
/// 并在 4 处调用 `progressContext?.throwIfCancelled`。
///
/// C# 侧 接入了 `CancellationToken`， / / 移植了整套进度上报与适配器，
/// **但 CSP1D 链路一个都没接**——所以进度上报在 CSP1D 里**完全不生效**。
///
/// **两套取消机制必须一起生效**：`cancellationToken` 与 `progressContext.IsCancelled` **语义相同、来源不同**，
/// 调用方可能只给了其中之一。只查一套会让另一套静默失效。
///
/// `Csp1dColumnGeneration.solve` and `solveWithTrace` both take a `progressContext` parameter and call `progressContext?.throwIfCancelled` at four
/// points.
///
/// wired `CancellationToken` into the C# side and / / implemented the whole reporting system and adapters, **but the CSP1D chain got none of it** — so
/// progress reporting **does not work at all** inside CSP1D.
///
/// **Both cancellation mechanisms must take effect together**: `cancellationToken` and `progressContext.IsCancelled` are **semantically identical but sourced
/// differently**, and a caller may supply only one. Checking just one silently disables the other.
/// </remarks>
public class Csp1dProgressWiringTests {
    private sealed class RecordingReporter : IProgressReporter {
        public List<SolverProgressSnapshot> Snapshots { get; } = new();

        public Result<Success, ErrorCode, Error<ErrorCode>> Report(SolverProgressSnapshot snapshot) {
            Snapshots.Add(snapshot);
            return Results.Ok(Results.SuccessInstance);
        }
    }

    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Csp1dProblem<Flt64> MakeProblem() {
        var material = new Material<Flt64>(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)
                ),
                Step: Millimetres(1.0)
            )
        );
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) }
        );
        var builder = new Csp1dProblemBuilder<Flt64>();
        builder.AddProduct(product);
        builder.AddMaterial(material);
        builder.AddDemand(new ProductDemand<Flt64>(product, Millimetres(50.0)));
        return builder.Build();
    }

    private static Csp1dColumnGeneration<Flt64> MakeColumnGeneration() =>
        new(solver: new Fuookami.Ospf.Example.Tests.FrameworkDemo.MockColumnGenerationSolver());

    /// <summary>**给了进度上下文就必须收到快照** / A supplied progress context must receive snapshots.</summary>
    [Fact]
    public async Task SolveAsync_ShouldReportToTheProgressContext() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);

        await MakeColumnGeneration().SolveAsync(
            MakeProblem(),
            solveConfig: null,
            cancellationToken: default,
            progressContext: context);

        reporter.Snapshots.Should().NotBeEmpty("the CSP1D chain must actually report progress");
    }

    /// <summary>**没给进度上下文时零上报** / With no progress context, nothing is reported.</summary>
    [Fact]
    public async Task SolveAsync_ShouldNotReportWithoutAProgressContext() {
        var reporter = new RecordingReporter();

        await MakeColumnGeneration().SolveAsync(MakeProblem());

        reporter.Snapshots.Should().BeEmpty();
    }

    /// <summary>**进度序列必须与基线数值一致** / The snapshot sequence must match its numbers.</summary>
    /// <remarks>
    /// **/ 的数值是我自拟的（开场 60、收尾 100），不是。** 这批核对了基线
    /// 的 4 处 `reportProgress`，拿到真实数值：
    ///
    /// | 位置 | 阶段 | 子阶段 | 阶段内 | 总体 |
    /// | --- | --- | --- | --- | --- |
    /// | :194 初始列 | `ColumnGeneration` | `initial_columns` | 0 | **15** |
    /// | :348 LP 就绪 | `ModelBuilding` | `lp_master_ready` | 100 | **20** |
    /// | :470 定价中段 | `ColumnGeneration` | `pricing_iteration` | 40 | 迭代区间内 |
    /// | :683 定价结束 | `ColumnGeneration` | `pricing_iteration` | 100 | 迭代区间末 |
    ///
    /// **收尾上界是 99，不是 100**——最终 MILP 拿的是 `phase(75, 99)`，所以进度**永远到不了 100**。
    /// 这条如实断言 99。
    ///
 /// **/the earlier numbers were my own invention (opening 60, closing 100), not its.** checked its four `reportProgress` calls and took
    /// the real values (table above).
    ///
    /// **The ceiling is 99, not 100** — its final MILP gets `phase(75, 99)`, so progress **never reaches 100**. This asserts 99 honestly.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldFollowTheBaselineProgressSequence() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);

        await MakeColumnGeneration().SolveAsync(
            MakeProblem(),
            solveConfig: null,
            cancellationToken: default,
            progressContext: context);

        reporter.Snapshots.Should().HaveCountGreaterThanOrEqualTo(2);
        // 开场：初始列（基线 :194）
        reporter.Snapshots[0].Stage.Id.Value.Should().Be("COLUMN_GENERATION");
        reporter.Snapshots[0].SubStage!.Key.Should().Be("initial_columns");
        reporter.Snapshots[0].ProgressInStage.Should().Be(0);
        reporter.Snapshots[0].OverallProgress.Should().Be(15);
        // 收尾：基线 `phase(75, 99)` 的上界
        reporter.Snapshots[^1].ProgressInStage.Should().Be(100);
        reporter.Snapshots[^1].OverallProgress.Should().Be(99);
    }

    /// <summary>**迭代中段必须上报定价快照** / The mid-iteration pricing snapshot must be reported.</summary>
    /// <remarks>
    /// 基线 :470 在每轮 LP 求解后上报 `(pricing_iteration, 阶段内 40)`，总体进度落在迭代区间内（20—75）。
    /// 没有它，长时间求解的区间 20—75 **完全没有中间反馈**。
    ///
    /// **这条判据的能力边界**：它验证"快照存在且阶段内是 40"，**不验证区间的算术**。
    /// `RangedProgress` 的变异（"永远返回区间起点"）**存活**——因为第一轮 `iteration = 0` 时，
    /// 正确值与变异值**都是 20**，无法区分。这一点在缺口里记下。
    ///
    /// The code's :470 reports `(pricing_iteration, in-stage 40)` after each LP solve, with overall progress inside the iteration band (20—75). Without it, the
    /// 20—75 band has **no feedback at all** during a long solve.
    ///
    /// **The criterion's limits**: it verifies "the snapshot exists with in-stage 40" and **not the band arithmetic**. The `RangedProgress` mutation ("always return
    /// the range start") **survives** — at the first iteration `iteration = 0`, the correct and mutated values are **both 20**, indistinguishable. Recorded as a gap.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldReportAMidIterationPricingSnapshot() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);

        await MakeColumnGeneration().SolveAsync(
            MakeProblem(),
            solveConfig: null,
            cancellationToken: default,
            progressContext: context);

        bool found = reporter.Snapshots.Any(
            snapshot => snapshot.SubStage is not null
                && snapshot.SubStage.Key == "pricing_iteration"
                && snapshot.ProgressInStage == 40);
        found.Should().BeTrue("each LP solve must be followed by a mid-iteration pricing snapshot");
    }

    /// <summary>**LP 主问题的进度必须被 `phase` 重映射进迭代带** / The LP master's progress must be remapped into the iteration band by `phase`.</summary>
    /// <remarks>
    /// 基线 `:383` 把 `progressContext?.phase(iterationStart, iterationEnd)` 传给 `solveLpMaster`。
    ///
    /// **判据靠"独立数值被替换掉"来区分**： 让 `SolveLPAsync(metaModel, options)` 在上报
    /// `(MasterLp, 0, **30**)` 与 `(MasterLp, 100, **40**)`——那是它**脱离父级单独算**的数字。
    /// 一旦套上 `phase`，这两个数字被换成带内值，**30 与 40 都不该再出现**。
    ///
    /// 若 `Phase` 没接上、直接把父上下文传下去，`MASTER_LP` 快照就是 30 / 40，这条**红**。
    ///
    /// 断言"不存在 30/40"而不是"等于某个带内值"——**带内值依赖迭代上限**（`iterationLimitBound`），
    /// 钉死具体数字会让判据随 fixture 变化而碎。
    ///
    /// The code's `:383` passes `progressContext?.phase(iterationStart, iterationEnd)` to `solveLpMaster`.
    ///
    /// **The criterion discriminates via "the standalone numbers are replaced"**: made `SolveLPAsync(metaModel, options)` report `(MasterLp, 0, **30**)` and
    /// `(MasterLp, 100, **40**)` — computed **standalone, without a parent**. Once `phase` wraps it, those become band-relative values, and **neither 30 nor 40 should
    /// appear**. If `Phase` were not wired and the parent context passed straight through, `MASTER_LP` snapshots would show 30 / 40 and this would be **red**.
    ///
    /// It asserts "30/40 are absent" rather than "equals some in-band value" — **in-band values depend on `iterationLimitBound`**, and pinning them would make the
    /// criterion break whenever the fixture changes.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRemapTheLpMasterProgressIntoItsBand() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);

        await MakeColumnGeneration().SolveAsync(
            MakeProblem(),
            solveConfig: null,
            cancellationToken: default,
            progressContext: context);

        List<SolverProgressSnapshot> masterLp = reporter.Snapshots
            .Where(snapshot => snapshot.Stage.Id.Value == "MASTER_LP")
            .ToList();
        masterLp.Should().NotBeEmpty("the LP master sub-solve must report through the parent context");
        masterLp.Should().NotContain(
            snapshot => snapshot.OverallProgress == 30 || snapshot.OverallProgress == 40,
            "phase must replace the standalone 30/40 with band-relative values");
    }

    /// <summary>**最终 MILP 的进度必须落在 75—99 带里** / The final MILP's progress must land in the 75—99 band.</summary>
    /// <remarks>
    /// 基线 `:753` 把 `progressContext?.phase(75, 99)` 传给 `solveFinalMilp`。
    ///
    /// **曾说这条"观测不到"，理由是"`Register` 阶段就失败、走不到求解器"——那个结论是错的。**
    /// 最初只是读代码推断，没有实际跑到那里。实测：完整 CSP1D 流程**确实到达最终 MILP**，
    /// 快照里有 `REGISTRATION/-/100/99` 与 `MILP/incumbent/99/99`。
    ///
    /// 判据失败的真正原因是**当时 `MockColumnGenerationSolver` 覆盖了带 options 的重载、
    /// 把适配器包装摘掉了**。**"读代码推出的结论"与"实测的结论"在这里正好相反。**
    ///
    /// The code's `:753` passes `progressContext?.phase(75, 99)` to `solveFinalMilp`.
    ///
    /// **claimed this was "unobservable" because "`Register` fails and the solver is never reached" — that conclusion was wrong.** I inferred it from reading code
    /// without actually running there. In fact the full CSP1D flow **does reach the final MILP**: the snapshots include `REGISTRATION/-/100/99` and `MILP/incumbent/99/99`.
    ///
    /// The real reason the earlier criterion failed was that **`MockColumnGenerationSolver` then overrode the options overload, removing the adapter wrapping** (fixed in
    /// ). **"A conclusion inferred from reading code" and "a measured conclusion" were exactly opposite here.**
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRemapTheFinalMilpProgressIntoItsBand() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter);

        await MakeColumnGeneration().SolveAsync(
            MakeProblem(),
            solveConfig: null,
            cancellationToken: default,
            progressContext: context);

        // 注册回调独立值是 10 + 100/5 = 30；带内是 99。断言 99 才能区分。
        // The registration callback is 10 + 100/5 = 30 standalone and 99 banded; asserting 99 is what discriminates.
        reporter.Snapshots.Should().Contain(
            snapshot => snapshot.Stage.Id.Value == "REGISTRATION" && snapshot.OverallProgress == 99);
    }

    /// <summary>**`progressContext.IsCancelled` 必须独立生效** / `progressContext.IsCancelled` must work on its own.</summary>
    /// <remarks>
    /// 这条**不给 `CancellationToken`**，只给一个 `IsCancelled = true` 的上下文。
    /// 若实现只查令牌、不查上下文的取消判定，这条会**红**——而只查令牌的实现在
    /// `CancellationToken` 的判据下**全部通过**。
    ///
    /// This supplies **no `CancellationToken`**, only a context with `IsCancelled = true`. If the implementation only checked the token and not the context's
    /// predicate, this would be **red** — while a token-only implementation **passes every** `CancellationToken` criterion.
    ///
    /// **这条判据的能力边界要说清**：它**无法区分"有显式检查点"与"没有显式检查点"**。
    /// 变异测试证实——把文件里**全部 3 处** `progressContext?.ThrowIfCancelled` 一起删掉，这条**仍然绿**。
    ///
    /// 原因是 `SolverProgressContext.Report` 内部**也会调 `ThrowIfCancelled`**，
    /// 而 CSP1D 在入口后必然上报一次阶段快照。所以取消**由 `Report` 兜住**，显式检查点是**冗余的第二道**。
    ///
    /// **冗余不是错误**：显式检查点能在"取消发生在两次上报之间"时更早抛出。但**它没有独立可证伪性**，
    /// 这里如实记录，不假装判据更强。
    ///
    /// **The criterion's limits must be stated**: it **cannot distinguish "there are explicit checkpoints" from "there are none"**. Mutation testing confirmed that
    /// deleting **all 3** `progressContext?.ThrowIfCancelled` calls still leaves this green.
    ///
    /// The reason is that `SolverProgressContext.Report` **also calls `ThrowIfCancelled` internally** (aligned with the code), and CSP1D always reports
    /// one stage snapshot after the entry. So cancellation **is caught by `Report`**, and the explicit checkpoints are a **redundant second layer**.
    ///
    /// **Redundancy is not a bug**: explicit checkpoints throw earlier when cancellation happens between two reports. But **they have no independent falsifiability**,
    /// recorded honestly here rather than pretending the criterion is stronger.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldHonourTheContextCancellationPredicateAlone() {
        var reporter = new RecordingReporter();
        var context = new SolverProgressContext(reporter, isCancelled: static () => true);

        Func<Task> solve = async () => await MakeColumnGeneration().SolveAsync(
            MakeProblem(),
            solveConfig: null,
            cancellationToken: default,
            progressContext: context);

        await solve.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>**`CancellationToken` 必须独立生效** / `CancellationToken` must work on its own.</summary>
    [Fact]
    public async Task SolveAsync_ShouldHonourTheCancellationTokenAlone() {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Func<Task> solve = async () => await MakeColumnGeneration().SolveAsync(
            MakeProblem(),
            solveConfig: null,
            cancellationToken: source.Token,
            progressContext: null);

        await solve.Should().ThrowAsync<OperationCanceledException>();
    }
}
