#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
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
/// 提前装载目标 / The item-ahead-load objective.
/// </summary>
/// <remarks>
/// 与对齐：**把已进入作业流程的货物尽早装上**，
/// 也就是最大化"已作业货物"的装载量——写法是最小化 `Σ coefficient × loaded[i]`，系数由调用方给（默认 1，
/// 目标名 `"item ahead load"`）。
///
/// 五处必须沿用：
///
/// 1. **三个前缀谓词按顺序找第一个成立的**：`任一货物已站坪` → `任一已站坪或已上车` → `任一三者皆有`。
///    找到**第一个**就停（`find`），不是三个都加。
/// 2. **一条都不成立时不加目标**（`if (predicate != null)`），返回成功。零多项式目标会占掉一个目标位。
/// 3. **是 `Minimum`**，不是 `Maximum`。基线写 `minimize`。
/// 4. **目标名带空格**：`"item ahead load"`，与管线名 `item_ahead_load_limit` 不同。
/// 5. **求和面向全部货物**，不是只面向满足谓词的那几件——谓词只决定"要不要加这条目标"。
///
/// **items already in the operational flow should be loaded early**. Five things must be copied: **the three prefix predicates are tried in order and the
/// first that holds wins** — any item on the hardstand, then any on the hardstand or boarded, then any with all three — since its `find` stops at the first, rather than
/// adding all three; **when none holds, no objective is added** (its `if (predicate != null)`), returning success, because a zero-polynomial objective would take up a slot;
/// **it is `Minimum`**, not `Maximum`; **the name has a space**, `"item ahead load"`; and **the sum covers every item**, since the predicate only decides whether to add the objective.
/// </remarks>
internal sealed class ItemAheadLoadLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly StowageModel _stowage;
    private readonly Func<Item, Flt64> _coefficient;

    /// <summary>
    /// 构造提前装载目标 / Construct the item-ahead-load objective.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="coefficient">按货物的系数 / The per-item coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public ItemAheadLoadLimit(
        IReadOnlyList<Item> items,
        StowageModel stowage,
        Func<Item, Flt64>? coefficient = null,
        string name = "item_ahead_load_limit")
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
    /// 三个前缀谓词 / The three prefix predicates.
    /// </summary>
    /// <remarks>
    /// 顺序有意义：越靠后的谓词越宽。`find` 取**第一个**成立的。
    ///
    /// The order matters: later predicates are wider. The code's `find` takes the **first** that holds.
    /// </remarks>
    public static IReadOnlyList<Func<Item, bool>> Predicates { get; } = new Func<Item, bool>[]
    {
        item => item.Order?.Hardstand is not null,
        item => item.Order?.Hardstand is not null || item.Order?.CarBoard is not null,
        item => item.Order?.Hardstand is not null
            || item.Order?.CarBoard is not null
            || item.Order?.Reweighed is not null,
    };

    /// <summary>
    /// 找到第一个成立的谓词 / Find the first predicate that holds.
    /// </summary>
    /// <returns>谓词下标，或 `null` / The predicate index, or null.</returns>
    public int? ActivePredicate()
    {
        for (int index = 0; index < Predicates.Count; ++index)
        {
            Func<Item, bool> predicate = Predicates[index];
            foreach (Item item in _items)
            {
                if (predicate(item))
                {
                    return index;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 施加目标 / Apply the objective.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        // **一条都不成立就不加目标**。
        // **No predicate holds, so no objective is added.**
        if (ActivePredicate() is null)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        for (int i = 0; i < _items.Count; ++i)
        {
            // **面向全部货物**：谓词只决定要不要加这条目标。
            // **Every item**: the predicate only decides whether the objective is added.
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
        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, constant),
            "item ahead load",
            displayName: null);
    }
}
