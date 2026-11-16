#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Service.Limits;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness;

/// <summary>
/// 装载效能聚合 / The loading-effectiveness aggregation.
/// </summary>
/// <remarks>
/// 只实现**基础部分**：相邻舱位对、有序舱位对、来源与目的地集合、
/// 以及 `PriorityOrderLimit` / `SourceEarlyLimit` 所需的两项派生量。
///
/// **这里只落地不依赖 `IfFunction` 的部分**。五个子模型里有四个（`SequentialLoading`、
/// `TransferAdjacentLoading`、`TrailerLoading`、`AdviceLoading` 的松弛项）都建立在
/// `IfFunction(… − 2 + ε)` 之上，而本仓的 `IfFunction.Create` 对"带单项式且带非零常数"的条件一律拒绝——
/// 与 、 同根。那四个模型连同依赖它们的十条限制留给后续批次，**不以恒零符号顶替**。
///
/// Aligned with its **foundation**: adjacent position pairs, ordered position pairs, the source and destination sets, and the two derived quantities that `PriorityOrderLimit`
/// and `SourceEarlyLimit` need. ** lands only the `IfFunction`-free part.** Four of its five sub-models (`SequentialLoading`, `TransferAdjacentLoading`,
/// `TrailerLoading`, and `AdviceLoading`'s slack terms) all rest on `IfFunction(… − 2 + ε)`, which this implementation's `IfFunction.Create` rejects for any condition carrying both a monomial
/// and a non-zero constant — the same root. Those four models and the ten limits that depend on them are left to later batches, **not substituted with constant-zero
/// symbols**.
/// </remarks>
public sealed class LoadingEffectivenessAggregation
{
    /// <summary>
    /// 构造聚合 / Construct the aggregation.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">配载矩阵 / The stowage matrix.</param>
    /// <param name="trailers">拖车 / The trailers.</param>
    /// <param name="flight">航班 / The flight.</param>
    /// <param name="cargosBySource">来源到货物下标的映射 / The source-to-item-index map.</param>
    /// <param name="load">装载模型，标记装配载 / The load model, from the stowage aggregation.</param>
    public LoadingEffectivenessAggregation(
        StowageMode stowageMode,
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        StowageModel stowage,
        IReadOnlyList<Trailer>? trailers = null,
        Flight? flight = null,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? cargosBySource = null,
        Load? load = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);
        StowageMode = stowageMode;
        Items = items;
        Positions = positions;
        Stowage = stowage;
        Trailers = trailers ?? Array.Empty<Trailer>();
        Flight = flight;
        CargosBySource = cargosBySource
            ?? new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        Load = load;

