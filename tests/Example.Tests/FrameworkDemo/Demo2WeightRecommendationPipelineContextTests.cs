// 建议重量管线上下文的判据 / Criteria for the weight-recommendation pipeline context.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **建议重量管线上下文** / The weight-recommendation pipeline context.
/// </summary>
/// <remarks>
/// 判据的重点：**七个上下文都建了**、`Init` **首个失败即短路**（而不是七步全跑完再报）、以及**模式常量只有一个定义**。
///
/// The criteria focus on **all seven contexts existing**, `Init` **short-circuiting on the first failure** rather than running all seven, and the **mode constant having one definition**.
/// </remarks>
public class Demo2WeightRecommendationPipelineContextTests {
    /// <summary>
    /// **七个上下文在构造时就全部就位** / All seven contexts exist at construction time.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 七个属性**都不为 `null`**——基线是把它们就地 `= XxxContext` 初始化的。
    /// 2. 此刻**都还没初始化聚合**——构造不该有副作用。
    ///
    /// Two paired points: all seven properties are **non-null**, matching the baseline's in-place `= XxxContext` initialisation; and **none has an aggregation yet**, since construction
    /// must have no side effects.
    /// </remarks>
    [Fact]
    public void Contexts_ShouldAllExistBeforeInit() {
        var context = new WeightRecommendationPipelineContext();

        context.AircraftContext.Should().NotBeNull();
        context.StowageContext.Should().NotBeNull();
        context.MacContext.Should().NotBeNull();
        context.AirworthinessSecurityContext.Should().NotBeNull();
        context.ExpressEffectivenessContext.Should().NotBeNull();
        context.RecommendedWeightEqualizationContext.Should().NotBeNull();
        context.PayloadMaximizationContext.Should().NotBeNull();

        context.AircraftContext.Aggregation.Should().BeNull("construction must have no side effects");
        context.StowageContext.Aggregation.Should().BeNull();
        context.MacContext.Aggregation.Should().BeNull();
    }

    /// <summary>
    /// **`Init` 首个失败即短路，后面的上下文不被改动** / `Init` short-circuits on the first failure, leaving later contexts untouched.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 一个位置表为空的请求会让**飞机**那一步失败（或配载那一步），`Init` 返回失败。
    /// 2. **后面的上下文仍是未初始化**——这是"短路"与"七步全跑完再报"的唯一可观测差别。
    /// 3. 返回值是失败，不是成功。
    ///
    /// 第 2 点最关键：把七步塞进数组字面量会**先全部求值**，失败时后面六步已经跑过。
    ///
    /// Three paired points: a request with an empty position table fails at the aircraft (or stowage) step, so `Init` fails; **later contexts stay uninitialised**, which is the only
    /// observable difference between short-circuiting and running all seven; and the return value is a failure. Point 2 matters most: packing the seven into an array literal
    /// **evaluates all of them first**, so a failure would still leave the remaining six run.
    /// </remarks>
    [Fact]
    public void Init_ShouldShortCircuitAndLeaveLaterContextsUninitialised() {
        var context = new WeightRecommendationPipelineContext();
        var request = new RequestDTO(
            "REQ",
            new List<CargoInput> { new("A", 100.0, 1, "PEK", "SHA") },
            new List<PositionInput>());

        Try result = context.Init(request);

        result.IsOk.Should().BeFalse("an empty position table cannot seed the flow");
        // **后面的上下文必须还是空的**：短路之后它们一步都没跑。
        // **The later contexts must still be empty**: after short-circuiting they never ran.
        context.PayloadMaximizationContext.Aggregation.Should().BeNull(
            "a short-circuited Init must not reach the last step");
        context.RecommendedWeightEqualizationContext.Aggregation.Should().BeNull();
        context.ExpressEffectivenessContext.Aggregation.Should().BeNull();
        context.AirworthinessSecurityContext.Aggregation.Should().BeNull();
        context.MacContext.Aggregation.Should().BeNull();
    }

