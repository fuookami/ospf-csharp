#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service;

/// <summary>
/// 软性安全管线列表生成器 / The soft-security pipeline list generator.
/// </summary>
/// <remarks>
/// **四个条件，四个不同判据**：
///
/// | 管线 | 条件 |
/// | --- | --- |
/// | `AdviceBallastWeightLimit` | `ballast != null` |
/// | `EmptyHatedLimit` | **全配载**模式 **且** 机型是 **** **且** 主舱货 ≥ **5** 件 |
/// | `MainDeckDoorEmptyLimit` | `aircraftModel.mainDeckDoorEmptyPrefer` |
/// | `DivideEmptyLoadingLimit` | 无条件 |
///
/// 三处必须沿用：
///
/// 1. **`withSoftSecurity` 为假时直接返回空表**，一条管线都不加。这不是"优化"，而是软安全在这一模式下**整体关闭**。
/// 2. **`EmptyHatedLimit` 的三个条件缺一不可**：全配载、主舱货 ≥ 5。只写"全配载"会让 也加这条目标；
///    阈值写成 `> 5` 会漏掉正好 5 件的情形（`>= 5`）。
/// 3. **`MainDeckDoorEmptyLimit` 的权重是分类阶梯**（空板 1 / 非空板 2 / 箱 5 / 其它 0），再乘 `parameter.besideDoorMainPosition`。
///    把阶梯写成常数会让"舱门旁放空板"与"放箱"代价相同，整个偏好失效。**三个条件互斥，顺序不影响结果**（见 `DoorWeight`）。
///
/// **four conditions, four distinct tests**, tabulated above. Three things must be copied: **a false `withSoftSecurity` returns an empty list**
/// immediately — not an optimisation but soft security being **switched off entirely** in that mode; **`EmptyHatedLimit` needs all three conditions** — full-load, and at least
/// five main-deck items, since testing only "full-load" adds the objective for a too, and `> 5` would miss exactly five (this implementation uses `>= 5`); and
/// **`MainDeckDoorEmptyLimit`'s weight is a class ladder** (empty pallet 1, non-empty pallet 2, container 5, otherwise 0) multiplied by `parameter.besideDoorMainPosition`, since
/// collapsing the ladder to a constant makes "an empty pallet by the door" cost the same as "a container", disabling the preference.
/// </remarks>
internal sealed class PipelineListGenerator
{
    private readonly SoftSecurityAggregation _aggregation;

    /// <summary>
    /// 构造生成器 / Construct the generator.
    /// </summary>
    /// <param name="aggregation">软性安全聚合 / The soft-security aggregation.</param>
    public PipelineListGenerator(SoftSecurityAggregation aggregation)
    {
        ArgumentNullException.ThrowIfNull(aggregation);
        _aggregation = aggregation;
    }

    /// <summary>
    /// 生成管线列表 / Generate the pipeline list.
    /// </summary>
    /// <param name="stowageMode">配载模式 / The stowage mode.</param>
    /// <param name="parameter">参数 / The parameter.</param>
    /// <returns>管线列表 / The pipeline list.</returns>
    public Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> Invoke(
        StowageMode stowageMode,
        Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        var pipelines = new List<Func<LinearMetaModel<Flt64>, Try>>();
        if (!stowageMode.WithSoftSecurity())
        {
            return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(pipelines);
        }

        if (_aggregation.Ballast is { } ballast)
        {
            Flt64 ballastCoefficient = parameter.BallastWeight;
            var advice = new AdviceBallastWeightLimit(ballast, () => ballastCoefficient);
            pipelines.Add(advice.Invoke);
        }

        // 全配载 + + 主舱货 ≥ 5，三个条件缺一不可。
        // Full load, and at least five main-deck items — all three.
        if (stowageMode == StowageMode.FullLoad
            && _aggregation.AircraftModel.Type == AircraftType.B757
            && CountMainDeckItems() >= 5)
        {
            Flt64 emptyHated = parameter.EmptyHated;
            var limit = new EmptyHatedLimit(_aggregation.Positions, _aggregation.Load, _ => emptyHated);
            pipelines.Add(limit.Invoke);
        }

        if (_aggregation.AircraftModel.MainDeckDoorEmptyPrefer)
        {
            Flt64 besideDoor = parameter.BesideDoorMainPosition;
            var limit = new MainDeckDoorEmptyLimit(
                _aggregation.Items,
                _aggregation.Positions,
                _aggregation.MainDeck,
                _aggregation.Stowage,
                item => DoorWeight(item) * besideDoor);
            pipelines.Add(limit.Invoke);
        }

        Flt64 dividedEmpty = parameter.DividedEmpty;
        var divideEmpty = new DivideEmptyLoadingLimit(
            _aggregation.Positions,
            _aggregation.DivideEmptyLoading.AdjacentPositions,
            _aggregation.DivideEmptyLoading,
            (_, _) => dividedEmpty,
            (_, _) => dividedEmpty,
            (_, _) => dividedEmpty);
        pipelines.Add(divideEmpty.Invoke);

        return Results.Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>>(pipelines);
    }

    /// <summary>
    /// 舱门权重阶梯 / The door-weight ladder.
    /// </summary>
    /// <param name="item">货物 / The item.</param>
    /// <returns>权重 / The weight.</returns>
    /// <remarks>
    /// **三个条件互斥，顺序不影响结果**——实测：把箱那一条提到最前面，三件货的系数仍然是 1/2/5。
    /// 早先的注释写"顺序决定结果"是**错的**，是变异测试把它证伪的。互斥来自集装器类别本身：
    /// 一个 ULD 要么是板要么是箱，而"空/非空"又只在板这一类上分支。
    ///
    /// **The three conditions are mutually exclusive, so the order does not affect the result** — measured: hoisting the container case still gives coefficients 1/2/5. The earlier
    /// comment claiming "the order decides the result" was **wrong**, and a mutation disproved it. Exclusivity comes from the ULD categories themselves: a ULD is either a pallet or
    /// a container, and the empty/non-empty split only branches within the pallet case.
    /// </remarks>
    private static Flt64 DoorWeight(Item item)
    {
        bool empty = item.Contains(CargoCode.Empty);
        ULDCategory? category = item.Uld?.Category;
        if (empty && category == ULDCategory.Pallet)
        {
            return Flt64.One;
        }
        if (!empty && category == ULDCategory.Pallet)
        {
            return new Flt64(2.0);
        }
        if (category == ULDCategory.Container)
        {
            return new Flt64(5.0);
        }
        return Flt64.Zero;
    }

    private int CountMainDeckItems()
    {
        int count = 0;
        foreach (Item item in _aggregation.Items)
        {
            if (item.Location.IsMain)
            {
                ++count;
            }
        }
        return count;
    }
}
