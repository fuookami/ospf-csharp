// 适航安全域聚合 / The airworthiness security domain aggregation.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;

/// <summary>
/// 适航安全域聚合 / The airworthiness security domain aggregation.
/// </summary>
/// <remarks>
/// 与基线 `demo2/domain/airworthiness_security/Aggregation.kt` 对齐。它在构造时就建好四个派生模型，`Register` 再把它们逐个注册进模型：
///
/// | 派生模型 | 何时建 | 备注 |
/// | --- | --- | --- |
/// | <see cref="LinearDensity"/> | 总是 | |
/// | <see cref="SurfaceDensity"/> | 总是 | |
/// | <see cref="MaxCLIM"/> | **宽体机 且 CLIM 点表非空** | 否则为 `null`，`CLIMLimit` 也就不会加 |
/// | <see cref="MinLowPayload"/> | 总是 | 点表为空时构造器抛异常（基线的 `first()` 也会） |
/// | <see cref="Envelopes"/> | 总是 | 按飞行阶段分组 |
///
/// 三处容易做错：
///
/// 1. **`MaxCLIM` 的两个前提**。只看"点表非空"会在窄体机上建一个永远用不到的 CLIM；只看"宽体"会在空点表时抛异常。
/// 2. **注册顺序**。基线是线性密度 → 表面密度 → `MaxCLIM` → `MinLowPayload` → 包络。顺序决定了模型里符号的排列，按序号引用约束的报告依赖它。
/// 3. **`RegisterForBendersSP` 就是 `Register`**，`RegisterForBendersMP` 是**空操作**（基线 `return ok`）。两者看起来该做别的事，但基线就是如此——照抄，并在测试里钉住。
///
/// Aligned with the baseline. Four derived models are built in the constructor and registered by `Register`, tabulated above. Three easy mistakes: (1) **`MaxCLIM` needs both
/// conditions** — testing only the point list builds a CLIM on a narrow body, testing only the wide-body flag throws on an empty list; (2) **the registration order** is linear
/// density → surface density → `MaxCLIM` → `MinLowPayload` → envelopes, and reports that reference constraints by index depend on it; (3) **`RegisterForBendersSP` is just
/// `Register`** while `RegisterForBendersMP` is a **no-op** (the baseline is `return ok`). Both look like they should do more; they do not, and the tests pin that.
/// </remarks>
public sealed class AirworthinessSecurityAggregation
{
    private readonly IReadOnlyDictionary<FlightPhase, IReadOnlyList<IEnvelope>> _envelopes;

