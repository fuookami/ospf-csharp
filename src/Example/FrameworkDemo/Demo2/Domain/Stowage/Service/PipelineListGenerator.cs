#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;

/// <summary>
/// 配载管线列表生成器 / The stowage pipeline list generator.
/// </summary>
/// <remarks>
/// 按装载模式与两个重量标志组装 11—14 条约束管线。**无条件的是 11 条**，第 4、5 条各按一个标志加，第 12 条按模式加。**顺序固定**，
/// 其中两条是条件性的：
///
/// | 位置 | 管线 | 条件 |
/// | --- | --- | --- |
/// | 1 | `ItemAssignmentLimit` | — |
/// | 2 | `AppointmentLimit` | — |
/// | 3 | `StowageLimit` | — |
/// | 4 | `PredicateLoadWeightLimit` | 任一舱位需要谓词重量 |
/// | 5 | `RecommendLoadWeightLimit` | 任一舱位需要推荐重量 |
/// | 6 | `LoadAmountLimit` | — |
/// | 7 | `LoadWeightLimit` | — |
/// | 8 | `EmptyForbiddenLimit` | — |
/// | 9 | `NormalBulkDestinationAssignmentLimit` | — |
/// | 10 | `AOGMATBulkConflictLimit` | — |
/// | 11 | `ELDAdjacentLimit` | — |
/// | 12 | `LoadingOrderLimit` | **仅预配载模式** |
/// | 13 | `BiologicalAdjacentLimit` | — |
/// | 14 | `BiologicalBulkConflictLimit` | — |
///
/// 三处容易做错：
///
/// 1. **两条条件管线的条件不同**：第 4 条看 `PredicateWeightNeeded`，第 5 条看 `RecommendedWeightNeeded`。用同一个条件会多算或少算一族符号。
/// 2. **第 12 条只在预配载模式加**。它在全配载模式下也会"成功"运行，但约束的是"怎么装"，那在一次性装载里没有意义。
/// 3. **第 11 与 13 用的邻接类型不同**：ELD 用 `Physics`，生物相邻用 `IndirectPhysics`。用同一个会让其中一族约束落在错误的邻接上。
///
/// 11 to 14 pipelines assembled by stowage mode and the two weight flags — **eleven unconditional**, the fourth and fifth each gated on one flag, the twelfth on the mode — **in a fixed order**, tabulated above. Three easy mistakes: **the two conditional pipelines test
/// different conditions** — the fourth looks at `PredicateWeightNeeded` and the fifth at `RecommendedWeightNeeded`, and sharing one condition adds or drops a symbol family; **the
/// twelfth is added only in predistribution mode** (it "succeeds" in full-load mode too, but constrains *how* to load, which is meaningless for a one-shot load); and **the eleventh
/// and thirteenth use different neighbour types** — `Physics` for ELD and `IndirectPhysics` for the biological adjacency, and sharing one puts a whole family of constraints on the
/// wrong adjacency.
/// </remarks>
internal static class PipelineListGenerator
{
    /// <summary>
    /// 生成管线列表 / Generate the pipeline list.
    /// </summary>
    /// <param name="aggregation">配载聚合 / The stowage aggregation.</param>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <returns>管线列表或失败 / The pipeline list or a failure.</returns>
    public static Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> Invoke(
        StowageAggregation aggregation,
        StowageMode stowageMode) {
        ArgumentNullException.ThrowIfNull(aggregation);

        IReadOnlyList<Neighbour> physics = NeighboursOf(aggregation, NeighbourType.Physics);
        IReadOnlyList<Neighbour> indirectPhysics = NeighboursOf(aggregation, NeighbourType.IndirectPhysics);
        IReadOnlyList<Neighbour> topologicalOrder = NeighboursOf(aggregation, NeighbourType.TopologicalLoadingOrder);

        var pipelines = new List<Func<LinearMetaModel<Flt64>, Try>>();

        var itemAssignment = new ItemAssignmentLimit(aggregation.Items, aggregation.Stowage);
        pipelines.Add(itemAssignment.Invoke);

        var appointment = new AppointmentLimit(aggregation.Items, aggregation.Positions, aggregation.Appointment, aggregation.Stowage);
        pipelines.Add(appointment.Invoke);

        var stowageLimit = new StowageLimit(aggregation.Items, aggregation.Positions, aggregation.Stowage);
        pipelines.Add(stowageLimit.Invoke);

        // 第 4 条：看 `PredicateWeightNeeded`。
        // The fourth: keyed on `PredicateWeightNeeded`.
        if (AnyPosition(aggregation, position => position.Status.PredicateWeightNeeded)) {
            var predicate = new PredicateLoadWeightLimit(aggregation.Positions, aggregation.Load);
            pipelines.Add(predicate.Invoke);
        }

        // 第 5 条：看 `RecommendedWeightNeeded`——与第 4 条**不同**的条件。
        // The fifth: keyed on `RecommendedWeightNeeded`, a **different** condition from the fourth.
        if (AnyPosition(aggregation, position => position.Status.RecommendedWeightNeeded)) {
            var recommend = new RecommendLoadWeightLimit(aggregation.Positions, aggregation.Load);
            pipelines.Add(recommend.Invoke);
        }

        var loadAmount = new LoadAmountLimit(aggregation.Positions, aggregation.Load);
        pipelines.Add(loadAmount.Invoke);

        var loadWeight = new LoadWeightLimit(aggregation.Positions, aggregation.Load, aggregation.MaxLoadWeight);
        pipelines.Add(loadWeight.Invoke);

        var emptyForbidden = new EmptyForbiddenLimit(aggregation.Positions, aggregation.Load);
        pipelines.Add(emptyForbidden.Invoke);

        var normalBulk = new NormalBulkDestinationAssignmentLimit(aggregation.Items, aggregation.Positions, aggregation.Stowage);
        pipelines.Add(normalBulk.Invoke);

        var aogmat = new AOGMATBulkConflictLimit(aggregation.Items, aggregation.Positions, aggregation.Stowage);
        pipelines.Add(aogmat.Invoke);

        var eld = new ELDAdjacentLimit(aggregation.Items, aggregation.Positions, physics, aggregation.Stowage);
        pipelines.Add(eld.Invoke);

        // 第 12 条：**仅预配载模式**。
        // The twelfth: **predistribution mode only**.
        if (stowageMode == StowageMode.Predistribution) {
            var loadingOrder = new LoadingOrderLimit(aggregation.Positions, topologicalOrder, aggregation.Load);
            pipelines.Add(loadingOrder.Invoke);
        }

        var biologicalAdjacent = new BiologicalAdjacentLimit(
            aggregation.Items, aggregation.Positions, indirectPhysics, aggregation.BiologicalLimit, aggregation.Stowage);
        pipelines.Add(biologicalAdjacent.Invoke);

        var biologicalBulk = new BiologicalBulkConflictLimit(
            aggregation.Items, aggregation.Positions, aggregation.BiologicalLimit, aggregation.Stowage);
        pipelines.Add(biologicalBulk.Invoke);

        return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(pipelines);
    }

    private static bool AnyPosition(StowageAggregation aggregation, Func<Model.StowagePosition, bool> predicate) {
        foreach (Model.StowagePosition position in aggregation.Positions) {
            if (predicate(position)) {
                return true;
            }
        }
        return false;
    }

    // `aggregation.neighbours[type]!!`——缺失时抛 `NullPointerException`。这里返回空表：邻接缺失意味着
    // **那一族约束不存在**，而空邻接表的管线本来就一行都不加，语义与"没有邻接"一致，比让调用方收一个异常更有用。
    //
    // The code uses `aggregation.neighbours[type]!!`, throwing on absence. This returns an empty list: a missing adjacency means **that family of constraints does not exist**,
    // and a pipeline over an empty list adds nothing anyway, which matches "no adjacency" better than handing the caller an exception.
    private static IReadOnlyList<Neighbour> NeighboursOf(StowageAggregation aggregation, NeighbourType type) =>
        aggregation.Neighbours.TryGetValue(type, out IReadOnlyList<Neighbour>? list)
            ? list
            : Array.Empty<Neighbour>();
}
