#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>消耗接口 / Consumption interface</summary>
public interface IConsumption {
    /// <summary>消耗量变量 / Quantity variables (indexed by material)</summary>
    object Quantity { get; }

    /// <summary>超量变量 / Over quantity variables</summary>
    object? OverQuantity { get; }

    /// <summary>不足量变量 / Less quantity variables</summary>
    object? LessQuantity { get; }

    /// <summary>是否启用超量 / Whether over quantity is enabled</summary>
    bool OverEnabled { get; }

    /// <summary>是否启用不足 / Whether less quantity is enabled</summary>
    bool LessEnabled { get; }

    /// <summary>注册到模型 / Register to model</summary>
    Try Register(object model);

    /// <summary>刷新影子价格 / Refresh shadow prices</summary>
    Try Refresh(object shadowPriceMap, object shadowPrices);
}

/// <summary>
/// 抽象消耗 / Abstract consumption
/// </summary>
/// <remarks>
/// 提供消耗量管理的通用框架，包括超量和不足量的松弛变量注册及影子价格提取。
/// Provides a common framework for consumption quantity management, including slack variable registration and shadow price extraction.
/// </remarks>
/// <typeparam name="C">材料类型 / Material type</typeparam>
/// <param name="Materials">材料与储备列表 / List of materials and reserves</param>
public abstract class AbstractConsumption<C>(
    IReadOnlyList<(C Material, MaterialReserves? Reserves)> Materials
) : IConsumption where C : IMaterial {
    /// <summary>材料与储备列表（按索引排序）/ Materials and reserves sorted by index</summary>
    protected IReadOnlyList<(C Material, MaterialReserves? Reserves)> SortedMaterials
        = Materials.OrderBy(m => m.Material.Index).ToList();

    /// <inheritdoc/>
    public abstract object Quantity { get; }

    /// <inheritdoc/>
    public object? OverQuantity { get; protected set; }

    /// <inheritdoc/>
    public object? LessQuantity { get; protected set; }

    /// <inheritdoc/>
    public abstract bool OverEnabled { get; }

    /// <inheritdoc/>
    public abstract bool LessEnabled { get; }

    /// <inheritdoc/>
    public virtual Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <inheritdoc/>
    public virtual Try Refresh(object shadowPriceMap, object shadowPrices) => Results.Ok<Success>(Results.SuccessInstance);
}

/// <summary>
/// 任务调度消耗 / Task scheduling consumption
/// </summary>
/// <remarks>
/// 暂未实现，请使用 BunchSchedulingConsumption。
/// Not yet implemented, please use BunchSchedulingConsumption.
/// </remarks>
/// <typeparam name="C">材料类型 / Material type</typeparam>
public sealed class TaskSchedulingConsumption<C>(
    IReadOnlyList<(C Material, MaterialReserves? Reserves)> Materials,
    bool OverEnabledFlag = false,
    bool LessEnabledFlag = false
) : AbstractConsumption<C>(Materials) where C : IMaterial {
    private readonly object _quantity = new();

    /// <inheritdoc/>
    public override object Quantity => _quantity;

    /// <inheritdoc/>
    public override bool OverEnabled => OverEnabledFlag;

    /// <inheritdoc/>
    public override bool LessEnabled => LessEnabledFlag;

    /// <inheritdoc/>
    public override Try Register(object model) {
        return new Failed<Success, ErrorCode, Error<ErrorCode>>(
            new Err<ErrorCode>(ErrorCode.ApplicationFailed,
                "TaskSchedulingConsumption.Register is not implemented. Use BunchSchedulingConsumption instead."));
    }
}

/// <summary>
/// 任务束调度消耗 / Bunch scheduling consumption
/// </summary>
/// <remarks>
/// 用于列生成场景，支持通过 addColumns 追加消耗贡献。
/// Used for column generation scenarios, supports adding consumption contribution through addColumns.
/// </remarks>
/// <typeparam name="C">材料类型 / Material type</typeparam>
public sealed class BunchSchedulingConsumption<C> : AbstractConsumption<C>
    where C : IMaterial {
    private readonly object _quantity = new();

    /// <summary>
    /// 任务束调度消耗构造 / Bunch scheduling consumption constructor
    /// </summary>
    /// <param name="materials">材料与储备列表 / List of materials and reserves</param>
    public BunchSchedulingConsumption(IReadOnlyList<(C Material, MaterialReserves? Reserves)> materials)
        : base(materials) { }

    /// <inheritdoc/>
    public override object Quantity => _quantity;

    /// <inheritdoc/>
    public override bool OverEnabled => true;

    /// <inheritdoc/>
    public override bool LessEnabled => true;

    /// <inheritdoc/>
    public override Try Register(object model) => base.Register(model);
}