    /// <summary>
    /// 构造聚合 / Construct the aggregation.
    /// </summary>
    /// <param name="aircraftModel">飞机型号 / The aircraft model.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="linearDensityLimitZones">线性密度限区 / The linear density zones.</param>
    /// <param name="linearDensityLimitLines">线性密度限制线 / The linear density limit lines.</param>
    /// <param name="surfaceDensityLimitZones">表面密度限区 / The surface density zones.</param>
    /// <param name="minLowPayloadPoints">最低业载点 / The minimum low payload points.</param>
    /// <param name="maxClimPoints">CLIM 点表，可为空 / The CLIM points, nullable.</param>
    /// <param name="envelopes">按飞行阶段的包络 / The envelopes by flight phase.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="totalWeight">总重模型 / The total weight model.</param>
    /// <param name="payload">业载模型 / The payload model.</param>
    /// <param name="maxZoneLoadWeight">区域载荷限重 / The zone load weight limits.</param>
    /// <param name="maxCumulativeLoadWeight">累积载荷限重 / The cumulative load weight limits.</param>
    /// <param name="torque">力矩模型 / The torque model.</param>
    /// <param name="horizontalStabilizers">水平安定面 / The horizontal stabilizers.</param>
    /// <param name="minPayload">最低业载 / The minimum payload.</param>
    /// <param name="targetLongitudinalMoment">目标纵向力矩 / The target longitudinal moment.</param>
    /// <param name="maxLongitudinalMomentDeviation">目标纵向力矩允许偏差 / The allowed target longitudinal moment deviation.</param>
    /// <param name="maxLateralImbalance">最大横向不平衡 / The maximum lateral imbalance.</param>
    /// <param name="ballast">配重模型，可为空 / The ballast model, nullable.</param>
    /// <param name="maxAdjacentLoadGap">相邻位置允许的最大载荷间隙，可为空 / The maximum allowed load gap between adjacent positions, nullable.</param>
    public AirworthinessSecurityAggregation(
        AircraftModel aircraftModel,
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyList<LinearDensityLimitZone> linearDensityLimitZones,
        IReadOnlyList<LinearDensityLimitLine> linearDensityLimitLines,
        IReadOnlyList<SurfaceDensityLimitZone> surfaceDensityLimitZones,
        IReadOnlyList<MinLowPayloadPoint> minLowPayloadPoints,
        IReadOnlyList<MaxClimPoint>? maxClimPoints,
        IReadOnlyDictionary<FlightPhase, IReadOnlyList<IEnvelope>> envelopes,
        Load load,
        TotalWeight totalWeight,
        Payload payload,
        MaxZoneLoadWeight maxZoneLoadWeight,
        MaxCumulativeLoadWeight maxCumulativeLoadWeight,
        Torque torque,
        IReadOnlyDictionary<HorizontalStabilizer.Key, HorizontalStabilizer> horizontalStabilizers,
        double minPayload,
        double targetLongitudinalMoment,
        double maxLongitudinalMomentDeviation,
        double maxLateralImbalance,
        Ballast? ballast = null,
        double? maxAdjacentLoadGap = null) {
        ArgumentNullException.ThrowIfNull(aircraftModel);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(linearDensityLimitZones);
        ArgumentNullException.ThrowIfNull(linearDensityLimitLines);
        ArgumentNullException.ThrowIfNull(surfaceDensityLimitZones);
        ArgumentNullException.ThrowIfNull(minLowPayloadPoints);
        ArgumentNullException.ThrowIfNull(envelopes);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(totalWeight);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(maxZoneLoadWeight);
        ArgumentNullException.ThrowIfNull(maxCumulativeLoadWeight);
        ArgumentNullException.ThrowIfNull(torque);
        ArgumentNullException.ThrowIfNull(horizontalStabilizers);

        AircraftModel = aircraftModel;
        Payload = payload;
        MaxZoneLoadWeight = maxZoneLoadWeight;
        MaxCumulativeLoadWeight = maxCumulativeLoadWeight;
        Torque = torque;
        HorizontalStabilizers = horizontalStabilizers;
        MinPayload = minPayload;
        TargetLongitudinalMoment = targetLongitudinalMoment;
        MaxLongitudinalMomentDeviation = maxLongitudinalMomentDeviation;
        MaxLateralImbalance = maxLateralImbalance;
        Ballast = ballast;
        MaxAdjacentLoadGap = maxAdjacentLoadGap;
        Positions = positions;
        Load = load;
        TotalWeight = totalWeight;
        // 密度模型要的是**飞机侧**的 `Position`（几何与坐标），而各条限制要的是**装载侧**的 `StowagePosition`
        // （带可用状态、最大重量、谓词）。C# 把基线的单个 `Position` 拆成了两个类型，这里按需要各给一份。
        // The density models take the **aircraft-side** `Position` (geometry and coordinates) while the limits take the **stowage-side** `StowagePosition` (status, maximum
        // weight, predicates). C# split the baseline's single `Position` into two types, so each consumer gets the one it needs.
        IReadOnlyList<Position> aircraftPositions = positions.Select(position => position.AircraftPosition).ToList();
        LinearDensity = new LinearDensity(linearDensityLimitZones, linearDensityLimitLines, aircraftPositions, load);
        SurfaceDensity = new SurfaceDensity(surfaceDensityLimitZones, aircraftPositions, load);
        MinLowPayload = new MinLowPayload(minLowPayloadPoints, totalWeight);
        _envelopes = envelopes;

        // 两个前提：宽体机**且**点表非空。只查一个都会出错——窄体机建一个用不到的 CLIM，或空点表让 `MaxCLIM` 构造器抛异常。
        // Both conditions: a wide body **and** a non-empty point list. Either alone is wrong — a narrow body gets a CLIM it never uses, or an empty list makes the `MaxCLIM`
        // constructor throw.
        if (aircraftModel.WideBody && maxClimPoints is { Count: > 0 }) {
            MaxCLIM = new MaxCLIM(maxClimPoints, totalWeight);
        }
    }

    /// <summary>飞机型号 / The aircraft model</summary>
    public AircraftModel AircraftModel { get; }

    /// <summary>舱位 / The positions</summary>
    public IReadOnlyList<StowagePosition> Positions { get; }

    /// <summary>装载模型 / The load model</summary>
    public Load Load { get; }

    /// <summary>总重模型 / The total weight model</summary>
    public TotalWeight TotalWeight { get; }

    /// <summary>线性密度 / The linear density</summary>
    public LinearDensity LinearDensity { get; }

    /// <summary>表面密度 / The surface density</summary>
    public SurfaceDensity SurfaceDensity { get; }

    /// <summary>最低业载 / The minimum low payload</summary>
    public MinLowPayload MinLowPayload { get; }

