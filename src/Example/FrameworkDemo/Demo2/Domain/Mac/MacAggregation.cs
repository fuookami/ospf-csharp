#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;

/// <summary>
/// MAC 域聚合 / The MAC domain aggregation.
/// </summary>
/// <remarks>
/// 构造时建好力矩、MAC 与按配置索引的水平安定面，`Register` 依次注册三者。
///
/// **三条路径与配载域的不一样**（配载域是 MP 少注册、SP 换一套）：
///
/// | 路径 | 行为 |
/// | --- | --- |
/// | `Register` | 力矩 → MAC → 每个水平安定面 |
/// | `RegisterForBendersMP` | **与 `Register` 完全相同** |
/// | `RegisterForBendersSP` | **空操作**，什么都不注册 |
///
/// MP 与 `Register` 相同、SP 是空操作——这与配载域"MP 少两族、SP 换一套"正好相反。两个聚合的 Benders 语义**不一致**，
/// 而它们名字一样。按名字类推是这里最危险的事：照配载域的样子写会让 SP 注册一堆符号，或让 MP 少注册水平安定面。
///
/// Torque, MAC and the per-configuration horizontal stabilizers are built in the constructor and `Register` registers all three. **The three paths differ
/// from the stowage domain's** (there the master registers less and the sub-problem a different set), tabulated above: here the master is **identical to `Register`** and the
/// sub-problem is a **no-op**. The two aggregations' Benders semantics are **inconsistent** while their names match, which is the most dangerous thing about this file: copying
/// the stowage shape either makes the sub-problem register a pile of symbols or makes the master skip the horizontal stabilizers.
/// </remarks>
public sealed class MacAggregation
{
    /// <summary>
    /// 构造 MAC 聚合 / Construct the MAC aggregation.
    /// </summary>
    /// <param name="fuselage">机身 / The fuselage.</param>
    /// <param name="fuel">燃油常量 / The fuel constants.</param>
    /// <param name="formula">力矩公式 / The moment formula.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">载荷模型 / The load model.</param>
    /// <param name="totalWeight">总重模型 / The total weight model.</param>
    /// <param name="horizontalStabilizers">水平安定面数据（配置 → 点集与限制）/ The horizontal stabilizer data, keyed by configuration.</param>
    /// <param name="wideBody">是否宽体（决定是否注册 CLIM）/ Whether the aircraft is a wide body, which decides the CLIM symbol.</param>
    public MacAggregation(
        Fuselage fuselage,
        IReadOnlyDictionary<FlightPhase, FuelConstant> fuel,
        Formula formula,
        IReadOnlyList<StowagePosition> positions,
        Load load,
        TotalWeight totalWeight,
        IReadOnlyDictionary<HorizontalStabilizer.Key, (IReadOnlyList<HorizontalStabilizer.Point> Points, HorizontalStabilizer.Limit Limit)> horizontalStabilizers,
        bool wideBody) {
        ArgumentNullException.ThrowIfNull(fuselage);
        ArgumentNullException.ThrowIfNull(fuel);
        ArgumentNullException.ThrowIfNull(formula);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(totalWeight);
        ArgumentNullException.ThrowIfNull(horizontalStabilizers);

        Torque = new Torque(fuselage, fuel, formula, positions, load, wideBody);
        Mac = new MacModel(formula, totalWeight, Torque);
        HorizontalStabilizers = horizontalStabilizers.ToDictionary(
            pair => pair.Key,
            pair => new HorizontalStabilizer(pair.Key, pair.Value.Points, pair.Value.Limit));
    }

    /// <summary>力矩模型 / The torque model</summary>
    public Torque Torque { get; }

    /// <summary>MAC 模型 / The MAC model</summary>
    public MacModel Mac { get; }

    /// <summary>水平安定面 / The horizontal stabilizers</summary>
    public IReadOnlyDictionary<HorizontalStabilizer.Key, HorizontalStabilizer> HorizontalStabilizers { get; }

    /// <summary>
    /// 注册力矩、MAC 与水平安定面 / Register the torque, the MAC and the horizontal stabilizers.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// `stowageMode` 在这条路径上**没有被用到**——`register` 收它，只往下传给水平安定面；而 C# 侧的 `HorizontalStabilizer.Register`
    /// 没有这个参数（`stowageMode` 影响的是限值约束，不在这里）。保留参数是为了签名一致。
    ///
    /// `stowageMode` is **unused** on this path: the code accepts it and forwards it to the horizontal stabilizers, while the C# `HorizontalStabilizer.Register` has no such
    /// parameter (the mode affects the limit constraints, which live elsewhere). The parameter stays for signature parity.
    /// </remarks>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        _ = stowageMode;

        Try torque = Torque.Register(model);
        if (!torque.IsOk) {
            return torque;
        }
        Try mac = Mac.Register(model);
        if (!mac.IsOk) {
            return mac;
        }
        foreach (HorizontalStabilizer stabilizer in HorizontalStabilizers.Values) {
            Try added = stabilizer.Register(stowageMode, model);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// Benders 主问题注册（**与 <see cref="Register"/> 相同**）/ Benders master-problem registration (**identical to** <see cref="Register"/>).
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try RegisterForBendersMP(StowageMode stowageMode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return Register(stowageMode, model);
    }

    /// <summary>
    /// Benders 子问题注册（**空操作**）/ Benders sub-problem registration (**a no-op**).
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 这里就是 `return ok`。**这与配载域相反**：那边的 SP 会注册业载/总重/最大装载重量。看起来该对称，但两个域在就是不一样。
    ///
    /// This is `return ok`. **This is the opposite of the stowage domain**, whose sub-problem does register the payload, total weight and maximum load weight. It looks
    /// like it should be symmetric; in practice it is not.
    /// </remarks>
    public Try RegisterForBendersSP(StowageMode stowageMode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        _ = stowageMode;
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
