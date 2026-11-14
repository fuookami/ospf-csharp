// 分区列生成服务的判据 / Criteria for the partition column generation service.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Application;
using Fuookami.Ospf.Framework.Bpp.Domain.PartitionLoading;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Application;

/// <summary>
/// **分区列生成服务的守卫与错误改写** / Guards and error re-shaping of the partition column generation service.
/// </summary>
/// <remarks>
/// 循环本身需要**真实求解器**才能有判据（LP 迭代 + 局部定价 + 加列），**那些判据尚未落地，见计划文档**。
/// 本文件覆盖**不需要求解器**的那一层：预算守卫、批次定价的显式拒绝、以及"受限主问题不可行"的改写。
///
/// 传 `null!` 作为求解器是**故意的**：守卫排在任何求解之前，**若守卫失效，判据会因为空引用而变红**，
/// 而不是静默走通。这一条比写一个五方法桩更省，也更容易看穿。
///
/// The loop itself needs **a real solver** to be criterion-backed; **those criteria are not written yet**, see the plan document. This file covers the layer that
/// **needs no solver**. Passing `null!` as the solver is **deliberate**: the guards run before any solve, so **if a guard stops working the criterion goes red with a
/// null-reference instead of silently passing** — cheaper and more transparent than a five-member stub.
/// </remarks>
public class PartitionColumnGenerationServiceTests {
    /// <summary>**无效预算必须在碰模型之前就拒掉** / An invalid budget must be refused before the model is touched.</summary>
    [Fact]
    public async Task SolveAsync_ShouldRefuseAnInvalidBudget() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> registration = Fixtures.Register();
        registration.IsOk.Should().BeTrue(registration.ToString());

        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new PartitionColumnGenerationApplicationService<Flt64>(null!)
                .SolveAsync(
                    ((Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>)registration).Value,
                    Fixtures.TwoPartitions,
                    new PartitionColumnGenerationConfig(IterationLimit: 0));

        result.IsFailed.Should().BeTrue("zero iterations is not a budget");
        ((Failed<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**列数上限装不下每个分区一列时必须拒** / A column budget that cannot hold one column per partition must be refused.</summary>
    [Fact]
    public async Task SolveAsync_ShouldRefuseAColumnBudgetBelowThePartitionCount() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> registration = Fixtures.Register();
        registration.IsOk.Should().BeTrue(registration.ToString());

        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new PartitionColumnGenerationApplicationService<Flt64>(null!)
                .SolveAsync(
                    ((Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>)registration).Value,
                    Fixtures.TwoPartitions,
                    new PartitionColumnGenerationConfig(ColumnLimit: 1));

        result.IsFailed.Should().BeTrue("two partitions cannot share a single column");
        ((Failed<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**批次定价已接上：现状不是拒绝而是跑通** / Batch pricing is wired now: it runs, it is not refused.</summary>
    /// <remarks>
    /// 判据按缺口翻面：原来的"必须明确拒绝"钉的是缺口，缺口补上之后它**必须变红并改成"确实跑通"**——
    /// 保研式的判据会把缺口钉死。逐位一致由 `BatchPricing_ShouldMatchPerPartitionPricing` 负责。
    ///
    /// The criterion flipped with the gap: the old "must be refused" pinned the gap, and once it closed it **had to go red and become "it really runs"** — a nail-like criterion keeps
    /// the gap forever. Exact agreement is covered by `BatchPricing_ShouldMatchPerPartitionPricing`.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRunWithBatchPricing() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> registration = Fixtures.Register();
        registration.IsOk.Should().BeTrue(registration.ToString());

        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new PartitionColumnGenerationApplicationService<Flt64>(
                    new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver())
                .SolveAsync(
                    ((Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>)registration).Value,
                    Fixtures.TwoPartitions,
                    new PartitionColumnGenerationConfig(BatchPricingEnabled: true));

        result.IsOk.Should().BeTrue(result.ToString());
    }

    /// <summary>**受限主问题不可行不得冒充原问题不可行** / An infeasible restricted master must not pose as original infeasibility.</summary>
    /// <remarks>
    /// 报 `ORModelInfeasible` 会让调用方**以为原问题无解**，而它可能只是列不够。
    /// **这是本文件唯一覆盖真实分支的判据。**
    ///
    /// Reporting `ORModelInfeasible` would let callers **conclude the original has no solution** when it may simply lack columns. **This is the only criterion here
    /// covering a real branch.**
    /// </remarks>
    [Fact]
    public void InfeasibleOr_ShouldReShapeOnlyModelInfeasibility() {
        Result<Success, ErrorCode, Error<ErrorCode>> infeasible =
            Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ORModelInfeasible, "empty pool"));

        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> reshaped =
            PartitionColumnGenerationApplicationService<Flt64>.InfeasibleOr(infeasible);

        reshaped.IsFailed.Should().BeTrue();
        ((Failed<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)reshaped).Error.Code
            .Should().Be(ErrorCode.OREngineSolvingException,
                "an empty column pool does not prove the original is infeasible");
        ((Failed<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)reshaped).Error.Message
            .Should().Contain("未证明");

        // 其他错误码原样透出——**改写只针对那一个码**。
        // Every other code passes through: **the re-shaping targets that one code only.**
        Result<Success, ErrorCode, Error<ErrorCode>> timeout =
            Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.OREngineSolvingException, "timeout"));
        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> kept =
            PartitionColumnGenerationApplicationService<Flt64>.InfeasibleOr(timeout);
        kept.IsFailed.Should().BeTrue();
        ((Failed<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)kept).Error.Message
            .Should().Be("timeout", "a solver exception must not be relabelled as a pool problem");