    /// <summary>
    /// **模式常量只有一个定义，且是 `WeightRecommendation`** / The mode constant has one definition and equals `WeightRecommendation`.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. `Mode` 是 `WeightRecommendation`——这套流程**只**在该模式下跑。
    /// 2. 它是**编译期常量**（`const`），所以六处调用不可能各写各的值。
    ///
    /// Two paired points: `Mode` is `WeightRecommendation`, the only mode this flow runs under; and it is a **compile-time constant**, so the six call sites cannot drift.
    /// </remarks>
    /// <summary>
    /// **整条链路能真的跑起来：七个上下文初始化 → 六个子域注册** / The whole chain actually runs: seven contexts init, six sub-domains register.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 用现成的样例请求 `Init` **成功**，七个聚合**全部非空**。
    /// 2. `Register` **成功**。
    /// 3. `MacContext.Register` 在这条路径上真的被调用—— 之前它根本不存在，而缺失的方法骗不过这一步。
    ///
    /// 这一条才能覆盖"注册顺序"与"每个子域都真的注册了"，只靠失败路径的分支判据覆盖不到。
    ///
    /// Three paired points: `Init` with the ready-made sample request **succeeds** and all seven aggregations are **non-null**; `Register` **succeeds**; and `MacContext.Register` really
    /// runs on this path — it did not exist before，and a missing method cannot fake this step. Only this criterion covers the registration order and "every sub-domain really
    /// registered"; failure-path criteria cannot reach them.
    /// </remarks>
    [Fact]
    public void InitAndRegister_ShouldRunTheWholeChain() {
        var context = new WeightRecommendationPipelineContext();
        // **不能直接用 `CreateSampleRequest`**：它早于，两个舱位的 `LoadingOrder` 都是默认的 0
        // （重复 → 飞机初始化的第 8 条校验必拒），`Spaces` 也是空的。这里补上这两样，但**不动那个共享夹具**——
        // 别处有判据依赖它"必定失败"。
        // **`CreateSampleRequest` cannot be used as-is**: it predates，both positions share the default `LoadingOrder` of 0 (a duplicate, which the aircraft initializer's
        // eighth check always rejects) and `Spaces` is empty. This fills both in **without touching the shared fixture**, which other criteria rely on to fail.
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            Spaces = new[] { new LoadingSpaceInput("MAIN", DeckLocationInput.Main) },
            Positions = new[] {
                new PositionInput("P1", 10.0, -1.0, -0.5,
                    SpaceId: "MAIN", PartitionId: "DEFAULT", LoadingOrder: 1,
                    Geometry: new PositionGeometryInput(-2.0, -0.5, -1.0, 0.0)),
                new PositionInput("P2", 10.0, 1.0, 0.5,
                    SpaceId: "MAIN", PartitionId: "DEFAULT", LoadingOrder: 2,
                    Geometry: new PositionGeometryInput(0.5, 2.0, 0.0, 1.0)),
            },
        };

        Try initialized = context.Init(request);
        initialized.IsOk.Should().BeTrue(
            initialized is Failed<Success, ErrorCode, Error<ErrorCode>> f ? f.Error.Message : "");
        context.AircraftContext.Aggregation.Should().NotBeNull();
        context.StowageContext.Aggregation.Should().NotBeNull();
        context.MacContext.Aggregation.Should().NotBeNull();
        context.AirworthinessSecurityContext.Aggregation.Should().NotBeNull();
        context.ExpressEffectivenessContext.Aggregation.Should().NotBeNull();
        context.RecommendedWeightEqualizationContext.Aggregation.Should().NotBeNull();
        context.PayloadMaximizationContext.Aggregation.Should().NotBeNull("the last step ran too");

        using var model = new LinearMetaModel<Flt64>();
        Try registered = context.Register(new Parameter(), model);
        registered.IsOk.Should().BeTrue(
            registered is Failed<Success, ErrorCode, Error<ErrorCode>> failure ? failure.Error.Message : "");
        // **固定条数与名字，不只断言非空**：只断言"非空"时，少注册一个子域照样通过（实测漏掉载荷最大化那条没被检出）。
        // **Pin the count and the names, not just non-emptiness**: a non-empty assertion passes when a sub-domain is dropped (measured: omitting payload maximisation slipped through).
        // 权重推荐模式下只有两个子域**加目标**，其余只加约束。逐条钉住，少一个就失败。
        // Only two sub-domains **add objectives** in weight-recommendation mode; the rest add constraints only. Pinning both catches either being dropped.
        model.MetaSubObjects.Select(entry => entry.Name).Should().BeEquivalentTo(new[] {
            "recommended_weight_deviation_objective", "max_payload_limit",
        });
    }

    /// <summary>
    /// **`MacContext.Register` 未初始化时报错** / `MacContext.Register` errors when uninitialised.
    /// </summary>
    /// <remarks>
    /// 之前这个方法**根本不存在**，所以"它存在且守住了未初始化"需要一条自己的判据——整链路的判据只走成功路径。
    ///
    /// Before this method **did not exist at all**, so "it exists and guards the uninitialised case" needs its own criterion — the whole-chain criterion only walks the success
    /// path.
    /// </remarks>
    [Fact]
    public void MacRegister_ShouldFailWhenUninitialised() {
        using var model = new LinearMetaModel<Flt64>();
        Try result = new WeightRecommendationPipelineContext().MacContext.Register(
            WeightRecommendationPipelineContext.Mode, model);

        result.IsOk.Should().BeFalse("an uninitialised MAC context must not report a successful registration");
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message.Should().Contain("尚未初始化");
    }

    [Fact]
    public void Mode_ShouldBeTheSingleRecommendationConstant() =>
        WeightRecommendationPipelineContext.Mode.Should().Be(StowageMode.WeightRecommendation);
}
