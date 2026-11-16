// 执行器-时隙编译约束 / Executor-slot compilation constraint.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Service.Limits;

/// <summary>
/// 执行器-时隙编译影子价格键 / Executor-slot compilation shadow price key.
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type.</typeparam>
public sealed class ExecutorSlotCompilationShadowPriceKey<E> : ShadowPriceKey
    where E : Executor {
    /// <summary>
    /// 构造影子价格键 / Construct the shadow price key.
    /// </summary>
    /// <param name="executor">执行器 / The executor.</param>
    /// <param name="slot">时隙 / The time slot.</param>
    public ExecutorSlotCompilationShadowPriceKey(E executor, TimeRange slot)
        : base(typeof(ExecutorSlotCompilationShadowPriceKey<E>)) {
        Executor = executor;
        Slot = slot;
    }

    /// <summary>执行器 / The executor.</summary>
    public E Executor { get; }

    /// <summary>时隙 / The time slot.</summary>
    public TimeRange Slot { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ExecutorSlotCompilationShadowPriceKey<E> other
        && EqualityComparer<E>.Default.Equals(Executor, other.Executor)
        && Slot == other.Slot;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Executor, Slot);
}

/// <summary>
/// 每个执行器在每个时隙恰好选择一列的约束 / The exactly-one-column-per-executor-slot constraint.
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type.</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type.</typeparam>
/// <remarks>
/// **表达式缺失必须报错，不能跳过**。这条约束的作用是"每个执行器在每个时隙恰好选一列"。
/// 若某个组合查不到表达式就静默跳过，那个执行器在该时隙就**没有任何覆盖要求**——
/// 求解器于是可以让它空着，而排程结果看起来完全正常：所有任务都排上了，只是某个班次少用了一个人。
/// 这种缺陷不会在目标值上留下痕迹，只会让实际产出比计划少。
///
/// **A missing expression must be an error, never skipped.** This constraint requires "exactly one column per executor and slot". Silently skipping a combination that lacks
/// an expression removes every coverage requirement for that executor in that slot — the solver may then leave it idle, and the schedule looks entirely normal: every task is
/// assigned, one shift simply used one fewer person. The defect leaves no trace in the objective value; it only makes actual output fall short of plan.
/// </remarks>
public class ExecutorSlotCompilationConstraint<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlyList<TimeRange> _slots;
    private readonly IReadOnlyDictionary<(E, TimeRange), object> _expressions;
    private readonly string _name;

    /// <summary>
    /// 构造约束 / Construct the constraint.
    /// </summary>
    /// <param name="executors">执行器列表 / The executors.</param>
    /// <param name="slots">时隙列表 / The time slots.</param>
    /// <param name="expressions">执行器-时隙到编译表达式的映射 / The map from executor and slot to a compiled expression.</param>
    /// <param name="name">管道名称 / The pipeline name.</param>
    public ExecutorSlotCompilationConstraint(
        IReadOnlyList<E> executors,
        IReadOnlyList<TimeRange> slots,
        IReadOnlyDictionary<(E, TimeRange), object>? expressions = null,
        string name = "executor_slot_compilation") {
        _executors = executors;
        _slots = slots;
        _expressions = expressions ?? new Dictionary<(E, TimeRange), object>();
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;

    bool IMetaConstraintGroup.Lazy => false;

    /// <summary>
    /// 检查每个执行器-时隙组合都有编译表达式 / Check that every executor-slot pair has a compiled expression.
    /// </summary>
    /// <returns>缺失的组合，或空列表 / The missing pairs, or an empty list.</returns>
    public IReadOnlyList<(E Executor, TimeRange Slot)> MissingExpressions() {
        var missing = new List<(E, TimeRange)>();
        foreach (E executor in _executors) {
            foreach (TimeRange slot in _slots) {
                if (!_expressions.ContainsKey((executor, slot))) {
                    missing.Add((executor, slot));
                }
            }
        }
        return missing;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 一个组合缺失时**在第一次遇到就返回失败**，而不是收集全部缺失再报。理由：调用方拿到第一个失败就会中止，
    /// 收集全部只是多做了工作；而如果实现选择"跳过缺失的继续做"，剩下的组合会被加上约束，
    /// 于是模型处于"部分约束"状态——比完全失败更危险。
    ///
    /// **Returns a failure on the first missing pair** rather than collecting them all. The caller stops at the first failure anyway, so collecting is wasted work; and an
    /// implementation that instead "skips the missing one and continues" leaves the model **partially constrained**, which is worse than failing outright.
    /// </remarks>
    /// <remarks>
    /// 一个组合缺失时**在第一次遇到就返回失败**，而不是收集全部缺失再报。理由：调用方拿到第一个失败就会中止，
    /// 收集全部只是多做了工作；而如果实现选择"跳过缺失的继续做"，剩下的组合会被加上约束，
    /// 于是模型处于"部分约束"状态——比完全失败更危险。
    ///
    /// **加行这一半以前是缺的**：它只校验、不加任何行。`invoke` 是
    /// `addConstraint(expression eq 1, args = ExecutorSlotCompilationShadowPriceKey(executor, slot))`——
    /// 也就是"每个（执行器, 时隙）恰好编译一次"这条要求本身。少了它，编译量可以取任意值，
    /// 那个执行器在那个时隙上可以一个任务都不接，而排程看起来完全正常。
    ///
    /// 表达式是**占位对象**时不加行也不失败：`CompilationTable.Build` 现在填的就是 `new object`，
    /// 占位是正常状态，改成失败会把全部分配打回。与 `TaskCompilationConstraint` 同一个天花板。
    ///
    /// **Returns a failure on the first missing pair** rather than collecting them all. The caller stops at the first failure anyway, so collecting is wasted work; and an
    /// implementation that instead "skips the missing one and continues" leaves the model **partially constrained**, which is worse than failing outright. **The row-adding half was
    /// missing**: it only validated. `invoke` is `addConstraint(expression eq 1, args = ExecutorSlotCompilationShadowPriceKey(executor, slot))` — the requirement itself,
    /// that **each (executor, slot) is compiled exactly once**. Without it the compiled quantity may take any value and that executor may take no task in that slot while the schedule looks
    /// normal. A **placeholder** expression adds no row and is not a failure: `CompilationTable.Build` fills `new object` today, a placeholder is the normal state, and failing would reject
    /// every assignment — the same ceiling as `TaskCompilationConstraint`.
    /// </remarks>
    public Try Invoke(object model) {
        System.ArgumentNullException.ThrowIfNull(model);
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "执行器-时隙编译约束只能施加到线性元模型 / the executor-slot compilation constraint can only be applied to a linear meta model"));
        }

        foreach (E executor in _executors) {
            foreach (TimeRange slot in _slots) {
                if (!_expressions.TryGetValue((executor, slot), out object? expression)) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "缺少执行器-时隙编译表达式：" + executor + ", " + slot
                        + " / Missing executor-slot compilation expression: " + executor + ", " + slot));
                }
                if (expression is not Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64> symbol) {
                    continue;
                }

                // **符号要包成单项式**：`LinearExpressionSymbol` 本身就是变量，展开它的多项式是空的。
                // **Wrap the symbol in a monomial**: a `LinearExpressionSymbol` *is* a variable and its polynomial is empty.
                string rowName = _name + "_" + executor + "_" + slot;
                Try added = linear.AddConstraint(
                    new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>(
                        new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                            new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, symbol) },
                            Flt64.Zero),
                        new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                            Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.One),
                        Fuookami.Ospf.Math.Symbol.Inequality.Comparison.EQ,
                        rowName),
                    group: null,
                    name: rowName,
                    args: new ExecutorSlotCompilationShadowPriceKey<E>(executor, slot));
                if (!added.IsOk) {
                    return added;
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 影子价格提取器 / The shadow price extractor.
    /// </summary>
    /// <returns>提取器 / The extractor.</returns>
    /// <remarks>
    /// **当前返回零，因为影子价格参数里还没有时隙信息**。C# 的 `IGanttSchedulingShadowPriceArguments` 只带执行器与任务，
    /// 而这条约束的键是（执行器，时隙）。硬把时隙塞进来需要改整个参数接口，这里不做。
    ///
    /// 这里返回零而不是抛异常：影子价格为零会让列生成**少一个加速项**，但结果仍然正确；
    /// 抛异常会让整条流水线停下来。**缺一个可选信息不该让主流程失败**。
    /// `ponytail:` 天花板——等影子价格参数接口需要携带时隙时再补上真实的查表。
    ///
    /// **It currently returns zero because the shadow-price arguments carry no slot yet.** C#'s `IGanttSchedulingShadowPriceArguments` only carries the executor and the task,
    /// while this constraint's key is (executor, slot). Threading the slot through would require changing the whole argument interface, which not do.
    ///
    /// Returning zero rather than throwing: a zero shadow price costs column generation **one acceleration term** while the answer stays correct; throwing would halt the
    /// entire pipeline. **A missing optional input should not fail the main flow.**
    /// **`ponytail:` ceiling** — add the real lookup when the argument interface needs to carry the slot.
    /// </remarks>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => (map, args) => Flt64.Zero;

    /// <inheritdoc/>
    /// <remarks>
    /// 按**行上带的键类型**从对偶解里认回本管线的行。提取器暂时读不到（参数里没有时隙），但**映射本身是公开的接口**——
    /// 手上有 `(执行器, 时隙)` 的调用方可以直接查。不写进去，那个调用方永远只能拿到零。
    ///
    /// Recognises this pipeline's rows in the dual solution by **the key type on the row**. The extractor cannot read them yet (the arguments carry no slot), but **the map is a public
    /// interface** — a caller holding `(executor, slot)` can look the price up directly. Without this, such a caller only ever gets zero.
    /// </remarks>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        System.ArgumentNullException.ThrowIfNull(shadowPriceMap);
        System.ArgumentNullException.ThrowIfNull(shadowPrices);

        foreach (KeyValuePair<MathConstraint, Flt64> pair in shadowPrices.Constraints) {
            if (pair.Key.Args is ExecutorSlotCompilationShadowPriceKey<E> key) {
                shadowPriceMap.Put(new ShadowPrice(key, pair.Value));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
