// 分区列生成接线的判据 / Criteria for the partition column-generation wiring.

using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **CG 分派那一段的接线判据** / Wiring criteria for the CG dispatch.
/// </summary>
/// <remarks>
/// 拒绝的三条里，"注册与分区配置没接起来"是**错的**（注册一直都在），分区配置 补齐，把两块交给框架循环。
/// 本文件钉的是**分派真的把请求带到了那条链上**，而不是"列生成能求出最优"——后者的前提是模型收敛，不属于接线。
///
/// Of the earlier three refusal reasons, "the registration and the partition configuration are not wired" was **wrong** (the registration was always there); supplied the
/// configuration and hands both to the framework loop. This file pins that **the dispatch really carries the request onto that chain**, not that "column generation
/// reaches optimality" — the latter presumes convergence, which is not wiring.
/// </remarks>
public class Demo2ColumnGenerationWiringTests
{
    /// <summary>**`Solve` 必须收一个列生成求解器参数** / `Solve` must take a column-generation solver.</summary>
    /// <remarks>
    /// 反射判据，**不需要后端**。参数类型必须是插件实现的那个接口（与 Benders 那次同一个坑：同名接口两份）。
    ///
    /// A reflection criterion, **needs no backend**. The parameter type must be the one the plugins implement — the same trap as the Benders round, where two
    /// interfaces shared a name.
    /// </remarks>
    [Fact]
    public void Solve_ShouldTakeAColumnGenerationSolver() {
        System.Reflection.ParameterInfo? parameter = typeof(Demo2ApplicationService)
            .GetMethod("Solve")!
            .GetParameters()
            .FirstOrDefault(candidate => candidate.Name == "columnGenerationSolver");

        parameter.Should().NotBeNull("the dispatch cannot reach column generation without a solver to hand it");
        parameter!.ParameterType.Should().Be(typeof(Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver));
        typeof(ScipColumnGenerationSolver).Should().Implement(parameter.ParameterType,
            "the real backend must satisfy the parameter type demo2 declares");
    }

    /// <summary>**没给求解器时点名的正是求解器** / With no solver, the refusal names the solver.</summary>
    [Fact]
    public void WithoutASolver_ShouldNameTheSolverNotTheModel() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PartitionColumnGeneration: true),
        };

        ResponseDTO response = Demo2ApplicationService.Solve(request, StowageMode.FullLoad).Response;

        response.Status.Should().Be("ColumnGenerationSolverNotSupplied");
        // **不能报成 `NotYetPorted`**：那句话会把人送去补已经补好的分区配置。
        // **Must not report `NotYetPorted`**: that wording sends people to rebuild the partition configuration that already exists (the lesson).
        response.Notes.Should().NotContain(note => note.Contains("尚未移植") || note.Contains("not yet dispatched"));
    }

    /// <summary>**装配失败时报 `InitFailed`，而不是悄悄走到列生成** / An assembly failure reports `InitFailed` rather than sliding into column generation.</summary>
    [Fact]
    public void AnUnknownAircraft_ShouldStillBeRefusedBeforeColumnGeneration() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            AircraftType = AircraftTypeInput.Unknown,
            SolvePolicy = new SolvePolicy(PartitionColumnGeneration: true),
        };

        ResponseDTO response = Demo2ApplicationService.Solve(request, StowageMode.FullLoad).Response;

        // **机型守卫仍然在策略分派之前**：列生成接上了也不改变这个顺序。
        // **The aircraft guard still precedes the policy dispatch** (the earlier order): wiring CG does not reorder it.
        response.Status.Should().Be("UnsupportedAircraft");
    }

