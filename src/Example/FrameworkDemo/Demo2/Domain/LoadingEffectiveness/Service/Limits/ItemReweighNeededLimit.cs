#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service.Limits;

/// <summary>
/// 待复重货物目标 / The item-reweigh-needed objective.
/// </summary>
/// <remarks>
/// 与对齐：最小化**尚未复重**货物的装载量，
/// 即尽量先装已复重的货物。
///
/// 四处必须沿用：
///
/// 1. **只有"没有复重时刻"的货物进目标**（`mapIndexedNotNull` 对 `reweighed != null` 返回 `null` 跳过）。
///    写反会让已复重的货物也被压后。
/// 2. **整件货物没有 `order` 也算"尚未复重"**——基线判的是 `item.order?.reweighed == null`，`order` 为 `null` 时该式也为真。
/// 3. **是 `Minimum`**，目标名带空格：`"item reweigh needed"`。
/// 4. **不判空集合**：即使所有货物都已复重，仍然加一个（可能为空的）目标。这与 `ItemAheadLoadLimit` 的早退**不同**。
///
/// Minimise the loaded amount of items **not yet reweighed**, i.e. load the already-reweighed ones first. Four things are carried over: **only items without
/// a reweigh instant enter the objective** (its `mapIndexedNotNull` returns null for `reweighed != null`), and inverting it would push reweighed items back too; **an item
/// with no `order` at all also counts as not-yet-reweighed**, since the code tests `item.order?.reweighed == null`, which is true when `order` is null; **it is `Minimum`** with a
/// spaced name, `"item reweigh needed"`; and **there is no empty-set guard** — even when every item is reweighed the objective is still added, which **differs** from
/// `ItemAheadLoadLimit`'s early return.
/// </remarks>
internal sealed class ItemReweighNeededLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly StowageModel _stowage;
    private readonly Func<Item, Flt64> _coefficient;

    /// <summary>
    /// 构造待复重货物目标 / Construct the item-reweigh-needed objective.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="coefficient">按货物的系数 / The per-item coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public ItemReweighNeededLimit(
        IReadOnlyList<Item> items,
        StowageModel stowage,
        Func<Item, Flt64>? coefficient = null,
        string name = "item_reweigh_needed_limit")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _stowage = stowage;
        _coefficient = coefficient ?? (_ => Flt64.One);
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 尚未复重的货物下标 / The indices of items not yet reweighed.
    /// </summary>
    /// <returns>货物下标 / The item indices.</returns>
    public IReadOnlyList<int> PendingIndices()
    {
        var indices = new List<int>();
        for (int i = 0; i < _items.Count; ++i)
        {
            // **`order` 为 `null` 也算尚未复重**。
            // **A null `order` also counts as not yet reweighed.**
            if (_items[i].Order?.Reweighed is null)
            {
                indices.Add(i);
            }
        }
        return indices;
    }

    /// <summary>
    /// 施加目标 / Apply the objective.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (int i in PendingIndices())
        {
            if (_stowage.LoadedSymbol(i) is not { } loaded)
            {
                continue;
            }
            Flt64 coefficient = _coefficient(_items[i]);
            foreach (LinearMonomial<Flt64> monomial in loaded.Polynomial.Monomials)
            {
                monomials.Add(new LinearMonomial<Flt64>(coefficient * monomial.Coefficient, monomial.Symbol));
            }
            constant += coefficient * loaded.Polynomial.Constant;
        }
        // **不判空集合**：即使没有待复重的货物也照加目标。
        // **No empty-set guard**: the objective is added even when nothing is pending.
        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, constant),
            "item reweigh needed",
            displayName: null);
    }
}
