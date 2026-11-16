// 连接资源使用的数量表/ The connection resource usage's quantity tables.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Resource;

/// <summary>
/// **连接资源使用的数量表** / The connection resource usage's quantity tables.
/// </summary>
/// <remarks>
/// 第三个、也是最后一个空 `Register`。连接资源的数量形状与执行资源相同（常数 = 初始数量），
/// 差别在 `invoke`：连接按"任务与时间槽的关联量"挂项，那是装配流水线的事，不属于 `register`。
///
/// 这里同时把三种资源使用里**逐字相同**的超量 \/ 不足两张表收进 `ResourceUsageSlackHelper`——
/// 三份抄下来，改一处必然漏两处。
///
/// The third and last bare `Register`. The connection resource's quantity has the same shape as the execution one (constant = the initial quantity); the difference is at `invoke`, where a
/// connection attaches terms by "the task-slot relation quantity", which belongs to the assembly pipeline rather than to `register`. also folds the **word-for-word identical**
/// over and less tables of all three usages into `ResourceUsageSlackHelper` — three copies would guarantee that fixing one misses the other two.
/// </remarks>
public class ConnectionResourceQuantityTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class TestConnection : ConnectionResource<ResourceCapacity<Flt64>, Flt64> {
        public TestConnection(Flt64 initial, IReadOnlyList<ResourceCapacity<Flt64>> capacities) {
            InitialQuantityValue = initial;
            Capacities = capacities;
        }

        public override string Id => "C1";

        public override string Name => "C1";

        public override IReadOnlyList<ResourceCapacity<Flt64>> Capacities { get; }

        public override Flt64 InitialQuantityValue { get; }

