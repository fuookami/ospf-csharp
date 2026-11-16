// 方案渲染器 / The solution renderer.

using System;
using System.Collections.Generic;
using System.Globalization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;

/// <summary>
/// 把配载方案渲染成输出 DTO / Render a stowage solution as the output DTO.
/// </summary>
/// <remarks>
/// 逐条对齐的 `Solution.render`：
/// **三个来源的舱位取并集**（分配表、谓词重量表、推荐重量表），按**甲板序号 → 装载顺序号 → 舱位 id 的字符串字典序**排好，
/// 每个舱位给出 id、名称、货物 id 列表与两个重量。
///
/// **三处沿用而不是"顺手改好"**：
/// 1. 第三级排序键是 `id.ToString`，即**字符串字典序**——`"10"` 排在 `"2"` 前面。看着像 bug，但它是行为；
/// 的 `LoadingOrderOutputExporter` 出于同样的理由沿用，两处因此一致。
/// 2. 货物 id 列表**排序后输出**，而不是按分配顺序——`.sorted` 就是这个意思。
/// 3. 重量用 `unit.symbol ?? unit.name`，**单位缺失时留空**而不是编一个默认单位。
///
/// **一处必须偏离**：`Position.id` 是强类型，C# 的 `StowagePosition` 借用 `AircraftPosition.Id`，而 `Item` **仍然只有 `Name` 没有 `Id`**，
/// 所以货物标识用的是名称。这是 `Item` 缺 `Id` 的后果，已记入缺口；两件同名的货会在渲染里显示成同一个标识。
///
/// `Solution.render`: **take the union of three sources** (assignments, predicate weights, recommended weights), sort by
/// **deck ordinal → loading-order number → the position id's string order**, and emit id, name, item ids and both weights per position.
///
/// **Three things copied rather than "fixed while at it"**: (1) the third key is `id.ToString`, i.e. **lexicographic** — `"10"` sorts before `"2"`. It looks like
/// a bug, but it is the code behaviour; the earlier `LoadingOrderOutputExporter` copies it for the same reason, so the two stay consistent. (2) the item ids are
/// **sorted before output**, which is what its `.sorted` means. (3) the weight uses `unit.symbol ?? unit.name`, **left blank when the unit is
/// missing** rather than inventing a default.
///
/// **One deliberate divergence**: its `Position.id` is strongly typed while this implementation borrows `AircraftPosition.Id`, and `Item` **still has only
/// `Name` and no `Id`**, so the item identifier is the name. That is a consequence of the missing `Item.Id`, recorded as a gap; two items with the same name
/// would render as the same identifier.
/// </remarks>
public sealed class SolutionRenderer
{
    /// <summary>
    /// 从解值直接渲染，失败时返回 <c>null</c> / Render straight from the solver values, returning <c>null</c> on failure.
    /// </summary>
    /// <param name="values">求解器解值 / The solver values.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="aggregation">配载聚合，缺失时无法分析 / The stowage aggregation; nothing can be analysed without it.</param>
    /// <returns>渲染 DTO，或 <c>null</c> / The render DTO, or <c>null</c>.</returns>
    /// <remarks>
    /// **渲染失败不翻转求解结论**：求解成功而渲染分析不出来时，调用方拿到的仍是 `Optimal` 响应加上一个 `null` 渲染，
    /// 而不是把整次求解判成失败。渲染是附带的输出，**它的失败不该吞掉已经算出来的方案**。
    ///
    /// **A render failure does not flip the solve verdict**: when the solve succeeded but the analysis cannot be rendered, the caller still gets the `Optimal`
    /// response with a `null` render rather than the whole solve being judged a failure. Rendering is an add-on output, and **its failure must not swallow a plan that
    /// was already computed**.
    /// </remarks>
    public static RenderDTO? RenderSolution(
        IReadOnlyList<Flt64> values,
        LinearMetaModel<Flt64> model,
        StowageAggregation? aggregation) {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(model);
        if (aggregation is null) {
            return null;
        }
        Result<Solution, ErrorCode, Error<ErrorCode>> analyzed = new SolutionAnalyzer(aggregation).Invoke(values, model);
        return analyzed is Ok<Solution, ErrorCode, Error<ErrorCode>> ok ? new SolutionRenderer().Invoke(ok.Value) : null;
    }

