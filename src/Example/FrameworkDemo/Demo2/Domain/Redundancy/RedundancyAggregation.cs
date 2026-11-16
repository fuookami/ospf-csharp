#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
// 命名空间 `Redundancy` 会遮蔽同名类型，所以类型走别名。
// The `Redundancy` namespace shadows the like-named type, so the type goes through an alias.
using RedundancyModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy.Model.Redundancy;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Redundancy;

/// <summary>
/// 冗余聚合 / The redundancy aggregation.
/// </summary>
/// <remarks>
/// **两个子模型**，注册时**先冗余、后实验纵向平衡**。
///
/// 顺序是行为：两者都会往同一个模型上写，先写的先占位，交换会让解在退化情形下不同。
///
/// **一处刻意的参数删减**：完整签名还会带上 `aircraftModel`、`flight`、`payload`，但两个子模型的 C# 实现在落地时
/// 只用到货物、舱位与装载量（`payload` 用于 `Redundancy` 的总容量，本仓的 `Redundancy` 直接从 `mlw` 求和），所以这里
/// **不接没有消费者的参数**。
///
/// **two sub-models**, registered **redundancy first, then the experimental longitudinal balance**. The order is behaviour — both write to the same model and
/// the first one takes its slots, so swapping them changes the solution in degenerate cases.
///
/// **One deliberate parameter omission**: the full signature also takes
/// (the code uses `payload` for `Redundancy`'s total capacity while this implementation sums `mlw` directly), so this implementation **does not accept parameters with no consumer**.
/// </remarks>
public sealed class RedundancyAggregation
{
    /// <summary>
    /// 构造聚合 / Construct the aggregation.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    public RedundancyAggregation(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        Load load)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(load);
        Redundancy = new RedundancyModel(items, positions, load);
        ExperimentalLongitudinalBalance = new ExperimentalLongitudinalBalance(positions, load);
    }

    /// <summary>冗余 / The redundancy</summary>
    public RedundancyModel Redundancy { get; }

    /// <summary>实验纵向平衡 / The experimental longitudinal balance</summary>
    public ExperimentalLongitudinalBalance ExperimentalLongitudinalBalance { get; }

    /// <summary>
    /// 注册到模型 / Register with the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// **先冗余、后纵向平衡**。
    ///
    /// **Redundancy first, then the balance**, as in the code.
    /// </remarks>
    public Try Register(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Try redundancy = Redundancy.Register(model);
        if (!redundancy.IsOk)
        {
            return redundancy;
        }
        return ExperimentalLongitudinalBalance.Register(model);
    }
    /// <summary>Benders 主问题注册（等价于完整注册）/ Benders master-problem registration (equivalent to the full registration).</summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try RegisterForBendersMP(StowageMode stowageMode, LinearMetaModel<Flt64> model) =>
        Register(model);

    /// <summary>Benders 子问题注册（空实现）/ Benders sub-problem registration (no-op).</summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="solution">主问题解 / The master-problem solution</param>
    /// <returns>执行结果 / Execution result</returns>
    /// <remarks>冗余的目标全在主问题；子问题没有额外约束。 / Every redundancy objective lives in the master problem; the sub-problem gets nothing.</remarks>
    public Try RegisterForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<double> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>Benders 子问题刷新（空实现）/ Benders sub-problem flush (no-op).</summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="solution">主问题解 / The master-problem solution</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try FlushForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<double> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
