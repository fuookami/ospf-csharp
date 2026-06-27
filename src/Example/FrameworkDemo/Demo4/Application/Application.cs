#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchSelection;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchSelection.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure.Dto;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Application;

/// <summary>
/// 航班恢复调度演示的主应用入口，编排端到端分支定价流程。
/// Main application entry point for the flight recovery scheduling demo, orchestrating the end-to-end branch-and-price flow.
/// </summary>
/// <remarks>
/// 流程: 构建域模型 -> 初始化批次生成上下文 -> 初始化批次选择上下文 -> 运行分支定价 -> 提取解。
/// Flow: build domain model -> init bunch generation context -> init bunch selection context -> run branch-and-price -> extract solution.
/// </remarks>
public sealed class FlightRecoveryApplication {
    private readonly IColumnGenerationSolver _solver;
    private readonly BranchAndPriceAlgorithm.Configuration? _bpConfiguration;

    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="solver">列生成求解器 / Column generation solver.</param>
    /// <param name="bpConfiguration">分支定价算法配置 / Branch-and-price algorithm configuration.</param>
    public FlightRecoveryApplication(
        IColumnGenerationSolver solver,
        BranchAndPriceAlgorithm.Configuration? bpConfiguration = null) {
        _solver = solver;
        _bpConfiguration = bpConfiguration;
    }

