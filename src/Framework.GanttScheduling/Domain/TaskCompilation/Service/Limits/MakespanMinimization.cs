#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 最大完工时间最小化 / Makespan minimization
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class MakespanMinimization<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly object _makespan;
    private readonly Flt64 _coefficient;
    private readonly string _name;

    /// <summary>
    /// 最大完工时间最小化构造 / Makespan minimization constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="makespan">最大完工时间对象 / Makespan object</param>
    /// <param name="coefficient">成本系数 / Cost coefficient</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public MakespanMinimization(
        TimeWindow<Flt64> timeWindow,
        object makespan,
        Flt64? coefficient = null,
        string name = "makespan_minimization") {
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        _makespan = makespan;
        _coefficient = coefficient ?? Flt64.One;
        _name = name;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="makespan">最大完工时间对象 / Makespan object</param>
    /// <param name="coefficient">成本系数 / Cost coefficient</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public MakespanMinimization(
        SolverTimeWindowBoundary timeBoundary,
        object makespan,
        Flt64? coefficient = null,
        string name = "makespan_minimization") {
        _timeBoundary = timeBoundary;
        _makespan = makespan;
        _coefficient = coefficient ?? Flt64.One;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) => Results.Ok<Success>(Results.SuccessInstance);
}
