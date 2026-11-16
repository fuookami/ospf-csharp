#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Service.Limits;
/// <summary>
/// 资源容量约束 / Resource capacity constraint.
/// </summary>
public class ResourceCapacityConstraint<E, A, S, R, C, V>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E>
    where S : IResourceTimeSlot<R, C, V>
    where R : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    private readonly IResourceUsage<S, R, C, V> _usage;
    private readonly string _name;

    public ResourceCapacityConstraint(IResourceUsage<S, R, C, V> usage, string? name = null) {
        _usage = usage;
        _name = name ?? $"{usage.Name}_resource_capacity";
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 每个时间槽两行：**上界** 是 `polyX` 不超过容量上界，**下界** 是 `polyX` 不低于容量下界，行名 `{name}_ub_{槽}` / `{name}_lb_{槽}`，
    /// 都挂 `ResourceCapacityShadowPriceKey(slot)`。
    ///
    /// 三件如此：
    ///
    /// 1. **`polyX` 不是数量本身**：有超量松弛时上界加在 `x - pos` 上，有不足松弛时下界加在 `x + neg` 上（"扣掉超出 / 补上不足之后的数量"）。
    ///    加在数量上，松弛就失去意义——超出的部分照样违反上界。
    /// 2. **松弛取不到时退回数量**：槽没开超量、或松弛是空符号时，行退成 `quantity` 不超过上界。
    /// 3. **数量表必须先注册**：没注册就失败，不能当"零数量"处理——零数量会让容量永远不紧，模型照样可解。
    ///
    /// Two rows per time slot: an **upper bound** of `polyX` against the capacity's upper bound and a **lower bound** of `polyX` against its lower bound, named `{name}_ub_{slot}` / `{name}_lb_{slot}`, both carrying a
 /// `ResourceCapacityShadowPriceKey(slot)`. Three things are: **`polyX` is not the quantity itself** — with an over slack the upper bound applies to `x - pos` and
    /// with a less slack the lower bound to `x + neg` (the quantity after removing the excess or covering the shortfall), since applying it to the quantity would make the slack pointless and
    /// the excess would still violate the bound; **when no slack is available the row falls back to the quantity**, for a slot whose over switch is off or whose slack is an empty symbol; and
    /// **the quantity table must be registered first** — failing instead of treating an unregistered table as "quantity zero", which would leave capacity never tight while the model stays solvable.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "资源容量约束只能施加到线性元模型 / the resource capacity constraint can only be applied to a linear meta model"));
        }
        if (_usage.Quantity is not IReadOnlyDictionary<S, ILinearIntermediateSymbol<Flt64>> quantity) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "资源使用尚未注册，取不到数量表 / the resource usage has not been registered, so its quantity table is unavailable"));
        }
        var over = _usage.OverQuantity as IReadOnlyDictionary<S, ILinearIntermediateSymbol<Flt64>>;
        var less = _usage.LessQuantity as IReadOnlyDictionary<S, ILinearIntermediateSymbol<Flt64>>;

        foreach (S slot in _usage.TimeSlots) {
            if (!quantity.TryGetValue(slot, out ILinearIntermediateSymbol<Flt64>? symbol)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "数量表缺项：" + slot + " / the quantity table is missing an entry: " + slot));
            }

            // **空符号不是松弛**：槽没开超量 \/ 不足时表里放的是空符号，对它取 `polyX` 会得到一个恒零的左端，
            // 那样上界行零 ≤ ub 恒真——看起来加了约束，其实什么也没约束。
            // **An empty symbol is not a slack**: a slot with over/less disabled holds an empty symbol, and taking `polyX` from it yields an identically zero left side, making a zero left side always within the bound — a constraint that looks added and constrains nothing.
            LinearFunctionSymbolAdapter<Flt64>? overSlack =
                _usage.OverEnabled
                && slot.ResourceCapacity.OverEnabled
                && over is not null
                && over.TryGetValue(slot, out ILinearIntermediateSymbol<Flt64>? o)
                    ? o as LinearFunctionSymbolAdapter<Flt64>
                    : null;
            LinearFunctionSymbolAdapter<Flt64>? lessSlack =
                _usage.LessEnabled
                && slot.ResourceCapacity.LessEnabled
                && less is not null
                && less.TryGetValue(slot, out ILinearIntermediateSymbol<Flt64>? l)
                    ? l as LinearFunctionSymbolAdapter<Flt64>
                    : null;

            Try added = ProduceSlackHelper.BuildQuantityRows(
                linear, symbol, overSlack, lessSlack,
                slot.ResourceCapacity.SolverLowerBound(),
                slot.ResourceCapacity.SolverUpperBound(),
                _name,
                slot.Index.ToString(),
                new ResourceCapacityShadowPriceKey<R, C, V>(slot));
            if (!added.IsOk) {
                return added;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => null;

    /// <inheritdoc/>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) => Results.Ok<Success>(Results.SuccessInstance);
}
