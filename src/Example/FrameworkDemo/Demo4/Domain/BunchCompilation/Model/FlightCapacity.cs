#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
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
/// 航班容量表达式层 / The flight capacity expression layer.
/// </summary>
/// <remarks>
/// 每个航班任务、每个舱位一个**表达式符号**，内容是"各任务束在这一舱位上提供的座位数 × 该束的列变量"。
/// 这个层此前一直没有——于是"这一轮的编排到底运了多少人/多少货"根本没有量可读。
///
/// 两条规则：
///
/// * **容量为零的束不进表达式**（座位数或货量是 0 的束对这条表达式没有贡献，加进去只会拖长展开）；
/// * **列变量按"去重后的顺序"取**，用 `Compilation.ColumnItem(iteration, bunch)` 而不是自己数一遍入参顺序。
///
/// **`AddColumns` 是"换掉"而不是"追加"**：每轮先 `Flush` 再加，而 `Flush` 是空实现，所以这里显式 `Clear`。
/// 不做这一步，跑十轮之后同一个束会在表达式里出现十次——解上未必立刻错，展开是十倍的项。
///
/// Each flight task and cabin gets one **expression symbol** whose content is "the seats each bunch offers in that cabin multiplied by that bunch's column variable". The expression layer
/// was missing entirely, so "how many passengers or how much cargo this round really moves" was simply unreadable. Two rules: **zero-capacity
/// bunches stay out of the expression** (they contribute nothing and only lengthen expansion), and **column variables come from the deduplicated order**, via
/// `Compilation.ColumnItem(iteration, bunch)` rather than recounting the argument order.
///
/// **`AddColumns` replaces rather than appends**: each round flushes before adding, but `Flush` is a no-op, so this clears explicitly. Without that step, ten rounds
/// would put the same bunch into the expression ten times — the solution may not break immediately, but the expansion grows tenfold.
/// </remarks>
public sealed class FlightCapacity {
    private readonly IReadOnlyList<FlightTask> _tasks;

    private readonly BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> _compilation;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="tasks">航班任务列表 / The flight tasks.</param>
    /// <param name="compilation">束编译 / The bunch compilation.</param>
    /// <param name="withPassenger">是否建客运容量，缺省按任务推断 / Whether to build passenger capacity, inferred when omitted.</param>
    /// <param name="withCargo">是否建货运容量，缺省按任务推断 / Whether to build cargo capacity, inferred when omitted.</param>
    public FlightCapacity(
        IReadOnlyList<FlightTask> tasks,
        BunchCompilation<FlightTaskBunch, FlightTask, Aircraft, FlightTaskAssignment> compilation,
        bool? withPassenger = null,
        bool? withCargo = null) {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(compilation);
        _tasks = tasks;
        _compilation = compilation;
        WithPassenger = withPassenger ?? tasks.Any(task => task.Capacity is AircraftCapacity.PassengerCapacity);
        WithCargo = withCargo ?? tasks.Any(task => task.Capacity is AircraftCapacity.CargoCapacity);
    }

    /// <summary>是否建客运容量 / Whether passenger capacity is built.</summary>
    public bool WithPassenger { get; }

    /// <summary>是否建货运容量 / Whether cargo capacity is built.</summary>
    public bool WithCargo { get; }

    /// <summary>客运容量表达式表 / The passenger capacity expressions.</summary>
    public IReadOnlyDictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, LinearExpressionSymbol>>? Passenger { get; private set; }

    /// <summary>货运容量表达式表 / The cargo capacity expressions.</summary>
    public IReadOnlyDictionary<FlightTask, LinearExpressionSymbol>? Cargo { get; private set; }

