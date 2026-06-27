#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Crew.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchGeneration.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// Demo4 flight scheduling domain model tests.
/// </summary>
public class Demo4Tests {
    [Fact]
    public void SemanticParameter_IATA_ValidCode() {
        var iata = new IATA("PEK");
        Assert.Equal("PEK", iata.Code);
        Assert.Equal("PEK", iata.ToString());
    }

    [Fact]
    public void SemanticParameter_IATA_InvalidCode_Throws() {
        Assert.Throws<ArgumentException>(() => new IATA("PE"));
    }

    [Fact]
    public void SemanticParameter_ICAO_ValidCode() {
        var icao = new ICAO("ZBAA");
        Assert.Equal("ZBAA", icao.Code);
    }

    [Fact]
    public void Airport_RegisterAndFind() {
        var icao = new ICAO("ZBAA");
        var airport = new Airport(icao, AirportType.Domestic);
        Airport.Register(airport);
        Assert.Equal(airport, Airport.Find(icao));
    }

    [Fact]
    public void AircraftType_GetOrAdd() {
        var code = new AircraftTypeCode("B737");
        var type1 = AircraftType.GetOrAdd(code);
        var type2 = AircraftType.GetOrAdd(code);
        Assert.Same(type1, type2);
    }

    [Fact]
    public void FlightHour_Arithmetic() {
        var h1 = new FlightHour(TimeSpan.FromHours(5));
        var h2 = new FlightHour(TimeSpan.FromHours(3));
        Assert.Equal(TimeSpan.FromHours(8), (h1 + h2).Hours);
        Assert.Equal(TimeSpan.FromHours(2), (h1 - h2).Hours);
        Assert.True(h2 < h1);
        Assert.True(h2 <= h1);
    }

    [Fact]
    public void FlightCycle_Arithmetic() {
        var c1 = new FlightCycle(5);
        var c2 = new FlightCycle(3);
        Assert.Equal(8UL, (c1 + c2).Cycles);
        Assert.Equal(2UL, (c1 - c2).Cycles);
        Assert.True(c2 < c1);
    }

    [Fact]
    public void FlightCyclePeriod_Enabled() {
        var period = new FlightCyclePeriod(
            DateTimeOffset.UtcNow.AddDays(30),
            new FlightHour(TimeSpan.FromHours(100)),
            new FlightCycle(50));
        Assert.True(period.Enabled(new FlightHour(TimeSpan.FromHours(50))));
        Assert.False(period.Enabled(new FlightHour(TimeSpan.FromHours(150))));
        Assert.True(period.Enabled(new FlightCycle(30)));
        Assert.False(period.Enabled(new FlightCycle(60)));
    }

    [Fact]
    public void FlightTaskStatus_Mapping() {
        var status = FlightTaskStatus.NotAdvance | FlightTaskStatus.NotCancel;
        var taskStatus = FlightTaskStatusMapping.ToTaskStatus(status);
        Assert.Contains(Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.TaskStatus.NotAdvance, taskStatus);
        Assert.Contains(Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.TaskStatus.NotCancel, taskStatus);
        Assert.DoesNotContain(Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model.TaskStatus.NotDelay, taskStatus);
    }

    [Fact]
    public void FlightType_FromAirportTypes() {
        Assert.Equal(FlightType.Domestic, FlightTypeExtensions.FromAirportTypes(AirportType.Domestic, AirportType.Domestic));
        Assert.Equal(FlightType.International, FlightTypeExtensions.FromAirportTypes(AirportType.Domestic, AirportType.International));
    }

    [Fact]
    public void TransitTimeScene_Determine() {
        var icao1 = new ICAO("ZBAA");
        var icao2 = new ICAO("ZPPP");
        var airport1 = new Airport(icao1, AirportType.Domestic);
        var airport2 = new Airport(icao2, AirportType.Domestic);
        Airport.Register(airport1);
        Airport.Register(airport2);

        var minorCode = new AircraftMinorTypeCode("738");
        var typeCode = new AircraftTypeCode("B737");
        var type = AircraftType.GetOrAdd(typeCode);
        var minorType = new AircraftMinorType(type, minorCode, Flt64.One,
            new Dictionary<Route, TimeSpan>(), new Dictionary<Airport, TimeSpan>());
        AircraftMinorType.Register(minorType);

        var regNo = new AircraftRegisterNumber("B1234");
        var capacity = new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong>());
        var aircraft = new Aircraft(regNo, minorType, capacity);
        Aircraft.Register(aircraft);
        aircraft.SetUsability(new AircraftUsability(null, airport1, DateTimeOffset.UtcNow));

