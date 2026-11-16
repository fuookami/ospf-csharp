// 装载顺序约束 / The loading order rows.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;

/// <summary>
/// 装载顺序约束 / The loading order rows.
/// </summary>
/// <remarks>
/// 对每条拓扑装载顺序邻接，加一行
/// <c>actualLoaded_{j1} ≥ actualLoaded_{j2}</c>——先装的舱位不能比后装的舱位装得少，否则后装的货被先装的挡住。
///
/// **只在不处于散货舱时加行**：散货舱是"倒进去"的，没有顺序可言。条件是
/// `position1.unavailable || position2.unavailable || position1.bulk || position2.bulk → continue`，
/// 也就是说**可用性与散货两两组合共四种情况都要跳过**。少查 `bulk` 会给散货舱加一条无意义的顺序约束，
/// 而它在物理上不可能被违反，只会白占约束表。
///
/// 这条管线**只在预配载模式下加入**（见 `PipelineListGenerator`），因为它约束的是"怎么装"而不是"装什么"。
///
/// For every topological loading-order adjacency, add <c>actualLoaded_{j1} ≥ actualLoaded_{j2}</c> — the earlier position must not carry less than the
/// later one, or the later cargo is blocked by the earlier. **Rows are skipped for bulk holds**, which are simply poured in and have no order: its condition is
/// `position1.unavailable || position2.unavailable || position1.bulk || position2.bulk → continue`, i.e. all four combinations of availability and bulk must skip. Dropping the
/// `bulk` checks adds a meaningless order row to a bulk hold that can never be violated physically, merely filling the table. This pipeline is **added only in predistribution
/// mode** (see `PipelineListGenerator`) because it constrains *how* to load rather than *what* to load.
/// </remarks>
public sealed class LoadingOrderLimit
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly IReadOnlyList<Neighbour> _neighbours;
    private readonly Load _load;

    /// <summary>
    /// 构造装载顺序约束 / Construct the loading order rows.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="neighbours">拓扑装载顺序邻接 / The topological loading-order neighbours.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public LoadingOrderLimit(
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyList<Neighbour> neighbours,
        Load load,
        string name = "loading_order_limit") {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(neighbours);
        ArgumentNullException.ThrowIfNull(load);
        _positions = positions;
        _neighbours = neighbours;
        _load = load;
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束 / Apply the rows.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> linear) {
        ArgumentNullException.ThrowIfNull(linear);

        foreach (Neighbour neighbour in _neighbours) {
            int j1 = IndexOfPosition(neighbour.Pair.First);
            int j2 = IndexOfPosition(neighbour.Pair.Second);
            // 邻接引用了不在舱位表里的位置是**失败**，不是跳过：跳过会让这条顺序约束静默消失。
            // An adjacency naming a position outside the list is a **failure**, not a skip: skipping would silently drop this order constraint.
            if (j1 < 0) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "装载顺序邻接引用了未知舱位 " + neighbour.Pair.First.SpaceName +
                    " / the loading-order adjacency names an unknown position"));
            }
            if (j2 < 0) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "装载顺序邻接引用了未知舱位 " + neighbour.Pair.Second.SpaceName +
                    " / the loading-order adjacency names an unknown position"));
            }

            StowagePosition position1 = _positions[j1];
            StowagePosition position2 = _positions[j2];
            if (!position1.Status.Available
                || !position2.Status.Available
                || position1.Location.Bulk
                || position2.Location.Bulk) {
                continue;
            }

            if (_load.ActualLoadedSymbol(j1) is not { } first) {
                return Missing(position1);
            }
            if (_load.ActualLoadedSymbol(j2) is not { } second) {
                return Missing(position2);
            }

            string rowName = Name + "_" + position1.Name + "_" + position2.Name;
            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, first) },
                    Flt64.Zero),
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, second) },
                    Flt64.Zero),
                Comparison.GE,
                rowName);
            Try added = linear.AddConstraint(row, group: null, name: rowName);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private int IndexOfPosition(Domain.Aircraft.Model.Position position) {
        for (int j = 0; j < _positions.Count; ++j) {
            if (string.Equals(_positions[j].Name, position.SpaceName, StringComparison.Ordinal)) {
                return j;
            }
        }
        return -1;
    }

    private Try Missing(StowagePosition position) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            "舱位 " + position.Name + " 的实际装载符号未注册 / the actual loaded symbol of position " + position.Name + " is not registered"));
}
