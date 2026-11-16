// 搜索记忆与接受策略测试 / Tests for search memory and acceptance policies.

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 搜索记忆 / The search memory.
/// </summary>
public sealed class SearchMemoryTests {
    /// <summary>**单目标档案按支配排序并截断到容量** / The single-objective archive sorts by dominance and truncates to capacity.</summary>
    [Fact]
    public void Merged_ShouldSortAndTruncateForSingleObjective() {
        SearchMemory<double, Flt64> memory = BuildMemory(capacity: 2, workingCapacity: 2);

        IReadOnlyList<IIndividual<double, Flt64>> merged = memory.Merged(
            Array.Empty<IIndividual<double, Flt64>>(),
            new[] { Individual(3.0), Individual(1.0), Individual(2.0) });

        merged.Count.Should().Be(2);
        merged[0].Objective.Should().Be(1.0);
        merged[1].Objective.Should().Be(2.0);
    }

    /// <summary>**多目标下互不支配的解全部保留** / Multi-objective search keeps every mutually non-dominating solution.</summary>
    [Fact]
    public void Merged_ShouldKeepMutuallyNonDominating() {
        var dominance = new ParetoDominance(new[] { true, true });
        SearchMemory<double, Flt64> memory = BuildMultiMemory(dominance, capacity: 8, LineCoordinate);

        // 线上四点的坐标是 (1,4)/(2,3)/(3,2)/(4,1)，两两互不支配。
        // The four points on the line have coordinates (1,4)/(2,3)/(3,2)/(4,1) and are pairwise non-dominating.
        IReadOnlyList<IIndividual<double, Flt64>> merged = memory.Merged(
            Array.Empty<IIndividual<double, Flt64>>(),
            new[] { Individual(1.0), Individual(2.0), Individual(3.0), Individual(4.0) });

        merged.Count.Should().Be(4, "互不支配的解都必须在第一前沿 / every non-dominating solution belongs to the first front");
    }

    /// <summary>**多目标下被支配的解被剔除** / Multi-objective search drops dominated solutions.</summary>
    [Fact]
    public void Merged_ShouldDropDominatedForMultiObjective() {
        var dominance = new ParetoDominance(new[] { true, true });
        SearchMemory<double, Flt64> memory = BuildMultiMemory(dominance, capacity: 8, SquareCoordinate);

        // 坐标 (x, x)：(1,1) 支配 (2,2) 与 (3,3)。
        // Coordinates (x, x): (1,1) dominates (2,2) and (3,3).
        IReadOnlyList<IIndividual<double, Flt64>> merged = memory.Merged(
            Array.Empty<IIndividual<double, Flt64>>(),
            new[] { Individual(1.0), Individual(2.0), Individual(3.0) });

        merged.Count.Should().Be(1);
        merged[0].Objective.Should().Be(1.0);
    }

    /// <summary>**合并时按解向量去重** / Merging deduplicates by solution vector.</summary>
    /// <remarks>
    /// 跳过去重会让同一个解被计入多次，**贡献度因此虚高**。
    /// Skipping deduplication counts one solution several times and **inflates the contribution**.
    /// </remarks>
    [Fact]
    public void Merged_ShouldDeduplicateBySolution() {
        SearchMemory<double, Flt64> memory = BuildMemory(capacity: 8, workingCapacity: 8);

        IReadOnlyList<IIndividual<double, Flt64>> merged = memory.Merged(
            new[] { Individual(1.0) },
            new[] { Individual(1.0), Individual(2.0) });

        merged.Count.Should().Be(2);
    }

    /// <summary>**贡献比例的分母是候选数** / The contribution ratio divides by the candidate count.</summary>
    /// <remarks>
    /// 用档案规模会让候选很少时贡献度虚高。
    /// Using the archive size inflates the contribution when there are few candidates.
    /// </remarks>
    [Fact]
    public void Contribution_ShouldDivideByCandidateCount() {
        SearchMemory<double, Flt64> memory = BuildMemory(capacity: 8, workingCapacity: 8);

        // 两个候选都优于空档案 → 单目标下 novel = 1，比例 = 1 / 2。
        // Both candidates beat the empty archive → novel = 1 in single-objective search, ratio = 1 / 2.
        (bool improved, double ratio) = memory.Contribution(
            Array.Empty<IIndividual<double, Flt64>>(),
            new[] { Individual(1.0), Individual(2.0) });

        improved.Should().BeTrue();
        ratio.Should().BeApproximately(0.5, 1e-12);
    }