    /// <summary>最大 CLIM（窄体机或空点表时为 `null`）/ The maximum CLIM, `null` on narrow bodies or with an empty point list</summary>
    public MaxCLIM? MaxCLIM { get; }

    /// <summary>按飞行阶段的包络 / The envelopes by flight phase</summary>
    public IReadOnlyDictionary<FlightPhase, IReadOnlyList<IEnvelope>> Envelopes => _envelopes;

    /// <summary>业载模型 / The payload model</summary>
    public Payload Payload { get; }

    /// <summary>区域载荷限重 / The zone load weight limits</summary>
    public MaxZoneLoadWeight MaxZoneLoadWeight { get; }

    /// <summary>累积载荷限重 / The cumulative load weight limits</summary>
    public MaxCumulativeLoadWeight MaxCumulativeLoadWeight { get; }

    /// <summary>力矩模型 / The torque model</summary>
    public Torque Torque { get; }

    /// <summary>水平安定面 / The horizontal stabilizers</summary>
    public IReadOnlyDictionary<HorizontalStabilizer.Key, HorizontalStabilizer> HorizontalStabilizers { get; }

    /// <summary>最低业载 / The minimum payload</summary>
    public double MinPayload { get; }

    /// <summary>目标纵向力矩 / The target longitudinal moment</summary>
    public double TargetLongitudinalMoment { get; }

    /// <summary>目标纵向力矩允许偏差 / The allowed target longitudinal moment deviation</summary>
    public double MaxLongitudinalMomentDeviation { get; }

    /// <summary>最大横向不平衡 / The maximum lateral imbalance</summary>
    public double MaxLateralImbalance { get; }

    /// <summary>配重模型，可为空 / The ballast model, nullable</summary>
    public Ballast? Ballast { get; }

    /// <summary>相邻位置允许的最大载荷间隙，可为空 / The maximum allowed load gap between adjacent positions, nullable</summary>
    public double? MaxAdjacentLoadGap { get; }

    /// <summary>
    /// 注册全部适航安全约束 / Register every airworthiness security constraint.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 顺序照抄基线：线性密度 → 表面密度 → `MaxCLIM`（若有）→ `MinLowPayload` → 包络。
    ///
    /// `stowageMode` 在这条路径上**没有被用到**——基线的 `register` 也收它，只往 `registerForBendersSP` 传。保留参数是为了与基线签名一致。
    ///
    /// The order follows the baseline. `stowageMode` is **unused** on this path — the baseline accepts it too and only forwards it in `registerForBendersSP`. The parameter stays
    /// for signature parity.
    /// </remarks>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        _ = stowageMode;

        Try density = LinearDensity.Register(model);
        if (!density.IsOk) {
            return density;
        }
        Try surface = SurfaceDensity.Register(model);
        if (!surface.IsOk) {
            return surface;
        }
        if (MaxCLIM is { } maxClim) {
            Try clim = maxClim.Register(model);
            if (!clim.IsOk) {
                return clim;
            }
        }
        Try low = MinLowPayload.Register(model);
        if (!low.IsOk) {
            return low;
        }
        foreach (KeyValuePair<FlightPhase, IReadOnlyList<IEnvelope>> pair in _envelopes) {
            foreach (IEnvelope envelope in pair.Value) {
                Try added = envelope.Register(model);
                if (!added.IsOk) {
                    return added;
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// Benders 主问题注册（空操作）/ Benders master-problem registration (a no-op).
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 基线就是 `return ok`。这个签名看起来该加 Benders 割，但适航安全域的约束全是原问题的，主问题不需要额外的行。照抄。
    ///
    /// The baseline is `return ok`. The signature suggests it should add Benders cuts, but every airworthiness security constraint belongs to the original problem and the master
    /// needs no extra rows. Copied as-is.
    /// </remarks>
    public Try RegisterForBendersMP(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// Benders 子问题注册（等价于 <see cref="Register"/>）/ Benders sub-problem registration (equivalent to <see cref="Register"/>).
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="solution">主问题的解 / The solution from the master problem.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 基线就是 `return register(stowageMode, model)`，**忽略 `solution`**。看起来该按解固定某些变量，但适航约束是原问题的硬约束，子问题只是重加一遍。
    ///
    /// The baseline is `return register(stowageMode, model)` and **ignores `solution`**. It looks like it should fix variables to the master's answer, but airworthiness constraints
    /// are hard constraints of the original problem and the sub-problem merely re-adds them.
    /// </remarks>
    public Try RegisterForBendersSP(StowageMode stowageMode, LinearMetaModel<Flt64> model, IReadOnlyList<double> solution) {
        ArgumentNullException.ThrowIfNull(solution);
        return Register(stowageMode, model);
    }
}
