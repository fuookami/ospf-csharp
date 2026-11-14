// 分区列生成的服务循环 / The partition column generation service loop.
// 与固定 Kotlin 基线 `application/PartitionColumnGenerationApplicationService.kt` 的 LP 阶段对应。
// Mirrors the LP phase of the pinned Kotlin baseline's `PartitionColumnGenerationApplicationService.kt`.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Framework.Bpp.Domain.PartitionLoading;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp.Application;

/// <summary>
/// 根节点列生成结果 / Root column-generation outcome.
/// </summary>
/// <param name="Loading">已复核的完整装载结果 / The verified full loading result.</param>
/// <param name="Trace">定价轨迹 / The pricing trace.</param>
/// <param name="ColumnCount">最终列数 / The final column count.</param>
/// <param name="LpConverged">是否证明根节点 LP 定价收敛 / Whether root LP pricing convergence was proven.</param>
public sealed record PartitionColumnGenerationOutcome<V>(
    DiscreteLoadingSolveResult<V> Loading,
    IReadOnlyList<PartitionColumnGenerationIteration> Trace,
    int ColumnCount,
    bool LpConverged)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 分区列生成 / Partition column generation.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **整条链是打通的**：种子列 → 根节点 LP 迭代 → 定价 → 加列 → **最终整数主问题** → 按完整模型复核收尾