    /// <summary>**不可行候选不进入档案** / Infeasible candidates never enter the archive.</summary>
    /// <remarks>
    /// 跳过可行性过滤会让不可行解进入档案，而档案规模与最优值都正常。
    /// Skipping the feasibility filter lets infeasible solutions into the archive while its size and best value look normal.
    /// </remarks>
    [Fact]
    public void Merged_ShouldRejectInfeasibleCandidates() {
        SearchMemory<double, Flt64> memory = BuildMemory(
            capacity: 8, workingCapacity: 8, admissible: item => item.Objective < 5.0);

        IReadOnlyList<IIndividual<double, Flt64>> merged = memory.Merged(
            Array.Empty<IIndividual<double, Flt64>>(),
            new[] { Individual(1.0), Individual(9.0) });

        merged.Count.Should().Be(1);
        merged[0].Objective.Should().Be(1.0);
    }

    /// <summary>**`Offer` 返回档案是否被改善** / `Offer` reports whether the archive improved.</summary>
    [Fact]
    public void Offer_ShouldReportImprovement() {
        SearchMemory<double, Flt64> memory = BuildMemory(capacity: 8, workingCapacity: 8);

        memory.Offer(new[] { Individual(5.0) }).Should().BeTrue();
        memory.Offer(new[] { Individual(9.0) }).Should().BeFalse(
            "更差的候选不改善档案 / a worse candidate does not improve the archive");
        memory.Archive[0].Objective.Should().Be(5.0);
    }

    /// <summary>**`Restore` 通过 `merged` 重建档案** / `Restore` rebuilds the archive through `merged`.</summary>
    /// <remarks>
    /// 直接赋值会让一份含重复个体的快照原样进入档案。
    /// Direct assignment would let a snapshot containing duplicates enter the archive verbatim.
    /// </remarks>
    [Fact]
    public void Restore_ShouldRebuildThroughMerged() {
        SearchMemory<double, Flt64> memory = BuildMemory(capacity: 8, workingCapacity: 8);

        memory.Restore(
            new[] { Individual(1.0), Individual(1.0), Individual(2.0) },
            new[] { Individual(1.0), Individual(2.0), Individual(3.0) });

        memory.Archive.Count.Should().Be(2, "重复个体必须被合并 / duplicates must be merged");
        memory.Working.Count.Should().Be(3);
    }

    /// <summary>**工作集不超过容量** / The working set never exceeds its capacity.</summary>
    [Fact]
    public void Offer_ShouldRespectWorkingCapacity() {
        SearchMemory<double, Flt64> memory = BuildMemory(capacity: 8, workingCapacity: 2);

        // **必须逐步变好才会被接受**：单目标最小化下，更差的候选被工作集里的成员支配，`NotWorse` 直接拒绝。
        // 第一版按 1→2→3 递增提交，工作集只留下 1 个，`Count` 断言因此失败。
        // **Candidates must improve each time to be accepted**: under single-objective minimization a worse candidate is dominated by a
        // working-set member and `NotWorse` rejects it. The first version offered 1 then 2 then 3, leaving only one entry.
        memory.Offer(new[] { Individual(3.0) });
        memory.Offer(new[] { Individual(2.0) });
        memory.Offer(new[] { Individual(1.0) });

        memory.Working.Count.Should().Be(2);
    }