    /// <summary>
    /// 注册到模型 / Register into the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Try Register(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (WithPassenger) {
            Passenger ??= BuildPassenger();
            foreach (IReadOnlyDictionary<PassengerClassId, LinearExpressionSymbol> row in Passenger.Values) {
                Try addedRow = model.Add(row.Values);
                if (!addedRow.IsOk) {
                    return addedRow;
                }
            }
        }
        if (WithCargo) {
            Cargo ??= BuildCargo();
            Try added = model.Add(Cargo.Values);
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 把这一轮的列变量加进表达式 / Fold this round's column variables into the expressions.
    /// </summary>
    /// <param name="iteration">迭代轮次 / The iteration round.</param>
    /// <param name="bunches">本轮任务束 / The bunches of this round.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Try AddColumns(ulong iteration, IReadOnlyList<FlightTaskBunch> bunches) {
        ArgumentNullException.ThrowIfNull(bunches);

        if (WithPassenger) {
            if (Passenger is null) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "客运容量还没注册 / the passenger capacity has not been registered"));
            }
            foreach (KeyValuePair<FlightTask, IReadOnlyDictionary<PassengerClassId, LinearExpressionSymbol>> row in Passenger) {
                foreach (KeyValuePair<PassengerClassId, LinearExpressionSymbol> cabin in row.Value) {
                    var monomials = new List<LinearMonomial<Flt64>>();
                    CollectPassenger(bunches, row.Key, cabin.Key, iteration, monomials);
                    if (monomials.Count > 0) {
                        // 先清再加，等价于`flush` / Clear, then add — the equivalent of its `flush`.
                        MutableLinearPolynomial<Flt64> mutable = cabin.Value.AsMutable();
                        mutable.Clear();
                        mutable.Add(new LinearPolynomial<Flt64>(monomials.ToArray(), Flt64.Zero));
                    }
                }
            }
        }

        if (WithCargo) {
            if (Cargo is null) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "货运容量还没注册 / the cargo capacity has not been registered"));
            }
            foreach (KeyValuePair<FlightTask, LinearExpressionSymbol> row in Cargo) {
                var monomials = new List<LinearMonomial<Flt64>>();
                CollectCargo(bunches, row.Key, iteration, monomials);
                if (monomials.Count > 0) {
                    MutableLinearPolynomial<Flt64> mutable = row.Value.AsMutable();
                    mutable.Clear();
                    mutable.Add(new LinearPolynomial<Flt64>(monomials.ToArray(), Flt64.Zero));
                }
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private void CollectPassenger(
        IReadOnlyList<FlightTaskBunch> bunches,
        FlightTask task,
        PassengerClassId cabin,
        ulong iteration,
        List<LinearMonomial<Flt64>> monomials) {
        foreach (FlightTaskBunch bunch in bunches) {
            FlightTask? thisTask = bunch.Get(task);
            if (thisTask is null) {
                continue;
            }
            // **座位数按"束用的那架飞机"取，不是按任务计划里那架**：同一个任务可以被不同座级的飞机构造成不同的束，
            // 按任务取会让"零座位的束"照样贡献 120 座——解上完全看不出，报告里人数却凭空多出来。
            //
            // **Seats come from the aircraft the bunch uses, not the one in the task's plan**: the same task can be bunched onto aircraft of different cabin sizes, and reading from the
            // task would let a zero-seat bunch still contribute 120 seats — invisible in the solution while the reported passenger count is inflated out of nowhere.
            if (bunch.Executor?.Capacity is not AircraftCapacity.PassengerCapacity passenger) {
                continue;
            }
            ulong seats = passenger.GetCapacity(cabin);
            if (seats == 0UL) {
                continue;
            }
            if (_compilation.ColumnItem(iteration, bunch) is { } column) {
                monomials.Add(new LinearMonomial<Flt64>(new Flt64(seats), column));
            }
        }
    }

    private void CollectCargo(
        IReadOnlyList<FlightTaskBunch> bunches,
        FlightTask task,
        ulong iteration,
        List<LinearMonomial<Flt64>> monomials) {
        foreach (FlightTaskBunch bunch in bunches) {
            FlightTask? thisTask = bunch.Get(task);
            if (thisTask is null) {
                continue;
            }
            // 同上：货量按束用的那架飞机取 / Same as above: cargo comes from the bunch's aircraft.
            if (bunch.Executor?.Capacity is not AircraftCapacity.CargoCapacity cargo) {
                continue;
            }
            if (cargo.ContainedCapacity.ToDouble() <= 0.0) {
                continue;
            }
            if (_compilation.ColumnItem(iteration, bunch) is { } column) {
                monomials.Add(new LinearMonomial<Flt64>(cargo.ContainedCapacity, column));
            }
        }
    }

    private Dictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, LinearExpressionSymbol>> BuildPassenger() {
        var table = new Dictionary<FlightTask, IReadOnlyDictionary<PassengerClassId, LinearExpressionSymbol>>();
        foreach (FlightTask task in _tasks) {
            if (task.Capacity is not AircraftCapacity.PassengerCapacity passenger) {
                continue;
            }
            var row = new Dictionary<PassengerClassId, LinearExpressionSymbol>();
            foreach (PassengerClassId cabin in passenger.Capacity.Keys) {
                row[cabin] = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                    name: $"{task.Name}_capacity_{cabin.Cls}");
            }
            table[task] = row;
        }
        return table;
    }

    private Dictionary<FlightTask, LinearExpressionSymbol> BuildCargo() {
        var table = new Dictionary<FlightTask, LinearExpressionSymbol>();
        foreach (FlightTask task in _tasks) {
            if (task.Capacity is not AircraftCapacity.CargoCapacity) {
                continue;
            }
            table[task] = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                name: $"{task.Name}_capacity");
        }
        return table;
    }
}
