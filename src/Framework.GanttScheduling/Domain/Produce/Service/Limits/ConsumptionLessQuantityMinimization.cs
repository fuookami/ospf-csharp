#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Service.Limits;
/// <summary>
/// 消费下限数量最小化 / Consumption less quantity minimization
/// </summary>
/// <typeparam name="C">材料类型 / Material type</typeparam>
public sealed class ConsumptionLessQuantityMinimization<C> : IPipeline<object>
    where C : IMaterial {
    private readonly IReadOnlyList<(C Material, MaterialReserves? Reserves)> _materials;
    private readonly IConsumption _consumption;
    private readonly Func<C, Flt64> _threshold;
    private readonly Func<C, Flt64> _coefficient;

    /// <summary>
    /// 消费下限数量最小化构造 / Consumption less quantity minimization constructor
    /// </summary>
    /// <param name="materials">材料与储备对列表 / List of material-reserve pairs</param>
    /// <param name="consumption">消费对象 / Consumption object</param>
    /// <param name="threshold">阈值函数 / Threshold function</param>
    /// <param name="coefficient">成本系数函数 / Cost coefficient function</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public ConsumptionLessQuantityMinimization(
        IReadOnlyList<(C Material, MaterialReserves? Reserves)> materials,
        IConsumption consumption,
        Func<C, Flt64>? threshold = null,
        Func<C, Flt64>? coefficient = null,
        string name = "consumption_less_quantity_minimization") {
        _materials = consumption.LessEnabled
            ? materials.Where(m => m.Reserves?.LessEnabled == true).ToList()
            : Array.Empty<(C, MaterialReserves?)>();
        _consumption = consumption;
        _threshold = threshold ?? (_ => Flt64.Zero);
        _coefficient = coefficient ?? (_ => Flt64.One);
        Name = name;
    }

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    /// <inheritdoc/>
    /// <remarks>
    /// 阈值 0 时直接计费数量符号，非零时建一条**自带约束**的阈值松弛再计费它的正部；系数在松弛外面乘。
    /// 空列表时**连目标都不加**（基线外层有 `if (materials.isNotEmpty)`）。
    ///
    /// A zero threshold charges the quantity symbol itself; a non-zero one builds a threshold slack that **carries its own rows** and charges its positive part, with the coefficient multiplying
    /// outside the slack. An empty list adds **no objective at all** (the baseline wraps these in `if (materials.isNotEmpty)`).
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "数量目标只能施加到线性元模型 / a quantity objective can only be applied to a linear meta model"));
        }
        // **表是 `IReadOnlyDictionary<T, ILinearIntermediateSymbol<Flt64>>`，不是 `<T, object>`**：
        // 值类型不协变，按 `<T, object>` 匹配永远失败——那样目标会一直报"未注册"，而它其实已注册。
        // **The table is `IReadOnlyDictionary<T, ILinearIntermediateSymbol<Flt64>>`, not `<T, object>`**: value types do not covary, so matching on `<T, object>` never succeeds — the objective
        // would then always report "unregistered" while it is in fact registered.
        if (_consumption.LessQuantity is not IReadOnlyDictionary<C, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>> table) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "消耗尚未注册，取不到消耗量表 / the consumption has not been registered, so its consumption-quantity table is unavailable"));
        }

        return ProduceSlackHelper.BuildObjective(
            linear, table, _materials.Select(m => m.Material).ToList(),
            _threshold, _coefficient,
            "consumption_less_quantity_minimization_threshold",
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            "consumption less quantity",
            skipWhenEmpty: true);
    }
}
