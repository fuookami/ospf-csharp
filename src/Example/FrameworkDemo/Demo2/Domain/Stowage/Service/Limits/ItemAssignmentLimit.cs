// 货物装载状态约束 / The item assignment rows.

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
/// 货物装载状态约束 / The item assignment rows.
/// </summary>
/// <remarks>
/// 对齐基线 `stowage/service/limits/ItemAssignmentLimit.kt`：按货物状态给 `loaded_{i}` 加一行。
///
/// | 状态 | 行 | 含义 |
/// | --- | --- | --- |
/// | `Preassigned` / `AdjustmentNeeded` | `loaded_i = 1` | 必须装上 |
/// | `Optional` | `loaded_i ≤ 1` | 可装可不装 |
/// | 其余（`Reserved` / `Loaded`） | **不加行** | 基线就是 `else -> {}` |
///
/// 第三行是最容易"顺手补上"的地方：`Reserved` 看起来该有 `loaded_i = 0`，但基线不写。补上会**多一条约束**，而模型仍然可解——
/// 这类错误在测试里只能靠"行数"发现，所以在判据里逐状态断言行是否存在。
///
/// Aligned with the baseline: one row per item on `loaded_{i}`, tabulated above. The third row is where a well-meaning addition goes wrong: `Reserved` looks like it deserves
/// `loaded_i = 0`, but the baseline writes nothing. Adding it produces **one extra constraint** while the model still solves, so a test can only catch it by counting rows —
/// hence the criteria assert row existence per status.
/// </remarks>
public sealed class ItemAssignmentLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly StowageModel _stowage;

    /// <summary>
    /// 构造货物装载状态约束 / Construct the item assignment rows.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="stowage">装载模型 / The stowage model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public ItemAssignmentLimit(
        IReadOnlyList<Item> items,
        StowageModel stowage,
        string name = "item_assignment_limit") {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
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
            Comparison? comparison = item.Status switch {
                ItemStatus.Preassigned or ItemStatus.AdjustmentNeeded => Comparison.EQ,
                ItemStatus.Optional => Comparison.LE,
                // 基线的 `else -> {}`：`Reserved` 与 `Loaded` 不加行。
                // The baseline's `else -> {}`: `Reserved` and `Loaded` get no row.
                _ => null,
            };
            if (comparison is not { } op) {
                continue;
            }
            if (_stowage.LoadedSymbol(i) is not { } loaded) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "货物 " + item.Name + " 的装载符号未注册 / the loaded symbol of item " + item.Name + " is not registered"));
            }
            string rowName = Name + "_" + item.Name;
            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, loaded) },
                    Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                op,
                rowName);
            Try added = linear.AddConstraint(row, group: null, name: rowName);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
