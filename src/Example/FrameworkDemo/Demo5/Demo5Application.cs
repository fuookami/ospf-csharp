#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Policy;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo5;

/// <summary>
/// demo5 语义参数 / demo5 semantic parameters.
/// </summary>
/// <remarks>
/// 与基线 `framework_demo/demo5/infrastructure/SemanticParameter.kt`（1455 B）对应。
///
/// **`Solver` 不是字符串名**。基线用 `System.getProperty("ospf.demo5.solver", "gurobi")` 按名字挑后端，
/// 那条路在 C# 侧不存在（没有 `LinearSolverBuilder`），**把求解器做成字符串只会得到一个"名字不认识"的运行时失败**，
/// 而错误现场离真正的配置错误很远。这里改成显式注入 `IColumnGenerationSolver`。
///
/// **`Solver` is not a name string.** The baseline picks a backend by name via `System.getProperty("ospf.demo5.solver", "gurobi")`, and that path does not
/// exist on the C# side (there is no `LinearSolverBuilder`) — **turning the solver into a string only buys an "unknown name" runtime failure** whose
/// site is far from the real configuration mistake. This version injects an `IColumnGenerationSolver` explicitly.
/// </remarks>
/// <param name="InstanceName">实例名称 / Instance name.</param>
/// <param name="CustomerCount">客户数量（仅 Demo17 适用）/ Customer count (Demo17 only).</param>
/// <param name="CostPolicy">成本策略标识（`demo17` 或 `solomon`）/ Cost policy identifier (`demo17` or `solomon`).</param>
/// <param name="TimeLimit">时间上限 / Time limit.</param>
/// <param name="NodeLimit">节点上限 / Node limit.</param>
/// <param name="RelativeGapTolerance">相对 gap 容差 / Relative gap tolerance.</param>
/// <param name="MaxColumnsPerPricing">每次定价最大列数 / Max columns per pricing.</param>
/// <param name="MaxCGIterationsPerNode">每节点最大 CG 迭代次数 / Max CG iterations per node.</param>
public sealed record SemanticParameter(
    string InstanceName = "demo17-25",
    int CustomerCount = 25,
    string CostPolicy = "demo17",
    TimeSpan? TimeLimit = null,
    int NodeLimit = int.MaxValue,
    Flt64? RelativeGapTolerance = null,
    int MaxColumnsPerPricing = int.MaxValue,
    int MaxCGIterationsPerNode = 1000);

/// <summary>
/// demo5 输入 DTO / demo5 input DTO.
/// </summary>
/// <param name="InstanceName">实例名称 / Instance name.</param>
/// <param name="CustomerCount">客户数量（仅 Demo17 适用）/ Customer count (Demo17 only).</param>
/// <param name="CostPolicy">成本策略标识 / Cost policy identifier.</param>
/// <param name="TimeLimitSeconds">时间上限（秒）/ Time limit in seconds.</param>
/// <param name="NodeLimit">节点上限 / Node limit.</param>
/// <param name="RelativeGapTolerance">相对 gap 容差 / Relative gap tolerance.</param>
public sealed record Input(
    string InstanceName = "demo17-25",
    int CustomerCount = 25,
    string CostPolicy = "demo17",
    double TimeLimitSeconds = double.PositiveInfinity,
    int NodeLimit = int.MaxValue,
    double RelativeGapTolerance = 1e-4);

/// <summary>
/// demo5 停靠点输出 / demo5 route-stop output.
/// </summary>
/// <param name="NodeId">节点标识 / Node identifier.</param>
/// <param name="CustomerId">客户标识（仓库为 null）/ Customer identifier (null at a depot).</param>
/// <param name="Arrival">到达时刻 / Arrival instant.</param>
/// <param name="ServiceStart">服务开始时刻 / Service-start instant.</param>
/// <param name="Departure">离开时刻 / Departure instant.</param>
/// <param name="AccumulatedLoad">累计装载 / Accumulated load.</param>
public sealed record StopOutput(
    string NodeId,
    string? CustomerId,
    string Arrival,
    string ServiceStart,
    string Departure,
    double AccumulatedLoad);

