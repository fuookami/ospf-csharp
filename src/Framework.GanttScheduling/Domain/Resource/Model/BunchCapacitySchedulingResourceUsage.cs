#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// Bunch 模式的产能调度资源使用量管理 / Bunch-mode resource usage for Capacity Scheduling.
/// </summary>
public class BunchCapacitySchedulingResourceUsage<R, C, V>
    : CapacitySchedulingResourceUsage<CapacityActionResourceTimeSlot<R, C, V>, R, C, V>
    where R : Resource<C, V>, ICapacityActionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    private readonly string _name;

    public BunchCapacitySchedulingResourceUsage(TimeWindow<V> timeWindow, IReadOnlyList<R> resources) {
        _name = "bunch_capacity_scheduling_resource";
        TimeSlots = BuildTimeSlots(timeWindow, resources);
    }

    /// <inheritdoc/>
    public override string Name => _name;

    /// <inheritdoc/>
    public override IReadOnlyList<CapacityActionResourceTimeSlot<R, C, V>> TimeSlots { get; }

    /// <inheritdoc/>
    public override Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);

    private static IReadOnlyList<CapacityActionResourceTimeSlot<R, C, V>> BuildTimeSlots(
        TimeWindow<V> timeWindow, IReadOnlyList<R> resources) {
        var timeSlots = new List<CapacityActionResourceTimeSlot<R, C, V>>();
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
                    timeSlots.Add(new CapacityActionResourceTimeSlot<R, C, V>(time, resource, capacity, index));
                    beginTime += thisInterval;
                    index++;
                }
            }
        }
        return timeSlots;
    }
}
