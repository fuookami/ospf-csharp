// 预约舱位约束 / The appointment rows.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using AircraftPosition = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.Position;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;

/// <summary>
/// 预约舱位约束 / The appointment rows.
/// </summary>
/// <remarks>
/// 对齐：两段，**一段加行、一段只校验**。
///
/// 1. 每件有预约的货物：`stowage_{i, j} = 1`，其中 `j` 是预约舱位在当前舱位表里的下标。预约舱位不在表里是**失败**，不是跳过。
/// 2. 每个舱位：预约到它的货物数量不得超过 `mla`。这一段**不加行**，只报错——数量上限本来就由内核的 `MaxCount` 管，
///    这里提前给一条可读的失败，免得求解器在"不可满足"上耗时间。
///
/// 第 2 段容易被写成加一行 `Σ stowage ≤ mla`，那是**重复约束**：内核已经建了同一个。判据因此断言行数只随第 1 段增长。
///
/// Two halves, **one adds rows and one only validates**. (1) every appointed item gets `stowage_{i, j} = 1` with `j` the appointed position's index in
/// the current list; an appointed position outside the list is a **failure**, not a skip. (2) every position's appointed count must not exceed `mla`; this half **adds no row**
/// and only reports — the count limit already belongs to the kernel's `MaxCount`, and the readable failure just spares the solver from burning time on an infeasible model.
/// Half two is easy to turn into a `Σ stowage ≤ mla` row, which is a **duplicate** of what the kernel built; the criteria therefore assert the row count grows only with half one.
/// </remarks>
public sealed class AppointmentLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Appointment _appointment;
    private readonly StowageModel _stowage;

    /// <summary>
    /// 构造预约舱位约束 / Construct the appointment rows.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="appointment">预约 / The appointment.</param>
    /// <param name="stowage">装载模型 / The stowage model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public AppointmentLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        Appointment appointment,
        StowageModel stowage,
        string name = "appointment_limit") {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(appointment);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _appointment = appointment;
        _stowage = stowage;
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

        for (int i = 0; i < _items.Count; ++i) {
            Item item = _items[i];
            if (_appointment.PositionOf(item) is not { } appointed) {
                continue;
            }
            int positionIndex = IndexOfPosition(appointed);
            if (positionIndex < 0) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "预约舱位不属于当前配载范围：" + appointed.SpaceName +
                    " / the appointed position is not part of the current stowage scope: " + appointed.SpaceName));
            }
            if (_stowage.StowageSymbol(i, positionIndex) is not { } symbol) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "货物 " + item.Name + " 与舱位 " + appointed.SpaceName +
                    " 的装载符号未注册 / the stowage symbol is not registered"));
            }
            string rowName = Name + "_" + item.Name + "_" + appointed.SpaceName;
            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) },
                    Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                Comparison.EQ,
                rowName);
            Try added = linear.AddConstraint(row, group: null, name: rowName);
            if (!added.IsOk) {
                return added;
            }
        }

        // 第二段只校验，不加行：`mla` 的上限已经由内核建好，再加一行是重复约束。
        // The second half only validates and adds no row: the `mla` limit already exists in the kernel, so another row would duplicate it.
        foreach (StowagePosition position in _positions) {
            int appointedCount = _appointment.ItemsOf(position.AircraftPosition).Count;
            if ((ulong)appointedCount > position.Mla) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "指定舱位 " + position.Name + " 的装载数量 " + appointedCount + " 超过该舱位可装载 " + position.Mla +
                    " 的上限 / the assigned item count exceeds the position capacity"));
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    // 按**名字**比舱位，不按引用：调用方可能传一份等价的副本。
    // Positions are matched by **name**, not by reference: a caller may pass an equivalent copy.
    private int IndexOfPosition(AircraftPosition position) {
        for (int j = 0; j < _positions.Count; ++j) {
            if (string.Equals(_positions[j].Name, position.SpaceName, StringComparison.Ordinal)) {
                return j;
            }
        }
        return -1;
    }
}
