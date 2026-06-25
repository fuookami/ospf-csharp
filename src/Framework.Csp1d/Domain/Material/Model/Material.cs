#nullable enable

using Fuookami.Ospf.Quantities.Quantity;
using System.Collections.Generic;
using System.Linq;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 分切物料 / Cutting material
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class Material<V> : IProduction<V> where V : struct {
    /// <summary>
    /// 构造物料实例 / Construct a material instance
    /// </summary>
    /// <param name="id">物料标识 / Material identifier.</param>
    /// <param name="name">物料名称 / Material name.</param>
    /// <param name="widthRange">可用幅宽范围 / Available width range.</param>
    /// <param name="length">卷长 / Coil length.</param>
    /// <param name="unitWeight">单位重量 / Unit weight.</param>
    /// <param name="machineId">设备标识 / Machine identifier.</param>
    /// <param name="availableBatches">可用批次数 / Available batches.</param>
    public Material(
        string id,
        string name,
        WidthRange<V> widthRange,
        Quantity<V>? length = null,
        Quantity<V>? unitWeight = null,
        string? machineId = null,
        UInt64? availableBatches = null) {
        Id = id;
        Name = name;
        WidthRange = widthRange;
        Length = length;
        UnitWeight = unitWeight;
        MachineId = machineId;
        AvailableBatches = availableBatches ?? new UInt64(ulong.MaxValue);
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <summary>物料名称 / Material name.</summary>
    public string Name { get; }

    /// <summary>可用幅宽范围 / Available width range.</summary>
    public WidthRange<V> WidthRange { get; }

    /// <inheritdoc/>
    public Quantity<V>? Length { get; }

    /// <inheritdoc/>
    public Quantity<V>? UnitWeight { get; }

    /// <summary>设备标识 / Machine identifier.</summary>
    public string? MachineId { get; }

    /// <summary>
    /// 可用批次数，主问题按物料方案使用量求和建模 / Available batches modeled by material plan usage sum in master problem
    /// </summary>
    public UInt64 AvailableBatches { get; }

    private IReadOnlyList<Quantity<V>>? _cachedWidth;

    /// <inheritdoc/>
    public IReadOnlyList<Quantity<V>> Width {
        get {
            _cachedWidth ??= new[] { WidthRange.LowerBound, WidthRange.UpperBound };
            return _cachedWidth;
        }
    }

    /// <summary>
    /// 判断切割方案是否满足物料基础约束 / Check whether a cutting plan satisfies material basic constraints
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <returns>是否满足 / Whether enabled.</returns>
    public bool Enabled(CuttingPlan<V> plan) {
        if (!EnabledWithoutWidthCheck(plan)) {
            return false;
        }

        Quantity<V>? usedWidth = plan.UsedWidth;
        if (usedWidth == null) {
            return false;
        }

        return WidthRange.CanCut(usedWidth);
    }

    /// <summary>
    /// 判断切割方案是否满足物料非宽度基础约束（material id + machine id 匹配）/
    /// Check whether a cutting plan satisfies material non-width basic constraints
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <returns>是否满足 / Whether enabled.</returns>
    public bool EnabledWithoutWidthCheck(CuttingPlan<V> plan) {
        if (plan.Material.Id != Id) {
            return false;
        }

        if (MachineId != null && plan.MachineId != null && MachineId != plan.MachineId) {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 判断切割方案是否满足物料和设备基础约束 / Check whether a cutting plan satisfies material and machine basic constraints
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <param name="machines">可用设备列表 / Available machines.</param>
    /// <returns>是否满足 / Whether enabled.</returns>
    public bool Enabled(CuttingPlan<V> plan, IReadOnlyList<Machine<V>> machines) {
        if (!Enabled(plan)) {
            return false;
        }

        string? planMachineId = plan.MachineId;
        if (planMachineId == null) {
            return true;
        }

        Machine<V>? machine = machines.FirstOrDefault(m => m.Id == planMachineId);
        return machine == null || machine.Enabled(this);
    }

    /// <summary>
    /// 判断切割方案是否满足物料非宽度和设备基础约束 /
    /// Check whether a cutting plan satisfies material non-width and machine basic constraints
    /// </summary>
    /// <param name="plan">切割方案 / Cutting plan.</param>
    /// <param name="machines">可用设备列表 / Available machines.</param>
    /// <returns>是否满足 / Whether enabled.</returns>
    public bool EnabledWithoutWidthCheck(CuttingPlan<V> plan, IReadOnlyList<Machine<V>> machines) {
        if (!EnabledWithoutWidthCheck(plan)) {
            return false;
        }

        string? planMachineId = plan.MachineId;
        if (planMachineId == null) {
            return true;
        }

        Machine<V>? machine = machines.FirstOrDefault(m => m.Id == planMachineId);
        return machine == null || machine.Enabled(this);
    }
}