    /// <summary>构造单目标记忆 / Build a single-objective memory.</summary>
    /// <param name="capacity">档案容量 / The archive capacity.</param>
    /// <param name="workingCapacity">工作集容量 / The working-set capacity.</param>
    /// <param name="admissible">合法性判据 / The admissibility predicate.</param>
    /// <returns>记忆 / The memory.</returns>
    private static SearchMemory<double, Flt64> BuildMemory(
        int capacity,
        int workingCapacity,
        Func<IIndividual<double, Flt64>, bool>? admissible = null) {
        var dominance = new ParetoDominance(new[] { true });
        return new SearchMemory<double, Flt64>(
            capacity, workingCapacity,
            new NotWorseAcceptancePolicy<double, Flt64>(dominance, Coordinate),
            multiObjective: false, dominance, Coordinate,
            admissible ?? (_ => true));
    }

    /// <summary>构造多目标记忆 / Build a multi-objective memory.</summary>
    /// <param name="dominance">支配判据 / The dominance predicate.</param>
    /// <param name="capacity">档案容量 / The archive capacity.</param>
    /// <returns>记忆 / The memory.</returns>
    private static SearchMemory<double, Flt64> BuildMultiMemory(
        IDominance dominance,
        int capacity,
        Func<double, IReadOnlyList<Flt64>> coordinates) =>
        new(capacity, capacity,
            new NotWorseAcceptancePolicy<double, Flt64>(dominance, coordinates),
            multiObjective: true, dominance, coordinates, _ => true);

    /// <summary>单目标坐标 / The single-objective coordinate.</summary>
    /// <param name="objective">目标 / The objective.</param>
    /// <returns>坐标 / The coordinates.</returns>
    private static IReadOnlyList<Flt64> Coordinate(double objective) => new[] { new Flt64(objective) };

    /// <summary>线上坐标 `(x, 4 - x)` / Coordinates on the line `(x, 4 - x)`.</summary>
    /// <param name="objective">目标 / The objective.</param>
    /// <returns>坐标 / The coordinates.</returns>
    private static IReadOnlyList<Flt64> LineCoordinate(double objective) =>
        new[] { new Flt64(objective), new Flt64(4.0 - objective) };

    /// <summary>对角坐标 `(x, x)` / Diagonal coordinates `(x, x)`.</summary>
    /// <param name="objective">目标 / The objective.</param>
    /// <returns>坐标 / The coordinates.</returns>
    private static IReadOnlyList<Flt64> SquareCoordinate(double objective) =>
        new[] { new Flt64(objective), new Flt64(objective) };

    /// <summary>构造个体 / Build an individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<double, Flt64> Individual(double objective) =>
        new SolutionWithFitness<double, Flt64>(new[] { new Flt64(objective) }, objective, objective);
}

/// <summary>
/// 自动接受策略 / The automatic acceptance policy.
/// </summary>
public sealed class AutoAcceptanceTests {
    /// <summary>**跨度只在恰好经过 `capacity` 个决策时调整** / The span only adjusts at exact capacity multiples.</summary>
    /// <remarks>
    /// 每步都调整会让跨度在两个值之间抖动，延迟窗口失去意义。
    /// Adjusting every step makes the span oscillate between two values and the delay window meaningless.
    /// </remarks>
    [Fact]
    public void Accept_ShouldAdjustSpanOnlyAtCapacityBoundary() {
        AutoAcceptance<double, Flt64> acceptance = BuildAcceptance(horizon: 64);
        int capacity = acceptance.Capacity;
        int initial = acceptance.HistorySpan;

        // 持续改进：改进占比 >= 1/4 → 跨度减半。
        // Keep improving: the improvement share reaches a quarter → the span halves.
        for (int index = 0; index < capacity - 1; index++) {
            acceptance.Accept(new[] { Individual(10.0 - index) }, Individual(9.0 - index));
        }
        acceptance.HistorySpan.Should().Be(initial,
            "尚未到容量边界，跨度不得变化 / the capacity boundary has not been reached");

        acceptance.Accept(new[] { Individual(10.0 - capacity + 1) }, Individual(9.0 - capacity + 1));
        acceptance.HistorySpan.Should().Be(global::System.Math.Max(initial / 2, 2));
    }

