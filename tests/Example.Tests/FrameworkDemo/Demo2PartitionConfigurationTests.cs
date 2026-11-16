using System;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
// 分区配置的判据 / Criteria for the partition configuration.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.PartitionLoading;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **分区配置那一半必须自己站得住** / The partition-configuration half must stand on its own.
/// </summary>
/// <remarks>
/// 起 `partitionColumnGeneration` 在 demo2 里被**显式拒绝**（`NotYetPorted`），理由是"注册与分区配置没接起来"。
/// 注册那一半由 `DiscreteLoadingContext.Register` 提供；**这一半（`PartitionConfiguration`）是 补的**。
/// 它是纯函数，所以**不需要求解器**就能钉住——而"整条 CG 链能跑"仍然需要真实后端，那个缺口不因为本文件而消失。
///
/// 判据刻意分开写，不合并成一个"跑通了"：**五条各自能单独红**，红了就知道是哪一条规矩破了。
///
/// Since `partitionColumnGeneration` is **explicitly refused** in demo2 (`NotYetPorted`) because "the registration and the partition configuration are not wired".
/// The registration half comes from `DiscreteLoadingContext.Register`; **this half (`PartitionConfiguration`) is what adds**. Being a pure function it can be pinned
/// **without a solver** — while "the whole CG chain runs" still needs a real backend, and this file does not make that gap go away. The criteria are deliberately split
/// rather than merged into one "it works": **each of the five can red on its own**, and a red one names which rule broke.
/// </remarks>
public class Demo2PartitionConfigurationTests
{
    /// <summary>**分区按 `partitionId` 分组，且顺序按首次出现** / Partitions group by `partitionId`, in first-appearance order.</summary>
    /// <remarks>
    /// **`AFT` 在字母序上先于 `FORWARD`**，所以这条判据同时钉住"不是按字典序排的"。分区号会进 `partition_columns=` 备注与日志，
    /// 换个顺序就是换一份可读性——那是个不该被顺手改掉的约定。
    ///
    /// **`AFT` sorts before `FORWARD` alphabetically**, so this also pins "not sorted". Partition ids reach the `partition_columns=` note and the log, and a different
    /// order is a different reading — an agreement that should not be changed in passing.
    /// </remarks>
    [Fact]
    public void Partitions_ShouldGroupByPartitionIdKeepingFirstAppearanceOrder() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with { Name = "A1", PartitionId = "FORWARD", LoadingOrder = 0 },
                sample.Positions[1] with { Name = "B1", PartitionId = "AFT", LoadingOrder = 1 },
                sample.Positions[1] with { Name = "A2", PartitionId = "FORWARD", LoadingOrder = 2 },
            ],
        };

        Result<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>> built = Build(request);
        built.IsOk.Should().BeTrue(built.ToString());

        IReadOnlyList<LoadingPartition> partitions = Ok(built).Partitions;
        partitions.Select(partition => partition.Id).Should().Equal("FORWARD", "AFT");
        partitions[0].PositionIds.Should().BeEquivalentTo("A1", "A2");
        partitions[1].PositionIds.Should().BeEquivalentTo("B1");
    }

    /// <summary>**没有分区归属的舱位必须结构化失败并点名** / A position without a partition must fail structurally, naming it.</summary>
    /// <remarks>
    /// `owners.getValue(position.spaceName)` 在这里会抛 `NoSuchElementException`。**抛异常不是契约**，而且它连"是哪个舱位"都不说。
    ///
    /// **这条只能直接调被测函数**：`Aircraft.AggregationInitializer` 已经把空分区号并进"物理空间或装载顺序无效"这条合并消息
    /// 。所以这里用**已装配好的聚合**配**故意改坏的请求**——被测函数自己那一层的守卫仍要单独成立。
    ///
    /// `owners.getValue(position.spaceName)` throws `NoSuchElementException` when the key is missing. **Throwing is not a contract**, and it does not even say which
    /// position is at fault. **This one has to call the subject directly**: the aircraft initialiser already folds a blank partition id into its merged "invalid physical
    /// space or loading order" message (a trade-off, not changed for this criterion), so an **already assembled aggregation** is paired with a **deliberately broken
    /// request** — the subject's own guard must still hold on its own.
    /// </remarks>
    [Fact]
    public void ABlankPartitionId_ShouldFailAndNameThePosition() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        StowageAggregation aggregation = Aggregation(sample);
        RequestDTO request = sample with {
            Positions = [sample.Positions[0] with { PartitionId = "  " }],
        };

        Result<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>> built =
            PartitionConfiguration.Build(request, aggregation);
        built.IsFailed.Should().BeTrue("a blank partition id is a contradiction in the request");

        var failed = (Failed<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>>)built;
        failed.Error.Code.Should().Be(ErrorCode.IllegalArgument);
        failed.Error.Message.Should().Contain("P1", "an unnamed diagnostic is no diagnostic");
    }

    /// <summary>**一个舱位都没有的请求必须拒** / A request with no positions at all must be refused.</summary>
    [Fact]
    public void ARequestWithoutPositions_ShouldBeRefused() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        StowageAggregation aggregation = Aggregation(sample);
        RequestDTO request = sample with { Positions = [] };

        Result<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>> built =
            PartitionConfiguration.Build(request, aggregation);
        built.IsFailed.Should().BeTrue("an empty master problem is not a column-generation input");
    }

    /// <summary>**聚合里的舱位不在请求分区里必须拒** / An aggregated position outside every requested partition must be refused.</summary>
    /// <remarks>
    /// 这条守的是**两层数据的一致性**：`aggregation.Positions` 与 `input.Positions` 本应同源，一旦不同源，
    /// 交下去的局部变量会挂到一个不存在的主问题列上——**比空表更坏，因为它在求解器里才炸**。
    ///
    /// **必须用 `Predistribution` 模式**：`PositionStatus.From` 只在"可用 / 要装载决策 / 主舱 / 模式匹配"四条同时成立时才给重量标志，
    /// 而 `FullLoad` 一个都不给——用默认模式跑，这个守卫**根本没机会执行**，判据会绿得毫无意义（第一版就是这样）。
    ///
    /// This guards **consistency between the two layers**: `aggregation.Positions` and `input.Positions` should share a source, and once they do not, the local variables
    /// handed downstream attach to a master column that does not exist — **worse than an empty list, because it explodes inside the solver**. **It has to run under
    /// `Predistribution`**: `PositionStatus.From` only sets the weight flags when "available / decision needed / main deck / matching mode" all hold, and `FullLoad` sets
    /// none — under the default mode this guard **never executes** and the criterion passes meaninglessly (which is exactly what the first version did).
    /// </remarks>
    [Fact]
    public void APositionOutsideEveryRequestedPartition_ShouldBeRefused() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        StowageAggregation aggregation = Aggregation(sample, StowageMode.Predistribution);
        RequestDTO request = sample with { Positions = [sample.Positions[0]] };

        Result<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>> built =
            PartitionConfiguration.Build(request, aggregation);
        built.IsFailed.Should().BeTrue("P2 is in the aggregation but not in any requested partition");

        ((Failed<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>>)built).Error.Message
            .Should().Contain("P2", "the diagnostic must name the position that is out of place");
    }

    /// <summary>**`Predistribution` 下两个舱位都拿到局部变量，且编号带分区归属** / Under `Predistribution` both positions get a local variable, labelled with their partition.</summary>
    [Fact]
    public void PredistributionDecisions_ShouldCarryThePartitionOfTheirPosition() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();

        Result<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>> built =
            PartitionConfiguration.Build(WithPredicateSpec(request), Aggregation(request, StowageMode.Predistribution));
        built.IsOk.Should().BeTrue(built.ToString());

        IReadOnlyList<PartitionDecision> decisions = Ok(built).Decisions;
        decisions.Should().NotBeEmpty(
            "under Predistribution every available main-deck position needs a predicate weight; an empty list here means the flags never fired");
        decisions.Select(decision => decision.Id)
            .Should().Contain("estimated:P1").And.Contain("estimated:P2");
        foreach (PartitionDecision decision in decisions) {
            decision.PartitionId.Should().Be("DEFAULT", "the sample request leaves every position in the default partition");
        }
    }

    /// <summary>**局部变量只给需要重量的舱位，编号带前缀** / Decisions only cover weight-needing positions and carry their prefixes.</summary>
    [Fact]
    public void Decisions_ShouldOnlyCoverThePositionsThatNeedAWeight() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();

        Result<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>> built = Build(request);
        built.IsOk.Should().BeTrue(built.ToString());

        IReadOnlyList<PartitionDecision> decisions = Ok(built).Decisions;
        // 没有舱位需要重量时**必须是空表，而不是 null 或抛**：空表是一个答案，`null` 让调用方去猜。
        // When no position needs a weight the result **must be an empty list, not null and not a throw**: an empty list is an answer, `null` makes the caller guess.
        decisions.Should().NotBeNull();
        foreach (PartitionDecision decision in decisions) {
            decision.Id.Should().MatchRegex("^(estimated|recommended):");
            decision.PartitionId.Should().NotBeNullOrWhiteSpace();
            decision.Variable.Should().NotBeNull();
        }
    }

    /// <summary>走生产同一条上下文链装配 / Assemble through the same context chain production uses.</summary>
    /// <param name="request">请求 / The request.</param>
    /// <returns>已装配的聚合 / The assembled aggregation.</returns>
    /// <remarks>
    /// **不自己 `new` 聚合**：那条路要手搓 `Flight`/`Formula`/`Fuselage`/`Fuel` 等十来个对象，而真正要验的是"请求 → 分区"这一段。
    /// 走 `AircraftContext` + `StowageContext` 与生产完全同路，顺带保证这条判据不会因为装配方式不同而与真实调用分叉。
    ///
    /// **No hand-built aggregation**: that path needs a dozen objects (`Flight`, `Formula`, `Fuselage`, `Fuel`,...) while what is under test is "request to partitions".
    /// Going through `AircraftContext` plus `StowageContext` is the production route, which also keeps this criterion from diverging from real calls.
    /// </remarks>
    /// <summary>**给每个舱位补一份谓词装载重量规格** / Give every position a predicate load-weight specification.</summary>
    /// <param name="request">请求 / The request.</param>
    /// <returns>补齐后的请求 / The request with the spec filled in.</returns>
    /// <remarks>
    /// `Predistribution` 的配载域注册**要求**这份规格：没有它，
    /// `PositionStatus.PredicateWeightNeeded` 为真而对应的重量规格为空，注册只能失败。
    ///
    /// The `Predistribution` stowage registration **requires** this specification (recorded as a gap, flipped): without it
    /// `PositionStatus.PredicateWeightNeeded` is true while the matching weight spec is absent, and registration can only fail.
    /// </remarks>
    private static RequestDTO WithPredicateSpec(RequestDTO request) {
        List<PositionInput> positions = [];
        foreach (PositionInput position in request.Positions) {
            positions.Add(position with { PredicateLoadWeight = new PositionPredicateLoadWeightInput(10.0, 1.0, 10.0) });
        }
        return request with { Positions = positions };
    }

    private static StowageAggregation Aggregation(RequestDTO request, StowageMode stowageMode = StowageMode.FullLoad) {
        if (stowageMode == StowageMode.Predistribution) {
            request = WithPredicateSpec(request);
        }
        var aircraft = new AircraftContext();
        aircraft.Init(request).IsOk.Should().BeTrue("the sample request must pass the aircraft initialiser");
        var stowage = new StowageContext();
        stowage.Init(aircraft, request, stowageMode).IsOk.Should().BeTrue("the sample request must pass the stowage initialiser");
        StowageAggregation aggregation = stowage.Aggregation!;
        // **`Load.Y`/`Load.Z` 是注册进模型时才建的符号**，装配阶段它们还是 `null`。生产路径也先注册再取局部变量，
        // 所以这里照做——否则判据测的是"还没注册的聚合"，而那从来不是生产会遇到的状态。
        //
        // **`Load.Y`/`Load.Z` are symbols created when registering into a model**, still `null` right after assembly. Production also registers before reading the local
        // variables, so this does the same — otherwise the criterion tests "an aggregation that was never registered", a state production never sees.
        using var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64>();
        aggregation.Register(stowageMode, model).IsOk.Should().BeTrue("the sample aggregation must register into a model");
        return aggregation;
    }

    /// <summary>装配后调用被测函数 / Assemble, then call the subject.</summary>
    /// <param name="request">请求 / The request.</param>
    /// <returns>分区配置或失败 / The partition configuration or a failure.</returns>
    private static Result<PartitionConfigurationResult, ErrorCode, Error<ErrorCode>> Build(RequestDTO request) =>
        PartitionConfiguration.Build(request, Aggregation(request));

    /// <summary>解包成功结果 / Unwrap a successful result.</summary>
    /// <param name="result">结果 / The result.</param>
    /// <returns>值 / The value.</returns>
    private static T Ok<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        ((Ok<T, ErrorCode, Error<ErrorCode>>)result).Value;

    /// <summary>**宽体走二维内核，窄体走一维** / Wide bodies take the 2-D kernel, narrow bodies the 1-D one.</summary>
    /// <remarks>
    /// 之前 `LoadingContextFactory` **恒走一维**，理由写在类注释里（"Bpp2d 没有 `Context` 工厂"），那个理由已经不成立。
    ///
    /// **判据看的是冲突数**，不是"构造成功"：宽体请求的两个舱位在 `Geometry` 里**纵向错开、横向重叠**，
    /// 二维下不冲突；若内核退化成了一维，它们会被判成冲突。**这正是二维与一维唯一的分水岭。**
    ///
    /// Before `LoadingContextFactory` **always** went 1-D for a reason that no longer holds. **The criterion reads the conflict count**, not "it built":
    /// the wide-body request's two positions are **staggered longitudinally and overlapping laterally**, which 2-D calls disjoint — a kernel degraded back to 1-D
    /// would call them conflicting. **That is the one place the two differ.**
    /// </remarks>
    [Fact]
    public void AWideBody_ShouldUseTheTwoDimensionalKernel() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            AircraftType = AircraftTypeInput.B767,
            Positions = [
                // **同一纵向区间、不同横向区间**：一维看 `[Front, Back)` 会判冲突，二维看矩形不会。
                // **Same longitudinal span, different lateral spans**: 1-D sees `[Front, Back)` and calls a conflict, 2-D sees rectangles and does not.
                sample.Positions[0] with {
                    Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0,
                    Geometry = new PositionGeometryInput(0.0, 1.0, 0.0, 1.0),
                },
                sample.Positions[1] with {
                    Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1,
                    Geometry = new PositionGeometryInput(0.0, 1.0, 2.0, 3.0),
                },
            ],
        };

        StowageAggregation aggregation = Aggregation(request);
        aggregation.Stowage.LoadingContext.Should().NotBeNull("the factory must have built a kernel");
        // **判据先钉住前提**：机型真的是请求里那个，否则下面"走二维"的结论无从谈起。
        // **The criterion pins the premise first**: the type really is the requested one, or "took the 2-D path" means nothing (this was always before ).
        aggregation.AircraftModel.Type.Should().Be(AircraftType.B767,
            "the request asked for B767; a hard-coded B737 would silently send every aircraft down the narrow-body path");
        aggregation.Stowage.LoadingContext!.Problem.Conflicts.Should().BeEmpty(
            "the two rectangles share a longitudinal span but not a lateral one; only a 1-D kernel would call that a conflict");
    }

    /// <summary>**窄体仍然走一维** / Narrow bodies still take the 1-D kernel.</summary>
    /// <remarks>
    /// 少了这条，"所有机型都走二维"也能让上一条绿——而宽体走二维、窄体走一维是**本实现的分派**。
    ///
    /// Without this, "every type goes 2-D" would also keep the criterion above green — while wide-in-2-D and narrow-in-1-D is **this implementation's dispatch**.
    /// </remarks>
    [Fact]
    public void ANarrowBody_ShouldKeepTheOneDimensionalKernel() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            AircraftType = AircraftTypeInput.B737,
            Positions = [
                sample.Positions[0] with {
                    Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0,
                    Geometry = new PositionGeometryInput(0.0, 1.0, 0.0, 1.0),
                },
                // **纵向区间也设成与 P1 相同**：一维内核只读 `Geometry.Front/Back`。
                // **The longitudinal span matches P1 too**: the 1-D kernel reads only `Geometry.Front/Back`.
                sample.Positions[1] with {
                    Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1,
                    Geometry = new PositionGeometryInput(0.0, 1.0, 2.0, 3.0),
                },
            ],
        };

        StowageAggregation aggregation = Aggregation(request);
        aggregation.Stowage.LoadingContext.Should().NotBeNull();
        // **一维区间取自 `Geometry.Front/Back`**（`AggregationInitializer` 用它建 `PositionCoordinate`），所以两个舱位共用纵向区间即冲突。
        // **The 1-D interval comes from `Geometry.Front/Back`** (the initialiser builds `PositionCoordinate` from it), so a shared longitudinal span conflicts.
        // **一维几何的 `SpaceId` 是舱位名**（`LoadingContextFactory` 里写的就是 `position.Name`），而 `Conflicts` **按 `SpaceId` 分桶**：
        // 两个舱位的 `SpaceId` 不同，于是**永远配不到一起**。所以一维内核在这条路径上**根本产生不了几何冲突**——
        // 这是既有行为，没有改它，**判据不该假装它会产生**。
        //
        // **The 1-D geometry's `SpaceId` is the position name** (that is what `LoadingContextFactory` writes), and `Conflicts` **buckets by `SpaceId`**: two positions
        // never share a bucket, so **they can never be paired**. The 1-D kernel therefore **produces no geometric conflict at all** on this path — existing behaviour
        // did not change, and **the criterion must not pretend otherwise**.
        // **修好 `SpaceId` 之后，一维路径真的会产生几何冲突了**。判据必须断言它——
        // 断言"空"就等于把修复前的缺陷当成正确行为钉住。
        // **After fixed `SpaceId`, the 1-D path really does produce a geometric conflict**, and the criterion must assert it: asserting "empty" would pin the
        // pre-fix defect as correct behaviour.
        aggregation.Stowage.LoadingContext!.Problem.Conflicts.Should().ContainSingle(
            "both positions sit in the MAIN space with the same longitudinal span, so the 1-D sweep must pair them");
    }

    /// <summary>**允许的指派是三支的并集** / Allowed assignments are the union of the three branches.</summary>
    /// <remarks>
    /// 之前 `LoadingContextFactory` 只问 `StowageNeeded`，而它只对 `Preassigned`/`Optional` 为真。
    /// 于是 `AdjustmentNeeded` 的货物与"已在舱位里"的货物**一个允许指派都拿不到**——稀疏决策不给它们建变量，
    /// **没有变量就等于这件货不存在，而且不报错**。
    ///
    /// **这条判据直接问那两个谓词，不经过请求**：`AggregationInitializer` 把货物状态硬编码成 `ItemStatus.Optional`
    /// ，所以走请求根本到不了 `AdjustmentNeeded` 分支——**走请求的判据在这里会是空的**。
    ///
    /// Before the factory asked only `StowageNeeded`, true solely for `Preassigned`/`Optional`, so `AdjustmentNeeded` items and items already inside a
    /// position **got no allowed assignment**: the sparse decisions created no variable for them, and **no variable reads as "this item does not exist", silently**.
    /// **This criterion asks the two predicates directly, not through a request**: `AggregationInitializer` hard-codes `ItemStatus.Optional` (measured),
    /// so a request-driven criterion could never reach the `AdjustmentNeeded` branch and **would be vacuous here**.
    /// </remarks>
    [Fact]
    public void TheTwoPredicates_ShouldCoverDisjointStatuses() {
        // **前提**：两个谓词覆盖的状态集合**不相交**——这正是"只问一个"会漏掉另一个的原因。
        // **Premise**: the two predicates cover **disjoint** status sets, which is exactly why asking only one drops the other.
        ItemStatuses.StowageNeeded(ItemStatus.AdjustmentNeeded).Should().BeFalse(
            "StowageNeeded only holds for Preassigned/Optional; if it also held for AdjustmentNeeded the union would be redundant");
        ItemStatuses.AdjustmentNeeded(ItemStatus.AdjustmentNeeded).Should().BeTrue(
            "the second branch exists precisely for this status");
        // **并集因此真的比单支大**：这是判据的核心，缺了它上一条就只是在描述实现细节。
        // **The union is therefore strictly larger than one branch**, which is the point; without this the two lines above only describe implementation detail.
        (ItemStatuses.StowageNeeded(ItemStatus.AdjustmentNeeded) || ItemStatuses.AdjustmentNeeded(ItemStatus.AdjustmentNeeded))
            .Should().BeTrue();
    }
    // **单位那条判据被我删掉了，因为它测不出东西。** 我先写了一条"英寸请求与米请求的相交结论必须一致"，
    // 然后把单位改回硬编码的米——**判据照样绿**。原因很直接：请求只有一个 `LengthUnit`，
    // **所有矩形被同一个倍数缩放，而相交关系在统一缩放下不变**。
    //
    // 所以"单位来自请求"这个修复**无法用相交结论钉住**。它仍然是正确的（`input.lengthUnit`，
    // 且硬编码的米会让一个舱位大 39.37 倍），只是**没有一条判据能在这个层次上看见它**。
    // 我宁可留一条明说的空缺，也不留一条"改坏了也不红"的判据——那是 记过的同一个坑。
    //
    // **I deleted the unit criterion because it measures nothing.** I first wrote "the inch request and the metre request must agree on overlap", then reverted
    // the unit to hard-coded metres — **and it stayed green**. The reason is plain: a request carries a single `LengthUnit`, so **every rectangle scales by the same
    // factor, and overlap is invariant under uniform scaling**. "The unit comes from the request" therefore **cannot be pinned by an overlap verdict**. The fix is
    // still right (`input.lengthUnit` is honoured, and hard-coded metres inflate a position 39.37x); it simply **has no criterion that can see it at this
    // level**. I would rather record an honest hole than keep a criterion that **stays green when the code is wrong** — the same trap recorded.

    /// <summary>**已装载的货物必须固定在自己的舱位** / An already-loaded item must stay fixed to its position.</summary>
    /// <remarks>
    /// 之前 `FixedPositionId` **硬编码 `null`**，于是已经装在某个舱位里的货物被当成自由货物，求解器可以把它挪走。
    /// **而"挪走已装载货物"恰恰是调整模式要最小化的东西**——判据看的是装载上下文里的 `FixedPositionId`，不是"构造成功"。
    ///
    /// Before `FixedPositionId` was **hard-coded `null`**, so an item already inside a position counted as free and the solver could move it — **while moving
    /// loaded cargo is exactly what the adjustment mode minimises**. The criterion reads `FixedPositionId` in the loading context, not "it built".
    /// </remarks>
    [Fact]
    public void AnAlreadyLoadedItem_ShouldBeFixedToItsPosition() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with {
                    Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0,
                    // **把第一件货标成已装在 P1**。 / **Mark the first cargo as already loaded in P1.**
                    LoadedItems = [sample.Cargos[0].Name],
                },
                sample.Positions[1] with { Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1 },
            ],
        };

        StowageAggregation aggregation = Aggregation(request);
        aggregation.Stowage.LoadingContext.Should().NotBeNull("the factory must have built a kernel");
        LoadingItem<Flt64> item = aggregation.Stowage.LoadingContext!.Problem.Items
            .Single(candidate => candidate.Id == sample.Cargos[0].Name);
        item.FixedPositionId.Should().Be("P1",
            "a loaded item that is not fixed can be moved, which is the one thing adjustment mode must avoid");
    }

    /// <summary>**一件货不能固定在两个舱位** / One item cannot be fixed to two positions.</summary>
    /// <remarks>
    /// 第二条前置校验。**少了它，`toMap` 会静默只留最后一个**，于是"固定在哪个舱位"取决于列表顺序——
    /// 一个不报错、只给出错误解的行为。
    ///
    /// The second pre-check. **Without it a map keeps only the last entry**, so "which position it is fixed to" depends on list order — a behaviour that
    /// raises nothing and simply yields a wrong solution.
    /// </remarks>
    [Fact]
    public void AnItemLoadedInTwoPositions_ShouldBeRefused() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with {
                    Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0,
                    LoadedItems = [sample.Cargos[0].Name],
                },
                sample.Positions[1] with {
                    Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1,
                    LoadedItems = [sample.Cargos[0].Name],
                },
            ],
        };

        var aircraft = new AircraftContext();
        aircraft.Init(request).IsOk.Should().BeTrue();
        var stowage = new StowageContext();
        Try init = stowage.Init(aircraft, request, StowageMode.FullLoad);
        // **在装配阶段就拒**，而不是"聚合成功、上下文为空"：**早拒比晚拒好**——晚一步的话，
        // 上面的模型与约束都已经建好，调用方还得从一句"装载上下文缺失"倒推原因。
        // **Refused during assembly**, not "assembly succeeds, context is empty": **refusing early beats refusing late**, since by the later point the model and
        // constraints are built and the caller has to infer the cause from "loading context missing".
        init.IsOk.Should().BeFalse("the same cargo cannot sit in two positions");
        init.ToString().Should().Contain("货物不能固定到多个舱位",
            "the message must name the reason; a bare failure sends the caller looking in the wrong place");
        init.ToString().Should().Contain("C1",
            "and it must name the cargo, or a plan with many cargos gives no way to find the offending one");
    }

    /// <summary>**`allowedPositions` 只能是真实舱位** / `allowedPositions` may only name real positions.</summary>
    /// <remarks>
    /// 第一条前置校验。**少了它，一个拼错的舱位名会让那条限制永远不生效，而且不留痕迹**——
    /// 求解器只是在更大的解空间里挑一个解，看起来完全正常。
    ///
    /// The first pre-check. **Without it a misspelt position name makes the restriction silently inert** — the solver just picks from a larger
    /// solution space and everything looks normal.
    /// </remarks>
    [Fact]
    public void AnUnknownAllowedPosition_ShouldBeRefused() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Cargos = [
                sample.Cargos[0] with {
                    AllowedPositions = new HashSet<string>(StringComparer.Ordinal) { "NO_SUCH_POSITION" },
                },
                .. sample.Cargos.Skip(1),
            ],
        };

        var aircraft = new AircraftContext();
        aircraft.Init(request).IsOk.Should().BeTrue();
        var stowage = new StowageContext();
        Try init = stowage.Init(aircraft, request, StowageMode.FullLoad);
        init.IsOk.Should().BeFalse("a restriction naming a position that does not exist can never take effect");
        init.ToString().Should().Contain("允许舱位引用未知名称",
            "the message must say which restriction is wrong, or the caller has no way to find the typo");
    }

    /// <summary>**允许舱位真的会缩小指派集合** / Allowed positions really do shrink the assignment set.</summary>
    /// <remarks>
    /// 光有前置校验不够：**过滤那一支如果没接上，判据照样绿**。这条数的是装载内核里的指派条数。
    ///
    /// A pre-check alone is not enough: **if the filter branch were unwired the criteria would still be green**. This one counts assignments in the kernel.
    /// </remarks>
    [Fact]
    public void RestrictedPositions_ShouldShrinkTheAssignmentSet() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        int unrestricted = Aggregation(sample).Stowage.LoadingContext!.Problem.AllowedAssignments.Count;

        RequestDTO restricted = sample with {
            Cargos = [
                sample.Cargos[0] with {
                    AllowedPositions = new HashSet<string>(StringComparer.Ordinal) { sample.Positions[0].Name },
                },
                .. sample.Cargos.Skip(1),
            ],
        };
        int constrained = Aggregation(restricted).Stowage.LoadingContext!.Problem.AllowedAssignments.Count;

        constrained.Should().BeLessThan(unrestricted,
            "pinning one cargo to one position must remove the assignments it used to have elsewhere");
    }

    /// <summary>**甲板从请求穿到 `Item.location`** / The deck flows from the request into `Item.location`.
    /// </summary>
    /// <remarks>
    /// 把这条判据的落点换了。** 第一稿钉的是"下舱货拿不到主舱舱位"，变异测试发现**把甲板过滤关掉它照样绿**——
    /// 因为同一个 `DeckLocation` 早就换算成了 `Item.location`，`StowagePosition.Enabled(item)` 已经挡住了。
    /// 那道过滤因此被删掉（空转代码只会让人以为有两道防线），判据改为钉**真正起作用的那一步**。
    ///
    /// **moved where this criterion lands.** The first draft pinned "a lower-deck cargo cannot reach a main-deck position", and mutation testing showed it
    /// **stayed green with the deck filter disabled** — the same `DeckLocation` already becomes `Item.location`, which `StowagePosition.Enabled(item)` enforces.
    /// That filter was therefore deleted (a no-op only makes people believe there are two lines of defence) and the criterion now pins **the step that does the work**.
    /// </remarks>
    [Fact]
    public void TheDeck_ShouldFlowIntoTheItemLocation() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Cargos = [
                sample.Cargos[0] with { DeckLocation = DeckLocationInput.LowForward },
                sample.Cargos[1] with { DeckLocation = DeckLocationInput.Main },
                .. sample.Cargos.Skip(2),
            ],
        };

        StowageAggregation aggregation = Aggregation(request);
        // **`Main → normalMain`、其余 → `lowNotBulk`**； 之前这里恒为主舱。
        // **`Main` maps to `normalMain`, everything else to `lowNotBulk`**; before this was always main deck.
        Item lower = aggregation.Items.Single(item => item.Id == sample.Cargos[0].Name);
        Item main = aggregation.Items.Single(item => item.Id == sample.Cargos[1].Name);
        lower.Location.Tags.Should().Contain(ItemLocationTag.Low,
            "a lower-deck cargo must carry the lower tag, or the deck constraint has nothing to enforce");
        lower.Location.Tags.Should().NotContain(ItemLocationTag.Main,
            "and it must not also claim the main deck: before B937 every cargo did, which made the constraint vacuous");
        main.Location.Tags.Should().Contain(ItemLocationTag.Main);

        // **可观测后果**：它拿不到主舱舱位。`Item.location` 一侧被钉住之后，这一条才有意义。
        // **The observable consequence**: it gets no main-deck assignment. Only after `Item.location` is pinned does this line mean anything.
        aggregation.Stowage.LoadingContext!.Problem.AllowedAssignments
            .Should().NotContain(pair => pair.ItemId == sample.Cargos[0].Name,
                "every position in the sample sits on the main deck");
    }

    /// <summary>**最大装载件数来自请求** / The maximum item count comes from the request.</summary>
    /// <remarks>
    /// 之前硬编码成 `1UL`（注释叫它"保守值"）：一个能装 20 件的舱位被当成只能装 1 件，**不报错，只是把可行解砍掉一大半**。
    ///
    /// 判据同时看**模型侧**（`Mla`）和**内核侧**（`LoadingPosition.MaxCount`）：只钉一个的话，另一侧断了也不会红。
    ///
    /// Before this was hard-coded `1UL` ("conservative"): a position holding 20 items counted as holding 1 — **no error, just most of the solution space
    /// gone**. The criterion reads **both the model** (`Mla`) and **the kernel** (`LoadingPosition.MaxCount`); pinning one would miss the other going stale.
    /// </remarks>
    [Fact]
    public void TheMaxLoadCount_ShouldComeFromTheRequest() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with { Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0, MaxLoadCount = 20UL },
                sample.Positions[1] with { Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1 },
            ],
        };

        StowageAggregation aggregation = Aggregation(request);
        aggregation.Positions.Single(position => position.Name == "P1").Mla.Should().Be(20UL,
            "a hard-coded 1 makes a 20-item position look like a 1-item position, and nothing reports it");
        aggregation.Stowage.LoadingContext!.Problem.Positions
            .Single(position => position.Id == "P1").MaxCount.Should().Be(20UL,
                "and the kernel must see the same number, or the model and the solver disagree");
    }

    /// <summary>**建议装载件数/重量真的进了模型** / The advisory count/weight really reach the model.</summary>
    /// <remarks>
    /// 两条建议装载目标**只收非 `null` 的舱位**。 之前 DTO 没有这两个字段，于是那两条目标**永远是空表**——
    /// 不报错，只是"建议装载"这个能力在模型里根本不存在。
    ///
    /// The two advisory objectives **only collect positions with a non-null value**. Before the DTO had neither field, so both objectives were
    /// **always empty** — no error, just "advisory loading" not existing in the model at all.
    /// </remarks>
    [Fact]
    public void TheAdvisoryCountAndWeight_ShouldReachTheModel() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with {
                    Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0,
                    Ala = 3.0, Alw = 250.0,
                },
                sample.Positions[1] with { Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1 },
            ],
        };

        StowageAggregation aggregation = Aggregation(request);
        StowagePosition withAdvice = aggregation.Positions.Single(position => position.Name == "P1");
        StowagePosition without = aggregation.Positions.Single(position => position.Name == "P2");
        withAdvice.Ala.Should().Be(3.0);
        withAdvice.Alw.Should().Be(250.0);
        // **另一侧必须为空**：否则"每个舱位都有建议值"也能让上面两行绿，而那条目标是假的。
        // **The other side must be null**: otherwise "every position has advice" would also keep the two lines above green, and the objective would be fake.
        without.Ala.Should().BeNull("P2 has no advisory value; filling one in would invent a soft target nobody asked for");
        without.Alw.Should().BeNull();
    }

    /// <summary>**件数上限为零要拒** / A zero item cap must be refused.</summary>
    /// <remarks>
    /// 第一条 `maxLoadCount` 校验。**少了它，一个"装 0 件"的舱位会变成永远不可用的舱位**——而它看起来只是个普通舱位，
    /// 求解器用不上它也不报错。
    ///
    /// The first `maxLoadCount` check. **Without it a "holds 0 items" position becomes permanently unusable** while looking like an ordinary one — the
    /// solver just never uses it and says nothing.
    /// </remarks>
    [Fact]
    public void AZeroItemCap_ShouldBeRefused() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with { Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0, MaxLoadCount = 0UL },
                sample.Positions[1] with { Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1 },
            ],
        };

        var aircraft = new AircraftContext();
        aircraft.Init(request).IsOk.Should().BeTrue();
        var stowage = new StowageContext();
        Try init = stowage.Init(aircraft, request, StowageMode.FullLoad);
        init.IsOk.Should().BeFalse("a position that holds nothing is a data error, not a position");
        init.ToString().Should().Contain("最大装载件数必须为正",
            "and the message must say which cap is wrong");
    }

    /// <summary>**已装载件数超过上限要拒** / Loaded items exceeding the cap must be refused.</summary>
    /// <remarks>
    /// 第二条校验。**少了它，内核会收到一个初始就不可行的起点**：已装 2 件、上限 1 件。
    ///
    /// The second check. **Without it the kernel receives a starting point that is infeasible before it begins**: 2 items loaded, cap 1.
    /// </remarks>
    [Fact]
    public void MoreLoadedItemsThanTheCap_ShouldBeRefused() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with {
                    Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0,
                    MaxLoadCount = 1UL,
                    LoadedItems = [sample.Cargos[0].Name, sample.Cargos[1].Name],
                },
                sample.Positions[1] with { Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1, MaxLoadCount = 5UL },
            ],
        };

        var aircraft = new AircraftContext();
        aircraft.Init(request).IsOk.Should().BeTrue();
        var stowage = new StowageContext();
        Try init = stowage.Init(aircraft, request, StowageMode.FullLoad);
        init.IsOk.Should().BeFalse("two items already sit where only one fits");
        init.ToString().Should().Contain("已装载件数超过舱位上限",
            "and the message must name the position, or a plan with many positions gives no way to find it");
    }

    /// <summary>**请求里的重量数字在整条链上用同一个单位** / The request's weight numbers use one unit throughout the chain.</summary>
    /// <remarks>
    /// 用脚本扫「硬编码常量冒充请求字段」（`logs/b939-scan.py`），扫到一个 `WeightUnit: MassUnits.Gram`。
    /// 逐条看下来它**不是缺陷**：货物重量、舱位上限、问题的 `WeightUnit` 三处都用克，内部一致。
    /// 但"内部一致"这件事**当时没有任何判据**——只要有一处改成千克，重量就会差 1000 倍，**而可行性往往看不出来**（全部等比缩放）。
    ///
    /// 这条判据把三者钉在一起：**把货物重量与舱位上限都换算到问题声明的单位，必须等于请求里的原始数字**。
    /// 任何一处换单位，这里立刻红。
    ///
    /// scanned for "hard-coded constants standing in for request fields" with a script and found a `WeightUnit: MassUnits.Gram`. Reviewed one by one, it
    /// is **not a defect**: cargo weights, position caps and the problem's `WeightUnit` all use grams, consistently. But **"consistent" had no criterion** —
    /// changing one of them to kilograms would make weights differ by 1000x, **and feasibility usually would not show it** (everything scales together). This
    /// criterion ties the three together: **converted into the unit the problem declares, the item weight and the position cap must equal the request's raw
    /// numbers**. Changing a unit anywhere reddens it immediately.
    /// </remarks>
    [Fact]
    public void TheWeightNumbers_ShouldUseOneUnitThroughoutTheChain() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Cargos = [sample.Cargos[0] with { Weight = 12.5 }, .. sample.Cargos.Skip(1)],
            Positions = [
                sample.Positions[0] with { Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0, MaxWeight = 40.0 },
                sample.Positions[1] with { Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1 },
            ],
        };

        StowageAggregation aggregation = Aggregation(request);
        DiscreteLoadingProblem<Flt64> problem = aggregation.Stowage.LoadingContext!.Problem;

        // **货物重量**：换算到问题声明的单位后，必须还是请求里的 12.5。
        // **The item weight**: converted into the problem's unit it must still be the requested 12.5.
        LoadingItem<Flt64> item = problem.Items.Single(candidate => candidate.Id == sample.Cargos[0].Name);
        double itemInProblemUnit = InUnit(item.Weight, problem.WeightUnit);
        itemInProblemUnit.Should().BeApproximately(12.5, 1e-9,
            "a unit change on either side would make this 0.0125 or 12500 while every ratio stays the same");

        // **舱位上限**同理：内部一致时它等于请求里的 40.0。
        // **The position cap likewise**: when consistent it equals the requested 40.0.
        LoadingPosition<Flt64> position = problem.Positions.Single(candidate => candidate.Id == "P1");
        InUnit(position.MaxWeight, problem.WeightUnit).Should().BeApproximately(40.0, 1e-9);
    }
    /// <summary>把一个量换算到目标单位并取数值 / Convert a quantity into the target unit and read the number.</summary>
    /// <param name="quantity">量 / The quantity.</param>
    /// <param name="unit">目标单位 / Target unit.</param>
    /// <returns>换算后的数值 / The converted number.</returns>
    private static double InUnit(Quantity<Flt64> quantity, PhysicalUnit unit) {
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> converted = quantity.To(unit);
        converted.IsOk.Should().BeTrue("the two units must be convertible at all: " + converted);
        return ((Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>>)converted).Value.Value.ToDouble();
    }

    /// <summary>**谓词下限必须在舱位载重范围内** / The predicate lower bound must sit within the position capacity.</summary>
    /// <remarks>
    /// 规则明写：非有限、为负、或**大于舱位上限**就拒。三个数进去，谓词重量的松弛右端就是一个非法区间，
    /// 而求解器只会报"不可行"，**不会告诉你是哪一端写错了**。
    ///
    /// The rule is outright: reject a non-finite, negative, or **above-cap** value. Those make the slack right-hand side an illegal interval and the
    /// solver only says "infeasible", **never which end is wrong**.
    /// </remarks>
    [Fact]
    public void APredicateBoundAboveTheCapacity_ShouldBeRefused() {
        RequestDTO sample = AircraftLoadingDemo.CreatePredistributionSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                // **下限 999 远超这个舱位的 10.0 上限**。
                // **A lower bound of 999 against this position's 10.0 cap.**
                sample.Positions[0] with { PredicateLoadWeight = new PositionPredicateLoadWeightInput(10.0, 999.0, 10.0) },
                .. sample.Positions.Skip(1),
            ],
        };

        var aircraft = new AircraftContext();
        aircraft.Init(request).IsOk.Should().BeTrue();
        var stowage = new StowageContext();
        Try init = stowage.Init(aircraft, request, StowageMode.Predistribution);
        init.IsOk.Should().BeFalse("a lower bound above the cap describes an empty interval");
        init.ToString().Should().Contain("谓词装载重量下限非法",
            "the message must name the bound, not just fail");
    }

    /// <summary>**预配载缺下限要拒** / Predistribution without a bound must be refused.</summary>
    /// <remarks>
    /// `stowageMode == Predistribution && any { it == null }` 就失败。**缺规格时那两条软目标会悄悄消失**，
    /// 模式照跑，只是不再是预配载——**不报错，只是做的是另一件事**。
    ///
    /// `stowageMode == Predistribution && any { it == null }` fails. **Without the spec the two soft objectives quietly vanish** and the mode still
    /// runs — just no longer as predistribution. **No error, it simply does something else.**
    /// </remarks>
    [Fact]
    public void PredistributionWithoutABound_ShouldBeRefused() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        var aircraft = new AircraftContext();
        aircraft.Init(sample).IsOk.Should().BeTrue();
        var stowage = new StowageContext();
        Try init = stowage.Init(aircraft, sample, StowageMode.Predistribution);
        init.IsOk.Should().BeFalse("the full-load sample carries no predicate bound, so predistribution cannot be modelled");
        init.ToString().Should().Contain("预配载模式要求每个舱位给出谓词装载重量下限");
    }

    /// <summary>**全配载模式不要求下限** / Full load does not demand a bound.</summary>
    /// <remarks>
    /// **这条是前一条的反面**：少了它，"永远要求下限"也能让前一条绿，而全配载请求会被无辜拒掉。
    ///
    /// **The flip side of the previous one**: without it, "always demand a bound" would also keep that criterion green, while every full-load request gets
    /// refused for no reason.
    /// </remarks>
    [Fact]
    public void FullLoadWithoutABound_ShouldStillAssemble() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        var aircraft = new AircraftContext();
        aircraft.Init(sample).IsOk.Should().BeTrue();
        var stowage = new StowageContext();
        stowage.Init(aircraft, sample, StowageMode.FullLoad).IsOk.Should().BeTrue(
            "a full-load request has no predicate variables, so a missing bound is not an error there");
    }

    /// <summary>**请求声明的业务互斥真的进装载问题** / The request's declared business exclusions really reach the loading problem.</summary>
    /// <remarks>
    /// 之前这里恒为空集：**声明了也白声明，不报错，只是那条业务规则不存在**。
    /// "只增不减、不覆盖几何冲突"，所以这条判据同时要求**几何那一半还在**（不是被替换掉）。
    ///
    /// Before the set was always empty: **declaring one did nothing, with no error — the rule simply did not exist**. The set adds without ever replacing,
    /// so this criterion also requires **the geometric half to survive** rather than be replaced.
    /// </remarks>
    [Fact]
    public void TheDeclaredBusinessConflicts_ShouldReachTheProblem() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with { Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0 },
                sample.Positions[1] with { Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1 },
            ],
            // **几何上完全不重叠**，所以冲突集里除了业务这一条不该有别的东西。
            // **Geometrically disjoint**, so the conflict set should hold nothing but the business edge.
            PositionConflicts = [new PositionConflictInput("P1", "P2")],
        };

        StowageAggregation aggregation = Aggregation(request);
        aggregation.Stowage.LoadingContext!.Problem.Conflicts
            .Should().ContainSingle("a declared exclusion that never reaches the problem is a rule that does not exist");
        aggregation.Stowage.LoadingContext!.Problem.Conflicts
            .Should().Contain(new PositionConflict("P1", "P2"));
    }

    /// <summary>**请求声明的布局组真的进装载问题** / The request's layout groups really reach the loading problem.</summary>
    /// <remarks>
    /// 之前是空表，于是"区域或整机恰选一种布局"变成"随便几种"。判据逐字段看：组标识、受约束舱位、可选布局**一个都不能少**。
    ///
    /// Before the list was empty, turning "exactly one layout per region" into "any number". The criterion checks every field: group id, governed
    /// positions and the available layouts **all have to be there**.
    /// </remarks>
    [Fact]
    public void TheDeclaredLayoutGroups_ShouldReachTheProblem() {
        RequestDTO sample = AircraftLoadingDemo.CreateSampleRequest();
        RequestDTO request = sample with {
            Positions = [
                sample.Positions[0] with { Name = "P1", PartitionId = "DEFAULT", LoadingOrder = 0 },
                sample.Positions[1] with { Name = "P2", PartitionId = "DEFAULT", LoadingOrder = 1 },
            ],
            LayoutGroups = [
                new LayoutGroupInput(
                    "G1",
                    new HashSet<string>(StringComparer.Ordinal) { "P1", "P2" },
                    new List<LoadingLayoutInput> {
                        new("L1", new HashSet<string>(StringComparer.Ordinal) { "P1" }),
                        new("L2", new HashSet<string>(StringComparer.Ordinal) { "P2" }),
                    }),
            ],
        };

        StowageAggregation aggregation = Aggregation(request);
        // **空表会让 `Single` 抛，正好就是"变成随便几种"的反面证据**。
        // **An empty list throws here, which is exactly the inverse evidence for "any number".**
        aggregation.Stowage.LoadingContext!.Problem.LayoutGroups.Should().ContainSingle(
            "an empty list makes \"exactly one\" mean \"any number\"");
        LayoutSelectionGroup group = aggregation.Stowage.LoadingContext!.Problem.LayoutGroups[0];
        group.Id.Should().Be("G1");
        group.PositionIds.Should().BeEquivalentTo(new[] { "P1", "P2" },
            "dropping the governed positions widens the constraint from a region to the whole aircraft");
        group.Layouts.Should().HaveCount(2, "both layouts must survive, or the choice disappears");
        group.Layouts.Select(layout => layout.Id).Should().BeEquivalentTo(new[] { "L1", "L2" });
    }
}
