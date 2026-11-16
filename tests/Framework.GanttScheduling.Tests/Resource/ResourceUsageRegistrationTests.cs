// 三种资源用量都真的建得起来/ All three resource usages really build their rows.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Resource;

/// <summary>
/// **三种资源用量都真的建得起来** / All three resource usages really build their rows.
/// </summary>
/// <remarks>
/// `BunchSchedulingConnectionResourceUsage`、`BunchSchedulingExecutionResourceUsage`、`BunchSchedulingStorageResourceUsage`
/// 早就在，但 只把 `CapacitySchedulingResourceUsage` 的注册钉死了。三者各有各的数量公式
/// （连接与执行从"初始量"起，存储是"初始量 + 供应 − 消耗"），**任何一处写空都看不出来**。
///
/// 写判据时又发现一层：**这三种资源在整个仓库里没有一具体实现**（`Resource` 的抽象成员没人实现过），
/// 于是它们至今不可构造。这里给测试用的三个最小具体资源，**"能造出来"本身就是第一条判据**。
///
/// All three existed already, but only pinned the registration of `CapacitySchedulingResourceUsage`. Each has its own quantity formula (connection and execution start from an initial
/// quantity, storage from initial + supply - consumption), so **an empty registration in any one of them would be invisible**. Writing the criteria surfaced another layer: **these three
/// resource kinds have no concrete implementation anywhere in the repository** — nobody implemented `Resource`'s abstract members — so they could not be constructed at all. Three minimal
/// concrete resources are provided here, and **"it can be constructed" is itself the first criterion**.
/// </remarks>
public class ResourceUsageRegistrationTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeWindow<Flt64> Window() => new(new TimeRange(Origin, Origin.AddDays(1)), false, TimeSpan.FromHours(8));

    private static List<TestUsageExecutor> Executors() => new() { new TestUsageExecutor("E1", "E1") };

    private static Flt64 Zero(Flt64 _) => Flt64.Zero;

    private static TestStorageResource Storage() => new("S1", "S1", new Flt64(100.0));

    private static TestConnectionResource Connection() => new("C1", "C1", new Flt64(10.0));

    private static TestExecutionResource Execution() => new("X1", "X1", new Flt64(4.0));

    private static BunchSchedulingStorageResourceUsage<TestUsageExecutor, TestStorageResource, ResourceCapacity<Flt64>, Flt64> StorageUsage() =>
        new(Window(), Executors(), new List<TestStorageResource> { Storage() }, "storage", _ => Zero(Flt64.Zero));

    private static BunchSchedulingConnectionResourceUsage<TestConnectionResource, ResourceCapacity<Flt64>, Flt64> ConnectionUsage() =>
        new(Window(), new List<TestConnectionResource> { Connection() }, "connection");

    private static BunchSchedulingExecutionResourceUsage<TestExecutionResource, ResourceCapacity<Flt64>, Flt64> ExecutionUsage() =>
        new(Window(), new List<TestExecutionResource> { Execution() }, "execution");

    /// <summary>
    /// **三种用量各建一条数量行，名字带配置时给的名称** / Each of the three builds one quantity row whose name carries its configured name.
    /// </summary>
    /// <remarks>
    /// 四点成对：三种都注册成功 / 三张数量表都非空 / 模型里各自出现以配置名开头的行 / **成品资源真的造得出来**。
    /// 只测"返回 Ok"会让第一版那种空 `Ok` 通过——必须看模型里有没有东西。
    ///
    /// Four paired points: each registers successfully; all three quantity tables are non-empty; rows starting with each configured name really appear in the model; and **the concrete
    /// resources can really be constructed**. Checking only "returns Ok" would let the original empty `Ok` pass — the model has to be inspected.
    /// </remarks>
    [Fact]
    public void Register_ShouldBuildAQuantityRowPerUsage() {
        using var model = new LinearMetaModel<Flt64>();
        BunchSchedulingStorageResourceUsage<TestUsageExecutor, TestStorageResource, ResourceCapacity<Flt64>, Flt64> storage = StorageUsage();
        BunchSchedulingConnectionResourceUsage<TestConnectionResource, ResourceCapacity<Flt64>, Flt64> connection = ConnectionUsage();
        BunchSchedulingExecutionResourceUsage<TestExecutionResource, ResourceCapacity<Flt64>, Flt64> execution = ExecutionUsage();

        storage.Register(model).IsOk.Should().BeTrue();
        connection.Register(model).IsOk.Should().BeTrue();
        execution.Register(model).IsOk.Should().BeTrue();

        storage.Quantity.Should().NotBeNull("storage must build its quantity table");
        connection.Quantity.Should().NotBeNull("connection must build its quantity table");
        execution.Quantity.Should().NotBeNull("execution must build its quantity table");

        string joined = string.Join(" | ", Names(model));
        joined.Should().Contain("storage_quantity_");
        joined.Should().Contain("connection_quantity_");
        joined.Should().Contain("execution_quantity_");
    }

    /// <summary>
    /// **重注册不重复建行** / Re-registration does not duplicate rows.
    /// </summary>
    /// <remarks>
    /// 两点成对：注册两次之后符号数不变 / 数量表还是原来那一份。
    /// **每次重建会让同一份数量在模型里出现两个符号**，求解时它们各自取值、彼此无关——在解上表现为"数量对不上"。
    ///
    /// Two paired points: registering twice leaves the symbol count unchanged; the quantity table is the same one. **Rebuilding every time would put two symbols for the same quantity into
    /// the model**, each taking its own value at solve time — showing up as "the numbers do not add up".
    /// </remarks>
    [Fact]
    public void Register_ShouldBeIdempotent() {
        using var model = new LinearMetaModel<Flt64>();
        BunchSchedulingExecutionResourceUsage<TestExecutionResource, ResourceCapacity<Flt64>, Flt64> execution = ExecutionUsage();

        execution.Register(model).IsOk.Should().BeTrue();
        int afterFirst = model.Tokens.Symbols.Count;
        object? table = execution.Quantity;
        execution.Register(model).IsOk.Should().BeTrue();

        model.Tokens.Symbols.Count.Should().Be(afterFirst, "no symbol may be added twice");
        execution.Quantity.Should().BeSameAs(table, "the table must not be rebuilt");
    }

    /// <summary>
    /// **换一个模型要重建，不能沿用旧符号** / A different model must be rebuilt, not reused.
    /// </summary>
    /// <remarks>
    /// 四点成对：注册进 `A` 成功 / 再注册进 `B` 也成功（**同一个用量对象换模型不该失败**）/ **数量表是重建的**（不是把 `A` 里那张挪过去）/ `B` 里确实有数量行。
    /// 只测同模型幂等会让"沿用旧符号"的实现通过——那些符号根本不在 `B` 里，求解时会表现为"数量不在模型里"。
    ///
    /// Four paired points: registering into `A` succeeds; the same usage object also registers into `B` (**switching models must not fail**); **the table is rebuilt** (not the one from
    /// `A` moved over); and `B` really holds the quantity rows. Pinning only same-model idempotency would let "reuse the old symbols" pass — those symbols are not in `B`, showing up at solve time as "the quantity is not in the
    /// model".
    /// </remarks>
    [Fact]
    public void Register_ShouldRebuildForADifferentModel() {
        using var first = new LinearMetaModel<Flt64>();
        using var second = new LinearMetaModel<Flt64>();
        BunchSchedulingExecutionResourceUsage<TestExecutionResource, ResourceCapacity<Flt64>, Flt64> execution = ExecutionUsage();

        execution.Register(first).IsOk.Should().BeTrue();
        object? firstTable = execution.Quantity;
        execution.Register(second).IsOk.Should().BeTrue("the same usage object must be able to register into another model");

        execution.Quantity.Should().NotBeSameAs(firstTable,
            "a different model needs a rebuilt symbol table, not the one already registered elsewhere");
        string joined = string.Join(" | ", Names(second));
        joined.Should().Contain("execution_quantity_", "the rebuilt symbols must be in the second model");
    }

    /// <summary>
    /// **非法入参显式失败** / An invalid argument fails explicitly.
    /// </summary>
    /// <remarks>
    /// 两点成对：`new object` → `Failed` / 模型里的符号数不变。**返回 `Ok` 会让调用方以为资源用量已经进模型**，而它根本没进去。
    ///
    /// Two paired points: `new object` yields `Failed`; the symbol count is unchanged. **Returning `Ok` would let the caller believe the usage reached the model** when it never did.
    /// </remarks>
    [Fact]
    public void Register_ShouldFailOnANonModel() {
        using var model = new LinearMetaModel<Flt64>();
        BunchSchedulingConnectionResourceUsage<TestConnectionResource, ResourceCapacity<Flt64>, Flt64> connection = ConnectionUsage();
        int before = model.Tokens.Symbols.Count;

        Result<Success, ErrorCode, Error<ErrorCode>> result = connection.Register(new object());

        result.IsFailed.Should().BeTrue();
        model.Tokens.Symbols.Count.Should().Be(before);
    }

    private static List<string> Names(LinearMetaModel<Flt64> model) {
        var names = new List<string>();
        foreach (Fuookami.Ospf.Math.Symbol.ISymbol symbol in model.Tokens.Symbols) {
            if (symbol.Name is { } name) {
                names.Add(name);
            }
        }
        return names;
    }
}

