// 资源使用量的三张表/ The resource usage's three tables.

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
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Resource;

/// <summary>
/// **资源使用量的三张表** / The resource usage's three tables.
/// </summary>
/// <remarks>
/// 资源模块是唯一整块没接线的地方：`IResourceUsage` 连数量表都没有，三个实现的 `Register` 都是返回空 `Ok` 的桩，
/// 于是容量约束那三条 `Invoke => Ok` 也就无米可炊。这里补最小的一片：**一个实现的 `quantity` / `overQuantity` / `lessQuantity` 三张表**。
///
/// 三件必须照抄基线：**范围要设**（不设是全域，松弛可以任意大，"超出多少"永远算成零）、
/// **超量取正部、不足取负部**、**槽自己的开关要单独看**（资源级开着不等于这个容量开）。
///
/// The resource module is the one block never wired: `IResourceUsage` did not even have a quantity table, all three `Register` implementations were stubs returning an empty `Ok`, and the
/// capacity constraint's three `Invoke => Ok` bodies had nothing to work with. adds the smallest slice: **the `quantity` / `overQuantity` / `lessQuantity` tables for one
/// implementation**. Three things must be copied from the baseline: **the range must be set** (unset is the full domain, the slack could be arbitrarily large, and "how much over" always
/// computes to zero), **over takes the positive part and less the negative part**, and **each slot's own switch is checked separately** (a resource-level flag does not mean this capacity has it on).
/// </remarks>
public class ResourceQuantityTableTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class TestExecutionResource : ExecutionResource<ResourceCapacity<Flt64>, Flt64> {
        public TestExecutionResource(
            string id,
            Flt64 initialQuantity,
            IReadOnlyList<ResourceCapacity<Flt64>> capacities) {
            Id = id;
            Capacities = capacities;
            InitialQuantityValue = initialQuantity;
        }

        public override string Id { get; }

        public override string Name => Id;

        public override IReadOnlyList<ResourceCapacity<Flt64>> Capacities { get; }

        public override Flt64 InitialQuantityValue { get; }

