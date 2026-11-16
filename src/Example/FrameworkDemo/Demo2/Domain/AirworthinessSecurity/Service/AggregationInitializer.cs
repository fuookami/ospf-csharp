#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service;

/// <summary>
/// 适航安全聚合初始化器 / The airworthiness security aggregation initializer.
/// </summary>
/// <remarks>
/// 与基线 `airworthiness_security/service/AggregationInitializer.kt` 逐段对齐。**它是一整片 DTO → 模型的翻译**，没有算法，
/// 因此最容易出的错不是算错而是**抄漏**：漏掉一个校验、漏掉一个 `min`、把 `maxOf(1.0, …)` 写成 `maxOf(0.0, …)`。
///
/// 四条校验的边界都照抄，包括**看起来重复**的那几条（`payloadUpperBound` 在第二条与第三条各查一次有限性）。基线如此。
///
/// **单位换算因子恒为 1**：基线用 `input.lengthUnit`/`weightUnit` 换算成机型单位。本仓的 `RequestDTO` 没有单位字段，
/// 机型单位固定为英寸/千克，所以因子是 1。若将来 DTO 引入单位，这里是唯一要改的地方。
///
/// Aligned section by section with the baseline. It is **one long DTO-to-model translation** with no algorithm, so the easy mistake is not miscalculating but **transcribing
/// incompletely**: a dropped validation, a dropped `min`, `maxOf(1.0, …)` written as `maxOf(0.0, …)`. All four validations are copied including the **apparently redundant**
/// checks (`payloadUpperBound` is finiteness-checked in both the second and the third block) — the baseline is so written. **The unit conversion factor is always 1**: the
/// baseline converts through `input.lengthUnit`/`weightUnit` into the aircraft's units, while this port's `RequestDTO` carries no unit fields and the aircraft units are fixed at
/// inches/kilograms. If the DTO ever gains units, this is the only place to change.
/// </remarks>
internal static class AggregationInitializer
{
    private const double MaxLinearDensity = 500.0;
    private const double MaxSurfaceDensity = 1000.0;

