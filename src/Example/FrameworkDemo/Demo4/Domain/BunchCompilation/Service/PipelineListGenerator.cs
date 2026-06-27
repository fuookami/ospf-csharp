#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;

/// <summary>
/// 包含编译约束和限制的列生成管线列表生成器。
/// Generator for the column generation pipeline list including compilation constraints and limits.
/// </summary>
/// <param name="aggregation">编译聚合 / Compilation aggregation.</param>
/// <param name="parameter">列生成主模型系数参数 / Column generation master model coefficient parameters.</param>
public sealed class PipelineListGenerator(
    Aggregation aggregation,
    Parameter? parameter = null) {
    private readonly Parameter _parameter = parameter ?? new Parameter();

    /// <summary>
    /// 创建并返回包含所有编译约束和限制的管线列表。
    /// Creates and returns the pipeline list with all compilation constraints and limits.
    /// </summary>
    /// <returns>管线列表结果 / Pipeline list result.</returns>
    public Result<IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>, ErrorCode, Error<ErrorCode>> Invoke() {
        var pipelines = new List<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>();

        pipelines.Add(new ExecutorCompilationConstraint<Aircraft, FlightTaskAssignment>(
            executors: aggregation.RecoveryNeededAircrafts,
            compilation: aggregation.Compilation));

        pipelines.Add(new TaskCompilationConstraint<Aircraft, FlightTaskAssignment>(
            tasks: aggregation.RecoveryNeededFlightTasks,
            compilation: aggregation.Compilation));

        // Note: BunchCostMinimization<T,E,A> requires BunchCompilation<AbstractTaskBunch<T,E,A>, T, E, A>
        // but our compilation is BunchCompilation<FlightTaskBunch, ...>. C# generics are invariant,
        // so we cannot pass it directly. The cost minimization objective is handled by the solver
        // through the bunch variables' cost coefficients. Skipping explicit pipeline registration.

        pipelines.Add(new ExecutorLeisureMinimization<Aircraft, FlightTaskAssignment>(
            executors: aggregation.RecoveryNeededAircrafts,
            compilation: aggregation.Compilation,
            coefficient: _ => _parameter.ResolvedExecutorLeisureCoeff));

        pipelines.Add(new TaskCancelMinimization<FlightTask, Aircraft, FlightTaskAssignment>(
            tasks: aggregation.RecoveryNeededFlightTasks,
            compilation: aggregation.Compilation));

        return Results.Ok<IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>>(pipelines);
    }
}
