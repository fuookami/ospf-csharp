#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure.Dto;
namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

public class Demo4RegistryConcurrencyTest {
    [Fact]
    public void BuildDomainData_DoesNotReadOrPublishGlobalObjects() {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("ISOLATION"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("ISOLATION"),
            CostPerHour: new Flt64(999),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        AircraftMinorType.Register(minor);
        var globalAirport = new Airport(new ICAO("ZISO"), AirportType.International);
        Airport.Register(globalAirport);
        var globalAircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber("BISO1"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong>()));
        globalAircraft.SetUsability(new AircraftUsability(
            lastTask: null,
            location: globalAirport,
            enabledTime: now));
        Aircraft.Register(globalAircraft);

        FlightRecoveryApplication.DomainData? first = FlightRecoveryApplication.BuildDomainData(CreateInput(
            cost: 111,
            location: "ZISO",
            enabled: now.AddHours(1)));
        FlightRecoveryApplication.DomainData? second = FlightRecoveryApplication.BuildDomainData(CreateInput(
            cost: 222,
            location: "ZNEW",
            enabled: now.AddHours(2)));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Aircraft firstAircraft = Assert.Single(first.Aircrafts);
        Aircraft secondAircraft = Assert.Single(second.Aircrafts);
        Assert.NotSame(globalAircraft, firstAircraft);
        Assert.NotSame(firstAircraft, secondAircraft);
        Assert.NotSame(type, firstAircraft.Type);
        Assert.NotSame(minor, firstAircraft.MinorType);
        Assert.Equal(new Flt64(111), firstAircraft.CostPerHour);
        Assert.Equal(new Flt64(222), secondAircraft.CostPerHour);
        Assert.Equal(now.AddHours(1), firstAircraft.Usability.EnabledTime);
        Assert.Equal(now.AddHours(2), secondAircraft.Usability.EnabledTime);
        Assert.Equal(AirportType.Domestic, firstAircraft.Usability.Location.Type);
        Assert.Equal("ZNEW", secondAircraft.Usability.Location.Icao.Code);
        Assert.Same(firstAircraft, Assert.Single(first.FlightTasks).Plan.Executor);
        Assert.Same(secondAircraft, Assert.Single(second.FlightTasks).Plan.Executor);
        Assert.Same(globalAircraft, Aircraft.Find(globalAircraft.RegNo));
        Assert.Same(minor, AircraftMinorType.Find(minor.Code));
        Assert.Same(globalAirport, Airport.Find(globalAirport.Icao));
    }