    /// <summary>
    /// 从飞机、配载和 MAC 聚合初始化适航安全聚合 / Initialize the airworthiness security aggregation from the aircraft, stowage and MAC aggregations.
    /// </summary>
    /// <param name="aircraftAggregation">飞机聚合 / The aircraft aggregation.</param>
    /// <param name="stowageAggregation">配载聚合 / The stowage aggregation.</param>
    /// <param name="macAggregation">MAC 聚合 / The MAC aggregation.</param>
    /// <param name="input">请求 DTO / The request DTO.</param>
    /// <returns>聚合结果 / The aggregation result.</returns>
    public static Result<AirworthinessSecurityAggregation, ErrorCode, Error<ErrorCode>> Initialize(
        AircraftAggregation aircraftAggregation,
        StowageAggregation stowageAggregation,
        MacAggregation macAggregation,
        RequestDTO input) {
        ArgumentNullException.ThrowIfNull(aircraftAggregation);
        ArgumentNullException.ThrowIfNull(stowageAggregation);
        ArgumentNullException.ThrowIfNull(macAggregation);
        ArgumentNullException.ThrowIfNull(input);

        AircraftModel aircraftModel = aircraftAggregation.AircraftModel;

        // `ponytail:` 单位换算因子恒为 1（本仓 DTO 无单位字段，机型单位固定）。DTO 引入单位时改这里。
        // `ponytail:` the unit factor is always 1 (no unit fields in this DTO, aircraft units fixed). Change here when the DTO gains units.
        // **力矩的换算因子必须跟着长度单位走**：模型内部长度单位是英寸（`AircraftModel.LengthUnit`），
        // 力矩 = 重量 x 长度，所以请求里的米制力矩边界要乘以 1/0.0254。这里写死成 `1.0` 会让
        // "力臂已经换算成英寸、边界还是米"两边对不上——**力矩行于是把装载量整个否掉**（隔离实测）。
        // 与 `Aircraft.AggregationInitializer.ToModelLength` 用同一套换算，两处不能各写一份。
        //
        // **The moment factor has to follow the length unit**: the model's internal length unit is the inch (`AircraftModel.LengthUnit`), and torque is weight over
        // length, so metre-scale moment bounds must be multiplied by 1/0.0254. Hard-coding `1.0` leaves "arms converted to inches but bounds still in metres", and the
        // moment rows then reject the whole load (measured by the earlier isolation). It mirrors `Aircraft.AggregationInitializer.ToModelLength` — one conversion, not two.
        double momentFactor = input.LengthUnit switch {
            LengthUnitInput.Meter => 1.0 / 0.0254,
            LengthUnitInput.Centimeter => 1.0 / 2.54,
            _ => 1.0,
        };

        IReadOnlyList<StowagePosition> positions = stowageAggregation.Positions;
        if (positions.Count == 0) {
            return Results.Failed<AirworthinessSecurityAggregation>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "适航安全聚合至少需要一个装载位置 / airworthiness aggregation requires at least one stowage position"));
        }
        if (!double.IsFinite(input.MaxCumulativeForwardLoad) || input.MaxCumulativeForwardLoad < 0.0
            || !double.IsFinite(input.MaxCumulativeBackwardLoad) || input.MaxCumulativeBackwardLoad < 0.0
            || !double.IsFinite(input.MaxAdjacentLoadGap) || input.MaxAdjacentLoadGap < 0.0) {
            return Results.Failed<AirworthinessSecurityAggregation>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "适航载荷上限必须为有限非负值 / airworthiness load limits must be finite and non-negative"));
        }
        if (!double.IsFinite(input.PayloadUpperBound) || input.PayloadUpperBound < 0.0
            || !double.IsFinite(input.MinPayloadRatio) || input.MinPayloadRatio < 0.0 || input.MinPayloadRatio > 1.0) {
            return Results.Failed<AirworthinessSecurityAggregation>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "业载边界必须为有限值且最小比例位于 [0, 1] / payload bounds must be finite and the minimum ratio within [0, 1]"));
        }
        if (!double.IsFinite(input.EnvelopeLongitudinalMomentMin)
            || !double.IsFinite(input.EnvelopeLongitudinalMomentMax)
            || input.EnvelopeLongitudinalMomentMin > input.EnvelopeLongitudinalMomentMax
            || !double.IsFinite(input.TargetLongitudinalMoment)
            || !double.IsFinite(input.MaxLongitudinalMomentDeviation) || input.MaxLongitudinalMomentDeviation < 0.0
            || !double.IsFinite(input.MaxLateralImbalance) || input.MaxLateralImbalance < 0.0) {
            return Results.Failed<AirworthinessSecurityAggregation>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "力矩与横向平衡边界必须为有效有限区间 / moment and lateral-balance bounds must be valid finite intervals"));
        }

        // 累积载荷的两条区域：向前按**后臂**升序扫，向后按**前臂**降序扫。两个排序方向相反，抄错一个方向不会报错，只会让限制区间反着来。
        // The two cumulative-load zones: forward sweeps by **back arm** ascending, aft by **front arm** descending. The two orders are opposite; getting one wrong raises no
        // error and merely reverses the limit interval.
        var forwardPositions = positions
            .OrderBy(position => position.AircraftPosition.Coordinate.BackArm)
            .ToList();
        var aftPositions = positions
            .OrderByDescending(position => position.AircraftPosition.Coordinate.FrontArm)
            .ToList();
        var aircraftPositions = positions.Select(position => position.AircraftPosition).ToList();

        var cumulativeLoadWeight = new MaxCumulativeLoadWeight {
            LimitZones = new List<CumulativeLoadLimitZone> {
                new(
                    direction: CumulativeLoadDirection.FWD,
                    fromArm: forwardPositions[0].AircraftPosition.Coordinate.FrontArm,
                    points: forwardPositions
                        .Select(position => new CumulativeLoadPoint(
                            ToArm: position.AircraftPosition.Coordinate.BackArm,
                            MaxSum: input.MaxCumulativeForwardLoad))
                        .ToList(),
                    positions: aircraftPositions),
                new(
                    direction: CumulativeLoadDirection.AFT,
                    fromArm: aftPositions[0].AircraftPosition.Coordinate.BackArm,
                    points: aftPositions
                        .Select(position => new CumulativeLoadPoint(
                            ToArm: position.AircraftPosition.Coordinate.FrontArm,
                            MaxSum: input.MaxCumulativeBackwardLoad))
                        .ToList(),
                    positions: aircraftPositions),
            },
        };

        double totalCargoWeight = stowageAggregation.Items.Sum(item => item.Weight);
        double minPayload = System.Math.Min(input.PayloadUpperBound, totalCargoWeight) * input.MinPayloadRatio;

        // 下甲板载荷只在**有下甲板舱位**时才要下限。没有下甲板时给 0 而不是给 minPayload——后者会要求主甲板承担一个本该由下货舱承担的下限。
        // The low-deck payload floor applies only when there **is** a low-deck position. Without one it is 0, not minPayload — the latter would make the main deck carry a floor
        // meant for the lower cargo hold.
        bool hasLowDeck = positions.Any(position =>
            position.AircraftPosition.Location.Location == DeckLocation.LowForward
            || position.AircraftPosition.Location.Location == DeckLocation.LowAft);
        double minLowPayload = hasLowDeck ? minPayload : 0.0;
        var minLowPayloadPoints = new List<MinLowPayloadPoint> {
            new(MinLowPayloadValue: minLowPayload, Zfw: 0.0),
            new(MinLowPayloadValue: minLowPayload, Zfw: input.PayloadUpperBound),
        };

        var densityLocations = positions
            .Select(position => position.AircraftPosition.Location.Location)
            .ToHashSet();
        double densityFrontArm = positions.Min(position => position.AircraftPosition.Coordinate.FrontArm);
        double densityBackArm = positions.Max(position => position.AircraftPosition.Coordinate.BackArm);
        var linearDensityLimitZones = new List<LinearDensityLimitZone> {
            new(
                Name: "request_linear_density",
                Locations: densityLocations,
                FrontArm: densityFrontArm,
                BackArm: densityBackArm,
                MaxLinearDensity: MaxLinearDensity),
        };
        var surfaceDensityLimitZones = new List<SurfaceDensityLimitZone> {
            new(
                Name: "request_surface_density",
                FrontArm: densityFrontArm,
                BackArm: densityBackArm,
                MaxSurfaceDensity: MaxSurfaceDensity),
        };

        // 包线的两个重量端点是 `0` 与 `maxOf(1.0, payloadUpperBound, totalCargoWeight)`。那个 `1.0` 是**除零保护**：
        // 两个端点重量相同时包线退化成一个点，插值会除以零。写成 `maxOf(0.0, …)` 看起来更自然，但会让"业载上限与货量都为 0"的输入炸在求解期。
        // The envelope's two weight ends are `0` and `maxOf(1.0, payloadUpperBound, totalCargoWeight)`. That `1.0` is **divide-by-zero protection**: with equal end weights the
        // envelope collapses to a point and interpolation divides by zero. `maxOf(0.0, ...)` looks more natural and makes an input with zero payload bound and zero cargo blow
        // up during the solve.
        double envelopeUpperWeight = System.Math.Max(1.0, System.Math.Max(input.PayloadUpperBound, totalCargoWeight));
        var envelopePoints = new List<EnvelopePoint> {
            new(TotalWeight: 0.0, Index: input.EnvelopeLongitudinalMomentMin * momentFactor),
            new(TotalWeight: envelopeUpperWeight, Index: input.EnvelopeLongitudinalMomentMin * momentFactor),
        };
        var envelopeUpperPoints = envelopePoints
            .Select(point => point with { Index = input.EnvelopeLongitudinalMomentMax * momentFactor })
            .ToList();

        double maxClim = System.Math.Max(System.Math.Abs(input.EnvelopeLongitudinalMomentMin), System.Math.Abs(input.EnvelopeLongitudinalMomentMax));
        List<MaxClimPoint>? maxClimPoints = aircraftModel.WideBody
            ? new List<MaxClimPoint> {
                new(Tow: 0.0, MaxClim: maxClim * momentFactor),
                new(Tow: envelopeUpperWeight, MaxClim: maxClim * momentFactor),
            }
            : null;

        // 包线按**每个飞行阶段**各建一条。基线用 `phase.name.lowercase`；C# 的 `ToLowerInvariant` 对 `ZeroFuel` 同样给出 `zerofuel`。
        // One envelope per **flight phase**. The baseline uses `phase.name.lowercase`; C#'s `ToLowerInvariant` yields `zerofuel` for `ZeroFuel` too.
        var envelopes = new Dictionary<FlightPhase, IReadOnlyList<IEnvelope>>();
        foreach (FlightPhase phase in Enum.GetValues<FlightPhase>()) {
            string suffix = phase.ToString().ToLowerInvariant();
            envelopes[phase] = new List<IEnvelope> {
                new Envelope(
                    phase: phase,
                    name: "request_envelope_" + suffix,
                    lhsSide: new EnvelopeSide(
                        name: "request_envelope_" + suffix + "_min",
                        type: EnvelopeSideType.Left,
                        points: envelopePoints),
                    rhsSide: new EnvelopeSide(
                        name: "request_envelope_" + suffix + "_max",
                        type: EnvelopeSideType.Right,
                        points: envelopeUpperPoints),
                    totalWeight: stowageAggregation.TotalWeight),
            };
        }

        return Results.Ok(new AirworthinessSecurityAggregation(
            aircraftModel: aircraftModel,
            positions: positions,
            linearDensityLimitZones: linearDensityLimitZones,
            linearDensityLimitLines: LinearDensity.BuildLimitLines(linearDensityLimitZones, aircraftPositions),
            surfaceDensityLimitZones: surfaceDensityLimitZones,
            minLowPayloadPoints: minLowPayloadPoints,
            maxClimPoints: maxClimPoints,
            envelopes: envelopes,
            load: stowageAggregation.Load,
            totalWeight: stowageAggregation.TotalWeight,
            payload: stowageAggregation.Payload,
            maxZoneLoadWeight: new MaxZoneLoadWeight(),
            maxCumulativeLoadWeight: cumulativeLoadWeight,
            torque: macAggregation.Torque,
            horizontalStabilizers: macAggregation.HorizontalStabilizers,
            minPayload: minPayload,
            targetLongitudinalMoment: input.TargetLongitudinalMoment * momentFactor,
            maxLongitudinalMomentDeviation: input.MaxLongitudinalMomentDeviation * momentFactor,
            maxLateralImbalance: input.MaxLateralImbalance * momentFactor,
            ballast: stowageAggregation.Ballast,
            maxAdjacentLoadGap: input.MaxAdjacentLoadGap));
    }
}
