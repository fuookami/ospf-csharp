// 容量调度的资源用量：显式失败而不是静默 `Ok`/ Capacity scheduling's resource usage: an explicit failure, not a silent `Ok`.

using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Resource;

/// <summary>
/// **容量调度的资源用量：显式失败而不是静默 `Ok`** / Capacity scheduling's resource usage: an explicit failure, not a silent `Ok`.
/// </summary>
/// <remarks>
/// `register` 只做一件事：把子类填好的 `quantity` 表加进模型。那张表的内容是
/// `Σ_action usedBy(action, slot.time) × x(action, slot)`——**要决策变量**。
///
/// C# 侧还没有那个变量：`IterativeCapacityCompilation.LocateColumnDecision` 返回的是 `new object` 占位，不是符号。
/// 没有变量就没法填表；返回 `Ok` 等于告诉每个调用方"已经接好了"，而表里什么都没有、容量永远不紧、模型照样可解。
///
/// Its `register` does one thing: add the `quantity` table its subclass already filled, and that table holds
/// `sum over actions of usedBy(action, slot.time) times x(action, slot)` — **it needs decision variables**. The C# side does not have them:
/// `IterativeCapacityCompilation.LocateColumnDecision` returns a `new object` placeholder rather than a symbol. Without variables the table cannot be filled, and returning `Ok` would tell every
/// caller it is wired while the table holds nothing, capacity is never tight and the model stays solvable.
/// </remarks>
public class CapacitySchedulingResourceUsageRegisterTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class TestResource : Resource<ResourceCapacity<Flt64>, Flt64>, ICapacityActionResource<ResourceCapacity<Flt64>, Flt64> {
        public TestResource(IReadOnlyList<ResourceCapacity<Flt64>> capacities) {
            Capacities = capacities;
        }

        public override string Id => "R1";

        public override string Name => "R1";

        public override IReadOnlyList<ResourceCapacity<Flt64>> Capacities { get; }

        public override Flt64 InitialQuantityValue => Flt64.Zero;

        public override Flt64 UsedQuantity<T, E, A>(AbstractTaskBunch<T, E, A> bunch, TimeRange time) => Flt64.Zero;

