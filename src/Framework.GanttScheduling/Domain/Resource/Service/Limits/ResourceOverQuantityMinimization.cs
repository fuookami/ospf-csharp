#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
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
/// 资源超限数量最小化 / Resource over quantity minimization.
/// </summary>
public class ResourceOverQuantityMinimization<E, A, S, R, C, V>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E>
    where S : IResourceTimeSlot<R, C, V>
    where R : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    private readonly IResourceUsage<S, R, C, V> _quantity;
    private readonly Func<S, Flt64> _threshold;
    private readonly Func<S, Flt64> _coefficient;
    private readonly string _name;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="quantity">资源使用 / The resource usage.</param>
    /// <param name="threshold">按槽的阈值，低于它不计费 / Per-slot threshold below which nothing is charged.</param>
    /// <param name="coefficient">按槽的权重 / Per-slot weight.</param>
    /// <param name="name">管道名称 / Pipeline name.</param>
    /// <remarks>
    /// **门槛与权重都按槽**：基线的签名是 `threshold: (S) -> Flt64 = zero` 与 `coefficient: (S) -> Flt64 = one`。
    /// 给个标量会让"某些槽更贵"这件事无处表达。
    ///
    /// **Both threshold and weight are per slot**: the baseline signature is per-slot for each; a scalar would leave "some slots cost more" inexpressible.
    /// </remarks>
    public ResourceOverQuantityMinimization(
        IResourceUsage<S, R, C, V> quantity,
        Func<S, Flt64>? threshold = null,
        Func<S, Flt64>? coefficient = null,
        string? name = null) {
        _quantity = quantity;
        _threshold = threshold ?? (_ => Flt64.Zero);
        _coefficient = coefficient ?? (_ => Flt64.One);
        _name = name ?? "resource_over_capacity_minimization";
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 把每个槽的over量按权重累加成一个目标。三件照抄基线：
    ///
    /// 1. **阈值 0 时目标项是松弛符号本身**（每个槽一个单项式）；阈值大于 0 时另建一条带阈值的松弛并加进模型，
    ///    目标项变成它的正部。**不能拿适配器的多项式当目标项**：那是 `pos - neg`，会把不该计费的方向也按负价算进去。
    /// 2. **权重乘在单项式系数上**，乘在松弛内部会让同一个松弛在别处被复用时不带这个权重（B701 的同一课）。
    /// 3. **表未注册就失败**，不能当零计费——零目标会让模型照样可解，缺陷只在产出上留痕。
    ///
    /// Accumulates every slot's over quantity, weighted, into one objective. Three things are copied from the baseline: **at a zero threshold the term is the slack itself**, not its
    /// polynomial — expanding the adapter yields the sum of both directions, which looks added while charging what should not be charged; a positive threshold builds a separate thresholded
    /// slack and adds it to the model. **The weight multiplies the monomial coefficients** rather than sitting inside the slack, which would drop it wherever else that slack is reused (the
    /// same lesson as B701). And **an unregistered table fails** rather than charging zero, since a zero objective leaves the model solvable and the defect visible only in output.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "资源over量目标只能施加到线性元模型 / the resource over quantity objective can only be applied to a linear meta model"));
        }
        if (_quantity.OverQuantity is not IReadOnlyDictionary<S, ILinearIntermediateSymbol<Flt64>> table) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "资源使用尚未注册，取不到over量表 / the resource usage has not been registered, so its over quantity table is unavailable"));
        }

        return ProduceSlackHelper.BuildObjective(
            linear, table, _quantity.TimeSlots,
            _threshold, _coefficient,
            _quantity.Name + "_over_quantity_minimization_threshold",
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            "resource over quantity",
            skipWhenEmpty: true,
            id: slot => slot.Index.ToString());
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
