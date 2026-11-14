#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;

/// <summary>
/// 配载域聚合 / The stowage domain aggregation.
/// </summary>
/// <remarks>
/// 与基线 `stowage/Aggregation.kt` 对齐：构造时建好 5 个派生模型（配重按机型可空），`Register` 按基线顺序注册。
///
/// **三条注册路径的顺序互不相同，这是最容易做错的地方**：
///
/// | 路径 | 顺序 |
/// | --- | --- |
/// | `Register` | 装载 → 载荷 → 业载 → 总重 → 最大装载重量 |
/// | `RegisterForBendersMP` | 装载 → 载荷 → 业载（**到此为止**） |
/// | `RegisterForBendersSP` | 业载 → 总重 → 最大装载重量（**没有装载与载荷**） |
///
/// MP 不注册总重与最大装载重量、SP 不注册装载与载荷——看起来像漏了，但基线就是如此。把三者写成同一个方法会出两种故障：
/// MP 多算两族符号；SP 重复注册装载变量（`Stowage.Register` 对同一对象只允许一次，会直接失败）。
///
/// Aligned with the baseline: five derived models are built in the constructor (the ballast one nullable by aircraft type) and `Register` follows the baseline's order.
/// **The three registration paths differ, which is where this is easiest to get wrong**, tabulated above. Collapsing them into one method causes two failures: the master
/// computes two extra symbol families, and the sub-problem re-registers the stowage variables (`Stowage.Register` allows one registration per object and fails).
/// </remarks>
public sealed class StowageAggregation
{
    private readonly Formula? _formula;

    /// <summary>
    /// 构造配载聚合 / Construct the stowage aggregation.
    /// </summary>
    /// <param name="aircraftModel">飞机型号 / The aircraft model.</param>
    /// <param name="flight">航班 / The flight.</param>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="appointment">约定 / The appointment.</param>
    /// <param name="biologicalLimit">生物限制 / The biological limit.</param>
    /// <param name="neighbours">邻接关系 / The neighbours.</param>
    /// <param name="plannedPayload">计划业载 / The planned payload.</param>
    /// <param name="maxPayload">最大业载 / The maximum payload.</param>
    /// <param name="withMultiLoadingSchema">是否允许多装载方案 / Whether multiple loading schemas are allowed.</param>
    /// <param name="formula">力矩公式 / The moment formula.</param>
    /// <param name="fuselage">机身 / The fuselage.</param>
    /// <param name="fuel">燃油常量 / The fuel constants.</param>
    /// <param name="computedPayload">已定业载 / The already-decided payload.</param>
    /// <param name="maxTotalWeight">最大总重 / The maximum total weight.</param>
    /// <param name="computedTotalWeight">已定总重 / The already-decided total weight.</param>
    public StowageAggregation(
        AircraftModel aircraftModel,
        Flight flight,
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        Appointment appointment,
        BiologicalLimit biologicalLimit,
        IReadOnlyDictionary<NeighbourType, IReadOnlyList<Neighbour>> neighbours,
        double plannedPayload,
        double maxPayload,
        bool withMultiLoadingSchema,
        Formula? formula = null,
        Fuselage? fuselage = null,
        IReadOnlyDictionary<FlightPhase, FuelConstant>? fuel = null,
        double? computedPayload = null,
        IReadOnlyDictionary<FlightPhase, double>? maxTotalWeight = null,
        IReadOnlyDictionary<FlightPhase, double>? computedTotalWeight = null) {
        ArgumentNullException.ThrowIfNull(aircraftModel);
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(biologicalLimit);
        ArgumentNullException.ThrowIfNull(neighbours);
        ArgumentNullException.ThrowIfNull(fuselage);

        AircraftModel = aircraftModel;
        Flight = flight;
        Items = items;
        Positions = positions;
        Appointment = appointment;
        BiologicalLimit = biologicalLimit;
        Neighbours = neighbours;
        WithMultiLoadingSchema = withMultiLoadingSchema;
        _formula = formula;

        Stowage = new StowageModel(items, positions);
        Load = new Load(items, positions, Stowage, withMultiLoadingSchema, aircraftModel, formula);
        Payload = new Payload(positions, Load, plannedPayload, maxPayload, computedPayload);
        TotalWeight = new TotalWeight(fuselage, fuel ?? new Dictionary<FlightPhase, FuelConstant>(), Payload, maxTotalWeight, computedTotalWeight);
        MaxLoadWeight = new MaxLoadWeight(positions, TotalWeight);

        // 配重只在机型需要时建。`BallastNeeded` 是**派生量**（窄体机不需要），不是配置项——照着配置项的直觉写会多出/少掉一个模型。
        // The ballast model exists only when the type needs it. `BallastNeeded` is a **derived** fact (narrow bodies do not need it), not a configuration knob; treating it as a
        // knob adds or drops a model.
        if (aircraftModel.BallastNeeded) {
            Ballast = new Ballast(aircraftModel, positions, null, Load);
        }
    }

