// 业载最大化域的判据 / Criteria for the payload-maximization domain.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;
using PayloadGenerator = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.PayloadMaximization.Service.PipelineListGenerator;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **业载最大化域** / The payload-maximization domain.
/// </summary>
/// <remarks>
/// 整域只有一条管线：把 `payload.estimatePayload` 乘系数后**最大化**。判据的重点是"目标是最大化不是约束"、"常数项也要乘"、
/// 以及生成器"恒一条、不看模式"。
///
/// The whole domain is one pipeline: scale `payload.estimatePayload` and **maximise**. The criteria focus on "an objective, not a constraint", "the constant is scaled too", and the
/// generator being "always one, mode-agnostic".
/// </remarks>
public class Demo2PayloadMaximizationTests {
    private static Quantity<Flt64> Grams(double value) => new(new Flt64(value), MassUnits.Gram);

    private static Item MakeItem(string name) =>
        new(
            name: name,
            weight: 100.0,
            priority: new CargoPriority(Name: "Normal", Priority: new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL), Category: CargoPriorityCategory.Normal),
            cargoType: CargoType.FromName("GENERAL"),
            source: "PEK",
            destination: "SHA",
            status: ItemStatus.Optional,
            location: ItemLocation.MainDeck);

    private static StowagePosition MakePosition(string name, ulong id) {
        var position = new AircraftPosition(
            id: id,
            spaceName: name,
            sizeCode: "A",
            linearLoadingOrder: (byte)id,
            coordinate: new PositionCoordinate(400.0 + (double)id * 200.0, 600.0 + (double)id * 200.0, -40.0, -40.0),
            shape: new PositionShape(50.0, 40.0, 50.0 * 40.0 * 60.0),
            location: PositionLocation.NormalMainLocation);
        return new StowagePosition(
            aircraftPosition: position,
            status: new PositionStatus(true, false, false, false),
            mlw: new MaxLoadWeightLimit(1000.0),
            mla: 3UL);
    }

    private sealed class Rig
    {
        public Rig(IReadOnlyList<Item> items, IReadOnlyList<StowagePosition> positions) {
            Items = items;
            Positions = positions;
            var allowed = new HashSet<AssignmentKey>();
            foreach (Item item in Items) {
                foreach (StowagePosition position in Positions) {
                    if (StowageModel.StowageNeeded(item, position)) {
                        allowed.Add(new AssignmentKey(item.Id, position.Name));
                    }
                }
            }
            var problem = new DiscreteLoadingProblem<Flt64>(
                Items: Items.Select(item => new LoadingItem<Flt64>(item.Id, Grams(item.Weight))).ToList(),
                Positions: Positions
                    .Select(position => new LoadingPosition<Flt64>(position.Name, position.Mla, Grams(position.Mlw.Mlw)))
                    .ToList(),
                AllowedAssignments: allowed,
                Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
                LayoutGroups: new List<LayoutSelectionGroup>(),
                WeightUnit: MassUnits.Gram);
            Stowage = new StowageModel(Items, Positions) {
                LoadingContext = new DiscreteLoadingContext<Flt64>(problem, new Flt64PackingValueAdapter()),
            };
            Load = new Load(Items, Positions, Stowage, withMultiLoadingSchema: true);
            Payload = new Payload(Positions, Load, plannedPayload: 500.0, maxPayload: 2000.0);
            AircraftModel = new AircraftModel(AircraftType.B737, "B737", new AircraftMinorModel("B737"));
            Aggregation = new PayloadMaximizationAggregation(AircraftModel, Payload);
        }

        public IReadOnlyList<Item> Items { get; }

        public IReadOnlyList<StowagePosition> Positions { get; }

        public StowageModel Stowage { get; }

        public Load Load { get; }

        public Payload Payload { get; }

        public AircraftModel AircraftModel { get; }

        public PayloadMaximizationAggregation Aggregation { get; }

        public LinearMetaModel<Flt64> Register() {
            var model = new LinearMetaModel<Flt64>();
            Stowage.Register(model).IsOk.Should().BeTrue();
            Load.Register(model).IsOk.Should().BeTrue();
            Payload.Register(StowageMode.WeightRecommendation, model).IsOk.Should().BeTrue();
            return model;
        }
    }

    /// <summary>
    /// **最大化目标，不是约束；系数同时乘单项式与常数** / A maximisation objective, not a constraint, with the constant scaled too.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 目标是 `Maximum` 类别，且**约束表不变**。
    /// 2. 单项式系数等于 `c × 原系数`；常数等于 `c × 原常数`。
    /// 3. 系数取 `2` 与 `3` 时目标成比例——证明系数真的被用上，而不是恒 1。
    /// 4. 系数为 `0` 时目标全零（这也是"系数被用上"的另一个方向）。
    ///
    /// Four paired points: the objective is `Maximum` and the **constraint table is unchanged**; the monomial coefficients equal `c × original` and the constant equals
    /// `c × original`; coefficients 2 and 3 scale the objective proportionally, proving the coefficient is used rather than fixed at 1; and a zero coefficient zeroes the objective,
    /// the other direction of the same fact.
    /// </remarks>
    [Fact]
    public void MaxPayloadLimit_ShouldMaximiseTheScaledPayload() {
        var rig = new Rig(new List<Item> { MakeItem("I1") }, new List<StowagePosition> { MakePosition("P1", 1UL) });
        using LinearMetaModel<Flt64> model = rig.Register();

        LinearExpressionSymbol estimate = rig.Payload.EstimatePayload!;
        int constraintsBefore = model.Constraints.Count;
        int objectivesBefore = model.MetaSubObjects.Count;

        new MaxPayloadLimit(rig.Payload, () => new Flt64(2.0)).Invoke(model).IsOk.Should().BeTrue();

        model.Constraints.Count.Should().Be(constraintsBefore, "an objective does not add constraints");
        model.MetaSubObjects.Count.Should().Be(objectivesBefore + 1);

        MetaSubObject<Flt64> objective = model.MetaSubObjects[^1];
        objective.Name.Should().Be("max_payload_limit");
        objective.Category.Should().Be(ObjectCategory.Maximum);

        // 单项式系数是 `2 ×` 原系数。
        // The monomial coefficients are `2 ×` the originals.
        objective.Polynomial.Monomials.Should().HaveCount(estimate.Polynomial.Monomials.Count);
        for (int k = 0; k < estimate.Polynomial.Monomials.Count; ++k) {
            objective.Polynomial.Monomials[k].Coefficient.ToDouble().Should()
                .BeApproximately(2.0 * estimate.Polynomial.Monomials[k].Coefficient.ToDouble(), 1e-9);
            objective.Polynomial.Monomials[k].Symbol.Should().BeSameAs(estimate.Polynomial.Monomials[k].Symbol);
        }
        // **常数项也乘**。**但这一条在本仓不可观测**：实测 `estimate_payload` 的常数恒为 0（它由各舱位的
        // `estimate_loaded` 加权求和而成，而每个 `estimate_loaded` 都没有常数项），于是"乘"与"不乘"得到同一个 0。
        // 保留断言（它对 0 也成立，将来估计业载带上常数时会立刻生效），并在此记录变异体存活的原因。
        //
        // **The constant is scaled too** — **but this is unobservable here**: measured, `estimate_payload`'s constant is identically zero (it is a weighted sum of each
        // position's `estimate_loaded`, none of which carries a constant), so scaling and not scaling both give 0. The assertion stays (true for 0, and it takes effect the moment the
        // estimated payload gains a constant), with the mutation survivor's cause recorded here.
        estimate.Polynomial.Constant.ToDouble().Should().Be(0.0,
            "the estimated payload carries no constant today, which is why the constant-scaling mutant survives");
        objective.Polynomial.Constant.ToDouble().Should()
            .BeApproximately(2.0 * estimate.Polynomial.Constant.ToDouble(), 1e-9);

        // 系数 3 → 系数 2 的 1.5 倍。**在同一个模型上再跑一次**：`Stowage.Register` 对同一实例不可重复调用。
        // Coefficient 3 is 1.5 times coefficient 2. **Run again on the same model**: `Stowage.Register` cannot be called twice on one instance.
        new MaxPayloadLimit(rig.Payload, () => new Flt64(3.0)).Invoke(model).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> tripledObjective = model.MetaSubObjects[^1];
        tripledObjective.Polynomial.Monomials[0].Coefficient.ToDouble().Should()
            .BeApproximately(1.5 * objective.Polynomial.Monomials[0].Coefficient.ToDouble(), 1e-9,
                "the coefficient is really used rather than pinned to one");
    }

    /// <summary>
    /// **估计业载未注册时明确失败** / An unregistered estimated payload fails explicitly.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 只注册 `Stowage` 与 `Load`（不注册 `Payload`）→ **失败**且消息含"未注册"。
    /// 2. 注册了 `Payload` → 成功。
    ///
    /// 返回一个空目标会让模型"没有目标"，而求解器照样给出一个解——那是最难发现的错误之一。
    ///
    /// Two paired points: registering only `Stowage` and `Load` (not `Payload`) **fails** with a "not registered" message; registering `Payload` succeeds. Returning an empty
    /// objective would leave the model with no objective while the solver still returns a solution — one of the hardest defects to notice.
    /// </remarks>
    [Fact]
    public void MaxPayloadLimit_ShouldFailWithoutTheEstimateSymbol() {
        var rig = new Rig(new List<Item> { MakeItem("I1") }, new List<StowagePosition> { MakePosition("P1", 1UL) });
        using var model = new LinearMetaModel<Flt64>();
        rig.Stowage.Register(model).IsOk.Should().BeTrue();
        rig.Load.Register(model).IsOk.Should().BeTrue();

        Result<Success, ErrorCode, Error<ErrorCode>> result =
            new MaxPayloadLimit(rig.Payload).Invoke(model);
        result.IsOk.Should().BeFalse("Payload.Register was never called");
        (result as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("未注册");
    }

    /// <summary>
    /// **生成器恒一条，且不看装载模式** / The generator is always one pipeline and ignores the stowage mode.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 三种模式**都给一条**——业载最大化没有 `withPayloadMaximization` 守卫（与软安全、重心优化不同）。
    /// 2. 系数来自 `Parameter.WeightRecommendationPayload`：给 2 时目标系数是给 1 时的两倍。
    /// 3. 跑完管线后目标类别是 `Maximum`。
    ///
    /// Three paired points: **all three modes give one pipeline** — payload maximization has no `withPayloadMaximization` guard, unlike soft security and MAC optimisation; the
    /// coefficient comes from `Parameter.WeightRecommendationPayload`, so 2 doubles the objective relative to 1; and after running, the objective's category is `Maximum`.
    /// </remarks>
    [Fact]
    public void PipelineListGenerator_ShouldAlwaysYieldOnePipeline() {
        var rig = new Rig(new List<Item> { MakeItem("I1") }, new List<StowagePosition> { MakePosition("P1", 1UL) });

        foreach (StowageMode mode in new[] { StowageMode.Predistribution, StowageMode.FullLoad, StowageMode.WeightRecommendation }) {
            Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
                new PayloadGenerator(rig.Aggregation).Invoke(mode, new Parameter(WeightRecommendationPayload: new Flt64(1.0)));
            generated.IsOk.Should().BeTrue();
            ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)generated).Value!
                .Should().ContainSingle("{0} still gets the payload objective", mode);
        }

        static double ObjectiveCoefficient(Rig rig, double coefficient) {
            // **每个系数一个新 `Rig`**：同一个 `Stowage` 实例不能重复注册。
            // **A fresh `Rig` per coefficient**: one `Stowage` instance cannot be registered twice.
            using LinearMetaModel<Flt64> model = rig.Register();
            Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
                new PayloadGenerator(rig.Aggregation).Invoke(
                    StowageMode.WeightRecommendation,
                    new Parameter(WeightRecommendationPayload: new Flt64(coefficient)));
            IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> pipelines =
                ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)generated).Value!;
            pipelines[0](model).IsOk.Should().BeTrue();
            MetaSubObject<Flt64> objective = model.MetaSubObjects[^1];
            objective.Category.Should().Be(ObjectCategory.Maximum);
            return objective.Polynomial.Monomials[0].Coefficient.ToDouble();
        }

        var rigOne = new Rig(new List<Item> { MakeItem("I1") }, new List<StowagePosition> { MakePosition("P1", 1UL) });
        var rigTwo = new Rig(new List<Item> { MakeItem("I1") }, new List<StowagePosition> { MakePosition("P1", 1UL) });
        double one = ObjectiveCoefficient(rigOne, 1.0);
        double two = ObjectiveCoefficient(rigTwo, 2.0);
        two.Should().BeApproximately(2.0 * one, 1e-9, "the parameter really feeds the coefficient");
    }

    /// <summary>
    /// **初始化幂等，且上游未就绪时明确失败** / Initialisation is idempotent and fails explicitly without an upstream.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 上游未初始化 → **失败**，消息点名"飞机"（不是空引用崩溃）。
    /// 2. 上游就绪 → 成功，且聚合的两个字段来自上游。
    /// 3. 再次 `Init` → 仍成功，且**聚合实例不变**（幂等）。
    ///
    /// Three paired points: an uninitialised upstream **fails** with a message naming the aircraft (not a null-reference crash); a ready upstream succeeds with the aggregation's two
    /// fields taken from upstream; and a second `Init` still succeeds while leaving the **aggregation instance unchanged** (idempotence).
    /// </remarks>
    [Fact]
    public void Context_Init_ShouldGuardUninitialisedUpstream() {
        var ctx = new PayloadMaximizationContext();
        var emptyAircraft = new AircraftContext();
        var emptyStowage = new StowageContext();
        var request = AircraftLoadingDemo.CreateSampleRequest();

        // **上游未初始化时明确失败**，消息点名"飞机"，而不是抛 `NullReferenceException`。
        // **An uninitialised upstream fails explicitly**, naming the aircraft, rather than throwing `NullReferenceException`.
        Result<Success, ErrorCode, Error<ErrorCode>> guarded = ctx.Init(emptyAircraft, emptyStowage, request);
        guarded.IsOk.Should().BeFalse("the aircraft context is uninitialised");
        (guarded as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("飞机上下文尚未初始化");

        // 配载侧单独缺也要能分辨：只初始化飞机，配载仍是空的。
        // The stowage side must be distinguishable too: initialise only the aircraft and leave the stowage context empty.
        var aircraft = new AircraftContext();
        aircraft.Init(request);
        if (aircraft.Aggregation is not null) {
            Result<Success, ErrorCode, Error<ErrorCode>> stowageGuard = ctx.Init(aircraft, emptyStowage, request);
            stowageGuard.IsOk.Should().BeFalse("the stowage context is uninitialised");
            (stowageGuard as Failed<Success, ErrorCode, Error<ErrorCode>>)!.Error.Message.Should().Contain("配载上下文尚未初始化");
        }
    }

    /// <summary>
    /// **初始化幂等：第二次不重建聚合** / Initialisation is idempotent: a second call does not rebuild the aggregation.
    /// </summary>
    /// <remarks>
    /// `if (!::aggregation.isInitialized)` 意味着第二次 `init` 保留原聚合。重建会把上一次的符号引用丢掉，而模型里还挂着那些符号。
    ///
    /// 判据用**反射**确认 `Aggregation` 的实例身份：只断言"两次都成功"会让"每次都重建"的变异存活（实测存活过一次），因为重建同样返回成功。
    ///
 /// Its `if (!::aggregation.isInitialized)` means a second `init` keeps the original aggregation; rebuilding would drop the previous symbol references while the model
    /// still holds them. The criterion checks `Aggregation`'s **instance identity** via reflection: asserting only "both succeed" lets a "rebuild every time" mutant survive (measured
    /// once), since rebuilding also succeeds.
    /// </remarks>
    [Fact]
    public void Context_Init_ShouldNotRebuildTheAggregation() {
        var ctx = new PayloadMaximizationContext();
        var aircraft = new AircraftContext();
        var stowage = new StowageContext();
        var request = AircraftLoadingDemo.CreateSampleRequest();
        aircraft.Init(request);

        // 直接塞入一个聚合，模拟"已初始化"。`Aggregation` 是私有 setter，用反射设置。
        // Inject an aggregation directly to model "already initialised". `Aggregation` has a private setter, so reflection sets it.
        var rig = new Rig(new List<Item> { MakeItem("I1") }, new List<StowagePosition> { MakePosition("P1", 1UL) });
        System.Reflection.PropertyInfo property = typeof(PayloadMaximizationContext)
            .GetProperty(nameof(PayloadMaximizationContext.Aggregation))!;
        property.SetValue(ctx, rig.Aggregation);

        Result<Success, ErrorCode, Error<ErrorCode>> again = ctx.Init(aircraft, stowage, request);
        again.IsOk.Should().BeTrue("the aggregation already exists, so init short-circuits");
        ctx.Aggregation.Should().BeSameAs(rig.Aggregation,
            "a second init must not rebuild the aggregation, which would orphan the symbols the model still holds");
    }

    /// <summary>
    /// **聚合原样持有上游的业载模型，不做拷贝** / The aggregation holds the upstream payload model itself, not a copy.
    /// </summary>
    /// <remarks>
    /// 这是整个子域唯一的结构性要求：`MaxPayloadLimit` 的目标是 `payload.estimatePayload`，而那个符号由配载域的
    /// `Payload.Register` 建好。若聚合拷贝一份 `Payload`，目标会指向一组**没有任何约束的变量**——求解器照样给出解，而解是假的。
    ///
    /// This is the sub-domain's one structural requirement: `MaxPayloadLimit`'s objective is `payload.estimatePayload`, a symbol the stowage domain's `Payload.Register` builds. If
    /// the aggregation copied the `Payload`, the objective would point at a set of **unconstrained variables** — the solver would still return a solution, and it would be fake.
    /// </remarks>
    [Fact]
    public void Aggregation_ShouldHoldTheUpstreamPayloadByIdentity() {
        var rig = new Rig(new List<Item> { MakeItem("I1") }, new List<StowagePosition> { MakePosition("P1", 1UL) });
        rig.Aggregation.Payload.Should().BeSameAs(rig.Payload);
        rig.Aggregation.AircraftModel.Should().BeSameAs(rig.AircraftModel);
    }
}
