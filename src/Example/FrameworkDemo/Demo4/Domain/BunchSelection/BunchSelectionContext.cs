#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchSelection;

/// <summary>
/// 分支定价算法中批次选择操作的上下文。
/// Context for bunch selection operations in the branch-and-price algorithm.
/// </summary>
/// <remarks>
/// 持有批次生成和编译上下文，提供两者共享的输入。
/// Holds bunch generation and compilation contexts, providing shared inputs for both.
/// </remarks>
/// <param name="aircrafts">所有飞机 / All aircraft.</param>
/// <param name="recoveryNeededAircrafts">需要恢复的飞机 / Aircraft needing recovery.</param>
/// <param name="recoveryNeededFlightTasks">需要恢复的航班任务 / Flight tasks needing recovery.</param>
/// <param name="timeWindow">时间窗口 / Time window.</param>
/// <param name="bunchGenerationContext">批次生成上下文 / Bunch generation context.</param>
/// <param name="bunchCompilationContext">批次编译上下文 / Bunch compilation context.</param>
/// <param name="parameter">列生成系数参数 / Column generation coefficient parameters.</param>
public sealed class BunchSelectionContext(
    IReadOnlyList<Aircraft> aircrafts,
    IReadOnlyList<Aircraft> recoveryNeededAircrafts,
    IReadOnlyList<FlightTask> recoveryNeededFlightTasks,
    TimeWindow<Flt64> timeWindow,
    BunchGenerationContext bunchGenerationContext,
    BunchCompilationContext bunchCompilationContext,
    Parameter? parameter = null) {
    /// <summary>所有飞机 / All aircraft.</summary>
    public IReadOnlyList<Aircraft> Aircrafts { get; } = aircrafts;

    /// <summary>需要恢复的飞机 / Aircraft needing recovery.</summary>
    public IReadOnlyList<Aircraft> RecoveryNeededAircrafts { get; } = recoveryNeededAircrafts;

    /// <summary>需要恢复的航班任务 / Flight tasks needing recovery.</summary>
    public IReadOnlyList<FlightTask> RecoveryNeededFlightTasks { get; } = recoveryNeededFlightTasks;

    /// <summary>时间窗口 / Time window.</summary>
    public TimeWindow<Flt64> TimeWindow { get; } = timeWindow;

    /// <summary>批次生成上下文 / Bunch generation context.</summary>
    public BunchGenerationContext BunchGenerationContext { get; } = bunchGenerationContext;

    /// <summary>批次编译上下文 / Bunch compilation context.</summary>
    public BunchCompilationContext BunchCompilationContext { get; } = bunchCompilationContext;

    /// <summary>列生成系数参数 / Column generation coefficient parameters.</summary>
    public Parameter Parameter { get; } = parameter ?? new Parameter();

    /// <summary>
    /// 初始化批次编译聚合。
    /// Initializes the bunch compilation aggregation.
    /// </summary>
    /// <param name="originBunches">初始批次 / Initial bunches.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try InitCompilation(IReadOnlyList<FlightTaskBunch> originBunches) {
        var aggregation = new BunchCompilation.Aggregation(
            timeWindow: TimeWindow,
            recoveryNeededAircrafts: RecoveryNeededAircrafts,
            recoveryNeededFlightTasks: RecoveryNeededFlightTasks);
        BunchCompilationContext.SetAggregation(aggregation);
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
