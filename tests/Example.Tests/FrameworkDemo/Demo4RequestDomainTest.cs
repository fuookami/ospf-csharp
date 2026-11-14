#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure.Dto;
namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

public class Demo4RequestDomainTest {
    [Fact]
    public async Task PreparedDomainData_CanRunWithoutDtoDomainObjects() {
        FlightRecoveryApplication.DomainData domain = CreateDomain();
        Aircraft aircraft = Assert.Single(domain.Aircrafts);
        FlightTask task = Assert.Single(domain.FlightTasks);
        var app = new FlightRecoveryApplication(new MockColumnGenerationSolver());
        Assert.False(aircraft.Indexed);

        Result<Output, ErrorCode, Error<ErrorCode>> result = await app.RunAsync(new Input(), domain);

        Assert.True(result.IsOk, result.ToString());
        Assert.True(aircraft.Indexed);
        Assert.Same(aircraft, task.Plan.Executor);
        Assert.Equal(new Flt64(456), aircraft.CostPerHour);
        Assert.Equal(123UL, aircraft.GetCapacity(new PassengerClassId("Y")));
        Assert.Equal(TimeSpan.FromMinutes(90), aircraft.RouteFlyTime[new Route(task.Dep, task.Arr)]);
        Assert.Equal(TimeSpan.FromMinutes(45), aircraft.ConnectionTime[task.Dep]);
        Assert.True(task.Dep.Base);
        Assert.Same(aircraft.Usability, domain.AircraftUsability[aircraft]);
    }

