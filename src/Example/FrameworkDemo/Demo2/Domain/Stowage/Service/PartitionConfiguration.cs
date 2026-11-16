// 分区配置：把请求里的分区与「下游局部变量」交给列生成 / Partition configuration: hand the request's partitions and downstream local variables to column generation.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.PartitionLoading;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using PositionStatus = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.PositionStatus;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;

/// <summary>
/// 从请求与聚合推出列生成要的两份输入 / Derive the two inputs column generation needs from the request and the aggregation.
/// </summary>
/// <remarks>
/// 对齐基线 `LoadingContextFactory.partitionConfiguration`，**逐字对应两件事**：
///
/// | 产物 | 来源 | 基线原话 |
/// | --- | --- | --- |
/// | `LoadingPartition` 列表 | `input.positions` 按 `partitionId` 分组 | `input.positions.groupBy { it.partitionId }` |
/// | `PartitionDecision` 列表 | `aggregation.positions` 里 `predicateWeightNeeded` / `recommendedWeightNeeded` 的舱位 | `position.status.predicateWeightNeeded` / `recommendedWeightNeeded` |
///
/// **为什么这一层值得单独存在**： 起 `partitionColumnGeneration` 在 demo2 里被**显式拒绝**（`NotYetPorted`），
/// 理由是"注册与分区配置没接起来"。注册那一半由 `DiscreteLoadingContext.Register` 提供，**缺的就是这一半**。
/// 把它做成**纯函数**（不碰求解器、不碰模型）后，那一半能被判据钉住，而剩下的求解器接线仍需真实后端。
///
/// Aligned with its `partitionConfiguration`, **one for one**: the `LoadingPartition` list comes from grouping `input.positions` by `partitionId`, and the
/// `PartitionDecision` list from the positions whose status needs a predicate or a recommended weight. **Why this layer earns its own file**: since then
/// `partitionColumnGeneration` is **explicitly refused** in demo2 (`NotYetPorted`) on the grounds that "the registration and the partition configuration are not wired".
/// The registration half comes from `DiscreteLoadingContext.Register`; **this is the missing half**. Made a **pure function** (no solver, no model), that half becomes
/// criterion-backed, while the remaining solver wiring still needs a real backend.
/// </remarks>
internal static class PartitionConfiguration
{
    /// <summary>
    /// 构造分区与下游局部变量 / Build the partitions and the downstream local variables.
    /// </summary>
    /// <param name="input">请求 / The request.</param>
    /// <param name="aggregation">配载聚合 / The stowage aggregation.</param>
    /// <returns>分区与局部变量，或结构化失败 / The partitions and local variables, or a structured failure.</returns>
    /// <remarks>
    /// **两种失败都是"请求本身不自洽"，不是求解失败**：
    ///
    /// 1. **舱位没有分区归属**。`owners.getValue(position.spaceName)` 在这里会抛 `NoSuchElementException`——**抛异常不是契约**，
    ///    所以改成结构化失败并点名是哪个舱位。
    /// 2. **一个分区里一个舱位都没有**。分组结果里出现空集合意味着 `partitionId` 全是空串一类的输入，交下去只会得到一个空主问题。
    ///
    /// **Both failures are "the request contradicts itself", not solve failures**: (1) **a position has no partition owner** — its
    /// `owners.getValue(position.spaceName)` would throw `NoSuchElementException`, and **throwing is not a contract**, so this reports a structured failure naming the
    /// position; (2) **a partition owns no position**, which means inputs like all-blank `partitionId`s and would only yield an empty master problem downstream.
    /// </remarks>
    public static Result<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>> Build(
        RequestDTO input,
        StowageAggregation aggregation)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(aggregation);

        // 顺序按**首次出现**，不按字典序：分区的编号会进日志与 `partition_columns=` 备注，排序换个顺序就是换一份可读性。
        // Order follows **first appearance**, not alphabetical: partition ids reach the log and the `partition_columns=` note, and re-sorting changes readability.
        var positionsByPartition = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var partitionOrder = new List<string>();
        var ownerByPosition = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PositionInput position in input.Positions ?? [])
        {
            if (string.IsNullOrWhiteSpace(position.PartitionId))
            {
                return Results.Failed<PartitionConfigurationResult>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "舱位缺少分区归属 / position has no partition: " + position.Name));
            }
            if (!positionsByPartition.TryGetValue(position.PartitionId, out List<string>? owned))
            {
                owned = [];
                positionsByPartition[position.PartitionId] = owned;
                partitionOrder.Add(position.PartitionId);
            }
            owned.Add(position.Name);
            ownerByPosition[position.Name] = position.PartitionId;
        }

        if (partitionOrder.Count == 0)
        {
            return Results.Failed<PartitionConfigurationResult>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "请求没有任何舱位，无法分区 / the request has no positions to partition"));
        }

        var partitions = new List<LoadingPartition>(partitionOrder.Count);
        foreach (string id in partitionOrder)
        {
            partitions.Add(new LoadingPartition(id, new HashSet<string>(positionsByPartition[id], StringComparer.Ordinal)));
        }

        // **`aggregation.Positions` 的顺序就是 `Load.Y`/`Load.Z` 的下标顺序**（`SolutionAnalyzer.CollectWeights` 也这么读），
        // 所以这里必须遍历它、而不是遍历 `input.Positions`，否则变量下标与舱位对不上。
        //
        // **`aggregation.Positions` order is the index order of `Load.Y`/`Load.Z`** (as `SolutionAnalyzer.CollectWeights` reads them), so the loop must walk it rather
        // than `input.Positions`, or the variable indices no longer line up with the positions.
        IReadOnlyList<StowagePosition> positions = aggregation.Positions;
        URealVariable1? predicateWeight = aggregation.Load.Y;
        UIntVariable1? recommendedWeight = aggregation.Load.Z;

        var decisions = new List<PartitionDecision>();
        for (int index = 0; index < positions.Count; ++index)
        {
            StowagePosition position = positions[index];
            PositionStatus status = position.Status;
            if (!status.PredicateWeightNeeded && !status.RecommendedWeightNeeded)
            {
                continue;
            }
            if (!ownerByPosition.TryGetValue(position.Name, out string? owner))
            {
                return Results.Failed<PartitionConfigurationResult>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "聚合里的舱位不在请求分区中 / the aggregated position is not in any requested partition: " + position.Name));
            }

            if (status.PredicateWeightNeeded)
            {
                if (predicateWeight is null)
                {
                    return Results.Failed<PartitionConfigurationResult>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "谓词装载重量变量未注册 / the predicate load-weight variable is not registered"));
                }
                decisions.Add(new PartitionDecision("estimated:" + position.Name, owner, predicateWeight[index]));
            }
            if (status.RecommendedWeightNeeded)
            {
                if (recommendedWeight is null)
                {
                    return Results.Failed<PartitionConfigurationResult>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "推荐装载重量变量未注册 / the recommended load-weight variable is not registered"));
                }
                decisions.Add(new PartitionDecision("recommended:" + position.Name, owner, recommendedWeight[index]));
            }
        }

        return Results.Ok(new PartitionConfigurationResult(partitions, decisions));
    }
}

/// <summary>
/// 分区配置的两份产物 / The two products of the partition configuration.
/// </summary>
/// <param name="Partitions">显式分区 / The explicit partitions.</param>
/// <param name="Decisions">下游局部变量归属 / Downstream local variable ownership.</param>
public sealed record PartitionConfigurationResult(
    IReadOnlyList<LoadingPartition> Partitions,
    IReadOnlyList<PartitionDecision> Decisions);
