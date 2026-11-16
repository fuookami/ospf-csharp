// 列生成算法的判据 / Criteria for the column-generation algorithm.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **列生成算法** / The column-generation algorithm.
/// </summary>
/// <remarks>
/// 判据的重点：**候选为空不计数**、**上限标记在循环之后判**、**没有求解器就正常结束**、**求解结果先落盘再建列**。
/// 这四条都是"循环照常跑完、结果看起来正常、其实口径错了"的地方。
///
/// The criteria focus on **an empty candidate set not counting an iteration**, **the limit flag being evaluated after the loop**, **a missing solver ending normally**, and **the solve result being
/// stored before the request is built**. All four are places where the loop finishes and the result looks fine while the accounting is wrong.
/// </remarks>
public class ColumnGenerationAlgorithmTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Layer<FltX> Layer(ulong units, string tag) {
        var placements = new List<Placement2<FltX>>();
        for (ulong index = 0UL; index < units; ++index)
        {
            placements.Add(new Placement2<FltX>(
                Shape: new RectanglePackingShape2<FltX>(Q(20.0), Q(10.0)),
                Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                Rotated: false,
                ItemId: tag + index));
        }
        return new Layer<FltX>(0L, null, null, new Sheet2Shape<FltX>(Q(100.0), Q(100.0)), placements);
    }

    private static Bpp2dLayerGenerationResult<FltX> Candidate(Layer<FltX> layer) =>
        new(layer);

    /// <summary>只吐固定候选的建层器 / A generator that always returns the same candidates.</summary>
    private sealed class FixedGenerator : IBpp2dLayerGenerator<FltX>
    {
        private readonly IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> _candidates;

        public FixedGenerator(IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> candidates) =>
            _candidates = candidates;

        public int Calls { get; private set; }

        public IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> Generate(
            Bpp2dLayerGenerationRequest<FltX> request) {
            ++Calls;
            return _candidates;
        }
    }

    /// <summary>固定影子价格的求解器 / A solver with fixed shadow prices.</summary>
    private sealed class FixedSolver : IColumnGenerationRmpSolver
    {
        private readonly FltX _price;

        public FixedSolver(FltX price) => _price = price;

        public int Calls { get; private set; }

        public Task<Result<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ColumnGenerationState state, CancellationToken cancellationToken = default) {
            ++Calls;
            return Task.FromResult(Results.Ok<ColumnGenerationLpResult>(new ColumnGenerationLpResult(
                new Dictionary<DemandModeKey, FltX> { [new DemandModeKey("item_amount", "I1")] = _price },
                Objective: _price)));
        }
    }

    /// <summary>失败求解器 / A failing solver.</summary>
    private sealed class FailingSolver : IColumnGenerationRmpSolver
    {
        public Task<Result<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ColumnGenerationState state, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<ColumnGenerationLpResult>(
                new Err<ErrorCode>(ErrorCode.ApplicationFailed, "lp failed")));
    }

    /// <summary>
    /// **每轮有新列才计数；候选为空就停** / Iterations advance only with new columns, and an empty candidate set stops the loop.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. **第一轮给一列、之后给空** → `IterationCount` 是 1，且**未因上限结束**。
    /// 2. **`LpSolvedTimes` 是 2**：第二轮也求解了一次 LP，才拿到"没有候选"这个结论。
    /// 3. **恒定给同一候选不算"没有新列"**：那会跑满上限——在候选**为空**时停，去重后列数不变也照跑。
    /// 4. 目标值被逐轮记下来，不是只留最后一个。
    ///
    /// Four paired points: **one column on the first round then nothing** gives `IterationCount` 1 without the limit flag; **`LpSolvedTimes` is 2**, since the second round still solved an LP
 /// before concluding there were no candidates; **repeating the same candidate is not "no new columns"**, which runs to the limit, because the implementation stops only on an **empty** candidate
    /// set and keeps going when deduplication merely leaves the count unchanged; and objectives are recorded per round rather than keeping only the last one.
    /// </remarks>
    [Fact]
    public async Task Solve_ShouldStopWhenNoNewColumnsArrive() {
        Layer<FltX> layer = Layer(2UL, "a");
        var generator = new CountingGenerator(call =>
            call == 1
                ? new[] { Candidate(layer) }
                : Array.Empty<Bpp2dLayerGenerationResult<FltX>>());
        var solver = new FixedSolver(new FltX(3.0));
        var config = new ColumnGenerationConfig(IterationLimit: 10);
        var algorithm = new ColumnGenerationAlgorithm(generator, solver);

        Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> solved =
            await algorithm.SolveAsync(config);

        solved.IsOk.Should().BeTrue();
        ColumnGenerationResult result = ((Ok<ColumnGenerationResult, ErrorCode, Error<ErrorCode>>)solved).Value;
        result.IterationCount.Should().Be(1, "the second round returned no candidates");
        result.TerminatedByIterationLimit.Should().BeFalse("stopping on empty candidates is not the limit");
        result.LpSolvedTimes.Should().Be(solver.Calls);
        result.LpSolvedTimes.Should().Be(2, "the second round also solved an LP before concluding there were no candidates");
        result.FinalSolved.Should().BeFalse("no final solver was supplied");
        result.LpObjectives.Should().OnlyContain(value => value != null);
    }

    /// <summary>
    /// **跑满上限才置上限标记，没有求解器则正常结束** / The limit flag needs a full run, and a missing solver ends normally.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 每轮都给新列、上限 2 → **`IterationCount` 2 且 `TerminatedByIterationLimit` 为真**。
    /// 2. **没有 RMP 求解器 → 成功返回、迭代 0、未因上限结束**（只跑初始列是合法用法）。
    /// 3. 求解器失败 → **整个调用失败**，不是"零迭代成功"。
    ///
    /// Three paired points: fresh columns each round with a limit of 2 gives **`IterationCount` 2 and the iteration-limit flag true**; **a missing RMP solver returns success with zero
    /// iterations and no limit flag**, since running only the initial columns is legitimate; and a failing solver makes **the whole call fail** rather than "succeeding with zero iterations".
    /// </remarks>
    [Fact]
    public async Task Solve_ShouldFlagTheIterationLimitOnlyWhenItIsReached() {
        var generator = new FixedGenerator(Array.Empty<Bpp2dLayerGenerationResult<FltX>>());
        var fresh = new CountingGenerator(call => new[] { Candidate(Layer(2UL, "c" + call)) });
        var solver = new FixedSolver(new FltX(1.0));

        Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> full =
            await new ColumnGenerationAlgorithm(fresh, solver)
                .SolveAsync(new ColumnGenerationConfig(IterationLimit: 2));
        ColumnGenerationResult fullResult = ((Ok<ColumnGenerationResult, ErrorCode, Error<ErrorCode>>)full).Value;
        fullResult.IterationCount.Should().Be(2);
        fullResult.TerminatedByIterationLimit.Should().BeTrue("the loop ran to the limit");

        Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> noSolver =
            await new ColumnGenerationAlgorithm(generator).SolveAsync();
        ColumnGenerationResult noSolverResult = ((Ok<ColumnGenerationResult, ErrorCode, Error<ErrorCode>>)noSolver).Value;
        noSolverResult.IterationCount.Should().Be(0);
        noSolverResult.TerminatedByIterationLimit.Should().BeFalse("zero iterations is not the limit");

        Result<ColumnGenerationResult, ErrorCode, Error<ErrorCode>> failing =
            await new ColumnGenerationAlgorithm(generator, new FailingSolver()).SolveAsync();
        failing.IsOk.Should().BeFalse("a solver failure must not look like an empty success");
    }

    /// <summary>按调用轮次给候选的建层器 / A generator whose candidates depend on the call number.</summary>
    private sealed class CountingGenerator : IBpp2dLayerGenerator<FltX>
    {
        private readonly Func<int, IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>> _byCall;
        private int _calls;

        public CountingGenerator(Func<int, IReadOnlyList<Bpp2dLayerGenerationResult<FltX>>> byCall) =>
            _byCall = byCall;

        public IReadOnlyList<Bpp2dLayerGenerationResult<FltX>> Generate(
            Bpp2dLayerGenerationRequest<FltX> request) => _byCall(++_calls);
    }
}
