#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
// **`Stowage` 既是命名空间又是类型**（`Domain.Stowage` 与 `Model.Stowage`），必须起别名。
// **`Stowage` is both a namespace and a type** (`Domain.Stowage` versus `Model.Stowage`), so an alias is mandatory.
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.RecommendedWeightEqualization.Service.Limits;

/// <summary>
/// 优先级预约约束 / The priority-appointment constraint.
/// </summary>
/// <remarks>
/// 与基线 `recommended_weight_equalization/service/limits/PriorityAppointmentLimit.kt` 对齐：**没有预约的货物只能落在与其优先级深度匹配的舱位上**。
///
/// 五处必须照抄：
///
/// 1. **三条跳过条件**：`!Location.Main`（只看主舱）、`Cargo.Contains(Virtual)`（虚拟货物不约束）、
///    `appointment.ContainsKey(item)`（已预约的不约束）。少任何一条都会多出约束，把合法解切掉。
/// 2. **`StowageNeeded` 为假也跳过**：不可放置的组合本来就没有变量，加约束会造出恒真式。
/// 3. **约束是 `s[i,j] = 0`**（等式），不是 `不超过 0`。用不等式会允许负值——而配载变量是二元的，负值不可能，但等式更贴合基线的语义。
/// 4. **`StowageNeeded` 用的是静态方法**，与 `StowageModel.StowageNeeded(item, position)` 一致。
/// 5. **基线还写了 `stowage.stowage[i, j].range.eq(Flt64.zero)`**——那是给符号设界，**紧接着又加了同义的等式约束**。
///    两件事都做是冗余但无害的；本仓只加约束（符号的界由注册时决定），并把这条差异记在这里。
///
/// Aligned with the baseline: **an item without an appointment may only sit at a position matching its priority depth**. Five things must be copied: **the three skip conditions** —
/// `!Location.Main` (main deck only), `Cargo.Contains(Virtual)` (virtual cargo is unconstrained) and `appointment.ContainsKey(item)` (appointed items are unconstrained); dropping any
/// of them adds constraints and cuts off valid solutions; **`StowageNeeded` false is skipped too**, since an unplaceable combination has no variable and the constraint would be a
/// tautology; **the constraint is `s[i,j] = 0`** (an equality), not `不超过 0`; **`StowageNeeded` is the static method** matching `StowageModel.StowageNeeded(item, position)`; and **the
/// baseline also writes `stowage.stowage[i, j].range.eq(Flt64.zero)`** — bounding the symbol — **immediately followed by an equivalent equality constraint**. Both is redundant but
/// harmless; this port adds only the constraint (bounds are decided at registration) and records the difference here.
/// </remarks>
internal sealed class PriorityAppointmentLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly IReadOnlyDictionary<Item, StowagePosition> _appointment;
    private readonly PriorityAppointment _priorityAppointment;
    private readonly StowageModel _stowage;

    /// <summary>
    /// 构造预约约束 / Construct the appointment constraint.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="appointment">货物到舱位的预约 / The item-to-position appointments.</param>
    /// <param name="priorityAppointment">优先级预约 / The priority appointment.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public PriorityAppointmentLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyDictionary<Item, StowagePosition> appointment,
        PriorityAppointment priorityAppointment,
        StowageModel stowage,
        string name = "priority_appointment_limit")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(priorityAppointment);
        ArgumentNullException.ThrowIfNull(stowage);

        _items = items;
        _positions = positions;
        _appointment = appointment;
        _priorityAppointment = priorityAppointment;
        _stowage = stowage;
        Name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束 / Apply the constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        for (int i = 0; i < _items.Count; ++i)
        {
            Item item = _items[i];
            if (!item.Location.IsMain || item.Contains(CargoCode.Virtual) || _appointment.ContainsKey(item))
            {
                continue;
            }
            for (int j = 0; j < _positions.Count; ++j)
            {
                StowagePosition position = _positions[j];
                if (!StowageModel.StowageNeeded(item, position))
                {
                    continue;
                }
                if (!_priorityAppointment.Invoke(item.Priority, position))
                {
                    LinearExpressionSymbol? symbol = _stowage.StowageSymbol(i, j);
                    if (symbol is null)
                    {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.ApplicationFailed,
                            "配载符号缺失 / the stowage symbol is missing: " + item + "," + position));
                    }
                    string rowName = Name + "_" + item + "_" + position;
                    var row = new LinearInequality<Flt64>(
                        symbol.Polynomial,
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                        Comparison.EQ,
                        rowName);
                    Try added = model.AddConstraint(row, group: null, name: rowName);
                    if (!added.IsOk)
                    {
                        return added;
                    }
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