        public override Flt64 UsedBy<T, TE, TA>(T? prevTask, T? task, TimeRange time)
            where T : default => Flt64.Zero;
    }

    private static BunchSchedulingConnectionResourceUsage<TestConnection, ResourceCapacity<Flt64>, Flt64> Usage(
        Flt64? less = null, Flt64? over = null) {
        var window = new TimeWindow<Flt64>(new TimeRange(Origin, Origin.AddHours(16)), true, TimeSpan.FromHours(8));
        var resource = new TestConnection(new Flt64(7.0), new List<ResourceCapacity<Flt64>> {
            new(new TimeRange(Origin, Origin.AddHours(16)), new Flt64(2.0), new Flt64(10.0),
                less, over, TimeSpan.FromHours(8)),
        });
        return new BunchSchedulingConnectionResourceUsage<TestConnection, ResourceCapacity<Flt64>, Flt64>(
            window, new List<TestConnection> { resource }, "connection");
    }

    private static IReadOnlyDictionary<ConnectionResourceTimeSlot<TestConnection, ResourceCapacity<Flt64>, Flt64>,
        ILinearIntermediateSymbol<Flt64>> Table(object? table) {
        table.Should().NotBeNull("registration must build the table");
        return (IReadOnlyDictionary<ConnectionResourceTimeSlot<TestConnection, ResourceCapacity<Flt64>, Flt64>,
            ILinearIntermediateSymbol<Flt64>>)table!;
    }

    /// <summary>
    /// **每槽一个"常数等于初始数量"的数量符号，范围照容量** / One quantity symbol per slot, constant at the initial quantity, ranged from the capacity.
    /// </summary>
    /// <remarks>
    /// 四点成对：数量符号的常数是 **7**（初始数量，不是零）/ 范围下界 `2 − 1 = 1` / 范围上界 `10 + 4 = 14` /
    /// 三个数互不相同，算错任何一段都读得出来。
    ///
    /// Four paired points: the quantity symbol's constant is **7** (the initial quantity, not zero); the range's lower bound is `2 - 1 = 1`; its upper bound is `10 + 4 = 14`; and the three
    /// numbers differ, so getting any piece wrong is visible.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildRangedQuantitySymbols() {
        using var model = new LinearMetaModel<Flt64>();
        BunchSchedulingConnectionResourceUsage<TestConnection, ResourceCapacity<Flt64>, Flt64> usage = Usage(new Flt64(1.0), new Flt64(4.0));
        usage.Register(model).IsOk.Should().BeTrue();

        IReadOnlyDictionary<ConnectionResourceTimeSlot<TestConnection, ResourceCapacity<Flt64>, Flt64>,
            ILinearIntermediateSymbol<Flt64>> table = Table(usage.Quantity);
        table.Should().HaveCount(usage.TimeSlots.Count);
        ILinearIntermediateSymbol<Flt64> symbol = table.Values.First();
        ((LinearExpressionSymbol)symbol).Polynomial.Constant.ToDouble().Should().Be(7.0,
            "the connection quantity starts at the resource's initial quantity");
        symbol.Range.LowerBound!.Value.Unwrap().ToDouble().Should().BeApproximately(1.0, 1e-9, "2 lower minus 1 less");
        symbol.Range.UpperBound!.Value.Unwrap().ToDouble().Should().BeApproximately(14.0, 1e-9, "10 upper plus 4 over");
    }

    /// <summary>
    /// **三张表都建、都进模型、方向相反** / All three tables are built, registered, and point in opposite directions.
    /// </summary>
    /// <remarks>
    /// 四点成对：三张表条目数都等于槽数 / 三个符号都在模型里 / 超量是 `_pos`、不足是 `_neg` /
    /// 数量表在这个管线里**没有** `_pos` \/ `_neg`（它只有一个常数）——否则表就串了。
    ///
    /// Four paired points: all three tables hold one entry per slot; all three symbols are in the model; over is `_pos` and less is `_neg`; and the quantity entry carries **no** `_pos` or
    /// `_neg` (it is a single constant) — otherwise the tables have bled into each other.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildAndRegisterAllThreeTables() {
        using var model = new LinearMetaModel<Flt64>();
        BunchSchedulingConnectionResourceUsage<TestConnection, ResourceCapacity<Flt64>, Flt64> usage = Usage(new Flt64(1.0), new Flt64(4.0));
        usage.Register(model).IsOk.Should().BeTrue();

        Table(usage.OverQuantity).Should().HaveCount(usage.TimeSlots.Count);
        Table(usage.LessQuantity).Should().HaveCount(usage.TimeSlots.Count);

        var inModel = model.Tokens.Symbols.Select(symbol => symbol.Name).ToList();
        foreach (ILinearIntermediateSymbol<Flt64> symbol in Table(usage.Quantity).Values) {
            inModel.Should().Contain(symbol.Name);
            symbol.Name.Should().NotContain("_pos").And.NotContain("_neg");
        }
        // **松弛也要在模型里**：只查数量符号会让"松弛表建了但没进模型"整条变异存活——
        // 松弛不在模型里，容量约束就挂不到它上面，而表看起来完全正常。
        // **The slacks must be in the model too**: checking only the quantity symbols lets "the slack tables were built but never registered" survive, and a slack outside the model gives the
        // capacity constraint nothing to attach to while the tables look perfectly fine.
        foreach (ILinearIntermediateSymbol<Flt64> symbol in Table(usage.OverQuantity).Values) {
            inModel.Should().Contain(symbol.Name, "the over slack must be registered into the model");
        }
        foreach (ILinearIntermediateSymbol<Flt64> symbol in Table(usage.LessQuantity).Values) {
            inModel.Should().Contain(symbol.Name, "the less slack must be registered into the model");
        }
        SlackNames(Table(usage.OverQuantity).Values.First()).Should().Contain(name => name.EndsWith("_pos"));
        SlackNames(Table(usage.LessQuantity).Values.First()).Should().Contain(name => name.EndsWith("_neg"));
    }

    /// <summary>
    /// **槽自己没开超量时给空符号** / A slot with over disabled gets an empty symbol.
    /// </summary>
    /// <remarks>
    /// 三点成对：容量没开超量（`over: null`）→ 超量表那个槽是空符号（多项式里没有辅助变量）/ 数量表不受影响 /
    /// 换成开了超量的容量后同一个槽就有 `_pos`——所以第 1 点是在看开关，不是在"没实现"。
    ///
    /// Three paired points: with the capacity's over quantity disabled (`over: null`) the over entry is an empty symbol (no helper variable in its polynomial); the quantity table is
    /// unaffected; and with an over-enabled capacity the same slot does carry `_pos`, so point 1 is about the switch rather than about missing code.
    /// </remarks>
    [Fact]
    public void Register_ShouldUseAnEmptySymbolForADisabledSlot() {
        using var disabledModel = new LinearMetaModel<Flt64>();
        BunchSchedulingConnectionResourceUsage<TestConnection, ResourceCapacity<Flt64>, Flt64> disabled = Usage(new Flt64(1.0), null);
        disabled.Register(disabledModel).IsOk.Should().BeTrue();
        Table(disabled.Quantity).Should().NotBeEmpty();
        SlackNames(Table(disabled.OverQuantity).Values.First()).Should().BeEmpty(
            "a disabled slot gets an empty symbol, not a zero slack");

        using var enabledModel = new LinearMetaModel<Flt64>();
        BunchSchedulingConnectionResourceUsage<TestConnection, ResourceCapacity<Flt64>, Flt64> enabled = Usage(new Flt64(1.0), new Flt64(4.0));
        enabled.Register(enabledModel).IsOk.Should().BeTrue();
        SlackNames(Table(enabled.OverQuantity).Values.First()).Should().Contain(name => name.EndsWith("_pos"));
    }

    /// <summary>
    /// **未注册是 `null`，非模型与 `null` 分别失败** / Unregistered is `null`; a non-model and `null` fail differently.
    /// </summary>
    /// <remarks>
    /// 三点成对：未注册三张表都是 `null` / 非模型 → `Failed` / `null` → 抛 `ArgumentNullException`。
    ///
    /// Three paired points: before registration all three are `null`; a non-model gives `Failed`; and `null` throws `ArgumentNullException`.
    /// </remarks>
    [Fact]
    public void Register_ShouldExposeNullsAndRejectNonModels() {
        BunchSchedulingConnectionResourceUsage<TestConnection, ResourceCapacity<Flt64>, Flt64> usage = Usage();
        usage.Quantity.Should().BeNull("an empty map would read as 'every quantity is zero'");
        usage.OverQuantity.Should().BeNull();
        usage.LessQuantity.Should().BeNull();

        using var model = new LinearMetaModel<Flt64>();
        usage.Register(new object()).IsFailed.Should().BeTrue("connection usage needs a linear meta model");
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
