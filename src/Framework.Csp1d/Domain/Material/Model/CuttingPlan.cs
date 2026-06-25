#nullable enable

using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
/// <summary>
/// 切割方案 / Cutting plan
/// </summary>
/// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
public sealed class CuttingPlan<V> : ICuttingPlanForCanonicalKey where V : struct {
    private readonly Lazy<Result<IQuantityArithmetic<V>, ErrorCode, Error<ErrorCode>>> _resolvedArithmeticResult;
    private Quantity<V>? _cachedUsedWidth;
    private bool _usedWidthCached;
    private Quantity<V>? _cachedRestWidth;
    private bool _restWidthCached;

    /// <summary>
    /// 构造切割方案 / Construct a cutting plan
    /// </summary>
    /// <param name="id">方案标识 / Plan identifier.</param>
    /// <param name="material">物料 / Material.</param>
    /// <param name="slices">切片列表 / Cut slices.</param>
    /// <param name="machineId">设备标识 / Machine identifier.</param>
    /// <param name="demandContributions">需求贡献 / Demand contributions.</param>
    /// <param name="arithmetic">物理量算术策略 / Quantity arithmetic strategy.</param>
    /// <param name="capacityConsumption">单次方案使用的设备产能消耗 / Machine capacity consumed by one plan usage.</param>
    public CuttingPlan(
        string id,
        Material<V> material,
        IReadOnlyList<CuttingPlanSlice<V>> slices,
        string? machineId = null,
        IReadOnlyList<CuttingPlanDemandContribution<V>>? demandContributions = null,
        IQuantityArithmetic<V>? arithmetic = null,
        Quantity<V>? capacityConsumption = null) {
        Id = id;
        Material = material;
        MachineId = machineId ?? material.MachineId;
        Slices = slices;
        DemandContributions = demandContributions ?? Array.Empty<CuttingPlanDemandContribution<V>>();
        Arithmetic = arithmetic;
        CapacityConsumption = capacityConsumption;

        _resolvedArithmeticResult = new Lazy<Result<IQuantityArithmetic<V>, ErrorCode, Error<ErrorCode>>>(() =>
            Arithmetic != null
                ? new Ok<IQuantityArithmetic<V>, ErrorCode, Error<ErrorCode>>(Arithmetic)
                : DefaultQuantityArithmetic.ResolveFor(material.WidthRange.UpperBound.Value));
    }

    /// <summary>方案标识 / Plan identifier.</summary>
    public string Id { get; }

    /// <summary>物料 / Material.</summary>
    public Material<V> Material { get; }

    /// <summary>设备标识 / Machine identifier.</summary>
    public string? MachineId { get; }

    /// <summary>切片列表 / Cut slices.</summary>
    public IReadOnlyList<CuttingPlanSlice<V>> Slices { get; }

    /// <summary>需求贡献 / Demand contributions.</summary>
    public IReadOnlyList<CuttingPlanDemandContribution<V>> DemandContributions { get; }

    /// <summary>物理量算术策略 / Quantity arithmetic strategy.</summary>
    public IQuantityArithmetic<V>? Arithmetic { get; }

    /// <summary>
    /// 单次方案使用的设备产能消耗，必须与设备产能同单位才进入主问题约束 / Machine capacity consumed by one plan usage
    /// </summary>
    public Quantity<V>? CapacityConsumption { get; }

    /// <summary>解析物理量算术策略 / Resolved quantity arithmetic strategy.</summary>
    public Result<IQuantityArithmetic<V>, ErrorCode, Error<ErrorCode>> ResolvedArithmetic => _resolvedArithmeticResult.Value;