    /// <summary>
    /// 渲染方案 / Render the solution.
    /// </summary>
    /// <param name="solution">配载方案 / The stowage solution.</param>
    /// <returns>渲染 DTO / The render DTO.</returns>
    public RenderDTO Invoke(Solution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        // **三个来源取并集**：只从分配表出发会漏掉"没有装货但有重量"的舱位，而那正是谓词/推荐重量模式下的常态。
        // **Union of the three sources**: starting from the assignment table alone would drop positions that hold no cargo yet carry a weight, which is exactly
        // the normal case under the predicate and recommended weight modes.
        List<StowagePosition> positions = new();
        HashSet<StowagePosition> seen = new();
        foreach (ItemAssignment assignment in solution.Assignments) {
            if (seen.Add(assignment.Position)) {
                positions.Add(assignment.Position);
            }
        }
        foreach (StowagePosition position in solution.PredicateLoadWeight.Keys) {
            if (seen.Add(position)) {
                positions.Add(position);
            }
        }
        foreach (StowagePosition position in solution.RecommendedLoadWeight.Keys) {
            if (seen.Add(position)) {
                positions.Add(position);
            }
        }

        positions.Sort(Compare);

        Dictionary<StowagePosition, List<string>> items = new();
        foreach (ItemAssignment assignment in solution.Assignments) {
            // **只收已配载的**：`Stowed == false` 表示这条分配是模型的候选而不是解里的结果，渲染出来会凭空多出货物。
            // **Only stowed assignments**: `Stowed == false` means the assignment is a model candidate rather than part of the solution, and rendering it would
            // invent cargo.
            if (!assignment.Stowed) {
                continue;
            }
            if (!items.TryGetValue(assignment.Position, out List<string>? names)) {
                names = new List<string>();
                items[assignment.Position] = names;
            }
            names.Add(assignment.Item.Name);
        }

        List<RenderPositionDTO> rows = new();
        foreach (StowagePosition position in positions) {
            List<string> itemIds = items.TryGetValue(position, out List<string>? names) ? names : new List<string>();
            itemIds.Sort(StringComparer.Ordinal);
            rows.Add(new RenderPositionDTO(
                PositionId: position.AircraftPosition.Id.ToString(CultureInfo.InvariantCulture),
                PositionName: position.Name,
                ItemIds: itemIds,
                PredicateLoadWeight: ToRenderWeight(position, solution.PredicateLoadWeight),
                RecommendedLoadWeight: ToRenderWeight(position, solution.RecommendedLoadWeight)));
        }

        return new RenderDTO(rows);
    }

    /// <summary>
    /// 甲板序号 → 装载顺序号 → 舱位 id / Deck ordinal, then loading order, then position id.
    /// </summary>
    /// <remarks>
    /// **装载顺序缺失时排在最后**（而不是当成 0）：缺顺序是数据问题，把它当 0 会让它悄悄排到最前面，
    /// 看起来像"这条最先装"。
    ///
    /// **A missing loading order sorts last** rather than counting as zero: a missing order is a data problem, and treating it as zero would quietly move it to
    /// the front, looking like "this one loads first".
    /// </remarks>
    private static int Compare(StowagePosition left, StowagePosition right) {
        Position leftPosition = left.AircraftPosition;
        Position rightPosition = right.AircraftPosition;

        int leftDeck = leftPosition.LoadingOrder is { } leftOrder ? (int)leftOrder.Location : -1;
        int rightDeck = rightPosition.LoadingOrder is { } rightOrder ? (int)rightOrder.Location : -1;
        int byDeck = leftDeck.CompareTo(rightDeck);
        if (byDeck != 0) {
            return byDeck;
        }

        ulong leftNumber = leftPosition.LoadingOrder is { } leftLoading ? (ulong)leftLoading.Order : ulong.MaxValue;
        ulong rightNumber = rightPosition.LoadingOrder is { } rightLoading ? (ulong)rightLoading.Order : ulong.MaxValue;
        int byOrder = leftNumber.CompareTo(rightNumber);
        if (byOrder != 0) {
            return byOrder;
        }

        return string.CompareOrdinal(
            leftPosition.Id.ToString(CultureInfo.InvariantCulture),
            rightPosition.Id.ToString(CultureInfo.InvariantCulture));
    }

    private static RenderWeightDTO? ToRenderWeight(
        StowagePosition position,
        IReadOnlyDictionary<StowagePosition, Quantity<Flt64>> weights) {
        if (!weights.TryGetValue(position, out Quantity<Flt64>? weight)) {
            return null;
        }
        string? unit = weight.Unit.Symbol ?? weight.Unit.Name;
        return new RenderWeightDTO(weight.Value.ToDouble(), unit);
    }
}
