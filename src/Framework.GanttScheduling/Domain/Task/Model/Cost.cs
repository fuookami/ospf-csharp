#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Error;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
/// <summary>
/// 成本项，包含标签、值和消息 / Cost item containing tag, value, and message.
/// </summary>
/// <typeparam name="V">数值类型 / The numeric type.</typeparam>
/// <param name="Tag">标签 / The tag.</param>
/// <param name="CostValue">成本值 / The cost value.</param>
/// <param name="Message">消息 / The message.</param>
public sealed record CostItem<V>(string Tag, V? CostValue = default, string? Message = null) : ICopyable<CostItem<V>>
    where V : struct, IRealNumber<V> {
    /// <summary>是否有效 / Whether valid.</summary>
    public bool Valid => CostValue.HasValue;

    /// <inheritdoc/>
    public CostItem<V> Copy() => new(Tag, CostValue, Message);
}

/// <summary>
/// 成本接口，支持迭代和复制 / Cost interface supporting iteration and copying.
/// </summary>
/// <typeparam name="V">数值类型 / The numeric type.</typeparam>
public interface ICost<V> : IEnumerable<CostItem<V>>, ICopyable<ICost<V>>
    where V : struct, IRealNumber<V> {
    /// <summary>成本项列表 / The list of cost items.</summary>
    IReadOnlyList<CostItem<V>> Items { get; }

    /// <summary>成本总和 / The sum cost value.</summary>
    V? CostSum { get; }

    /// <summary>是否有效 / Whether valid.</summary>
    bool Valid => CostSum.HasValue;

    /// <summary>读取 solver 成本值（nullable）/ Read cost as a solver value (nullable).</summary>
    Flt64? SolverCostOrNull(Flt64? @default = null)
        => CostSum.HasValue ? new Flt64(Convert.ToDouble(CostSum.Value)) : @default;

    /// <summary>读取 solver 成本值 / Read cost as a solver value.</summary>
    Result<Flt64, ErrorCode, Error<ErrorCode>> SolverCost(Flt64? @default = null) {
        Flt64? value = SolverCostOrNull(@default);
        if (value is not null) {
            return Results.Ok<Flt64>(value.Value);
        }
        return Results.Failed<Flt64>(new GanttSchedulingLifecycleError("cost sum is required to build solver cost"));
    }
}

/// <summary>
/// 不可变成本 / Immutable cost.
/// </summary>
/// <typeparam name="V">数值类型 / The numeric type.</typeparam>
/// <param name="Items">成本项列表 / The list of cost items.</param>
/// <param name="CostSum">成本总和 / The sum cost value.</param>
public sealed record ImmutableCost<V>(
    IReadOnlyList<CostItem<V>> Items,
    V? CostSum = default
) : ICost<V>
    where V : struct, IRealNumber<V> {
    /// <inheritdoc/>
    IReadOnlyList<CostItem<V>> ICost<V>.Items => Items;

    /// <inheritdoc/>
    public ICost<V> Copy()
        => new ImmutableCost<V>(Items.Select(i => i.Copy()).ToList(), CostSum);

    /// <inheritdoc/>
    public IEnumerator<CostItem<V>> GetEnumerator() => Items.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// 可变成本，支持动态添加成本项 / Mutable cost supporting dynamic addition of cost items.
/// </summary>
/// <typeparam name="V">数值类型 / The numeric type.</typeparam>
public sealed class MutableCost<V> : ICost<V>
    where V : struct, IRealNumber<V> {
    private readonly List<CostItem<V>> _items;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="items">初始成本项列表 / Initial cost items.</param>
    /// <param name="costSum">初始成本总和 / Initial cost sum.</param>
    public MutableCost(IReadOnlyList<CostItem<V>>? items = null, V? costSum = default) {
        _items = items is not null ? new List<CostItem<V>>(items) : new List<CostItem<V>>();
        CostSum = costSum;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CostItem<V>> Items => _items;

    /// <inheritdoc/>
    public V? CostSum { get; private set; }

    /// <summary>
    /// 添加成本项 / Add cost item.
    /// </summary>
    /// <param name="item">成本项 / The cost item.</param>
    public void Add(CostItem<V> item) {
        if (!item.Valid || !item.CostValue!.Value.Equals(default(V))) {
            _items.Add(item);
        }
        if (item.Valid) {
            CostSum = CostSum.HasValue
                ? CostSum.Value
                : item.CostValue;
        }
    }

    /// <inheritdoc/>
    public ICost<V> Copy()
        => new MutableCost<V>(_items.Select(i => i.Copy()).ToList(), CostSum);

    /// <inheritdoc/>
    public IEnumerator<CostItem<V>> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
