#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Example.FrameworkDemo.Demo5.Solomon;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Infrastructure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo5.Solomon;

/// <summary>
/// 把 Solomon 原始数值转换为领域值的扩展点 / Extension point converting Solomon raw values into domain values.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface ISolomonValueConverter<V> where V : struct, IRealNumber<V> {
    /// <summary>转换一个原始值 / Convert one raw value.</summary>
    /// <param name="value">原始值 / Raw value.</param>
    /// <returns>领域值 / The domain value.</returns>
    V Convert(FltX value);
}

/// <summary>
/// Solomon 的 <see cref="Flt64"/> 转换器 / Solomon <see cref="Flt64"/> converter.
/// </summary>
public sealed class SolomonFlt64ValueConverter : ISolomonValueConverter<Flt64> {
    /// <inheritdoc/>
    public Flt64 Convert(FltX value) => value.ToFlt64();
}

/// <summary>
/// Solomon 的 <see cref="FltX"/> 转换器 / Solomon <see cref="FltX"/> converter.
/// </summary>
public sealed class SolomonFltXValueConverter : ISolomonValueConverter<FltX> {
    /// <inheritdoc/>
    public FltX Convert(FltX value) => value;
}

/// <summary>
/// 把 demo5 的 Solomon 输入装配为 framework VRPTW 实例 / Assemble demo5 Solomon input into a framework VRPTW instance.
/// </summary>
/// <remarks>
/// 与基线 `framework_demo/demo5/infrastructure/adapter/SolomonInstanceAdapter.kt`（7809 B）对应。
///
/// **所有原始数值在进入领域之前都要过一次非有限检查**。`FltX` 底层是 `decimal`、天然没有 `NaN`/`Infinity`，
/// 但这层检查不能省：转换器是**可替换的实现**，换成 `double` 支撑的类型后，非有限值会一路流进时间窗与需求量，
/// **而失败点会漂到某个"时间窗倒置"的校验上**，与真因无关。
///
/// **Every raw value passes a finiteness check before entering the domain.** `FltX` sits on `decimal` and has no `NaN`/`Infinity` by construction, but the
/// check cannot be dropped: the converter is **a replaceable implementation**, and once it is backed by a `double`-based type, non-finite values flow
/// into time windows and demands, **and the failure surfaces at some unrelated "inverted time window" validation**.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="converter">原始数值转换器 / Raw-value converter.</param>
public sealed class SolomonInstanceAdapter<V>(ISolomonValueConverter<V> converter)
    where V : struct, IRealNumber<V> {
    private readonly ISolomonValueConverter<V> _converter = converter;

    /// <summary>创建 VRPTW 实例 / Create a VRPTW instance.</summary>
    /// <param name="data">Solomon 原始数据 / Solomon raw data.</param>
    /// <param name="units">实例单位 / Instance units.</param>
    /// <param name="fixedVehicleCost">固定车辆成本 / Fixed vehicle cost.</param>
    /// <param name="schedulingWindow">业务绝对时间轴与 solver 数值转换 / Business absolute timeline and solver numeric conversion.</param>
    /// <param name="tolerances">算法容差 / Algorithm tolerances.</param>
    /// <returns>VRPTW 实例或装配失败 / The VRPTW instance, or an assembly failure.</returns>
    public Result<VrptwInstance<V>, ErrorCode, Error<ErrorCode>> Create(
        SolomonData data,
        VrptwUnits units,
        Quantity<V> fixedVehicleCost,
        TimeWindow<V> schedulingWindow,
        VrptwTolerances? tolerances = null) {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(fixedVehicleCost);
        ArgumentNullException.ThrowIfNull(schedulingWindow);

        Result<NetworkNode<V>, ErrorCode, Error<ErrorCode>> startNode =
            Node(data.Depot, "depot-start", units);
        if (startNode is not Ok<NetworkNode<V>, ErrorCode, Error<ErrorCode>> startOk) {
            return Results.Failed<VrptwInstance<V>>(ErrorOf(startNode));
        }
        Result<NetworkNode<V>, ErrorCode, Error<ErrorCode>> endNode =
            Node(data.Depot, "depot-end", units);
        if (endNode is not Ok<NetworkNode<V>, ErrorCode, Error<ErrorCode>> endOk) {
            return Results.Failed<VrptwInstance<V>>(ErrorOf(endNode));
        }
        Result<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> depotWindow =
            TimeWindowOf(data.Depot, schedulingWindow);
        if (depotWindow is not Ok<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> depotWindowOk) {
            return Results.Failed<VrptwInstance<V>>(ErrorOf(depotWindow));
        }

        var customers = new List<Customer<V>>(data.Customers.Count);
        foreach (SolomonNodeData row in data.Customers) {
            Result<NetworkNode<V>, ErrorCode, Error<ErrorCode>> customerNode =
                Node(row, $"customer-{row.Id}", units);
            if (customerNode is not Ok<NetworkNode<V>, ErrorCode, Error<ErrorCode>> nodeOk) {
                return Results.Failed<VrptwInstance<V>>(ErrorOf(customerNode));
            }
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> demand =
                Value(row.Demand, units.LoadUnit);
            if (demand is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> demandOk) {
                return Results.Failed<VrptwInstance<V>>(ErrorOf(demand));
            }
            Result<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> window =
                TimeWindowOf(row, schedulingWindow);
            if (window is not Ok<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> windowOk) {
                return Results.Failed<VrptwInstance<V>>(ErrorOf(window));
            }
            Result<TimeSpan, ErrorCode, Error<ErrorCode>> serviceTime =
                Duration(row.ServiceTime, schedulingWindow);
            if (serviceTime is not Ok<TimeSpan, ErrorCode, Error<ErrorCode>> serviceOk) {
                return Results.Failed<VrptwInstance<V>>(ErrorOf(serviceTime));
            }
            Result<Customer<V>, ErrorCode, Error<ErrorCode>> customer = Customer<V>.Invoke(
                id: new CustomerId(row.Id),
                node: nodeOk.Value,
                demand: demandOk.Value,
                timeWindow: windowOk.Value,
                serviceTime: serviceOk.Value);
            if (customer is not Ok<Customer<V>, ErrorCode, Error<ErrorCode>> customerOk) {
                return Results.Failed<VrptwInstance<V>>(ErrorOf(customer));
            }
            customers.Add(customerOk.Value);
        }

        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> capacity =
            Value(data.Vehicle.Capacity, units.LoadUnit);
        if (capacity is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> capacityOk) {
            return Results.Failed<VrptwInstance<V>>(ErrorOf(capacity));
        }
        Result<VehicleType<V>, ErrorCode, Error<ErrorCode>> vehicleType =
            VehicleType<V>.Invoke(
                id: new VehicleTypeId("solomon-vehicle"),
                capacity: capacityOk.Value,
                fixedCost: fixedVehicleCost,
                amount: data.Vehicle.Amount);
        if (vehicleType is not Ok<VehicleType<V>, ErrorCode, Error<ErrorCode>> vehicleOk) {
            return Results.Failed<VrptwInstance<V>>(ErrorOf(vehicleType));
        }

        return VrptwInstance<V>.Invoke(
            name: data.Name,
            startDepot: new Depot<V>(startOk.Value, depotWindowOk.Value),
            endDepot: new Depot<V>(endOk.Value, depotWindowOk.Value),
            customers: customers,
            vehicleTypes: new[] { vehicleOk.Value },
            schedulingWindow: schedulingWindow,
            units: units,
            tolerances: tolerances ?? VrptwTolerances.Default);
    }

    private Result<NetworkNode<V>, ErrorCode, Error<ErrorCode>> Node(
        SolomonNodeData row,
        string id,
        VrptwUnits units) {
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> x =
            Value(row.X, units.DistanceUnit);
        if (x is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> xOk) {
            return Results.Failed<NetworkNode<V>>(ErrorOf(x));
        }
        Result<Quantity<V>, ErrorCode, Error<ErrorCode>> y =
            Value(row.Y, units.DistanceUnit);
        if (y is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> yOk) {
            return Results.Failed<NetworkNode<V>>(ErrorOf(y));
        }
        return NetworkNode<V>.Invoke(
            id: new NetworkNodeId(id),
            attributes: new Dictionary<string, Quantity<V>>(StringComparer.Ordinal) {
                ["x"] = xOk.Value,
                ["y"] = yOk.Value,
            });
    }

    private Result<ServiceTimeWindow, ErrorCode, Error<ErrorCode>> TimeWindowOf(
        SolomonNodeData row,
        TimeWindow<V> schedulingWindow) {
        Result<DateTimeOffset, ErrorCode, Error<ErrorCode>> ready =
            Instant(row.ReadyTime, schedulingWindow);
        if (ready is not Ok<DateTimeOffset, ErrorCode, Error<ErrorCode>> readyOk) {
            return Results.Failed<ServiceTimeWindow>(ErrorOf(ready));
        }
        Result<DateTimeOffset, ErrorCode, Error<ErrorCode>> due =
            Instant(row.DueTime, schedulingWindow);
        if (due is not Ok<DateTimeOffset, ErrorCode, Error<ErrorCode>> dueOk) {
            return Results.Failed<ServiceTimeWindow>(ErrorOf(due));
        }
        return ServiceTimeWindow.Invoke(readyOk.Value, dueOk.Value);
    }

    private Result<DateTimeOffset, ErrorCode, Error<ErrorCode>> Instant(
        FltX raw,
        TimeWindow<V> schedulingWindow) {
        Result<V, ErrorCode, Error<ErrorCode>> converted = Convert(raw);
        return converted is Ok<V, ErrorCode, Error<ErrorCode>> convertedOk
            ? Results.Ok(schedulingWindow.InstantOf(ToFlt64(convertedOk.Value)))
            : Results.Failed<DateTimeOffset>(ErrorOf(converted));
    }

    private Result<TimeSpan, ErrorCode, Error<ErrorCode>> Duration(
        FltX raw,
        TimeWindow<V> schedulingWindow) {
        Result<V, ErrorCode, Error<ErrorCode>> converted = Convert(raw);
        return converted is Ok<V, ErrorCode, Error<ErrorCode>> convertedOk
            ? Results.Ok(schedulingWindow.DurationOf(ToFlt64(convertedOk.Value)))
            : Results.Failed<TimeSpan>(ErrorOf(converted));
    }

    private Result<Quantity<V>, ErrorCode, Error<ErrorCode>> Value(
        FltX raw,
        PhysicalUnit unit) {
        Result<V, ErrorCode, Error<ErrorCode>> converted = Convert(raw);
        return converted is Ok<V, ErrorCode, Error<ErrorCode>> convertedOk
            ? Results.Ok(new Quantity<V>(convertedOk.Value, unit))
            : Results.Failed<Quantity<V>>(ErrorOf(converted));
    }

    private Result<V, ErrorCode, Error<ErrorCode>> Convert(FltX raw) {
        V value = _converter.Convert(raw);
        // **非有限值必须在这里拒**：转换器可替换，换成 double 支撑的类型后 `NaN` 会一路流进时间窗与需求量，
        // **而失败点会漂到某个"时间窗倒置"的校验上**，与真因无关。
        // **Non-finite values must be rejected here**: the converter is replaceable, and once it is backed by a double-based type `NaN` flows into time
        // windows and demands, **and the failure surfaces at some unrelated "inverted time window" validation**.
        double probe = ToFlt64(value).ToDouble();
        return double.IsFinite(probe)
            ? Results.Ok(value)
            : NetworkSchedulingErrors.Failure<V>(
                "装配 Solomon 实例失败：数值必须有限 / Failed to assemble Solomon instance: numeric value must be finite");
    }

    private static Flt64 ToFlt64(V value) => value.ToFlt64();

    private static Error<ErrorCode> ErrorOf<T>(
        Result<T, ErrorCode, Error<ErrorCode>> source) =>
        ((Failed<T, ErrorCode, Error<ErrorCode>>)source).Error;
}