    /// <summary>**历史入队的是"接受则候选、否则当前"** / The history enqueues the candidate when accepted, else the current objective.</summary>
    /// <remarks>
    /// 总是记录候选会让延迟比较基准变成一串从未被接受的解，**接受率因此与配置无关地漂移**。
    /// Always recording the candidate makes the delayed baseline a run of never-accepted solutions, and **the acceptance rate drifts
    /// independently of the configuration**.
    /// </remarks>
    [Fact]
    public void Accept_ShouldRecordNonAcceptedCurrent() {
        AutoAcceptance<double, Flt64> acceptance = BuildAcceptance(horizon: 64);
        IIndividual<double, Flt64>[] working = { Individual(1.0) };

        // 候选 9 被 1 支配，也不被延迟基准接受 → 历史应记录当前目标 1.0。
        // Candidate 9 is dominated by 1 and not accepted by the delayed baseline → the history records the current objective 1.0.
        bool accepted = acceptance.Accept(working, Individual(9.0));

        accepted.Should().BeFalse();
        Result<JsonElement, ErrorCode, Error<ErrorCode>> saved = acceptance.Save();
        saved.Should().BeOfType<Ok<JsonElement, ErrorCode, Error<ErrorCode>>>();
        JsonElement state = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)saved).Value;
        state.GetProperty("history").GetRawText().Should().Contain("1",
            "被拒绝时候选目标不得进入历史 / a rejected candidate must not enter the history");
    }

    /// <summary>**`load` 校验改进数上界** / `load` validates the improvement bound.</summary>
    [Fact]
    public void Load_ShouldRejectImpossibleImprovementCount() {
        AutoAcceptance<double, Flt64> acceptance = BuildAcceptance(horizon: 64);
        acceptance.Accept(new[] { Individual(1.0) }, Individual(2.0));
        Result<JsonElement, ErrorCode, Error<ErrorCode>> saved = acceptance.Save();
        JsonElement state = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)saved).Value;

        // 篡改 improvements 为一个不可能的值（远大于 decisions % capacity）。
        // Tamper with improvements to an impossible value (far above decisions % capacity).
        JsonElement tampered = JsonSerializer.SerializeToElement(new {
            version = 1,
            horizon = acceptance.Horizon,
            capacity = acceptance.Capacity,
            span = acceptance.HistorySpan,
            decisions = 1L,
            improvements = 99,
            history = state.GetProperty("history"),
        });

        acceptance.Load(tampered).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**快照往返一致** / Snapshots round-trip.</summary>
    [Fact]
    public void Save_ShouldRoundTrip() {
        AutoAcceptance<double, Flt64> acceptance = BuildAcceptance(horizon: 64);
        for (int index = 0; index < 5; index++) {
            acceptance.Accept(new[] { Individual(10.0 - index) }, Individual(9.0 - index));
        }

        JsonElement state = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)acceptance.Save()).Value;
        AutoAcceptance<double, Flt64> restored = BuildAcceptance(horizon: 64);
        restored.Load(state).Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        restored.Decisions.Should().Be(acceptance.Decisions);
        restored.HistorySpan.Should().Be(acceptance.HistorySpan);
    }

    /// <summary>**视野不匹配的快照被拒绝** / A snapshot with a mismatched horizon is rejected.</summary>
    [Fact]
    public void Load_ShouldRejectMismatchedHorizon() {
        AutoAcceptance<double, Flt64> source = BuildAcceptance(horizon: 64);
        JsonElement state = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)source.Save()).Value;

        BuildAcceptance(horizon: 4096).Load(state)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造自动接受 / Build an automatic acceptance.</summary>
    /// <param name="horizon">视野 / The horizon.</param>
    /// <returns>策略 / The policy.</returns>
    private static AutoAcceptance<double, Flt64> BuildAcceptance(long horizon) {
        var dominance = new ParetoDominance(new[] { true });
        return new AutoAcceptance<double, Flt64>(
            SearchCodecs.List(new DoubleCodec()), dominance, Coordinate, horizon);
    }

    /// <summary>单目标坐标 / The single-objective coordinate.</summary>
    /// <param name="objective">目标 / The objective.</param>
    /// <returns>坐标 / The coordinates.</returns>
    private static IReadOnlyList<Flt64> Coordinate(double objective) => new[] { new Flt64(objective) };

    /// <summary>构造个体 / Build an individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<double, Flt64> Individual(double objective) =>
        new SolutionWithFitness<double, Flt64>(new[] { new Flt64(objective) }, objective, objective);
}

