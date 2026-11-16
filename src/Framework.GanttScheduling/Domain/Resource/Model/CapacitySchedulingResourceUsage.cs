#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using ValueRange = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Fuookami.Ospf.Math.Algebra.Number.Flt64>;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// 产能调度场景的资源使用量管理抽象基类 / Abstract base class for resource usage in Capacity Scheduling.
/// </summary>
public abstract class CapacitySchedulingResourceUsage<S, R, C, V>
    : IResourceUsage<S, R, C, V>
    where S : IResourceTimeSlot<R, C, V>
    where R : Resource<C, V>, ICapacityActionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 空构造，供不需要自动切时隙的子类使用 / Empty constructor for subclasses that cut their own slots.
    /// </summary>
    protected CapacitySchedulingResourceUsage() {
        Name = string.Empty;
        TimeSlots = Array.Empty<S>();
    }

    private readonly ICapacityAmountSource? _amounts;

    /// <summary>用量表 / The usage table.</summary>
    /// <remarks>
    /// **注册前是 `null`，不是空字典**：空字典读起来像"这张表确实没有量"，而实际是"还没注册"。
    ///
    /// **`null` rather than an empty dictionary before registration**: an empty dictionary reads as "this table genuinely holds nothing" when the truth is "not registered yet".
    /// </remarks>
    public IReadOnlyDictionary<S, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>? QuantityTable { get; private set; }

    /// <summary>
    /// 构造时把各资源容量的时间切成时隙 / Cut each resource capacity's time into slots at construction.
    /// </summary>
    /// <param name="name">名称 / The name.</param>
    /// <param name="timeWindow">时间窗口 / The time window.</param>
    /// <param name="resources">资源列表 / The resources.</param>
    /// <param name="amounts">产能决策变量视图，缺省则无法填表 / The capacity decision-variable view; without it the table cannot be filled.</param>
    protected CapacitySchedulingResourceUsage(
        string name,
        TimeWindow<V> timeWindow,
        IReadOnlyList<R> resources,
        ICapacityAmountSource? amounts = null) {
        Name = name;
        TimeSlots = BuildTimeSlots(timeWindow, resources);
        _amounts = amounts;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public IReadOnlyList<S> TimeSlots { get; }

    /// <summary>
    /// 切时隙 / Cut the time slots.
    /// </summary>
    /// <param name="timeWindow">时间窗口 / The time window.</param>
    /// <param name="resources">资源列表 / The resources.</param>
    /// <returns>时隙列表 / The slots.</returns>
    /// <remarks>
    /// **两个子类的 `BuildTimeSlots` 逐字相同**：整份文件除名字字符串外没有任何差异，
    /// 复制两份的唯一后果是改一处忘另一处。放到基类里，子类只剩名字。
    ///
    /// **The two subclasses' `BuildTimeSlots` were byte-identical**: the whole file differed only in the name string, and keeping two copies only guarantees that one edit forgets the
    /// other. It lives in the base now and the subclasses keep just their name.
    /// </remarks>
    private static IReadOnlyList<S> BuildTimeSlots(
        TimeWindow<V> timeWindow, IReadOnlyList<R> resources) {
        var timeSlots = new List<S>();
        foreach (R resource in resources) {
            foreach (C capacity in resource.Capacities) {
                ulong index = 0;
                DateTimeOffset beginTime = capacity.Time.Start > timeWindow.Start ? capacity.Time.Start : timeWindow.Start;
                DateTimeOffset endTime = capacity.Time.End < timeWindow.End ? capacity.Time.End : timeWindow.End;
                while (beginTime < endTime) {
                    TimeSpan thisInterval = endTime - beginTime;
                    if (thisInterval > capacity.Interval) {
                        thisInterval = capacity.Interval;
                    }

                    if (thisInterval > timeWindow.Interval) {
                        thisInterval = timeWindow.Interval;
                    }

                    var time = new TimeRange(beginTime, beginTime + thisInterval);
                    timeSlots.Add((S)(object)new CapacityActionResourceTimeSlot<R, C, V>(time, resource, capacity, index));
                    beginTime += thisInterval;
                    index++;
                }
            }
        }
        return timeSlots;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **失败原因写的是缺什么，不是"还没做"**：产能调度的决策变量本身已经存在（`CapacityDecisionVariables`、`CapacityOrderVariables`），
    /// 缺的是「资源时隙 → 占它的那些动作的决策变量」这条映射：动作时隙与资源时隙是两套网格，重叠换算尚未实现。
    /// **一句过期的实话比一句含混的话更糟**，它会让下一个读代码的人去找一个早就建好的东西。
    ///
    /// **The failure names what is missing rather than "not done yet"**: capacity scheduling's decision variables do exist (`CapacityDecisionVariables`, `CapacityOrderVariables`); what is
    /// missing is the mapping from a resource time slot to the decision variables of the actions occupying it — action slots and resource slots are two different grids and the overlap
    /// conversion is not implemented. **A stale true-sounding sentence is worse than a vague one**, because it sends the next reader hunting for something already built.
    /// </remarks>
    public Try Register(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "资源使用只能注册到线性元模型 / resource usage can only be registered into a linear meta model"));
        }
        if (TimeSlots.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (_amounts is null || !_amounts.IsRegistered) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "容量调度缺的是「每个资源时隙上挂哪些动作的哪个决策变量」这条映射：决策变量本身（724 起）和数量立方（726 起）都已就位，"
                + "但构造时必须把 `ICapacityAmountSource` 传进来，资源用量表才能按重叠时隙填出用量 / "
                + "capacity scheduling needs the mapping from a resource time slot to the decision variables of the actions that occupy it: the decision variables (since batch 724) and the "
                + "amount cube (since batch 726) exist, but an `ICapacityAmountSource` must be passed to the constructor so the usage table can be filled from overlapping slots"));
        }

        var quantities = new Dictionary<S, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>();
        foreach (S slot in TimeSlots) {
            var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
            foreach (IProductionAction action in _amounts.Actions) {
                for (int index = 0; index < _amounts.ActionSlots.Count; ++index) {
                    TimeRange actionSlot = _amounts.ActionSlots[index];
                    if (actionSlot.End <= slot.Time.Start || actionSlot.Start >= slot.Time.End) {
                        continue;
                    }
                    Flt64 used = slot.Resource.UsedBy(action, actionSlot).ToSolverValue();
                    if (used.ToDouble() == 0.0) {
                        continue;
                    }
                    monomials.Add(new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                        used, _amounts.AmountOf(action, index)));
                }
            }

            var symbol = new Fuookami.Ospf.Core.Symbol.LinearExpressionSymbol(
                new Fuookami.Ospf.Math.Symbol.Polynomial.MutableLinearPolynomial<Flt64>(
                    monomials, constant: Flt64.Zero),
                name: Name + "_quantity_" + slot.Index);
            Result<ValueRange, ErrorCode, Error<ErrorCode>> range = ValueRange.Of(
                Flt64.Zero, slot.ResourceCapacity.SolverUpperBound());
            if (range is not Ok<ValueRange, ErrorCode, Error<ErrorCode>> inRange) {
                return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                    ((Failed<ValueRange, ErrorCode, Error<ErrorCode>>)range).Error);
            }
            symbol.Range.SetRange(inRange.Value);
            quantities[slot] = symbol;
        }
        QuantityTable = quantities;
        return linear.Add(quantities.Values);
    }

    /// <inheritdoc/>
    public bool OverEnabled => true;

    /// <inheritdoc/>
    public bool LessEnabled => true;
}
