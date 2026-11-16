#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;

/// <summary>
/// 束编译聚合，组合任务时间用于列生成过程。
/// Aggregation for bunch compilation combining task time for the column generation process.
/// </summary>
/// <remarks>
/// 继承框架 BunchCompilationAggregation，提供任务时间和完工时间管理。
/// Extends framework BunchCompilationAggregation, providing task time and makespan management.
/// </remarks>
public sealed class Aggregation
    : BunchCompilationAggregation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> {
    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window.</param>
    /// <param name="recoveryNeededAircrafts">需要恢复的飞机列表 / List of aircraft needing recovery.</param>
    /// <param name="recoveryNeededFlightTasks">需要恢复的航班任务列表 / List of flight tasks needing recovery.</param>
    /// <param name="flows">流量规则 / The flow rules.</param>
    /// <param name="originBunches">初始束，用于机队检查点 / The origin bunches used for the fleet checkpoints.</param>
    /// <param name="links">任务链接 / The task links.</param>
    /// <param name="withCapacity">是否建航班容量层，缺省按任务推断 / Whether to build the flight capacity layer, inferred from the tasks when omitted.</param>
    public Aggregation(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<Aircraft> recoveryNeededAircrafts,
        IReadOnlyList<FlightTask> recoveryNeededFlightTasks,
        IReadOnlyList<Flow>? flows = null,
        IReadOnlyList<FlightTaskBunch>? originBunches = null,
        IReadOnlyList<Link>? links = null,
        bool? withCapacity = null)
        : base(recoveryNeededFlightTasks, recoveryNeededAircrafts) {
        RecoveryNeededAircrafts = recoveryNeededAircrafts;
        RecoveryNeededFlightTasks = recoveryNeededFlightTasks;
        TaskTime = new BunchSchedulingTaskTime<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>(
            timeWindow, recoveryNeededFlightTasks, Compilation);
        // **流量是连接资源用量**：`flow` 就是这么装的，只是 C# 侧此前连 `Flow` 都当不了资源。
        // 缺省为空列表——**没有流量规则时不该建出任何流量行**，而不是建一堆恒零的符号。
        //
        // **Flows are connection resource usage**: the code wires `flow` exactly this way, except the C# side could not even treat `Flow` as a resource . The default is an empty
        // list — **no flow rules must build no flow rows**, rather than a pile of always-zero symbols.
        Flow = new BunchSchedulingConnectionResourceUsage<Flow, FlowControl, Flt64>(
            timeWindow, flows ?? Array.Empty<Flow>(), "flow");
        // **剩下三样按装配顺序补齐**：机队平衡、任务链接、航班容量。三者都只在有输入时才建符号，
        // 所以缺省的空列表不会让模型里多出一堆恒零符号。
        //
        // **The remaining three follow its assembly order**: fleet balance, task links and flight capacity. Each builds symbols only when it has input, so the default empty
        // lists add no always-zero symbols.
        FleetBalance = new FleetBalance(recoveryNeededAircrafts, originBunches ?? Array.Empty<FlightTaskBunch>(), Compilation);
        FlightLink = new FlightLink(links ?? Array.Empty<Link>(), Compilation);
        FlightCapacity = withCapacity ?? (recoveryNeededFlightTasks.Count > 0)
            ? new FlightCapacity(recoveryNeededFlightTasks, Compilation)
            : null;
    }

    /// <summary>需要恢复的飞机列表 / List of aircraft needing recovery.</summary>
    public IReadOnlyList<Aircraft> RecoveryNeededAircrafts { get; }

    /// <summary>需要恢复的航班任务列表 / List of flight tasks needing recovery.</summary>
    public IReadOnlyList<FlightTask> RecoveryNeededFlightTasks { get; }

    /// <summary>任务时间 / Task time.</summary>
    public BunchSchedulingTaskTime<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> TaskTime { get; }

    /// <summary>流量连接资源用量 / The flow connection resource usage.</summary>
    public BunchSchedulingConnectionResourceUsage<Flow, FlowControl, Flt64> Flow { get; }

    /// <summary>机队平衡 / The fleet balance.</summary>
    public FleetBalance FleetBalance { get; }

    /// <summary>任务链接 / The task links.</summary>
    public FlightLink FlightLink { get; }

    /// <summary>航班容量 / The flight capacity, null when no task carries capacity.</summary>
    public FlightCapacity? FlightCapacity { get; }

    /// <inheritdoc/>
    public override Try Register(object model) {
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                ErrorCode.IllegalArgument,
                "产能聚合只能注册到线性元模型 / the capacity aggregation can only register into a linear meta model");
        }
        Try result = base.Register(model);
        if (result.IsFailed) {
            return result;
        }
        // **顺序照基线**：taskTime → flow → fleetBalance → flightLink → flightCapacity。
        // 任何一步失败就整体失败——半装的聚合在求解时表现为"约束莫名不紧"，比直接失败难查得多。
        //
        // **The order follows the code**: taskTime, flow, fleetBalance, flightLink, flightCapacity. A failure anywhere aborts the whole registration — a half-wired aggregation shows
        // up at solve time as "the constraint is mysteriously slack", much harder to diagnose than a straight failure.
        Try taskTime = TaskTime.Register(model);
        if (taskTime.IsFailed) {
            return taskTime;
        }
        Try flow = Flow.Register(model);
        if (flow.IsFailed) {
            return flow;
        }
        Try fleetBalance = FleetBalance.Register(linear);
        if (fleetBalance.IsFailed) {
            return fleetBalance;
        }
        Try flightLink = FlightLink.Register(linear);
        if (flightLink.IsFailed) {
            return flightLink;
        }
        if (FlightCapacity is null) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        return FlightCapacity.Register(linear);
    }

    /// <inheritdoc/>
    public override Result<IReadOnlyList<FlightTaskBunch>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<FlightTaskBunch> newBunches,
        object model) {
        var result = base.AddColumns(iteration, newBunches, model);
        if (result.IsFailed) {
            return result;
        }
        var undupBunches = ((Ok<IReadOnlyList<FlightTaskBunch>, ErrorCode, Error<ErrorCode>>)result).Value;
        Try taskTime = TaskTime.AddColumns(iteration, undupBunches, model);
        if (taskTime.IsFailed) {
            return Results.Failed<IReadOnlyList<FlightTaskBunch>>(taskTime switch {
                Failed<Success, ErrorCode, Error<ErrorCode>> failed => failed.Error,
                _ => new Err<ErrorCode>(ErrorCode.ApplicationError, "任务时间加列失败 / task time column addition failed"),
            });
        }
        // **注册接上了，加列也要接上**： 只接了注册，于是容量/机队/链接三层的表达式永远是空的，
        // 求解成功但所有数字都是 0——**"求出来了"最容易骗过人的形态**。 的真实求解判据就是在这里红的。
        //
        // **Wiring registration is not enough; column addition must follow**: only wired registration, so the capacity, fleet and link expressions stayed empty, the solve succeeded and
        // every number came back 0 — **the most deceptive form of "it solved"**. the earlier real-solve criterion is what turned red here.
        Try fleetBalance = FleetBalance.AddColumns(iteration, undupBunches);
        if (fleetBalance.IsFailed) {
            return Results.Failed<IReadOnlyList<FlightTaskBunch>>(fleetBalance switch {
                Failed<Success, ErrorCode, Error<ErrorCode>> failed => failed.Error,
                _ => new Err<ErrorCode>(ErrorCode.ApplicationError, "机队加列失败 / fleet balance column addition failed"),
            });
        }
        Try flightLink = FlightLink.AddColumns(iteration, undupBunches);
        if (flightLink.IsFailed) {
            return Results.Failed<IReadOnlyList<FlightTaskBunch>>(flightLink switch {
                Failed<Success, ErrorCode, Error<ErrorCode>> failed => failed.Error,
                _ => new Err<ErrorCode>(ErrorCode.ApplicationError, "链接加列失败 / link column addition failed"),
            });
        }
        if (FlightCapacity is not null) {
            Try capacity = FlightCapacity.AddColumns(iteration, undupBunches);
            if (capacity.IsFailed) {
                return Results.Failed<IReadOnlyList<FlightTaskBunch>>(capacity switch {
                    Failed<Success, ErrorCode, Error<ErrorCode>> failed => failed.Error,
                    _ => new Err<ErrorCode>(ErrorCode.ApplicationError, "容量加列失败 / capacity column addition failed"),
                });
            }
        }
        return Results.Ok(undupBunches);
    }
}
