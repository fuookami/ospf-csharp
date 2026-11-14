// 产能排序的三维变量层（B726）/ The capacity-order three-dimensional variable layer (B726).

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.CapacityScheduling;

/// <summary>
/// **产能排序的三维变量层（B726）** / The capacity-order three-dimensional variable layer (B726).
/// </summary>
/// <remarks>
/// 基线的 `CapacityOrderCompilation.register` 建两个三维量：`x[动作, 时隙, 顺序位]`（数量，非负整数，逐格上界是
/// `action.upperBound(slot, timeWindow)`）与 `b[动作, 时隙, 顺序位]`（二元选中）。`OrderConstraint` 的三族约束全建在它们上面，
/// C# 侧两者都是 `null`，所以那个 `Invoke` 只能写 `=> Ok`。
///
/// **顺便记一次自己踩的坑**：这一批最初的实现新建了 `FltVariable2` / `UIntVariable3` / `BinVariable3` 三个类型，
/// 而 Core 里 `URealVariable2` / `UIntVar3` / `BinVariable3` 本来就有——**重复造轮子是这套代码库里最常见的浪费**，
/// 三个新类型全部删掉，改用现成的。
///
/// The baseline's `CapacityOrderCompilation.register` builds two three-dimensional quantities: `x[action, slot, order position]` (amount, non-negative integer, per-cell upper bound
/// `action.upperBound(slot, timeWindow)`) and `b[action, slot, order position]` (binary selection). All three `OrderConstraint` families build on them, and both are `null` on the C#
/// side, so that `Invoke` can only be `=> Ok`. **Also recording a self-inflicted detour**: the first version of this batch introduced `FltVariable2` / `UIntVariable3` /
/// `BinVariable3`, while Core already had `URealVariable2` / `UIntVar3` / `BinVariable3` — **re-inventing what already sits a few files over is the most common waste in this
/// codebase**, so all three new types were deleted in favour of the existing ones.
/// </remarks>
public class CapacityOrderVariablesTests {
    private sealed record TestOrderAction(string Id, Executor Executor, ulong Bound) : IProductionAction {
        public string Name => Id;

        public bool Discrete => true;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => Flt64.Zero;

        public ulong UpperBound(TimeRange slot, TimeWindow<Flt64> timeWindow) => Bound;

