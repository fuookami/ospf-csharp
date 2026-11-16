#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Service.Limits;

/// <summary>
/// 航班上的人数不得超过舱位容量 / Passengers on a flight may not exceed its cabin capacity.
/// </summary>
/// <remarks>
/// ：对每个航班、每个舱位加 `人数[航班, 舱位] ≤ 容量[航班, 舱位]`。
///
/// 这是乘客域与机队层之间**唯一的接缝**：左边是乘客域的人数表达式，右边是批次汇编里按机型分档的容量表达式。
/// 两侧都靠符号名对上，所以判据必须**同时认住左边与右边是哪个符号**——只数行数会让"两边都写成本侧"的实现通过：
/// 那种模型永远可行，`人数 ≤ 人数`。
///
/// 另一个容易漏的形态：乘客飞机的容量表只覆盖**它真的排了班的**那些航班。查不到容量就当成"容量为零"会把一条本来合法的排班判死，
/// 当成"容量无穷"又会让超售悄悄通过——所以这里**查不到就报失败**，交给人看。
///
/// The code's adds `amount[flight, cabin] ≤ capacity[flight, cabin]` per flight and cabin.
///
/// This is **the only seam** between the passenger domain and the fleet layer: the left side is the passenger amount expression , the right side the per-cabin capacity expression
/// built during bunch compilation . Both sides are matched by symbol name, so the criterion must **pin down which symbol sits on each side** — counting rows would let an
/// implementation with the same symbol on both sides pass: such a model is always feasible, `amount ≤ amount`.
///
/// Another easy miss: a passenger aircraft's capacity table only covers **the flights it is actually scheduled on**. Treating a missing entry as "capacity zero" kills a legal schedule,
/// treating it as "infinite" lets overbooking slip through — so **a missing entry is reported as a failure**, for a human to look at.
/// </remarks>
public sealed class PassengerFlightCapacityConstraint
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> {
    private readonly IReadOnlyList<FlightTask> _flights;

    private readonly PassengerAmount _amount;

    private readonly FlightCapacity _capacity;

    private readonly IReadOnlyList<PassengerClassId> _classes;

    private readonly string _name;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="flights">航班任务 / The flight tasks.</param>
    /// <param name="amount">人数表达式 / The amount expressions.</param>
    /// <param name="capacity">航班容量 / The flight capacity.</param>
    /// <param name="classes">全部舱位 / All cabins.</param>
    /// <param name="name">管道名称 / The pipeline name.</param>
    public PassengerFlightCapacityConstraint(
        IReadOnlyList<FlightTask> flights,
        PassengerAmount amount,
        FlightCapacity capacity,
        IReadOnlyList<PassengerClassId> classes,
        string name = "passenger_flight_capacity_constraint") {
        ArgumentNullException.ThrowIfNull(flights);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(classes);
        _flights = flights;
        _amount = amount;
        _capacity = capacity;
        _classes = classes;
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "航班容量约束只能施加到线性元模型 / the flight capacity constraint can only be applied to a linear meta model"));
        }

        if (_amount.Amounts is not { } amounts) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "人数表达式还没注册 / the amount expressions have not been registered yet"));
        }

        if (_capacity.Passenger is not { } capacities) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "这架飞机没有乘客舱位容量 / this aircraft has no passenger cabin capacity"));
        }

        for (int index = 0; index < _flights.Count; ++index) {
            FlightTask flight = _flights[index];
            foreach (PassengerClassId cls in _classes) {
                if (!amounts.TryGetValue(flight, out IReadOnlyDictionary<PassengerClassId, LinearExpressionSymbol>? amountRow)
                    || !amountRow.TryGetValue(cls, out LinearExpressionSymbol? amount)
                    || !capacities.TryGetValue(flight, out IReadOnlyDictionary<PassengerClassId, LinearExpressionSymbol>? capacityRow)
                    || !capacityRow.TryGetValue(cls, out LinearExpressionSymbol? capacity)) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "航班或舱位没有容量数据 / no capacity data for flight " + flight.Key.Id + " cabin " + cls.Cls));
                }

                string rowName = _name + "_" + index + "_" + cls.Cls;
                Try added = linear.AddConstraint(
                    new LinearInequality<Flt64>(
                        new LinearPolynomial<Flt64>(amount.Polynomial.Monomials, amount.Polynomial.Constant),
                        new LinearPolynomial<Flt64>(capacity.Polynomial.Monomials, capacity.Polynomial.Constant),
                        Comparison.LE,
                        rowName),
                    group: null,
                    name: rowName,
                    args: null);
                if (!added.IsOk) {
                    return added;
                }
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **返回零而不是抛异常**：影子价格为零只是让列生成少一个加速项，结果仍然正确。`ponytail:` 天花板——等这条约束的对偶信息被列生成用上再补。
    ///
    /// **Returns zero rather than throwing**: a zero shadow price only costs column generation one acceleration term while the answer stays correct. A **`ponytail:` ceiling**, to be
    /// filled in once column generation uses this constraint's dual information.
    /// </remarks>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>? Extractor()
        => (map, args) => Flt64.Zero;

    /// <inheritdoc/>
    /// <remarks>
    /// 目前没有键可认，所以只是返回成功——**不假装认回了什么**。
    ///
    /// There is no key to recognise yet, so this only returns success — **without pretending to have matched anything**.
    /// </remarks>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        ArgumentNullException.ThrowIfNull(shadowPriceMap);
        ArgumentNullException.ThrowIfNull(shadowPrices);
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