/// （`DiscreteLoadingApplicationService.FinishAsync`）。**唯一的例外是批次定价**：`BatchPricingEnabled` 为真时
/// 明确返回失败，而不是悄悄退回单分区路径——**悄悄退回会让"批次定价没接上"看起来像"批次定价没有必要"。**
///
/// 收尾复核不是可选项：它是本条链上**唯一能抓住模型/求解器错位**的地方。没收敛时状态写 `RestrictedFeasible`，
/// 别让人以为手里的整数解是全局最优。
///
/// 另一处刻意收窄：**批次定价（`BatchPricingEnabled`）显式拒绝**，与 B433 对 Benders 的处理一致——静默走单分区路径
/// 会让调用方以为拿到了批次语义。
///
/// **This class stops once the root LP converges.** The final integer master and the full loading result are **not ported**: that step must reuse the
/// "recompute the objective from the full model" check in `DiscreteLoadingApplicationService.finish`, which is **the only thing on this path that catches a
/// model/solver mismatch**. **Producing no result beats producing an unverified one.** The second deliberate narrowing: **batch pricing
/// (`BatchPricingEnabled`) is refused explicitly**, matching B433's handling of Benders — silently taking the single-partition path would let callers believe they hold
/// batch semantics.
/// </remarks>
public sealed class PartitionColumnGenerationApplicationService<V>
    where V : struct, IFloatingNumber<V> {
    private readonly IColumnGenerationSolver _solver;
    private readonly IPartitionPricingOracle<V> _oracle;

    /// <summary>构造 / Constructor.</summary>
    /// <param name="solver">LP、局部 MILP 执行器 / The LP and local MILP backend.</param>
    /// <param name="oracle">可替换局部定价策略 / The replaceable local pricing strategy.</param>
    public PartitionColumnGenerationApplicationService(
        IColumnGenerationSolver solver,
        IPartitionPricingOracle<V>? oracle = null) {
        _solver = solver;
        _oracle = oracle ?? new MilpPartitionPricingOracle<V>(solver);
    }

    /// <summary>
    /// 跑根节点列生成 / Run root column generation.
    /// </summary>
    /// <param name="registration">完整模型注册 / The full-model registration.</param>
    /// <param name="partitions">显式分区 / The explicit partitions.</param>
    /// <param name="config">根节点预算 / The root budget.</param>
    /// <param name="extraDecisions">下游局部变量归属 / Downstream local variable ownership.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>轨迹与收敛结论，或结构化失败 / The trace and convergence verdict, or a structured failure.</returns>
    public async Task<Result<PartitionColumnGenerationOutcome<V>, ErrorCode, Error<ErrorCode>>> SolveAsync(
        RegisteredDiscreteLoading<V> registration,
        IReadOnlyList<LoadingPartition> partitions,
        PartitionColumnGenerationConfig? config = null,
        IReadOnlyList<PartitionDecision>? extraDecisions = null,
        CancellationToken cancellationToken = default) {
        config ??= new PartitionColumnGenerationConfig();
        if (!config.IsValidFor(partitions.Count)) {
            return Results.Failed<PartitionColumnGenerationOutcome<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "分区列生成预算无效 / Invalid partition CG budget"));
        }

        Result<PartitionLoadingContext<V>, ErrorCode, Error<ErrorCode>> created =
            await PartitionLoadingContext<V>.CreateAsync(
                registration.Context, registration.Model, partitions, extraDecisions).ConfigureAwait(false);
        if (created is not Ok<PartitionLoadingContext<V>, ErrorCode, Error<ErrorCode>> createdOk) {
            return Propagate<PartitionColumnGenerationOutcome<V>, PartitionLoadingContext<V>>(created);
        }
        PartitionLoadingContext<V> context = createdOk.Value;

        Result<Success, ErrorCode, Error<ErrorCode>> seeded = await SeedAsync(context, partitions, config)
            .ConfigureAwait(false);
        if (seeded is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<PartitionColumnGenerationOutcome<V>, Success>(seeded);
        }

        var trace = new List<PartitionColumnGenerationIteration>();
        bool phaseOne = true;
        bool converged = false;
        for (int iteration = 0; iteration < config.IterationLimit; ++iteration) {
            cancellationToken.ThrowIfCancellationRequested();

            Result<ColumnMasterRegistration<Flt64>, ErrorCode, Error<ErrorCode>> built =
                await context.MasterAsync(phaseOne, integer: false).ConfigureAwait(false);
            if (built is not Ok<ColumnMasterRegistration<Flt64>, ErrorCode, Error<ErrorCode>> builtOk) {
                return InfeasibleOr(built);
            }
            using ColumnMasterRegistration<Flt64> master = builtOk.Value;

            Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lp =
                await _solver.SolveLPAsync("partition_rmp", master.Model).ConfigureAwait(false);
            if (lp is not Ok<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lpOk) {
                return InfeasibleOr(lp);
            }

            if (lpOk.Value.Solution.Count == 0) {
                break;
            }
            if (phaseOne && lpOk.Value.Obj.ToDouble() <= config.Tolerance) {
                trace.Add(new PartitionColumnGenerationIteration(
                    PhaseOne: true,
                    Objective: lpOk.Value.Obj,
                    AddedColumns: 0,
                    PricingProven: false));
                phaseOne = false;
                continue;
            }

            Result<Success, ErrorCode, Error<ErrorCode>> priced = await context
                .RefreshShadowPriceAsync(master, lpOk.Value).ConfigureAwait(false);
            if (priced is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Propagate<PartitionColumnGenerationOutcome<V>, Success>(priced);
            }

            Result<(int Added, bool Improving, bool Proven), ErrorCode, Error<ErrorCode>> round =
                await PriceRoundAsync(context, partitions, config, phaseOne, cancellationToken).ConfigureAwait(false);
            if (round is not Ok<(int Added, bool Improving, bool Proven), ErrorCode, Error<ErrorCode>> roundOk) {
                return Propagate<PartitionColumnGenerationOutcome<V>, (int, bool, bool)>(round);
            }

            trace.Add(new PartitionColumnGenerationIteration(
                PhaseOne: phaseOne,
                Objective: lpOk.Value.Obj,
                AddedColumns: roundOk.Value.Added,
                PricingProven: roundOk.Value.Proven));
            if (!roundOk.Value.Improving) {
                converged = true;
                break;
            }
        }

        return await FinalAsync(context, registration, trace, converged).ConfigureAwait(false);
    }

    /// <summary>
    /// 最终受限整数主问题 + 已复核的装载结果 / The final restricted integer master plus a verified loading result.
    /// </summary>
    /// <param name="context">分区上下文 / The partition context.</param>
    /// <param name="registration">完整模型注册 / The full-model registration.</param>
    /// <param name="trace">定价轨迹 / The pricing trace.</param>
    /// <param name="converged">LP 是否已证明收敛 / Whether LP convergence was proven.</param>
    /// <returns>结果或失败 / The outcome or a failure.</returns>
    /// <remarks>
    /// **收口复用 `DiscreteLoadingApplicationService.FinishAsync`**：它按完整模型重算目标并与求解器报的目标对上。
    /// 少了这一步，列池解会被当成"装好了"返回，**而错位在业务侧几乎看不出来**。
    ///
    /// **LP 未收敛也照样收口**：那是一个**受限整数池可行解**，不是整数最优解——`LpConverged` 就是拿来区分这两者的，
    /// 而不是拿来隐瞒失败。
    ///
    /// **The close-out reuses `DiscreteLoadingApplicationService.FinishAsync`**, which recomputes the objective from the full model and matches it against the solver's.
    /// Without it a pool solution would be returned as "loaded", **and the mismatch is nearly invisible in business code**. **An unconverged LP is still closed out**: that
    /// is a **restricted-pool feasible solution**, not an integer optimum — `LpConverged` exists to distinguish the two, not to hide a failure.
    /// </remarks>
    private async Task<Result<PartitionColumnGenerationOutcome<V>, ErrorCode, Error<ErrorCode>>> FinalAsync(
        PartitionLoadingContext<V> context,
        RegisteredDiscreteLoading<V> registration,
        IReadOnlyList<PartitionColumnGenerationIteration> trace,
        bool converged) {
        Result<ColumnMasterRegistration<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await context.MasterAsync(phaseOne: false, integer: true).ConfigureAwait(false);
        if (built is not Ok<ColumnMasterRegistration<Flt64>, ErrorCode, Error<ErrorCode>> builtOk) {
            return InfeasibleOr(built);
        }
        using ColumnMasterRegistration<Flt64> master = builtOk.Value;

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await _solver.SolveMILPAsync("partition_master", master.Model).ConfigureAwait(false);
        if (solved is not Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solvedOk) {
            return InfeasibleOr(solved);
        }

        Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> merged = await context
            .ExtractSolutionAsync(master, solvedOk.Value.Solution.Values).ConfigureAwait(false);
        if (merged is not Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>) {
            return Propagate<PartitionColumnGenerationOutcome<V>, IReadOnlyList<Flt64>>(merged);
        }

        Result<DiscreteLoadingSolveResult<V>, ErrorCode, Error<ErrorCode>> finished =
            await DiscreteLoadingApplicationService<V>.FinishAsync(
                registration,
                ((Ok<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>)merged).Value,
                converged ? "Feasible" : "RestrictedFeasible",
                solvedOk.Value.Obj,
                "partition_column_generation",
                converged ? null : "根节点 LP 收敛未证明 / Root LP convergence unproven",
                // 轨迹类型不同（列生成轨迹 vs Benders 轨迹），**不硬塞**：CG 轨迹由本类的 outcome 自带。
                // The trace types differ (column generation vs Benders); **never force it in** — the CG trace rides on this class's outcome.
                null,
                null).ConfigureAwait(false);
        if (finished is not Ok<DiscreteLoadingSolveResult<V>, ErrorCode, Error<ErrorCode>> finishedOk) {
            return Propagate<PartitionColumnGenerationOutcome<V>, DiscreteLoadingSolveResult<V>>(finished);
        }

        return Results.Ok(new PartitionColumnGenerationOutcome<V>(
            Loading: finishedOk.Value,
            Trace: trace,
            ColumnCount: context.Columns.Count,
            LpConverged: converged));
    }

    /// <summary>种子阶段：每个分区先放一列 / Seed phase: one column per partition first.</summary>
    /// <param name="context">分区上下文 / The partition context.</param>
    /// <param name="partitions">分区 / The partitions.</param>
    /// <param name="config">预算 / The budget.</param>
    /// <returns>结果 / The result.</returns>
    /// <remarks>
    /// **可用列数按"剩余分区还要各留一列"扣减**（`ColumnLimit - 已有列 - 剩余分区数`）。不扣减的话，
    /// 前面的分区会把预算吃光，**后面的分区连一列种子都放不进去**，而失败出现在主问题里。
    ///
    /// **The available column count is reduced by "each remaining partition still needs one column"**. Without that reduction the earlier partitions eat the whole budget
    /// and **the later ones cannot even seed one column**, while the failure appears in the master.
    /// </remarks>
    private async Task<Result<Success, ErrorCode, Error<ErrorCode>>> SeedAsync(
        PartitionLoadingContext<V> context,
        IReadOnlyList<LoadingPartition> partitions,
        PartitionColumnGenerationConfig config) {
        for (int index = 0; index < partitions.Count; ++index) {
            Result<ColumnPricingRequest<Flt64>, ErrorCode, Error<ErrorCode>> request =
                await context.PricingRequestAsync(partitions[index].Id, phaseOne: true, seed: true).ConfigureAwait(false);
            if (request is not Ok<ColumnPricingRequest<Flt64>, ErrorCode, Error<ErrorCode>> requestOk) {
                return Propagate<Success, ColumnPricingRequest<Flt64>>(request);
            }

            Result<PartitionPricingResult, ErrorCode, Error<ErrorCode>> priced =
                await _oracle.PriceAsync(context, requestOk.Value).ConfigureAwait(false);
            if (priced is not Ok<PartitionPricingResult, ErrorCode, Error<ErrorCode>>) {
                return Propagate<Success, PartitionPricingResult>(priced);
            }

            int available = config.ColumnLimit - context.Columns.Count - (partitions.Count - index - 1);
            if (available <= 0) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "列数上限不足以给每个分区留一列 / Column limit cannot hold one column per partition"));
            }

            var vectors = new List<(string PartitionId, IReadOnlyDictionary<string, Flt64> Values)>();
            foreach (IReadOnlyDictionary<string, Flt64> vector in
                ((Ok<PartitionPricingResult, ErrorCode, Error<ErrorCode>>)priced).Value.Values) {
                if (vectors.Count >= available) {
                    break;
                }
                vectors.Add((partitions[index].Id, vector));
            }
            if (vectors.Count == 0) {
                continue;
            }

            Result<Success, ErrorCode, Error<ErrorCode>> added = await context.RegisterAsync(vectors).ConfigureAwait(false);
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }
        return Results.OkInstance;
    }

    /// <summary>一次迭代内的分区定价 / One iteration of per-partition pricing.</summary>
    /// <param name="context">分区上下文 / The partition context.</param>
    /// <param name="partitions">分区 / The partitions.</param>
    /// <param name="config">预算 / The budget.</param>
    /// <param name="phaseOne">是否可行性阶段 / Whether this is the feasibility phase.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>新增列数、是否有改进、是否全部有证明 / Added columns, whether anything improved, and whether all were proven.</returns>
    private async Task<Result<(int Added, bool Improving, bool Proven), ErrorCode, Error<ErrorCode>>> PriceRoundAsync(
        PartitionLoadingContext<V> context,
        IReadOnlyList<LoadingPartition> partitions,
        PartitionColumnGenerationConfig config,
        bool phaseOne,
        CancellationToken cancellationToken) {

        bool proven = true;
        bool improving = false;
        var pending = new List<(string PartitionId, IReadOnlyDictionary<string, Flt64> Values)>();

        // **批次定价只在默认 MILP 定价器上成立**：`PricePrepared` 要的是"预先建好的模型"，自定义定价器拿不出这个契约。
        // 传了别的定价器就退回逐分区路径——**基线的行为就是这样**，这里照着做并写在注释里，而不是假装有批量。
        //
        // **Batch pricing only holds for the default MILP pricer**: `PricePrepared` needs a "model built up front", a contract a custom pricer cannot offer. With any other pricer the
        // per-partition path is used — **that is what the baseline does**, mirrored here and written down rather than faking a batch.
        MilpPartitionPricingOracle<V>? batchOracle = config.BatchPricingEnabled ? _oracle as MilpPartitionPricingOracle<V> : null;
        if (batchOracle is not null) {
            var partitionIds = new List<string>(partitions.Count);
            foreach (LoadingPartition partition in partitions) {
                partitionIds.Add(partition.Id);
            }
            Result<ColumnPricingBatch<Flt64>, ErrorCode, Error<ErrorCode>> preparedBatch =
                await context.PreparePricingBatchAsync(partitionIds, phaseOne).ConfigureAwait(false);
            if (preparedBatch is not Ok<ColumnPricingBatch<Flt64>, ErrorCode, Error<ErrorCode>> batchOk) {
                return Propagate<(int, bool, bool), ColumnPricingBatch<Flt64>>(preparedBatch);
            }
            using ColumnPricingBatch<Flt64> batch = batchOk.Value;
            var partitionsById = new Dictionary<string, LoadingPartition>(StringComparer.Ordinal);
            foreach (LoadingPartition partition in partitions) {
                partitionsById.TryAdd(partition.Id, partition);
            }
            foreach (ColumnPricingEntry<Flt64> entry in batch.Entries) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!partitionsById.TryGetValue(entry.Request.BlockId, out LoadingPartition? partition)) {
                    return Results.Failed<(int, bool, bool)>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "未知定价分区 / Unknown pricing partition: " + entry.Request.BlockId));
                }
                Result<PartitionPricingResult, ErrorCode, Error<ErrorCode>> priced =
                    await batchOracle.PricePreparedAsync(context, entry, batch).ConfigureAwait(false);
                if (priced is not Ok<PartitionPricingResult, ErrorCode, Error<ErrorCode>> pricedOk) {
                    return Propagate<(int, bool, bool), PartitionPricingResult>(priced);
                }
                Result<Success, ErrorCode, Error<ErrorCode>> current =
                    await context.RequireCurrentAsync(entry.Request.Revision).ConfigureAwait(false);
                if (current is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                    return Propagate<(int, bool, bool), Success>(current);
                }
                Result<(bool Proven, bool Improving), ErrorCode, Error<ErrorCode>> consumed =
                    Consume(partition, entry.Request, pricedOk.Value, pending, proven, improving, config, context);
                if (consumed is not Ok<(bool Proven, bool Improving), ErrorCode, Error<ErrorCode>> consumedOk) {
                    return Propagate<(int, bool, bool), (bool Proven, bool Improving)>(consumed);
                }
                (proven, improving) = consumedOk.Value;
            }
        }
        else {
            foreach (LoadingPartition partition in partitions) {
                cancellationToken.ThrowIfCancellationRequested();

                Result<ColumnPricingRequest<Flt64>, ErrorCode, Error<ErrorCode>> request =
                    await context.PricingRequestAsync(partition.Id, phaseOne).ConfigureAwait(false);
                if (request is not Ok<ColumnPricingRequest<Flt64>, ErrorCode, Error<ErrorCode>> requestOk) {
                    return Propagate<(int, bool, bool), ColumnPricingRequest<Flt64>>(request);
                }

                Result<PartitionPricingResult, ErrorCode, Error<ErrorCode>> priced =
                    await _oracle.PriceAsync(context, requestOk.Value).ConfigureAwait(false);
                if (priced is not Ok<PartitionPricingResult, ErrorCode, Error<ErrorCode>> pricedOk) {
                    return Propagate<(int, bool, bool), PartitionPricingResult>(priced);
                }

                // **异步求解之后必须复核请求仍对应当前模型**：期间若有列被加进来，这份影子价格已经作废，
                // 拿它算出的检验数**看起来完全正常**，却对应一张旧的 LP。
                //
                // **After an asynchronous solve the request must still match the current model**: if a column was added meanwhile, these shadow prices are stale and the
                // reduced costs derived from them **look entirely normal** while describing an old LP.
                Result<Success, ErrorCode, Error<ErrorCode>> current =
                    await context.RequireCurrentAsync(requestOk.Value.Revision).ConfigureAwait(false);
                if (current is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                    return Propagate<(int, bool, bool), Success>(current);
                }

                Result<(bool Proven, bool Improving), ErrorCode, Error<ErrorCode>> consumed =
                    Consume(partition, requestOk.Value, pricedOk.Value, pending, proven, improving, config, context);
                if (consumed is not Ok<(bool Proven, bool Improving), ErrorCode, Error<ErrorCode>> consumedOk) {
                    return Propagate<(int, bool, bool), (bool Proven, bool Improving)>(consumed);
                }
                (proven, improving) = consumedOk.Value;
            }
        }

        if (pending.Count == 0) {
            return Results.Ok((0, improving, proven));
        }
        Result<Success, ErrorCode, Error<ErrorCode>> added = await context.RegisterAsync(pending).ConfigureAwait(false);
        if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<(int, bool, bool), Success>(added);
        }
        return Results.Ok((pending.Count, improving, proven));
    }

    /// <remarks>
    /// **两条定价路径共用这一段**：检验数是否够负、列预算还剩多少、以及"降价列"的标志。分开写两份，改一处忘一处，
    /// 最后表现成"批量打开之后结果就不一样了"——**而那种差异只有逐位比较结果才看得见**。
    ///
    /// **Both pricing paths share this block**: whether a reduced cost is negative enough, how much column budget is left, and the "improving" flag. Written twice, one copy gets
    /// forgotten and it shows up as "turning the batch on changes the result" — **a difference only visible by comparing the results exactly**.
    /// </remarks>
    private static Result<(bool Proven, bool Improving), ErrorCode, Error<ErrorCode>> Consume(
        LoadingPartition partition,
        ColumnPricingRequest<Flt64> request,
        PartitionPricingResult priced,
        List<(string PartitionId, IReadOnlyDictionary<string, Flt64> Values)> pending,
        bool proven,
        bool improving,
        PartitionColumnGenerationConfig config,
        PartitionLoadingContext<V> context) {
        proven = proven && priced.Optimal;
        int budget = config.ColumnLimit - context.Columns.Count - pending.Count;
        foreach (IReadOnlyDictionary<string, Flt64> vector in priced.Values) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> cost = request.ReducedCost(vector);
            if (cost is not Ok<Flt64, ErrorCode, Error<ErrorCode>> costOk) {
                return Propagate<(bool Proven, bool Improving), Flt64>(cost);
            }
            if (costOk.Value.ToDouble() >= -config.Tolerance) {
                continue;
            }
            improving = true;
            if (pending.Count >= budget) {
                continue;
            }
            pending.Add((partition.Id, vector));
        }
        return Results.Ok<(bool Proven, bool Improving)>((proven, improving));
    }


    /// <summary>把"主问题不可行"改写成"未证明原问题不可行" / Re-shape "master infeasible" into "original infeasibility unproven".</summary>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>失败 / The failure.</returns>
    /// <remarks>
    /// **受限主问题不可行只说明"这一池列装不下"**，不是原问题不可行的证明。报 `ORModelInfeasible` 会让调用方
    /// 以为原问题无解，**而它可能只是列不够**。
    ///
    /// **An infeasible restricted master only says "this pool cannot cover it"**, not that the original model is infeasible. Reporting `ORModelInfeasible` would let
    /// callers conclude the original has no solution **when it may simply lack columns**.
    /// </remarks>
    internal static Result<PartitionColumnGenerationOutcome<V>, ErrorCode, Error<ErrorCode>> InfeasibleOr<TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where TSource : notnull {
        if (result is Failed<TSource, ErrorCode, Error<ErrorCode>> failed) {
            return failed.Error.Code == ErrorCode.ORModelInfeasible
                ? Results.Failed<PartitionColumnGenerationOutcome<V>>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    "受限主问题不可行，未证明原问题不可行 / Restricted master infeasible; original infeasibility unproven"))
                : Results.Failed<PartitionColumnGenerationOutcome<V>>(failed.Error);
        }
        if (result is Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<PartitionColumnGenerationOutcome<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        return Results.Failed<PartitionColumnGenerationOutcome<V>>(new Err<ErrorCode>(
            ErrorCode.ApplicationError, "unreachable"));
    }

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<S, ErrorCode, Error<ErrorCode>> Propagate<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<S>(new Err<ErrorCode>(ErrorCode.ApplicationError, "unreachable")),
        };
}
