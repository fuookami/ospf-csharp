// 存储资源使用的组合式数量/ The storage resource usage's combined quantity.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Resource;

/// <summary>
/// **存储资源使用的组合式数量** / The storage resource usage's combined quantity.
/// </summary>
/// <remarks>
/// 执行资源的数量是"一个已知的起点"，存储的不是：**初始量 + 供应 − 消耗**，三段代数和。
/// 这条 `Register` 以前是空 `Ok`——表、范围、松弛一律没有，而"存储也有容量约束"这句话照样成立。
///
/// An execution resource's quantity is a known starting point; storage's is not: **initial + supply - consumption**, an algebra of three pieces. This `Register` used to be a bare `Ok` —
/// no tables, no ranges, no slacks — while "storage also has capacity constraints" still sounded true.
/// </remarks>
public class StorageResourceQuantityTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class TestStorage : StorageResource<ResourceCapacity<Flt64>, Flt64> {
        public TestStorage(Flt64 initial, Flt64 fixedSupply, IReadOnlyList<ResourceCapacity<Flt64>> capacities) {
            InitialQuantityValue = initial;
            FixedSupply = fixedSupply;
            Capacities = capacities;
        }

        public Flt64 FixedSupply { get; }

        public override string Id => "S1";

        public override string Name => "S1";

        public override IReadOnlyList<ResourceCapacity<Flt64>> Capacities { get; }

        public override Flt64 InitialQuantityValue { get; }

        public override Flt64 FixedSupplyIn(TimeSpan time) => FixedSupply * new Flt64(time.TotalHours / 8.0);

        public override Flt64 SupplyBy<T, TE, TA>(T task, TimeSpan time) => Flt64.Zero;

