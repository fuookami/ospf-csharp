#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;

/// <summary>
/// 航班上的人数表达式 / The passenger-amount expressions per flight and cabin.
/// </summary>
/// <remarks>
/// 基线 `PassengerAmount.kt`：每个（航班任务，舱位）一个表达式，四件事合成的：
///
/// * **原人数**：这位乘客本来就在这一档 → 加上人数；
/// * **取消**：无论哪一档，都减去取消量（取消是"这个人不飞了"，与舱位无关）；
/// * **变更出去**：自己那一档减去所有变更量；**别的档**则加上"改到这一档来"的量；
/// * **变更进来**：别人的行程改签到本航班时，本航班在那一档上多出这些人。
///
/// 最容易写错的是第三点的两个分支：**自己那一档是"减出去"，别的档是"加进来"**。两处写反会让人数在舱位之间凭空翻倍或归零，
/// 而容量约束照样能过——**因为容量约束只看总数，不看分档**。
///
/// **一处必要差异**：基线在没开航班变更时会去读未初始化的变量表（运行时抛异常）。C# 侧遇到 `null` 就跳过那几项——
/// 没开就是没开，不是错误。
///
/// The baseline's `PassengerAmount.kt`: one expression per (flight task, cabin), assembled from four things: **the original head count** when the passenger is already in that cabin;
/// **cancellation**, subtracted regardless of cabin (cancelling means "this person does not fly", independent of cabin); **changing out** — the passenger's own cabin subtracts every change,
/// while **other cabins** add "the amount moved into this cabin"; and **changing in** — when someone else's itinerary moves onto this flight, that flight gains those people in that cabin.
///
/// The easiest mistake is the two branches in the third point: **the own cabin subtracts, other cabins add**. Swapping them makes the head count double or vanish between cabins while
/// the capacity constraints still pass — **because those constraints only look at the total, not the split**.
///
/// **One necessary difference**: without flight change enabled the baseline reads an uninitialised variable table and throws at runtime. The C# side skips those terms on `null` —
/// not enabled means not enabled, not an error.
/// </remarks>
public sealed class PassengerAmount {
    private readonly IReadOnlyList<FlightTask> _flights;

    private readonly IReadOnlyDictionary<FlightTask, IReadOnlyList<FlightPassenger>> _passengers;

    private readonly IReadOnlyList<PassengerClassId> _classes;

    private readonly PassengerCancel _cancel;

    private readonly PassengerChange _change;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="flights">航班任务 / The flight tasks.</param>
    /// <param name="passengers">航班到该航班乘客的映射 / The passengers per flight.</param>
    /// <param name="classes">全部舱位 / All cabins.</param>
    /// <param name="cancel">取消变量 / The cancellation variables.</param>
    /// <param name="change">变更变量 / The change variables.</param>
    public PassengerAmount(
        IReadOnlyList<FlightTask> flights,
        IReadOnlyDictionary<FlightTask, IReadOnlyList<FlightPassenger>> passengers,
        IReadOnlyList<PassengerClassId> classes,
        PassengerCancel cancel,
        PassengerChange change) {
        ArgumentNullException.ThrowIfNull(flights);
        ArgumentNullException.ThrowIfNull(passengers);
        ArgumentNullException.ThrowIfNull(classes);
        ArgumentNullException.ThrowIfNull(cancel);
        ArgumentNullException.ThrowIfNull(change);
        _flights = flights;
        _passengers = passengers;
        _classes = classes;
        _cancel = cancel;
        _change = change;
    }

    /// <summary>人数表达式 / The amount expressions, keyed by flight and cabin.</summary>
    public IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, LinearExpressionSymbol>>? Amounts { get; private set; }

