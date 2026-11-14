#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchSelection;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchSelection.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure.Dto;
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
    /// 每次输入独立构建领域对象，不使用进程级注册池。
    /// Domain objects are built per input without using the process-wide registries.
    /// </summary>
    /// <param name="input">输入数据 / Input data.</param>
    /// <returns>输出结果 / Output result.</returns>
    public Task<Result<Output, ErrorCode, Error<ErrorCode>>> RunAsync(Input input) => RunAsync(input, null);

    /// <summary>
    /// 使用请求范围的领域数据执行航班恢复；不读取或修改进程级注册池。
    /// Runs flight recovery with request-scoped domain data without using process-wide registries.
    /// </summary>
    /// <param name="input">问题标识和求解配置 / Problem identifier and solve configuration.</param>
    /// <param name="domainData">已装配领域数据；为空时从输入构建 / Prepared domain data, or null to build it from the input.</param>
    /// <returns>输出结果 / Output result.</returns>
    /// <remarks>
    /// 调用方须在求解期间保持传入的集合及领域对象不变，并负责隔离并发请求；编排可能设置飞机索引。
    /// Callers must keep supplied collections and domain objects unchanged while solving and isolate concurrent requests; orchestration may set aircraft indices.
    /// 仅校验基本引用和时间，尚不保证自定义领域数据能够完整建模或求解。
    /// Only basic references and times are validated; custom domain data is not guaranteed to be fully modeled or solved.
    /// </remarks>
    public async Task<Result<Output, ErrorCode, Error<ErrorCode>>> RunAsync(Input input, DomainData? domainData) {
        // Step 1: Build domain objects from input
        if (input is null) {
            return Results.Failed<Output>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "输入不能为空。 / Input cannot be null."));
        }
        domainData ??= BuildDomainData(input);
        if (domainData is null || !IsValidDomainData(domainData)) {
            return Results.Failed<Output>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "领域数据无效或飞机可用性不一致。 / Domain data is invalid or aircraft usability is inconsistent."));
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

    private static bool IsValidDomainData(DomainData? data) {
        if (data is null || data.TimeWindow is null || data.TimeWindow.Empty || data.Aircrafts is null ||
            data.AircraftUsability is null || data.FlightTasks is null || data.OriginBunches is null) {
            return false;
        }
        var aircraftByRegNo = new Dictionary<AircraftRegisterNumber, Aircraft>();
        foreach (Aircraft? aircraft in data.Aircrafts) {
            if (aircraft is null || aircraft.RegNo.No is null || aircraft.MinorType is null ||
                aircraft.Capacity is null || aircraft.MinorType.Type is null ||
                aircraft.MinorType.RouteFlyTime is null || aircraft.MinorType.ConnectionTime is null ||
                !data.AircraftUsability.TryGetValue(aircraft, out AircraftUsability? usability) ||
                usability is null || usability.Location is null ||
                (usability.LastTask is not null &&
                    (usability.LastTask is not FlightTask lastTask || !IsValidFlightTask(lastTask) || lastTask.Time is null)) ||
                !ReferenceEquals(aircraft.ConfiguredUsability, usability)) {
                return false;
            }
            if (aircraftByRegNo.TryGetValue(aircraft.RegNo, out Aircraft? existing) &&
                !ReferenceEquals(existing, aircraft)) {
                return false;
            }
            aircraftByRegNo[aircraft.RegNo] = aircraft;
        }
        foreach (FlightTask? task in data.FlightTasks) {
            if (!IsValidFlightTask(task)) {
                return false;
            }
        }
        foreach (FlightTaskBunch? bunch in data.OriginBunches) {
            if (bunch is null || bunch.Executor is null || bunch.Tasks is null ||
                !aircraftByRegNo.TryGetValue(bunch.Executor.RegNo, out Aircraft? aircraft) ||
                !ReferenceEquals(aircraft, bunch.Executor)) {
                return false;
            }
            foreach (FlightTask? task in bunch.Tasks) {
                if (!IsValidFlightTask(task)) {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool IsValidFlightTask(FlightTask? task) {
        if (task is null || task.Plan is null || task.Dep is null || task.Arr is null ||
            task.DepBackup is null || task.ArrBackup is null || (task.Time is null && task.TimeWindow is null)) {
            return false;
        }
        foreach (Airport? airport in task.DepBackup) {
            if (airport is null) {
                return false;
            }
        }
        foreach (Airport? airport in task.ArrBackup) {
            if (airport is null) {
                return false;
            }
        }
        return true;
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
        var aircraftByRegNo = new Dictionary<AircraftRegisterNumber, Aircraft>();
        var typesByCode = new Dictionary<AircraftTypeCode, AircraftType>();
        var minorTypesByCode = new Dictionary<AircraftMinorTypeCode, AircraftMinorType>();
        var airportsByIcao = new Dictionary<ICAO, Airport>();

        if (input.Airports is not null) {
            foreach (Input.AirportInput? airportInput in input.Airports) {
                if (airportInput is null || airportInput.Icao is null || airportInput.Icao.Length != 4
                    || !Enum.IsDefined(airportInput.Type ?? AirportType.Domestic)) {
                    return null;
                }
                var icao = new ICAO(airportInput.Icao);
                airportsByIcao.TryAdd(icao, new Airport(icao, airportInput.Type ?? AirportType.Domestic));
            }
        }

        Airport GetAirport(ICAO icao) {
            if (!airportsByIcao.TryGetValue(icao, out Airport? airport)) {
                airport = new Airport(icao, AirportType.Domestic);
                airportsByIcao.Add(icao, airport);
            }
            return airport;
        }

        foreach (var acInput in input.Aircrafts) {
            var regNo = new AircraftRegisterNumber(acInput.RegNo);
            if (aircraftByRegNo.TryGetValue(regNo, out Aircraft? existing)) {
                aircrafts.Add(existing);
                if (!aircraftUsability.ContainsKey(existing)) {
                    aircraftUsability[existing] = existing.Usability;
                }
                continue;
            }

            var minorCode = new AircraftMinorTypeCode(acInput.MinorTypeCode);
            if (!minorTypesByCode.TryGetValue(minorCode, out AircraftMinorType? minorType)) {
                var typeCode = new AircraftTypeCode(acInput.AircraftTypeCode);
                if (!typesByCode.TryGetValue(typeCode, out AircraftType? type)) {
                    type = new AircraftType(typeCode);
                    typesByCode.Add(typeCode, type);
                }
                minorType = new AircraftMinorType(
                    Type: type,
                    Code: minorCode,
                    CostPerHour: new Flt64(acInput.CostPerHour ?? 100.0),
                    RouteFlyTime: new Dictionary<Route, TimeSpan>(),
                    ConnectionTime: new Dictionary<Airport, TimeSpan>());
                minorTypesByCode.Add(minorCode, minorType);
            }

            var capacity = new AircraftCapacity.PassengerCapacity(
                new Dictionary<PassengerClassId, ulong>());
            var aircraft = new Aircraft(
                RegNo: regNo,
                MinorType: minorType,
                Capacity: capacity);
            Airport location = GetAirport(new ICAO(acInput.LocationIcao));

            aircraft.SetUsability(new AircraftUsability(
                lastTask: null,
                location: location,
                enabledTime: acInput.EnabledTime ?? DateTimeOffset.UtcNow));
            aircraftByRegNo.Add(regNo, aircraft);
            aircrafts.Add(aircraft);
            aircraftUsability[aircraft] = aircraft.Usability;
        }

        var legs = new List<FlightLeg>();
        foreach (var legInput in input.FlightLegs) {
            var depIcao = new ICAO(legInput.DepIcao);
            var arrIcao = new ICAO(legInput.ArrIcao);
            Airport dep = GetAirport(depIcao);
            Airport arr = GetAirport(arrIcao);

            var regNo = new AircraftRegisterNumber(legInput.AircraftRegNo);
            if (!aircraftByRegNo.TryGetValue(regNo, out Aircraft? aircraft)) {
                continue;
            }

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
    /// 请求范围的已装配领域数据；其集合与对象由调用方持有。
    /// Prepared request-scoped domain data whose collections and objects remain caller-owned.
    /// </summary>
    /// <param name="TimeWindow">恢复时间窗口 / Recovery time window.</param>
    /// <param name="Aircrafts">飞机列表 / Aircraft list.</param>
    /// <param name="AircraftUsability">每架飞机的可用性，须与飞机自身引用一致 / Usability per aircraft, matching each aircraft's own reference.</param>
    /// <param name="FlightTasks">航班任务 / Flight tasks.</param>
    /// <param name="OriginBunches">初始航班束 / Origin flight bunches.</param>
    public sealed record DomainData(
        TimeRange TimeWindow,
        IReadOnlyList<Aircraft> Aircrafts,
        IReadOnlyDictionary<Aircraft, AircraftUsability> AircraftUsability,
        IReadOnlyList<FlightTask> FlightTasks,
        IReadOnlyList<FlightTaskBunch> OriginBunches);
}