        public override Flt64 UsedBy<E, A>(IAbstractTask<E, A> task, TimeRange time) => Flt64.Zero;
    }

    /// <summary>松弛多项式里出现的辅助变量名 / The helper-variable names inside a slack's polynomial.</summary>
    /// <param name="slack">松弛符号 / The slack symbol.</param>
    /// <returns>名字列表 / The names.</returns>
    /// <remarks>
    /// **不是适配器就返回空**：槽没开超量 \/ 不足时放的是空符号（`LinearExpressionSymbol`），它没有任何辅助变量。
    /// 硬转会抛 `InvalidCastException`，那会把"空符号"这个**正确结果**报成失败。
    ///
    /// **A non-adapter yields an empty list**: a slot with over/less disabled holds an empty symbol (`LinearExpressionSymbol`) with no helper variables at all, and a hard cast would throw
    /// `InvalidCastException` and report that **correct** outcome as a failure.
    /// </remarks>
    private static List<string> SlackNames(ILinearIntermediateSymbol<Flt64> slack) =>
        slack is Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64> adapter
            ? adapter.Polynomial.Monomials.Select(monomial => monomial.Symbol.Name).ToList()
            : new List<string>();

    private static ResourceCapacity<Flt64> Capacity(
        int from,
        int to,
        Flt64 lower,
        Flt64 upper,
        Flt64? less = null,
        Flt64? over = null) =>
        new(new TimeRange(Origin.AddHours(from), Origin.AddHours(to)), lower, upper,
            less, over, TimeSpan.FromHours(8));

    private static (TestExecutionResource Resource, TimeWindow<Flt64> Window) Prepare(
        Flt64? less = null, Flt64? over = null) {
        var window = new TimeWindow<Flt64>(
            new TimeRange(Origin, Origin.AddHours(24)), true, TimeSpan.FromHours(8));
        var resource = new TestExecutionResource(
            "R1", new Flt64(3.0),
            new List<ResourceCapacity<Flt64>> { Capacity(0, 16, new Flt64(2.0), new Flt64(10.0), less, over) });
        return (resource, window);
    }

    /// <summary>
    /// 把 `object` 表转成具体键类型的字典 / Cast the `object` table onto its concrete key type.
    /// </summary>
    /// <remarks>
    /// **键类型必须写具体时间槽**：`IReadOnlyDictionary` 的键类型不协变，转成以 `IResourceTimeSlot` 为键的字典恒抛
    /// `InvalidCastException`—— 在产量侧踩过同一个坑。
    ///
    /// **The key type must be the concrete slot**: `IReadOnlyDictionary` is not covariant in its key type, so casting onto a map keyed by `IResourceTimeSlot` always throws
    /// `InvalidCastException` — the same trap hit on the produce side.
    /// </remarks>
    private static IReadOnlyDictionary<ExecutionResourceTimeSlot<TestExecutionResource, ResourceCapacity<Flt64>, Flt64>,
        ILinearIntermediateSymbol<Flt64>> Table(object? table) {
        table.Should().NotBeNull("registration must build the table");
        return (IReadOnlyDictionary<ExecutionResourceTimeSlot<TestExecutionResource, ResourceCapacity<Flt64>, Flt64>,
            ILinearIntermediateSymbol<Flt64>>)table!;
    }

    /// <summary>
    /// **注册之前三张表都是 `null`** / All three tables are `null` before registration.
    /// </summary>
    /// <remarks>
    /// 三点成对：未注册时三张表**都是 `null`**（不是空字典——空字典读起来像"每个槽的数量都是零"，那会让列生成按一个不存在的量去算）/
    /// 注册之后都有了条目 / 条目数等于时间槽数。
    ///
    /// Three paired points: before registration all three are **`null`** rather than empty maps (an empty map reads as "every slot's quantity is zero", which makes column generation
    /// compute against a quantity that does not exist); after registration all three have entries; and the entry count equals the slot count.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildAllThreeTables() {
        (TestExecutionResource resource, TimeWindow<Flt64> window) = Prepare(new Flt64(1.0), new Flt64(2.0));
        var usage = new BunchSchedulingExecutionResourceUsage<TestExecutionResource, ResourceCapacity<Flt64>, Flt64>(
            window, new List<TestExecutionResource> { resource }, "execution");
        using var model = new LinearMetaModel<Flt64>();

        usage.Quantity.Should().BeNull("an empty map would read as 'every quantity is zero'");
        usage.OverQuantity.Should().BeNull();
        usage.LessQuantity.Should().BeNull();

        usage.Register(model).IsOk.Should().BeTrue();

        Table(usage.Quantity).Should().HaveCount(usage.TimeSlots.Count);
        Table(usage.OverQuantity).Should().HaveCount(usage.TimeSlots.Count);
        Table(usage.LessQuantity).Should().HaveCount(usage.TimeSlots.Count);

        // **建了表还要进模型**：表只在内存里，模型里没有这些符号，松弛就无处可加——"表非空"这条断言看不出来。
        // **A built table must also enter the model**: a table that lives only in memory leaves the model without those symbols and the slacks have nowhere to go — "the table is non-empty"
        // cannot see that.
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
    }

    /// <summary>
    /// **数量符号带初始值常数，且范围是 `[下界 − 不足, 上界 + 超量]`** / The quantity symbol carries the initial value and ranges over `[lower - less, upper + over]`.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 数量符号的**常数**等于资源的初始数量（3.0）——它不是零变量，是一个已知的起点。
    /// 2. 范围下界是 `2.0 − 1.0 = 1.0`。
    /// 3. 范围上界是 `10.0 + 2.0 = 12.0`。
    /// 4. **不设范围时是全域**：若把范围那一行去掉，第 2 \/ 3 点会读到 ±∞。所以这两条断言正是"范围真的设了"的探针。
    ///
    /// Four paired points: the symbol's **constant** equals the resource's initial quantity (3.0), since it is not a zero variable but a known starting point; the range's lower bound is
    /// `2.0 - 1.0 = 1.0`; its upper bound is `10.0 + 2.0 = 12.0`; and **an unset range is the full domain** — dropping the range line makes points 2 and 3 read ±∞, so these two assertions are
    /// exactly the probe that the range was really set.
    /// </remarks>
    [Fact]
    public void Register_ShouldSetTheQuantityRangeFromTheCapacity() {
        (TestExecutionResource resource, TimeWindow<Flt64> window) = Prepare(new Flt64(1.0), new Flt64(2.0));
        var usage = new BunchSchedulingExecutionResourceUsage<TestExecutionResource, ResourceCapacity<Flt64>, Flt64>(
            window, new List<TestExecutionResource> { resource }, "execution");
        using var model = new LinearMetaModel<Flt64>();
        usage.Register(model).IsOk.Should().BeTrue();

        IReadOnlyDictionary<ExecutionResourceTimeSlot<TestExecutionResource, ResourceCapacity<Flt64>, Flt64>,
            ILinearIntermediateSymbol<Flt64>> table = Table(usage.Quantity);
        ILinearIntermediateSymbol<Flt64> symbol = table.Values.First();

        ((LinearExpressionSymbol)symbol).Polynomial.Constant.ToDouble().Should().Be(3.0,
            "the quantity starts at the resource's initial quantity, it is not a zero variable");
        symbol.Range.LowerBound.Should().NotBeNull();
        symbol.Range.LowerBound!.Value.Unwrap().ToDouble().Should().BeApproximately(1.0, 1e-9, "lower minus less");
        symbol.Range.UpperBound.Should().NotBeNull();
        symbol.Range.UpperBound!.Value.Unwrap().ToDouble().Should().BeApproximately(12.0, 1e-9, "upper plus over");
    }

    /// <summary>
    /// **超量松弛取正部、不足取负部，且都挂在数量符号上** / The over slack takes the positive part, the less slack the negative part, both over the quantity symbol.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 每个槽的超量松弛里**有且只有一个正部**（`_pos` 后缀的辅助变量），**没有负部**。
    /// 2. 不足松弛相反：**只有一个负部**，没有正部。
    /// 3. 两者都**不建约束**——度量归这里，约束归 `ResourceCapacityConstraint`。
    /// 4. 松弛的 `X` 就是数量符号本身，不是它的多项式（展开数量符号等于没扣，\/ 都踩过）。
    ///
    /// Four paired points: each slot's over slack holds **exactly one positive part** (`_pos` helper) and **no negative part**; the less slack is the mirror; neither **builds a constraint**,
    /// since measuring belongs here and constraining to `ResourceCapacityConstraint`; and the slack's `X` is the quantity symbol itself rather than its polynomial (expanding a quantity
    /// symbol costs nothing, as and both learned).
    /// </remarks>
    [Fact]
    public void Register_ShouldTakeThePositiveAndNegativeParts() {
        (TestExecutionResource resource, TimeWindow<Flt64> window) = Prepare(new Flt64(1.0), new Flt64(2.0));
        var real = new BunchSchedulingExecutionResourceUsage<TestExecutionResource, ResourceCapacity<Flt64>, Flt64>(
            window, new List<TestExecutionResource> { resource }, "execution");
        using var model = new LinearMetaModel<Flt64>();
        real.Register(model).IsOk.Should().BeTrue();

        IReadOnlyDictionary<ExecutionResourceTimeSlot<TestExecutionResource, ResourceCapacity<Flt64>, Flt64>,
            ILinearIntermediateSymbol<Flt64>> over = Table(real.OverQuantity);
        IReadOnlyDictionary<ExecutionResourceTimeSlot<TestExecutionResource, ResourceCapacity<Flt64>, Flt64>,
            ILinearIntermediateSymbol<Flt64>> less = Table(real.LessQuantity);

        // **看的是多项式里的辅助变量，不是适配器自己的名字**：`Build` 用给定的名字建适配器，
        // `_pos` / `_neg` 是它内部的辅助变量。
        // **Look at the helper variables inside the polynomial, not the adapter's own name**: `Build` names the adapter from the given name, while `_pos` / `_neg` are its internal helpers
        // (the the same lesson).
        List<string> overNames = SlackNames(over.Values.First());
        overNames.Should().Contain(name => name.EndsWith("_pos"));
        overNames.Should().NotContain(name => name.EndsWith("_neg"));
        List<string> lessNames = SlackNames(less.Values.First());
        lessNames.Should().Contain(name => name.EndsWith("_neg"));
        lessNames.Should().NotContain(name => name.EndsWith("_pos"));

        // **约束数为零**：松弛只是度量，约束由容量约束那条管线加。
        ((IMetaModel<Flt64>)model).Constraints.Should().BeEmpty("the slacks measure; the constraint pipeline constrains");
    }

    /// <summary>
    /// **槽自己的开关关着时给空符号，不是零松弛** / A slot whose own switch is off gets an empty symbol, not a zero slack.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 容量没启用超量（`over: null`）→ 超量表里那个槽是**空符号**（名字里没有 `_pos`），不是一条零松弛。
    /// 2. 数量表不受影响，照样建。
    /// 3. 换成启用超量的容量后，同一个槽就有 `_pos` 了——所以第 1 点是"看开关"，不是"没实现"。
    ///
    /// Three paired points: with the capacity's over quantity disabled (`over: null`) that slot's over entry is an **empty symbol** (no `_pos` in its name) rather than a zero slack; the
    /// quantity table is unaffected and still built; and with an over-enabled capacity the same slot does carry `_pos`, so point 1 is about the switch rather than about missing code.
    /// </remarks>
    [Fact]
    public void Register_ShouldUseAnEmptySymbolForADisabledSlot() {
        (TestExecutionResource disabled, TimeWindow<Flt64> window) = Prepare(new Flt64(1.0), null);
        var usage = new BunchSchedulingExecutionResourceUsage<TestExecutionResource, ResourceCapacity<Flt64>, Flt64>(
            window, new List<TestExecutionResource> { disabled }, "execution");
        using var model = new LinearMetaModel<Flt64>();
        usage.Register(model).IsOk.Should().BeTrue();

        Table(usage.Quantity).Should().NotBeEmpty();
        SlackNames(Table(usage.OverQuantity).Values.First()).Should().BeEmpty(
            "a disabled slot gets an empty symbol, not a slack that is identically zero");

        (TestExecutionResource enabled, TimeWindow<Flt64> window2) = Prepare(new Flt64(1.0), new Flt64(2.0));
        var second = new BunchSchedulingExecutionResourceUsage<TestExecutionResource, ResourceCapacity<Flt64>, Flt64>(
            window2, new List<TestExecutionResource> { enabled }, "execution");
        using var model2 = new LinearMetaModel<Flt64>();
        second.Register(model2).IsOk.Should().BeTrue();
        SlackNames(Table(second.OverQuantity).Values.First()).Should().Contain(name => name.EndsWith("_pos"));
    }
}