/// <summary>
/// demo5 路线输出 / demo5 route output.
/// </summary>
/// <param name="VehicleTypeId">车辆类型 / Vehicle type.</param>
/// <param name="Stops">停靠点 / Stops.</param>
/// <param name="Cost">路线成本 / Route cost.</param>
/// <param name="Distance">路线距离 / Route distance.</param>
/// <param name="Load">路线装载 / Route load.</param>
/// <param name="ArcIds">弧标识 / Arc identities.</param>
public sealed record RouteOutput(
    string VehicleTypeId,
    IReadOnlyList<StopOutput> Stops,
    double Cost,
    double Distance,
    double Load,
    IReadOnlyList<string> ArcIds);

/// <summary>
/// demo5 求解轨迹输出 / demo5 solve-trace output.
/// </summary>
/// <param name="NodesExplored">已探索节点 / Nodes explored.</param>
/// <param name="NodesPruned">已剪枝节点 / Nodes pruned.</param>
/// <param name="GlobalLowerBound">全局下界 / Global lower bound.</param>
/// <param name="GlobalUpperBound">全局上界 / Global upper bound.</param>
/// <param name="RelativeGap">相对间隙 / Relative gap.</param>
/// <param name="TotalTimeMs">总耗时（毫秒）/ Total time in milliseconds.</param>
/// <param name="TotalIterations">总迭代次数 / Total iterations.</param>
public sealed record TraceOutput(
    int NodesExplored,
    int NodesPruned,
    double GlobalLowerBound,
    double GlobalUpperBound,
    double RelativeGap,
    long TotalTimeMs,
    int TotalIterations);

/// <summary>
/// demo5 输出 DTO / demo5 output DTO.
/// </summary>
/// <param name="Status">终止状态 / Termination status.</param>
/// <param name="Routes">路线 / Routes.</param>
/// <param name="TotalCost">总成本 / Total cost.</param>
/// <param name="TotalDistance">总距离 / Total distance.</param>
/// <param name="VehiclesUsed">使用车辆数 / Vehicles used.</param>
/// <param name="Trace">求解轨迹 / Solve trace.</param>
public sealed record Output(
    BranchAndPriceStatus Status,
    IReadOnlyList<RouteOutput> Routes,
    double TotalCost,
    double TotalDistance,
    int VehiclesUsed,
    TraceOutput Trace);

/// <summary>
/// demo5 VRPTW 分支定价求解应用 / demo5 VRPTW branch-and-price solver application.
/// </summary>
/// <remarks>
/// 接线：`VrptwApplicationService` → `BranchAndPriceAlgorithm` → `BranchNodeSolver` → `ColumnGenerationSolver`。
/// 不重新定义 `Graph`、`Label`、`Pricer`、`RouteCompilation` 或 `BranchAndPriceAlgorithm`。
///
/// Wiring: `VrptwApplicationService` → `BranchAndPriceAlgorithm` → `BranchNodeSolver` → `ColumnGenerationSolver`. It does not redefine `Graph`, `Label`,
/// `Pricer`, `RouteCompilation`, or `BranchAndPriceAlgorithm`.
/// </remarks>
public static class Application {
    /// <summary>
    /// 默认业务时间轴 / The default business timeline.
    /// </summary>
    /// <remarks>
    /// 1 小时窗口、以秒为刻度，与 Demo17 原始数值 1:1 对应。
    ///
    /// A one-hour window with a one-second tick, matching Demo17's raw values 1:1.
    ///
    /// **与 Demo5 其它夹具共享同一对常量**，避免"两处各写一个时间轴"后 `InstantOf` 给出相差几分钟的绝对时刻。
    ///
    /// **Shares one pair of constants with the other Demo5 fixtures** so that two independently written timelines cannot make `InstantOf` return instants
    /// minutes apart.
    /// </remarks>
    /// <returns>调度时间窗 / The scheduling window.</returns>
    public static TimeWindow<Flt64> DefaultSchedulingWindow() =>
        new(
            Window: new TimeRange(WindowStart, WindowStart.AddHours(1)),
            Continues: true,
            Interval: TimeSpan.FromSeconds(1));

