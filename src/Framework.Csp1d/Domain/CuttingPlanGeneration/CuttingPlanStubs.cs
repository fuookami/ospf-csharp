#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration
{
    /// <summary>
    /// 切割方案存根，用于生成器的最小可编译实现 / Cutting plan stub for minimal compilable generator implementation.
    ///
    /// 后续将替换为完整的领域模型 / Will be replaced by the full domain model later.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    public sealed class CuttingPlanStub<V> : ICuttingPlanForCanonicalKey where V : struct
    {
        /// <summary>方案标识 / Plan identifier.</summary>
        public string Id { get; init; } = "";

        /// <summary>物料标识 / Material identifier.</summary>
        public string MaterialId { get; init; } = "";

        /// <summary>设备标识 / Machine identifier.</summary>
        public string? MachineId { get; init; }

        /// <summary>产能消耗键 / Capacity consumption key.</summary>
        public string? CapacityConsumptionKey { get; init; }

        /// <summary>切片列表 / Slice list.</summary>
        public IReadOnlyList<ICuttingPlanSliceForCanonicalKey> Slices { get; init; } = Array.Empty<ICuttingPlanSliceForCanonicalKey>();

        /// <summary>需求贡献列表 / Demand contribution list.</summary>
        public IReadOnlyList<ICuttingPlanDemandContributionForCanonicalKey> DemandContributions { get; init; } = Array.Empty<ICuttingPlanDemandContributionForCanonicalKey>();

        /// <summary>剩余宽度 / Rest width.</summary>
        public Quantity<V>? RestWidth { get; init; }
    }

    /// <summary>
    /// 切片存根 / Slice stub.
    /// </summary>
    public sealed class CuttingPlanSliceStubForCanonical : ICuttingPlanSliceForCanonicalKey
    {
        /// <inheritdoc/>
        public string ProductionType { get; init; } = "";
        /// <inheritdoc/>
        public string? ProductionId { get; init; }
        /// <inheritdoc/>
        public string WidthKey { get; init; } = "";
        /// <inheritdoc/>
        public UInt64 Amount { get; init; } = UInt64.One;
    }

    /// <summary>
    /// 需求贡献存根 / Demand contribution stub.
    /// </summary>
    public sealed class CuttingPlanDemandContributionStubForCanonical : ICuttingPlanDemandContributionForCanonicalKey
    {
        /// <inheritdoc/>
        public string ProductId { get; init; } = "";
        /// <inheritdoc/>
        public string UnitKey { get; init; } = "";
        /// <inheritdoc/>
        public string QuantityValue { get; init; } = "";
    }

    /// <summary>
    /// 生成输入存根 / Generation input stub.
    ///
    /// 后续将替换为完整的领域输入类型 / Will be replaced by the full domain input type later.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
    public sealed class GenerationInputStub<V> where V : struct
    {
        /// <summary>物料列表 / Material list.</summary>
        public IReadOnlyList<MaterialStub<V>> Materials { get; init; } = Array.Empty<MaterialStub<V>>();

        /// <summary>需求列表 / Demand list.</summary>
        public IReadOnlyList<DemandStub<V>> Demands { get; init; } = Array.Empty<DemandStub<V>>();
    }

    /// <summary>
    /// 物料存根 / Material stub.
    /// </summary>
    public sealed class MaterialStub<V> where V : struct
    {
        /// <summary>物料标识 / Material identifier.</summary>
        public string Id { get; init; } = "";
        /// <summary>宽度上界 / Width upper bound.</summary>
        public Quantity<V> WidthUpperBound { get; init; }
    }

    /// <summary>
    /// 需求存根 / Demand stub.
    /// </summary>
    public sealed class DemandStub<V> where V : struct
    {
        /// <summary>产品标识 / Product identifier.</summary>
        public string ProductId { get; init; } = "";
        /// <summary>产品宽度列表 / Product width list.</summary>
        public IReadOnlyList<Quantity<V>> ProductWidths { get; init; } = Array.Empty<Quantity<V>>();
        /// <summary>需求量 / Demand quantity.</summary>
        public Quantity<V> Quantity { get; init; }
    }
}
