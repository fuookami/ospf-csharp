#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
/// <summary>
/// 产能调度场景的资源使用量管理抽象基类 / Abstract base class for resource usage in Capacity Scheduling.
/// </summary>
public abstract class CapacitySchedulingResourceUsage<S, R, C, V>
    : IResourceUsage<S, R, C, V>
    where S : IResourceTimeSlot<R, C, V>
    where R : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract IReadOnlyList<S> TimeSlots { get; }

    /// <inheritdoc/>
    public bool OverEnabled => true;

    /// <inheritdoc/>
    public bool LessEnabled => true;

    /// <inheritdoc/>
    public abstract Try Register(object model);
}