        public Flt64 UnitCapacity(TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static List<TimeRange> Slots() => new() { new TimeRange(Origin, Origin.AddHours(8)) };

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static List<TestOrderAction> Actions() {
        var executor = new TestExecutor("E1", "E1");
        return new List<TestOrderAction> {
            new("A1", executor, 5UL),
            new("A2", executor, 7UL),
        };
    }

    /// <summary>
    /// **注册前没有立方，取用直接抛** / There is no cube before registration, and using one throws.
    /// </summary>
    /// <remarks>
    /// 三点成对：未注册 `Amounts` 与 `Selections` 都是 `null` / 未注册取符号抛 `InvalidOperationException` /
    /// 注册之后同一个取用成功，且两个立方都建起来了。
    ///
    /// Three paired points: `Amounts` and `Selections` are both `null` before registration; taking a symbol throws `InvalidOperationException`; and after registration the same call
    /// succeeds with both cubes built.
    /// </remarks>
    [Fact]
    public void Cubes_ShouldBeNullUntilRegistered() {
        var variables = new CapacityOrderVariables<TestOrderAction>(Actions(), Slots(), Window(), 2UL);
        variables.Amounts.Should().BeNull();
        variables.Selections.Should().BeNull();

        Action beforeRegistration = () => variables.AmountOf(0, 0, 0);
        beforeRegistration.Should().Throw<InvalidOperationException>();

        using var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();
        variables.Amounts.Should().NotBeNull();
        variables.Selections.Should().NotBeNull();
        variables.AmountOf(0, 0, 0).Should().NotBeNull();
        variables.SelectionOf(0, 0, 0).Should().NotBeNull();
    }

    /// <summary>
    /// **逐格上界来自动作的 `UpperBound`，不是猜出来的 `M`** / The per-cell upper bound comes from the action, not a guessed `M`.
    /// </summary>
    /// <remarks>
    /// 三点成对：两个动作在**同一时隙同一顺序位**上的上界不同（5 与 7）——全都一样就说明上界没被逐格设置 /
    /// 同一动作的所有顺序位共享同一个上界（顺序位不该改变上界）/ 上界来自 `UpperBound(slot, window)` 而不是常量：
    /// 把动作的 `Bound` 换成 3，读到的上界就是 3。
    ///
    /// 这是 `OrderConstraint` 里 `x ≤ M · b` 的 `M` 的唯一正当来源：猜大了解松弛，猜小了切掉可行解。
    ///
    /// Three paired points: the two actions differ at the **same slot and position** (5 versus 7) — identical values would mean the bound was never set per cell; every position of one
    /// action shares that action's bound (a position must not change it); and the bound really comes from `UpperBound(slot, window)` rather than a constant — with the action's `Bound`
    /// set to 3 the read-back is 3. This is the only legitimate source of the `M` in `x <= M . b`: too large relaxes the model, too small cuts off feasible solutions.
    /// </remarks>
    [Fact]
    public void Amounts_ShouldCarryTheActionUpperBound() {
        var variables = new CapacityOrderVariables<TestOrderAction>(Actions(), Slots(), Window(), 2UL);
        using var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();

        UpperBoundOf(variables.Amounts!, 0, 0, 0).Should().Be(5UL);
        UpperBoundOf(variables.Amounts!, 1, 0, 0).Should().Be(7UL, "a different action has a different bound");
        UpperBoundOf(variables.Amounts!, 0, 0, 1).Should().Be(5UL, "the order position must not change the bound");

        var tight = new List<TestOrderAction> { new("A3", new TestExecutor("E1", "E1"), 3UL) };
        var tightVariables = new CapacityOrderVariables<TestOrderAction>(tight, Slots(), Window(), 1UL);
        using var tightModel = new LinearMetaModel<Flt64>();
        tightVariables.Register(tightModel).IsOk.Should().BeTrue();
        UpperBoundOf(tightVariables.Amounts!, 0, 0, 0).Should().Be(3UL, "the bound must be read from the action");
    }

    /// <summary>
    /// **选中标记是二元量，且与数量分别建、彼此不同** / Selections are binary, built separately from amounts and distinct.
    /// </summary>
    /// <remarks>
    /// 四点成对：`Selections` 的格与 `Amounts` 的格**不是同一个符号**（共用就退化成 `x = b`）/ `Selections` 的值域上限是 1 /
    /// `Amounts` 的上限不是 1（除非动作自己给了 1）/ 空动作、空时隙或零顺序位时注册成功但**不建立方**。
    ///
    /// Four paired points: a `Selections` cell and an `Amounts` cell are **not the same symbol** (sharing one degenerates to `x = b`); `Selections` caps at 1; `Amounts` does not cap at 1
    /// unless the action itself says so; and with no actions, no slots or zero positions, registration succeeds while **building no cube**.
    /// </remarks>
    [Fact]
    public void Selections_ShouldBeBinaryAndSeparateFromAmounts() {
        var variables = new CapacityOrderVariables<TestOrderAction>(Actions(), Slots(), Window(), 2UL);
        using var model = new LinearMetaModel<Flt64>();
        variables.Register(model).IsOk.Should().BeTrue();

        variables.SelectionOf(0, 0, 0).Should().NotBeSameAs(variables.AmountOf(0, 0, 0));
        UpperBoundOf(variables.Selections!, 0, 0, 0).Should().Be(1UL);
        UpperBoundOf(variables.Amounts!, 1, 0, 1).Should().Be(7UL, "amounts keep the action bound, not the binary cap");

        var empty = new CapacityOrderVariables<TestOrderAction>(Array.Empty<TestOrderAction>(), Slots(), Window(), 2UL);
        using var emptyModel = new LinearMetaModel<Flt64>();
        empty.Register(emptyModel).IsOk.Should().BeTrue();
        empty.Amounts.Should().BeNull("no actions means no cube");

        var noPositions = new CapacityOrderVariables<TestOrderAction>(Actions(), Slots(), Window(), 0UL);
        using var noPositionsModel = new LinearMetaModel<Flt64>();
        noPositions.Register(noPositionsModel).IsOk.Should().BeTrue();
        noPositions.Selections.Should().BeNull("zero order positions means no cube");
    }

    /// <summary>读取变量格的上界 / Read a variable cell's upper bound.</summary>
    /// <param name="cube">立方 / The cube.</param>
    /// <param name="action">动作下标 / The action index.</param>
    /// <param name="slot">时隙下标 / The slot index.</param>
    /// <param name="order">顺序位下标 / The order-position index.</param>
    /// <returns>上界 / The upper bound.</returns>
    private static ulong UpperBoundOf(UIntVar3 cube, int action, int slot, int order) {
        object? bound = cube[action, slot, order].Range.UpperBound?.Value?.Unwrap();
        bound.Should().NotBeNull("the bound must be set");
        // **`Convert.ToUInt64` 在这里会抛**：`UInt64` 不实现 `IConvertible`（与 `Flt64` 同一个坑）。
        // **`Convert.ToUInt64` throws here**: `UInt64` does not implement `IConvertible` (the same trap as `Flt64`).
        return ((Fuookami.Ospf.Math.Algebra.Number.UInt64)bound!).ToULong();
    }

    /// <summary>读取二元立方的上界 / Read a binary cube cell's upper bound.</summary>
    /// <param name="cube">立方 / The cube.</param>
    /// <param name="action">动作下标 / The action index.</param>
    /// <param name="slot">时隙下标 / The slot index.</param>
    /// <param name="order">顺序位下标 / The order-position index.</param>
    /// <returns>上界 / The upper bound.</returns>
    private static ulong UpperBoundOf(BinVariable3 cube, int action, int slot, int order) {
        object? bound = cube[action, slot, order].Range.UpperBound?.Value?.Unwrap();
        bound.Should().NotBeNull("the bound must be set");
        // `UInt8` 没有公开的数值取值器，用它的相等语义比较（`Unwrap` 给出的是代数数字本身）。
        // `UInt8` exposes no public numeric accessor, so compare with its own equality (`Unwrap` yields the algebra number itself).
        (bound is Fuookami.Ospf.Math.Algebra.Number.UInt8 narrow && narrow.Equals(new Fuookami.Ospf.Math.Algebra.Number.UInt8(1)))
            .Should().BeTrue("a binary variable caps at 1");
        return 1UL;
    }
}