#if SCIP_INTEGRATION
    /// <summary>**真后端上整条链跑通** / The whole chain completes on a real backend.</summary>
    /// <remarks>
    /// **这条判据钉的是"停在哪"**：链跑到收尾复核就失败，`目标引用缺少变量值`。
    /// 补上 `MetaModel.ObjectiveSnapshot`（此前一直缺），这条**应声翻面**——现在必须是 `Optimal`。
    ///
    /// **这正是"缺口变成判据、补上时判据翻面"的用法**： 写下停点，补上之后它立刻红了，
    /// 提醒我改判据而不是让它默默绿着。
    ///
    /// **pinned where it stopped** — the chain failed at the final check with "missing objective variable". added `MetaModel.ObjectiveSnapshot`
    /// (the code always had it, this implementation never did) and this criterion **flipped immediately**: it must now be `Optimal`. **That is exactly what
    /// "turn a gap into a criterion" buys** — the pin from went red the moment the gap closed, forcing the criterion to be rewritten instead of quietly
    /// staying green.
    /// </remarks>
    [Fact]
    public void WithARealBackend_ShouldCompleteTheChain() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PartitionColumnGeneration: true),
        };

        ResponseDTO response = Demo2ApplicationService.Solve(
            request,
            StowageMode.FullLoad,
            columnGenerationSolver: new ScipColumnGenerationSolver()).Response;
        var notes = response.Notes ?? [];
        string joined = string.Join(" | ", notes);

        // 1. **模型建出来并注册完**。
        // **The model is built and registered.**
        notes.Should().Contain(note => note.Contains("partition_model=rows"),
            "the dispatch must build and register the full model before column generation; notes: " + joined);
        // 2. **链走到收尾**，并报出列数与收敛结论。
        // **The chain reaches the end**, reporting the column count and the convergence verdict.
        notes.Should().Contain(note => note.Contains("partition_columns="),
            "reaching the end is what emits this note; notes: " + joined);
        notes.Should().Contain(note => note.Contains("root_lp_converged="),
            "the convergence verdict must be reported, not assumed; notes: " + joined);
        // 3. **求解成功**，且目标复核通过（否则 `FinishAsync` 会报错）。
        // **The solve succeeds**, and the objective re-check passed (otherwise `FinishAsync` would have failed).
        response.Succeed.Should().BeTrue("the chain must complete; notes: " + joined);
        // 4. **不许再出现 那个停点**。
        // **The stop point must not come back.**
        notes.Should().NotContain(note => note.Contains("目标引用缺少变量值") || note.Contains("Missing objective variable"),
            "B930 flattened the objective; this wording coming back means the flattening was lost; notes: " + joined);
    }

    /// <summary>**列数必须至少等于分区数** / The column count must be at least the partition count.</summary>
    /// <remarks>
    /// 上一条只断言"跑到收尾 + 报出列数"，**它挡不住"只选了一个种子列"**。实测示例请求是 `partition_columns=1`——
    /// 但那是因为**它只有一个分区**（两个舱位都在 `DEFAULT`），所以 1 是下限、证明不了任何事。
    ///
    /// 这条改用**两个分区**的请求：列生成对每个分区至少要有自己的种子列，否则"收敛"只覆盖了其中一个分区。
    /// **判据不写死列数**，只写下限——上限由定价决定，写死就成了"实现指纹"。
    ///
    /// The criterion above only asserts "reached the end and reported a column count", **which does not rule out "one seed column and done"**. The measured sample
    /// request gives `partition_columns=1`, but **it has only one partition** (both positions sit in `DEFAULT`), so 1 is the floor and proves nothing. This one uses a
    /// **two-partition** request: column generation needs at least its own seed per partition, or "converged" covers only one of them. **The criterion states a lower
    /// bound, never an exact count** — the upper bound is decided by pricing, and pinning it would be an implementation fingerprint.
    /// </remarks>
    [Fact]
    public void WithTwoPartitions_ShouldProduceAtLeastOneColumnPerPartition() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with { PartitionId = "FORWARD", LoadingOrder = 0 },
                sample.Positions[1] with { PartitionId = "AFT", LoadingOrder = 1 },
            ],
            SolvePolicy = new SolvePolicy(PartitionColumnGeneration: true),
        };

        ResponseDTO response = Demo2ApplicationService.Solve(
            request,
            StowageMode.FullLoad,
            columnGenerationSolver: new ScipColumnGenerationSolver()).Response;
        var notes = response.Notes ?? [];
        string joined = string.Join(" | ", notes);

        string? columns = notes.FirstOrDefault(note => note.Contains("partition_columns="));
        columns.Should().NotBeNull("the chain must reach the end to report a column count; notes: " + joined);
        int count = int.Parse(
            columns!.Split('=')[1].Split(';')[0],
            System.Globalization.CultureInfo.InvariantCulture);
        count.Should().BeGreaterThanOrEqualTo(2,
            "two partitions need at least their own seed columns; a single column cannot cover both; notes: " + joined);
    }
#endif
}
