#nullable enable

using Fuookami.Ospf.Quantities.Quantity;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 分切设备 / Slitting machine
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Machine<V> where V : struct {
    /// <summary>
    /// 构造设备实例 / Construct a machine instance
    /// </summary>
    /// <param name="id">设备标识 / Machine identifier.</param>
    /// <param name="name">设备名称 / Machine name.</param>
    /// <param name="maxBatchCount">最大批次数 / Maximum batch count.</param>
    /// <param name="maxSwitchCount">最大换料次数 / Maximum material switch count.</param>
    /// <param name="widthRange">可加工幅宽范围 / Processable width range.</param>
    /// <param name="capacity">业务产能上限 / Business capacity upper bound.</param>
    public Machine(
        string id,
        string name,
        UInt64? maxBatchCount = null,
        UInt64? maxSwitchCount = null,
        WidthRange<V>? widthRange = null,
        Quantity<V>? capacity = null) {
        Id = id;
        Name = name;
        MaxBatchCount = maxBatchCount;
        MaxSwitchCount = maxSwitchCount;
        WidthRange = widthRange;
        Capacity = capacity;
    }

    /// <summary>设备标识 / Machine identifier.</summary>
    public string Id { get; }

    /// <summary>设备名称 / Machine name.</summary>
    public string Name { get; }

    /// <summary>
    /// 最大批次数，主问题按设备方案使用量求和建模 / Maximum batch count modeled by plan usage sum in master problem
    /// </summary>
    public UInt64? MaxBatchCount { get; }

    /// <summary>
    /// 最大换料次数，需要加工序列变量，当前无序主问题不建模 / Maximum material switch count requiring sequence variables, not modeled in current unordered master problem
    /// </summary>
    public UInt64? MaxSwitchCount { get; }

    /// <summary>可加工幅宽范围 / Processable width range.</summary>
    public WidthRange<V>? WidthRange { get; }

    /// <summary>
    /// 业务产能上限，仅与同单位方案产能消耗一起建模 / Business capacity upper bound modeled only with same-unit plan consumption
    /// </summary>
    public Quantity<V>? Capacity { get; }

    /// <summary>
    /// 判断是否可加工给定物料 / Check whether the machine can process the material
    /// </summary>
    /// <param name="material">物料 / Material.</param>
    /// <returns>是否可加工 / Whether enabled.</returns>
    public bool Enabled(Material<V> material) {
        WidthRange<V>? thisWidthRange = WidthRange;
        if (thisWidthRange == null) {
            return true;
        }

        return thisWidthRange.Width.Contains(material.WidthRange.LowerBound)
            && thisWidthRange.Width.Contains(material.WidthRange.UpperBound);
    }
}
