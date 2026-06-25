#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Concept;
using System;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Service.Limits;
/// <summary>
/// 影子价格键类型安全转换支持 / Shadow price key type-safe cast support.
/// </summary>
internal static class ShadowPriceKeyCastSupport {
    /// <summary>将约束参数安全收敛为目标影子价格 key 类型 / Safely narrows constraint args to the target shadow-price key type.</summary>
    public static K? ShadowPriceKeyOf<K>(object? args) where K : ShadowPriceKey
        => args as K;
}

/// <summary>
/// 资源容量影子价格键 / Resource capacity shadow price key.
/// </summary>
public sealed class ResourceCapacityShadowPriceKey<R, C, V> : ShadowPriceKey
    where R : Resource<C, V>
    where C : IAbstractResourceCapacity<V>
    where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造函数 / Constructor.
    /// </summary>
    /// <param name="slot">资源时间槽 / Resource time slot.</param>
    public ResourceCapacityShadowPriceKey(IResourceTimeSlot<R, C, V> slot)
        : base(typeof(ResourceCapacityShadowPriceKey<R, C, V>)) {
        Slot = slot;
    }

    /// <summary>资源时间槽 / Resource time slot.</summary>
    public IResourceTimeSlot<R, C, V> Slot { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is ResourceCapacityShadowPriceKey<R, C, V> other
           && Slot.Resource.Id == other.Slot.Resource.Id
           && Slot.IndexInRule == other.Slot.IndexInRule;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Slot.Resource.Id, Slot.IndexInRule);
}
