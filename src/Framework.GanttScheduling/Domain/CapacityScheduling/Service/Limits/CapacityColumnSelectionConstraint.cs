// 产能列选择约束 / Capacity column selection constraint.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits;

/// <summary>
/// 执行器-时隙产能列选择影子价格键 / The executor-slot capacity-column shadow price key.
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type.</typeparam>
public sealed record CapacityColumnSelectionShadowPriceKey<E>(E Executor, TimeRange Slot)
    where E : Executor;

/// <summary>
/// 每个执行器在每个时隙恰好选择一条产能列 / Select exactly one capacity column for every executor and slot.
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type.</typeparam>
/// <remarks>
/// **`Groups` 是"需要恰好一个"的活动组合子集，不是全笛卡尔积**。默认取全积，但允许显式传入更小的集合：
/// 不可用的执行器、零长度时隙、以及被外部排班排除的组合都不该被要求"恰好选一列"。
/// 若把它当成全积，一个本该空着的组合会**强制**求解器塞进一列，于是产能被凭空占用。
///
/// **`Groups` is the active subset that needs "exactly one", not the full Cartesian product.** It defaults to the full product but accepts a smaller explicit set:
/// unavailable executors, zero-length slots and combinations excluded by an external roster must not be required to pick a column. Treating it as the full product **forces**
/// the solver to fill a combination that should stay empty, consuming capacity out of thin air.
///
/// **组内缺表达式必须失败**：与 `ExecutorSlotCompilationConstraint` 同理，静默跳过会让该组合失去覆盖要求。
/// 而**组外的组合必须跳过**——它们本来就不需要覆盖。两条规则的差别正是这里的核心。
///
/// **A missing expression inside the group must fail**: as with `ExecutorSlotCompilationConstraint`, skipping removes the coverage requirement. And **combinations outside the
/// group must be skipped** — they need no coverage to begin with. The difference between those two rules is the heart of the class.
/// </remarks>
public sealed class CapacityColumnSelectionConstraint<E>
    where E : Executor {
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlyList<TimeRange> _slots;
    private readonly IReadOnlyDictionary<(E, TimeRange), object> _expressions;
    private readonly IReadOnlySet<(E, TimeRange)> _groups;
    private readonly string _name;

    /// <summary>
    /// 构造约束 / Construct the constraint.
    /// </summary>
    /// <param name="executors">执行器列表 / The executors.</param>
    /// <param name="slots">时隙列表 / The time slots.</param>
    /// <param name="expressions">执行器-时隙到编译表达式的映射 / The map from executor and slot to a compiled expression.</param>
    /// <param name="groups">需要恰好一个的活动组合；为空表示全积 / The active pairs requiring exactly one; null means the full product.</param>
    /// <param name="name">管道名称 / The pipeline name.</param>
    public CapacityColumnSelectionConstraint(
        IReadOnlyList<E> executors,
        IReadOnlyList<TimeRange> slots,
        IReadOnlyDictionary<(E, TimeRange), object>? expressions = null,
        IReadOnlySet<(E, TimeRange)>? groups = null,
        string name = "capacity_column_selection") {
        _executors = executors;
        _slots = slots;
        _expressions = expressions ?? new Dictionary<(E, TimeRange), object>();
        _groups = groups ?? FullProduct(executors, slots);
        _name = name;
    }

    /// <summary>管道名称 / The pipeline name.</summary>
    public string Name => _name;

    /// <summary>需要恰好一个的活动组合 / The active pairs requiring exactly one.</summary>
    public IReadOnlySet<(E Executor, TimeRange Slot)> Groups => _groups;

    /// <summary>
    /// 检查组内每个组合都有编译表达式 / Check that every in-group pair has a compiled expression.
    /// </summary>
    /// <returns>组内缺失的组合 / The missing in-group pairs.</returns>
    /// <remarks>
    /// **只检查组内**：组外组合没有表达式是正常的，报出来会把正常状态当成缺陷。
    ///
    /// **Only the group is checked**: an out-of-group pair without an expression is normal, and reporting it would turn a normal state into a defect.
    /// </remarks>
    public IReadOnlyList<(E Executor, TimeRange Slot)> MissingExpressions() {
        var missing = new List<(E, TimeRange)>();
        foreach (E executor in _executors) {
            foreach (TimeRange slot in _slots) {
                if (_groups.Contains((executor, slot)) && !_expressions.ContainsKey((executor, slot))) {
                    missing.Add((executor, slot));
                }
            }
        }
        return missing;
    }

    /// <summary>
    /// 施加约束 / Apply the constraint.
    /// </summary>
    /// <returns>操作结果 / The operation result.</returns>
    public Try Invoke() {
        foreach (E executor in _executors) {
            foreach (TimeRange slot in _slots) {
                if (!_groups.Contains((executor, slot))) {
                    continue;
                }
                if (!_expressions.ContainsKey((executor, slot))) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "缺少执行器-时隙产能列表达式：" + executor + ", " + slot
                        + " / Missing executor-slot capacity-column expression: " + executor + ", " + slot));
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 提取影子价格并按键入累加 / Extract shadow prices and accumulate them per key.
    /// </summary>
    /// <param name="prices">约束键到对偶价格的记录 / The records of constraint key to dual price.</param>
    /// <returns>按强类型键索引的累计价格 / The accumulated prices indexed by typed key.</returns>
    /// <remarks>
    /// **同一键出现多次要累加，不能覆盖**。一个执行器-时隙组合可能被多条约束共享（例如同一组合在主模型与子模型各加一次），
    /// 覆盖会**只保留最后一条**的价格，而那条往往是最不重要的。下游用这个价格做列定价，
    /// 少算的价格会让本该入列的候选被跳过，产出因此变差，但**不会有任何错误信息**。
    ///
    /// **The same key appearing more than once accumulates; it must not overwrite.** An executor-slot pair can be shared by several constraints (added once in the master model
    /// and once in a sub-model, say), and overwriting **keeps only the last** price — usually the least significant one. Downstream uses this price for column pricing, so an
    /// under-counted price silently skips candidates that should enter the basis, degrading the output **with no error message at all**.
    /// </remarks>
    public static IReadOnlyDictionary<CapacityColumnSelectionShadowPriceKey<E>, Flt64> ExtractShadowPrices(
        IReadOnlyList<(CapacityColumnSelectionShadowPriceKey<E> Key, Flt64 Price)> prices) {
        System.ArgumentNullException.ThrowIfNull(prices);

        var result = new Dictionary<CapacityColumnSelectionShadowPriceKey<E>, Flt64>();
        foreach ((CapacityColumnSelectionShadowPriceKey<E> key, Flt64 price) in prices) {
            result[key] = result.TryGetValue(key, out Flt64 existing) ? existing + price : price;
        }
        return result;
    }

    /// <summary>构造全积组合集 / Build the full-product group set.</summary>
    /// <param name="executors">执行器 / The executors.</param>
    /// <param name="slots">时隙 / The slots.</param>
    /// <returns>全部组合 / Every combination.</returns>
    private static HashSet<(E, TimeRange)> FullProduct(
        IReadOnlyList<E> executors,
        IReadOnlyList<TimeRange> slots) {
        var groups = new HashSet<(E, TimeRange)>();
        foreach (E executor in executors) {
            foreach (TimeRange slot in slots) {
                groups.Add((executor, slot));
            }
        }
        return groups;
    }
}