/// <summary>测试用执行器 / A test executor.</summary>
/// <param name="Id">标识 / The id.</param>
/// <param name="Name">名称 / The name.</param>
public class TestUsageExecutor(string Id, string Name) : Executor(Id, Name) { }

/// <summary>测试用的时间原点 / The origin used by the fixtures.</summary>
internal static class FixtureClock {
    /// <summary>2024-01-01 00:00 UTC / 2024-01-01 00:00 UTC.</summary>
    public static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

/// <summary>测试用存储资源 / A test storage resource.</summary>
/// <param name="id">标识 / The id.</param>
/// <param name="name">名称 / The name.</param>
/// <param name="initial">初始量 / The initial quantity.</param>
public sealed class TestStorageResource(string id, string name, Flt64 initial)
    : StorageResource<ResourceCapacity<Flt64>, Flt64> {
    private readonly List<ResourceCapacity<Flt64>> _capacities = new() {
        new(new TimeRange(FixtureClock.Origin, FixtureClock.Origin.AddDays(1)), initial, initial),
    };

    /// <inheritdoc/>
    public override string Id => id;

    /// <inheritdoc/>
    public override string Name => name;

    /// <inheritdoc/>
    public override IReadOnlyList<ResourceCapacity<Flt64>> Capacities => _capacities;

    /// <inheritdoc/>
    public override Flt64 InitialQuantityValue => initial;

    /// <inheritdoc/>
    public override Flt64 UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time) => Flt64.Zero;