        var plan1 = new FlightLegPlan("1", "CA100", FlightType.Domestic, DateTimeOffset.UtcNow,
            aircraft, new HashSet<Aircraft> { aircraft }, airport1, airport2,
            new TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)),
            null, null, null, FlightTaskStatus.NotCancel);
        var leg1 = FlightLeg.Create(plan1);

        var plan2 = new FlightLegPlan("2", "CA200", FlightType.Domestic, DateTimeOffset.UtcNow.AddHours(3),
            aircraft, new HashSet<Aircraft> { aircraft }, airport2, airport1,
            new TimeRange(DateTimeOffset.UtcNow.AddHours(3), DateTimeOffset.UtcNow.AddHours(5)),
            null, null, null, FlightTaskStatus.NotCancel);
        var leg2 = FlightLeg.Create(plan2);

        var scene = TransitTimeSceneExtensions.Determine(leg1, leg2);
        Assert.Equal(TransitTimeScene.SameAircraft, scene);
    }

    [Fact]
    public void FlightTaskReverse_ReverseEnabled() {
        var icao1 = new ICAO("ZBAA");
        var icao2 = new ICAO("ZPPP");
        var airport1 = new Airport(icao1, AirportType.Domestic);
        var airport2 = new Airport(icao2, AirportType.Domestic);
        Airport.Register(airport1);
        Airport.Register(airport2);

        var minorCode = new AircraftMinorTypeCode("738");
        var typeCode = new AircraftTypeCode("B737");
        var type = AircraftType.GetOrAdd(typeCode);
        var minorType = new AircraftMinorType(type, minorCode, Flt64.One,
            new Dictionary<Route, TimeSpan>(), new Dictionary<Airport, TimeSpan>());
        AircraftMinorType.Register(minorType);

        var regNo = new AircraftRegisterNumber("B5678");
        var capacity = new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong>());
        var aircraft = new Aircraft(regNo, minorType, capacity);
        Aircraft.Register(aircraft);
        aircraft.SetUsability(new AircraftUsability(null, airport1, DateTimeOffset.UtcNow));

        var now = DateTimeOffset.UtcNow;
        var plan1 = new FlightLegPlan("10", "CA100", FlightType.Domestic, now,
            aircraft, new HashSet<Aircraft> { aircraft }, airport1, airport2,
            new TimeRange(now, now.AddHours(2)),
            null, null, null, FlightTaskStatus.NotCancel);
        var leg1 = FlightLeg.Create(plan1);

        var plan2 = new FlightLegPlan("20", "CA200", FlightType.Domestic, now.AddHours(3),
            aircraft, new HashSet<Aircraft> { aircraft }, airport2, airport1,
            new TimeRange(now.AddHours(3), now.AddHours(5)),
            null, null, null, FlightTaskStatus.NotCancel);
        var leg2 = FlightLeg.Create(plan2);

        var lockObj = new Lock();
        bool result = FlightTaskReverse.ReverseEnabled(leg1, leg2, lockObj, TimeSpan.FromHours(5));
        Assert.True(result);
    }

    [Fact]
    public void Restriction_RelationRestriction_Check() {
        var icao1 = new ICAO("ZBAA");
        var icao2 = new ICAO("ZPPP");
        var airport1 = new Airport(icao1, AirportType.Domestic);
        var airport2 = new Airport(icao2, AirportType.Domestic);
        Airport.Register(airport1);
        Airport.Register(airport2);

        var minorCode = new AircraftMinorTypeCode("738");
        var typeCode = new AircraftTypeCode("B737");
        var type = AircraftType.GetOrAdd(typeCode);
        var minorType = new AircraftMinorType(type, minorCode, Flt64.One,
            new Dictionary<Route, TimeSpan>(), new Dictionary<Airport, TimeSpan>());
        AircraftMinorType.Register(minorType);

        var regNo = new AircraftRegisterNumber("B9999");
        var capacity = new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong>());
        var aircraft = new Aircraft(regNo, minorType, capacity);
        Aircraft.Register(aircraft);
        aircraft.SetUsability(new AircraftUsability(null, airport1, DateTimeOffset.UtcNow));

        var restriction = new RelationRestriction(
            RestrictionType.Strong,
            RelationRestrictionCategory.BlackList,
            airport1, airport2,
            new HashSet<Aircraft> { aircraft });

        Assert.True(restriction.Related(aircraft));

        var now = DateTimeOffset.UtcNow;
        var plan = new FlightLegPlan("30", "CA300", FlightType.Domestic, now,
            aircraft, new HashSet<Aircraft> { aircraft }, airport1, airport2,
            new TimeRange(now, now.AddHours(2)),
            null, null, null, FlightTaskStatus.NotCancel);
        var leg = FlightLeg.Create(plan);

        var result = restriction.Check(leg);
        Assert.IsType<Violate>(result);
    }

    [Fact]
    public void Lock_LockedTime_ReturnsNullForUnknownTask() {
        var lockObj = new Lock();
        // Create a minimal task to test with
        var icao = new ICAO("ZBAA");
        var airport = new Airport(icao, AirportType.Domestic);
        Airport.Register(airport);
        var minorCode = new AircraftMinorTypeCode("738");
        var typeCode = new AircraftTypeCode("B737");
        var type = AircraftType.GetOrAdd(typeCode);
        var minorType = new AircraftMinorType(type, minorCode, Flt64.One,
            new Dictionary<Route, TimeSpan>(), new Dictionary<Airport, TimeSpan>());
        AircraftMinorType.Register(minorType);
        var regNo = new AircraftRegisterNumber("B0001");
        var capacity = new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong>());
        var aircraft = new Aircraft(regNo, minorType, capacity);
        Aircraft.Register(aircraft);
        aircraft.SetUsability(new AircraftUsability(null, airport, DateTimeOffset.UtcNow));
        var plan = new FlightLegPlan("99", "TEST", FlightType.Domestic, DateTimeOffset.UtcNow,
            aircraft, new HashSet<Aircraft> { aircraft }, airport, airport,
            new TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)),
            null, null, null, FlightTaskStatus.NotCancel);
        var leg = FlightLeg.Create(plan);
        Assert.Null(lockObj.LockedTime(leg));
    }

    [Fact]
    public void FlowControlCapacity_Close() {
        var time = new TimeRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2));
        var cap = FlowControlCapacity.Close(time);
        Assert.True(cap.Closed);
        Assert.Equal("closed", cap.ToString());
    }

    [Fact]
    public void BunchGenerationContext_Initializes() {
        var ctx = new BunchGenerationContext();
        Assert.Empty(ctx.InitialFlightBunches);
    }
}
