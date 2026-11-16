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
/// 预留货物目标 / The item-reserve objective.
/// </summary>
/// <remarks>
/// 与对齐：最小化 `Σ (−c × loaded[i] + c)`，
/// 也就是最小化（常数 − 已装载量）。注意**每件货物都带一个常数 `+c`**，所以这个目标的**常数项非零**。
///
/// 四处必须沿用：
///
/// 1. **是 `Minimum`**。
/// 2. **每一项都是 `−c × loaded[i] + c`**：常数和变量同时出现，写成 `−c × loaded[i]` 会丢掉常数项，
///    而丢掉常数项会改变不同车型之间的目标可比性。
/// 3. **目标名带空格**：`"item reserve"`。
/// 4. **不跳过任何货物**，包括没有 `loaded` 符号的——但本仓没有符号时无法贡献变量，只累常数的写法见下。
///
/// Minimise `Σ (−c × loaded[i] + c)`, i.e. minimise (a constant minus the loaded amount). Note that **every item contributes a `+c` constant**, so this
/// objective's **constant term is non-zero**. Four things are carried over: **it is `Minimum`**; **each term is `−c × loaded[i] + c`**, with the constant and the variable together,
/// since dropping the constant would change the comparability of objectives across aircraft types; **the name has a space**, `"item reserve"`; and **no item is skipped**.
/// </remarks>
internal sealed class ItemReserveLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly StowageModel _stowage;
    private readonly Func<Item, Flt64> _coefficient;

    /// <summary>
    /// 构造预留货物目标 / Construct the item-reserve objective.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="coefficient">按货物的系数 / The per-item coefficient.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public ItemReserveLimit(
        IReadOnlyList<Item> items,
        StowageModel stowage,
        Func<Item, Flt64>? coefficient = null,
        string name = "item_reserve_limit")
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
    /// 施加目标 / Apply the objective.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        for (int i = 0; i < _items.Count; ++i)
        {
            Flt64 coefficient = _coefficient(_items[i]);
            // **每件货物都贡献 `+c` 的常数**，无论有没有 `loaded` 符号。
            // **Every item contributes the `+c` constant**, whether or not it has a `loaded` symbol.
            constant += coefficient;
            if (_stowage.LoadedSymbol(i) is not { } loaded)
            {
                continue;
            }
            Flt64 negative = new Flt64(0.0) - coefficient;
            foreach (LinearMonomial<Flt64> monomial in loaded.Polynomial.Monomials)
            {
                monomials.Add(new LinearMonomial<Flt64>(negative * monomial.Coefficient, monomial.Symbol));
            }
            constant += negative * loaded.Polynomial.Constant;
        }
        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, constant),
            "item reserve",
            displayName: null);
    }
}