        public override Flt64 CostBy<T, TE, TA>(T task, TimeSpan time) => Flt64.Zero;
    }

    private static BunchSchedulingStorageResourceUsage<TestExecutor, TestStorage, ResourceCapacity<Flt64>, Flt64> Usage(
        Flt64 initial, Flt64 fixedSupply, Func<StorageResourceTimeSlot<TestStorage, ResourceCapacity<Flt64>, Flt64>, Flt64>? cost) {
        var window = new TimeWindow<Flt64>(new TimeRange(Origin, Origin.AddHours(16)), true, TimeSpan.FromHours(8));
        var resource = new TestStorage(initial, fixedSupply, new List<ResourceCapacity<Flt64>> {
            new(new TimeRange(Origin, Origin.AddHours(16)), new Flt64(2.0), new Flt64(30.0),
                new Flt64(1.0), new Flt64(4.0), TimeSpan.FromHours(8)),
        });
        return new BunchSchedulingStorageResourceUsage<TestExecutor, TestStorage, ResourceCapacity<Flt64>, Flt64>(
            window, new List<TestExecutor> { new("E1", "E1") },
            new List<TestStorage> { resource }, "storage", cost);
    }

    private static IReadOnlyDictionary<StorageResourceTimeSlot<TestStorage, ResourceCapacity<Flt64>, Flt64>,
        ILinearIntermediateSymbol<Flt64>> Table(object? table) {
        table.Should().NotBeNull("registration must build the table");
        return (IReadOnlyDictionary<StorageResourceTimeSlot<TestStorage, ResourceCapacity<Flt64>, Flt64>,
            ILinearIntermediateSymbol<Flt64>>)table!;
    }

    /// <summary>
    /// **数量是"初始 + 供应 − 消耗"，不是初始量** / The quantity is `initial + supply - consumption`, not the initial value.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 第一个槽的常数 = `3（初始）+ 5（每槽供应）− 1（消耗） = 7`。三个数**互不相同**，算错任何一段都读得出来。
    /// 2. 第二个槽 = `3 + 10 − 1 = 12`——供应按"从头到本槽结束"累计，不是每槽常量。
    /// 3. 没有消耗时第二个槽是 `3 + 10 = 13`，与第 2 点差 1——所以消耗真的被减掉了，而不是被忽略。
    /// 4. 两个槽的常数**不相等**，所以"每个槽都写成初始量"这种错法立刻暴露。
    ///
    /// Four paired points: the first slot's constant is `3 + 5 - 1 = 7`, and the three numbers **differ** so getting any piece wrong is visible; the second slot is `3 + 10 - 1 = 12`,
    /// because supply accumulates from the window's start rather than being constant per slot; without consumption the second slot is `3 + 10 = 13`, one more than point 2, so consumption
    /// really is subtracted; and the two slots' constants **differ**, so writing the initial value everywhere is immediately obvious.
    /// </remarks>
    [Fact]
    public void Register_ShouldCombineInitialSupplyAndConsumption() {
        using var model = new LinearMetaModel<Flt64>();
        BunchSchedulingStorageResourceUsage<TestExecutor, TestStorage, ResourceCapacity<Flt64>, Flt64> usage =
            Usage(new Flt64(3.0), new Flt64(5.0), _ => new Flt64(1.0));
        usage.Register(model).IsOk.Should().BeTrue();

        IReadOnlyDictionary<StorageResourceTimeSlot<TestStorage, ResourceCapacity<Flt64>, Flt64>,
            ILinearIntermediateSymbol<Flt64>> table = Table(usage.Quantity);
        var constants = table.Values
            .Select(symbol => ((LinearExpressionSymbol)symbol).Polynomial.Constant.ToDouble())
            .ToList();
        constants.Should().HaveCount(2);
        constants[0].Should().BeApproximately(7.0, 1e-9, "3 initial + 5 supply - 1 consumption");
        constants[1].Should().BeApproximately(12.0, 1e-9, "3 initial + 10 supply - 1 consumption");
        constants[0].Should().NotBe(constants[1], "supply accumulates, so a constant quantity is wrong");

        using var free = new LinearMetaModel<Flt64>();
        BunchSchedulingStorageResourceUsage<TestExecutor, TestStorage, ResourceCapacity<Flt64>, Flt64> withoutCost = Usage(new Flt64(3.0), new Flt64(5.0), cost: null);
        withoutCost.Register(free).IsOk.Should().BeTrue();
        var noCost = Table(withoutCost.Quantity).Values
            .Select(symbol => ((LinearExpressionSymbol)symbol).Polynomial.Constant.ToDouble())
            .ToList();
        noCost[1].Should().BeApproximately(13.0, 1e-9, "the same slot without the 1.0 of consumption");
        noCost[0].Should().BeApproximately(8.0, 1e-9);
    }

    /// <summary>
    /// **三张表都建、都进模型、范围都设** / All three tables are built, registered and ranged.
    /// </summary>
    /// <remarks>
    /// 四点成对：三张表的条目数都等于槽数 / 三张表里的符号都在模型里/
    /// 数量符号的范围是 `[2 − 1, 30 + 4]`（下界减不足、上界加超量）/ 超量与不足是两个方向（`_pos` / `_neg`）。
    ///
    /// Four paired points: all three tables hold one entry per slot; every symbol in them is in the model (the earlier "non-empty is not registered"); the quantity symbol's range is
    /// `[2 - 1, 30 + 4]` (lower minus less, upper plus over); and the over and less slacks point in opposite directions (`_pos` / `_neg`).
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildAndRegisterAllThreeTables() {
        using var model = new LinearMetaModel<Flt64>();
        BunchSchedulingStorageResourceUsage<TestExecutor, TestStorage, ResourceCapacity<Flt64>, Flt64> usage = Usage(new Flt64(3.0), new Flt64(5.0), _ => new Flt64(1.0));
        usage.Register(model).IsOk.Should().BeTrue();

        Table(usage.Quantity).Should().HaveCount(usage.TimeSlots.Count);
        Table(usage.OverQuantity).Should().HaveCount(usage.TimeSlots.Count);
        Table(usage.LessQuantity).Should().HaveCount(usage.TimeSlots.Count);

        var inModel = model.Tokens.Symbols.Select(symbol => symbol.Name).ToList();
        foreach (ILinearIntermediateSymbol<Flt64> symbol in Table(usage.Quantity).Values) {
            inModel.Should().Contain(symbol.Name, "the quantity symbol must be registered into the model");
        }
        foreach (ILinearIntermediateSymbol<Flt64> symbol in Table(usage.OverQuantity).Values) {
            inModel.Should().Contain(symbol.Name, "the over slack must be registered into the model");
        }
        foreach (ILinearIntermediateSymbol<Flt64> symbol in Table(usage.LessQuantity).Values) {
            inModel.Should().Contain(symbol.Name, "the less slack must be registered into the model");
        }

        ILinearIntermediateSymbol<Flt64> quantity = Table(usage.Quantity).Values.First();
        quantity.Range.LowerBound.Should().NotBeNull();
        quantity.Range.LowerBound!.Value.Unwrap().ToDouble().Should().BeApproximately(1.0, 1e-9, "2 lower minus 1 less");
        quantity.Range.UpperBound.Should().NotBeNull();
        quantity.Range.UpperBound!.Value.Unwrap().ToDouble().Should().BeApproximately(34.0, 1e-9, "30 upper plus 4 over");

        SlackNames(Table(usage.OverQuantity).Values.First()).Should().Contain(name => name.EndsWith("_pos"));
        SlackNames(Table(usage.LessQuantity).Values.First()).Should().Contain(name => name.EndsWith("_neg"));
    }

    /// <summary>
    /// **未注册时三张表都是 `null`，非模型与 `null` 分别失败** / Before registration all three are `null`; a non-model and `null` fail differently.
    /// </summary>
    /// <remarks>
    /// 三点成对：未注册三张表都是 `null`（空字典会读成"每个槽都是零"）/ 非模型 → `Failed` / `null` → 抛
    /// `ArgumentNullException`（与 `Failed` 不是同一条路径）。
    ///
    /// Three paired points: before registration all three are `null` (an empty map would read as "every slot is zero"); a non-model gives `Failed`; and `null` throws
    /// `ArgumentNullException`, which is not the same path as `Failed`.
    /// </remarks>
    [Fact]
    public void Register_ShouldExposeNullsAndRejectNonModels() {
        BunchSchedulingStorageResourceUsage<TestExecutor, TestStorage, ResourceCapacity<Flt64>, Flt64> usage = Usage(new Flt64(3.0), new Flt64(5.0), null);
        usage.Quantity.Should().BeNull("an empty map would read as 'every quantity is zero'");
        usage.OverQuantity.Should().BeNull();
        usage.LessQuantity.Should().BeNull();

        using var model = new LinearMetaModel<Flt64>();
        usage.Register(new object()).IsFailed.Should().BeTrue("storage usage needs a linear meta model");
        Action nullModel = () => usage.Register(null!);
        nullModel.Should().Throw<ArgumentNullException>();

        usage.Register(model).IsOk.Should().BeTrue();
        usage.Quantity.Should().NotBeNull();
    }

    private static List<string> SlackNames(ILinearIntermediateSymbol<Flt64> slack) =>
        slack is Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64> adapter
            ? adapter.Polynomial.Monomials.Select(monomial => monomial.Symbol.Name).ToList()
            : new List<string>();
}
