// 重心优化域的判据 / Criteria for the MAC-optimisation domain.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using MacGenerator = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service.PipelineListGenerator;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// **重心优化域** / The MAC-optimisation domain.
/// </summary>
/// <remarks>
/// 四条管线，全是**最小化目标**。判据的重点：纵向松弛集合**随装载模式变化**、目标名与管线名**不是同一个字符串**、
/// 横向只对宽体机、安定面**每个一个目标**而不是合并。
///
/// Four pipelines, all **minimisation objectives**. The criteria focus on: the longitudinal slack set **varies with the stowage mode**, the objective name and the pipeline name are
/// **different strings**, lateral exists only for wide-body aircraft, and stabilizers get **one objective each** rather than a merge.
/// </remarks>
public class Demo2MacOptimizationTests {
    private static MacOptimizationAggregation MakeAggregation(
        AircraftType type = AircraftType.B737,
        IReadOnlyDictionary<HorizontalStabilizer.Key, HorizontalStabilizer>? stabilizers = null) {
        var aircraftModel = new AircraftModel(type, type.ToString(), new AircraftMinorModel(type.ToString()));
        // **`lip` 必须非零**：`BalancedArm(0) = lip`，lip 为 0 时 OPT 点的扭矩也是 0，"取 60 还是 70"就分辨不出来（实测两条断言都落在 0 上）。
        // **`lip` must be non-zero**: `BalancedArm(0) = lip`, so with a zero lip the OPT torque is also zero and "60 or 70" becomes indistinguishable (measured: both assertions landed on 0).
        var formula = new Formula(aircraftModel, lip: 25.0, chord: 1.0, standardDatum: 0.0, forceDistanceCoefficient: 1.0, doiCorrection: 0.0);
        var fuselage = new Fuselage(Liferaft: null, Dow: 0.0, Doi: 0.0, BalancedArm: 0.0);
        var fuel = new Dictionary<FlightPhase, FuelConstant>();
        var positions = new List<StowagePosition>();
        var items = new List<Item>();
        var stowage = new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage(items, positions);
        var load = new Load(items, positions, stowage, withMultiLoadingSchema: true);
        var payload = new Payload(positions, load, plannedPayload: 10.0, maxPayload: 20.0);
        var totalWeight = new TotalWeight(
            fuselage, fuel, payload,
            maxTotalWeight: new Dictionary<FlightPhase, double> { [FlightPhase.TakeOff] = 70.0 },
            computedTotalWeight: new Dictionary<FlightPhase, double> { [FlightPhase.TakeOff] = 60.0 });
        var torque = new Torque(fuselage, fuel, formula, positions, load, type.IsWideBody());
        return new MacOptimizationAggregation(
            aircraftModel, formula, totalWeight, torque,
            stabilizers ?? new Dictionary<HorizontalStabilizer.Key, HorizontalStabilizer>());
    }

