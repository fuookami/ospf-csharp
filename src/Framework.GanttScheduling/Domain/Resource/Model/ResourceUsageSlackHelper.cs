#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;

/// <summary>
/// 资源使用的松弛表构建 / Builds the slack tables of a resource usage.
/// </summary>
/// <remarks>
/// 执行、连接、存储三种资源使用的超量 \/ 不足两张表**逐字相同**：按槽建 `x − 上界` 的正部与 `x − 下界` 的负部，
/// 槽自己没开就给空符号，然后把表加进模型。三份抄下来，改一处必然漏两处——本类只做这一件事。
///
/// The over and less tables are **word-for-word identical** across the execution, connection and storage usages: per slot, the positive part of `x - upper` and the negative part of
/// `x - lower`, an empty symbol when the slot's own switch is off, then the table goes into the model. Three copies would guarantee that fixing one misses the other two; this class does
/// only that one thing.
/// </remarks>
public static class ResourceUsageSlackHelper {
    /// <summary>
    /// 建超量 \/ 不足两张表并加进模型 / Build both slack tables and add them to the model.
    /// </summary>
    /// <typeparam name="TSlot">时间槽类型 / The time slot type.</typeparam>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <param name="name">名称前缀 / The name prefix.</param>
    /// <param name="slots">时间槽列表 / The time slots.</param>
    /// <param name="quantity">数量表 / The quantity table.</param>
    /// <param name="over">超量表输出 / The over table output.</param>
    /// <param name="less">不足量表输出 / The less table output.</param>
    /// <param name="overEnabled">资源级超量开关 / The resource-level over switch.</param>
    /// <param name="lessEnabled">资源级不足开关 / The resource-level less switch.</param>
    /// <param name="slotOverEnabled">槽级超量开关 / The slot-level over switch.</param>
    /// <param name="slotLessEnabled">槽级不足开关 / The slot-level less switch.</param>
    /// <param name="upper">槽的上界 / The slot's upper bound.</param>
    /// <param name="lower">槽的下界 / The slot's lower bound.</param>
    /// <param name="id">槽在符号名里的键 / The slot's key in symbol names.</param>
    /// <returns>成功或错误 / Success or an error.</returns>
    /// <remarks>
    /// **槽自己的开关要单独看**：资源级开着不等于这个容量开超量；没开就给空符号，而不是给一个恒为零的松弛——
    /// 零松弛会让容量约束看起来加了、其实没起作用。
    ///
    /// **The slot's own switch is checked separately**: a resource-level flag does not mean this capacity has over enabled; when it does not, use an empty symbol rather than a slack that is
    /// identically zero — a zero slack makes the capacity constraint look added while doing nothing.
    /// </remarks>
    public static Try BuildSlacks<TSlot>(
        IAbstractLinearMetaModel<Flt64> linear,
        string name,
        IReadOnlyList<TSlot> slots,
        IReadOnlyDictionary<TSlot, ILinearIntermediateSymbol<Flt64>> quantity,
        out IReadOnlyDictionary<TSlot, ILinearIntermediateSymbol<Flt64>>? over,
        out IReadOnlyDictionary<TSlot, ILinearIntermediateSymbol<Flt64>>? less,
        bool overEnabled,
        bool lessEnabled,
        Func<TSlot, bool> slotOverEnabled,
        Func<TSlot, bool> slotLessEnabled,
        Func<TSlot, Flt64> upper,
        Func<TSlot, Flt64> lower,
        Func<TSlot, string> id)
        where TSlot : notnull {
        ArgumentNullException.ThrowIfNull(linear);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(quantity);

        over = null;
        less = null;

        if (overEnabled) {
            var table = new Dictionary<TSlot, ILinearIntermediateSymbol<Flt64>>();
            foreach (TSlot slot in slots) {
                table[slot] = slotOverEnabled(slot)
                    ? Produce.Model.ProduceSlackHelper.Build(
                        quantity[slot],
                        upper(slot),
                        withNegative: false,
                        withPositive: true,
                        constraint: false,
                        name + "_over_quantity_" + id(slot))
                    : Produce.Model.ProduceSlackHelper.EmptySymbol(name + "_over_quantity_" + id(slot));
            }
            over = table;
            Try addedOver = linear.Add(table.Values);
            if (!addedOver.IsOk) {
                return addedOver;
            }
        }

        if (lessEnabled) {
            var table = new Dictionary<TSlot, ILinearIntermediateSymbol<Flt64>>();
            foreach (TSlot slot in slots) {
                table[slot] = slotLessEnabled(slot)
                    ? Produce.Model.ProduceSlackHelper.Build(
                        quantity[slot],
                        lower(slot),
                        withNegative: true,
                        withPositive: false,
                        constraint: false,
                        name + "_less_quantity_" + id(slot))
                    : Produce.Model.ProduceSlackHelper.EmptySymbol(name + "_less_quantity_" + id(slot));
            }
            less = table;
            Try addedLess = linear.Add(table.Values);
            if (!addedLess.IsOk) {
                return addedLess;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
