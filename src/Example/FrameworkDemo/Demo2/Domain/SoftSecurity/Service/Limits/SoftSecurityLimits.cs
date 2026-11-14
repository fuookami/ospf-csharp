// 软性安全约束管线 / The soft-security pipelines.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using SoftPositionPair = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Model.PositionPair;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service.Limits;

/// <summary>
/// 空载厌恶目标 / The empty-hating objective.
/// </summary>
/// <remarks>
/// 与基线 `soft_security/service/limits/EmptyHatedLimit.kt` 对齐：对**带 `EmptyHated` 类型码**的舱位加一项
/// `c·(1 - full_j)`——即"该舱位空着"的代价，由 `minimize` 作为**目标**（不是约束）。
///
/// 三处必须照抄：
///
/// 1. **它是目标，不是约束**。写成 `addConstraint` 会把它变成"空载必须 ≤ 某值"，语义完全相反。
/// 2. **只有带该类型码的舱位参与**，其余 `mapNotNull` 掉。
/// 3. **系数由外部注入**（`parameter.emptyHated`），不是常数 1。
///
/// Aligned with the baseline: for each position carrying the `EmptyHated` type code, add `c·(1 - full_j)` — the cost of leaving it empty — via `minimize` as an **objective**,
/// not a constraint. Three things must be copied: **it is an objective** (writing `addConstraint` turns it into "emptiness must be at most some value", the exact opposite);
/// **only positions with that type code participate**, the rest being filtered out; and **the coefficient is injected** (`parameter.emptyHated`), not a constant one.
/// </remarks>
public sealed class EmptyHatedLimit
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;
    private readonly Func<StowagePosition, Flt64> _coefficient;

    /// <summary>
    /// 构造空载厌恶目标 / Construct the empty-hating objective.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public EmptyHatedLimit(
        IReadOnlyList<StowagePosition> positions,
        Load load,
        Func<StowagePosition, Flt64>? coefficient = null,
        string name = "empty_hated_limit")
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(load);
        _positions = positions;
        _load = load;
        _coefficient = coefficient ?? (_ => Flt64.One);
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
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
        var constant = Flt64.Zero;
        for (int j = 0; j < _positions.Count; ++j)
        {
            StowagePosition position = _positions[j];
            if (!position.Type.Contains(PositionTypeCode.EmptyHated))
            {
                continue;
            }
            ILinearIntermediateSymbol<Flt64>? full = _load.FullSymbol(j);
            if (full is null)
            {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "舱位 " + position.Name + " 的满载符号未注册 / the full symbol of position " + position.Name + " is not registered"));
            }
            // `c·(1 - full_j)` = 常数 `c` 减去 `c·full_j`。
            // `c·(1 - full_j)` is the constant `c` minus `c·full_j`.
            Flt64 c = _coefficient(position);
            monomials.Add(new LinearMonomial<Flt64>(Flt64.Zero - c, full));
            constant += c;
        }

        // 目标是**最小化**，用 `AddObject(ObjectCategory.Minimum, …)`；`LinearMetaModel` 没有 `Minimize` 方法。
        // The objective is a **minimisation**, added through `AddObject(ObjectCategory.Minimum, …)`; `LinearMetaModel` has no `Minimize`.
        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, constant),
            "empty hated",
            displayName: null);
    }
}

