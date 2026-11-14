// 装载可行性约束 / The stowage feasibility rows.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;

/// <summary>
/// 装载可行性约束 / The stowage feasibility rows.
/// </summary>
/// <remarks>
/// 对齐基线 `stowage/service/limits/StowageLimit.kt`：对每一对"双方都可用但**位置不接纳**"的货物与舱位，加一行 `stowage_{i,j} = 0`。
///
/// 三个条件缺一不可：`item.status.available`、`position.status.available`、`!position.enabled(item).ok`。
/// 少查前两个会让"这件货根本不装"或"这个舱位根本不用"的组合也加行——行本身是对的（`= 0` 成立），但约束表被无谓地撑大，
/// 而且掩盖了真正的不合法对。少查第三个则是**漏掉全部约束**，模型会允许把货塞进装不下的舱位。
///
/// Aligned with the baseline: for every pair where **both sides are available but the position refuses the item**, add `stowage_{i,j} = 0`. All three conditions matter. Dropping
/// the first two adds rows for pairs that are irrelevant anyway — the rows are true (`= 0` holds) but the constraint table swells and the genuinely illegal pairs get hidden;
/// dropping the third **loses every row**, letting the model stuff cargo into positions that cannot take it.
/// </remarks>
public sealed class StowageLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly StowageModel _stowage;

    /// <summary>
    /// 构造装载可行性约束 / Construct the stowage feasibility rows.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">装载模型 / The stowage model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public StowageLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        StowageModel stowage,
        string name = "stowage_limit") {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _stowage = stowage;
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束 / Apply the rows.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> linear) {
        ArgumentNullException.ThrowIfNull(linear);
        for (int i = 0; i < _items.Count; ++i) {
            Item item = _items[i];
            if (!ItemStatuses.Available(item.Status)) {
                continue;
            }
            for (int j = 0; j < _positions.Count; ++j) {
                StowagePosition position = _positions[j];
                if (!position.Status.Available) {
                    continue;
                }
                if (position.Enabled(item).Ok) {
                    continue;
                }
                if (_stowage.StowageSymbol(i, j) is not { } symbol) {
                    // **不合法对没有符号**说明内核根本没建这个组合。跳过会让这一对彻底不受约束——模型可以把货放进去。
                    // **No symbol for an illegal pair** means the kernel never created the combination. Skipping leaves the pair entirely unconstrained, so the model may load it.
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "货物 " + item.Name + " 与舱位 " + position.Name +
                        " 的装载符号未注册 / the stowage symbol is not registered"));
                }
                string rowName = Name + "_" + item.Name + "_" + position.Name;
                var row = new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) },
                        Flt64.Zero),
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                    Comparison.EQ,
                    rowName);
                Try added = linear.AddConstraint(row, group: null, name: rowName);
                if (!added.IsOk) {
                    return added;
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