    /// <summary>
    /// 执行航班恢复调度的端到端流程。
    /// Executes the end-to-end flight recovery scheduling flow.
    /// </summary>
    /// <param name="input">输入数据 / Input data.</param>
    /// <returns>输出结果 / Output result.</returns>
    public async Task<Result<Output, ErrorCode, Error<ErrorCode>>> RunAsync(Input input) {
        // Step 1: Build domain objects from input
        var domainData = BuildDomainData(input);
        if (domainData is null) {
            return Results.Failed<Output>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "Failed to build domain data from input."));
        }

        // Step 2: Initialize bunch generation context
        var bunchGenCtx = new BunchGenerationContext();
        var connectionTimeCalc = BuildConnectionTimeCalculator();
        var minDepTimeCalc = BuildMinDepartureTimeCalculator();
        var costCalc = BuildCostCalculator();
        var totalCostCalc = BuildTotalCostCalculator();
        var ruleChecker = BuildRuleChecker();

        bunchGenCtx.Init(
            domainData.Aircrafts,
            domainData.AircraftUsability,
            domainData.FlightTasks,
            domainData.OriginBunches,
            new Lock(),
            connectionTimeCalc,
            minDepTimeCalc,
            ruleChecker,
            costCalc,
            totalCostCalc);

        // Step 3: Create bunch compilation and selection contexts
        var cgParameter = new Parameter(
            TaskCancelCoeff: new Flt64(input.TaskCancelCost ?? 9999.0));
        var bunchCompCtx = new BunchCompilationContext(cgParameter);

        var timeWindowGeneric = new TimeWindow<Flt64>(
            domainData.TimeWindow, false, TimeSpan.FromHours(1));

        var bunchSelectionCtx = new BunchSelectionContext(
            aircrafts: domainData.Aircrafts,
            recoveryNeededAircrafts: domainData.Aircrafts,
            recoveryNeededFlightTasks: domainData.FlightTasks,
            timeWindow: timeWindowGeneric,
            bunchGenerationContext: bunchGenCtx,
            bunchCompilationContext: bunchCompCtx,
            parameter: cgParameter);

        // Step 4: Initialize compilation
        var initResult = bunchSelectionCtx.InitCompilation(bunchGenCtx.InitialFlightBunches);
        if (initResult.IsFailed) {
            return Results.Failed<Output>(new Err<ErrorCode>(
                ErrorCode.ApplicationError, "Failed to initialize compilation."));
        }

        // Step 5: Create and run B&P algorithm
        var bpAlgorithm = new BranchAndPriceAlgorithm(
            bunchSelectionCtx, _solver, _bpConfiguration);

        var bpResult = await bpAlgorithm.RunAsync(
            input.ProblemId ?? "demo4-bp",
            (id, rate) => { /* heartbeat callback */ });

        if (bpResult is not Ok<BunchSolution<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>, ErrorCode, Error<ErrorCode>> ok) {
            return bpResult switch {
                Failed<BunchSolution<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>, ErrorCode, Error<ErrorCode>> f
                    => Results.Failed<Output>(f.Error),
                Fatal<BunchSolution<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment>, ErrorCode, Error<ErrorCode>> ft
                    => new Fatal<Output, ErrorCode, Error<ErrorCode>>(ft.Errors),
                _ => Results.Failed<Output>(new Err<ErrorCode>(ErrorCode.ApplicationError, "B&P failed"))
            };
        }

        // Step 6: Extract solution into output
        return Results.Ok(BuildOutput(ok.Value));
    }

    /// <summary>
    /// 从输入构建域数据。
    /// Builds domain data from input.
    /// </summary>
    internal static DomainData? BuildDomainData(Input input) {
        if (input.Aircrafts is null || input.Aircrafts.Count == 0) return null;
        if (input.FlightLegs is null || input.FlightLegs.Count == 0) return null;

        var timeWindow = new TimeRange(
            input.WindowStart ?? DateTimeOffset.UtcNow,
            input.WindowEnd ?? DateTimeOffset.UtcNow.AddDays(7));

        var aircrafts = new List<Aircraft>();
        var aircraftUsability = new Dictionary<Aircraft, AircraftUsability>();

        foreach (var acInput in input.Aircrafts) {
            var regNo = new AircraftRegisterNumber(acInput.RegNo);
            var existing = Aircraft.Find(regNo);
            if (existing is not null) {
                aircrafts.Add(existing);
                if (!aircraftUsability.ContainsKey(existing)) {
                    aircraftUsability[existing] = existing.Usability;
                }
                continue;
            }

            var typeCode = new AircraftTypeCode(acInput.AircraftTypeCode);
            var type = AircraftType.GetOrAdd(typeCode);
            var minorCode = new AircraftMinorTypeCode(acInput.MinorTypeCode);
            var minorType = AircraftMinorType.Find(minorCode);
            if (minorType is null) {
                minorType = new AircraftMinorType(
                    type, minorCode, new Flt64(acInput.CostPerHour ?? 100.0),
                    new Dictionary<Route, TimeSpan>(),
                    new Dictionary<Airport, TimeSpan>());
                AircraftMinorType.Register(minorType);
            }

            var capacity = new AircraftCapacity.PassengerCapacity(
                new Dictionary<PassengerClassId, ulong>());
            var aircraft = new Aircraft(regNo, minorType, capacity);
            Aircraft.Register(aircraft);

            var locationIcao = new ICAO(acInput.LocationIcao);
            var location = Airport.Find(locationIcao);
            if (location is null) {
                location = new Airport(locationIcao, AirportType.Domestic);
                Airport.Register(location);
            }

            aircraft.SetUsability(new AircraftUsability(null, location, acInput.EnabledTime ?? DateTimeOffset.UtcNow));
            aircrafts.Add(aircraft);
            aircraftUsability[aircraft] = aircraft.Usability;
        }

        var legs = new List<FlightLeg>();
        foreach (var legInput in input.FlightLegs) {
            var depIcao = new ICAO(legInput.DepIcao);
            var arrIcao = new ICAO(legInput.ArrIcao);
            var dep = Airport.Find(depIcao) ?? RegisterAirport(depIcao, AirportType.Domestic);
            var arr = Airport.Find(arrIcao) ?? RegisterAirport(arrIcao, AirportType.Domestic);

            var regNo = new AircraftRegisterNumber(legInput.AircraftRegNo);
            var aircraft = Aircraft.Find(regNo);
            if (aircraft is null) continue;

            var scheduledTime = new TimeRange(
                legInput.ScheduledDepTime ?? DateTimeOffset.UtcNow,
                legInput.ScheduledArrTime ?? DateTimeOffset.UtcNow.AddHours(2));
            var flightType = FlightTypeExtensions.FromAirportTypes(dep.Type, arr.Type);

            var plan = new FlightLegPlan(
                legInput.Id,
                legInput.FlightNo ?? $"CA{legInput.Id}",
                flightType,
                legInput.Date ?? DateTimeOffset.UtcNow,
                aircraft,
                new HashSet<Aircraft> { aircraft },
                dep, arr,
                scheduledTime,
                legInput.EstimatedTime,
                legInput.ActualTime,
                legInput.OutTime,
                FlightTaskStatus.NotCancel);

            legs.Add(FlightLeg.Create(plan));
        }

        var flightTasks = new List<FlightTask>(legs);

        return new DomainData(
            timeWindow,
            aircrafts,
            aircraftUsability,
            flightTasks,
            Array.Empty<FlightTaskBunch>());
    }

    private static Airport RegisterAirport(ICAO icao, AirportType type) {
        var airport = new Airport(icao, type);
        Airport.Register(airport);
        return airport;
    }

    private static Output BuildOutput(BunchSolution<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> solution) {
        var scheduledFlights = new List<Output.ScheduledFlight>();
        foreach (var bunch in solution.Bunches) {
            foreach (var task in bunch.Tasks) {
                if (task.IsFlight && task is FlightLeg) {
                    scheduledFlights.Add(new Output.ScheduledFlight {
                        TaskId = task.Id,
                        FlightNo = task.Name,
                        AircraftRegNo = bunch.Executor.RegNo.No,
                        DepIcao = task.Dep.Icao.Code,
                        ArrIcao = task.Arr.Icao.Code,
                        ScheduledDepTime = task.Time?.Start,
                        ScheduledArrTime = task.Time?.End,
                        Recovered = task.Recovered
                    });
                }
            }
        }

        var canceledTasks = new List<string>();
        if (solution.CanceledTasks is not null) {
            foreach (var task in solution.CanceledTasks) {
                canceledTasks.Add(task.Id);
            }
        }

        return new Output {
            ScheduledFlights = scheduledFlights,
            CanceledTaskIds = canceledTasks,
            TotalBunches = solution.Bunches.Count,
            TotalScheduledTasks = scheduledFlights.Count,
            TotalCanceledTasks = canceledTasks.Count
        };
    }

    private static Domain.BunchGeneration.Service.ConnectionTimeCalculator BuildConnectionTimeCalculator()
        => (aircraft, prevTask, succTask) => {
            if (succTask is null) return TimeSpan.Zero;
            var ct = aircraft.ConnectionTime;
            return ct.TryGetValue(prevTask.Arr, out var time) ? time : aircraft.MaxConnectionTime;
        };

    private static Domain.BunchGeneration.Service.MinimumDepartureTimeCalculator BuildMinDepartureTimeCalculator()
        => (arrivalTime, aircraft, task, connectionTime) => {
            var scheduled = task.ScheduledTime;
            if (scheduled is not null) return scheduled.Start;
            return arrivalTime + connectionTime;
        };

    private static Domain.BunchGeneration.Service.BunchCostCalculator BuildCostCalculator()
        => (aircraft, prevTask, task, flightHour, flightCycle) => {
            if (!task.IsFlight) return null;
            var duration = task.DurationFor(aircraft);
            var costValue = new Flt64(duration.TotalHours) * aircraft.CostPerHour;
            return new ImmutableCost<Flt64>(new[] { new CostItem<Flt64>("flight", costValue) });
        };

    private static Domain.BunchGeneration.Service.TotalCostCalculator BuildTotalCostCalculator()
        => (aircraft, tasks) => {
            if (tasks.Count == 0) return null;
            var totalCost = Flt64.Zero;
            foreach (var task in tasks) {
                if (!task.IsFlight) continue;
                var duration = task.DurationFor(aircraft);
                totalCost += new Flt64(duration.TotalHours) * aircraft.CostPerHour;
            }
            return new ImmutableCost<Flt64>(new[] { new CostItem<Flt64>("total", totalCost) });
        };

    private static Domain.BunchGeneration.Service.RuleChecker BuildRuleChecker()
        => (aircraft, prevTask, task) => true;

    /// <summary>
    /// 域数据中间结构，用于在构建和使用之间传递。
    /// Intermediate domain data structure for passing between build and use phases.
    /// </summary>
    internal sealed record DomainData(
        TimeRange TimeWindow,
        IReadOnlyList<Aircraft> Aircrafts,
        IReadOnlyDictionary<Aircraft, AircraftUsability> AircraftUsability,
        IReadOnlyList<FlightTask> FlightTasks,
        IReadOnlyList<FlightTaskBunch> OriginBunches);
}