/// <summary>
/// 主甲板舱门空载偏好目标 / The main-deck-door empty preference objective.
/// </summary>
/// <remarks>
/// 与基线 `soft_security/service/limits/MainDeckDoorEmptyLimit.kt` 对齐：对**紧邻舱门**的主甲板舱位，按货物类别加不同权重，
/// 由 `minimize` 作为目标。
///
/// 权重表（基线硬编码，照抄）：
///
/// | 货物 | 集装器类别 | 权重 |
/// | --- | --- | --- |
/// | 空 | 板（Pallet） | **1** |
/// | 非空 | 板（Pallet） | **2** |
/// | 任意 | 箱（Container） | **5** |
/// | 其它 | — | 0 |
///
/// 三点：
///
/// 1. **顺序决定结果**：空/板先判，非空/板次之，箱最后。把箱提到前面会让"空板"也拿 5。
/// 2. **只有 `DoorUbietyType.Beside` 的舱位参与**，其它 `mapNotNull` 掉。
/// 3. **权重再乘 `parameter.besideDoorMainPosition`**，不是裸权重。
///
/// Aligned with the baseline: for main-deck positions **right beside a door**, weight each cargo class differently and `minimize`. The weight table is tabulated above and the
/// baseline hard-codes it. Three points: **the order decides the result** — empty/pallet first, non-empty/pallet next, container last, and hoisting the container case makes an
/// "empty pallet" weigh 5; **only `DoorUbietyType.Beside` positions participate**; and **the weight is multiplied by `parameter.besideDoorMainPosition`**, not used raw.
/// </remarks>
public sealed class MainDeckDoorEmptyLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Deck _deck;
    private readonly Stowage.Model.Stowage _stowage;
    private readonly Func<Item, Flt64> _coefficient;

    /// <summary>
    /// 构造舱门空载偏好目标 / Construct the door-empty preference objective.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="deck">主甲板 / The main deck.</param>
    /// <param name="stowage">装载模型 / The stowage model.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public MainDeckDoorEmptyLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        Deck deck,
        Stowage.Model.Stowage stowage,
        Func<Item, Flt64>? coefficient = null,
        string name = "main_deck_door_empty_limit")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _deck = deck;
        _stowage = stowage;
        _coefficient = coefficient ?? (_ => Flt64.One);
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
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
        for (int i = 0; i < _items.Count; ++i)
        {
            Item item = _items[i];
            Flt64 c = _coefficient(item);
            if (c.ToDouble() == 0.0)
            {
                continue;
            }
            for (int j = 0; j < _positions.Count; ++j)
            {
                StowagePosition position = _positions[j];
                if (!IsBesideDoor(position))
                {
                    continue;
                }
                if (_stowage.StowageSymbol(i, j) is not { } symbol)
                {
                    continue;
                }
                monomials.Add(new LinearMonomial<Flt64>(c, symbol));
            }
        }

        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, Flt64.Zero),
            "main deck door empty",
            displayName: null);
    }

    // 舱门邻接关系住在 `Deck.DoorUbieties` 里（门 → 一组 `DoorUbiety`），不是 `Ubieties(position)`。
    // Door adjacency lives in `Deck.DoorUbieties` (door to a set of `DoorUbiety`), not a `Ubieties(position)` method.
    private bool IsBesideDoor(StowagePosition position)
    {
        foreach (KeyValuePair<HatchDoor, IReadOnlyList<DoorUbiety>> entry in _deck.DoorUbieties)
        {
            foreach (DoorUbiety ubiety in entry.Value)
            {
                if (ubiety.Type == DoorUbietyType.Beside
                    && string.Equals(ubiety.Position?.SpaceName, position.Name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }
}

/// <summary>
/// 分隔空载目标 / The divide-empty-loading objective.
/// </summary>
/// <remarks>
/// 与基线 `soft_security/service/limits/DivideEmptyLoadingLimit.kt` 对齐：三族符号各 `minimize` **一次**，共三个目标项。
/// 三族分别是"舱位间空载"、"空载舱位后接货物"、"空载舱位后接空载"。
///
/// 三点：
///
/// 1. **三个 `minimize`，不是三个约束**。合并成一个会改变权重的相对关系（基线允许三族系数不同）。
/// 2. **每个相邻对都参与**，包括系数为 0 的——基线没有跳过零系数，跳过会让"系数为 0"与"相邻对不存在"在模型里不可区分。
/// 3. **符号缺失是失败**，不是跳过：缺符号意味着 `Register` 没跑或跑了一半。
///
/// Aligned with the baseline: one `minimize` per symbol family, three objective terms in total — "empty between cargo", "empty cargo then cargo" and "empty then empty". Three
/// points: **three `minimize` calls, not three constraints** (merging them changes the relative weights, which the baseline allows to differ per family); **every adjacent pair
/// participates**, zero coefficients included, since skipping would make "coefficient is zero" indistinguishable from "the pair does not exist"; and **a missing symbol is a
/// failure**, not a skip, because it means `Register` did not run or ran halfway.
/// </remarks>
public sealed class DivideEmptyLoadingLimit
{
    private readonly IReadOnlyList<SoftPositionPair> _adjacentPositions;
    private readonly DivideEmptyLoading _divideEmptyLoading;
    private readonly Func<StowagePosition, StowagePosition, Flt64> _emptyBetweenCargoCoefficient;
    private readonly Func<StowagePosition, StowagePosition, Flt64> _emptyCargoBetweenCargoCoefficient;
    private readonly Func<StowagePosition, StowagePosition, Flt64> _emptyBetweenEmptyCargoCoefficient;
    private readonly IReadOnlyList<StowagePosition> _positions;

    /// <summary>
    /// 构造分隔空载目标 / Construct the divide-empty-loading objective.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="adjacentPositions">相邻舱位对 / The adjacent pairs.</param>
    /// <param name="divideEmptyLoading">分隔空载模型 / The divide-empty-loading model.</param>
    /// <param name="emptyBetweenCargoCoefficient">第一族系数 / The first family's coefficient.</param>
    /// <param name="emptyCargoBetweenCargoCoefficient">第二族系数 / The second family's coefficient.</param>
    /// <param name="emptyBetweenEmptyCargoCoefficient">第三族系数 / The third family's coefficient.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public DivideEmptyLoadingLimit(
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyList<SoftPositionPair> adjacentPositions,
        DivideEmptyLoading divideEmptyLoading,
        Func<StowagePosition, StowagePosition, Flt64>? emptyBetweenCargoCoefficient = null,
        Func<StowagePosition, StowagePosition, Flt64>? emptyCargoBetweenCargoCoefficient = null,
        Func<StowagePosition, StowagePosition, Flt64>? emptyBetweenEmptyCargoCoefficient = null,
        string name = "divide_empty_loading_limit")
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(adjacentPositions);
        ArgumentNullException.ThrowIfNull(divideEmptyLoading);
        _positions = positions;
        _adjacentPositions = adjacentPositions;
        _divideEmptyLoading = divideEmptyLoading;
        _emptyBetweenCargoCoefficient = emptyBetweenCargoCoefficient ?? ((_, _) => Flt64.One);
        _emptyCargoBetweenCargoCoefficient = emptyCargoBetweenCargoCoefficient ?? ((_, _) => Flt64.One);
        _emptyBetweenEmptyCargoCoefficient = emptyBetweenEmptyCargoCoefficient ?? ((_, _) => Flt64.One);
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加三个目标 / Apply the three objectives.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Try first = Apply(
            model,
            _emptyBetweenCargoCoefficient,
            _divideEmptyLoading.EmptyBetweenCargo,
            "empty between cargo");
        if (!first.IsOk)
        {
            return first;
        }
        Try second = Apply(
            model,
            _emptyCargoBetweenCargoCoefficient,
            _divideEmptyLoading.EmptyCargoBetweenCargo,
            "empty cargo between cargo");
        if (!second.IsOk)
        {
            return second;
        }
        return Apply(
            model,
            _emptyBetweenEmptyCargoCoefficient,
            _divideEmptyLoading.EmptyBetweenEmptyCargo,
            "empty between empty cargo");
    }

    private Try Apply(
        LinearMetaModel<Flt64> model,
        Func<StowagePosition, StowagePosition, Flt64> coefficient,
        Func<int, ILinearIntermediateSymbol<Flt64>?> symbolOf,
        string name)
    {
        var monomials = new List<LinearMonomial<Flt64>>();
        for (int p = 0; p < _adjacentPositions.Count; ++p)
        {
            SoftPositionPair pair = _adjacentPositions[p];
            StowagePosition position1 = _positions[pair.First];
            StowagePosition position2 = _positions[pair.Second];
            if (symbolOf(p) is not { } symbol)
            {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    name + " 的符号未注册 / the symbol of " + name + " is not registered at pair " + p));
            }
            Flt64 c = coefficient(position1, position2);
            monomials.Add(new LinearMonomial<Flt64>(c, symbol));
        }

        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, Flt64.Zero),
            name,
            displayName: null);
    }
}
