#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;

/// <summary>
/// 产能调度上下文的默认实现 / The default capacity scheduling context.
/// </summary>
/// <remarks>
/// **这是产能调度一直缺的装配点。** `ICapacitySchedulingContext` 从 B4xx 起就在，但**没有任何实现**——
/// 于是决策变量、排序变量、三族约束、成本目标与资源用量表
/// 全是"零件齐了、没装起来"。一个没有实现的接口比没有接口更糟：它让人以为整条链路已经能跑。
///
/// 装配顺序：**先建变量、再建约束、最后建目标**。顺序不能换——约束里引用的符号必须已经在模型里，
/// 目标也一样；反过来会把"符号不在模型里"变成运行期错误，而不是注册期失败。
///
/// **This is the assembly point capacity scheduling has been missing.** `ICapacitySchedulingContext` has existed since B4xx with **no implementation at all**, so the earlier decision variables,
/// the earlier order variables, the earlier three constraint families, the earlier cost objective and the earlier resource usage table were all "parts present, never assembled". An interface with no
/// implementation is worse than no interface: it makes readers believe the whole chain already runs.
///
/// The wiring order is **variables first, then constraints, then the objective**. The order matters: symbols the constraints reference must already be in the model, and
/// the same holds for the objective; reversing it turns "the symbol is not in the model" into a runtime error instead of a registration-time failure.
/// </remarks>
/// <typeparam name="A">生产动作类型 / Production action type</typeparam>
public sealed class CapacitySchedulingContext<A> : ICapacitySchedulingContext<A>
    where A : IProductionAction {
    private readonly CapacityOrderVariables<A> _variables;

    private readonly IReadOnlyList<object> _constraints;

    private readonly CapacityCostMinimization<A>? _objective;

    private readonly CapacityLoadExpressions<A> _load;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="actions">生产动作列表 / The production actions.</param>
    /// <param name="slots">时隙列表 / The time slots.</param>
    /// <param name="timeWindow">时间窗口 / The time window.</param>
    /// <param name="compilation">产能编译对象 / The capacity compilation.</param>
    /// <param name="maxOrderPerSlot">每时隙最大顺序数 / The maximum number of order positions per slot.</param>
    /// <param name="withCostObjective">是否挂成本目标 / Whether to attach the cost objective.</param>
    public CapacitySchedulingContext(
        IReadOnlyList<A> actions,
        IReadOnlyList<TimeRange> slots,
        TimeWindow<Flt64> timeWindow,
        CapacityOrderCompilation<A> compilation,
        ulong maxOrderPerSlot,
        bool withCostObjective = true) {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(compilation);
        Actions = actions;
        Slots = slots;
        TimeWindow = timeWindow;
        Compilation = compilation;
        MaxOrderPerSlot = maxOrderPerSlot;
        _variables = compilation.Variables
            ?? new CapacityOrderVariables<A>(actions, slots, timeWindow, maxOrderPerSlot);
        _load = new CapacityLoadExpressions<A>(_variables, timeWindow);
        _constraints = new List<object> {
            new OrderConstraint<A>(_variables),
            new ExecutorCapacityConstraint<A>(_load, timeWindow),
        };
        _objective = withCostObjective ? new CapacityCostMinimization<A>(_variables) : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<A> Actions { get; }

    /// <inheritdoc/>
    public IReadOnlyList<TimeRange> Slots { get; }

    /// <inheritdoc/>
    public TimeWindow<Flt64> TimeWindow { get; }

    /// <inheritdoc/>
    public ICapacity<A> Compilation { get; }

    /// <summary>每时隙最大顺序数 / The maximum number of order positions per slot.</summary>
    public ulong MaxOrderPerSlot { get; }

    /// <summary>排序变量层 / The order variable layer.</summary>
    public CapacityOrderVariables<A> Variables => _variables;

    /// <inheritdoc/>
    /// <remarks>
    /// 三步按序执行：变量 → 约束 → 目标。**任何一步失败就整体失败**，不继续装剩下的——半装的模型在求解时会表现为
    /// "约束莫名不紧"，比直接失败难查得多。
    ///
    /// Three steps in order: variables, constraints, objective. **A failure anywhere aborts the whole registration** rather than continuing — a half-wired model shows up at solve time as
    /// "the constraint is mysteriously slack", which is much harder to diagnose than a straight failure.
    /// </remarks>
    public Try Register(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "产能调度上下文只能注册到线性元模型 / the capacity scheduling context can only be registered into a linear meta model"));
        }

        Try variables = _variables.Register(linear);
        if (!variables.IsOk) {
            return variables;
        }
        Try load = _load.Register(linear);
        if (!load.IsOk) {
            return load;
        }

        foreach (object constraint in _constraints) {
            Try applied = constraint switch {
                OrderConstraint<A> order => order.Invoke(linear),
                ExecutorCapacityConstraint<A> capacity => capacity.Invoke(linear),
                _ => Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "未知的约束类型 / unknown constraint type")),
            };
            if (!applied.IsOk) {
                return applied;
            }
        }

        return _objective is null ? Results.Ok<Success>(Results.SuccessInstance) : _objective.Invoke(linear);
    }

    /// <inheritdoc/>
    public Result<CapacitySchedulingSolution<A>, ErrorCode, Error<ErrorCode>> ExtractSolution(object model) =>
        Compilation.ExtractSolution(model);
}