    /// <inheritdoc/>
    public override Flt64 CostBy<T, E, A>(T task, TimeSpan time) => Flt64.Zero;

    /// <inheritdoc/>
    public override Flt64 SupplyBy<T, E, A>(T task, TimeSpan time) => Flt64.Zero;
}

/// <summary>测试用连接资源 / A test connection resource.</summary>
/// <param name="id">标识 / The id.</param>
/// <param name="name">名称 / The name.</param>
/// <param name="initial">初始量 / The initial quantity.</param>
public sealed class TestConnectionResource(string id, string name, Flt64 initial)
    : ConnectionResource<ResourceCapacity<Flt64>, Flt64> {
    private readonly List<ResourceCapacity<Flt64>> _capacities = new() {
        new(new TimeRange(FixtureClock.Origin, FixtureClock.Origin.AddDays(1)), initial, initial),
    };

    /// <inheritdoc/>
    public override string Id => id;

    /// <inheritdoc/>
    public override string Name => name;

    /// <inheritdoc/>
    public override IReadOnlyList<ResourceCapacity<Flt64>> Capacities => _capacities;

    /// <inheritdoc/>
    public override Flt64 InitialQuantityValue => initial;

    /// <inheritdoc/>
    public override Flt64 UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time) => Flt64.Zero;

    /// <inheritdoc/>
    // 基类写的是 `T?`，但重写处不能重新声明那套约束，只能去掉注解——**可空性警告在这里是必然的，不是缺陷**。
    // The base declares `T?`, while an override cannot restate the constraints, so the annotation goes away — **the nullability warning is unavoidable here, not a defect**.
#pragma warning disable CS8765
    public override Flt64 UsedBy<T, E, A>(T prevTask, T task, TimeRange time) => Flt64.Zero;
#pragma warning restore CS8765
}

/// <summary>测试用执行资源 / A test execution resource.</summary>
/// <param name="id">标识 / The id.</param>
/// <param name="name">名称 / The name.</param>
/// <param name="initial">初始量 / The initial quantity.</param>
public sealed class TestExecutionResource(string id, string name, Flt64 initial)
    : ExecutionResource<ResourceCapacity<Flt64>, Flt64> {
    private readonly List<ResourceCapacity<Flt64>> _capacities = new() {
        new(new TimeRange(FixtureClock.Origin, FixtureClock.Origin.AddDays(1)), initial, initial),
    };

    /// <inheritdoc/>
    public override string Id => id;

    /// <inheritdoc/>
    public override string Name => name;

    /// <inheritdoc/>
    public override IReadOnlyList<ResourceCapacity<Flt64>> Capacities => _capacities;

    /// <inheritdoc/>
    public override Flt64 InitialQuantityValue => initial;

    /// <inheritdoc/>
    public override Flt64 UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time) => Flt64.Zero;

    /// <inheritdoc/>
    public override Flt64 UsedBy<E, A>(IAbstractTask<E, A> task, TimeRange time) => Flt64.Zero;
}
