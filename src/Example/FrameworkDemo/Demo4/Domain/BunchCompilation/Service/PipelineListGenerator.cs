#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service.Limits;
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

        // `BunchCostMinimization` 的构造参数现在是 `ITaskCost`，泛型不变性不再挡路（B566），所以这条目标真的接上了。
        // The `BunchCostMinimization` constructor now takes `ITaskCost`, so invariance no longer blocks it (B566) and this objective is actually wired.
        pipelines.Add(new BunchCostMinimization<FlightTask, Aircraft, FlightTaskAssignment>(aggregation.Compilation));

        // 两条"编译无关"的目标现在真的接上了（B569）：它们只要 `ICompilation` + `ICompilationColumns`，
        // 而任务束编译从 B563 起就实现了后者。原来那句"注册进来只会让管线失败"是因为当时接口没实现，
        // 不是目标本身的问题。
        // The two compilation-agnostic objectives are wired now (B569): they need only `ICompilation` + `ICompilationColumns`, and the bunch compilation has implemented the
        // latter since B563. The old note said "registering them would only fail" because the interface was missing then — not because of the objectives.
        pipelines.Add(new TaskCancelMinimization<FlightTask, Aircraft, FlightTaskAssignment>(
            tasks: aggregation.RecoveryNeededFlightTasks,
            compilation: aggregation.Compilation,
            coefficient: task => new Flt64(1)));
        pipelines.Add(new ExecutorLeisureMinimization<Aircraft, FlightTaskAssignment>(
            executors: aggregation.RecoveryNeededAircrafts,
            compilation: aggregation.Compilation,
            coefficient: _ => new Flt64(1)));
        // **机队平衡的限制与目标（B753）**：约束与目标两半成对，缺一半都会让模型"可行但无意义"。
        // **The fleet balance's constraint and objective (B753)**: the two halves are a pair, and dropping either leaves the model feasible but meaningless.
        pipelines.Add(new FleetBalanceLimit(aggregation.FleetBalance));
        // **任务链接的限制与目标（B754）**：与机队那一条结构相同、右端项恒为 1（接上一次就成立）。
        // **The task links' constraint and objective (B754)**: the same shape as the fleet one with a right-hand side of 1 (stitched once is enough).
        pipelines.Add(new FlightLinkLimit(aggregation.FlightLink));

        return Results.Ok<IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>>(pipelines);
    }
}
