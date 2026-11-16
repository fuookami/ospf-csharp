#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// Plan 模式的产能调度资源使用量管理 / Plan-mode resource usage for Capacity Scheduling.
/// </summary>
public class PlanCapacitySchedulingResourceUsage<R, C, V>
    : CapacitySchedulingResourceUsage<CapacityActionResourceTimeSlot<R, C, V>, R, C, V>
    where R : Resource<C, V>, ICapacityActionResource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="timeWindow">时间窗口 / The time window.</param>
    /// <param name="resources">资源列表 / The resources.</param>
    /// <param name="amounts">产能决策变量视图 / The capacity decision-variable view.</param>
    /// <remarks>
    /// **之后这个类只剩一个名字**：它原先与 BunchCapacitySchedulingResourceUsage 逐字相同（整份文件只差名字字符串），
    /// 切时隙与注册失败的处理都已上移到 `CapacitySchedulingResourceUsage`。
    ///
    /// **After this class is just a name**: it used to be byte-identical to BunchCapacitySchedulingResourceUsage (the whole file differed only in the name string), and both slot cutting and the failing
    /// registration moved up into `CapacitySchedulingResourceUsage`.
    /// </remarks>
    public PlanCapacitySchedulingResourceUsage(TimeWindow<V> timeWindow, IReadOnlyList<R> resources, ICapacityAmountSource? amounts = null)
        : base("plan_capacity_scheduling_resource", timeWindow, resources, amounts) {
    }
}
