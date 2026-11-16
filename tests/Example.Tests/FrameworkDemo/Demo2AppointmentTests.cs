// Demo2 预约映射与生物限制的实现测试 / Port tests for Demo2's appointment mapping and biological limits.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// `Appointment` 与 `BiologicalLimit` 的行为 / The behaviour of `Appointment` and `BiologicalLimit`.
/// </summary>
/// <remarks>
/// 这两个类型原本是只有一行 `// TODO` 的空壳。这里钉住三件容易写错的事：
/// **反向映射要合并同舱位的多个货物**、**未预约的货物要返回 null 而不是抛异常**、
/// **未预约的舱位要返回空列表而不是 null**。
///
/// Both types were one-line `// TODO` shells. These pin the three things easiest to get wrong: **the reverse map must merge several items on one position**,
/// **an unappointed item must return null rather than throw**, and **an unappointed position must return an empty list rather than null**.
/// </remarks>
public class Demo2AppointmentTests
{
    private static Position MakePosition(ulong id, string spaceName) =>
        new Position(
            id: id,
            spaceName: spaceName,
            sizeCode: "A",
            linearLoadingOrder: (byte)0,
            coordinate: new PositionCoordinate(
                frontArm: 100.0,
                backArm: 120.0,
                leftArm: -10.0,
                rightArm: 10.0
            ),
            shape: new PositionShape(
                Length: 20.0,
                Width: 20.0,
                Volume: 8000.0
            ),
            location: PositionLocation.NormalMainLocation
        );

    private static Item MakeItem(string name, double weight) =>
        new Item(
            name: name,
            weight: weight,
            priority: new CargoPriority(
                Name: "Normal",
                Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL),
                Category: CargoPriorityCategory.Normal
            ),
            cargoType: CargoType.FromName("General"),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: new ItemLocation(
                enabledLocations: new HashSet<DeckLocation> { DeckLocation.Main }
            )
        );

    [Fact]
    public void Appointment_ShouldReturnTheAppointedPosition()
    {
        Position position = MakePosition(id: 1, spaceName: "11A");
        Item item = MakeItem(name: "cargo-1", weight: 100.0);
        var appointment = new Appointment(
            appointment: new Dictionary<Item, Position> { [item] = position }
        );

        appointment.PositionOf(item).Should().BeSameAs(position);
    }

    [Fact]
    public void Appointment_ShouldReturnNullForAnUnappointedItem()
    {
        Position position = MakePosition(id: 1, spaceName: "11A");
        Item appointed = MakeItem(name: "cargo-1", weight: 100.0);
        Item stranger = MakeItem(name: "cargo-2", weight: 200.0);
        var appointment = new Appointment(
            appointment: new Dictionary<Item, Position> { [appointed] = position }
        );

        appointment.PositionOf(stranger).Should().BeNull();
    }

    /// <summary>**同一舱位上的多个货物必须合并** / Several items on one position must be merged.</summary>
    [Fact]
    public void Appointment_ReverseMap_ShouldMergeSeveralItemsOnOnePosition()
    {
        Position position = MakePosition(id: 1, spaceName: "11A");
        Item first = MakeItem(name: "cargo-1", weight: 100.0);
        Item second = MakeItem(name: "cargo-2", weight: 200.0);
        var appointment = new Appointment(
            appointment: new Dictionary<Item, Position>
            {
                [first] = position,
                [second] = position,
            }
        );

        appointment.ItemsOf(position).Should().HaveCount(2);
        appointment.Reverse[position].Should().HaveCount(2);
    }

    [Fact]
    public void Appointment_ShouldReturnEmptyForAnUnappointedPosition()
    {
        Position appointed = MakePosition(id: 1, spaceName: "11A");
        Position stranger = MakePosition(id: 2, spaceName: "12A");
        Item item = MakeItem(name: "cargo-1", weight: 100.0);
        var appointment = new Appointment(
            appointment: new Dictionary<Item, Position> { [item] = appointed }
        );

        appointment.ItemsOf(stranger).Should().BeEmpty();
    }

    /// <summary>**反向映射要按舱位分组，不能把不同舱位的货物混在一起** / The reverse map must group by position.</summary>
    [Fact]
    public void Appointment_ReverseMap_ShouldSeparateDistinctPositions()
    {
        Position firstPosition = MakePosition(id: 1, spaceName: "11A");
        Position secondPosition = MakePosition(id: 2, spaceName: "12A");
        Item firstItem = MakeItem(name: "cargo-1", weight: 100.0);
        Item secondItem = MakeItem(name: "cargo-2", weight: 200.0);
        var appointment = new Appointment(
            appointment: new Dictionary<Item, Position>
            {
                [firstItem] = firstPosition,
                [secondItem] = secondPosition,
            }
        );

        appointment.Reverse.Should().HaveCount(2);
        appointment.ItemsOf(firstPosition).Should().ContainSingle().Which.Should().BeSameAs(firstItem);
        appointment.ItemsOf(secondPosition).Should().ContainSingle().Which.Should().BeSameAs(secondItem);
    }

    /// <summary>**空预约的三种查询都必须安全** / All three lookups must be safe on an empty mapping.</summary>
    [Fact]
    public void Appointment_ShouldBeSafeWhenEmpty()
    {
        var appointment = new Appointment(appointment: new Dictionary<Item, Position>());
        Item item = MakeItem(name: "cargo-1", weight: 100.0);
        Position position = MakePosition(id: 1, spaceName: "11A");

        appointment.PositionOf(item).Should().BeNull();
        appointment.ItemsOf(position).Should().BeEmpty();
        appointment.Reverse.Should().BeEmpty();
    }

    [Fact]
    public void BiologicalLimit_ShouldCarryAllThreeRuleTables()
    {
        var first = CargoType.FromName("Live");
        var second = CargoType.FromName("Food");
        var limit = new BiologicalLimit(
            adjacentLimit: new List<(CargoType First, CargoType Second)> { (first, second) },
            bulkConflictLimit: new List<(CargoType First, CargoType Second)> { (second, first) },
            bulkLimit: new List<CargoType> { first }
        );

        limit.AdjacentLimit.Should().HaveCount(1);
        limit.BulkConflictLimit.Should().HaveCount(1);
        limit.BulkLimit.Should().ContainSingle().Which.Should().BeSameAs(first);
    }

    [Fact]
    public void BiologicalLimit_ShouldAllowEmptyRules()
    {
        var limit = new BiologicalLimit(
            adjacentLimit: new List<(CargoType First, CargoType Second)>(),
            bulkConflictLimit: new List<(CargoType First, CargoType Second)>(),
            bulkLimit: new List<CargoType>()
        );

        limit.AdjacentLimit.Should().BeEmpty();
        limit.BulkConflictLimit.Should().BeEmpty();
        limit.BulkLimit.Should().BeEmpty();
    }
}