    /// <summary>业务时间轴起点 / The business timeline start.</summary>
    public static DateTimeOffset WindowStart { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 求解 VRPTW 实例 / Solve a VRPTW instance.
    /// </summary>
    /// <remarks>
    /// **`CostPolicy` 只接受两个已知取值**。未知取值会被静默当成 `demo17`，而**那意味着一次求解用的是完全另一套距离与成本语义，
    /// 结果看上去仍然"正常"**。
    ///
    /// **`CostPolicy` accepts only two known values.** An unknown value would be silently treated as `demo17`, and **that means a solve ran under an
    /// entirely different distance and cost semantics while the results still look "normal"**.
    /// </remarks>
    /// <param name="instance">VRPTW 实例 / The VRPTW instance.</param>
    /// <param name="solver">列生成求解器 / The column-generation solver.</param>
    /// <param name="parameter">语义参数 / The semantic parameters.</param>
    /// <returns>求解结果 / The solve result.</returns>
    public static Task<Result<VrptwSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>> Solve(
        VrptwInstance<Flt64> instance,
        IColumnGenerationSolver solver,
        SemanticParameter? parameter = null) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(solver);
        SemanticParameter used = parameter ?? new SemanticParameter();
        return BuildPolicies(used) is not Ok<
                BranchAndPriceAlgorithm<Flt64>.Policy,
                ErrorCode,
                Error<ErrorCode>> policyOk
            ? Task.FromResult(
                Results.Failed<VrptwSolveResult<Flt64>>(
                    ((Failed<
                        BranchAndPriceAlgorithm<Flt64>.Policy,
                        ErrorCode,
                        Error<ErrorCode>>)BuildPolicies(used)).Error))
            : new VrptwApplicationService<Flt64>(
                    instance: instance,
                    solver: solver,
                    configuration: new BranchAndPriceAlgorithm<Flt64>.Configuration(
                        TimeLimit: used.TimeLimit ?? Timeout.InfiniteTimeSpan,
                        NodeLimit: used.NodeLimit,
                        RelativeGapTolerance: used.RelativeGapTolerance,
                        MaxColumnsPerPricing: used.MaxColumnsPerPricing,
                        MaxCGIterationsPerNode: used.MaxCGIterationsPerNode),
                    policy: policyOk.Value)
                .Solve();
    }

    /// <summary>
    /// 构造与 `CostPolicy` 对应的策略组 / Build the policy set matching `CostPolicy`.
    /// </summary>
    /// <param name="parameter">语义参数 / The semantic parameters.</param>
    /// <returns>策略组或非法参数 / The policy set, or an illegal-argument failure.</returns>
    public static Result<
        BranchAndPriceAlgorithm<Flt64>.Policy,
        ErrorCode,
        Error<ErrorCode>> BuildPolicies(SemanticParameter parameter) {
        ArgumentNullException.ThrowIfNull(parameter);
        TimeWindow<Flt64> schedulingWindow = DefaultSchedulingWindow();
        IDistanceCalculator<Flt64> distanceCalculator;
        IRouteCostPolicy<Flt64> routeCostPolicy;
        switch (parameter.CostPolicy) {
            case "solomon":
                // **Solomon 口径要求距离保留一位小数**：不取整会让累积距离与 Solomon 基准实例公布的
                // 最优值差出可观量，而**求解本身成功、结果看上去正常**。
                // **The Solomon convention keeps one decimal of distance**: without rounding, cumulative distances drift away from the published optima of
                // the Solomon instances, while **the solve still succeeds and the result looks normal**.
                distanceCalculator = new SolomonDistancePolicy<Flt64>(
                    SIBaseUnits.Meter,
                    value => new Flt64(global::System.Math.Round(value.ToDouble(), 1)));
                routeCostPolicy = new FixedPlusArcCostPolicy<Flt64>(NoneUnit.Instance);
                break;
            case "demo17":
                distanceCalculator = new EuclideanDistanceCalculator<Flt64>(SIBaseUnits.Meter);
                routeCostPolicy = new Demo17CostPolicy<Flt64>(NoneUnit.Instance);
                break;
            default:
                return Results.Failed<
                    BranchAndPriceAlgorithm<Flt64>.Policy>(
                    new Err<ErrorCode>(
                        code: ErrorCode.IllegalArgument,
                        message: $"未知成本策略：{parameter.CostPolicy} / "
                            + $"Unknown cost policy: {parameter.CostPolicy}"));
        }
        return Results.Ok(new BranchAndPriceAlgorithm<Flt64>.Policy(
            ValueAdapter: Flt64NetworkSchedulingSolverValueAdapter.Instance,
            DistanceCalculator: distanceCalculator,
            TravelTimeCalculator: new DistanceAsTravelTimeCalculator<Flt64>(
                distanceCalculator, schedulingWindow),
            ArcCostCalculator: new DistanceArcCostCalculator<Flt64>(
                SIBaseUnits.Meter, NoneUnit.Instance),
            RouteCostPolicy: routeCostPolicy));
    }

