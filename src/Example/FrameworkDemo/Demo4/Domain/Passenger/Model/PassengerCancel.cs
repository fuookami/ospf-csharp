#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using UInt64Number = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Passenger.Model;

/// <summary>
/// 乘客取消变量 / The passenger cancellation variables.
/// </summary>
/// <remarks>
/// ：每位航班乘客一个非负整数变量，上界是这一位的人数——**取消量不可能超过人本身**。
/// 这条上界不是可有可无的收紧：没有它，取消变量可以取任意大，取消最小化会把"取消多少人"压到零的同时，
/// 让别的约束通过这个变量拿到任意多的自由度。
///
/// The code's: one non-negative integer variable per flight passenger, bounded above by that passenger's head count — **cancelling more people than exist is
/// impossible**. That bound is not optional tightening: without it the cancellation variables could take any value, and cancellation minimisation would drive "how many people are
/// cancelled" to zero while other constraints gain arbitrary freedom through them.
/// </remarks>
public sealed class PassengerCancel {
    private readonly IReadOnlyList<FlightPassenger> _passengers;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="passengers">航班乘客 / The flight passengers.</param>
    public PassengerCancel(IReadOnlyList<FlightPassenger> passengers) {
        ArgumentNullException.ThrowIfNull(passengers);
        _passengers = passengers;
    }

    /// <summary>取消变量集合 / The cancellation variables, indexed by passenger order.</summary>
    public UIntVariable1? Variables { get; private set; }

    /// <summary>
    /// 某位乘客的取消变量 / The cancellation variable of one passenger.
    /// </summary>
    /// <param name="passenger">航班乘客 / The flight passenger.</param>
    /// <returns>变量项 / The variable item.</returns>
    public CombinationVariableItem<UInt64Number, UInteger> Of(FlightPassenger passenger) {
        ArgumentNullException.ThrowIfNull(passenger);
        UIntVariable1 variables = Variables
            ?? throw new InvalidOperationException("取消变量还没注册 / the cancellation variables have not been registered yet");
        return variables[Index(passenger)];
    }

    /// <summary>
    /// 注册到模型 / Register into the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    /// <remarks>
    /// **重复注册是空操作**：变量已经建过就不再建，也不会往模型里再加一遍——重名符号会让求解器直接拒绝。
    ///
    /// **Re-registration is a no-op**: existing variables are not rebuilt or re-added, because a repeated symbol makes the solver reject the model outright.
    /// </remarks>
    public Try Register(IAbstractLinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (Variables is null) {
            var variables = new UIntVariable1("passenger_cancel", Shape1.Invoke(_passengers.Count));
            for (int index = 0; index < _passengers.Count; ++index) {
                variables[index].Name = "passenger_cancel_" + index;
                variables[index].Range.Leq(new UInt64Number(_passengers[index].Amount));
            }
            Variables = variables;
            Try added = model.Add(variables.Items);
            return added.IsOk ? Results.Ok<Success>(Results.SuccessInstance) : added;
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <remarks>
    /// **按下标取，不按 `Index` 属性取**：`ManualIndexed` 的下标是全局计数器给的，与这里的变量表并不对应；
    /// 直接拿它当数组下标会越界——而越界异常要到某一批数据凑巧超过时才会出现。
    ///
    /// **Indexed by position, not by the `Index` property**: `ManualIndexed`'s index comes from a global counter and does not correspond to this variable table; using it as an array
    /// index goes out of range — and that exception only shows up once some data set happens to exceed the bound.
    /// </remarks>
    private int Index(FlightPassenger passenger) {
        for (int index = 0; index < _passengers.Count; ++index) {
            if (ReferenceEquals(_passengers[index], passenger)) {
                return index;
            }
        }
        throw new ArgumentException("这位乘客不在取消变量表里 / the passenger is not in this cancellation table", nameof(passenger));
    }
}
