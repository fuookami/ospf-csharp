#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;

/// <summary>
/// 管理聚合和管线注册的批次编译上下文。
/// Context for bunch compilation managing aggregation and pipeline registration.
/// </summary>
/// <param name="parameter">列生成主模型系数参数 / Column generation master model coefficient parameters.</param>
public sealed class BunchCompilationContext(Parameter? parameter = null)
    : IBunchCompilationContext<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> {
    private readonly Parameter _parameter = parameter ?? new Parameter();
    private IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>> _pipelineList
        = Array.Empty<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>();

    /// <inheritdoc/>
    public BunchCompilationAggregation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> Aggregation { get; private set; } = null!;

    /// <inheritdoc/>
    public IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>> PipelineList => _pipelineList;

    /// <summary>
    /// 注册管线列表并委托父类注册。
    /// Registers the pipeline list and delegates to the parent registration.
    /// </summary>
    /// <param name="model">线性元模型 / Linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(object model) {
        if (Aggregation is not Aggregation aggregation) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationError, "Aggregation not set or wrong type"));
        }

        var generator = new PipelineListGenerator(aggregation, _parameter);
        var pipelineResult = generator.Invoke();
        if (pipelineResult is not Ok<IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>, ErrorCode, Error<ErrorCode>> ok) {
            return pipelineResult switch {
                Failed<IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>, ErrorCode, Error<ErrorCode>> f => Results.Failed<Success>(f.Error),
                Fatal<IReadOnlyList<ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>>, ErrorCode, Error<ErrorCode>> ft => new Fatal<Success, ErrorCode, Error<ErrorCode>>(ft.Errors),
                _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationError, "Pipeline generation failed"))
            };
        }
        _pipelineList = ok.Value;

        // Register pipelines to model
        foreach (var pipeline in _pipelineList) {
            pipeline.Register(model);
            Try ret = pipeline.Invoke(model);
            if (ret.IsFailed) {
                return ret;
            }
        }

        return Aggregation.Register(model);
    }

    /// <inheritdoc/>
    public Result<IReadOnlyList<FlightTaskBunch>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<FlightTaskBunch> newBunches,
        object model) => Aggregation.AddColumns(iteration, newBunches, model);

    /// <inheritdoc/>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> RemoveColumns(
        Flt64 maximumReducedCost,
        ulong maximumColumnAmount,
        Func<FlightTaskBunch, Flt64> reducedCost,
        ISet<FlightTaskBunch> fixedBunches,
        ISet<FlightTaskBunch> keptBunches,
        object model) => Aggregation.RemoveColumns(maximumReducedCost, maximumColumnAmount, reducedCost, fixedBunches, keptBunches, model);

    /// <inheritdoc/>
    public Try ExtractShadowPrice(
        GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        foreach (var pipeline in _pipelineList) {
            Try ret = pipeline.Refresh(shadowPriceMap, model, shadowPrices);
            if (ret.IsFailed) {
                return ret;
            }

            ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>? extractor = pipeline.Extractor();
            if (extractor is not null) {
                shadowPriceMap.Put(extractor);
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public Result<ISet<FlightTaskBunch>, ErrorCode, Error<ErrorCode>> ExtractFixedBunches(ulong iteration, object model)
        => Aggregation.ExtractFixedBunches(iteration, model);

    /// <inheritdoc/>
    public Result<ISet<FlightTaskBunch>, ErrorCode, Error<ErrorCode>> ExtractKeptBunches(ulong iteration, object model)
        => Aggregation.ExtractKeptBunches(iteration, model);

    /// <inheritdoc/>
    public Result<IReadOnlyDictionary<FlightTaskBunch, Flt64>, ErrorCode, Error<ErrorCode>> ExtractKeptBunchesWithRatio(ulong iteration, object model)
        => Aggregation.ExtractKeptBunchesWithRatio(iteration, model);

    /// <inheritdoc/>
    public Result<ISet<Aircraft>, ErrorCode, Error<ErrorCode>> ExtractHiddenExecutors(IReadOnlyList<Aircraft> executors, object model)
        => Aggregation.ExtractHiddenExecutors(executors, model);

    /// <inheritdoc/>
    public Try GloballyFix(ISet<FlightTaskBunch> fixedBunches)
        => Aggregation.GloballyFix(fixedBunches);

    /// <inheritdoc/>
    public Result<ISet<FlightTaskBunch>, ErrorCode, Error<ErrorCode>> LocallyFix(
        ulong iteration,
        Flt64 bar,
        ISet<FlightTaskBunch> fixedBunches,
        object model)
        => Aggregation.LocallyFix(iteration, bar, fixedBunches, model);

    /// <inheritdoc/>
    public Result<TaskSolution<FlightTask, Aircraft, FlightTaskAssignment>, ErrorCode, Error<ErrorCode>> AnalyzeTaskSolution(
        ulong iteration,
        IReadOnlyList<FlightTask> tasks,
        object model,
        IReadOnlyList<Flt64>? solution = null) {
        // Analyze bunch solution first, then extract task solution from it
        var bunchResult = AnalyzeBunchSolution(iteration, tasks, model, solution);
        if (bunchResult is not Ok<BunchSolution<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>, ErrorCode, Error<ErrorCode>> ok) {
            return bunchResult switch {
                Failed<BunchSolution<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>, ErrorCode, Error<ErrorCode>> f => Results.Failed<TaskSolution<FlightTask, Aircraft, FlightTaskAssignment>>(f.Error),
                Fatal<BunchSolution<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>, ErrorCode, Error<ErrorCode>> ft => new Fatal<TaskSolution<FlightTask, Aircraft, FlightTaskAssignment>, ErrorCode, Error<ErrorCode>>(ft.Errors),
                _ => Results.Failed<TaskSolution<FlightTask, Aircraft, FlightTaskAssignment>>(new Err<ErrorCode>(ErrorCode.ApplicationError, "Analysis failed"))
            };
        }
        var bunchSolution = ok.Value;
        var assignedTasks = new List<FlightTask>();
        var canceledTasks = new List<FlightTask>();
        foreach (var bunch in bunchSolution.Bunches) {
            assignedTasks.AddRange(bunch.Tasks);
        }
        var assignedSet = new HashSet<FlightTask>(assignedTasks);
        foreach (var task in tasks) {
            if (!assignedSet.Contains(task)) {
                canceledTasks.Add(task);
            }
        }
        return Results.Ok(new TaskSolution<FlightTask, Aircraft, FlightTaskAssignment>(assignedTasks, canceledTasks));
    }

    /// <inheritdoc/>
    public Result<BunchSolution<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>, ErrorCode, Error<ErrorCode>> AnalyzeBunchSolution(
        ulong iteration,
        IReadOnlyList<FlightTask> tasks,
        object model,
        IReadOnlyList<Flt64>? solution = null) {
        // Use the framework's BunchSolutionAnalyzer
        if (solution is null) {
            return Results.Ok(new BunchSolution<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>(
                Array.Empty<FlightTaskBunch>(), Array.Empty<FlightTask>()));
        }
        return Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Service.BunchSolutionAnalyzer.Analyze<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>(
            iteration, tasks, Aggregation.BunchesIteration, Aggregation.Compilation, solution);
    }

    /// <summary>
    /// 设置聚合 / Set aggregation.
    /// </summary>
    /// <param name="aggregation">编译聚合 / Compilation aggregation.</param>
    internal void SetAggregation(BunchCompilationAggregation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> aggregation) {
        Aggregation = aggregation;
    }
}