        public Flt64 UsedBy(object action, TimeRange time) => new(2.0);
    }

    private static List<ResourceCapacity<Flt64>> Capacities() => new() {
        new(new TimeRange(Origin, Origin.AddHours(8)), new Flt64(0.0), new Flt64(10.0),
            null, null, TimeSpan.FromHours(8)),
    };

    private static TimeWindow<Flt64> Window() =>
        new(new TimeRange(Origin, Origin.AddHours(8)), true, TimeSpan.FromHours(8));

    /// <summary>
    /// **两个容量调度实现都失败，且模型里一行都没留** / Both capacity-scheduling implementations fail and leave nothing in the model.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. `BunchCapacitySchedulingResourceUsage.Register` → **`Failed`**，不是 `Ok`：没有决策变量就没法填表，静默成功会掩盖整条缺口。
    /// 2. `PlanCapacitySchedulingResourceUsage.Register` 同样失败——两个实现走的是同一条缺口，只堵一个等于没堵。
    /// 3. 两次调用之后模型里**一个符号、一条约束都没有**——失败不能留下半成品。
    ///
    /// Three paired points: `BunchCapacitySchedulingResourceUsage.Register` is **`Failed`** rather than `Ok`, since without decision variables the table cannot be filled and a silent success
    /// hides the whole gap; `PlanCapacitySchedulingResourceUsage.Register` fails too — both implementations share the one gap, and plugging only one plugs nothing; and after both calls the
    /// model holds **no symbol and no row**, because a failure must not leave half-built state behind.
    /// </remarks>
    [Fact]
    public void Register_ShouldFailInsteadOfPretending() {
        using var model = new LinearMetaModel<Flt64>();
        var resources = new List<TestResource> { new(Capacities()) };

        var bunch = new BunchCapacitySchedulingResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64>(
            Window(), resources);
        bunch.Register(model).IsFailed.Should().BeTrue(
            "a table that cannot be filled must not report success");

        var plan = new PlanCapacitySchedulingResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64>(
            Window(), resources);
        plan.Register(model).IsFailed.Should().BeTrue(
            "both implementations share the same gap");

        model.Tokens.Symbols.Should().BeEmpty("a failure must not leave half-built state behind");
        ((Fuookami.Ospf.Core.Model.Mechanism.IMetaModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    /// <summary>
    /// **失败信息说得出原因** / The failure says why.
    /// </summary>
    /// <remarks>
    /// **起消息改口径**：决策变量本身已经存在（ 的 `CapacityDecisionVariables`、 的
    /// `CapacityOrderVariables`），旧消息却写着"决策变量尚未接入"——**一句过期的实话比一句含混的话更糟**，
    /// 它会让下一个读代码的人去找一个早就建好的东西。
    ///
    /// 现在钉两点：错误码仍是 `ApplicationError`（缺的是上层输入）/ 消息说的是**动作时隙与资源时隙的重叠换算**
    /// 这条映射没实现，而**不再**声称决策变量缺失。
    ///
    /// **changes the wording**: the decision variables themselves exist (`CapacityDecisionVariables` from，`CapacityOrderVariables` from ) while the old message claimed they
 /// were not wired — **a stale true-sounding sentence is worse than a vague one**, because it sends the next reader hunting for something already built. Two points are pinned: the implementation
    /// stays `ApplicationError` (an upstream input is missing) and the message names the unimplemented **overlap conversion between action slots and resource slots** while **no longer**
    /// claiming the decision variables are absent.
    /// </remarks>
    [Fact]
    public void Register_ShouldSayWhyItFailed() {
        using var model = new LinearMetaModel<Flt64>();
        var usage = new BunchCapacitySchedulingResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64>(
            Window(), new List<TestResource> { new(Capacities()) });

        Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> result =
            usage.Register(model);

        result.IsFailed.Should().BeTrue();
        // 直接就地转型，不落中间变量：分析器对这个转换的 `var` \/ 显式类型两边都不满意，而两种写法本身都不重要。
        // Cast in place with no intermediate variable: the analyzer is unhappy with both `var` and the explicit type here, and neither spelling matters.
        ((Fuookami.Ospf.Utils.Functional.Failed<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)result)
            .Error.Code.Should().Be(Fuookami.Ospf.Utils.Error.ErrorCode.ApplicationError);
        ((Fuookami.Ospf.Utils.Functional.Failed<Fuookami.Ospf.Utils.Functional.Success,
            Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>)result)
            .Error.Message.Should().Contain("重叠时隙", "the message must name the overlap between action and resource slots")
            .And.NotContain("决策变量尚未接入", "a stale claim would send the reader after something already built")
            .And.NotContain("decision variables are not wired", "the English half must be corrected too");
    }

    /// <summary>
    /// **给了决策变量就真的填表** / With decision variables the table is really filled.
    /// </summary>
    /// <remarks>
    /// 四点成对：注册 → `Ok`/ 表里每个资源时隙一个符号 /
    /// 符号真进了模型 / 单项式数量等于 `动作数 × 与该时隙重叠的动作时隙数`——重叠换算是这里的核心，算错了这里就红。
    ///
    /// Four paired points: registration yields `Ok` (the `Failed` from now only happens when the variables are missing); the table holds one symbol per resource slot; the symbols
    /// really entered the model; and the monomial count equals `actions x overlapping action slots` — the overlap conversion is's core, and a wrong one turns this red.
    /// </remarks>
    [Fact]
    public void Register_ShouldFillTheTableWhenAmountsAreGiven() {
        using var model = new LinearMetaModel<Flt64>();
        var actions = new List<FakeAction> { new("A1"), new("A2") };
        var actionSlots = new List<TimeRange> {
            new(Origin, Origin.AddHours(4)),
            new(Origin.AddHours(4), Origin.AddHours(8)),
        };
        using var amounts = new LinearMetaModel<Flt64>();
        var source = new FakeAmountSource(actions, actionSlots, amounts);
        var usage = new BunchCapacitySchedulingResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64>(
            Window(), new List<TestResource> { new(Capacities()) }, source);

        usage.Register(model).IsOk.Should().BeTrue();
        usage.QuantityTable.Should().NotBeNull();
        usage.QuantityTable!.Should().HaveCount(usage.TimeSlots.Count, "one quantity symbol per resource slot");
        model.Tokens.Symbols.Should().NotBeEmpty("the quantity symbols must enter the model");

        // 时间窗 8 小时、容量时隙 8 小时 → 资源时隙只有一个，它与两个动作时隙都重叠 → 2 个动作 × 2 个动作时隙 = 4 项。
        // An 8-hour window with an 8-hour capacity gives one resource slot that overlaps both action slots: 2 actions x 2 action slots = 4 terms.
        IReadOnlyList<double> coefficients = usage.QuantityTable!.Values
            .SelectMany(symbol => symbol.Polynomial!.Monomials)
            .Select(monomial => monomial.Coefficient.ToDouble())
            .ToList();
        coefficients.Should().HaveCount(actions.Count * actionSlots.Count);
        coefficients.Should().OnlyContain(value => value == 2.0, "every action uses 2 units per action slot");
    }

    /// <summary>**不重叠的动作时隙不产生项** / Action slots that do not overlap produce no term.</summary>
    /// <remarks>
    /// 两点成对：把动作时隙挪到时间窗之外后，项数变成 0 / 注册仍然成功——**空表不等于失败**，没有占用就是没有项。
    ///
    /// Two paired points: moving the action slots outside the window drops the term count to 0; and registration still succeeds — **an empty table is not a failure**; no occupancy means
    /// no terms.
    /// </remarks>
    [Fact]
    public void Register_ShouldSkipActionSlotsOutsideTheResourceSlot() {
        using var model = new LinearMetaModel<Flt64>();
        var actions = new List<FakeAction> { new("A1") };
        var farAway = new List<TimeRange> {
            new(Origin.AddDays(3), Origin.AddDays(3).AddHours(4)),
        };
        using var amounts = new LinearMetaModel<Flt64>();
        var usage = new BunchCapacitySchedulingResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64>(
            Window(), new List<TestResource> { new(Capacities()) }, new FakeAmountSource(actions, farAway, amounts));

        usage.Register(model).IsOk.Should().BeTrue();
        usage.QuantityTable!.Values
            .SelectMany(symbol => symbol.Polynomial!.Monomials)
            .Should().BeEmpty("a slot three days away cannot occupy an 8-hour window");
    }

    /// <summary>测试用的动作 / A test action.</summary>
    /// <param name="Id">标识 / The identifier.</param>
    private sealed record FakeAction(string Id) : IProductionAction {
        public string Name => Id;

        public Executor Executor => new Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation.TestExecutor("E1", "E1");

        public bool Discrete => true;

        public Flt64 UnitCost(DateTimeOffset time, Func<double, Flt64> fromDouble) => Flt64.Zero;

        public ulong UpperBound(TimeRange slot, Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeWindow<Flt64> timeWindow) => 9UL;

        public Flt64 UnitCapacity(Fuookami.Ospf.Framework.GanttScheduling.Infrastructure.TimeWindow<Flt64> timeWindow) => Flt64.One;
    }

    /// <summary>测试用的决策变量视图 / A test decision-variable view.</summary>
    private sealed class FakeAmountSource : ICapacityAmountSource, IDisposable {
        private readonly LinearMetaModel<Flt64> _model;

        public FakeAmountSource(
            IReadOnlyList<IProductionAction> actions,
            IReadOnlyList<TimeRange> actionSlots,
            LinearMetaModel<Flt64> model) {
            Actions = actions;
            ActionSlots = actionSlots;
            _model = model;
            IsRegistered = true;
            var symbols = new List<ILinearIntermediateSymbol<Flt64>>();
            foreach (IProductionAction action in actions) {
                for (int index = 0; index < actionSlots.Count; ++index) {
                    var symbol = new Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol(
                        new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                            constant: Flt64.Zero),
                        name: $"{action.Id}_{index}");
                    _model.Add(symbol).IsOk.Should().BeTrue();
                    symbols.Add(symbol);
                }
            }
            Symbols = symbols;
        }

        public IReadOnlyList<TimeRange> ActionSlots { get; }

        public IReadOnlyList<IProductionAction> Actions { get; }

        public bool IsRegistered { get; }

        public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> Symbols { get; }

        public ISymbol AmountOf(IProductionAction action, int slotIndex) {
            int actionIndex = Actions.ToList().IndexOf(action);
            return Symbols[actionIndex * ActionSlots.Count + slotIndex];
        }

        public void Dispose() => _model.Dispose();
    }

    /// <summary>
    /// **切时隙只有一份实现** / Slot cutting has exactly one implementation.
    /// </summary>
    /// <remarks>
    /// 三点成对：`BuildTimeSlots` 在基类里出现 / 两个子类里**都不出现**/
    /// 两个子类仍各自存在且名字不同（对应两个类，不能合并成一个）。
    ///
    /// Three paired points: `BuildTimeSlots` appears in the base class; it appears in **neither** subclass (before the two were byte-identical, and one edit forgetting the other was
    /// certain); and both subclasses still exist with different names (they mirror two distinct roles and must not collapse into one).
    /// </remarks>
    [Fact]
    public void SlotCutting_ShouldLiveInOnePlace() {
        string root = RepositoryRoot();
        string directory = Path.Combine(root, "src", "Framework.GanttScheduling", "Domain", "Resource", "Model");
        string baseFile = Path.Combine(directory, "CapacitySchedulingResourceUsage.cs");
        string bunch = Path.Combine(directory, "BunchCapacitySchedulingResourceUsage.cs");
        string plan = Path.Combine(directory, "PlanCapacitySchedulingResourceUsage.cs");

        File.ReadAllText(baseFile).Should().Contain("BuildTimeSlots(");
        File.ReadAllText(bunch).Should().NotContain("BuildTimeSlots(", "a second copy is one edit away from diverging");
        File.ReadAllText(plan).Should().NotContain("BuildTimeSlots(", "a second copy is one edit away from diverging");
        File.ReadAllText(bunch).Should().Contain("bunch_capacity_scheduling_resource");
        File.ReadAllText(plan).Should().Contain("plan_capacity_scheduling_resource");
    }

    /// <summary>仓库根目录 / The repository root.</summary>
    /// <returns>绝对路径 / The absolute path.</returns>
    private static string RepositoryRoot() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            if (Directory.Exists(Path.Combine(directory, "src")) && File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("repository root not found");
    }
}