        // 致命错误保持致命——折叠成普通失败会丢掉"这不是普通失败"这个信息。
        // A fatal error stays fatal: folding it into an ordinary failure drops the "this is not an ordinary failure" signal.
        Result<Success, ErrorCode, Error<ErrorCode>> fatal = new Fatal<Success, ErrorCode, Error<ErrorCode>>(
            new List<Error<ErrorCode>> { new Err<ErrorCode>(ErrorCode.ApplicationError, "fatal") });
        PartitionColumnGenerationApplicationService<Flt64>.InfeasibleOr(fatal).Should()
            .BeOfType<Fatal<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Fatal<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)
            PartitionColumnGenerationApplicationService<Flt64>.InfeasibleOr(fatal)).Errors
            .Should().ContainSingle();
    }

#if SCIP_INTEGRATION
    /// <summary>**端到端：真求解器跑完整条链** / End to end: the whole chain on a real solver.</summary>
    /// <remarks>
    /// 这是列生成链路**唯一的行为判据**：种子 → LP 迭代 → 定价 → 加列 → 最终整数主问题 → 复核收口。
    /// 它至少钉住三件事：真的跑到了、有列进池、以及**收口时完整模型复核没把结果拒掉**。
    ///
    /// This is the chain's **only behavioural criterion**: seed → LP iteration → pricing → column addition → final integer master → verified close-out. It pins three
    /// things at least: it actually ran, columns entered the pool, and **the full-model re-check did not reject the result at close-out**.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRunTheWholeChainOnARealSolver() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> registration = Fixtures.Register();
        registration.IsOk.Should().BeTrue(registration.ToString());

        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new PartitionColumnGenerationApplicationService<Flt64>(
                    new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver())
                .SolveAsync(
                    ((Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>)registration).Value,
                    Fixtures.TwoPartitions,
                    new PartitionColumnGenerationConfig(IterationLimit: 8, ColumnLimit: 16));

        result.IsOk.Should().BeTrue(result.ToString());
        PartitionColumnGenerationOutcome<Flt64> outcome =
            ((Ok<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        outcome.ColumnCount.Should().BeGreaterThan(
            Fixtures.TwoPartitions.Count - 1,
            "seeding must give every partition at least one column");
        outcome.Trace.Should().NotBeEmpty("next to nothing else proves the LP loop actually ran");
        outcome.Loading.Solution.Should().NotBeNull("the close-out must produce a domain solution");
        outcome.Loading.Path.Should().Be("partition_column_generation");
    }

    /// <summary>**LP 未收敛时必须如实报受限池解** / An unconverged LP must be reported as a restricted-pool solution.</summary>
    /// <remarks>
    /// 预算只给一次迭代，链必然停在半路。**这是"未收敛"那条变异的唯一杀手**：把状态一律写成 `Feasible`，
    /// 会让调用方**以为拿到了整数最优解**，而它只是这一池列能凑出来的最好解。
    ///
    /// One iteration is all the budget allows, so the chain necessarily stops halfway. **This is the only killer for the "unconverged" mutant**: labelling the status
    /// `Feasible` would let callers **believe they hold an integer optimum** when they hold the best this column pool can assemble.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldReportAnUnconvergedRestrictedResult() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> registration = Fixtures.Register();
        registration.IsOk.Should().BeTrue(registration.ToString());

        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new PartitionColumnGenerationApplicationService<Flt64>(
                    new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver())
                .SolveAsync(
                    ((Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>)registration).Value,
                    Fixtures.TwoPartitions,
                    new PartitionColumnGenerationConfig(IterationLimit: 1, ColumnLimit: 8));

        result.IsOk.Should().BeTrue(result.ToString());
        PartitionColumnGenerationOutcome<Flt64> outcome =
            ((Ok<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        outcome.LpConverged.Should().BeFalse("one iteration cannot prove convergence");
        outcome.Loading.Status.Should().Be("RestrictedFeasible",
            "an unproven convergence must not be reported as a proven one");
        outcome.Loading.FallbackReason.Should().NotBeNullOrEmpty(
            "the caller must be able to see why the result is only pool-feasible");
    }

    /// <summary>**列数上限恰好等于分区数时受限主问题不可行，且不得冒充原问题不可行** / With a budget of exactly one column per partition the restricted master is infeasible, and that must not pose as original infeasibility.</summary>
    /// <remarks>
    /// **这是实测推翻一个想法的算例**：先前以为"每个分区一列种子"就保证池可行。**不是**——
    /// 货物必须恰好装进一个舱位，而每个分区只有一列时主问题**凑不出这样的组合**。
    ///
    /// 于是这条判据走的是**真实的不可行分支**：必须报 `OREngineSolvingException` 并注明"未证明"，
    /// **不能报 `ORModelInfeasible`**——那会让调用方以为原问题无解。
    ///
    /// **This fixture overturned an assumption**: one seed column per partition was believed to guarantee a feasible pool. **It does not** — the item must sit in exactly one
    /// position, and with one column per partition the master **cannot assemble such a combination**. The criterion therefore exercises the **real infeasibility branch**:
    /// it must report `OREngineSolvingException` with "unproven", **never `ORModelInfeasible`**, which would let callers conclude the original is unsolvable.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldNotPoseAThinPoolAsOriginalInfeasibility() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> registration = Fixtures.Register();
        registration.IsOk.Should().BeTrue(registration.ToString());

        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new PartitionColumnGenerationApplicationService<Flt64>(
                    new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver())
                .SolveAsync(
                    ((Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>)registration).Value,
                    Fixtures.TwoPartitions,
                    new PartitionColumnGenerationConfig(IterationLimit: 4, ColumnLimit: 2));

        result.IsFailed.Should().BeTrue("one column per partition cannot cover the demand");
        ((Failed<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.OREngineSolvingException, "a thin pool does not prove the original is infeasible");
        ((Failed<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("未证明");
    }

    /// <summary>**列数上限被尊重** / The column budget is respected.</summary>
    /// <remarks>
    /// 上限略高于分区数：加列最多用到上限为止。**超出上限会让主问题规模失控**——在真求解器上表现为求解时间暴涨，不是报错。
    ///
    /// The budget sits just above the partition count, so additions may use it up to the limit. **Exceeding it lets the master grow without bound** — on a real solver that
    /// shows up as explosive solve time, not as an error.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldRespectTheColumnBudget() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> registration = Fixtures.Register();
        registration.IsOk.Should().BeTrue(registration.ToString());

        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new PartitionColumnGenerationApplicationService<Flt64>(
                    new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver())
                .SolveAsync(
                    ((Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>)registration).Value,
                    Fixtures.TwoPartitions,
                    new PartitionColumnGenerationConfig(IterationLimit: 4, ColumnLimit: 3));

        result.IsOk.Should().BeTrue(result.ToString());
        PartitionColumnGenerationOutcome<Flt64> outcome =
            ((Ok<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        outcome.ColumnCount.Should().BeLessThanOrEqualTo(3, "the column budget is three");
    }

    /// <summary>**可行性阶段目标归零时必须跳阶段且不加列** / When the phase-one objective reaches zero the phase must be left without adding a column.</summary>
    /// <remarks>
    /// 这一支的标记是唯一的：`PhaseOne == true` **且** `AddedColumns == 0` **且** `PricingProven == false`。
    /// 其他任何组合都说明走的不是这支。
    ///
    /// 若这一支**从不出现**，说明它在当前夹具下不可达——**那也是结论**，不能把"没跑到"写成"跑过了"。
    ///
    /// This branch has a unique marker: `PhaseOne == true` **and** `AddedColumns == 0` **and** `PricingProven == false`. Any other combination comes from elsewhere.
    /// If it **never appears**, it is unreachable under this fixture — **that too is a result**; "did not run" must not be written as "passed".
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldLeaveTheFeasibilityPhaseWithoutAddingAColumn() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> registration = Fixtures.Register();
        registration.IsOk.Should().BeTrue(registration.ToString());

        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new PartitionColumnGenerationApplicationService<Flt64>(
                    new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver())
                .SolveAsync(
                    ((Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>)registration).Value,
                    Fixtures.TwoPartitions,
                    new PartitionColumnGenerationConfig(IterationLimit: 6, ColumnLimit: 6));

        result.IsOk.Should().BeTrue(result.ToString());
        PartitionColumnGenerationOutcome<Flt64> outcome =
            ((Ok<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;

        // **恰好一次**，且其后必须有 `PhaseOne == false` 的条目：只"看到标记"不够——
        // 不清除阶段标志的实现会让每一轮都留下标记，**看起来同样顺利**。
        //
        // **Exactly once**, and a `PhaseOne == false` entry must follow: seeing the marker is not enough — an implementation that never clears the flag leaves a marker
        // every round and **looks equally fine**.
        int markers = 0;
        int phaseExitIndex = -1;
        for (int index = 0; index < outcome.Trace.Count; ++index) {
            PartitionColumnGenerationIteration entry = outcome.Trace[index];
            if (entry.PhaseOne && entry.AddedColumns == 0 && !entry.PricingProven) {
                markers += 1;
                phaseExitIndex = index;
            }
        }
        markers.Should().Be(1, "trace: " + string.Join(" | ", outcome.Trace));
        phaseExitIndex.Should().BeLessThan(outcome.Trace.Count - 1,
            "the phase exit must not be the last entry: " + string.Join(" | ", outcome.Trace));
        outcome.Trace[outcome.Trace.Count - 1].PhaseOne.Should().BeFalse(
            "the chain must keep pricing after leaving the feasibility phase");
    }
#endif

    /// <summary>夹具 / Fixtures.</summary>
    /// <summary>
    /// **批次定价打开后，结果必须与逐分区定价一致** / With batch pricing on, the result must match per-partition pricing.
    /// </summary>
    /// <remarks>
    /// 三点成对：两条路径都成功 / **两条路径的目标值相同** / 两条路径的列数相同。
    /// 这条判据是批次定价的**唯一说服力**：一批里先建好所有分区的局部模型，再连着解——**省的是建模型与求解器的重复准备，
    /// 不是数学**。所以结果必须逐位相同；不相同就说明影子价格或局部模型在两次之间变了。
    ///
    /// Three paired points: both paths succeed; **their objectives agree**; and their column counts agree. This is batch pricing's **only claim to anything**: building every
    /// partition's local model up front and then solving in one round **saves repeated model/solver preparation, not mathematics**. So the results must agree exactly; a mismatch
    /// means the shadow prices or a local model changed in between.
    /// </remarks>
    [Fact]
    public async Task BatchPricing_ShouldMatchPerPartitionPricing() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> registration = Fixtures.Register();
        registration.IsOk.Should().BeTrue(registration.ToString());

        var loop = new PartitionColumnGenerationConfig(IterationLimit: 8, ColumnLimit: 16);
        var batched = new PartitionColumnGenerationConfig(IterationLimit: 8, ColumnLimit: 16, BatchPricingEnabled: true);
        var service = new PartitionColumnGenerationApplicationService<Flt64>(
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver());

        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> loopResult =
            await service.SolveAsync(
                ((Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>)registration).Value,
                Fixtures.TwoPartitions,
                loop);
        Result<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>> batchedResult =
            await service.SolveAsync(
                ((Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>)registration).Value,
                Fixtures.TwoPartitions,
                batched);

        loopResult.IsOk.Should().BeTrue(loopResult.ToString());
        batchedResult.IsOk.Should().BeTrue(
            batchedResult.ToString(),
            "batch pricing is wired now; a refusal here means the wiring regressed");

        PartitionColumnGenerationOutcome<Flt64> loopOutcome =
            ((Ok<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)loopResult).Value;
        PartitionColumnGenerationOutcome<Flt64> batchedOutcome =
            ((Ok<PartitionColumnGenerationOutcome<Flt64>, ErrorCode, Error<ErrorCode>>)batchedResult).Value;

        batchedOutcome.Loading.Objective.ToDouble().Should().Be(
            loopOutcome.Loading.Objective.ToDouble(),
            "a batch changes when the models are built, never the mathematics");
        batchedOutcome.ColumnCount.Should().Be(loopOutcome.ColumnCount);
    }

    private static class Fixtures {
        /// <summary>两个分区，各占一个舱位 / Two partitions owning one position each.</summary>
        internal static readonly IReadOnlyList<LoadingPartition> TwoPartitions = new List<LoadingPartition> {
            new("A", new HashSet<string> { "P1" }),
            new("B", new HashSet<string> { "P2" }),
        };

        /// <summary>构造一份已注册的完整模型 / Build a registered full model.</summary>
        /// <returns>注册结果 / The registration.</returns>
        internal static Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> Register() {
            DiscreteLoadingProblem<Flt64> problem = new(
                Items: new List<LoadingItem<Flt64>> {
                    new("I1", Kilograms(1.0), Required: true, FixedPositionId: null),
                },
                Positions: new List<LoadingPosition<Flt64>> {
                    new("P1", MaxCount: 5UL, MaxWeight: Kilograms(5.0), FixedOccupied: false, Enabled: true),
                    new("P2", MaxCount: 5UL, MaxWeight: Kilograms(5.0), FixedOccupied: false, Enabled: true),
                },
                AllowedAssignments: new HashSet<AssignmentKey> { new("I1", "P1"), new("I1", "P2") },
                Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
                LayoutGroups: new List<LayoutSelectionGroup>(),
                WeightUnit: SIBaseUnits.Kilogram);

            var loading = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
            var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64>();
            Try registered = loading.Register(model);
            if (registered is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Results.Failed<RegisteredDiscreteLoading<Flt64>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "fixture registration failed"));
            }
            return Results.Ok(new RegisteredDiscreteLoading<Flt64>(loading, model));
        }

        /// <summary>构造千克量 / Build a kilogram quantity.</summary>
        /// <param name="value">数值 / Value.</param>
        /// <returns>带单位的量 / The unit-carrying quantity.</returns>
        private static Quantity<Flt64> Kilograms(double value) =>
            new(new Flt64(value), SIBaseUnits.Kilogram);
    }
}