    /// <summary>已使用幅宽 / Used width.</summary>
    public Quantity<V>? UsedWidth {
        get {
            if (_usedWidthCached) {
                return _cachedUsedWidth;
            }

            _usedWidthCached = true;

            if (!ResolvedArithmetic.IsOk) {
                return null;
            }

            IQuantityArithmetic<V> arith = ResolvedArithmetic.Value;

            Quantity<V>? result = null;
            foreach (CuttingPlanSlice<V> slice in Slices) {
                Quantity<V>? sliceWidth = slice.Width.Repeat(slice.Amount, arith);
                if (sliceWidth == null) { _cachedUsedWidth = null; return null; }
                if (result == null) {
                    result = sliceWidth;
                }
                else {
                    Result<Quantity<V>, ErrorCode, Error<ErrorCode>> addResult = arith.Add(result, sliceWidth);
                    if (addResult.IsFailed) { _cachedUsedWidth = null; return null; }
                    result = addResult.Value;
                }
            }
            _cachedUsedWidth = result;
            return result;
        }
    }

    /// <summary>剩余幅宽 / Remaining width.</summary>
    public Quantity<V>? RestWidth {
        get {
            if (_restWidthCached) {
                return _cachedRestWidth;
            }

            _restWidthCached = true;

            Quantity<V>? currentUsedWidth = UsedWidth;
            if (currentUsedWidth == null) { _cachedRestWidth = null; return null; }

            Quantity<V> upperBound = Material.WidthRange.UpperBound;
            if (upperBound.Unit != currentUsedWidth.Unit) { _cachedRestWidth = null; return null; }

            if (!ResolvedArithmetic.IsOk) { _cachedRestWidth = null; return null; }
            IQuantityArithmetic<V> arith = ResolvedArithmetic.Value;

            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> subResult = arith.Subtract(upperBound, currentUsedWidth);
            _cachedRestWidth = subResult.IsOk ? subResult.Value : null;
            return _cachedRestWidth;
        }
    }

    // ===== ICuttingPlanForCanonicalKey implementation =====

    /// <inheritdoc/>
    string ICuttingPlanForCanonicalKey.MaterialId => Material.Id;

    /// <inheritdoc/>
    string? ICuttingPlanForCanonicalKey.MachineId => MachineId;

    /// <inheritdoc/>
    string? ICuttingPlanForCanonicalKey.CapacityConsumptionKey => CapacityConsumption?.ToString();

    /// <inheritdoc/>
    IReadOnlyList<ICuttingPlanSliceForCanonicalKey> ICuttingPlanForCanonicalKey.Slices =>
        Slices.Cast<ICuttingPlanSliceForCanonicalKey>().ToList();

    /// <inheritdoc/>
    IReadOnlyList<ICuttingPlanDemandContributionForCanonicalKey> ICuttingPlanForCanonicalKey.DemandContributions =>
        DemandContributions.Cast<ICuttingPlanDemandContributionForCanonicalKey>().ToList();
}

/// <summary>
/// 物理量按离散次数累加的扩展方法 / Extension method for repeating a quantity by discrete amount
/// </summary>
public static class QuantityRepeatExtensions {
    /// <summary>
    /// 将物理量按离散次数累加 / Repeat quantity by discrete amount
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    /// <param name="quantity">待累加物理量 / Quantity to repeat.</param>
    /// <param name="amount">累加次数 / Repeat amount.</param>
    /// <param name="arithmetic">物理量算术策略 / Quantity arithmetic strategy.</param>
    /// <returns>累加结果，失败时返回 null / Repeated quantity, or null on failure.</returns>
    public static Quantity<V>? Repeat<V>(this Quantity<V> quantity, UInt64 amount, IQuantityArithmetic<V> arithmetic)
        where V : struct {
        Quantity<V> result = arithmetic.Zero(quantity.Unit);
        UInt64 remaining = amount;
        while (remaining > UInt64.Zero) {
            Result<Quantity<V>, ErrorCode, Error<ErrorCode>> addResult = arithmetic.Add(result, quantity);
            if (addResult.IsFailed) {
                return null;
            }

            result = addResult.Value;
            remaining = remaining - UInt64.One;
        }
        return result;
    }
}
