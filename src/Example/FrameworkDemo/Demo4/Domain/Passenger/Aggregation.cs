#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger;

/// <summary>
/// 乘客域对象聚合 / The passenger domain aggregation.
/// </summary>
/// <remarks>
/// ：把取消、变更、人数三块装起来，注册顺序就是**取消 → 变更 → 人数**。
///
/// 顺序不是随意的：人数表达式引用取消与变更的变量项，**先建人数再建变量，引用就落空**——而落空的表现是
/// "表达式里少了那些项，解出来的人数看起来正常"。所以这一层的判据不看"注册成功"，看**表达式里真的有变更项**。
///
/// **一处推迟**：基线还把 `timeWindow` / `time` / `capacity` 三个外部对象装进聚合，但它们只被 6 条限制拿去用。
/// C# 的限制层会自己接收依赖，这里不提前存三个只有后续批次才读的字段。**装好一个字段没人读，就是给下一个人留一个"看起来在用"的假象。**
///
/// The code's assembles cancellation, change and amount, in the registration order **cancel, change, amount**.
///
/// The order is not arbitrary: the amount expressions reference the cancellation and change variable items, so **building the amounts before the variables leaves those references
/// dangling** — and the symptom is "the expressions are missing those terms while the solved head count looks normal". The criterion here therefore does not check "registration
/// succeeded" but **that the change terms really appear in the expressions**.
///
/// **One deferral**: the code also carries `timeWindow`, `time` and `capacity`, but only the six limits read them. The C# limits take their dependencies directly, so three fields that
/// nobody reads until a later batch are not stored here. **Storing a field nobody reads leaves the next reader with the illusion that it is used.**
/// </remarks>
public sealed class Aggregation {
    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="flights">航班任务 / The flight tasks.</param>
    /// <param name="passengers">航班乘客 / The flight passengers.</param>
    /// <param name="classes">全部舱位 / All cabins.</param>
    /// <param name="withFlightChange">是否建航班变更变量 / Whether to build flight-change variables.</param>
    public Aggregation(
        IReadOnlyList<FlightTask> flights,
        IReadOnlyList<FlightPassenger> passengers,
        IReadOnlyList<PassengerClassId> classes,
        bool withFlightChange = false) {
        ArgumentNullException.ThrowIfNull(flights);
        ArgumentNullException.ThrowIfNull(passengers);
        ArgumentNullException.ThrowIfNull(classes);
        Flights = flights;
        Passengers = passengers;
        Classes = classes;
        WithFlightChange = withFlightChange;
        Cancel = new PassengerCancel(passengers);
        Change = new PassengerChange(flights, passengers, classes, withFlightChange);
        Amount = new PassengerAmount(
            flights,
            passengers
                .GroupBy(passenger => passenger.Flight)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<FlightPassenger>)group.ToList()),
            classes,
            Cancel,
            Change);
    }

    /// <summary>航班任务 / The flight tasks.</summary>
    public IReadOnlyList<FlightTask> Flights { get; }

    /// <summary>航班乘客 / The flight passengers.</summary>
    public IReadOnlyList<FlightPassenger> Passengers { get; }

    /// <summary>全部舱位 / All cabins.</summary>
    public IReadOnlyList<PassengerClassId> Classes { get; }

    /// <summary>是否建航班变更变量 / Whether flight-change variables are built.</summary>
    public bool WithFlightChange { get; }

    /// <summary>取消变量 / The cancellation variables.</summary>
    public PassengerCancel Cancel { get; }

    /// <summary>变更变量 / The change variables.</summary>
    public PassengerChange Change { get; }

    /// <summary>人数表达式 / The amount expressions.</summary>
    public PassengerAmount Amount { get; }

    /// <summary>
    /// 注册到模型 / Register into the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Try Register(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        Try cancel = Cancel.Register(model);
        if (cancel.IsFailed) {
            return cancel;
        }
        Try change = Change.Register(model);
        if (change.IsFailed) {
            return change;
        }
        return Amount.Register(model);
    }
}
