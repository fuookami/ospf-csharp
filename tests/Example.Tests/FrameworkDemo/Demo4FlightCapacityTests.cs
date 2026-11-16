// 航班容量表达式层/ The flight capacity expression layer.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **航班容量表达式层** / The flight capacity expression layer.
/// </summary>
/// <remarks>
/// 基线 `FlightCapacity.kt` 在 C# 侧根本不存在：**"这一轮到底运了多少人、多少货"没有量可读**。补上之后还顺手修了两处
/// "构造参数不是成员"的缺陷——`PassengerCapacity` 的舱位表和 `CargoCapacity` 的货量，**在类型外都读不到**。
///
/// **天花板**：判据只覆盖"开关与表的存在性"，还没有任务实例走一遍 `AddColumns`——造一个 `FlightTask` 需要 `FlightLeg` 等一整套输入，
/// 那是后续的事。升级路径：有任务夹具之后补"零容量的束不进表达式"与"列变量按去重顺序取"。

/// The baseline's `FlightCapacity.kt` did not exist on the C# side at all: **how many passengers or how much cargo a round really moves had no readable quantity**. Two "a
/// constructor parameter is not a member" defects were fixed along the way — `PassengerCapacity`'s cabin table and `CargoCapacity`'s value were both **unreachable from outside**.
///
/// **Ceiling**: the criteria cover only the switches and the presence of the tables; no real task instance walks `AddColumns` yet, because building one needs `FlightLeg` and a whole
/// input stack — that is the next step. Upgrade path: once a task fixture exists, add "zero-capacity bunches stay out" and "column variables follow the deduplicated order".
/// </remarks>
public class Demo4FlightCapacityTests {
    private static BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> Compilation() =>
        new(new List<FlightTask>(), new List<Aircraft>());

    /// <summary>
    /// **没有任务时两个开关都关，注册什么都不建** / With no tasks both switches are off and registration builds nothing.
    /// </summary>
    /// <remarks>
    /// 三点成对：`WithPassenger` / `WithCargo` 都为假（按任务推断）/ 注册成功 / 两张表都是 `null`。
    /// **"没打开"必须与"打开了但空"区分开**——前者是 `null`，后者是空字典。
    ///
    /// Three paired points: both `WithPassenger` and `WithCargo` are false (inferred from the tasks); registration succeeds; and both tables are `null`. **"Not enabled" must be
    /// distinguishable from "enabled but empty"** — the former is null, the latter an empty dictionary.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildNothingWithoutTasks() {
        using var model = new LinearMetaModel<Flt64>();
        var capacity = new FlightCapacity(new List<FlightTask>(), Compilation());

        capacity.WithPassenger.Should().BeFalse();
        capacity.WithCargo.Should().BeFalse();
        capacity.Register(model).IsOk.Should().BeTrue();
        capacity.Passenger.Should().BeNull();
        capacity.Cargo.Should().BeNull();
    }

    /// <summary>
    /// **强制打开时表存在但为空** / Forcing a switch on yields an empty table rather than null.
    /// </summary>
    /// <remarks>
    /// 两点成对：`withPassenger: true` 且没有任务 → `Passenger` **不是 `null`**（是空字典）/ 模型里一个符号都没多。
    /// 这条正是上一条的对照：**同一个"没有符号"的结果，来自两种不同的原因**，判据必须分得开。
    ///
    /// Two paired points: `withPassenger: true` with no tasks leaves `Passenger` **non-null** (an empty dictionary); and not a single symbol enters the model. This is the control for
    /// the previous point: **the same "no symbols" outcome arises from two different causes**, and the criteria must tell them apart.
    /// </remarks>
    [Fact]
    public void Register_ShouldExposeAnEmptyTableWhenForcedOn() {
        using var model = new LinearMetaModel<Flt64>();
        var capacity = new FlightCapacity(new List<FlightTask>(), Compilation(), withPassenger: true);

        capacity.Register(model).IsOk.Should().BeTrue();

        capacity.Passenger.Should().NotBeNull("the switch is on, so the table exists even when empty");
        capacity.Passenger!.Should().BeEmpty();
        model.Tokens.Symbols.Should().BeEmpty("nothing was built");
    }

    /// <summary>
    /// **没注册就加列会显式失败** / Adding columns before registration fails explicitly.
    /// </summary>
    /// <remarks>
    /// 两点成对：未注册时 `AddColumns` → `Failed` / 注册之后同一个调用 → `Ok`。**静默返回 `Ok` 会让"容量完全没进模型"看起来像成功**，
    /// 这正是这一族缺陷最容易骗过人的形态。
    ///
    /// Two paired points: `AddColumns` before registration yields `Failed`; the same call after registration yields `Ok`. **A silent `Ok` would make "the capacity never reached the
    /// model" look like success**, which is exactly how this family of defects fools a reviewer.
    /// </remarks>
    [Fact]
    public void AddColumns_ShouldFailBeforeRegister() {
        using var model = new LinearMetaModel<Flt64>();
        var capacity = new FlightCapacity(new List<FlightTask>(), Compilation(), withPassenger: true, withCargo: true);

        capacity.AddColumns(0UL, new List<FlightTaskBunch>()).IsFailed.Should().BeTrue();

        capacity.Register(model).IsOk.Should().BeTrue();
        capacity.AddColumns(0UL, new List<FlightTaskBunch>()).IsOk.Should().BeTrue();
    }

    /// <summary>
    /// **容量值在类型外读得到** / The capacity values are readable from outside.
    /// </summary>
    /// <remarks>
    /// 三点成对：`PassengerCapacity.Capacity` 暴露舱位表 / `CargoCapacity.ContainedCapacity` 暴露货量 / 两者与构造时给的值一致。
    /// 此前它们只是主构造函数的参数——**构造得出来，读不出来**。
    ///
    /// Three paired points: `PassengerCapacity.Capacity` exposes the cabin table; `CargoCapacity.ContainedCapacity` exposes the cargo value; and both match what was passed in. They used
    /// to be primary-constructor parameters only — **constructible but unreadable**.
    /// </remarks>
    [Fact]
    public void CapacityValues_ShouldBeReadable() {
        var cabins = new Dictionary<PassengerClassId, ulong> { [new PassengerClassId("Y")] = 120UL, [new PassengerClassId("J")] = 8UL };
        var passenger = new AircraftCapacity.PassengerCapacity(cabins);
        var cargo = new AircraftCapacity.CargoCapacity(new Flt64(250.0));

        passenger.Capacity.Should().HaveCount(2);
        passenger.Capacity[new PassengerClassId("Y")].Should().Be(120UL);
        passenger.Total.Should().Be(128UL);
        cargo.ContainedCapacity.ToDouble().Should().Be(250.0);
    }
}