    /// <summary>
    /// 将求解结果转换为输出 DTO / Convert a solve result into the output DTO.
    /// </summary>
    /// <param name="result">求解结果 / The solve result.</param>
    /// <returns>输出 DTO / The output DTO.</returns>
    public static Output ToOutput(VrptwSolveResult<Flt64> result) {
        ArgumentNullException.ThrowIfNull(result);
        VrptwSolution<Flt64>? solution = result.Solution;
        var routes = new List<RouteOutput>(solution?.Routes.Count ?? 0);
        if (solution is not null) {
            foreach (Route<Flt64> route in solution.Routes) {
                var stops = new List<StopOutput>(route.Stops.Count);
                foreach (RouteStop<Flt64> stop in route.Stops) {
                    stops.Add(new StopOutput(
                        NodeId: stop.NodeId.Value,
                        CustomerId: stop.CustomerId?.Value,
                        Arrival: stop.Arrival.ToString("O", Culture),
                        ServiceStart: stop.ServiceStart.ToString("O", Culture),
                        Departure: stop.Departure.ToString("O", Culture),
                        AccumulatedLoad: stop.AccumulatedLoad.Value.ToDouble()));
                }
                routes.Add(new RouteOutput(
                    VehicleTypeId: route.VehicleTypeId.Value,
                    Stops: stops,
                    Cost: route.Cost.Value.ToDouble(),
                    Distance: route.Distance.Value.ToDouble(),
                    // **装载取最后一个停靠点的累计值**：取第一个会在"只有一个客户"的短线上给出 0，
                    // 而 0 装载正是"这条路线什么也没送"的样子。
                    // **Load comes from the last stop's accumulated value**: taking the first would yield 0 on a one-customer route, and a zero load is
                    // exactly what "this route delivered nothing" looks like.
                    Load: route.Stops.Count > 0
                        ? route.Stops[^1].AccumulatedLoad.Value.ToDouble()
                        : 0.0,
                    ArcIds: route.ArcIds.Select(arcId => arcId.Value).ToList()));
            }
        }
        return new Output(
            Status: result.Status,
            Routes: routes,
            TotalCost: solution?.TotalCost.Value.ToDouble() ?? 0.0,
            TotalDistance: solution?.TotalDistance.Value.ToDouble() ?? 0.0,
            VehiclesUsed: solution?.Routes.Count ?? 0,
            Trace: new TraceOutput(
                NodesExplored: result.Trace.NodesExplored,
                NodesPruned: result.Trace.NodesPruned,
                GlobalLowerBound: result.Trace.GlobalLowerBound.ToDouble(),
                GlobalUpperBound: result.Trace.GlobalUpperBound.ToDouble(),
                RelativeGap: result.Trace.RelativeGap.ToDouble(),
                TotalTimeMs: (long)result.Trace.TotalTime.TotalMilliseconds,
                TotalIterations: result.Trace.TotalIterations));
    }

    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;
}
