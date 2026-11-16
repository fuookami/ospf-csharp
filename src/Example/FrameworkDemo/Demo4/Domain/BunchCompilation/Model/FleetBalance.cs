#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;

/// <summary>
/// 机队平衡层 / The fleet balance layer.
/// </summary>
/// <remarks>
/// ：按**检查点**（"型别 × 机场"）给机队计数，每台飞机一个 `z` 变量进表达式，再挂一个
/// "不超过该检查点的机数"的负侧松弛。检查点不是随便取的——**在飞的飞机算它要落的机场，不在飞的算它当前所在机场**，
/// 否则"今晚有多少架这种机型能落在浦东"根本算不出来。
///
/// 判据要小心的两点：检查点按 `(机场, 型别)` **去重**；同一检查点下每台飞机**各占一项**（不是一台飞机一项、也不是共享一个变量）。
///
/// **`AddColumns` 是"换掉"而不是"追加"**：基线每轮先 `flush` 再累加，所以注册时那些 `z` 项在这一轮会被清掉，
/// `fleet_l` 变成"这一轮的束各占一项"。C# 的 `Flush` 是空实现，这里显式 `Clear` 再累加——**否则每多一轮就多一批项，
/// 跑十轮之后同一个束在表达式里出现十次**。
///
/// The code counts the fleet by **checkpoint** ("minor type x airport"), puts one `z` variable per aircraft into an expression, and attaches a negative-side slack
/// bounding it by the checkpoint's aircraft count. The checkpoint is not arbitrary — **an airborne aircraft counts at the airport it will land at, an idle one at where it is now** —
/// otherwise "how many of this type can land at ZSPD tonight" is unanswerable.
///
/// Two things the criteria must watch: checkpoints are **deduplicated** by (airport, minor type); and every aircraft under a checkpoint holds **its own term** (not one term per
/// aircraft group, and not a shared variable).
///
/// **`AddColumns` replaces rather than appends**: the code flushes before accumulating each round, so the `z` terms from registration are cleared and `fleet_l` becomes "one term
/// per bunch of this round". C#'s `Flush` is a no-op, so this clears explicitly before accumulating — **otherwise every extra round adds another batch of terms, and after ten rounds
/// the same bunch appears ten times in the expression**.
/// </remarks>
public sealed class FleetBalance {
    /// <summary>
    /// 检查点：型别 × 机场 / A checkpoint: minor type x airport.
    /// </summary>
    /// <param name="Airport">机场 / The airport.</param>
    /// <param name="MinorType">机型 / The aircraft minor type.</param>
    public sealed record CheckPoint(Airport Airport, AircraftMinorType MinorType) {
        /// <summary>
        /// 该束是否落在本检查点 / Whether the bunch falls under this checkpoint.
        /// </summary>
        /// <param name="bunch">任务束 / The bunch.</param>
        /// <returns>是否命中 / Whether it matches.</returns>
        /// <remarks>
        /// **按"落地的机场"判，不是按出发机场**：检查点统计的是"这个机场晚上有多少架这种机型"，出发机场说明不了这件事。
        ///
        /// **Judged by the arrival airport, not the departure one**: the checkpoint counts "how many of this type sit at this airport tonight", and the departure airport says nothing
        /// about that.
        /// </remarks>
        public bool Matches(FlightTaskBunch bunch) =>
            Equals(bunch.Executor.MinorType, MinorType) && Equals(bunch.Arr, Airport);
    }

    /// <summary>
    /// 一个检查点下的机数上限 / The aircraft bound under one checkpoint.
    /// </summary>
    /// <param name="Amount">机数 / The aircraft count.</param>
    /// <param name="Aircrafts">这些飞机 / The aircraft.</param>
    public sealed record Limit(ulong Amount, IReadOnlyList<Aircraft> Aircrafts);

    private readonly IReadOnlyList<Aircraft> _aircrafts;

    private readonly IReadOnlyList<FlightTaskBunch> _originBunches;

    private readonly BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> _compilation;

    private readonly Dictionary<int, LinearExpressionSymbol> _fleet = new();

    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _slack = new();

    private readonly Dictionary<int, LinearExpressionSymbol> _source = new();

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="aircrafts">飞机列表 / The aircraft.</param>
    /// <param name="originBunches">初始束 / The origin bunches.</param>
    /// <param name="compilation">束编译 / The bunch compilation.</param>
    public FleetBalance(
        IReadOnlyList<Aircraft> aircrafts,
        IReadOnlyList<FlightTaskBunch> originBunches,
        BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation) {
        ArgumentNullException.ThrowIfNull(aircrafts);
        ArgumentNullException.ThrowIfNull(originBunches);
        ArgumentNullException.ThrowIfNull(compilation);
        _aircrafts = aircrafts;
        _originBunches = originBunches;
        _compilation = compilation;
    }

    /// <summary>检查点与限额 / The checkpoints and their bounds.</summary>
    public IReadOnlyList<KeyValuePair<CheckPoint, Limit>> Limits {
        get {
            var originByAircraft = new Dictionary<Aircraft, FlightTaskBunch>();
            foreach (FlightTaskBunch bunch in _originBunches) {
                originByAircraft.TryAdd(bunch.Executor, bunch);
            }

            var counts = new List<CheckPoint>();
            var table = new Dictionary<CheckPoint, List<Aircraft>>();
            foreach (Aircraft aircraft in _aircrafts) {
                // **在飞的算落点、不在飞的算当前位置**：两条分支不能合并成一条。
                // **Airborne counts at its arrival airport, idle at where it is now**: the two branches cannot be merged into one.
                CheckPoint key = originByAircraft.TryGetValue(aircraft, out FlightTaskBunch? bunch) && bunch.Tasks.Count > 0
                    ? new CheckPoint(bunch.Arr, aircraft.MinorType)
                    : new CheckPoint(aircraft.Usability.Location, aircraft.MinorType);
                if (!table.TryGetValue(key, out List<Aircraft>? aircrafts)) {
                    aircrafts = new List<Aircraft>();
                    table[key] = aircrafts;
                    counts.Add(key);
                }
                aircrafts.Add(aircraft);
            }

            return counts
                .Select(key => new KeyValuePair<CheckPoint, Limit>(
                    key,
                    new Limit((ulong)table[key].Count, table[key])))
                .ToList();
        }
    }

