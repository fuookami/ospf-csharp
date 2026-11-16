#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity;

/// <summary>
/// 软性安全聚合根 / The soft-security aggregation.
/// </summary>
/// <remarks>
/// 七个上游模型 + 一个自建的 `DivideEmptyLoading`。
///
/// 三处必须沿用：
///
/// 1. **`DivideEmptyLoading` 在构造时就建好**，相邻对由舱位表推出（按甲板分组、组内按装载顺序排序、取相邻）。
/// 2. **`Register` 只注册分隔空载的符号**，不跑任何管线——管线由 `PipelineListGenerator` 组装，由上下文执行。
/// 3. **`RegisterForBendersMP` 就是 `Register`**（软安全约束属于主问题）；**`RegisterForBendersSP` 是空操作**——
///    软安全不向子问题贡献任何东西。
///
/// Seven upstream models plus a self-built `DivideEmptyLoading`. Three things must be copied: **`DivideEmptyLoading` is built at construction**, its
/// adjacent pairs derived from the position table (grouped by deck, sorted by loading order, consecutive); **`Register` only registers the divide-empty symbols** and runs no
/// pipeline, since `PipelineListGenerator` assembles them and the context executes them; and **`RegisterForBendersMP` is `Register`** (soft-security constraints belong to the
/// master problem) while **`RegisterForBendersSP` is a no-op** — soft security contributes nothing to the sub-problem.
/// </remarks>
public sealed class SoftSecurityAggregation
{
    /// <summary>飞机型号 / The aircraft model</summary>
    public AircraftModel AircraftModel { get; }

    /// <summary>主甲板 / The main deck</summary>
    public Deck MainDeck { get; }

    /// <summary>货物 / The items</summary>
    public IReadOnlyList<Item> Items { get; }

    /// <summary>舱位 / The positions</summary>
    public IReadOnlyList<StowagePosition> Positions { get; }

    /// <summary>装载模型 / The stowage model</summary>
    public Stowage.Model.Stowage Stowage { get; }

    /// <summary>装载量模型 / The load model</summary>
    public Load Load { get; }

    /// <summary>压舱，未启用时为 `null` / The ballast, `null` when disabled</summary>
    public Ballast? Ballast { get; }

    /// <summary>分隔空载模型 / The divide-empty-loading model</summary>
    public DivideEmptyLoading DivideEmptyLoading { get; }

    /// <summary>
    /// 构造软性安全聚合 / Construct the soft-security aggregation.
    /// </summary>
    /// <param name="aircraftModel">飞机型号 / The aircraft model.</param>
    /// <param name="mainDeck">主甲板 / The main deck.</param>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">装载模型 / The stowage model.</param>
    /// <param name="load">装载量模型 / The load model.</param>
    /// <param name="ballast">压舱 / The ballast.</param>
    public SoftSecurityAggregation(
        AircraftModel aircraftModel,
        Deck mainDeck,
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        Stowage.Model.Stowage stowage,
        Load load,
        Ballast? ballast)
    {
        ArgumentNullException.ThrowIfNull(aircraftModel);
        ArgumentNullException.ThrowIfNull(mainDeck);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);
        ArgumentNullException.ThrowIfNull(load);
        AircraftModel = aircraftModel;
        MainDeck = mainDeck;
        Items = items;
        Positions = positions;
        Stowage = stowage;
        Load = load;
        Ballast = ballast;
        DivideEmptyLoading = new DivideEmptyLoading(
            positions,
            DivideEmptyLoading.BuildAdjacentPositions(positions),
            load,
            items);
    }

    /// <summary>
    /// 注册分隔空载符号 / Register the divide-empty symbols.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(StowageMode stowageMode, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return DivideEmptyLoading.Register(model);
    }

    /// <summary>
    /// 注册 Benders 主问题约束 / Register the Benders master-problem constraints.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try RegisterForBendersMP(StowageMode stowageMode, LinearMetaModel<Flt64> model) =>
        Register(stowageMode, model);

    /// <summary>
    /// 注册 Benders 子问题约束 / Register the Benders sub-problem constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="solution">子问题解值 / The sub-problem solution.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **空操作**：软安全不向子问题贡献任何东西。写成 `Register` 会重复注册符号。
    ///
    /// **A no-op**: soft security contributes nothing to the sub-problem. Calling `Register` would register the symbols twice.
    /// </remarks>
    public Try RegisterForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<Flt64> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