/// <summary>
/// 延迟接受与记录-记录接受 / Late acceptance and record-to-record acceptance.
/// </summary>
public sealed class AdaptiveAcceptanceTests {
    /// <summary>**多目标下延迟接受一律拒绝** / Late acceptance rejects outright in multi-objective search.</summary>
    /// <remarks>
    /// 用一个个体充当基准会让接受判据取决于工作集的排列顺序，**而策略看起来仍在正常工作**。
    /// Using one individual as the code makes acceptance depend on the working-set order **while the policy still looks like it
    /// works**.
    /// </remarks>
    [Fact]
    public void LateAcceptance_ShouldRejectMultiObjective() {
        var dominance = new ParetoDominance(new[] { true });
        var acceptance = new LateAcceptance<double, Flt64>(
            SearchCodecs.List(new DoubleCodec()), dominance, Coordinate, multiObjective: true);

        acceptance.Accept(new[] { Individual(1.0) }, Individual(0.5))
            .Should().BeFalse("多目标下不退化 / no degradation under multi-objective search");
    }

    /// <summary>**延迟历史只在第一次调用时填满** / The late history is filled only on the first call.</summary>
    /// <remarks>
    /// 每次调用都重新填充会让延迟窗口永远等于当前目标，退化成爬山法。
    /// Refilling on every call keeps the delay window equal to the current objective, degenerating into hill climbing.
    /// </remarks>
    [Fact]
    public void LateAcceptance_ShouldFillHistoryOnce() {
        var dominance = new ParetoDominance(new[] { true });
        var acceptance = new LateAcceptance<double, Flt64>(
            SearchCodecs.List(new DoubleCodec()), dominance, Coordinate, multiObjective: false, length: 3);

        acceptance.Accept(new[] { Individual(1.0) }, Individual(0.5));
        acceptance.Accept(new[] { Individual(1.0) }, Individual(0.6));
        acceptance.Accept(new[] { Individual(1.0) }, Individual(0.7));

        // 游标必须随调用前进，才说明历史是原地替换而不是每次重填。
        // The cursor must advance with calls, which shows the history is replaced in place rather than refilled.
        acceptance.Cursor.Should().Be(0, "三次调用后游标回到 0 / the cursor wraps to 0 after three calls");
    }

    /// <summary>**记录-记录容差在 `clock` 超过 `horizon` 后不会变负** / The record-to-record tolerance never turns negative past the horizon.</summary>
    /// <remarks>
    /// 不夹会让 `clock` 超过 `horizon` 之后容差变负，于是**连真正的改进都被拒绝**——搜索彻底停摆，而每一次判断单独看都像"稳定收敛"。
    /// 第一版断言的是"稍差被拒绝"，但未夹紧时同样拒绝，两种实现无法区分；必须断言"改进仍被接受"才有意义。
    /// Without the clamp the tolerance turns negative past the horizon, so **even a real improvement is rejected** — the search stalls
    /// while each individual decision looks like stable convergence. The first version asserted "slightly worse is rejected", which holds
    /// under both implementations; asserting "an improvement is still accepted" is what distinguishes them.
    /// </remarks>
    [Fact]
    public void RecordToRecord_ShouldAcceptImprovementAfterHorizon() {
        var acceptance = new RecordToRecordAcceptance<double, Flt64>(
            Coordinate, multiObjective: false, initialRatio: 0.5, horizon: 2);

        // 走 5 次把 clock 推过 horizon=2；记录为 10，量级为 10。
        // Run five times to push the clock past horizon = 2; the record is 10 and the scale is 10.
        for (int index = 0; index < 5; index++) {
            acceptance.Accept(new[] { Individual(10.0) }, Individual(10.0));
        }

        acceptance.Clock.Should().Be(5);
        acceptance.Best.Should().Be(10.0);
        acceptance.Scale.Should().Be(10.0);
        // decay = clamp(1 - 4/2) = 0 → 容差恰为 0；9 - 10 = -1 <= 0 → 接受。
        // decay = clamp(1 - 4/2) = 0 → the tolerance is exactly 0; 9 - 10 = -1 <= 0 → accepted.
        acceptance.Accept(new[] { Individual(10.0) }, Individual(9.0))
            .Should().BeTrue("容差归零后真正的改进仍须被接受 / a real improvement must still be accepted once the tolerance bottoms out");
        // 未夹紧时容差是 0.5 * (-1) * 10 = -5，上式 -1 <= -5 为假，改进被误拒。
        // Unclamped the tolerance is 0.5 * (-1) * 10 = -5, so -1 <= -5 is false and the improvement is wrongly rejected.
    }