    [Fact]
    public void BuildDomainData_UsesOnlyAircraftsFromTheSameInput() {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        var type = new AircraftType(new AircraftTypeCode("EXTERNAL"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("EXTERNAL"),
            CostPerHour: Flt64.One,
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var external = new Aircraft(
            RegNo: new AircraftRegisterNumber("BEXT1"),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong>()));
        Aircraft.Register(external);
        Input input = CreateInput(
            cost: 111,
            location: "ZISO",
            enabled: now);
        input.FlightLegs!.Add(new Input.FlightLegInput {
            Id = "external",
            AircraftRegNo = external.RegNo.No,
            DepIcao = "ZISO",
            ArrIcao = "ZNEW",
        });

        FlightRecoveryApplication.DomainData? data = FlightRecoveryApplication.BuildDomainData(input);

        Assert.NotNull(data);
        Assert.Single(data.FlightTasks);
        Assert.Same(Assert.Single(data.Aircrafts), data.FlightTasks[0].Plan.Executor);
    }

    [Fact]
    public void BuildDomainData_SharesObjectsWithinOneInput() {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        Input input = CreateInput(
            cost: 111,
            location: "ZISO",
            enabled: now);
        input.Aircrafts!.Add(new Input.AircraftInput {
            RegNo = "BISO2",
            AircraftTypeCode = "ISOLATION",
            MinorTypeCode = "ISOLATION",
            LocationIcao = "ZISO",
            CostPerHour = 222,
            EnabledTime = now,
        });
        input.Aircrafts.Add(new Input.AircraftInput {
            RegNo = "BISO1",
            AircraftTypeCode = "OTHER",
            MinorTypeCode = "OTHER",
            LocationIcao = "ZNEW",
            CostPerHour = 333,
            EnabledTime = now.AddHours(1),
        });

        FlightRecoveryApplication.DomainData? data = FlightRecoveryApplication.BuildDomainData(input);

        Assert.NotNull(data);
        Assert.Equal(3, data.Aircrafts.Count);
        Assert.Same(data.Aircrafts[0], data.Aircrafts[2]);
        Assert.Same(data.Aircrafts[0].MinorType, data.Aircrafts[1].MinorType);
        Assert.Equal(new Flt64(111), data.Aircrafts[1].CostPerHour);
        Assert.Same(data.Aircrafts[0].Usability.Location, data.Aircrafts[1].Usability.Location);
        Assert.Same(data.Aircrafts[0].Usability.Location, Assert.Single(data.FlightTasks).Plan.Dep);
        Assert.Equal(2, data.AircraftUsability.Count);
    }

    [Fact]
    public void BuildDomainData_UsesRequestAirportTypes() {
        Input input = CreateInput(
            cost: 111,
            location: "ZISO",
            enabled: DateTimeOffset.UnixEpoch);
        input.Airports = new List<Input.AirportInput> {
            new() { Icao = "ZISO", Type = AirportType.International },
            new() { Icao = "ZISO", Type = AirportType.Domestic },
            new() { Icao = "ZNEW", Type = AirportType.Regional },
        };

        FlightRecoveryApplication.DomainData? data = FlightRecoveryApplication.BuildDomainData(input);

        Assert.NotNull(data);
        Aircraft aircraft = Assert.Single(data.Aircrafts);
        FlightLegPlan plan = Assert.IsType<FlightLegPlan>(Assert.Single(data.FlightTasks).Plan);
        Assert.Same(aircraft.Usability.Location, plan.Dep);
        Assert.Equal(AirportType.International, plan.Dep.Type);
        Assert.Equal(AirportType.Regional, plan.Arr.Type);
        Assert.Equal(FlightType.International, plan.FlightType);
    }

    [Theory]
    [InlineData("BAD", AirportType.Domestic)]
    [InlineData("ZBAD", (AirportType)99)]
    [InlineData(null, AirportType.Domestic)]
    public void BuildDomainData_RejectsInvalidRequestAirports(string? icao, AirportType type) {
        Input input = CreateInput(
            cost: 111,
            location: "ZISO",
            enabled: DateTimeOffset.UnixEpoch);
        input.Airports = new List<Input.AirportInput> { new() { Icao = icao!, Type = type } };

        Assert.Null(FlightRecoveryApplication.BuildDomainData(input));
    }

    [Fact]
    public void BuildDomainData_RejectsNullAirportEntry() {
        Input input = CreateInput(
            cost: 111,
            location: "ZISO",
            enabled: DateTimeOffset.UnixEpoch);
        input.Airports = new List<Input.AirportInput> { null! };

        Assert.Null(FlightRecoveryApplication.BuildDomainData(input));
    }

    [Fact]
    public void BuildDomainData_DefaultsUnspecifiedAirportTypeToDomestic() {
        Input input = CreateInput(
            cost: 111,
            location: "ZISO",
            enabled: DateTimeOffset.UnixEpoch);
        input.Airports = new List<Input.AirportInput> { new() { Icao = "ZISO" } };

        FlightRecoveryApplication.DomainData? data = FlightRecoveryApplication.BuildDomainData(input);

        Assert.NotNull(data);
        Assert.Equal(AirportType.Domestic, Assert.Single(data.Aircrafts).Usability.Location.Type);
    }

    [Fact]
    public async Task RunAsync_InvalidRequestAirport_ReturnsFailure() {
        Input input = CreateInput(
            cost: 111,
            location: "ZISO",
            enabled: DateTimeOffset.UnixEpoch);
        input.Airports = new List<Input.AirportInput> { new() { Icao = "BAD" } };
        var app = new FlightRecoveryApplication(new MockColumnGenerationSolver());

        Assert.True((await app.RunAsync(input)).IsFailed);
    }

    [Fact]
    public void BuildDomainData_ParallelInputsKeepTheirOwnValues() {
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        Parallel.For(
            fromInclusive: 0,
            toExclusive: 96,
            body: i => {
                FlightRecoveryApplication.DomainData? data = FlightRecoveryApplication.BuildDomainData(CreateInput(
                    cost: 100 + i,
                    location: "ZISO",
                    enabled: now.AddMinutes(i)));
                Assert.NotNull(data);
                Aircraft aircraft = Assert.Single(data.Aircrafts);
                Assert.Equal(new Flt64(100 + i), aircraft.CostPerHour);
                Assert.Equal(now.AddMinutes(i), aircraft.Usability.EnabledTime);
                Assert.Same(aircraft, Assert.Single(data.FlightTasks).Plan.Executor);
                Assert.Same(aircraft.Usability, data.AircraftUsability[aircraft]);
            });
    }

    [Fact]
    public void Registries_SupportConcurrentRegistrationAndSnapshots() {
        string prefix = Guid.NewGuid().ToString("N");
        var sharedCode = new AircraftTypeCode(prefix);
        var shared = new AircraftType[96];
        Parallel.For(
            fromInclusive: 0,
            toExclusive: shared.Length,
            body: i => shared[i] = AircraftType.GetOrAdd(sharedCode));
        foreach (AircraftType type in shared) {
            Assert.Same(shared[0], type);
        }

        IReadOnlyCollection<AircraftType> typeSnapshot = AircraftType.Values;
        var newType = AircraftType.GetOrAdd(new AircraftTypeCode(prefix + "snapshot"));
        Assert.DoesNotContain(newType, typeSnapshot);
        Assert.Contains(newType, AircraftType.Values);

        Parallel.For(
            fromInclusive: 0,
            toExclusive: 96,
            body: i => {
                string id = prefix + i;
                var airport = new Airport(new ICAO($"Q{i:000}"), AirportType.Domestic);
                Airport.Register(airport);
                Assert.Same(airport, Airport.Find(airport.Icao));
                var type = AircraftType.GetOrAdd(new AircraftTypeCode(id));
                var minor = new AircraftMinorType(
                    Type: type,
                    Code: new AircraftMinorTypeCode(id),
                    CostPerHour: Flt64.One,
                    RouteFlyTime: new Dictionary<Route, TimeSpan>(),
                    ConnectionTime: new Dictionary<Airport, TimeSpan>());
                AircraftMinorType.Register(minor);
                Assert.Same(minor, AircraftMinorType.Find(minor.Code));
                var aircraft = new Aircraft(
                    RegNo: new AircraftRegisterNumber(id),
                    MinorType: minor,
                    Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong>()));
                Aircraft.Register(aircraft);
                Assert.Same(aircraft, Aircraft.Find(aircraft.RegNo));
                var crew = new CrewMan(
                    WorkerNo: new WorkerNo(id),
                    Name: id,
                    Nationality: "CN");
                CrewMan.Register(crew);
                Assert.Same(crew, CrewMan.Find(crew.WorkerNo));
                var crewRank = new CrewManRank(new CrewManRankNo(id), id);
                CrewManRank.Register(crewRank);
                Assert.Same(crewRank, CrewManRank.Find(crewRank.No));
                var pilot = new Pilot(
                    Code: new PilotCode(id),
                    WorkerNo: new WorkerNo(id),
                    Name: id,
                    Nationality: "CN");
                Pilot.Register(pilot);
                Assert.Same(pilot, Pilot.Find(pilot.Code));
                Assert.Same(pilot, Pilot.Find(pilot.WorkerNo));
                var pilotRank = new PilotRank(new PilotRankNo(id), id);
                PilotRank.Register(pilotRank);
                Assert.Same(pilotRank, PilotRank.Find(pilotRank.No));
                Assert.Contains(airport, Airport.Values);
                Assert.Contains(minor, AircraftMinorType.Values);
                Assert.Contains(aircraft, Aircraft.Values);
                Assert.Contains(crew, CrewMan.Values);
                Assert.Contains(crewRank, CrewManRank.Values);
                Assert.Contains(pilot, Pilot.Values);
                Assert.Contains(pilotRank, PilotRank.Values);
            });
    }

    private static Input CreateInput(double cost, string location, DateTimeOffset enabled) => new() {
        WindowStart = enabled,
        WindowEnd = enabled.AddDays(1),
        Aircrafts = new List<Input.AircraftInput> {
            new() {
                RegNo = "BISO1",
                AircraftTypeCode = "ISOLATION",
                MinorTypeCode = "ISOLATION",
                LocationIcao = location,
                CostPerHour = cost,
                EnabledTime = enabled,
            },
        },
        FlightLegs = new List<Input.FlightLegInput> {
            new() {
                Id = "isolation",
                AircraftRegNo = "BISO1",
                DepIcao = location,
                ArrIcao = "ZNEW",
                ScheduledDepTime = enabled.AddHours(1),
                ScheduledArrTime = enabled.AddHours(2),
                Date = enabled,
            },
        },
    };
}
