#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Service.Bunch;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchSelection.Service;

/// <summary>
/// 用于求解航班恢复调度问题的分支定价算法。
/// Branch and price algorithm for solving the flight recovery scheduling problem.
/// </summary>
/// <remarks>
/// 封装框架 BunchBranchAndPriceAlgorithm，通过 demo4 Policy 连接各组件。
/// Wraps framework BunchBranchAndPriceAlgorithm, connecting components via demo4 Policy.
/// </remarks>
/// <param name="context">批次选择上下文 / Bunch selection context.</param>
/// <param name="solver">列生成求解器 / Column generation solver.</param>
/// <param name="configuration">算法配置 / Algorithm configuration.</param>
public sealed class BranchAndPriceAlgorithm(
    BunchSelectionContext context,
    IColumnGenerationSolver solver,
    BranchAndPriceAlgorithm.Configuration? configuration = null) {
    /// <summary>
    /// 分支定价算法配置。
    /// Branch and price algorithm configuration.
    /// </summary>
    /// <param name="BadReducedAmount">约简成本差数量阈值 / Bad reduced cost amount threshold.</param>
    /// <param name="MaximumColumnAmount">最大列数 / Maximum column amount.</param>
    /// <param name="MinimumColumnAmountPerExecutor">每个执行器最小列数 / Minimum column amount per executor.</param>
    /// <param name="TimeLimit">时间限制 / Time limit.</param>
    public sealed record Configuration(
        ulong BadReducedAmount = 20UL,
        ulong MaximumColumnAmount = 50000UL,
        ulong MinimumColumnAmountPerExecutor = 0UL,
        TimeSpan? TimeLimit = null
    );

    private readonly Configuration _configuration = configuration ?? new Configuration();

    private BunchBranchAndPriceAlgorithm<
        FlightShadowPriceMap,
        IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>,
        FlightTaskBunch,
        FlightTask,
        Aircraft,
        FlightTaskAssignment>? _algorithm;

    /// <summary>
    /// 初始化算法（延迟构造）。
    /// Initializes the algorithm (lazy construction).
    /// </summary>
    private BunchBranchAndPriceAlgorithm<
        FlightShadowPriceMap,
        IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>,
        FlightTaskBunch,
        FlightTask,
        Aircraft,
        FlightTaskAssignment> GetOrCreateAlgorithm() {
        if (_algorithm is not null) return _algorithm;

        var policy = new BunchBranchAndPriceAlgorithm<
            FlightShadowPriceMap,
            IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>,
            FlightTaskBunch,
            FlightTask,
            Aircraft,
            FlightTaskAssignment>.Policy(
            ContextBuilder: () => context.BunchCompilationContext,
            ExtractContextBuilder: new List<Func<
                IBunchCompilationContext<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>,
                IReadOnlyList<IExtractBunchCompilationContext<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>>>>(),
            ShadowPriceMap: () => new FlightShadowPriceMap(),
            ReducedCost: (map, bunch) => map.ReducedCost((FlightTaskBunch)bunch),
            BunchGenerator: (iteration, aircrafts, map) => {
                IReadOnlyList<FlightTaskBunch> bunches = context.BunchGenerationContext.GenerateFlightTaskBunch(aircrafts, (long)iteration, map);
                return System.Threading.Tasks.Task.FromResult(Results.Ok<IReadOnlyList<FlightTaskBunch>>(bunches));
            }
        );

        var frameworkConfig = new BunchBranchAndPriceAlgorithm<
            FlightShadowPriceMap,
            IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>,
            FlightTaskBunch,
            FlightTask,
            Aircraft,
            FlightTaskAssignment>.Configuration(
            BadReducedAmount: _configuration.BadReducedAmount,
            MaximumColumnAmount: _configuration.MaximumColumnAmount,
            MinimumColumnAmountPerExecutor: _configuration.MinimumColumnAmountPerExecutor,
            TimeLimit: _configuration.TimeLimit
        );

        _algorithm = new BunchBranchAndPriceAlgorithm<
            FlightShadowPriceMap,
            IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>,
            FlightTaskBunch,
            FlightTask,
            Aircraft,
            FlightTaskAssignment>(
            executors: context.RecoveryNeededAircrafts,
            tasks: context.RecoveryNeededFlightTasks,
            initialBunches: context.BunchGenerationContext.InitialFlightBunches,
            solver: solver,
            policy: policy,
            configuration: frameworkConfig
        );

        return _algorithm;
    }

    /// <summary>
    /// 执行分支定价算法。
    /// Executes the branch and price algorithm.
    /// </summary>
    /// <param name="id">标识符 / Identifier.</param>
    /// <param name="heartBeatCallBack">心跳回调 / Heartbeat callback.</param>
    /// <returns>任务束解 / Bunch solution.</returns>
    public async Task<Result<BunchSolution<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>, ErrorCode, Error<ErrorCode>>> RunAsync(
        string id,
        Action<string, Flt64>? heartBeatCallBack = null) {
        var algorithm = GetOrCreateAlgorithm();
        return await algorithm.RunAsync(id, heartBeatCallBack);
    }
}