    /// <summary>**记录-记录的量级单调不减** / The record-to-record scale is monotone non-decreasing.</summary>
    /// <remarks>
    /// 用当前批次的量级会让容差随窗口内容忽大忽小，而每一次判断看起来都合理。
    /// Using the current batch's magnitude makes the tolerance lurch with the batch contents while each decision looks reasonable.
    /// </remarks>
    [Fact]
    public void RecordToRecord_ShouldKeepScaleMonotone() {
        var acceptance = new RecordToRecordAcceptance<double, Flt64>(
            Coordinate, multiObjective: false, initialRatio: 0.05, horizon: 1000);

        acceptance.Accept(new[] { Individual(100.0) }, Individual(100.0));
        double first = acceptance.Scale;
        acceptance.Accept(new[] { Individual(1.0) }, Individual(1.0));

        acceptance.Scale.Should().Be(first, "量级不得因小批次而下降 / the scale must not drop with a small batch");
        first.Should().Be(100.0);
    }

    /// <summary>**缺少坐标投影时一律拒绝** / A missing coordinate projection always rejects.</summary>
    [Fact]
    public void RecordToRecord_ShouldRejectWithoutCoordinates() {
        var acceptance = new RecordToRecordAcceptance<double, Flt64>(null);

        acceptance.Accept(new[] { Individual(1.0) }, Individual(0.5)).Should().BeFalse();
    }

    /// <summary>**非单维坐标被拒绝** / Non-scalar coordinates are rejected.</summary>
    [Fact]
    public void RecordToRecord_ShouldRejectMultiDimensionalCoordinates() {
        var acceptance = new RecordToRecordAcceptance<double, Flt64>(
            objective => new[] { new Flt64(objective), new Flt64(objective) });

        acceptance.Accept(new[] { Individual(1.0) }, Individual(0.5))
            .Should().BeFalse("只有一维坐标才能记录-记录比较 / only scalar coordinates support record-to-record comparison");
    }

    /// <summary>单目标坐标 / The single-objective coordinate.</summary>
    /// <param name="objective">目标 / The objective.</param>
    /// <returns>坐标 / The coordinates.</returns>
    private static IReadOnlyList<Flt64> Coordinate(double objective) => new[] { new Flt64(objective) };

    /// <summary>构造个体 / Build an individual.</summary>
    /// <param name="objective">目标值 / The objective value.</param>
    /// <returns>个体 / The individual.</returns>
    private static IIndividual<double, Flt64> Individual(double objective) =>
        new SolutionWithFitness<double, Flt64>(new[] { new Flt64(objective) }, objective, objective);
}

/// <summary>
/// `double` 编解码器 / A `double` codec.
/// </summary>
internal sealed class DoubleCodec : ISearchCodec<double> {
    /// <inheritdoc/>
    public Result<JsonElement, ErrorCode, Error<ErrorCode>> Encode(double value) =>
        Results.Ok(JsonSerializer.SerializeToElement(value));

    /// <inheritdoc/>
    public Result<double, ErrorCode, Error<ErrorCode>> Decode(JsonElement value) =>
        Results.Ok(value.GetDouble());
}
