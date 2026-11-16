#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 配载方案。Stowage solution with cargo-position assignments.
/// </summary>
/// <remarks>
/// 与对齐：三张表——**货物→舱位的分配**、**谓词装载重量**、**推荐装载重量**。
/// 后两张只含"变量为正"的舱位，因为零意味着该舱位不使用这个模式，把它放进结果会让下游误以为有值。
///
/// Three tables — **item-to-position assignments**, **predicate load weights** and **recommended load weights**. The latter two hold only positions whose
/// variable is positive, because a zero means the position does not use that mode and including it would suggest to downstream consumers that it does.
/// </remarks>
public sealed class Solution
{
    /// <summary>货物分配 / Item assignments</summary>
    public IReadOnlyList<ItemAssignment> Assignments { get; }

    /// <summary>谓词装载重量（仅正值的舱位）/ Predicate load weights, positive positions only</summary>
    public IReadOnlyDictionary<StowagePosition, Quantity<Flt64>> PredicateLoadWeight { get; }

    /// <summary>推荐装载重量（仅正值的舱位）/ Recommended load weights, positive positions only</summary>
    public IReadOnlyDictionary<StowagePosition, Quantity<Flt64>> RecommendedLoadWeight { get; }

    /// <summary>
    /// 构造配载方案 / Construct the stowage solution.
    /// </summary>
    /// <param name="assignments">货物分配 / Item assignments.</param>
    /// <param name="predicateLoadWeight">谓词装载重量 / Predicate load weights.</param>
    /// <param name="recommendedLoadWeight">推荐装载重量 / Recommended load weights.</param>
    public Solution(
        IReadOnlyList<ItemAssignment> assignments,
        IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>? predicateLoadWeight = null,
        IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>? recommendedLoadWeight = null)
    {
        Assignments = assignments;
        PredicateLoadWeight = predicateLoadWeight
            ?? new Dictionary<StowagePosition, Quantity<Flt64>>();
        RecommendedLoadWeight = recommendedLoadWeight
            ?? new Dictionary<StowagePosition, Quantity<Flt64>>();
    }
}

/// <summary>
/// 货物分配。Item assignment to a position.
/// </summary>
/// <param name="Item">货物 / Item</param>
/// <param name="Position">位置 / Position</param>
/// <param name="Stowed">是否已配载 / Whether stowed</param>
public sealed record ItemAssignment(Item Item, StowagePosition Position, bool Stowed);
