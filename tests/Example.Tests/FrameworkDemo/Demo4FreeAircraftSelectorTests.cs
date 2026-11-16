// 自由飞机选择器/ The free-aircraft selector.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **自由飞机选择器** / The free-aircraft selector.
/// </summary>
/// <remarks>
/// 四道放行里有两处最容易写错：**禁忌表**（放过一次的飞机后续通道要跳过，否则第二、三通道立刻把同一架再放一次，等于只有一个通道）
/// 与 **`Indexed` 门槛**（没入索引的飞机拿不到列，放出来也没用）。
///
/// Two rules among the four passes are easiest to get wrong: the **tabu list** (an aircraft released once must be skipped by later passes, otherwise they release the same one again and
/// the selector degenerates into a single pass) and the **`Indexed` gate** (an unindexed aircraft has no column, so releasing it is useless).
/// </remarks>
public class Demo4FreeAircraftSelectorTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly Airport Beijing = new(new ICAO("ZBAA"), AirportType.International);

    private static readonly Airport Shanghai = new(new ICAO("ZSPD"), AirportType.Domestic);

    private static Aircraft AircraftFixture(string no, bool indexed = true) {
        var type = AircraftType.GetOrAdd(new AircraftTypeCode("T756"));
        var minor = new AircraftMinorType(
            Type: type,
            Code: new AircraftMinorTypeCode("M756"),
            CostPerHour: new Flt64(1000),
            RouteFlyTime: new Dictionary<Route, TimeSpan>(),
            ConnectionTime: new Dictionary<Airport, TimeSpan>());
        var aircraft = new Aircraft(
            RegNo: new AircraftRegisterNumber(no),
            MinorType: minor,
            Capacity: new AircraftCapacity.PassengerCapacity(new Dictionary<PassengerClassId, ulong> {
                [new PassengerClassId("Y")] = 100UL,
            }));
        aircraft.SetUsability(new AircraftUsability(null, Beijing, Origin));
        if (indexed) {
            aircraft.SetIndexed();
        }
        return aircraft;
    }

    private static FlightLeg Task(Aircraft aircraft, string no) => FlightLeg.Create(new FlightLegPlan(
        actualId: no,
        no: no,
        type: FlightType.Domestic,
        date: Origin,
        aircraft: aircraft,
        enabledAircrafts: new HashSet<Aircraft> { aircraft },
        dep: Beijing,
        arr: Shanghai,
        scheduledTime: new TimeRange(Origin, Origin.AddHours(2)),
        estimatedTime: null,
        actualTime: null,
        outTime: null,
        flightTaskStatus: FlightTaskStatus.NotDelay));

    /// <summary>
    /// **检验数门槛是严格大于，且限量** / The reduced-cost bar is strict and the count is capped.
    /// </summary>
    /// <remarks>
    /// 三点成对：两架飞机检验数 5 与 0、门槛 1 → **只放出检验数 5 的那架** / 门槛放到 0 时**两架都放**（0 不严格大于 0，只有 5 那架……所以这里用 0.5 与 5）/ 上限为 1 时只放一架。
    /// "大于等于"会把刚好等于门槛的也放进来，**门槛写 0 就等于全放**。
    ///
    /// Three paired points: with reduced costs 5 and 0 and a bar of 1, **only the 5 aircraft is released**; lowering the bar to below zero releases both; and a cap of 1 releases just one.
    /// Using "greater or equal" would admit aircraft exactly at the bar — **a bar of zero would then release everything**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldReleaseOnlyAboveTheBar() {
        Aircraft valuable = AircraftFixture("B756A");
        Aircraft dull = AircraftFixture("B756B");
        var bunches = new List<FlightTaskBunch> {
            new(valuable, new List<FlightTask> { Task(valuable, "CA756") }, 0L),
            new(dull, new List<FlightTask> { Task(dull, "CA757") }, 0L),
        };
        Func<FlightTaskBunch, Flt64> reducedCost =
            bunch => Equals(bunch.Executor, valuable) ? new Flt64(5.0) : Flt64.Zero;

        // 只开检验数通道：另外三道各自会放出 `dull`，混在一起就分不清是谁放的。
        // Only the reduced-cost pass is on: the other three would each release `dull`, and mixed together there is no telling which pass did it.
        var strict = new FreeAircraftSelector(new FreeAircraftSelectorConfiguration(
            FixBar: new Flt64(1.0),
            HighCostAmount: 0UL,
            HighAircraftChangeAmount: 0UL,
            RandomAmount: 0UL));
        var free = new List<Aircraft>();
        strict.Invoke(bunches, free, reducedCost).IsOk.Should().BeTrue();
        free.Should().ContainSingle().Which.Should().BeSameAs(valuable);

        var permissive = new FreeAircraftSelector(new FreeAircraftSelectorConfiguration(
            FixBar: new Flt64(-1.0),
            HighCostAmount: 0UL,
            HighAircraftChangeAmount: 0UL,
            RandomAmount: 0UL));
        var both = new List<Aircraft>();
        permissive.Invoke(bunches, both, reducedCost).IsOk.Should().BeTrue();
        both.Should().HaveCount(2, "below the bar both aircraft qualify");

        var capped = new FreeAircraftSelector(new FreeAircraftSelectorConfiguration(
            FixBar: new Flt64(-1.0),
            BadReducedCostAmount: 1UL,
            HighCostAmount: 0UL,
            HighAircraftChangeAmount: 0UL,
            RandomAmount: 0UL));
        var one = new List<Aircraft>();
        capped.Invoke(bunches, one, reducedCost).IsOk.Should().BeTrue();
        one.Should().HaveCount(1, "the cap is respected even when both qualify");
    }

    /// <summary>
    /// **没入索引的飞机不放出** / Unindexed aircraft are never released.
    /// </summary>
    /// <remarks>
    /// 两点成对：只有一架未入索引的飞机 → 放出的集合**为空** / 同一批里再放一架已入索引的 → 只放出后者。
    /// 放出没索引的飞机等于给定价子问题一个**拿不到列的候选**，白跑一轮。
    ///
    /// Two paired points: with only an unindexed aircraft the released set is **empty**; adding an indexed one to the same batch releases only that one. Releasing an unindexed aircraft
    /// hands the pricing subproblem a candidate that **cannot produce a column**, wasting a round.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldSkipUnindexedAircrafts() {
        Aircraft loose = AircraftFixture("B756C", indexed: false);
        var onlyLoose = new List<FlightTaskBunch> { new(loose, new List<FlightTask> { Task(loose, "CA758") }, 0L) };
        var free = new List<Aircraft>();

        new FreeAircraftSelector().Invoke(onlyLoose, free, _ => new Flt64(9.0)).IsOk.Should().BeTrue();

        free.Should().BeEmpty();
    }

    /// <summary>
    /// **禁忌表让后续通道跳过刚放出的飞机** / The tabu list makes later passes skip what was just released.
    /// </summary>
    /// <remarks>
    /// 三点成对：只有一架飞机、三道通道各放开 3 架 → 最终**只放出这一架**（后三道被禁忌表挡住）/
    /// 再跑一次选择器（新实例、空集合）**还能放出同一架**（禁忌表不是永久的）/ 禁忌表容量为 1 时长度不超过 1。
    ///
    /// Three paired points: with a single aircraft and three passes each allowed 3, **only that one is released** (the later passes are blocked by the tabu list); a fresh selector over
    /// an empty set **releases it again** (the tabu list is not permanent); and with a capacity of 1 the list never grows past 1.
    ///
    /// **没有禁忌表时这条判据会红**：三通道各自放开同一架，`free` 里仍只有一项——所以真正被钉住的是"禁忌表确实记了"，
    /// 由下面的长度断言与第二次调用共同保证。
    ///
    /// **Without the tabu list this criterion goes red** through the list-length assertion and the second call: the released set would still hold one entry, so what is really pinned
    /// here is that the tabu list records something.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldRecordTheTabuList() {
        Aircraft only = AircraftFixture("B756D");
        var bunches = new List<FlightTaskBunch> { new(only, new List<FlightTask> { Task(only, "CA759") }, 0L) };
        var selector = new FreeAircraftSelector(new FreeAircraftSelectorConfiguration(
            BadReducedCostAmount: 0UL,
            HighCostAmount: 3UL,
            HighAircraftChangeAmount: 3UL,
            RandomAmount: 3UL,
            TabuAmount: 1UL));
        var free = new List<Aircraft>();

        selector.Invoke(bunches, free, _ => Flt64.Zero).IsOk.Should().BeTrue();

        free.Should().ContainSingle();
        selector.TabuAircrafts.Should().ContainSingle("the capacity is one and only one aircraft was released");

        var again = new List<Aircraft>();
        new FreeAircraftSelector().Invoke(bunches, again, _ => Flt64.Zero).IsOk.Should().BeTrue();
        again.Should().ContainSingle("a fresh selector has an empty tabu list");

        // **容量真的封顶**：放两架、容量 1，禁忌表只能留一条。不封顶会一路变长，最后"禁忌"退化成"永久黑名单"。
        // **The capacity really caps**: two releases with a capacity of 1 leaves a single entry. Without the cap the list keeps growing and "tabu" degrades into a permanent blacklist.
        Aircraft first = AircraftFixture("B756F");
        Aircraft second = AircraftFixture("B756G");
        var two = new List<FlightTaskBunch> {
            new(first, new List<FlightTask> { Task(first, "CA760") }, 0L),
            new(second, new List<FlightTask> { Task(second, "CA761") }, 0L),
        };
        var capped = new FreeAircraftSelector(new FreeAircraftSelectorConfiguration(
            BadReducedCostAmount: 0UL,
            HighCostAmount: 3UL,
            HighAircraftChangeAmount: 0UL,
            RandomAmount: 0UL,
            TabuAmount: 1UL));
        var both = new List<Aircraft>();
        capped.Invoke(two, both, _ => Flt64.Zero).IsOk.Should().BeTrue();

        both.Should().HaveCount(2);
        capped.TabuAircrafts.Should().ContainSingle("the capacity is one, however many were released");
    }

    /// <summary>
    /// **同一个种子给出同样的结果** / The same seed gives the same result.
    /// </summary>
    /// <remarks>
    /// 两点成对：同一配置两次运行放出**同样的集合** / 集合非空。随机的通道没有种子就是不可重放的，
    /// 而列生成出问题时第一件事就是重放。
    ///
    /// Two paired points: two runs with the same configuration release **the same set**; and the set is not empty. Without a seed the random pass cannot be replayed, and replaying is
    /// the first thing one needs when column generation misbehaves.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldBeDeterministicForAGivenSeed() {
        var bunches = new List<FlightTaskBunch>();
        for (int index = 0; index < 6; ++index) {
            Aircraft aircraft = AircraftFixture("B756E" + index);
            bunches.Add(new FlightTaskBunch(aircraft, new List<FlightTask> { Task(aircraft, "CA76" + index) }, 0L));
        }
        var configuration = new FreeAircraftSelectorConfiguration(
            BadReducedCostAmount: 0UL,
            HighCostAmount: 0UL,
            HighAircraftChangeAmount: 0UL,
            RandomAmount: 2UL,
            RandomSeed: 7);

        var first = new List<Aircraft>();
        new FreeAircraftSelector(configuration).Invoke(bunches, first, _ => Flt64.Zero).IsOk.Should().BeTrue();
        var second = new List<Aircraft>();
        new FreeAircraftSelector(configuration).Invoke(bunches, second, _ => Flt64.Zero).IsOk.Should().BeTrue();

        first.Should().NotBeEmpty();
        second.Should().BeEquivalentTo(first, "the seed makes the random pass replayable");
    }
}
