#nullable enable

using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;

/// <summary>
/// 一位乘客的行程 / One passenger's itinerary.
/// </summary>
/// <remarks>
/// ：一位乘客 = 人数 + 一串"航班任务 × 舱位"。行程不是随便一串航段——
/// **相邻两段必须首尾相接**（前一段的到达机场等于后一段的出发机场），否则这条行程根本飞不到目的地。
///
/// **一处有意的强化**：Kotlin 的 `assert` 做这三条检查，而 `assert` 在 release 构建里**整个消失**——
/// 也就是说生产环境上一条不连通的行程会被安静地接受。C# 侧改成抛 `ArgumentException`：这里是信任边界（外部数据进来的地方），
/// 悄悄放过一条错行程比直接报错危险得多。
///
/// **只用 `PassengerClassId`**：`PassengerClass` 是包字符串的内联值类，C# 侧已有的 `PassengerClassId` 就是同一个东西，
/// 不必再造一个类型。
///
/// The code's: a passenger is a head count plus a list of "flight task x cabin". The itinerary is not an arbitrary list of legs — **adjacent legs must
/// connect** (the previous leg's arrival airport equals the next leg's departure airport), otherwise the itinerary cannot reach its destination.
///
/// **One deliberate strengthening**: the code checks these three rules with Kotlin's `assert`, which **vanishes entirely in release builds** — so in production a disconnected
/// itinerary is quietly accepted. The C# side throws `ArgumentException`: this is a trust boundary (where external data enters), and silently accepting a broken itinerary is far more
/// dangerous than failing loudly.
///
/// **Only `PassengerClassId` is used**: its `PassengerClass` is an inline value class wrapping a string, and C#'s existing `PassengerClassId` is the same thing — there is no
/// need for another type.
/// </remarks>
public sealed class Passenger {
    private readonly IReadOnlyList<(FlightTask Flight, PassengerClassId Cls)> _seats;

    private readonly Dictionary<TaskKey, PassengerClassId> _byTask;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="amount">人数 / The head count.</param>
    /// <param name="flights">"航班任务 × 舱位"序列 / The flight-task and cabin pairs.</param>
    /// <param name="id">乘客标识，缺省自动生成 / The passenger id, generated when omitted.</param>
    public Passenger(
        ulong amount,
        IReadOnlyList<(FlightTask Flight, PassengerClassId Cls)> flights,
        string? id = null) {
        ArgumentNullException.ThrowIfNull(flights);
        if (flights.Count == 0) {
            throw new ArgumentException("行程至少要有一段航班 / an itinerary needs at least one flight", nameof(flights));
        }
        for (int index = 0; index < flights.Count; ++index) {
            if (!flights[index].Flight.IsFlight) {
                throw new ArgumentException(
                    "行程里只能是航班任务 / an itinerary may only hold flight tasks: " + flights[index].Flight.Id,
                    nameof(flights));
            }
            if (index > 0 && !Equals(flights[index - 1].Flight.Arr, flights[index].Flight.Dep)) {
                throw new ArgumentException(
                    "相邻航段必须首尾相接 / adjacent legs must connect: " + flights[index - 1].Flight.Id + " -> " + flights[index].Flight.Id,
                    nameof(flights));
            }
        }

        Id = id ?? Guid.NewGuid().ToString();
        Amount = amount;
        _seats = flights;
        _byTask = new Dictionary<TaskKey, PassengerClassId>();
        foreach ((FlightTask flight, PassengerClassId cls) in flights) {
            _byTask[flight.Key] = cls;
        }
        var route = new List<Airport> { flights[0].Flight.Dep };
        route.AddRange(flights.Select(seat => seat.Flight.Arr));
        Route = route;
    }

    /// <summary>乘客标识 / The passenger id.</summary>
    public string Id { get; }

    /// <summary>人数 / The head count.</summary>
    public ulong Amount { get; }

    /// <summary>航段与舱位 / The legs and cabins.</summary>
    public IReadOnlyList<(FlightTask Flight, PassengerClassId Cls)> Seats => _seats;

    /// <summary>途经机场 / The airports along the route.</summary>
    public IReadOnlyList<Airport> Route { get; }

    /// <summary>出发机场 / The departure airport.</summary>
    public Airport Dep => Route[0];

    /// <summary>到达机场 / The arrival airport.</summary>
    public Airport Arr => Route[Route.Count - 1];

    /// <summary>是否需要中转 / Whether this itinerary needs a transfer.</summary>
    /// <remarks>
    /// 一段航程的路线是"出发 + 到达"两个机场，所以**路线长度大于 2 才是中转**；用 `Route.Count > 1` 会把所有直飞都算成中转。
    ///
    /// A single-leg route holds "departure plus arrival", so **a transfer means a route length above 2**; using `Route.Count > 1` would call every direct flight a transfer.
    /// </remarks>
    public bool Transfer => Route.Count > 2;

    /// <summary>
    /// 是否包含该航班任务 / Whether the itinerary uses the task.
    /// </summary>
    /// <param name="task">航班任务 / The flight task.</param>
    /// <returns>是否包含 / Whether it is included.</returns>
    public bool Contains(FlightTask task) => _byTask.ContainsKey(task.Key);

    /// <summary>
    /// 该航班任务上的舱位 / The cabin on that task.
    /// </summary>
    /// <param name="task">航班任务 / The flight task.</param>
    /// <returns>舱位，没有则 null / The cabin, or null.</returns>
    public PassengerClassId? Get(FlightTask task) => _byTask.TryGetValue(task.Key, out PassengerClassId cls) ? cls : null;

    /// <inheritdoc/>
    public override string ToString() => Id + "(" + Amount + ")";
}

/// <summary>
/// 一个航班任务上的一位乘客 / A passenger on one flight task.
/// </summary>
/// <remarks>
/// 与基线一样继承 `ManualIndexed`——C# 侧本来就有这个基类（`Utils.Concept.Indexed`），**不必自己再写一遍三件套**。
///
/// It extends `ManualIndexed` exactly as the code does — C# already has that base class (`Utils.Concept.Indexed`), so **there is no reason to re-implement the index trio**.
/// </remarks>
public sealed class FlightPassenger : ManualIndexed {
    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="flight">航班任务 / The flight task.</param>
    /// <param name="passenger">乘客 / The passenger.</param>
    /// <param name="prev">行程上的前一段 / The previous leg on the itinerary.</param>
    public FlightPassenger(FlightTask flight, Passenger passenger, FlightPassenger? prev = null) {
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(passenger);
        Cls = passenger.Get(flight)
            ?? throw new ArgumentException("这位乘客的行程里没有这个航班 / the passenger's itinerary does not include this flight", nameof(flight));
        Flight = flight;
        Passenger = passenger;
        Prev = prev;
    }

    /// <summary>航班任务 / The flight task.</summary>
    public FlightTask Flight { get; }

    /// <summary>乘客 / The passenger.</summary>
    public Passenger Passenger { get; }

    /// <summary>行程上的前一段 / The previous leg on the itinerary.</summary>
    public FlightPassenger? Prev { get; }

    /// <summary>舱位 / The cabin.</summary>
    public PassengerClassId Cls { get; }

    /// <summary>人数 / The head count.</summary>
    public ulong Amount => Passenger.Amount;
}