    /// <summary>机队表达式 / The fleet expressions, keyed by checkpoint index.</summary>
    public IReadOnlyDictionary<int, LinearExpressionSymbol> Fleets => _fleet;

    /// <summary>松弛符号 / The slack symbols, keyed by checkpoint index.</summary>
    public IReadOnlyDictionary<int, ILinearIntermediateSymbol<Flt64>> Slacks => _slack;

    /// <summary>
    /// 松弛的源多项式 / The slacks' source polynomials.
    /// </summary>
    /// <remarks>
    /// 与 同一个理由：松弛符号是适配器，看不到系数；**源多项式才是这条规则的正文**。
    ///
    /// For the same reason: the slack symbol is an adapter whose coefficients are invisible; **the source polynomial is the rule's body**.
    /// </remarks>
    public IReadOnlyDictionary<int, LinearExpressionSymbol> Sources => _source;

    /// <summary>
    /// 把这一轮的列变量换进机队表达式 / Swap this round's column variables into the fleet expressions.
    /// </summary>
    /// <param name="iteration">迭代轮次 / The iteration round.</param>
    /// <param name="bunches">本轮任务束 / The bunches of this round.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    /// <remarks>
    /// 按 `(落地机场, 机型)` 分组，检查点命中才累加。**系数是 1**：一台飞机占一格，与上限同单位。
    /// 没有命中的检查点保持原样——**"这一轮没有飞机落在这里"不等于"把上轮的占用留着"**，但也确实没有新信息可加。
    ///
    /// Groups by `(arrival airport, minor type)` and accumulates only where a checkpoint matches, with coefficient 1: one aircraft occupies one slot, the same unit as the bound. A
    /// checkpoint with no match keeps its previous content — "no aircraft landed here this round" is not the same as "keep last round's occupancy", but it also brings no new information.
    /// </remarks>
    public Try AddColumns(ulong iteration, IReadOnlyList<FlightTaskBunch> bunches) {
        ArgumentNullException.ThrowIfNull(bunches);

        var byArrival = new Dictionary<CheckPoint, List<FlightTaskBunch>>();
        foreach (FlightTaskBunch bunch in bunches) {
            var key = new CheckPoint(bunch.Arr, bunch.Executor.MinorType);
            if (!byArrival.TryGetValue(key, out List<FlightTaskBunch>? matched)) {
                matched = new List<FlightTaskBunch>();
                byArrival[key] = matched;
            }
            matched.Add(bunch);
        }

        IReadOnlyList<KeyValuePair<CheckPoint, Limit>> limits = Limits;
        for (int index = 0; index < limits.Count; ++index) {
            if (!byArrival.TryGetValue(limits[index].Key, out List<FlightTaskBunch>? matched) || matched.Count == 0) {
                continue;
            }
            var monomials = new List<LinearMonomial<Flt64>>();
            foreach (FlightTaskBunch bunch in matched) {
                if (_compilation.ColumnItem(iteration, bunch) is { } column) {
                    monomials.Add(new LinearMonomial<Flt64>(Flt64.One, column));
                }
            }
            if (monomials.Count == 0) {
                continue;
            }
            // **先清再加**，等价于`flush`：不清的话每轮都会把上一轮的项留在里面。
            // **Clear, then add** — the equivalent of its `flush`: without the clear every round keeps the previous round's terms.
            MutableLinearPolynomial<Flt64> mutable = _fleet[index].AsMutable();
            mutable.Clear();
            mutable.Add(new LinearPolynomial<Flt64>(monomials.ToArray(), Flt64.Zero));
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册到模型 / Register into the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Try Register(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        IReadOnlyList<KeyValuePair<CheckPoint, Limit>> limits = Limits;
        if (limits.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        if (_fleet.Count == 0) {
            for (int index = 0; index < limits.Count; ++index) {
                var monomials = new List<LinearMonomial<Flt64>>();
                foreach (Aircraft aircraft in limits[index].Value.Aircrafts) {
                    if (_compilation.Leisure.TryGetValue(aircraft, out object? symbol)
                        && symbol is Fuookami.Ospf.Math.Symbol.ISymbol typed) {
                        monomials.Add(new LinearMonomial<Flt64>(Flt64.One, typed));
                    }
                }
                _fleet[index] = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(monomials, Flt64.Zero),
                    name: "fleet_" + index);
            }
            Try addedFleets = model.Add(_fleet.Values);
            if (!addedFleets.IsOk) {
                return addedFleets;
            }
        }

        if (_slack.Count == 0) {
            for (int index = 0; index < limits.Count; ++index) {
                var poly = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, _fleet[index]) },
                        Flt64.Zero),
                    name: "fleet_slack_source_" + index);
                _source[index] = poly;
                _slack[index] = ProduceSlackHelper.Build(
                    poly,
                    threshold: new Flt64(limits[index].Value.Amount),
                    withNegative: true,
                    withPositive: false,
                    constraint: false,
                    name: "fleet_slack_" + index);
            }
            Try addedSlacks = model.Add(_slack.Values);
            if (!addedSlacks.IsOk) {
                return addedSlacks;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