        var sources = new List<string>();
        var seenSources = new HashSet<string>(StringComparer.Ordinal);
        foreach (Item item in items)
        {
            if (item.Source is { } source && seenSources.Add(source))
            {
                sources.Add(source);
            }
        }
        Sources = sources;
        Destinations = DestinationsOf(items, flight);
        AdjacentPositions = CombineAdjacentByLoadingOrder(positions);
        OrderedPositions = CombineByLoadingOrder(positions);
        // **`(positions.size − 1).coerceAtLeast(0) / 2`**，整数除法，且空列表时为 0。
        // **`(positions.size − 1).coerceAtLeast(0) / 2`**, an integer division, and zero for an empty list.
        EarlyEnd = System.Math.Max(positions.Count - 1, 0) / 2;
        BigM = positions.Count;
    }

    /// <summary>
    /// 装载模型 / The load model.
    /// </summary>
    /// <remarks>
    /// **可空**：为 `null` 时 <see cref="Register"/> 无法派生子模型，只能产出不依赖子模型的那几条管线。
    /// 真实路径上它总是由 `StowageAggregation.Load` 传入。
    ///
    /// **Nullable**: when null, <see cref="Register"/> cannot derive sub-models and yields only the pipelines that need none. On the real path it always comes from
    /// `StowageAggregation.Load`.
    /// </remarks>
    public Load? Load { get; }

    /// <summary>装载模式 / The stowage mode</summary>
    public StowageMode StowageMode { get; }

    /// <summary>货物 / The items</summary>
    public IReadOnlyList<Item> Items { get; }

    /// <summary>舱位 / The positions</summary>
    public IReadOnlyList<StowagePosition> Positions { get; }

    /// <summary>配载矩阵 / The stowage matrix</summary>
    public StowageModel Stowage { get; }

    /// <summary>拖车 / The trailers</summary>
    public IReadOnlyList<Trailer> Trailers { get; }

    /// <summary>航班 / The flight</summary>
    public Flight? Flight { get; }

    /// <summary>来源到货物下标的映射 / The source-to-item-index map</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<int>> CargosBySource { get; }

    /// <summary>来源集合 / The source set</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>目的地集合（不含航班到达站）/ The destination set excluding the flight's arrival</summary>
    public IReadOnlyList<string> Destinations { get; }

    /// <summary>相邻舱位对 / The adjacent position pairs</summary>
    public IReadOnlyList<(StowagePosition First, StowagePosition Second)> AdjacentPositions { get; }

    /// <summary>全部有序舱位对 / Every ordered position pair</summary>
    public IReadOnlyList<(StowagePosition First, StowagePosition Second)> OrderedPositions { get; }

    /// <summary>前段舱位的末位下标 / The index of the last early position</summary>
    public int EarlyEnd { get; }

    /// <summary>位置的 Big-M 上界 / The Big-M upper bound over positions</summary>
    public int BigM { get; }

    /// <summary>相邻装载指示（无条件建）/ The adjacency indicators (always built)</summary>
    /// <remarks>
    /// **无条件**构造它，与装载模式无关；其余三个子模型都是按模式二选一。
    ///
    /// Built **unconditionally**, independent of stowage mode; the other three sub-models are chosen by mode.
    /// </remarks>
    public TransferAdjacentLoading? TransferAdjacentLoading { get; private set; }

    /// <summary>建议装载松弛（仅预配载）/ The advised-load slack (predistribution only)</summary>
    public AdviceLoading? AdviceLoading { get; private set; }

    /// <summary>顺序装载反转（仅预配载）/ The sequential-loading reversals (predistribution only)</summary>
    public SequentialLoading? SequentialLoading { get; private set; }

    /// <summary>拖车指示（仅全载）/ The trailer indicators (full load only)</summary>
    public TrailerLoading? TrailerLoading { get; private set; }

    /// <summary>
    /// 派出四个子模型 / Derive the four sub-models.
    /// </summary>
    /// <remarks>
    /// 按 `stowageMode` 分派：**`TransferAdjacentLoading` 无条件**，
    /// `AdviceLoading` 与 `SequentialLoading` 只在预配载，`TrailerLoading` 只在全载。建议打板三种模型全空。
    ///
    /// Dispatch on `stowageMode`: **`TransferAdjacentLoading` unconditionally**, `AdviceLoading` and `SequentialLoading` only under predistribution, `TrailerLoading` only
    /// under full load, with weight recommendation leaving all three null.
    /// </remarks>
    /// <param name="load">装载模型 / The load model.</param>
    public void DeriveSubModels(Load load)
    {
        ArgumentNullException.ThrowIfNull(load);
        TransferAdjacentLoading = new TransferAdjacentLoading(
            AdjacentPositions, Sources, Destinations, load);
        AdviceLoading = StowageMode == StowageMode.Predistribution
            ? new AdviceLoading(Positions, load)
            : null;
        SequentialLoading = StowageMode == StowageMode.Predistribution
            ? new SequentialLoading(Items, Positions, OrderedPositions, Stowage)
            : null;
        TrailerLoading = StowageMode == StowageMode.FullLoad
            ? new TrailerLoading(Items, Positions, AdjacentPositions, Trailers, Stowage, load)
            : null;
    }

    /// <summary>
    /// 注册基础管线 / Register the foundation pipelines.
    /// </summary>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="parameter">参数 / The parameter.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(StowageMode stowageMode, Parameter parameter, LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        ArgumentNullException.ThrowIfNull(model);

        // **先派生四个子模型**：`Aggregation.register` 自己建 `load` 再建子模型，两步都在生成管线之前。
        // **Derive the four sub-models first**: its `Aggregation.register` builds `load` and then the sub-models, both before generating the pipelines.
        if (Load is { } load)
        {
            if (TransferAdjacentLoading is null)
            {
                Try registered = load.Register(model);
                if (!registered.IsOk)
                {
                    return registered;
                }
                DeriveSubModels(load);
            }

            // **派生只建符号对象，还要注册到模型上**——否则管线去查指示会查不到，报"未注册"。
            // **Deriving only builds the symbol objects; they must also be registered** — otherwise the pipelines look them up and find nothing.
            if (TransferAdjacentLoading is { } adjacency)
            {
                Try adjacencyRegistered = adjacency.Register(model, Positions);
                if (!adjacencyRegistered.IsOk)
                {
                    return adjacencyRegistered;
                }
            }
            if (AdviceLoading is { } advice)
            {
                Try adviceRegistered = advice.Register(model);
                if (!adviceRegistered.IsOk)
                {
                    return adviceRegistered;
                }
            }
            if (SequentialLoading is { } sequential)
            {
                Try sequentialRegistered = sequential.Register(model);
                if (!sequentialRegistered.IsOk)
                {
                    return sequentialRegistered;
                }
            }
            if (TrailerLoading is { } trailers)
            {
                Try trailersRegistered = trailers.Register(model);
                if (!trailersRegistered.IsOk)
                {
                    return trailersRegistered;
                }
            }
        }

        // **走生成器**：条件与顺序集中在一处，`Register` 不再重复判断。
        // **Goes through the generator**: conditions and order live in one place and `Register` does not re-decide them.
        Result<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> generated =
            new Service.PipelineListGenerator(this).Invoke(stowageMode, parameter);
        if (generated is Failed<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>> failed)
        {
            return new Failed<Success, ErrorCode, Error<ErrorCode>>(failed.Error);
        }
        IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>> pipelines =
            ((Ok<IReadOnlyList<Func<LinearMetaModel<Flt64>, Try>>, ErrorCode, Error<ErrorCode>>)generated).Value!;
        foreach (Func<LinearMetaModel<Flt64>, Try> pipeline in pipelines)
        {
            Try applied = pipeline(model);
            if (!applied.IsOk)
            {
                return applied;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 相邻舱位对：按装载顺序排序后两两相邻 / Adjacent pairs: consecutive entries after sorting by loading order.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <returns>相邻对 / The adjacent pairs.</returns>
    private static List<(StowagePosition First, StowagePosition Second)> CombineAdjacentByLoadingOrder(
        IReadOnlyList<StowagePosition> positions)
    {
        List<StowagePosition> sorted = StableSortByOrder(positions);
        var pairs = new List<(StowagePosition First, StowagePosition Second)>();
        for (int index = 0; index + 1 < sorted.Count; ++index)
        {
            pairs.Add((sorted[index], sorted[index + 1]));
        }
        return pairs;
    }

    /// <summary>
    /// 全部有序舱位对 / Every ordered position pair.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <returns>有序对 / The ordered pairs.</returns>
    private static List<(StowagePosition First, StowagePosition Second)> CombineByLoadingOrder(
        IReadOnlyList<StowagePosition> positions)
    {
        List<StowagePosition> sorted = StableSortByOrder(positions);
        var pairs = new List<(StowagePosition First, StowagePosition Second)>();
        for (int index = 0; index < sorted.Count; ++index)
        {
            for (int other = index + 1; other < sorted.Count; ++other)
            {
                pairs.Add((sorted[index], sorted[other]));
            }
        }
        return pairs;
    }

    /// <summary>
    /// 按装载顺序稳定排序 / Stable sort by loading order.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <returns>已排序副本 / The sorted copy.</returns>
    private static List<StowagePosition> StableSortByOrder(IReadOnlyList<StowagePosition> positions)
    {
        var indexed = new List<(StowagePosition Position, int Original)>(positions.Count);
        for (int index = 0; index < positions.Count; ++index)
        {
            indexed.Add((positions[index], index));
        }
        // **`sortedBy` 是稳定的，`List.Sort` 不是**，所以用原下标作次键。
        // **`sortedBy` is stable while `List.Sort` is not**, so the original index breaks ties.
        indexed.Sort((left, right) =>
        {
            int byOrder = left.Position.AircraftPosition.LinearLoadingOrder
                .CompareTo(right.Position.AircraftPosition.LinearLoadingOrder);
            return byOrder != 0 ? byOrder : left.Original.CompareTo(right.Original);
        });
        var sorted = new List<StowagePosition>(indexed.Count);
        foreach ((StowagePosition position, int _) in indexed)
        {
            sorted.Add(position);
        }
        return sorted;
    }

    /// <summary>
    /// 目的地集合：排除航班到达站 / The destination set, excluding the flight's arrival.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="flight">航班 / The flight.</param>
    /// <returns>目的地 / The destinations.</returns>
    private static List<string> DestinationsOf(IReadOnlyList<Item> items, Flight? flight)
    {
        var destinations = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Item item in items)
        {
            if (item.Destination is not { } destination)
            {
                continue;
            }
            // **到达站不计入目的地**（`if (it.destination != flight.arrival)`）。
            // **The arrival is not a destination**, as in the code.
            if (flight is not null && string.Equals(destination, flight.Destination.Code, StringComparison.Ordinal))
            {
                continue;
            }
            if (seen.Add(destination))
            {
                destinations.Add(destination);
            }
        }
        return destinations;
    }
    /// <summary>Benders 主问题注册（等价于完整注册）/ Benders master-problem registration (equivalent to the full registration).</summary>
    /// <param name="stowageMode">配载模式 / Stowage mode</param>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <returns>执行结果 / Execution result</returns>
    /// <remarks>
    /// "装卸效率的约束进主问题"，实现就是 `register(...)`。
    /// "Loading effectiveness constraints go into the master problem" — the implementation is `register(...)`.
    /// </remarks>
    public Try RegisterForBendersMP(StowageMode stowageMode, LinearMetaModel<Flt64> model) =>
        Register(stowageMode, new Parameter(), model);

    /// <summary>Benders 子问题注册（空实现）/ Benders sub-problem registration (no-op).</summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="solution">主问题解 / The master-problem solution</param>
    /// <returns>执行结果 / Execution result</returns>
    /// <remarks>"装卸效率不参与子问题"。 / "Loading effectiveness does not contribute to the sub problem".</remarks>
    public Try RegisterForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<double> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);

    /// <summary>Benders 子问题刷新（空实现）/ Benders sub-problem flush (no-op).</summary>
    /// <param name="model">线性元模型 / Linear meta model</param>
    /// <param name="solution">主问题解 / The master-problem solution</param>
    /// <returns>执行结果 / Execution result</returns>
    public Try FlushForBendersSP(LinearMetaModel<Flt64> model, IReadOnlyList<double> solution) =>
        Results.Ok<Success>(Results.SuccessInstance);
}