    /// <summary>飞机型号 / The aircraft model</summary>
    public AircraftModel AircraftModel { get; }

    /// <summary>航班 / The flight</summary>
    public Flight Flight { get; }

    /// <summary>货物 / The items</summary>
    public IReadOnlyList<Item> Items { get; }

    /// <summary>舱位 / The positions</summary>
    public IReadOnlyList<StowagePosition> Positions { get; }

    /// <summary>约定 / The appointment</summary>
    public Appointment Appointment { get; }

    /// <summary>生物限制 / The biological limit</summary>
    public BiologicalLimit BiologicalLimit { get; }

    /// <summary>邻接关系 / The neighbours</summary>
    public IReadOnlyDictionary<NeighbourType, IReadOnlyList<Neighbour>> Neighbours { get; }

    /// <summary>是否允许多装载方案 / Whether multiple loading schemas are allowed</summary>
    public bool WithMultiLoadingSchema { get; }

    /// <summary>装载模型 / The stowage model</summary>
    public StowageModel Stowage { get; }

    /// <summary>载荷模型 / The load model</summary>
    public Load Load { get; }

    /// <summary>业载模型 / The payload model</summary>
    public Payload Payload { get; }

    /// <summary>总重模型 / The total weight model</summary>
    public TotalWeight TotalWeight { get; }

    /// <summary>最大装载重量 / The maximum load weight</summary>
    public MaxLoadWeight MaxLoadWeight { get; }

    /// <summary>配重模型；机型不需要时为 `null` / The ballast model, `null` when the type does not need it</summary>
    public Ballast? Ballast { get; }

    /// <summary>力矩公式（供调用方读取）/ The moment formula, for callers</summary>
    public Formula? Formula => _formula;

    /// <summary>
    /// 注册全部子模型 / Register every sub-model.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        Try stowage = Stowage.Register(model);
        if (!stowage.IsOk) {
            return stowage;
        }
        Try load = Load.Register(model);
        if (!load.IsOk) {
            return load;
        }
        Try payload = Payload.Register(stowageMode, model);
        if (!payload.IsOk) {
            return payload;
        }
        Try totalWeight = TotalWeight.Register(model);
        if (!totalWeight.IsOk) {
            return totalWeight;
        }
        return MaxLoadWeight.Register(model);
    }

    /// <summary>
    /// Benders 主问题注册（**不含总重与最大装载重量**）/ Benders master-problem registration (**without** the total weight and the maximum load weight).
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try RegisterForBendersMP(StowageMode stowageMode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        Try stowage = Stowage.Register(model);
        if (!stowage.IsOk) {
            return stowage;
        }
        Try load = Load.Register(model);
        if (!load.IsOk) {
            return load;
        }
        return Payload.Register(stowageMode, model);
    }

    /// <summary>
    /// Benders 子问题注册（**不含装载与载荷**）/ Benders sub-problem registration (**without** the stowage and the load).
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try RegisterForBendersSP(StowageMode stowageMode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        Try payload = Payload.Register(stowageMode, model);
        if (!payload.IsOk) {
            return payload;
        }
        Try totalWeight = TotalWeight.Register(model);
        if (!totalWeight.IsOk) {
            return totalWeight;
        }
        return MaxLoadWeight.Register(model);
    }

    /// <summary>
    /// Benders **单模型**路径的追索注册：业载已在模型里，只补总重与最大装载重量。
    /// The recourse registration for the **single-model** Benders path: the payload is already in the model; only the weight families are added.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 基线是**一个模型 + 阶段表**（`DiscreteLoadingApplicationService.kt:90`），所以业载只能注册一次。
    /// `RegisterForBendersSP` 会在同一个模型上再注册一次业载，而 `Register` 对同一对象只允许一次——**那是两条路径，不是一条**。
    /// 这里把"业载已注册"当**前提**写进签名（没有 payload 参数，不猜、不跳过），只补总重与最大装载重量。
    /// **调用方必须先跑 `RegisterForBendersMP`**，否则模型里没有业载，重量符号引用不到东西。
    ///
    /// The baseline is **one model plus a stage table** (`DiscreteLoadingApplicationService.kt:90`), so the payload can be registered only once. `RegisterForBendersSP`
    /// registers it again on the same model, and `Register` allows one registration per object — **those are two paths, not one**. This puts "the payload is already
    /// registered" into the signature as a **premise** (no payload argument, no guessing, no skipping) and adds only the weight families. **The caller must run
    /// `RegisterForBendersMP` first**, otherwise the model has no payload and the weight symbols reference nothing.
    /// </remarks>
    public Try RegisterForBendersSPWithoutPayload(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        Try totalWeight = TotalWeight.Register(model);
        if (!totalWeight.IsOk) {
            return totalWeight;
        }
        return MaxLoadWeight.Register(model);
    }
}