    [Fact]
    public async Task PreparedDomainData_MissingOrInconsistentUsability_ReturnsFailure() {
        FlightRecoveryApplication.DomainData domain = CreateDomain();
        Aircraft aircraft = Assert.Single(domain.Aircrafts);
        var app = new FlightRecoveryApplication(new MockColumnGenerationSolver());
        FlightRecoveryApplication.DomainData missing = domain with { AircraftUsability = new Dictionary<Aircraft, AircraftUsability>() };
        FlightRecoveryApplication.DomainData different = domain with {
            AircraftUsability = new Dictionary<Aircraft, AircraftUsability> {
                [aircraft] = new AircraftUsability(
                    lastTask: null,
                    location: aircraft.Usability.Location,
                    enabledTime: aircraft.Usability.EnabledTime),
            },
        };

        Result<Output, ErrorCode, Error<ErrorCode>> missingResult = await app.RunAsync(new Input(), missing);
        Result<Output, ErrorCode, Error<ErrorCode>> differentResult = await app.RunAsync(new Input(), different);

        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Output, ErrorCode, Error<ErrorCode>>>(missingResult).Error.Code);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Output, ErrorCode, Error<ErrorCode>>>(differentResult).Error.Code);
    }

    [Fact]
    public async Task PreparedDomainData_NullElementsAndEmptyTimeWindow_ReturnFailure() {
        FlightRecoveryApplication.DomainData domain = CreateDomain();
        var app = new FlightRecoveryApplication(new MockColumnGenerationSolver());
        FlightRecoveryApplication.DomainData invalidTasks = domain with { FlightTasks = new FlightTask[] { null! } };
        FlightRecoveryApplication.DomainData invalidWindow = domain with { TimeWindow = new TimeRange(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch) };

        Assert.True((await app.RunAsync(new Input(), invalidTasks)).IsFailed);
        Assert.True((await app.RunAsync(new Input(), invalidWindow)).IsFailed);
    }

    [Fact]
    public async Task PreparedDomainData_UninitializedAircraftAndDuplicateIdentity_ReturnFailure() {
        FlightRecoveryApplication.DomainData domain = CreateDomain();
        Aircraft aircraft = Assert.Single(domain.Aircrafts);
        var uninitialized = new Aircraft(aircraft.RegNo, aircraft.MinorType, aircraft.Capacity);
        var duplicate = new Aircraft(aircraft.RegNo, aircraft.MinorType, aircraft.Capacity);
        duplicate.SetUsability(aircraft.Usability);
        FlightRecoveryApplication.DomainData missingUsability = domain with { Aircrafts = new[] { uninitialized } };
        FlightRecoveryApplication.DomainData duplicateIdentity = domain with { Aircrafts = new[] { aircraft, duplicate } };
        var app = new FlightRecoveryApplication(new MockColumnGenerationSolver());

        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Output, ErrorCode, Error<ErrorCode>>>(await app.RunAsync(new Input(), missingUsability)).Error.Code);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Output, ErrorCode, Error<ErrorCode>>>(await app.RunAsync(new Input(), duplicateIdentity)).Error.Code);
    }

    [Fact]
    public async Task PreparedDomainData_OriginBunchWithAnotherAircraft_ReturnsFailure() {
        FlightRecoveryApplication.DomainData domain = CreateDomain();
        Aircraft aircraft = Assert.Single(domain.Aircrafts);
        var other = new Aircraft(new AircraftRegisterNumber("OTHER"), aircraft.MinorType, aircraft.Capacity);
        other.SetUsability(aircraft.Usability);
        var bunch = new FlightTaskBunch(
            aircraft: other,
            airport: other.Usability.Location,
            time: other.Usability.EnabledTime,
            iteration: 0);
        FlightRecoveryApplication.DomainData invalid = domain with { OriginBunches = new[] { bunch } };
        var app = new FlightRecoveryApplication(new MockColumnGenerationSolver());

        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Output, ErrorCode, Error<ErrorCode>>>(await app.RunAsync(new Input(), invalid)).Error.Code);
    }

    [Fact]
    public async Task PreparedDomainData_OriginBunchWithNullTask_ReturnsFailure() {
        FlightRecoveryApplication.DomainData domain = CreateDomain();
        Aircraft aircraft = Assert.Single(domain.Aircrafts);
        var tasks = new List<FlightTask> { Assert.Single(domain.FlightTasks) };
        var bunch = new FlightTaskBunch(
            aircraft: aircraft,
            tasks: tasks,
            iteration: 0);
        tasks.Add(null!);
        FlightRecoveryApplication.DomainData invalid = domain with { OriginBunches = new[] { bunch } };
        var app = new FlightRecoveryApplication(new MockColumnGenerationSolver());

        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Output, ErrorCode, Error<ErrorCode>>>(await app.RunAsync(new Input(), invalid)).Error.Code);
    }

    [Fact]
    public async Task PreparedDomainData_TaskWithoutTimeOrPlan_ReturnsFailure() {
        FlightRecoveryApplication.DomainData domain = CreateDomain();
        Aircraft aircraft = Assert.Single(domain.Aircrafts);
        FlightTask original = Assert.Single(domain.FlightTasks);
        var plan = new FlightLegPlan(
            actualId: "missing-time",
            no: "RICH2",
            type: FlightType.Domestic,
            date: DateTimeOffset.UnixEpoch,
            aircraft: aircraft,
            enabledAircrafts: new HashSet<Aircraft> { aircraft },
            dep: original.Dep,
            arr: original.Arr,
            scheduledTime: null!,
            estimatedTime: null,
            actualTime: null,
            outTime: null,
            flightTaskStatus: FlightTaskStatus.NotCancel);
        FlightRecoveryApplication.DomainData missingTime = domain with { FlightTasks = new FlightTask[] { FlightLeg.Create(plan) } };
        FlightRecoveryApplication.DomainData missingPlan = domain with { FlightTasks = new FlightTask[] { FlightLeg.Create(null!) } };
        var app = new FlightRecoveryApplication(new MockColumnGenerationSolver());

        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Output, ErrorCode, Error<ErrorCode>>>(await app.RunAsync(new Input(), missingTime)).Error.Code);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Output, ErrorCode, Error<ErrorCode>>>(await app.RunAsync(new Input(), missingPlan)).Error.Code);

        var usability = new AircraftUsability(
            lastTask: FlightLeg.Create(plan),
            location: original.Dep,
            enabledTime: DateTimeOffset.UnixEpoch);
        aircraft.SetUsability(usability);
        FlightRecoveryApplication.DomainData missingLastTime = domain with {
            AircraftUsability = new Dictionary<Aircraft, AircraftUsability> { [aircraft] = usability },
        };
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<Output, ErrorCode, Error<ErrorCode>>>(await app.RunAsync(new Input(), missingLastTime)).Error.Code);
    }

    [Fact]
    public async Task PreparedDomainData_TransferWithTimeWindowUsesRouteMap() {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var departure = new Airport(new ICAO("ZRQA"), AirportType.International, Base: true);
        var arrival = new Airport(new ICAO("ZRQB"), AirportType.Domestic);
        var routes = new ObservedLookup<Route, TimeSpan>(new Route(departure, arrival), TimeSpan.FromMinutes(90));
        FlightRecoveryApplication.DomainData domain = CreateDomain(routeTimes: routes);
        Aircraft aircraft = Assert.Single(domain.Aircrafts);
        FlightTask original = Assert.Single(domain.FlightTasks);
        var plan = TransferPlan.Create(
            dep: original.Dep,
            arr: original.Arr,
            timeWindow: new TimeRange(now, now.AddHours(4)),
            aircrafts: new HashSet<Aircraft> { aircraft });
        var transfer = Transfer.Create(plan);
        var bunch = new FlightTaskBunch(
            aircraft: aircraft,
            time: new TimeRange(now, now.AddHours(4)),
            dep: original.Dep,
            arr: original.Arr,
            tasks: new FlightTask[] { transfer },
            cost: new ImmutableCost<Flt64>(Array.Empty<CostItem<Flt64>>()),
            iteration: 0);
        FlightRecoveryApplication.DomainData request = domain with {
            FlightTasks = new FlightTask[] { transfer },
            OriginBunches = new[] { bunch },
        };
        var app = new FlightRecoveryApplication(new MockColumnGenerationSolver());

        Result<Output, ErrorCode, Error<ErrorCode>> result = await app.RunAsync(new Input(), request);

        Assert.True(result.IsOk, result.ToString());
        Assert.True(routes.Lookups > 0);
        Assert.Equal(TimeSpan.FromMinutes(90), transfer.DurationFor(aircraft));
    }

    [Fact]
    public async Task PreparedDomainData_IndependentConcurrentRequestsUseTheirConnectionMaps() {
        Task[] runs = Enumerable.Range(0, 12).Select(i => Task.Run(async () => {
            var airport = new Airport(new ICAO("ZRQB"), AirportType.Domestic);
            var connections = new ObservedLookup<Airport, TimeSpan>(airport, TimeSpan.FromMinutes(30 + i));
            FlightRecoveryApplication.DomainData domain = CreateDomain(connections);
            Aircraft aircraft = Assert.Single(domain.Aircrafts);
            FlightTask first = Assert.Single(domain.FlightTasks);
            var secondPlan = new FlightLegPlan(
                actualId: $"second-{i}",
                no: "RICH2",
                type: FlightType.Domestic,
                date: DateTimeOffset.UnixEpoch,
                aircraft: aircraft,
                enabledAircrafts: new HashSet<Aircraft> { aircraft },
                dep: first.Arr,
                arr: new Airport(new ICAO("ZRQC"), AirportType.Domestic),
                scheduledTime: new TimeRange(DateTimeOffset.UnixEpoch.AddHours(5), DateTimeOffset.UnixEpoch.AddHours(6)),
                estimatedTime: null,
                actualTime: null,
                outTime: null,
                flightTaskStatus: FlightTaskStatus.NotCancel);
            FlightTask[] tasks = { first, FlightLeg.Create(secondPlan) };
            var bunch = new FlightTaskBunch(
                aircraft: aircraft,
                tasks: tasks,
                iteration: 0);
            FlightRecoveryApplication.DomainData request = domain with { FlightTasks = tasks, OriginBunches = new[] { bunch } };
            var app = new FlightRecoveryApplication(new MockColumnGenerationSolver());

            Result<Output, ErrorCode, Error<ErrorCode>> result = await app.RunAsync(new Input(), request);

            Assert.True(result.IsOk, result.ToString());
            Assert.True(connections.Lookups > 0);
            Assert.Equal(TimeSpan.FromMinutes(30 + i), connections[airport]);
        })).ToArray();

        await Task.WhenAll(runs);
    }

    private static FlightRecoveryApplication.DomainData CreateDomain(
        IReadOnlyDictionary<Airport, TimeSpan>? connectionTimes = null,
        IReadOnlyDictionary<Route, TimeSpan>? routeTimes = null) {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var departure = new Airport(
            Icao: new ICAO("ZRQA"),
            Type: AirportType.International,
            Base: true);
        var arrival = new Airport(new ICAO("ZRQB"), AirportType.Domestic);
        var minor = new AircraftMinorType(
            Type: new AircraftType(new AircraftTypeCode("RICH")),
            Code: new AircraftMinorTypeCode("RICH"),
            CostPerHour: new Flt64(456),
            RouteFlyTime: routeTimes ?? new Dictionary<Route, TimeSpan> { [new Route(departure, arrival)] = TimeSpan.FromMinutes(90) },
            ConnectionTime: connectionTimes ?? new Dictionary<Airport, TimeSpan> { [departure] = TimeSpan.FromMinutes(45) });
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("BRICH"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [new PassengerClassId("Y")] = 123,
            }));
        var usability = new AircraftUsability(
            lastTask: null,
            location: departure,
            enabledTime: now);
        aircraft.SetUsability(usability);
        var plan = new FlightLegPlan(
            actualId: "rich",
            no: "RICH1",
            type: FlightType.International,
            date: now,
            aircraft: aircraft,
            enabledAircrafts: new HashSet<Aircraft> { aircraft },
            dep: departure,
            arr: arrival,
            scheduledTime: new TimeRange(now.AddHours(2), now.AddHours(4)),
            estimatedTime: null,
            actualTime: null,
            outTime: null,
            flightTaskStatus: FlightTaskStatus.NotCancel);
        return new FlightRecoveryApplication.DomainData(
            TimeWindow: new TimeRange(now, now.AddDays(1)),
            Aircrafts: new[] { aircraft },
            AircraftUsability: new Dictionary<Aircraft, AircraftUsability> { [aircraft] = usability },
            FlightTasks: new FlightTask[] { FlightLeg.Create(plan) },
            OriginBunches: Array.Empty<FlightTaskBunch>());
    }

    private sealed class ObservedLookup<TKey, TValue>(TKey key, TValue value) : IReadOnlyDictionary<TKey, TValue>
        where TKey : notnull {
        private readonly Dictionary<TKey, TValue> _values = new() { [key] = value };

        public int Lookups { get; private set; }
        public TValue this[TKey key] => _values[key];
        public IEnumerable<TKey> Keys => _values.Keys;
        public IEnumerable<TValue> Values => _values.Values;
        public int Count => _values.Count;
        public bool ContainsKey(TKey key) => _values.ContainsKey(key);
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue result) {
            Lookups++;
            return _values.TryGetValue(key, out result);
        }
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