    /// <summary>
    /// 注册到模型 / Register into the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    /// <remarks>
    /// **取消与变更变量必须先注册**：表达式引用的是它们的变量项，先建表达式再建变量会让引用落空。
    ///
    /// **The cancellation and change variables must be registered first**: the expressions reference their variable items, and building the expressions first leaves those references
    /// dangling.
    /// </remarks>
    public Try Register(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (Amounts is not null) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        IReadOnlyDictionary<FlightTask, IReadOnlyList<FlightPassenger>> incoming = Incoming();
        var table = new Dictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, LinearExpressionSymbol>>();
        var symbols = new List<LinearExpressionSymbol>();
        foreach (FlightTask flight in _flights) {
            var row = new Dictionary<PassengerClassId, LinearExpressionSymbol>();
            foreach (PassengerClassId cls in _classes) {
                var monomials = new List<LinearMonomial<Flt64>>();
                Flt64 constant = Flt64.Zero;

                IReadOnlyList<FlightPassenger> onBoard = _passengers.TryGetValue(flight, out IReadOnlyList<FlightPassenger>? found)
                    ? found
                    : Array.Empty<FlightPassenger>();
                foreach (FlightPassenger passenger in onBoard) {
                    bool sameCabin = passenger.Cls == cls;
                    if (sameCabin) {
                        constant += new Flt64(passenger.Amount);
                    }
                    // 取消与舱位无关：**不论哪一档，取消的人都得减掉**。
                    // Cancellation is cabin-independent: **whoever cancels is subtracted, in every cabin**.
                    monomials.Add(new LinearMonomial<Flt64>(Flt64.One.Negate(), _cancel.Of(passenger)));
                    if (sameCabin) {
                        if (_change.PassengerClassChange is { } classChange
                            && classChange.TryGetValue(passenger, out IReadOnlyDictionary<PassengerClassId, UIntVar>? toCabins)) {
                            foreach (UIntVar variable in toCabins.Values) {
                                monomials.Add(new LinearMonomial<Flt64>(Flt64.One.Negate(), variable));
                            }
                        }
                        if (_change.WithFlightChange
                            && _change.PassengerFlightChange is { } flightChange
                            && flightChange.TryGetValue(passenger, out IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, UIntVar>>? toFlights)) {
                            foreach (IReadOnlyDictionary<PassengerClassId, UIntVar> byClass in toFlights.Values) {
                                foreach (UIntVar variable in byClass.Values) {
                                    monomials.Add(new LinearMonomial<Flt64>(Flt64.One.Negate(), variable));
                                }
                            }
                        }
                    } else if (_change.PassengerClassChange is { } moves
                        && moves.TryGetValue(passenger, out IReadOnlyDictionary<PassengerClassId, UIntVar>? intoThisCabin)
                        && intoThisCabin.TryGetValue(cls, out UIntVar? movedIn)) {
                        // **别的档是"加进来"**：改到这一档的人在这一档上多出来。
                        // **Other cabins add**: whoever moved into this cabin appears in it.
                        monomials.Add(new LinearMonomial<Flt64>(Flt64.One, movedIn));
                    }
                }

                if (_change.WithFlightChange
                    && _change.PassengerFlightChange is { } movingIn
                    && incoming.TryGetValue(flight, out IReadOnlyList<FlightPassenger>? arrivals)) {
                    foreach (FlightPassenger passenger in arrivals) {
                        if (movingIn.TryGetValue(passenger, out IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, UIntVar>>? byFlight)
                            && byFlight.TryGetValue(flight, out IReadOnlyDictionary<PassengerClassId, UIntVar>? byClass)
                            && byClass.TryGetValue(cls, out UIntVar? arrived)) {
                            monomials.Add(new LinearMonomial<Flt64>(Flt64.One, arrived));
                        }
                    }
                }

                var symbol = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(monomials, constant),
                    name: "passenger_amount_" + flight.Id + "_" + cls.Cls);
                row[cls] = symbol;
                symbols.Add(symbol);
            }
            table[flight] = row;
        }

        Amounts = table;
        foreach (LinearExpressionSymbol symbol in symbols) {
            Try added = model.Add(new[] { symbol });
            if (!added.IsOk) {
                Amounts = null;
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <remarks>
    /// **"变更进来"要按目标航班归集**：一位乘客的行程改到哪一班，就在那一班上出现，与他自己原来那班无关。
    /// 写成"按原航班归集"会让两班的人数同时错——**错的还正好互补，总数看起来对**。
    ///
    /// **"Changing in" is grouped by target flight**: whichever flight the itinerary moves to is where the passenger shows up, unrelated to the original one. Grouping by the original
    /// flight makes both flights wrong at once — **and the errors cancel out, so the total still looks right**.
    /// </remarks>
    private IReadOnlyDictionary<FlightTask, IReadOnlyList<FlightPassenger>> Incoming() {
        var indexed = new Dictionary<FlightTask, List<FlightPassenger>>();
        if (!_change.WithFlightChange) {
            return new Dictionary<FlightTask, IReadOnlyList<FlightPassenger>>();
        }
        IReadOnlyDictionary<FlightTask, IReadOnlyList<FlightTask>> toFlights = _change.ToFlights;
        foreach (FlightPassenger passenger in _passengers.Values.SelectMany(row => row)) {
            if (!toFlights.TryGetValue(passenger.Flight, out IReadOnlyList<FlightTask>? targets)) {
                continue;
            }
            foreach (FlightTask target in targets.Distinct()) {
                if (!indexed.TryGetValue(target, out List<FlightPassenger>? arriving)) {
                    arriving = new List<FlightPassenger>();
                    indexed[target] = arriving;
                }
                if (!arriving.Any(existing => ReferenceEquals(existing, passenger))) {
                    arriving.Add(passenger);
                }
            }
        }
        return indexed.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<FlightPassenger>)pair.Value);
    }
}