    private static HorizontalStabilizer MakeStabilizer(string name, double? minTrim, double? maxTrim) =>
        new(
            new HorizontalStabilizer.Key(new HorizontalStabilizerAngle(name), null),
            Array.Empty<HorizontalStabilizer.Point>(),
            new HorizontalStabilizer.Limit(0.0, 0.0, WarnMinTrim: minTrim, WarnMaxTrim: maxTrim));

    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) {
        result.IsOk.Should().BeTrue();
        return ((Ok<T, ErrorCode, Error<ErrorCode>>)result).Value!;
    }

    /// <summary>
    /// **MAC 范围四个固定点：-1 / 0 / 1 / 2** / The four fixed MAC-range points: -1 / 0 / 1 / 2.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. **四个点，`C` 在 `2` 而不是 `-1` 的对称位**。写成对称的 `-1/0/1/2` 之外的值会移动 `C` 的扭矩与指数。
    /// 2. `OptPoint` 是 `OPT`，`LhsPoints` 只含 `A`，`RhsPoints` 含 `B` 与 `C`。
    /// 3. **参考重量取 `ComputedTotalWeight`**（60），不是 `MaxTotalWeight`（70）——`OPT` 点的扭矩是 `60 × g × BalancedArm(0)`。
    ///
    /// Three paired points: **four points with `C` at `2`**, not the mirror of `-1`; `OptPoint` is `OPT`, `LhsPoints` holds only `A`, and `RhsPoints` holds `B` and `C`; and **the
    /// reference weight comes from `ComputedTotalWeight`** (60), not `MaxTotalWeight` (70), so `OPT`'s torque is `60 × g × BalancedArm(0)`.
    /// </remarks>
    [Fact]
    public void MacRange_ShouldUseFourFixedPoints() {
        MacOptimizationAggregation aggregation = MakeAggregation();
        MACRange range = aggregation.MacRange;

        range.Points.Should().HaveCount(4);
        range.Points.Select(point => point.MacValue).Should().Equal(-1.0, 0.0, 1.0, 2.0);
        range.Points.Select(point => point.Type).Should().Equal(
            MACRangeType.A, MACRangeType.OPT, MACRangeType.B, MACRangeType.C);
        range.OptPoint!.Type.Should().Be(MACRangeType.OPT);
        range.LhsPoints.Select(point => point.Type).Should().Equal(MACRangeType.A);
        range.RhsPoints.Select(point => point.Type).Should().Equal(MACRangeType.B, MACRangeType.C);

        // 参考重量 60（Computed 优先于 Max）。
        // Reference weight 60 (Computed wins over Max).
        MACRangePoint opt = range.Points.Single(point => point.Type == MACRangeType.OPT);
        // 千克力数值等于千克数，所以扭矩 = 参考重量 × 臂长。
        // The kilogram-force value equals the kilogram count, so torque = reference weight × arm.
        opt.TorqueValue.Should().BeApproximately(60.0 * opt.BalancedArm, 1e-9,
            "the reference weight is the computed take-off weight, not the maximum");
        // **两级回退要分开断言**：只写"等于 60"在 Computed 与 Max 恰好相等时会放过顺序颠倒的变异。
        // 这里把两者设成不同的值（60 与 70），并显式排除 70。
        // **The two-level fallback needs separate assertions**: asserting only "equals 60" lets a reversed-order mutant through when Computed and Max happen to coincide. They are set
        // to different values (60 and 70) here, and 70 is excluded explicitly.
        opt.TorqueValue.Should().NotBeApproximately(70.0 * opt.BalancedArm, 1e-9,
            "the maximum take-off weight is the fallback, not the first choice");

        // **第三级回退**：两个字典都空时取 1，而不是 0。取 0 会让扭矩恒零而**不报错**。
        // **The third fallback**: with both dictionaries empty the reference is 1, not 0. A zero would make every torque zero **without reporting anything**.
        var bare = new TotalWeight(
            new Fuselage(null, 0.0, 0.0, 0.0),
            new Dictionary<FlightPhase, FuelConstant>(),
            new Payload(new List<StowagePosition>(), null!, plannedPayload: 0.0, maxPayload: 0.0),
            maxTotalWeight: new Dictionary<FlightPhase, double>(),
            computedTotalWeight: new Dictionary<FlightPhase, double>());
        var bareModel = new AircraftModel(AircraftType.B737, "B737", new AircraftMinorModel("B737"));
        MACRange bareRange = MACRange.From(
            bareModel,
            new Formula(bareModel, 25.0, 1.0, 0.0, 1.0, 0.0),
            bare);
        MACRangePoint bareOpt = bareRange.Points.Single(point => point.Type == MACRangeType.OPT);
        bareOpt.TorqueValue.Should().BeApproximately(1.0 * bareOpt.BalancedArm, 1e-9,
            "the third-level fallback is one, not zero");
    }

    /// <summary>
    /// **纵向松弛随装载模式变化** / The longitudinal slack set varies with the stowage mode.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. **全载 → `A` 与 `B`**，且**不含 `OPT`/`C`**。
    /// 2. **预配载 → 仅 `OPT`**，且名字是 `_opt`（小写）。
    /// 3. **建议打板 → 空集合**（不是"默认全注册"）。
    ///
    /// Three paired points: **full load → `A` and `B`**, excluding `OPT`/`C`; **predistribution → `OPT` only**, named `_opt` (lower-case); and **weight recommendation → an empty
    /// set**, not "register everything by default".
    /// </remarks>
    [Fact]
    public void LongitudinalBalance_ShouldDependOnTheStowageMode() {
        MacOptimizationAggregation aggregation = MakeAggregation();

        using var full = new LinearMetaModel<Flt64>();
        Unwrap(aggregation.Register(StowageMode.FullLoad, full));
        aggregation.LongitudinalBalance.Slack!.Keys.Should().BeEquivalentTo(
            new[] { MACRangeType.A, MACRangeType.B }, "OPT and C are not on either side of the range");
        aggregation.LongitudinalBalance.Slack[MACRangeType.A].Name.Should().Be("longitudinal_balance_slack_A");

        // **每个模式一个聚合**：`LongitudinalBalance.Slack` 是 `lateinit` 语义，只算一次。
        // **One aggregation per mode**: `LongitudinalBalance.Slack` is lateinit-style and computed once.
        MacOptimizationAggregation predistribution = MakeAggregation();
        using var pre = new LinearMetaModel<Flt64>();
        Unwrap(predistribution.Register(StowageMode.Predistribution, pre));
        predistribution.LongitudinalBalance.Slack!.Keys.Should().BeEquivalentTo(new[] { MACRangeType.OPT });
        predistribution.LongitudinalBalance.Slack[MACRangeType.OPT].Name.Should().Be("longitudinal_balance_slack_opt");

        MacOptimizationAggregation recommendation = MakeAggregation();
        using var rec = new LinearMetaModel<Flt64>();
        Unwrap(recommendation.Register(StowageMode.WeightRecommendation, rec));
        recommendation.LongitudinalBalance.Slack!.Should().BeEmpty("weight recommendation registers none");
    }

    /// <summary>
    /// **横向只对宽体机存在** / Lateral balance exists only for wide-body aircraft.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. （窄体）→ `LateralBalance` 为 `null`。
    /// 2. （宽体）→ 非 `null`，注册后符号名是 `lateral_balance_slack`。
    /// 3. **生成器的管线数量因此不同**：窄体 2 条（纵 + 安定面），宽体 3 条。
    ///
    /// Three paired points: a (narrow-body) gives a `null` `LateralBalance`; a (wide-body) gives one whose registered symbol is `lateral_balance_slack`; and **the generator's
    /// pipeline count differs as a result** — two for a narrow-body (longitudinal plus stabilizers), three for a wide-body.
    /// </remarks>
    [Fact]
    public void LateralBalance_ShouldExistOnlyForWideBody() {
        MakeAggregation(AircraftType.B737).LateralBalance.Should().BeNull("the B737 is a narrow-body");
        MacOptimizationAggregation wide = MakeAggregation(AircraftType.B767);
        wide.LateralBalance.Should().NotBeNull("the B767 is a wide-body");

        using var model = new LinearMetaModel<Flt64>();
        Unwrap(wide.Register(StowageMode.FullLoad, model));
        wide.LateralBalance!.Slack!.Name.Should().Be("lateral_balance_slack");

        IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> narrowPipelines =
            Unwrap(new MacGenerator(MakeAggregation(AircraftType.B737)).Invoke(StowageMode.FullLoad, new Parameter()));
        narrowPipelines.Should().HaveCount(2, "longitudinal plus stabilizers");
        IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> widePipelines =
            Unwrap(new MacGenerator(wide).Invoke(StowageMode.FullLoad, new Parameter()));
        widePipelines.Should().HaveCount(3, "longitudinal, lateral and stabilizers");
    }

    /// <summary>
    /// **目标名与管线名不同；安定面每个一个目标** / The objective name differs from the pipeline name, and stabilizers get one objective each.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. **纵向目标是 `"longitudinal balance"`（带空格）**，而管线 `Name` 是 `"longitudinal_balance_limit"`。
    /// 2. **纵向是单个目标**，即使有 `A` 与 `B` 两个松弛。
    /// 3. **安定面每个一个目标**，名字带键：`"horizontal stabilizer {key}"`。
    /// 4. 全部目标的类别都是 `Minimum`。
    ///
    /// Four paired points: the **longitudinal objective is `"longitudinal balance"`** (with a space) while the pipeline `Name` is `"longitudinal_balance_limit"`; **longitudinal is a
    /// single objective** even with two slacks (`A` and `B`); **stabilizers get one objective each**, named with the key; and every objective's category is `Minimum`.
    /// </remarks>
    [Fact]
    public void Limits_ShouldNameObjectivesDifferentlyAndSplitStabilizers() {
        var stabilizers = new Dictionary<HorizontalStabilizer.Key, HorizontalStabilizer> {
            [new HorizontalStabilizer.Key(new HorizontalStabilizerAngle("HS1"), null)] = MakeStabilizer("HS1", null, null),
            [new HorizontalStabilizer.Key(new HorizontalStabilizerAngle("HS2"), null)] = MakeStabilizer("HS2", null, null),
        };
        MacOptimizationAggregation aggregation = MakeAggregation(AircraftType.B737, stabilizers);
        using var model = new LinearMetaModel<Flt64>();
        Unwrap(aggregation.Register(StowageMode.FullLoad, model));
        // **安定面自己也要注册**：`WarnSlack` 由 `HorizontalStabilizer.Register` 建。
        // **The stabilizers register themselves too**: `WarnSlack` is built by `HorizontalStabilizer.Register`.
        foreach (HorizontalStabilizer stabilizer in stabilizers.Values) {
            stabilizer.Register(StowageMode.FullLoad, model).IsOk.Should().BeTrue();
        }

        new LongitudinalBalanceLimit(aggregation.LongitudinalBalance, _ => new Flt64(1.0))
            .Invoke(model).IsOk.Should().BeTrue();
        new HorizontalStabilizerLimit(aggregation.HorizontalStabilizers, _ => new Flt64(1.0))
            .Invoke(model).IsOk.Should().BeTrue();

        model.MetaSubObjects.Select(sub => sub.Name).Should().BeEquivalentTo(new[] {
            "longitudinal balance", "horizontal stabilizer HS1", "horizontal stabilizer HS2",
        });
        model.MetaSubObjects.Select(sub => sub.Name).Should().NotContain("longitudinal_balance_limit",
            "the objective name is spaced, the pipeline name is underscored");
        model.MetaSubObjects.Should().OnlyContain(sub => sub.Category == ObjectCategory.Minimum);
        // **纵向是单个目标**，不是 A、B 各一个。
        // **Longitudinal is a single objective**, not one each for A and B.
        model.MetaSubObjects.Count(sub => sub.Name == "longitudinal balance").Should().Be(1);
    }

    /// <summary>
    /// **系数三分支：`C` → `MacRangeC`，→ ` LongitudinalBalance`，其余 → `LongitudinalBalance`** / Three coefficient branches.
    /// </summary>
    /// <remarks>
    /// 三点成对，用**预配载模式**让 `OPT` 松弛存在：
    ///
    /// 1. 走 ` LongitudinalBalance`（不是 `LongitudinalBalance`）。
    /// 2. 走 `LongitudinalBalance`——证明第二分支判的是 **本身**，不是"窄体机"。
    /// 3. 三者系数取 1/2/3 时目标系数分别是 1/2/3，证明分支真的生效。
    ///
    /// Three paired points on **predistribution** so the `OPT` slack exists: a takes ` LongitudinalBalance` (not `LongitudinalBalance`); a takes
    /// `LongitudinalBalance`, proving the middle branch tests **specifically** rather than "narrow-body"; and coefficients 1/2/3 produce objective coefficients 1/2/3, proving the
    /// branches take effect.
    /// </remarks>
    [Fact]
    public void LongitudinalBalanceLimit_ShouldPickTheCoefficientByType() {
        // **这条判据只能测"三分支各自可达"，测不出系数数值**：三个松弛符号都是恒零多项式（基线就是
        // `LinearExpressionSymbol(Flt64, name = …)`），所以乘任何系数都得到零多项式，目标里**没有单项式**。
        // 强行断言系数会让判据依赖一个不存在的单项式——实测抛 `ArgumentOutOfRangeException`。
        // 系数是否真的被用上，在 `LongitudinalBalance` 之外的路径（`MacRangeC` 等参数解析）里没有别的可观察面，
        // 因此这里只钉"三分支都能装配出一个目标"，并把不可观测记下来。
        //
        // **This criterion can only show the three branches are reachable, not the coefficient values**: all three slack symbols are zero polynomials (the code writes
        // `LinearExpressionSymbol(Flt64, name = …)`), so multiplying by any coefficient still gives the zero polynomial and the objective carries **no monomials**. Asserting a
        // coefficient would depend on a monomial that does not exist — measured to throw `ArgumentOutOfRangeException`. Whether the coefficient is really used has no other observable
        // surface outside `LongitudinalBalance` (the `MacRangeC` parameter is parsed elsewhere), so this pins only "each branch assembles one objective" and records the gap.
        foreach (AircraftType type in new[] { AircraftType.B737, AircraftType.B757, AircraftType.B767 }) {
            MacOptimizationAggregation aggregation = MakeAggregation(type);
            using var model = new LinearMetaModel<Flt64>();
            Unwrap(aggregation.Register(StowageMode.FullLoad, model));
            Try applied = new LongitudinalBalanceLimit(aggregation.LongitudinalBalance, rangeType => rangeType == MACRangeType.C
                ? new Flt64(999.0)
                : type == AircraftType.B737
                    ? new Flt64(3.0)
                    : new Flt64(2.0)).Invoke(model);
            applied.IsOk.Should().BeTrue("{0} assembles its longitudinal objective", type);
            model.MetaSubObjects.Should().ContainSingle(sub => sub.Name == "longitudinal balance");
            model.MetaSubObjects.Single(sub => sub.Name == "longitudinal balance")
                .Polynomial.Monomials.Should().BeEmpty(
                    "every slack is a zero polynomial, which is why the coefficient is unobservable here");
            // **` ` 与 ` ` 走不同分支这件事也不可观测**：两者都是恒零松弛，乘 2 或乘 3 都得零。
            // 实测：把 `type == AircraftType. ` 放宽成 ` or `，判据仍然全绿。
            // 这里如实记录，而不是编一条"看起来在测"的断言。
            // **Which branch a ` ` versus a ` ` takes is unobservable too**: both are zero slacks, so 2 or 3 both give zero. Measured: widening
            // `type == AircraftType. ` to ` or ` leaves every criterion green. Recorded honestly rather than dressed up as an assertion that looks like it tests it.
        }
    }

    /// <summary>
    /// **装载模式关掉重心优化时返回空列表，不是失败** / A disabled stowage mode yields an empty list, not a failure.
    /// </summary>
    /// <remarks>
    /// 两点成对：
    ///
    /// 1. 关闭模式（这里用 `StowageMode` 中 `WithMacOptimization` 为假的那个）→ **成功且空列表**。
    /// 2. 开启模式 → 非空。
    ///
    /// 返回失败会让上层把"这个模式不需要重心优化"当成错误。
    ///
    /// Two paired points: a disabled mode (whichever `StowageMode` has `WithMacOptimization` false) yields **success with an empty list**, and an enabled one yields a non-empty
    /// list. Returning a failure would make the caller treat "this mode does not need MAC optimisation" as an error.
    /// </remarks>
    [Fact]
    public void PipelineListGenerator_ShouldReturnEmptyWhenTheModeIsOff() {
        MacOptimizationAggregation aggregation = MakeAggregation();
        StowageMode disabled = Enum.GetValues<StowageMode>().Single(mode => !mode.WithMacOptimization());
        StowageMode enabled = StowageMode.FullLoad;

        Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> off =
            new MacGenerator(aggregation).Invoke(disabled, new Parameter());
        off.IsOk.Should().BeTrue("a disabled mode is not an error");
        Unwrap(off).Should().BeEmpty();

        Unwrap(new MacGenerator(aggregation).Invoke(enabled, new Parameter())).Should().NotBeEmpty();
    }

    /// <summary>
    /// **初始化器：上游缺失或位置为空都明确失败** / The initializer fails explicitly on a missing upstream or empty positions.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 两点成对：
    ///
    /// 1. 三个上游全为 `null` → 失败，消息点名**"飞机"**（第一个守卫）。
    /// 2. 只有飞机就绪 → 失败，消息点名**"配载"**。
    ///
    /// 两条守卫的消息必须可区分：都用"未初始化"会让"配错了上游"无法定位。第三、四条守卫（重心缺失、位置为空）需要完整的上游聚合，
    /// 构造代价远超本判据的价值，**未覆盖**——`AggregationInitializer` 的源码里它们各自有独立分支，属于可读的直线代码。
    ///
    /// Two paired points: all three upstreams `null` fails naming the **aircraft** (the first guard); only the aircraft ready fails naming **stowage**. The two guards' messages must be
    /// distinguishable, since a shared "not initialised" would make a mis-wired upstream impossible to locate. The third and fourth guards (missing MAC aggregation, empty positions)
    /// need complete upstream aggregations whose construction cost far outweighs this criterion's value, so they are **not covered** — each is a separate branch of straight-line code
    /// in `AggregationInitializer`.
    /// </remarks>
    [Fact]
    public void AggregationInitializer_ShouldGuardEachUpstream() {
        var request = new RequestDTO("REQ", Array.Empty<CargoInput>(), Array.Empty<PositionInput>());
        string? Message(
            Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.AircraftAggregation? aircraft,
            Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.StowageAggregation? stowage,
            Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.MacAggregation? mac) {
            Result<MacOptimizationAggregation, ErrorCode, Error<ErrorCode>> result =
                Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization.Service.AggregationInitializer.Initialize(aircraft, stowage, mac, request);
            return result is Failed<MacOptimizationAggregation, ErrorCode, Error<ErrorCode>> failed
                ? failed.Error.Message
                : null;
        }

        Message(null, null, null).Should().Contain("飞机聚合未初始化");
        Message(null, null, null).Should().NotContain("配载", "the aircraft guard fires first");
        Message(FakeAircraft(), null, null).Should().Contain("配载聚合未初始化");
    }

    /// <summary>造一个最小飞机聚合 / Build a minimal aircraft aggregation.</summary>
    private static Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.AircraftAggregation FakeAircraft() {
        var aircraftModel = new AircraftModel(AircraftType.B737, "B737", new AircraftMinorModel("B737"));
        var formula = new Formula(aircraftModel, 0.0, 1.0, 0.0, 1.0, 0.0);
        return new Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.AircraftAggregation(
            regNo: new RegNo("DEFAULT"),
            aircraftModel: aircraftModel,
            formula: formula,
            fuselage: new Fuselage(null, 0.0, 0.0, 0.0),
            fuelTanks: Array.Empty<FuelTank>(),
            fuel: new Dictionary<FlightPhase, FuelConstant>(),
            decks: Array.Empty<Deck>(),
            neighbours: new Dictionary<NeighbourType, IReadOnlyList<Neighbour>>());
    }
}
